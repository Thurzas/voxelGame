// Fichier: World.cs (Singleton ou accessible globalement)
using System.Collections.Generic;
using UnityEngine;
using EcsWorld = Unity.Entities.World;
using VoxelGame.Streaming;

public class World : MonoBehaviour
{
    // --- Singleton ---
    public static World Instance { get; private set; }

    // --- Configuration ---
    public Material worldMaterial; // Le matériel avec l'atlas de texture pour tous les chunks
    public GameObject chunkPrefab; // Le prefab du GameObject Chunk (avec le script Chunk attaché)
    public int renderDistance = 8; // Nombre de chunks à charger/afficher autour du joueur (en rayon)
    public int worldSeed;
    [SerializeField] private NoiseSettings terrainSettings;

    // Budget de temps (ms) accordé chaque frame pour finaliser des chunks dont la
    // heightmap GPU est prête (remplissage voxels + décoration + meshing + collider) —
    // étale ce travail sur plusieurs frames au lieu de tout faire d'un coup dès qu'un lot
    // de requêtes async se termine en même temps (roadmap phase 3, correctif du freeze
    // constaté au chargement/déplacement). À ajuster selon le profilage.
    public float terrainFinalizeBudgetMs = 4f;

    // --- Gestion des Chunks ---
    // La décision de quels chunks doivent être chargés/déchargés est pilotée par
    // VoxelGame.Streaming.ChunkStreamingRequestSystem (roadmap phase 2). World reste le
    // pont temporaire : il instancie/détruit encore les GameObjects Chunk et gère leur
    // mise à jour de mesh. Clé 3D depuis le passage aux chunks cubiques empilables
    // verticalement (roadmap phase SVO sous-étape 2) — auparavant Y était toujours 0.
    public Dictionary<Vector3Int, Chunk> activeChunks = new Dictionary<Vector3Int, Chunk>();
    private Transform playerTransform; // Pour savoir où charger/décharger les chunks

    private Vector2Int lastPlayerChunkCoord;

    // Une colonne XZ peut désormais être couverte par plusieurs chunks verticaux (roadmap phase
    // SVO sous-étape 2) : la heightmap (fonction pure de X/Z, indépendante de Y) est demandée
    // UNE fois par colonne et partagée entre eux plutôt que redemandée par étage — sinon on
    // multiplierait par le nombre de couches verticales le nombre de dispatchs GPU de bruit
    // pour un résultat identique.
    private class ColumnHeightmapState
    {
        public bool Ready;
        public float[,] Heightmap;
        public int RefCount;
        public List<System.Action<float[,]>> Pending = new List<System.Action<float[,]>>();
    }
    private readonly Dictionary<Vector2Int, ColumnHeightmapState> columnHeightmaps = new Dictionary<Vector2Int, ColumnHeightmapState>();

    // Incrémente le compteur de référence de la colonne (xz.x, xz.y) et invoque onReady dès que
    // sa heightmap est disponible (immédiatement si déjà en cache). Doit être appairé avec
    // ReleaseColumnHeightmap une fois pour chaque appel (cf. LoadChunk/UnloadChunk).
    private void RequestColumnHeightmap(Vector2Int xz, System.Action<float[,]> onReady)
    {
        if (!columnHeightmaps.TryGetValue(xz, out ColumnHeightmapState state))
        {
            state = new ColumnHeightmapState();
            columnHeightmaps.Add(xz, state);

            Vector2 offset = new Vector2(xz.x, xz.y);
            Noise.RequestHeightmapAsync(Chunk.Size, offset, heightmap =>
            {
                state.Ready = true;
                state.Heightmap = heightmap;
                var pending = state.Pending;
                state.Pending = new List<System.Action<float[,]>>();
                for (int i = 0; i < pending.Count; i++)
                {
                    pending[i](heightmap);
                }
            });
        }

        state.RefCount++;

        if (state.Ready)
        {
            onReady(state.Heightmap);
        }
        else
        {
            state.Pending.Add(onReady);
        }
    }

    private void ReleaseColumnHeightmap(Vector2Int xz)
    {
        if (!columnHeightmaps.TryGetValue(xz, out ColumnHeightmapState state))
        {
            return;
        }

        state.RefCount--;
        if (state.RefCount <= 0)
        {
            columnHeightmaps.Remove(xz);
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    void Start()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);

        if (terrainSettings != null)
        {
            Noise.ApplySettings(terrainSettings);
        }
        
        // Vérifications de base
        if (chunkPrefab == null || worldMaterial == null)
        {
            Debug.LogError("Configuration manquante dans World!");
            return;
        }

        // Trouver le joueur
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            playerTransform = player.transform;
            Vector3Int coord3D = GetChunkCoordsFromWorldPos(playerTransform.position);
            lastPlayerChunkCoord = new Vector2Int(coord3D.x, coord3D.z);
            PushStreamingConfig(lastPlayerChunkCoord);
        }
        else
        {
            Debug.LogWarning("Joueur non trouvé (Tag 'Player')");
        }
    }

    void Update()
    {
        // Délivre les heightmaps GPU déjà prêtes, en respectant un budget de temps par
        // frame (indépendant de playerTransform : doit tourner même si le joueur n'est
        // pas encore trouvé).
        Noise.DeliverReadyResults(terrainFinalizeBudgetMs);

        if (playerTransform == null) return;

        // Obtenir les coordonnées du chunk où se trouve le joueur (XZ : la décision de streaming
        // reste pilotée en XZ pour cette sous-étape, cf. ChunkStreamingRequestSystem).
        Vector3Int playerChunkCoord3D = GetChunkCoordsFromWorldPos(playerTransform.position);
        Vector2Int currentPlayerChunkCoord = new Vector2Int(playerChunkCoord3D.x, playerChunkCoord3D.z);

        // Si le joueur a changé de chunk, notifier VoxelGame.Streaming.ChunkStreamingRequestSystem
        // (c'est lui qui décide désormais quels chunks charger/décharger, cf. roadmap phase 2).
        if (currentPlayerChunkCoord != lastPlayerChunkCoord)
        {
            lastPlayerChunkCoord = currentPlayerChunkCoord;
            PushStreamingConfig(currentPlayerChunkCoord);
        }

        // Mettre à jour les meshes des chunks actifs (indépendant de la décision de
        // streaming : déclenché par les modifications de voxels, cf. Chunk.SetVoxel).
        foreach (var chunk in activeChunks.Values)
        {
            chunk.UpdateChunk();
        }
    }

    void PushStreamingConfig(Vector2Int playerChunkCoord)
    {
        var ecsWorld = EcsWorld.DefaultGameObjectInjectionWorld;
        if (ecsWorld == null || !ecsWorld.IsCreated)
        {
            return;
        }

        var entityManager = ecsWorld.EntityManager;
        var query = entityManager.CreateEntityQuery(typeof(ChunkStreamingConfig));
        if (query.IsEmpty)
        {
            return; // Le OnCreate de ChunkStreamingRequestSystem n'a pas encore tourné.
        }

        entityManager.SetComponentData(query.GetSingletonEntity(), new ChunkStreamingConfig
        {
            PlayerChunkCoord = new Unity.Mathematics.int2(playerChunkCoord.x, playerChunkCoord.y),
            RenderDistance = renderDistance,
        });
    }

    // Appelé par VoxelGame.Streaming.ChunkStreamingBridgeSystem (roadmap phase 2) pour les
    // entités passées à l'état Requested. coords est désormais une vraie position 3D
    // (roadmap phase SVO sous-étape 2 : chunks cubiques empilables verticalement).
    public void LoadChunk(Vector3Int coords) {
         if (!activeChunks.ContainsKey(coords)) {
             GameObject newChunkObject = Instantiate(chunkPrefab, Vector3.zero, Quaternion.identity, this.transform); // Parenté au World
             Chunk newChunk = newChunkObject.GetComponent<Chunk>();
             if (newChunk != null) {
                 newChunk.Initialize(coords, worldMaterial);
                 activeChunks.Add(coords, newChunk);

                 var xz = new Vector2Int(coords.x, coords.z);
                 RequestColumnHeightmap(xz, heightmap => newChunk.OnHeightmapReady(heightmap));
             } else {
                 Debug.LogError($"Le prefab de Chunk n'a pas de script Chunk attaché !");
                 Destroy(newChunkObject);
             }
         }
    }

    // Appelé par VoxelGame.Streaming.ChunkStreamingBridgeSystem (roadmap phase 2) pour les
    // entités passées à l'état Unloading.
    public void UnloadChunk(Vector3Int coords) {
        if (activeChunks.TryGetValue(coords, out Chunk chunk)) {
            Destroy(chunk.gameObject);
            activeChunks.Remove(coords);
            ReleaseColumnHeightmap(new Vector2Int(coords.x, coords.z));
        }
    }

    // --- Accès aux Voxels (méthodes helper) ---

    // Position du joueur pour la priorisation des remaillages GPU (cf. VoxelMesherGpu) — plus
    // pertinent que la seule distance de chargement de chunks une fois que des éditions/de la
    // simulation de fluides pourront déclencher des remaillages en dehors de l'ordre de
    // chargement initial.
    public Vector3 PlayerPosition => playerTransform != null ? playerTransform.position : Vector3.zero;

    public Chunk GetChunk(Vector3Int chunkPosition) {
         activeChunks.TryGetValue(chunkPosition, out Chunk chunk);
         return chunk;
    }

    public Chunk GetChunkFromWorldPosition(Vector3 worldPos)
    {
        Vector3Int chunkCoord = GetChunkCoordsFromWorldPos(worldPos);
        activeChunks.TryGetValue(chunkCoord, out Chunk chunk);
        return chunk;
    }

    public Vector3Int GetChunkCoordsFromWorldPos(Vector3 worldPos)
    {
        // Convertir la position monde en coordonnées de chunk (X, Y et Z : chunks cubiques
        // empilables, roadmap phase SVO sous-étape 2).
        int chunkX = Mathf.FloorToInt(worldPos.x / Chunk.Size);
        int chunkY = Mathf.FloorToInt(worldPos.y / Chunk.Size);
        int chunkZ = Mathf.FloorToInt(worldPos.z / Chunk.Size);
        return new Vector3Int(chunkX, chunkY, chunkZ);
    }

    public Voxel GetVoxel(Vector3Int worldPos)
    {
        Chunk chunk = GetChunkFromWorldPosition(worldPos);
        if (chunk != null)
        {
            Vector3Int localPos = chunk.GetLocalPosition(worldPos);
            return chunk.GetVoxel(localPos.x, localPos.y, localPos.z); // Le GetVoxel du chunk gère déjà les limites internes
        }
        // Chunk non chargé (hors render distance, ou en dehors de la plage Y actuellement
        // streamée) : Air par défaut, même convention que pour les positions X/Z non chargées.
        return new Voxel(VoxelType.Air);

    }

    public bool IsVoxelSolid(Vector3Int worldPos) {
         return GetVoxel(worldPos).IsSolid;
    }

    public void SetVoxel(Vector3Int worldPos, VoxelType type)
    {
        Chunk chunk = GetChunkFromWorldPosition(worldPos);
        if (chunk != null)
        {
            Vector3Int localPos = chunk.GetLocalPosition(worldPos);
            chunk.SetVoxel(localPos.x, localPos.y, localPos.z, type);
        }
        else
        {
            Debug.LogWarning($"Tentative de modification d'un voxel dans un chunk non chargé à {worldPos}");
            // Idéalement, il faudrait charger le chunk ou mettre en file d'attente la modification
        }
    }

    void OnDestroy()
    {
        Noise.Cleanup();
    }
}
