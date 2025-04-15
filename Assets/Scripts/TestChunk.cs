using UnityEngine;

public class TestChunk : MonoBehaviour
{
    public Chunk chunk;
    public Material material;
    
    void Start()
    {
        chunk.Initialize(Vector3Int.zero, material);
        
        // Test de génération d'arbre
        Vector3Int treePosition = new Vector3Int(8, 0, 8); // Position au milieu du chunk
        if (TreeGenerator.CanGenerateTree(treePosition, chunk))
        {
            TreeGenerator.GenerateTree(treePosition, chunk);
        }
    }

    void Update()
    {
        chunk.UpdateChunk();
    }
}
