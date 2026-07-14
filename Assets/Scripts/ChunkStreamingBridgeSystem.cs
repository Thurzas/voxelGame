using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VoxelGame.Data;
using VoxelGame.Streaming;

// Volontairement dans Assembly-CSharp (pas dans l'asmdef VoxelGame.Streaming) : ce fichier
// fait le pont entre les entités ECS et le MonoBehaviour World legacy (roadmap phase 2).
// Un asmdef ne peut pas référencer Assembly-CSharp (c'est l'inverse qui est vrai), donc ce
// pont ne peut vivre que du côté "legacy". Il disparaîtra une fois le meshing/rendu migrés
// vers ECS/Entities Graphics (roadmap phases 4-5).

/// <summary>
/// Temporary bridge (roadmap phase 2): turns the ECS chunk-lifecycle decisions made by
/// <see cref="ChunkStreamingRequestSystem"/> into calls on the legacy <c>World</c>
/// MonoBehaviour, which still owns the actual GameObject instantiation/destruction and
/// mesh generation.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChunkStreamingRequestSystem))]
public partial class ChunkStreamingBridgeSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (global::World.Instance == null)
        {
            return;
        }

        var toLoad = new NativeList<Entity>(Allocator.Temp);
        var toLoadCoords = new NativeList<Unity.Mathematics.int2>(Allocator.Temp);
        var toUnload = new NativeList<Entity>(Allocator.Temp);
        var toUnloadCoords = new NativeList<Unity.Mathematics.int2>(Allocator.Temp);

        foreach (var (key, lifecycle, entity) in
                 SystemAPI.Query<RefRO<ChunkMortonKey>, RefRO<ChunkLifecycle>>().WithEntityAccess())
        {
            MortonCode.Decode(key.ValueRO.Morton, out int x, out _, out int z);

            switch (lifecycle.ValueRO.State)
            {
                case ChunkLifecycleState.Requested:
                    toLoad.Add(entity);
                    toLoadCoords.Add(new Unity.Mathematics.int2(x, z));
                    break;
                case ChunkLifecycleState.Unloading:
                    toUnload.Add(entity);
                    toUnloadCoords.Add(new Unity.Mathematics.int2(x, z));
                    break;
            }
        }

        for (int i = 0; i < toLoad.Length; i++)
        {
            var coord = toLoadCoords[i];
            global::World.Instance.LoadChunk(new Vector2Int(coord.x, coord.y));
            EntityManager.SetComponentData(toLoad[i], new ChunkLifecycle { State = ChunkLifecycleState.Loaded });
        }

        for (int i = 0; i < toUnload.Length; i++)
        {
            var coord = toUnloadCoords[i];
            global::World.Instance.UnloadChunk(new Vector2Int(coord.x, coord.y));
            EntityManager.DestroyEntity(toUnload[i]);
        }

        toLoad.Dispose();
        toLoadCoords.Dispose();
        toUnload.Dispose();
        toUnloadCoords.Dispose();
    }
}