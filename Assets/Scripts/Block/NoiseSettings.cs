using UnityEngine;

[CreateAssetMenu(fileName = "NoiseSettings", menuName = "Terrain/Noise Settings")]
public class NoiseSettings : ScriptableObject
{
    [Header("Terrain Height")]
    public float baseHeight = 64f;
    public float heightMultiplier = 64f;

    [Header("Noise Parameters")]
    [Range(1f, 400f)]
    public float scale = 50f;
    [Range(1, 8)]
    public int octaves = 6;
    [Range(0.1f, 1f)]
    public float persistence = 0.5f;
    [Range(1f, 4f)]
    public float lacunarity = 2.0f;

    public void ApplyToNoise()
    {
        // Scale inversé pour fonctionner comme un zoom :
        // scale grand = zoom out = motif plus large = coordonnées plus petites
        Noise.Scale = 1f/scale;
        Noise.Octaves = octaves;
        Noise.Persistence = persistence;
        Noise.Lacunarity = lacunarity;
    }
}

