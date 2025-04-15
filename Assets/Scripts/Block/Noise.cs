using UnityEngine;

public static class Noise
{
    private static ComputeShader heightmapShader;
    private static RenderTexture heightmapTexture;
    private static int currentMapSize;
    
    public static int Seed { get; private set; }
    
    // Paramètres de climat
    public static float TemperatureScale { get; set; } = 100f;
    public static float HumidityScale { get; set; } = 100f;

    public static void Initialize(int seed)
    {
        Seed = seed;
        Random.InitState(seed);
    }

    public static float GetTemperature(float x, float z)
    {
        float noise = Mathf.PerlinNoise(
            (x + Seed) / TemperatureScale, 
            (z + Seed) / TemperatureScale
        );
        return (noise * 2f) - 1f; // Convert to range -1 to 1
    }

    public static float GetHumidity(float x, float z)
    {
        float noise = Mathf.PerlinNoise(
            (x + Seed + 1000) / HumidityScale, 
            (z + Seed + 1000) / HumidityScale
        );
        return noise; // Range 0 to 1
    }
    
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

    private static void InitializeHeightmapResources(int size)
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

    public static float[,] GenerateHeightmap(int size, Vector2 offset)
    {
        InitializeHeightmapResources(size);

        if (heightmapShader == null || heightmapTexture == null)
        {
            Debug.LogError("Heightmap generation failed - shader or texture not initialized!");
            return new float[size, size];
        }

        // Configuration du shader
        int kernelIndex = heightmapShader.FindKernel("GenerateHeightmap");
        heightmapShader.SetTexture(kernelIndex, "HeightmapResult", heightmapTexture);
        heightmapShader.SetFloat("scale", Scale);
        heightmapShader.SetVector("offset", offset);
        heightmapShader.SetInt("octaves", Octaves);
        heightmapShader.SetFloat("persistence", Persistence);
        heightmapShader.SetFloat("lacunarity", Lacunarity);
        heightmapShader.SetInt("mapSize", size);

        // Dispatch
        int threadGroups = Mathf.CeilToInt(size / 8.0f);
        heightmapShader.Dispatch(kernelIndex, threadGroups, threadGroups, 1);

        // Lecture des résultats
        float[,] heightmap = new float[size, size];
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

        Object.DestroyImmediate(temp);
        return heightmap;
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
    }
}
