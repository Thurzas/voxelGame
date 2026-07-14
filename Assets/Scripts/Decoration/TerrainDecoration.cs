using UnityEngine;
using System.Collections.Generic;

public class TerrainDecoration
{
    private const float TREES_PER_CHUNK_DENSITY = 0.01f;
    private const int MIN_HEIGHT = 4;
    private const int MAX_HEIGHT = 7;
    private const int MIN_CROWN_RADIUS = 2;
    private const int MAX_CROWN_RADIUS = 3;
    private const float LEAF_DENSITY = 0.7f;
    private System.Random random;

    public TerrainDecoration()
    {
        random = new System.Random();
    }

    public void DecorateChunk(Chunk chunk)
    {
        int chunkArea = Chunk.Width * Chunk.Depth;
        int treesToPlace = Mathf.FloorToInt(chunkArea * TREES_PER_CHUNK_DENSITY);

        for (int i = 0; i < treesToPlace; i++)
        {
            int localX = random.Next(0, Chunk.Width);
            int localZ = random.Next(0, Chunk.Depth);

            for (int y = Chunk.Height - 1; y >= 0; y--)
            {
                var voxel = chunk.GetVoxel(localX, y, localZ);
                if (voxel.type == VoxelType.Grass)
                {
                    // Génération de l'arbre en utilisant les coordonnées locales
                    int treeBaseY = y + 1;

                    // Tronc
                    int height = UnityEngine.Random.Range(MIN_HEIGHT, MAX_HEIGHT + 1);
                    for (int treeY = 0; treeY < height; treeY++)
                    {
                        chunk.SetVoxel(localX, treeBaseY + treeY, localZ, VoxelType.Wood);
                    }

                    // Couronne de feuilles
                    int crownRadius = UnityEngine.Random.Range(MIN_CROWN_RADIUS, MAX_CROWN_RADIUS + 1);
                    int crownBaseHeight = treeBaseY + height - 2;

                    for (int leafY = 0; leafY < 3; leafY++)
                    {
                        for (int leafX = -crownRadius; leafX <= crownRadius; leafX++)
                        {
                            for (int leafZ = -crownRadius; leafZ <= crownRadius; leafZ++)
                            {
                                int finalX = localX + leafX;
                                int finalZ = localZ + leafZ;

                                // Vérifier si nous sommes toujours dans les limites du chunk
                                if (!Chunk.IsVoxelInChunk(finalX, crownBaseHeight + leafY, finalZ))
                                    continue;

                                if (leafX == 0 && leafZ == 0) continue;

                                float distance = Mathf.Sqrt(leafX * leafX + leafZ * leafZ);
                                if (distance <= crownRadius && UnityEngine.Random.value < LEAF_DENSITY)
                                {
                                    chunk.SetVoxel(finalX, crownBaseHeight + leafY, finalZ, VoxelType.Leaves);
                                }
                            }
                        }
                    }

                    break;
                }
            }
        }
    }
}