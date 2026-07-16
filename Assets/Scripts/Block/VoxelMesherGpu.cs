using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// Culling de faces sur GPU (roadmap phase 4 suite) : un thread par voxel, teste ses 6
// faces et empile celles exposées dans un AppendStructuredBuffer — remplace le calcul de
// masque CPU pour la détection des faces visibles. La construction du mesh/UV reste côté
// CPU (Chunk.AddGreedyFace, réutilisée telle quelle avec largeur=hauteur=1 : cette
// première version ne fusionne pas les faces adjacentes, cf. décision avec l'utilisateur
// — la fusion greedy sur GPU est repoussée après le SVO). Suit le même schéma de pool que
// Noise.cs, mais avec en plus une file priorisée par distance : contrairement au bruit (les
// requêtes arrivent déjà groupées/triées par ChunkStreamingBridgeSystem), un remaillage peut
// être déclenché à tout moment par une édition de voxel (SetVoxel) ou, plus tard, par la
// simulation de fluides — sans priorité, une édition proche du joueur attendrait derrière
// tout un lot de premiers maillages de chunks en cours de chargement (constaté en jeu).
public static class VoxelMesherGpu
{
    // Priorité de traitement d'une requête : le tier (catégorie) domine toujours, la distance ne
    // départage qu'à tier égal. Décidé avec l'utilisateur après un bug observé en jeu : une
    // téléportation laisse dans la file des remaillages LOD obsolètes dont la distance a été
    // figée AVANT le saut (donc encore "proche" par erreur) — sans hiérarchie de catégorie, ces
    // requêtes périmées pouvaient passer devant le premier maillage des chunks nouvellement
    // chargés, dont la géométrie est bien plus urgente (un chunk absent = un trou visible, un
    // chunk au mauvais LOD = juste moins de détail). Voir Chunk.RequestRemesh pour l'attribution
    // du tier côté appelant.
    public readonly struct RequestPriority : IComparable<RequestPriority>
    {
        public readonly int Tier; // plus petit = plus prioritaire
        public readonly float DistanceSq;

        public RequestPriority(int tier, float distanceSq)
        {
            Tier = tier;
            DistanceSq = distanceSq;
        }

        public int CompareTo(RequestPriority other)
        {
            int tierCompare = Tier.CompareTo(other.Tier);
            return tierCompare != 0 ? tierCompare : DistanceSq.CompareTo(other.DistanceSq);
        }
    }

    public readonly struct Face
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;
        public readonly int Direction; // 0..5, cf. VoxelFaceCulling.compute
        public readonly VoxelType Type;

        public Face(int x, int y, int z, int direction, VoxelType type)
        {
            X = x;
            Y = y;
            Z = z;
            Direction = direction;
            Type = type;
        }
    }

    // Capacité généreuse pour un terrain réaliste (pas le pire cas théorique en
    // damier) ; un avertissement signale si elle est dépassée.
    private const int MaxFaces = 131072;

    // Nombre de dispatchs GPU (voxels+faces+readback) en vol simultanément. Plus élevé que 1
    // (ancienne version strictement séquentielle) pour absorber le débit nécessaire une fois la
    // simulation de fluides en place (remaillages continus sur plusieurs chunks à la fois, pas
    // seulement au chargement) ; volontairement plus bas que le pool de 16 textures de Noise.cs
    // — chaque slot réserve ici un ExposedFaces de ~2 Mo (MaxFaces × 16 octets), donc 16 slots
    // représenterait ~32 Mo rien que pour ce buffer. À réajuster si le profilage le justifie.
    private const int MaxConcurrentRequests = 4;

    private static ComputeShader shader;

    private struct Slot
    {
        public ComputeBuffer VoxelBuffer;
        public int VoxelBufferCapacity;
        public ComputeBuffer FaceBuffer;
        public ComputeBuffer CountBuffer;
        public bool Busy;
    }

    private static Slot[] slots;
    private static int busyCount;

    private readonly struct PendingRequest
    {
        public readonly uint[] PaddedVoxels;
        public readonly int PaddedLength;
        public readonly int InnerWidth;
        public readonly int InnerHeight;
        public readonly int InnerDepth;
        public readonly int PaddedWidth;
        public readonly int PaddedHeightStride;
        public readonly int LoY;
        public readonly RequestPriority Priority;
        public readonly Action<Face[], int> OnComplete;

        public PendingRequest(uint[] paddedVoxels, int paddedLength, int innerWidth, int innerHeight, int innerDepth,
            int paddedWidth, int paddedHeightStride, int loY, RequestPriority priority, Action<Face[], int> onComplete)
        {
            PaddedVoxels = paddedVoxels;
            PaddedLength = paddedLength;
            InnerWidth = innerWidth;
            InnerHeight = innerHeight;
            InnerDepth = innerDepth;
            PaddedWidth = paddedWidth;
            PaddedHeightStride = paddedHeightStride;
            LoY = loY;
            Priority = priority;
            OnComplete = onComplete;
        }
    }

    // Liste plutôt que Queue : la sélection de la prochaine requête à dispatcher se fait par
    // priorité (scan linéaire), pas par ordre d'arrivée — le nombre de requêtes en attente reste
    // modeste (quelques dizaines au pire, lors d'un chargement massif) donc un scan O(n) à
    // chaque libération de slot est largement suffisant, pas besoin d'un tas.
    private static readonly List<PendingRequest> pending = new List<PendingRequest>();

    private static bool EnsureShaderLoaded()
    {
        if (shader != null)
        {
            return true;
        }

        shader = Resources.Load<ComputeShader>("VoxelFaceCulling");
        if (shader == null)
        {
            Debug.LogError("Failed to load VoxelFaceCulling compute shader!");
            return false;
        }
        return true;
    }

    private static void EnsureSlots()
    {
        if (slots != null)
        {
            return;
        }

        slots = new Slot[MaxConcurrentRequests];
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].FaceBuffer = new ComputeBuffer(MaxFaces, sizeof(uint) * 4, ComputeBufferType.Append);
            slots[i].CountBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        }
    }

    private static void EnsureVoxelBuffer(int slotIndex, int paddedLength)
    {
        ref Slot slot = ref slots[slotIndex];
        if (slot.VoxelBuffer == null || slot.VoxelBufferCapacity < paddedLength)
        {
            slot.VoxelBuffer?.Release();
            slot.VoxelBufferCapacity = paddedLength;
            slot.VoxelBuffer = new ComputeBuffer(paddedLength, sizeof(uint));
        }
    }

    // paddedVoxels : buffer aplati (voir VoxelFaceCulling.compute pour la disposition exacte),
    // longueur paddedLength. priority : cf. RequestPriority — traité avant les autres requêtes
    // en attente dès qu'un slot se libère (tier d'abord, distance au joueur en départage — cf.
    // Chunk.RequestRemesh). onComplete est appelé avec la liste des faces exposées (coordonnées
    // locales au chunk) une fois le readback GPU terminé, potentiellement plusieurs frames plus
    // tard.
    public static void RequestFaces(uint[] paddedVoxels, int paddedLength, int innerWidth, int innerHeight,
        int innerDepth, int paddedWidth, int paddedHeightStride, int loY, RequestPriority priority, Action<Face[], int> onComplete)
    {
        pending.Add(new PendingRequest(paddedVoxels, paddedLength, innerWidth, innerHeight, innerDepth,
            paddedWidth, paddedHeightStride, loY, priority, onComplete));
        TryDispatchNext();
    }

    private static int FindFreeSlot()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i].Busy)
            {
                return i;
            }
        }
        return -1;
    }

    private static int FindHighestPriorityIndex()
    {
        int best = 0;
        for (int i = 1; i < pending.Count; i++)
        {
            if (pending[i].Priority.CompareTo(pending[best].Priority) < 0)
            {
                best = i;
            }
        }
        return best;
    }

    private static void TryDispatchNext()
    {
        EnsureSlots();

        while (busyCount < MaxConcurrentRequests && pending.Count > 0)
        {
            int slotIndex = FindFreeSlot();
            if (slotIndex < 0)
            {
                break; // ne devrait pas arriver tant que busyCount < MaxConcurrentRequests
            }

            int bestIndex = FindHighestPriorityIndex();
            PendingRequest req = pending[bestIndex];
            pending.RemoveAt(bestIndex);

            if (!EnsureShaderLoaded())
            {
                req.OnComplete?.Invoke(Array.Empty<Face>(), 0);
                continue;
            }

            DispatchSlot(slotIndex, req);
        }
    }

    private static void DispatchSlot(int slotIndex, PendingRequest req)
    {
        EnsureVoxelBuffer(slotIndex, req.PaddedLength);
        Slot slot = slots[slotIndex];

        slot.VoxelBuffer.SetData(req.PaddedVoxels, 0, 0, req.PaddedLength);
        slot.FaceBuffer.SetCounterValue(0);

        int kernel = shader.FindKernel("CullFaces");
        shader.SetBuffer(kernel, "Voxels", slot.VoxelBuffer);
        shader.SetBuffer(kernel, "ExposedFaces", slot.FaceBuffer);
        shader.SetInt("InnerWidth", req.InnerWidth);
        shader.SetInt("InnerHeight", req.InnerHeight);
        shader.SetInt("InnerDepth", req.InnerDepth);
        shader.SetInt("PaddedWidth", req.PaddedWidth);
        shader.SetInt("PaddedHeightStride", req.PaddedHeightStride);
        shader.SetInt("LoY", req.LoY);

        int groupsX = Mathf.CeilToInt(req.InnerWidth / 4.0f);
        int groupsY = Mathf.CeilToInt(req.InnerHeight / 4.0f);
        int groupsZ = Mathf.CeilToInt(req.InnerDepth / 4.0f);
        shader.Dispatch(kernel, Mathf.Max(1, groupsX), Mathf.Max(1, groupsY), Mathf.Max(1, groupsZ));

        ComputeBuffer.CopyCount(slot.FaceBuffer, slot.CountBuffer, 0);

        slots[slotIndex].Busy = true;
        busyCount++;

        bool countReady = false;
        bool dataReady = false;
        uint faceCount = 0;
        NativeArray<uint4> rawFaces = default;
        Action<Face[], int> onCompleteCaptured = req.OnComplete;
        ComputeBuffer faceBufferRef = slot.FaceBuffer;
        ComputeBuffer countBufferRef = slot.CountBuffer;

        void Finish()
        {
            slots[slotIndex].Busy = false;
            busyCount--;

            Face[] result = Array.Empty<Face>();
            int count = 0;
            if (rawFaces.IsCreated)
            {
                count = Mathf.Min((int)faceCount, rawFaces.Length);
                if ((int)faceCount >= MaxFaces)
                {
                    Debug.LogWarning($"VoxelMesherGpu: face buffer capacity ({MaxFaces}) reached, some faces may be missing.");
                }

                result = new Face[count];
                for (int i = 0; i < count; i++)
                {
                    uint4 f = rawFaces[i];
                    int direction = (int)(f.w & 0xFF);
                    VoxelType type = (VoxelType)(f.w >> 8);
                    result[i] = new Face((int)f.x, (int)f.y, (int)f.z, direction, type);
                }
            }

            onCompleteCaptured?.Invoke(result, count);
            TryDispatchNext();
        }

        AsyncGPUReadback.Request(countBufferRef, request =>
        {
            if (!request.hasError)
            {
                faceCount = request.GetData<uint>()[0];
            }
            countReady = true;
            if (dataReady)
            {
                Finish();
            }
        });

        AsyncGPUReadback.Request(faceBufferRef, request =>
        {
            if (!request.hasError)
            {
                rawFaces = request.GetData<uint4>();
            }
            dataReady = true;
            if (countReady)
            {
                Finish();
            }
        });
    }

    public static void Cleanup()
    {
        if (slots != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].VoxelBuffer?.Release();
                slots[i].FaceBuffer?.Release();
                slots[i].CountBuffer?.Release();
            }
            slots = null;
        }

        pending.Clear();
        busyCount = 0;
        shader = null;
    }
}
