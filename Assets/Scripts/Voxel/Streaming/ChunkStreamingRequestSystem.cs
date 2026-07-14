using Unity.Collections;
using Unity.Entities;
using VoxelGame.Data;

namespace VoxelGame.Streaming
{
    /// <summary>
    /// Decides which chunk columns should exist given the current player position and
    /// render distance (singleton <see cref="ChunkStreamingConfig"/>, written by the
    /// legacy World MonoBehaviour bridge). Creates entities for newly-needed chunks
    /// (state Requested) and marks out-of-range loaded chunks for eviction (state
    /// Unloading). Replaces World.cs's UpdateChunksAroundPlayer/ProcessChunkUpdates
    /// scan (roadmap phase 2). Actual GameObject instantiation/destruction is handled
    /// by <see cref="ChunkStreamingBridgeSystem"/>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ChunkStreamingRequestSystem : SystemBase
    {
        private int2Key lastPlayerChunkCoord;
        private bool hasRun;

        protected override void OnCreate()
        {
            if (!SystemAPI.HasSingleton<ChunkStreamingConfig>())
            {
                var entity = EntityManager.CreateEntity(typeof(ChunkStreamingConfig));
                EntityManager.SetComponentData(entity, new ChunkStreamingConfig
                {
                    PlayerChunkCoord = new Unity.Mathematics.int2(int.MinValue, int.MinValue),
                    RenderDistance = 0,
                });
            }
        }

        protected override void OnUpdate()
        {
            var config = SystemAPI.GetSingleton<ChunkStreamingConfig>();
            var currentCoord = new int2Key(config.PlayerChunkCoord.x, config.PlayerChunkCoord.y);

            if (hasRun && currentCoord.Equals(lastPlayerChunkCoord))
            {
                return;
            }

            hasRun = true;
            lastPlayerChunkCoord = currentCoord;

            var existing = new NativeParallelHashMap<ulong, Entity>(256, Allocator.Temp);
            foreach (var (key, entity) in SystemAPI.Query<RefRO<ChunkMortonKey>>().WithEntityAccess())
            {
                existing.TryAdd(key.ValueRO.Morton, entity);
            }

            int radius = config.RenderDistance;
            var wanted = new NativeParallelHashSet<ulong>((existing.Count() + 64), Allocator.Temp);

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int x = config.PlayerChunkCoord.x + dx;
                    int z = config.PlayerChunkCoord.y + dz;
                    ulong morton = MortonCode.Encode(x, 0, z);
                    wanted.Add(morton);

                    if (!existing.ContainsKey(morton))
                    {
                        var entity = EntityManager.CreateEntity(typeof(ChunkMortonKey), typeof(ChunkLifecycle));
                        EntityManager.SetComponentData(entity, new ChunkMortonKey { Morton = morton, Level = 0 });
                        EntityManager.SetComponentData(entity, new ChunkLifecycle { State = ChunkLifecycleState.Requested });
                    }
                }
            }

            foreach (var kv in existing)
            {
                if (wanted.Contains(kv.Key))
                {
                    continue;
                }

                var lifecycle = EntityManager.GetComponentData<ChunkLifecycle>(kv.Value);
                if (lifecycle.State != ChunkLifecycleState.Unloading)
                {
                    lifecycle.State = ChunkLifecycleState.Unloading;
                    EntityManager.SetComponentData(kv.Value, lifecycle);
                }
            }

            existing.Dispose();
            wanted.Dispose();
        }

        // Small blittable (x, z) pair used to detect "player changed chunk" without
        // pulling UnityEngine.Vector2Int into this Entities-only assembly.
        private readonly struct int2Key : System.IEquatable<int2Key>
        {
            private readonly int x;
            private readonly int z;

            public int2Key(int x, int z)
            {
                this.x = x;
                this.z = z;
            }

            public bool Equals(int2Key other) => x == other.x && z == other.z;
        }
    }
}