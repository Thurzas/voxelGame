using UnityEngine;

public abstract class TerrainDecorator
{
    public float spawnChance { get; protected set; }  // Changé en public avec setter protected
    
    public TerrainDecorator(float spawnChance)
    {
        this.spawnChance = spawnChance;
    }

    public abstract bool CanPlace(Vector3Int position, Chunk chunk);
    public abstract void Place(Vector3Int position, Chunk chunk);
}
