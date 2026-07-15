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
// — la fusion greedy sur GPU est repoussée après le SVO). Suit le même schéma que
// Noise.cs : compute shader + readback asynchrone, file de requêtes séquentielle.
public static class VoxelMesherGpu
{
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

    private static ComputeShader shader;
    private static ComputeBuffer voxelBuffer;
    private static int voxelBufferCapacity;
    private static ComputeBuffer faceBuffer;
    private static ComputeBuffer countBuffer;
    private static bool busy;

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
        public readonly Action<Face[], int> OnComplete;

        public PendingRequest(uint[] paddedVoxels, int paddedLength, int innerWidth, int innerHeight, int innerDepth,
            int paddedWidth, int paddedHeightStride, int loY, Action<Face[], int> onComplete)
        {
            PaddedVoxels = paddedVoxels;
            PaddedLength = paddedLength;
            InnerWidth = innerWidth;
            InnerHeight = innerHeight;
            InnerDepth = innerDepth;
            PaddedWidth = paddedWidth;
            PaddedHeightStride = paddedHeightStride;
            LoY = loY;
            OnComplete = onComplete;
        }
    }

    private static readonly Queue<PendingRequest> pending = new Queue<PendingRequest>();

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

    private static void EnsureBuffers(int paddedLength)
    {
        if (voxelBuffer == null || voxelBufferCapacity < paddedLength)
        {
            voxelBuffer?.Release();
            voxelBufferCapacity = paddedLength;
            voxelBuffer = new ComputeBuffer(paddedLength, sizeof(uint));
        }

        if (faceBuffer == null)
        {
            faceBuffer = new ComputeBuffer(MaxFaces, sizeof(uint) * 4, ComputeBufferType.Append);
        }

        if (countBuffer == null)
        {
            countBuffer = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Raw);
        }
    }

    // paddedVoxels : buffer aplati (voir VoxelFaceCulling.compute pour la disposition
    // exacte), longueur paddedLength. onComplete est appelé avec la liste des faces
    // exposées (coordonnées locales au chunk) une fois le readback GPU terminé,
    // potentiellement plusieurs frames plus tard.
    public static void RequestFaces(uint[] paddedVoxels, int paddedLength, int innerWidth, int innerHeight,
        int innerDepth, int paddedWidth, int paddedHeightStride, int loY, Action<Face[], int> onComplete)
    {
        pending.Enqueue(new PendingRequest(paddedVoxels, paddedLength, innerWidth, innerHeight, innerDepth,
            paddedWidth, paddedHeightStride, loY, onComplete));
        TryDispatchNext();
    }

    private static void TryDispatchNext()
    {
        if (busy || pending.Count == 0)
        {
            return;
        }

        if (!EnsureShaderLoaded())
        {
            PendingRequest failed = pending.Dequeue();
            failed.OnComplete?.Invoke(Array.Empty<Face>(), 0);
            TryDispatchNext();
            return;
        }

        PendingRequest req = pending.Dequeue();
        EnsureBuffers(req.PaddedLength);

        voxelBuffer.SetData(req.PaddedVoxels, 0, 0, req.PaddedLength);
        faceBuffer.SetCounterValue(0);

        int kernel = shader.FindKernel("CullFaces");
        shader.SetBuffer(kernel, "Voxels", voxelBuffer);
        shader.SetBuffer(kernel, "ExposedFaces", faceBuffer);
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

        ComputeBuffer.CopyCount(faceBuffer, countBuffer, 0);

        busy = true;

        bool countReady = false;
        bool dataReady = false;
        uint faceCount = 0;
        NativeArray<uint4> rawFaces = default;
        Action<Face[], int> onCompleteCaptured = req.OnComplete;

        void Finish()
        {
            busy = false;

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

        AsyncGPUReadback.Request(countBuffer, request =>
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

        AsyncGPUReadback.Request(faceBuffer, request =>
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
        voxelBuffer?.Release();
        voxelBuffer = null;
        voxelBufferCapacity = 0;
        faceBuffer?.Release();
        faceBuffer = null;
        countBuffer?.Release();
        countBuffer = null;
        pending.Clear();
        busy = false;
        shader = null;
    }
}
