// Fichier: Chunk.cs
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    // --- Constantes ---
    public const int Width = 32;   // Taille X
    public const int Height = 256; // Taille Y (Hauteur du monde)
    public const int Depth = 32;   // Taille Z

    // --- Données ---
    public Vector3Int chunkPosition; // Position du chunk dans la grille de chunks (ex: (0,0), (1,0))
                                     // La position réelle dans le monde est chunkPosition * TailleChunk
    private Voxel[,,] voxelData = new Voxel[Width, Height, Depth];

    // --- Composants Unity ---
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh generatedMesh; // Le mesh généré

    // --- État ---
    private bool needsMeshUpdate = false;

    // --- Eau ---

    RenderTexture waterStateA;
    RenderTexture waterStateB;
    bool useAasInput = true;

    // pourrait avoir d'autres états: isLoaded, isGenerated, etc.

    void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
    }

    public void Initialize(Vector3Int position, Material material) // Passé par le World Manager
    {
        this.chunkPosition = position;
        this.transform.position = new Vector3(position.x * Width, 0, position.z * Depth); // Positionner le GameObject
        this.name = $"Chunk ({position.x}, {position.z})";
        this.meshRenderer.material = material; // Assigner le matériel (atlas de textures)

        GenerateTerrain(); // Exemple simple

        // Ajoutez des logs pour debug
        Debug.Log($"Starting decoration for chunk at {position}");
        TerrainDecoration decorator = new TerrainDecoration();
        decorator.DecorateChunk(this);
        Debug.Log("Chunk decoration completed");

        // Initialisation de l'état de l'eau APRÈS la décoration
        InitializeWaterStateTexture();

        // Marquer pour la génération initiale du mesh
        needsMeshUpdate = true;
    }

    // Méthode pour obtenir/définir un voxel (coordonnées locales au chunk)
    public Voxel GetVoxel(int x, int y, int z)
    {
        if (IsVoxelInChunk(x, y, z))
        {
            return voxelData[x, y, z];
        }
        // Si hors limites, demander au gestionnaire de monde (World) le voxel du chunk voisin
        // Pour l'instant, retournons Air comme valeur sûre ou lance une exception.
        // return VoxelType.Air; // Ou mieux : déléguer au World.cs
        return World.Instance.GetVoxel(GetWorldPosition(x, y, z)); // Exemple avec un singleton World
    }

    public void SetVoxel(int x, int y, int z, VoxelType type)
    {
        if (IsVoxelInChunk(x, y, z))
        {
            if (voxelData[x, y, z].type != type) // Vérifier si le type change réellement
            {
                 voxelData[x, y, z].type = type;
                 needsMeshUpdate = true; // Le mesh doit être regénéré

                 // OPTIMISATION: Si le bloc est à la frontière (x=0, x=Width-1, z=0, z=Depth-1)
                 // il faut aussi notifier le chunk voisin de potentiellement mettre à jour son mesh.
                 CheckNeighborChunksForUpdate(x, y, z);
            }
        }
        else // Si on essaie de modifier hors limites, on délègue au World
        {
             World.Instance.SetVoxel(GetWorldPosition(x,y,z), type);
        }
    }

    // Vérifie si les coordonnées sont dans les limites du chunk
    public static bool IsVoxelInChunk(int x, int y, int z)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height && z >= 0 && z < Depth;
    }

    // Convertit les coordonnées locales du chunk en coordonnées mondiales
    public Vector3Int GetWorldPosition(int x, int y, int z) {
        return new Vector3Int(
            x + chunkPosition.x * Width,
            y, // Y n'est pas affecté par chunkPosition.y qui est souvent 0
            z + chunkPosition.z * Depth
        );
    }

     // Convertit les coordonnées mondiales en coordonnées locales du chunk
    public Vector3Int GetLocalPosition(Vector3Int worldPos) {
        return new Vector3Int(
            worldPos.x - chunkPosition.x * Width,
            worldPos.y,
            worldPos.z - chunkPosition.z * Depth
        );
    }


    // Méthode appelée régulièrement (ex: dans Update ou via un gestionnaire) pour reconstruire le mesh si nécessaire
    public void UpdateChunk()
    {
        if (needsMeshUpdate)
        {
            needsMeshUpdate = false;
            GenerateMeshWithShader(); // Ou GenerateMeshCPU() pour une version CPU
        }
    }

    // --- Eau ---
    // Initialisation des textures de l'eau
    void InitializeWaterStateTexture()
    {
        waterStateA = new RenderTexture(Width, Height, 0, RenderTextureFormat.RFloat);
        waterStateA.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        waterStateA.enableRandomWrite = true;
        waterStateA.volumeDepth = Depth;
        waterStateA.Create();

        waterStateB = new RenderTexture(Width, Height, 0, RenderTextureFormat.RFloat);
        waterStateB.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        waterStateB.enableRandomWrite = true;
        waterStateB.volumeDepth = Depth;
        waterStateB.Create();

        // Remplissage initial de l'état de l'eau selon voxelData
        Texture3D temp = new Texture3D(Width, Height, Depth, TextureFormat.RFloat, false);
        Color[] colors = new Color[Width * Height * Depth];
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                for (int z = 0; z < Depth; z++)
                    colors[x + y * Width + z * Width * Height] =
                        voxelData[x, y, z].type == VoxelType.Water ? new Color(1, 0, 0, 0) : new Color(0, 0, 0, 0);
        temp.SetPixels(colors);
        temp.Apply();
        Graphics.CopyTexture(temp, 0, 0, waterStateA, 0, 0);
        Object.Destroy(temp);
    }

    // Simulation de l'eau (génération à chaque tick)
 
    void SimulateWaterStep()
    {
        // Ne simule que si les textures sont bien initialisées
        if (waterStateA == null || waterStateB == null)
            return;

        var compute = (ComputeShader)Resources.Load("WaterAutomata");
        int kernel = compute.FindKernel("SimulateWater");

        // Définir la taille du chunk
        compute.SetInts("chunkSize", Width, Height, Depth);

        // Ping-pong des textures
        if (useAasInput)
        {
            compute.SetTexture(kernel, "WaterStateIn", waterStateA);
            compute.SetTexture(kernel, "WaterStateOut", waterStateB);
        }
        else
        {
            compute.SetTexture(kernel, "WaterStateIn", waterStateB);
            compute.SetTexture(kernel, "WaterStateOut", waterStateA);
        }
        useAasInput = !useAasInput;

        int threadGroupsX = Mathf.CeilToInt(Width / 8f);
        int threadGroupsY = Mathf.CeilToInt(Height / 8f);
        int threadGroupsZ = Mathf.CeilToInt(Depth / 8f);

        compute.Dispatch(kernel, threadGroupsX, threadGroupsY, threadGroupsZ);
    }

    void Update()
    {
        // Simulation de l'eau à chaque frame
        SimulateWaterStep();
    }

    // --- Méthodes de génération (exemples) ---

    void GenerateTerrain()
    {
        // Correction : offset mondial pour continuité parfaite du bruit
        Vector2 worldOffset = new Vector2(
            chunkPosition.x * Width,
            chunkPosition.z * Depth
        );
        float[,] heightmap = Noise.GenerateHeightmap(Width, worldOffset);
        NoiseSettings settings = Noise.CurrentSettings;
        int seaLevel = 20;
        
        // --- Génération du terrain de base avec plages et eau ---
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Depth; z++)
            {
                // Correction : coordonnées mondiales pour chaque voxel
                float heightValue = heightmap[x, z];
                int worldX = chunkPosition.x * Width + x;
                int worldZ = chunkPosition.z * Depth + z;
                int groundHeight = Mathf.FloorToInt(settings.baseHeight + (heightValue - 0.5f) * settings.heightMultiplier);
                for (int y = 0; y < Height; y++)
                {
                    if (y < groundHeight - 3)
                        voxelData[x, y, z] = new Voxel(VoxelType.Stone);
                    else if (y < groundHeight)
                        voxelData[x, y, z] = new Voxel(VoxelType.Dirt);
                    else if (y == groundHeight)
                    {
                        if (groundHeight <= seaLevel + 2)
                            voxelData[x, y, z] = new Voxel(VoxelType.Sand); // Plage
                        else
                            voxelData[x, y, z] = new Voxel(VoxelType.Grass);
                    }
                    else if (y > groundHeight && y <= seaLevel)
                        voxelData[x, y, z] = new Voxel(VoxelType.Water); // Eau jusqu'au niveau de la mer
                    else
                        voxelData[x, y, z] = new Voxel(VoxelType.Air);
                }
            }
        }

        // --- Génération déterministe des décorations (arbres) ---
        int worldSeed = World.Instance.worldSeed;
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Depth; z++)
            {
                int worldX = chunkPosition.x * Width + x;
                int worldZ = chunkPosition.z * Depth + z;
                int hash = worldX * 73856093 ^ worldZ * 19349663 ^ worldSeed;
                Random.InitState(hash);
                int surfaceY = GetSurfaceY(x, z);
                // On ne place d'arbre que si la surface est au-dessus de l'eau, et sur de la terre
                if (
                    surfaceY > seaLevel &&
                    voxelData[x, surfaceY, z].type == VoxelType.Dirt &&
                    Random.value < 0.05f
                )
                {
                    PlaceTree(worldX, surfaceY, worldZ);
                }
            }
        }
        // Suppression de l'appel à InitializeWaterStateTexture ici (déplacé dans Initialize)
    }

    // Retourne la hauteur du sol pour (x, z) local au chunk
    int GetSurfaceY(int x, int z)
    {
        for (int y = Height - 1; y >= 0; y--)
        {
            if (voxelData[x, y, z].type != VoxelType.Air)
                return y + 1;
        }
        return 1;
    }

    // Place un arbre déterministe, en écrivant uniquement dans le chunk courant
    void PlaceTree(int worldX, int y, int worldZ)
    {
        int treeHeight = 5;
        int leafRadius = 1;
        for (int dy = 0; dy < treeHeight; dy++)
        {
            WriteVoxelIfInChunk(worldX, y + dy, worldZ, VoxelType.Wood);
        }
        for (int dx = -leafRadius; dx <= leafRadius; dx++)
        for (int dz = -leafRadius; dz <= leafRadius; dz++)
        for (int dy = -1; dy <= 1; dy++)
        {
            WriteVoxelIfInChunk(worldX + dx, y + treeHeight - 1 + dy, worldZ + dz, VoxelType.Leaves);
        }
    }

    // N'écrit que si la position est dans le chunk courant
    void WriteVoxelIfInChunk(int wx, int y, int wz, VoxelType type)
    {
        int localX = wx - chunkPosition.x * Width;
        int localZ = wz - chunkPosition.z * Depth;
        if (localX >= 0 && localX < Width && localZ >= 0 && localZ < Depth && y >= 0 && y < Height)
        {
            voxelData[localX, y, localZ] = new Voxel(type);
        }
    }

    void CheckNeighborChunksForUpdate(int x, int y, int z)
    {
        if (World.Instance == null) return; // S'assurer que le monde existe

        // Si à la limite X=0, notifier le chunk voisin en (-1, 0)
        if (x == 0) World.Instance.GetChunk(chunkPosition + Vector3Int.left)?.MarkForMeshUpdate();
        // Si à la limite X=Width-1, notifier le chunk voisin en (+1, 0)
        else if (x == Width - 1) World.Instance.GetChunk(chunkPosition + Vector3Int.right)?.MarkForMeshUpdate();

        // Si à la limite Z=0, notifier le chunk voisin en (0, -1)
        if (z == 0) World.Instance.GetChunk(chunkPosition + new Vector3Int(0,0,-1))?.MarkForMeshUpdate();
        // Si à la limite Z=Depth-1, notifier le chunk voisin en (0, +1)
        else if (z == Depth - 1) World.Instance.GetChunk(chunkPosition + new Vector3Int(0,0,1))?.MarkForMeshUpdate();

        // Pas besoin de vérifier Y car les chunks sont typiquement empilés verticalement dans le même GameObject ou gérés différemment.
        // Si vous avez des chunks empilés (ex: 16x16x16), il faudrait vérifier Y aussi.
    }

     public void MarkForMeshUpdate() {
        needsMeshUpdate = true;
    }


    // --- Logique de Mesh (sera détaillée ensuite) ---
    void GenerateMeshWithShader()
    {
        // Implémentation détaillée dans la section suivante
        //Debug.Log($"Generating mesh for chunk {chunkPosition} using Shaders (Conceptual)");
        // 1. Préparer les données pour le Compute Shader (ex: ComputeBuffer des voxelData)
        // 2. Dispatcher le Compute Shader
        // 3. Récupérer les buffers de sortie (vertices, triangles, uvs, etc.)
        // 4. Créer/Mettre à jour le Mesh Unity
        // 5. Assigner le mesh au MeshFilter et au MeshCollider

        // TODO: Implémenter la logique de génération de mesh par Shader
        // Pour l'instant, on peut mettre un placeholder ou une version CPU simple
        GenerateMeshCPU_Naive(); // Appelons une version CPU simple pour tester
    }

    // Version CPU très basique pour le test (NON OPTIMALE !)
     void GenerateMeshCPU_Naive()
    {
        //Debug.Log($"Generating mesh for chunk {chunkPosition} using CPU (Naive)");
        System.Collections.Generic.List<Vector3> vertices = new System.Collections.Generic.List<Vector3>();
        System.Collections.Generic.List<int> triangles = new System.Collections.Generic.List<int>();
        System.Collections.Generic.List<Vector2> uvs = new System.Collections.Generic.List<Vector2>();
        int vertexIndex = 0;

        for (int x = 0; x < Width; x++) {
            for (int y = 0; y < Height; y++) {
                for (int z = 0; z < Depth; z++) {
                    if (voxelData[x, y, z].IsSolid) {
                        Vector3 pos = new Vector3(x, y, z);
                        // Vérifier chaque face
                        // Face +X (Droite)
                        if (!IsVoxelSolid(x + 1, y, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.right, VoxelTypeToTexture(voxelData[x, y, z].type, Vector3.right));
                        // Face -X (Gauche)
                        if (!IsVoxelSolid(x - 1, y, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.left, VoxelTypeToTexture(voxelData[x, y, z].type, Vector3.left));
                        // Face +Y (Haut)
                        if (!IsVoxelSolid(x, y + 1, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.up, VoxelTypeToTexture(voxelData[x, y, z].type, Vector3.up));
                        // Face -Y (Bas)
                        if (!IsVoxelSolid(x, y - 1, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.down, VoxelTypeToTexture(voxelData[x, y, z].type, Vector3.down));
                        // Face +Z (Avant)
                        if (!IsVoxelSolid(x, y, z + 1)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.forward, VoxelTypeToTexture(voxelData[x, y, z].type, Vector3.forward));
                        // Face -Z (Arrière)
                        if (!IsVoxelSolid(x, y, z - 1)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.back, VoxelTypeToTexture(voxelData[x, y, z].type, Vector3.back));
                    }
                }
            }
        }

        if (generatedMesh == null) {
             generatedMesh = new Mesh();
             generatedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Pour les grands meshes
        } else {
             generatedMesh.Clear();
        }

        generatedMesh.vertices = vertices.ToArray();
        generatedMesh.triangles = triangles.ToArray();
        generatedMesh.uv = uvs.ToArray();
        generatedMesh.RecalculateNormals(); // Important pour l'éclairage
        generatedMesh.Optimize(); // Optimiser la structure du mesh

        meshFilter.mesh = generatedMesh;
        meshCollider.sharedMesh = generatedMesh; // Mettre à jour le collider physique
    }

     // Helper pour vérifier si un voxel est solide (gère les limites du chunk)
    bool IsVoxelSolid(int x, int y, int z) {
        // Vérifier d'abord si c'est DANS ce chunk
        if (IsVoxelInChunk(x, y, z)) {
             return voxelData[x, y, z].IsSolid;
        } else {
             // Si c'est hors limites, demander au monde (potentiellement un chunk voisin)
             if (World.Instance != null) {
                 return World.Instance.IsVoxelSolid(GetWorldPosition(x, y, z));
             }
             return false; // Par défaut, considérer hors monde comme non solide (ou solide, selon la logique voulue aux bords du monde chargé)
        }
    }

    // Helper pour ajouter une face (version très basique, UVs simplifiés)
    // TODO: Implémenter correctement les UVs basés sur l'atlas de texture
    // TODO: Ajouter les données de texture correctes (via VoxelTypeToTexture)
    int AddFace(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> triangles, System.Collections.Generic.List<Vector2> uvs, int vertexIndex, Vector3 position, Vector3 direction, Rect uvCoords) {
        Vector3[] faceVertices = GetFaceVertices(direction);
        int[] faceTriangles = { 0, 1, 2, 0, 2, 3 }; // Ordre standard pour un quad

        // Ajouter un petit offset pour éviter le texture bleeding
        float uvPadding = 0.001f;
        Rect paddedUV = new Rect(
            uvCoords.x + uvPadding,
            uvCoords.y + uvPadding,
            uvCoords.width - (uvPadding * 2),
            uvCoords.height - (uvPadding * 2)
        );

        for (int i = 0; i < 4; i++) {
            vertices.Add(position + faceVertices[i]);
            
            // Mapper correctement les UVs selon l'ordre des vertices
            Vector2 uv = Vector2.zero;
            switch(i) {
                case 0: uv = new Vector2(paddedUV.xMin, paddedUV.yMin); break; // Bas gauche
                case 1: uv = new Vector2(paddedUV.xMin, paddedUV.yMax); break; // Haut gauche
                case 2: uv = new Vector2(paddedUV.xMax, paddedUV.yMax); break; // Haut droite
                case 3: uv = new Vector2(paddedUV.xMax, paddedUV.yMin); break; // Bas droite
            }
            uvs.Add(uv);
        }

        for (int i = 0; i < 6; i++) {
            triangles.Add(vertexIndex + faceTriangles[i]);
        }

        return vertexIndex + 4;
    }

    // Placeholder pour obtenir les coordonnées UV d'un type de voxel
    // Devrait retourner un Rect(x, y, width, height) dans l'atlas 0..1
    Rect VoxelTypeToTexture(VoxelType type, Vector3 normal)
    {
        var props = VoxelProperties.Instance.GetPropertiesForType(type);
        
        // Configuration pour un atlas 256x256 avec des tiles 16x16
        const int ATLAS_SIZE = 256;
        const int TILE_SIZE = 16;
        const int TILES_PER_ROW = ATLAS_SIZE / TILE_SIZE; // = 16
        const float UV_TILE_SIZE = 1f / TILES_PER_ROW;    // = 0.0625f
        
        Vector2Int coords;
        if (normal == Vector3.up)
            coords = props.topTextureCoords;
        else if (normal == Vector3.down)
            coords = props.bottomTextureCoords;
        else
            coords = props.sideTextureCoords;

        // Calculer les coordonnées UV
        float u = coords.x * UV_TILE_SIZE;
        float v = 1f - ((coords.y + 1) * UV_TILE_SIZE); // Première ligne en haut

        Rect result = new Rect(u, v, UV_TILE_SIZE, UV_TILE_SIZE);
        
        //Debug.Log($"VoxelType: {type}, Normal: {normal}, Tile: {coords}, UV: {result}");
        
        return result;
    }

    // Placeholder pour mapper les coins du quad aux UVs
     Vector2 GetUVForVertex(int vertexIndex, Rect uvRect) {
         switch (vertexIndex) {
             case 0: return new Vector2(uvRect.xMin, uvRect.yMin);
             case 1: return new Vector2(uvRect.xMin, uvRect.yMax);
             case 2: return new Vector2(uvRect.xMax, uvRect.yMax);
             case 3: return new Vector2(uvRect.xMax, uvRect.yMin);
             default: return Vector2.zero;
         }
     }

    // Helper pour définir les 4 vertices d'une face en fonction de la direction
     Vector3[] GetFaceVertices(Vector3 direction) {
         // Ces vertices sont relatifs à la position du voxel (0,0,0) localement
         // Ex: Face +X (Droite)
         if (direction == Vector3.right) return new Vector3[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) };
         // Ex: Face -X (Gauche)
         if (direction == Vector3.left) return new Vector3[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) };
         // Ex: Face +Y (Haut)
         if (direction == Vector3.up) return new Vector3[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) };
         // Ex: Face -Y (Bas)
         if (direction == Vector3.down) return new Vector3[] { new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 0) };
         // Ex: Face +Z (Avant)
         if (direction == Vector3.forward) return new Vector3[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) };
         // Ex: Face -Z (Arrière)
         if (direction == Vector3.back) return new Vector3[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) };

         return new Vector3[4]; // Ne devrait pas arriver
     }
}
