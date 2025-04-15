using UnityEngine;

[CreateAssetMenu(fileName = "BiomeSettings", menuName = "Terrain/Biome Settings")]
public class BiomeSettings : ScriptableObject
{
    public string biomeName;
    public NoiseSettings terrainSettings;
    
    [Header("Biome Thresholds")]
    [Range(-1f, 1f)]
    public float minTemperature = -1f;
    [Range(-1f, 1f)]
    public float maxTemperature = 1f;
    [Range(0f, 1f)]
    public float minHumidity = 0f;
    [Range(0f, 1f)]
    public float maxHumidity = 1f;

    [System.Serializable]
    public struct BiomeBlock
    {
        public int blockType;      // Type du bloc (correspondant à VoxelType)
        [Range(0f, 1f)]
        public float probability;   // Probabilité d'apparition
        [Range(0, 256)]
        public int minHeight;      // Hauteur minimum d'apparition
        [Range(0, 256)]
        public int maxHeight;      // Hauteur maximum d'apparition
    }

    [Header("Biome Blocks")]
    public BiomeBlock surfaceBlock;    // Bloc de surface principal (ex: sable pour le désert)
    public BiomeBlock subSurfaceBlock; // Bloc sous la surface (ex: grès pour le désert)
    public BiomeBlock stoneBlock;      // Bloc de pierre remplaçant (ex: grès compact pour le désert)
    public BiomeBlock[] decorationBlocks; // Blocs de décoration (ex: cactus, buissons morts, etc.)

    [Header("Generation Parameters")]
    [Range(0f, 1f)]
    public float decorationDensity = 0.1f; // Densité des blocs de décoration

    public bool IsInBiome(float temperature, float humidity)
    {
        return temperature >= minTemperature && 
               temperature <= maxTemperature && 
               humidity >= minHumidity && 
               humidity <= maxHumidity;
    }

    public int GetBlockTypeAtHeight(int height, int maxHeight, float noise)
    {
        // Bloc de base (pierre ou son équivalent)
        if (height < maxHeight - 4) 
        {
            return stoneBlock.blockType;
        }
        // Couche intermédiaire
        else if (height < maxHeight - 1)
        {
            return subSurfaceBlock.blockType;
        }
        // Surface
        else
        {
            return surfaceBlock.blockType;
        }
    }

    public bool ShouldPlaceDecoration(float x, float y, float z)
    {
        // Utiliser une fonction de bruit pour déterminer si on place une décoration
        float decorationNoise = Mathf.PerlinNoise(x * 0.5f, z * 0.5f);
        return decorationNoise < decorationDensity;
    }

    public int GetRandomDecorationBlock()
    {
        if (decorationBlocks == null || decorationBlocks.Length == 0)
            return 0;

        float total = 0;
        foreach (var block in decorationBlocks)
        {
            total += block.probability;
        }

        float random = Random.Range(0f, total);
        float current = 0;

        foreach (var block in decorationBlocks)
        {
            current += block.probability;
            if (random <= current)
                return block.blockType;
        }

        return decorationBlocks[0].blockType;
    }
}
