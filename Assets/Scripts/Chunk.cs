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

        // TODO: Remplir voxelData avec la génération procédurale initiale
        GenerateTerrain(); // Exemple simple

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


    // --- Méthodes de génération (exemples) ---

    void GenerateTerrain() // Placeholder très basique
    {
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Depth; z++)
            {
                // Calculer la position mondiale pour le bruit Perlin
                float worldX = x + chunkPosition.x * Width;
                float worldZ = z + chunkPosition.z * Depth;

                // Utiliser le bruit Perlin pour déterminer la hauteur du sol
                int groundHeight = Mathf.FloorToInt(Mathf.PerlinNoise(worldX * 0.05f, worldZ * 0.05f) * 15) + 64; // Hauteur de base + variation

                for (int y = 0; y < Height; y++)
                {
                    if (y < groundHeight - 3)
                    {
                        voxelData[x, y, z] = new Voxel(VoxelType.Stone);
                    }
                    else if (y < groundHeight)
                    {
                        voxelData[x, y, z] = new Voxel(VoxelType.Dirt);
                    }
                    else if (y == groundHeight)
                    {
                        voxelData[x, y, z] = new Voxel(VoxelType.Grass);
                    }
                    else
                    {
                        voxelData[x, y, z] = new Voxel(VoxelType.Air); // Air au-dessus
                    }
                }
            }
        }
         needsMeshUpdate = true; // Marquer pour la génération de mesh après la génération du terrain
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
                        if (!IsVoxelSolid(x + 1, y, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.right, VoxelTypeToTexture(voxelData[x, y, z].type));
                        // Face -X (Gauche)
                        if (!IsVoxelSolid(x - 1, y, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.left, VoxelTypeToTexture(voxelData[x, y, z].type));
                        // Face +Y (Haut)
                        if (!IsVoxelSolid(x, y + 1, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.up, VoxelTypeToTexture(voxelData[x, y, z].type));
                        // Face -Y (Bas)
                        if (!IsVoxelSolid(x, y - 1, z)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.down, VoxelTypeToTexture(voxelData[x, y, z].type));
                        // Face +Z (Avant)
                        if (!IsVoxelSolid(x, y, z + 1)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.forward, VoxelTypeToTexture(voxelData[x, y, z].type));
                        // Face -Z (Arrière)
                        if (!IsVoxelSolid(x, y, z - 1)) vertexIndex = AddFace(vertices, triangles, uvs, vertexIndex, pos, Vector3.back, VoxelTypeToTexture(voxelData[x, y, z].type));
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
        // Simplifié: utilise des vertices prédéfinis pour chaque direction
        // Il faudrait une structure de données plus propre pour ça.
        Vector3[] faceVertices = GetFaceVertices(direction);
        int[] faceTriangles = { 0, 1, 2, 0, 2, 3 }; // Ordre standard pour un quad

        for (int i = 0; i < 4; i++) {
             vertices.Add(position + faceVertices[i]);
             // TODO: Calculer les vrais UVs basés sur uvCoords
             uvs.Add(GetUVForVertex(i, uvCoords)); // Placeholder UV calculation
        }

        for (int i = 0; i < 6; i++) {
             triangles.Add(vertexIndex + faceTriangles[i]);
        }

        return vertexIndex + 4;
    }

    // Placeholder pour obtenir les coordonnées UV d'un type de voxel
    // Devrait retourner un Rect(x, y, width, height) dans l'atlas 0..1
    Rect VoxelTypeToTexture(VoxelType type) {
        // Exemple TRES simplifié, à remplacer par une vraie logique d'atlas
        float textureSize = 1f / 16f; // Si atlas 16x16 textures
        switch (type) {
            case VoxelType.Grass: return new Rect(0 * textureSize, 15 * textureSize, textureSize, textureSize); // Coordonnées exemple
            case VoxelType.Dirt: return new Rect(2 * textureSize, 15 * textureSize, textureSize, textureSize);
            case VoxelType.Stone: return new Rect(1 * textureSize, 15 * textureSize, textureSize, textureSize);
            case VoxelType.Wood: return new Rect(4*textureSize, 14*textureSize, textureSize, textureSize);
            case VoxelType.Leaves: return new Rect(4*textureSize, 12*textureSize, textureSize, textureSize);
            default: return new Rect(15 * textureSize, 0 * textureSize, textureSize, textureSize); // Texture "missing"
        }
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