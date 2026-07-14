using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class Noise
{
    private static ComputeShader heightmapShader;
    private static RenderTexture heightmapTexture;
    private static int currentMapSize;

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

    private static void Initialize(int size)
    {
        // Charger le shader seulement s'il n'est pas déjà chargé
        if (heightmapShader == null)
        {
            heightmapShader = Resources.Load<ComputeShader>("HeightmapGenerator");
            if (heightmapShader == null)
            {
                Debug.LogError("Failed to load HeightmapGenerator compute shader!");
                return;
            }
        }

        // Créer ou recréer la texture si nécessaire
        if (heightmapTexture == null || currentMapSize != size)
        {
            ReleaseTexture();
            currentMapSize = size;
            heightmapTexture = new RenderTexture(size, size, 0, RenderTextureFormat.RFloat);
            heightmapTexture.enableRandomWrite = true;
            heightmapTexture.Create();
        }
    }

    // Configure les paramètres du compute shader et dispatch. Retourne false si le
    // shader/la texture ne sont pas disponibles (déjà loggé dans ce cas).
    private static bool PrepareAndDispatch(int size, Vector2 offset)
    {
        Initialize(size);

        if (heightmapShader == null || heightmapTexture == null)
        {
            Debug.LogError("Heightmap generation failed - shader or texture not initialized!");
            return false;
        }

        int kernelIndex = heightmapShader.FindKernel("GenerateHeightmap");
        heightmapShader.SetInt("worldSeed", World.Instance.worldSeed);
        heightmapShader.SetTexture(kernelIndex, "HeightmapResult", heightmapTexture);
        heightmapShader.SetFloat("scale", Scale);
        heightmapShader.SetVector("offset", offset);
        heightmapShader.SetInt("octaves", Octaves);
        heightmapShader.SetFloat("persistence", Persistence);
        heightmapShader.SetFloat("lacunarity", Lacunarity);
        heightmapShader.SetInt("mapSize", size);

        int threadGroups = Mathf.CeilToInt(size / 8.0f);
        heightmapShader.Dispatch(kernelIndex, threadGroups, threadGroups, 1);
        return true;
    }

    // Génération synchrone : réservée à l'aperçu de l'éditeur (NoiseSettingsEditor), où un
    // stall CPU/GPU ponctuel est acceptable (outil, pas gameplay). Le chemin gameplay
    // utilise RequestHeightmapAsync ci-dessous (roadmap phase 3).
    public static float[,] GenerateHeightmap(int size, Vector2 offset)
    {
        float[,] heightmap = new float[size, size];

        if (!PrepareAndDispatch(size, offset))
        {
            return heightmap;
        }

        RenderTexture.active = heightmapTexture;
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
    // heightmapTexture est une ressource GPU partagée entre toutes les requêtes : on ne
    // dispatch une nouvelle requête dedans qu'une fois le readback de la précédente
    // terminé, d'où la file FIFO traitée une requête à la fois. Élimine le stall
    // CPU/GPU de ReadPixels (le thread principal n'attend jamais le GPU).
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
    private static bool isProcessingRequest;

    public static void RequestHeightmapAsync(int size, Vector2 offset, Action<float[,]> onComplete)
    {
        pendingRequests.Enqueue(new PendingRequest(size, offset, onComplete));
        TryDispatchNext();
    }

    private static void TryDispatchNext()
    {
        if (isProcessingRequest || pendingRequests.Count == 0)
        {
            return;
        }

        PendingRequest next = pendingRequests.Peek();

        if (!PrepareAndDispatch(next.Size, next.Offset))
        {
            pendingRequests.Dequeue();
            next.OnComplete?.Invoke(new float[next.Size, next.Size]);
            TryDispatchNext();
            return;
        }

        isProcessingRequest = true;
        int size = next.Size;
        AsyncGPUReadback.Request(heightmapTexture, 0, request => OnReadbackComplete(request, size));
    }

    private static void OnReadbackComplete(AsyncGPUReadbackRequest request, int size)
    {
        PendingRequest completed = pendingRequests.Dequeue();
        isProcessingRequest = false;

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

        completed.OnComplete?.Invoke(heightmap);
        TryDispatchNext();
    }

    private static void ReleaseTexture()
    {
        if (heightmapTexture != null)
        {
            heightmapTexture.Release();
            heightmapTexture = null;
        }
    }

    // Méthode à appeler lors de la fermeture du jeu ou du changement de scène
    public static void Cleanup()
    {
        ReleaseTexture();
        heightmapShader = null;
        pendingRequests.Clear();
        isProcessingRequest = false;
    }
}