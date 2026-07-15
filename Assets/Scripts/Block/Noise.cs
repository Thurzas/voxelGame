using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class Noise
{
    private static ComputeShader heightmapShader;

    public static NoiseSettings CurrentSettings { get; private set; }

    public static void ApplySettings(NoiseSettings settings)
    {
        CurrentSettings = settings;
        settings.ApplyToNoise();
    }

    // Paramètres FBM
    public static float Scale { get; set; } = 50f;
    public static int Octaves { get; set; } = 6;
    public static float Persistence { get; set; } = 0.5f;
    public static float Lacunarity { get; set; } = 2.0f;

    private static bool EnsureShaderLoaded()
    {
        if (heightmapShader != null)
        {
            return true;
        }

        heightmapShader = Resources.Load<ComputeShader>("HeightmapGenerator");
        if (heightmapShader == null)
        {
            Debug.LogError("Failed to load HeightmapGenerator compute shader!");
            return false;
        }

        return true;
    }

    private static void DispatchInto(RenderTexture target, int size, Vector2 offset)
    {
        int kernelIndex = heightmapShader.FindKernel("GenerateHeightmap");
        heightmapShader.SetInt("worldSeed", World.Instance.worldSeed);
        heightmapShader.SetTexture(kernelIndex, "HeightmapResult", target);
        heightmapShader.SetFloat("scale", Scale);
        heightmapShader.SetVector("offset", offset);
        heightmapShader.SetInt("octaves", Octaves);
        heightmapShader.SetFloat("persistence", Persistence);
        heightmapShader.SetFloat("lacunarity", Lacunarity);
        heightmapShader.SetInt("mapSize", size);

        int threadGroups = Mathf.CeilToInt(size / 8.0f);
        heightmapShader.Dispatch(kernelIndex, threadGroups, threadGroups, 1);
    }

    // --- Génération synchrone : réservée à l'aperçu de l'éditeur (NoiseSettingsEditor) ---
    // Utilise sa propre texture, complètement séparée du pool async ci-dessous : les deux
    // chemins ne doivent jamais partager une texture GPU (l'un pourrait écraser les
    // données de l'autre en plein readback).
    private static RenderTexture previewTexture;
    private static int previewTextureSize = -1;

    public static float[,] GenerateHeightmap(int size, Vector2 offset)
    {
        float[,] heightmap = new float[size, size];

        if (!EnsureShaderLoaded())
        {
            return heightmap;
        }

        if (previewTexture == null || previewTextureSize != size)
        {
            if (previewTexture != null)
            {
                previewTexture.Release();
            }

            previewTextureSize = size;
            previewTexture = new RenderTexture(size, size, 0, RenderTextureFormat.RFloat);
            previewTexture.enableRandomWrite = true;
            previewTexture.Create();
        }

        DispatchInto(previewTexture, size, offset);

        RenderTexture.active = previewTexture;
        Texture2D temp = new Texture2D(size, size, TextureFormat.RFloat, false);
        temp.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        temp.Apply();

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                heightmap[x, y] = temp.GetPixel(x, y).r;
            }
        }

        UnityEngine.Object.DestroyImmediate(temp);
        return heightmap;
    }

    // --- Génération asynchrone (gameplay, roadmap phase 3) ---
    // Pool de textures réutilisables permettant plusieurs requêtes GPU en vol en même
    // temps (au lieu d'une seule à la fois) : élimine à la fois le stall CPU/GPU
    // (AsyncGPUReadback) ET le goulot d'étranglement d'un traitement strictement
    // séquentiel (insuffisant dès qu'un renderDistance raisonnable demande des dizaines
    // de chunks d'un coup - constaté en jeu : chargement trop lent, joueur tombant à
    // travers le monde). Chaque requête en vol porte son propre état dans la closure du
    // callback (texture, taille, callback utilisateur) : pas de file partagée à
    // dépiler en retour d'un événement asynchrone, donc pas de risque de dépiler une
    // file vide si l'ordre de complétion des requêtes diffère de l'ordre de lancement.
    private const int MaxConcurrentRequests = 16;

    private readonly struct PendingRequest
    {
        public readonly int Size;
        public readonly Vector2 Offset;
        public readonly Action<float[,]> OnComplete;

        public PendingRequest(int size, Vector2 offset, Action<float[,]> onComplete)
        {
            Size = size;
            Offset = offset;
            OnComplete = onComplete;
        }
    }

    private static readonly Queue<PendingRequest> pendingRequests = new Queue<PendingRequest>();
    private static readonly Queue<RenderTexture> freeTextures = new Queue<RenderTexture>();
    private static int pooledTextureSize = -1;
    private static int inFlightCount;

    public static void RequestHeightmapAsync(int size, Vector2 offset, Action<float[,]> onComplete)
    {
        pendingRequests.Enqueue(new PendingRequest(size, offset, onComplete));
        TryDispatchNext();
    }

    private static void TryDispatchNext()
    {
        while (inFlightCount < MaxConcurrentRequests && pendingRequests.Count > 0)
        {
            PendingRequest next = pendingRequests.Dequeue();

            if (!EnsureShaderLoaded())
            {
                next.OnComplete?.Invoke(new float[next.Size, next.Size]);
                continue;
            }

            RenderTexture tex = RentTexture(next.Size);
            DispatchInto(tex, next.Size, next.Offset);

            inFlightCount++;
            int size = next.Size;
            Action<float[,]> onComplete = next.OnComplete;
            AsyncGPUReadback.Request(tex, 0, request => OnReadbackComplete(request, size, tex, onComplete));
        }
    }

    private static void OnReadbackComplete(AsyncGPUReadbackRequest request, int size, RenderTexture tex, Action<float[,]> onComplete)
    {
        inFlightCount--;
        ReturnTexture(tex);

        float[,] heightmap = new float[size, size];
        if (request.hasError)
        {
            Debug.LogError("Heightmap AsyncGPUReadback a échoué.");
        }
        else
        {
            var data = request.GetData<float>();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    heightmap[x, y] = data[(y * size) + x];
                }
            }
        }

        // Ne PAS invoquer onComplete tout de suite : le readback GPU est terminé, mais le
        // travail qui suit côté appelant (remplissage des voxels, décoration, greedy
        // meshing, bake du MeshCollider) est lourd et synchrone. Si plusieurs readbacks
        // se terminent la même frame (typique juste après un déplacement de chunk, avec
        // jusqu'à MaxConcurrentRequests requêtes en vol), les invoquer tous immédiatement
        // concentre tout ce travail sur une seule frame -> freeze. On les met en attente
        // et un appelant (World.Update) les délivre à un rythme étalé via
        // DeliverReadyResults. Constaté en jeu (freeze au démarrage + à chaque
        // déplacement) avant ce correctif.
        readyResults.Enqueue((heightmap, onComplete));
        TryDispatchNext();
    }

    private static readonly Queue<(float[,] heightmap, Action<float[,]> onComplete)> readyResults = new Queue<(float[,], Action<float[,]>)>();

    // À appeler une fois par frame (World.Update) : délivre des résultats déjà prêts en
    // respectant un budget de temps, pour étaler le travail de finalisation des chunks
    // sur plusieurs frames plutôt que tout faire d'un coup. Délivre toujours au moins un
    // résultat s'il y en a (pas de famine si maxMilliseconds est trop petit).
    public static void DeliverReadyResults(float maxMilliseconds)
    {
        if (readyResults.Count == 0)
        {
            return;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            var (heightmap, onComplete) = readyResults.Dequeue();
            onComplete?.Invoke(heightmap);
        }
        while (readyResults.Count > 0 && stopwatch.Elapsed.TotalMilliseconds < maxMilliseconds);
    }

    private static RenderTexture RentTexture(int size)
    {
        if (pooledTextureSize != size)
        {
            // Ne devrait pas arriver en jeu (toujours Width) ; sécurité si la taille change.
            while (freeTextures.Count > 0)
            {
                freeTextures.Dequeue().Release();
            }
            pooledTextureSize = size;
        }

        if (freeTextures.Count > 0)
        {
            return freeTextures.Dequeue();
        }

        var tex = new RenderTexture(size, size, 0, RenderTextureFormat.RFloat);
        tex.enableRandomWrite = true;
        tex.Create();
        return tex;
    }

    private static void ReturnTexture(RenderTexture tex)
    {
        freeTextures.Enqueue(tex);
    }

    // Méthode à appeler lors de la fermeture du jeu ou du changement de scène. Les
    // requêtes déjà en vol (AsyncGPUReadback) ne peuvent pas être annulées : leur
    // callback s'exécutera quand même plus tard et gérera son propre nettoyage (texture
    // remise dans un pool qui ne sera simplement plus utilisé).
    public static void Cleanup()
    {
        if (previewTexture != null)
        {
            previewTexture.Release();
            previewTexture = null;
        }

        while (freeTextures.Count > 0)
        {
            freeTextures.Dequeue().Release();
        }

        pendingRequests.Clear();
        readyResults.Clear();
        heightmapShader = null;
    }
}