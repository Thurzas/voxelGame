using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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

        int2 playerChunkCoord = SystemAPI.GetSingleton<ChunkStreamingConfig>().PlayerChunkCoord;

        var toLoad = new NativeList<LoadRequest>(Allocator.Temp);
        var toUnload = new NativeList<Entity>(Allocator.Temp);
        var toUnloadCoords = new NativeList<int3>(Allocator.Temp);

        foreach (var (key, lifecycle, entity) in
                 SystemAPI.Query<RefRO<ChunkMortonKey>, RefRO<ChunkLifecycle>>().WithEntityAccess())
        {
            MortonCode.Decode(key.ValueRO.Morton, out int x, out int y, out int z);

            switch (lifecycle.ValueRO.State)
            {
                case ChunkLifecycleState.Requested:
                    var coord = new int3(x, y, z);
                    // Distance en XZ uniquement : la décision de streaming reste pilotée en XZ
                    // pour cette sous-étape (toutes les couches Y d'une colonne sont chargées
                    // ensemble, cf. ChunkStreamingRequestSystem), donc ordonner par XZ suffit à
                    // faire apparaître les colonnes proches en premier.
                    int2 delta = coord.xz - playerChunkCoord;
                    toLoad.Add(new LoadRequest
                    {
                        Entity = entity,
                        Coord = coord,
                        DistanceSq = (delta.x * delta.x) + (delta.y * delta.y),
                    });
                    break;
                case ChunkLifecycleState.Unloading:
                    toUnload.Add(entity);
                    toUnloadCoords.Add(new int3(x, y, z));
                    break;
            }
        }

        // Tri à la source, par distance au joueur : les files d'attente en aval (pool async de
        // heightmaps dans Noise.cs, queue de meshing GPU dans VoxelMesherGpu) sont de simples
        // FIFO — les alimenter déjà dans l'ordre de proximité fait apparaître les chunks proches
        // en premier sans avoir besoin de rendre ces files elles-mêmes priorité-conscientes.
        toLoad.AsArray().Sort();

        for (int i = 0; i < toLoad.Length; i++)
        {
            LoadRequest request = toLoad[i];
            global::World.Instance.LoadChunk(new Vector3Int(request.Coord.x, request.Coord.y, request.Coord.z));
            EntityManager.SetComponentData(request.Entity, new ChunkLifecycle { State = ChunkLifecycleState.Loaded });
        }

        for (int i = 0; i < toUnload.Length; i++)
        {
            var coord = toUnloadCoords[i];
            global::World.Instance.UnloadChunk(new Vector3Int(coord.x, coord.y, coord.z));
            EntityManager.DestroyEntity(toUnload[i]);
        }

        toLoad.Dispose();
        toUnload.Dispose();
        toUnloadCoords.Dispose();
    }

    private struct LoadRequest : System.IComparable<LoadRequest>
    {
        public Entity Entity;
        public int3 Coord;
        public int DistanceSq;

        public int CompareTo(LoadRequest other) => DistanceSq.CompareTo(other.DistanceSq);
    }
}