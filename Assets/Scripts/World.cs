// Fichier: World.cs (Singleton ou accessible globalement)
using System.Collections.Generic;
using UnityEngine;

public class World : MonoBehaviour
{
    // --- Singleton ---
    public static World Instance { get; private set; }

    // --- Configuration ---
    public Material worldMaterial; // Le matériel avec l'atlas de texture pour tous les chunks
    public GameObject chunkPrefab; // Le prefab du GameObject Chunk (avec le script Chunk attaché)
    public int renderDistance = 8; // Nombre de chunks à charger/afficher autour du joueur (en rayon)

    // --- Gestion des Chunks ---
    public Dictionary<Vector2Int, Chunk> activeChunks = new Dictionary<Vector2Int, Chunk>();
    private Transform playerTransform; // Pour savoir où charger/décharger les chunks

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
         if(chunkPrefab == null) {
             Debug.LogError("Chunk Prefab non assigné dans le World !");
             return;
         }
         if(worldMaterial == null) {
             Debug.LogError("World Material non assigné dans le World !");
             return;
         }

         // Trouver le joueur (suppose qu'il a le tag "Player")
         GameObject player = GameObject.FindGameObjectWithTag("Player");
         if(player != null) {
             playerTransform = player.transform;
         } else {
             Debug.LogWarning("Joueur non trouvé (Tag 'Player'). Chargement initial autour de (0,0).");
         }

         // Commencer à charger les chunks initiaux autour du point de départ
         // TODO: Implémenter un chargement/déchargement dynamique basé sur la position du joueur
         GenerateInitialChunks();
    }

    void Update() {
        // TODO: Mettre à jour les chunks (ex: appeler UpdateChunk pour ceux qui en ont besoin)
        // Idéalement, faire ça de manière asynchrone ou répartie sur plusieurs frames
        ProcessChunkUpdates();

        // TODO: Vérifier la position du joueur et charger/décharger les chunks dynamiquement
        // CheckAndLoadChunksAroundPlayer();
    }


    void GenerateInitialChunks() {
        // Exemple: Charger une zone fixe au démarrage
        Vector2Int playerChunkPos = GetChunkCoordsFromWorldPos(playerTransform != null ? playerTransform.position : Vector3.zero);

        for(int x = -renderDistance; x <= renderDistance; x++) {
             for(int z = -renderDistance; z <= renderDistance; z++) {
                 Vector2Int chunkCoord = new Vector2Int(playerChunkPos.x + x, playerChunkPos.y + z); // Utiliser y de Vector2Int pour z
                 LoadChunk(chunkCoord);
             }
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
                 Debug.Log($"Loaded Chunk at {coords}");
             } else {
                 Debug.LogError($"Le prefab de Chunk n'a pas de script Chunk attaché !");
                 Destroy(newChunkObject);
             }
         }
    }

     // Appelé par Update pour générer/mettre à jour les meshes des chunks marqués
    void ProcessChunkUpdates() {
        foreach(var chunkPair in activeChunks) {
             chunkPair.Value.UpdateChunk(); // Demande au chunk de vérifier s'il doit reconstruire son mesh
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

    public Vector2Int GetChunkCoordsFromWorldPos(Vector3 worldPos) {
         int x = Mathf.FloorToInt(worldPos.x / Chunk.Width);
         int z = Mathf.FloorToInt(worldPos.z / Chunk.Depth);
         return new Vector2Int(x, z);
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
}