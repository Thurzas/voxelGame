using UnityEngine;

public static class TreeGenerator
{
    private const int MIN_HEIGHT = 4;
    private const int MAX_HEIGHT = 7;
    private const int MIN_CROWN_RADIUS = 2;
    private const int MAX_CROWN_RADIUS = 3;
    private const float LEAF_DENSITY = 0.7f;

    public static void GenerateTree(Vector3Int worldPos, Chunk chunk)
    {
        Vector3Int localPos = chunk.GetLocalPosition(worldPos);
        int height = Random.Range(MIN_HEIGHT, MAX_HEIGHT + 1);
        
        // Générer le tronc
        for (int y = 0; y < height; y++)
        {
            SetVoxelInWorld(new Vector3Int(worldPos.x, worldPos.y + y, worldPos.z), VoxelType.Wood);
        }

        int crownRadius = Random.Range(MIN_CROWN_RADIUS, MAX_CROWN_RADIUS + 1);
        int crownHeight = height - 2;

        // Générer la couronne de feuilles
        for (int y = 0; y < 3; y++)
        {
            for (int x = -crownRadius; x <= crownRadius; x++)
            {
                for (int z = -crownRadius; z <= crownRadius; z++)
                {
                    float distance = Mathf.Sqrt(x * x + z * z);
                    if (distance <= crownRadius && Random.value < LEAF_DENSITY)
                    {
                        if (x != 0 || z != 0)
                        {
                            Vector3Int leafWorldPos = new Vector3Int(
                                worldPos.x + x,
                                worldPos.y + crownHeight + y,
                                worldPos.z + z
                            );
                            SetVoxelInWorld(leafWorldPos, VoxelType.Leaves);
                        }
                    }
                }
            }
        }
    }

    // Nouvelle méthode helper pour placer des voxels en utilisant des coordonnées mondiales
    private static void SetVoxelInWorld(Vector3Int worldPos, VoxelType type)
    {
        // Utiliser World pour placer le voxel, il s'occupera de trouver le bon chunk
        World.Instance.SetVoxel(worldPos, type);
    }

    public static bool CanGenerateTree(Vector3Int worldPos, Chunk chunk)
    {
        // Vérifier le bloc en dessous
        Vector3Int posBelow = new Vector3Int(worldPos.x, worldPos.y - 1, worldPos.z);
        var blockBelow = World.Instance.GetVoxel(posBelow);
        if (blockBelow.type != VoxelType.Grass)
            return false;

        // Vérifier l'espace pour le tronc et la couronne
        int maxRadius = MAX_CROWN_RADIUS;
        int maxHeight = MAX_HEIGHT + 3;

        // Vérifier l'espace pour le tronc
        for (int y = 0; y < maxHeight; y++)
        {
            if (World.Instance.GetVoxel(new Vector3Int(worldPos.x, worldPos.y + y, worldPos.z)).type != VoxelType.Air)
                return false;
        }

        // Vérifier l'espace pour la couronne
        for (int x = -maxRadius; x <= maxRadius; x++)
        {
            for (int z = -maxRadius; z <= maxRadius; z++)
            {
                for (int y = maxHeight - 4; y < maxHeight - 1; y++)
                {
                    Vector3Int checkPos = new Vector3Int(
                        worldPos.x + x,
                        worldPos.y + y,
                        worldPos.z + z
                    );
                    if (World.Instance.GetVoxel(checkPos).type != VoxelType.Air)
                        return false;
                }
            }
        }

        return true;
    }
}




