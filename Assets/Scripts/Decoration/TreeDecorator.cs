using UnityEngine;

public class TreeDecorator : TerrainDecorator
{
    private const int MIN_HEIGHT = 4;
    private const int MAX_HEIGHT = 7;
    private const int MIN_CROWN_RADIUS = 2;
    private const int MAX_CROWN_RADIUS = 3;
    private const float LEAF_DENSITY = 0.7f;

    public TreeDecorator(float spawnChance = 0.5f) : base(spawnChance)
    {
        Debug.Log($"TreeDecorator initialized with spawn chance: {spawnChance}");
    }

    public override bool CanPlace(Vector3Int position, Chunk chunk)
    {
        // Position du bloc en dessous
        Vector3Int posBelow = new Vector3Int(position.x, position.y - 1, position.z);
        var blockBelow = World.Instance.GetVoxel(posBelow);
        
        Debug.Log($"Checking tree placement at {position}, block below ({posBelow}): {blockBelow.type}");
        
        if (blockBelow.type != VoxelType.Grass)
        {
            Debug.Log($"Cannot place tree at {position} - Block below is {blockBelow.type}");
            return false;
        }

        // Vérifier l'espace pour le tronc et la couronne
        int maxHeight = MAX_HEIGHT + 3;
        bool hasSpace = true;
        for (int y = 0; y < maxHeight; y++)
        {
            Vector3Int checkPos = new Vector3Int(position.x, position.y + y, position.z);
            if (World.Instance.GetVoxel(checkPos).type != VoxelType.Air)
            {
                Debug.Log($"Cannot place tree at {position} - Blocked at height {y} at {checkPos}");
                hasSpace = false;
                break;
            }
        }

        if (hasSpace)
        {
            Debug.Log($"Tree placement approved at {position}");
            return true;
        }
        
        return false;
    }

    public override void Place(Vector3Int position, Chunk chunk)
    {
        Debug.Log($"Starting tree placement at {position}");
        int height = UnityEngine.Random.Range(MIN_HEIGHT, MAX_HEIGHT + 1);
        
        // Générer le tronc
        for (int y = 0; y < height; y++)
        {
            World.Instance.SetVoxel(new Vector3Int(position.x, position.y + y, position.z), VoxelType.Wood);
        }

        int crownRadius = UnityEngine.Random.Range(MIN_CROWN_RADIUS, MAX_CROWN_RADIUS + 1);
        int crownHeight = height - 2;

        // Générer la couronne de feuilles
        for (int y = 0; y < 3; y++)
        {
            for (int x = -crownRadius; x <= crownRadius; x++)
            {
                for (int z = -crownRadius; z <= crownRadius; z++)
                {
                    float distance = Mathf.Sqrt(x * x + z * z);
                    if (distance <= crownRadius && UnityEngine.Random.value < LEAF_DENSITY)
                    {
                        Vector3Int leafPos = new Vector3Int(
                            position.x + x,
                            position.y + crownHeight + y,
                            position.z + z
                        );
                        World.Instance.SetVoxel(leafPos, VoxelType.Leaves);
                    }
                }
            }
        }
        Debug.Log($"Finished placing tree at {position}");
    }
}

