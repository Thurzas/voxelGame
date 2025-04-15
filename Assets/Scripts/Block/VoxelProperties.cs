using UnityEngine;

[CreateAssetMenu(fileName = "VoxelProperties", menuName = "Voxel/Properties")]
public class VoxelProperties : ScriptableObject
{
    [System.Serializable]
    public class VoxelTypeProperties
    {
        public VoxelType type;
        public string displayName;
        public bool isTransparent;
        public bool isLiquid;
        
        // Coordonnées UV dans l'atlas de texture
        public Vector2Int topTextureCoords;    // Pour le dessus du bloc
        public Vector2Int sideTextureCoords;   // Pour les côtés
        public Vector2Int bottomTextureCoords; // Pour le dessous
    }

    public VoxelTypeProperties[] voxelTypes;
    private static VoxelProperties instance;

    public static VoxelProperties Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<VoxelProperties>("VoxelProperties");
            return instance;
        }
    }

    public VoxelTypeProperties GetPropertiesForType(VoxelType type)
    {
        foreach (var props in voxelTypes)
        {
            if (props.type == type)
                return props;
        }
        Debug.LogWarning($"No properties found for voxel type {type}, returning Air properties");
        return voxelTypes[0]; // Assuming Air is always at index 0
    }
}