using UnityEngine;

public class TestChunk : MonoBehaviour
{
    public Chunk chunk;
    public Material material;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        chunk.Initialize(Vector3Int.zero, material); 
    }

    void Update()
    {
        chunk.UpdateChunk();
    }
}
