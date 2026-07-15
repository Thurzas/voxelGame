using Unity.Collections;
using Unity.Entities;
using VoxelGame.Data;

namespace VoxelGame.Streaming
{
    /// <summary>
    /// Decides which chunks should exist given the current player position and render
    /// distance (singleton <see cref="ChunkStreamingConfig"/>, written by the legacy World
    /// MonoBehaviour bridge). Creates entities for newly-needed chunks (state Requested)
    /// and marks out-of-range loaded chunks for eviction (state Unloading). Replaces
    /// World.cs's UpdateChunksAroundPlayer/ProcessChunkUpdates scan (roadmap phase 2).
    /// Actual GameObject instantiation/destruction is handled by
    /// <see cref="ChunkStreamingBridgeSystem"/>.
    /// </summary>
    /// <remarks>
    /// Scans X/Z by <see cref="ChunkStreamingConfig.RenderDistance"/> around the player, and Y
    /// by a fixed <see cref="YLayerCount"/> (roadmap phase SVO sous-étape 2: chunks are now
    /// cubic and stack vertically, but Y-streaming isn't distance-driven yet — every XZ column
    /// in range always loads all <see cref="YLayerCount"/> vertical layers, reproducing the
    /// pre-SVO behaviour of one full-height column, structurally decomposed into cubic chunks.
    /// Distance-based vertical culling is deferred to a follow-up sous-étape).
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ChunkStreamingRequestSystem : SystemBase
    {
        // Nombre de couches verticales chargées par colonne XZ, mirroring l'ancienne hauteur de
        // colonne fixe (256) découpée en chunks cubiques de Chunk.Size (32) — 256/32 = 8. Ne peut
        // pas référencer Chunk.Size directement (VoxelGame.Streaming ne peut pas référencer
        // Assembly-CSharp, cf. roadmap phase 2), d'où la constante dupliquée avec ce commentaire.
        private const int YLayerCount = 8;

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

                    for (int y = 0; y < YLayerCount; y++)
                    {
                        ulong morton = MortonCode.Encode(x, y, z);
                        wanted.Add(morton);

                        if (!existing.ContainsKey(morton))
                        {
                            var entity = EntityManager.CreateEntity(typeof(ChunkMortonKey), typeof(ChunkLifecycle));
                            EntityManager.SetComponentData(entity, new ChunkMortonKey { Morton = morton, Level = 0 });
                            EntityManager.SetComponentData(entity, new ChunkLifecycle { State = ChunkLifecycleState.Requested });
                        }
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