using Unity.Entities;
using Unity.Mathematics;

namespace VoxelGame.Streaming
{
    /// <summary>
    /// Identifies a chunk entity by the Morton code of its column (x, 0, z) plus an
    /// octree level. Level is fixed at 0 during the phase-2 bridge (chunks are still
    /// full-height columns, mirroring the legacy World.cs); it becomes meaningful once
    /// real octree subdivision lands (roadmap phases 4-5).
    /// </summary>
    public struct ChunkMortonKey : IComponentData
    {
        public ulong Morton;
        public byte Level;
    }

    public enum ChunkLifecycleState : byte
    {
        Requested, // entity exists, legacy Chunk GameObject not instantiated yet
        Loaded,    // legacy Chunk GameObject instantiated and tracked in World.activeChunks
        Unloading, // out of range; pending destruction of both the entity and the GameObject
    }

    public struct ChunkLifecycle : IComponentData
    {
        public ChunkLifecycleState State;
    }

    /// <summary>
    /// Singleton written once per frame by the legacy <c>World</c> MonoBehaviour
    /// (roadmap phase 2 bridge) until player movement itself is ECS-driven. Read by
    /// <see cref="ChunkStreamingRequestSystem"/> to decide which chunk entities should
    /// exist.
    /// </summary>
    public struct ChunkStreamingConfig : IComponentData
    {
        public int2 PlayerChunkCoord;
        public int RenderDistance;
    }
}