// Fichier: World.cs (Singleton ou accessible globalement)
using System.Collections.Generic;
using UnityEngine;

public class World : MonoBehaviour
{
    // --- Singleton ---
    public static World Instance { get; private set; }

    // --- Configuration ---
    public Material worldMaterial; // Le matériel avec l'atlas de texture pour tous les chunks
    public Material waterMaterial; // Le material pour l'eau
    public GameObject chunkPrefab; // Le prefab du GameObject Chunk (avec le script Chunk attaché)
    public int renderDistance = 8; // Nombre de chunks à charger/afficher autour du joueur (en rayon)
    public int worldSeed = 1337;
    [SerializeField] private NoiseSettings terrainSettings;

    // --- Gestion des Chunks ---
    public Dictionary<Vector2Int, Chunk> activeChunks = new Dictionary<Vector2Int, Chunk>();
    private Transform playerTransform; // Pour savoir où charger/décharger les chunks

    private Vector2Int lastPlayerChunkCoord;
    private HashSet<Vector2Int> chunksToLoad = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> chunksToUnload = new HashSet<Vector2Int>();

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
            // Charger les chunks initiaux autour du joueur
            UpdateChunksAroundPlayer(GetChunkCoordsFromWorldPos(playerTransform.position));
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

        // Si le joueur a changé de chunk, mettre à jour les chunks
        if (currentPlayerChunkCoord != lastPlayerChunkCoord)
        {
            UpdateChunksAroundPlayer(currentPlayerChunkCoord);
            lastPlayerChunkCoord = currentPlayerChunkCoord;
        }

        // Traiter les chunks à charger/décharger
        ProcessChunkUpdates();
    }

    void UpdateChunksAroundPlayer(Vector2Int playerChunkCoord)
    {
        chunksToLoad.Clear();
        chunksToUnload.Clear();

        // Déterminer quels chunks devraient être chargés
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int z = -renderDistance; z <= renderDistance; z++)
            {
                Vector2Int chunkCoord = new Vector2Int(playerChunkCoord.x + x, playerChunkCoord.y + z);
                
                // Si le chunk n'est pas déjà chargé, l'ajouter à la liste de chargement
                if (!activeChunks.ContainsKey(chunkCoord))
                {
                    chunksToLoad.Add(chunkCoord);
                }
            }
        }

        // Identifier les chunks à décharger (ceux qui sont trop loin)
        foreach (var chunk in activeChunks)
        {
            Vector2Int coord = chunk.Key;
            if (Mathf.Abs(coord.x - playerChunkCoord.x) > renderDistance ||
                Mathf.Abs(coord.y - playerChunkCoord.y) > renderDistance)
            {
                chunksToUnload.Add(coord);
            }
        }
    }

    void ProcessChunkUpdates()
    {
        // Décharger les chunks trop éloignés
        foreach (var coord in chunksToUnload)
        {
            if (activeChunks.TryGetValue(coord, out Chunk chunk))
            {
                Destroy(chunk.gameObject);
                activeChunks.Remove(coord);
            }
        }

        // Charger les nouveaux chunks
        foreach (var coord in chunksToLoad)
        {
            LoadChunk(coord);
        }

        // Mettre à jour les meshes des chunks actifs
        foreach (var chunk in activeChunks.Values)
        {
            chunk.UpdateChunk();
        }
    }

    void LoadChunk(Vector2Int coords) {
         if (!activeChunks.ContainsKey(coords)) {
             GameObject newChunkObject = Instantiate(chunkPrefab, Vector3.zero, Quaternion.identity, this.transform); // Parenté au World
             Chunk newChunk = newChunkObject.GetComponent<Chunk>();
             if (newChunk != null) {
                 Vector3Int chunkPos3D = new Vector3Int(coords.x, 0, coords.y); // Y=0 pour la position du chunk
                 newChunk.Initialize(chunkPos3D, worldMaterial);
                 activeChunks.Add(coords, newChunk);
                 //Debug.Log($"Loaded Chunk at {coords}");
             } else {
                 Debug.LogError($"Le prefab de Chunk n'a pas de script Chunk attaché !");
                 Destroy(newChunkObject);
             }
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
