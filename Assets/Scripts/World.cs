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

    // --- Gestion des Chunks ---
    // La décision de quels chunks doivent être chargés/déchargés est pilotée par
    // VoxelGame.Streaming.ChunkStreamingRequestSystem (roadmap phase 2). World reste le
    // pont temporaire : il instancie/détruit encore les GameObjects Chunk et gère leur
    // mise à jour de mesh.
    public Dictionary<Vector2Int, Chunk> activeChunks = new Dictionary<Vector2Int, Chunk>();
    private Transform playerTransform; // Pour savoir où charger/décharger les chunks

    private Vector2Int lastPlayerChunkCoord;

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
            lastPlayerChunkCoord = GetChunkCoordsFromWorldPos(playerTransform.position);
            PushStreamingConfig(lastPlayerChunkCoord);
        }
        else
        {
            Debug.LogWarning("Joueur non trouvé (Tag 'Player')");
        }
    }

    void Update()
    {
        if (playerTransform == null) return;

        // Obtenir les coordonnées du chunk où se trouve le joueur
        Vector2Int currentPlayerChunkCoord = GetChunkCoordsFromWorldPos(playerTransform.position);

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
    // entités passées à l'état Requested.
    public void LoadChunk(Vector2Int coords) {
         if (!activeChunks.ContainsKey(coords)) {
             GameObject newChunkObject = Instantiate(chunkPrefab, Vector3.zero, Quaternion.identity, this.transform); // Parenté au World
             Chunk newChunk = newChunkObject.GetComponent<Chunk>();
             if (newChunk != null) {
                 Vector3Int chunkPos3D = new Vector3Int(coords.x, 0, coords.y); // Y=0 pour la position du chunk
                 newChunk.Initialize(chunkPos3D, worldMaterial);
                 activeChunks.Add(coords, newChunk);
             } else {
                 Debug.LogError($"Le prefab de Chunk n'a pas de script Chunk attaché !");
                 Destroy(newChunkObject);
             }
         }
    }

    // Appelé par VoxelGame.Streaming.ChunkStreamingBridgeSystem (roadmap phase 2) pour les
    // entités passées à l'état Unloading.
    public void UnloadChunk(Vector2Int coords) {
        if (activeChunks.TryGetValue(coords, out Chunk chunk)) {
            Destroy(chunk.gameObject);
            activeChunks.Remove(coords);
        }
    }

    // --- Accès aux Voxels (méthodes helper) ---

    public Chunk GetChunk(Vector3Int chunkPosition) {
        Vector2Int coords = new Vector2Int(chunkPosition.x, chunkPosition.z);
         activeChunks.TryGetValue(coords, out Chunk chunk);
         return chunk;
    }

    public Chunk GetChunkFromWorldPosition(Vector3 worldPos)
    {
        Vector2Int chunkCoord = GetChunkCoordsFromWorldPos(worldPos);
        activeChunks.TryGetValue(chunkCoord, out Chunk chunk);
        return chunk;
    }

    public Vector2Int GetChunkCoordsFromWorldPos(Vector3 worldPos)
    {
        // Convertir la position monde en coordonnées de chunk
        int chunkX = Mathf.FloorToInt(worldPos.x / Chunk.Width);
        int chunkZ = Mathf.FloorToInt(worldPos.z / Chunk.Depth);
        return new Vector2Int(chunkX, chunkZ);
    }

    public Voxel GetVoxel(Vector3Int worldPos)
    {
        if(worldPos.y < 0 || worldPos.y >= Chunk.Height) return new Voxel(VoxelType.Air); // Hors limites verticales

        Chunk chunk = GetChunkFromWorldPosition(worldPos);
        if (chunk != null)
        {
            Vector3Int localPos = chunk.GetLocalPosition(worldPos);
            return chunk.GetVoxel(localPos.x, localPos.y, localPos.z); // Le GetVoxel du chunk gère déjà les limites internes
        }
        // Si le chunk n'est pas chargé, considérer comme Air (ou Stone sous une certaine hauteur?)
        // return new Voxel(worldPos.y < 60 ? VoxelType.Stone : VoxelType.Air); // Comportement simple si chunk non chargé
        return new Voxel(VoxelType.Air); // Comportement par défaut si chunk non chargé

    }

    public bool IsVoxelSolid(Vector3Int worldPos) {
         return GetVoxel(worldPos).IsSolid;
    }

    public void SetVoxel(Vector3Int worldPos, VoxelType type)
    {
         if(worldPos.y < 0 || worldPos.y >= Chunk.Height) return; // Ne peut pas modifier hors limites verticales

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
