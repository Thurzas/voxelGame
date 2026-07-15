// Fichier: Chunk.cs
using UnityEngine;
using Unity.Collections;
using VoxelGame.Data;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class Chunk : MonoBehaviour
{
    // --- Constantes ---
    // Chunk cubique (X=Y=Z) empilable dans les 3 axes, plutôt que l'ancienne colonne fixe
    // 32×256×32 — roadmap phase SVO sous-étape 2 : nécessaire pour un vrai streaming 3D et des
    // nœuds d'octree cubiques (le LOD par profondeur d'octree, sous-étape 3, suppose des nœuds
    // de même forme sur les 3 axes).
    public const int Size = 32;

    // --- Données ---
    public Vector3Int chunkPosition; // Position du chunk dans la grille de chunks (x, y, z).
                                     // La position réelle dans le monde est chunkPosition * Size.

    // Stockage par briques (VoxelBrick, phase 1 : VoxelGame.Data) au lieu d'un tableau
    // plat unique — prérequis pour le meshing GPU/job-parallélisable et le LOD par
    // profondeur d'octree (roadmap phase 4 sous-étape 2). Grille régulière fixe pour
    // l'instant (adressage direct, pas encore de VoxelOctree/hashmap : la sparsité et la
    // profondeur variable deviendront utiles au niveau monde, phase 5). Chaque brique
    // référence sa position par un code de Morton (VoxelGame.Data.MortonCode), ce qui
    // établit le même schéma d'adressage linéaire que le reste du projet.
    const int BrickSize = VoxelBrick.Size; // 16
    const int BricksX = Size / BrickSize;   // 2
    const int BricksY = Size / BrickSize;   // 2
    const int BricksZ = Size / BrickSize;   // 2
    const int BrickCount = BricksX * BricksY * BricksZ; // 8

    private VoxelBrick[] bricks;
    private bool bricksBuilt;

    static int BrickArrayIndex(int bx, int by, int bz) => bx + (by * BricksX) + (bz * BricksX * BricksY);

    static void ToBrickCoords(int x, int y, int z, out int bx, out int by, out int bz, out int wx, out int wy, out int wz)
    {
        bx = x / BrickSize; wx = x % BrickSize;
        by = y / BrickSize; wy = y % BrickSize;
        bz = z / BrickSize; wz = z % BrickSize;
    }

    // Tampon plat réutilisé pour le meshing (voir MaterializeScratch/GenerateMeshGreedy).
    // NativeArray applique une vérification de sécurité à CHAQUE accès individuel hors
    // d'un job Burst ; le balayage du masque du greedy meshing fait des millions de petits
    // accès, donc lire les briques une par une pendant ce balayage est plus lent qu'un
    // tableau plat classique (régression mesurée en jeu). On matérialise donc les briques
    // en un tableau plat en une seule passe avant de mesher, et le balayage relit ce
    // tableau (rapide, aucune vérification par accès) au lieu des briques directement.
    private VoxelType[] scratch;

    // Buffer réutilisé pour transférer une brique (dense ou à construire) en une seule
    // copie en bloc plutôt que 4096 appels Get()/Set() individuels — même raison que
    // ci-dessus, appliqué à VoxelBrick.CopyDenseTo/CopyFromDense.
    private Voxel[] brickReadBuffer;

    static int FlatIndex(int x, int y, int z) => x + (y * Size) + (z * Size * Size);

    // Plage Y (inclusive, LOCALE à ce chunk) contenant potentiellement des voxels solides.
    // Permet au meshing de sauter les portions d'air du chunk (utile même à Size=32 : un
    // chunk entièrement au-dessus du terrain ou entièrement sous terre reste courant une fois
    // le monde streamé verticalement) au lieu de parcourir les Size niveaux à chaque fois —
    // correctif d'une régression de perf mesurée à l'origine sur l'ancienne colonne 256 de haut
    // (cf. roadmap phase 4), toujours utile à cette granularité. Ne descend jamais en dessous
    // de la vraie plage (sûr : au pire un peu de travail inutile, jamais de face manquante),
    // mais ne rétrécit jamais non plus après une suppression de voxel (conservateur, pas exact).
    private int minSolidY = int.MaxValue;
    private int maxSolidY = int.MinValue;

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

    void OnDestroy()
    {
        // VoxelBrick peut posséder un NativeArray (état "dense") : à libérer explicitement,
        // sinon Unity signale une fuite mémoire native.
        DisposeBricks();
    }

    void DisposeBricks()
    {
        if (bricks == null)
        {
            return;
        }

        for (int i = 0; i < bricks.Length; i++)
        {
            bricks[i].Dispose();
        }

        bricks = null;
        bricksBuilt = false;
    }

    // Construit les briques à partir de "scratch" en une seule passe, une fois le
    // remplissage (terrain + décoration) terminé. Détermine l'homogénéité PENDANT la
    // lecture de scratch (tableau managé rapide) plutôt que d'allouer un NativeArray dense
    // par brique puis de le compacter après coup (l'ancienne approche relisait chaque
    // brique voxel par voxel via NativeArray pour la compaction — même coût que le
    // problème déjà corrigé pour le meshing, cf. roadmap phase 4). Les briques non
    // homogènes reçoivent leurs données en un seul NativeArray.CopyFrom (bloc), pas un
    // Set() par voxel.
    void BuildBricksFromScratch()
    {
        if (bricks == null)
        {
            bricks = new VoxelBrick[BrickCount];
        }
        if (brickReadBuffer == null)
        {
            brickReadBuffer = new Voxel[VoxelBrick.VoxelCount];
        }

        for (int bz = 0; bz < BricksZ; bz++)
        {
            for (int by = 0; by < BricksY; by++)
            {
                for (int bx = 0; bx < BricksX; bx++)
                {
                    int baseX = bx * BrickSize;
                    int baseY = by * BrickSize;
                    int baseZ = bz * BrickSize;

                    VoxelType first = scratch[FlatIndex(baseX, baseY, baseZ)];
                    bool uniform = true;
                    int localIdx = 0;
                    for (int dz = 0; dz < BrickSize; dz++)
                    {
                        for (int dy = 0; dy < BrickSize; dy++)
                        {
                            int idx = FlatIndex(baseX, baseY + dy, baseZ + dz);
                            for (int dx = 0; dx < BrickSize; dx++, localIdx++)
                            {
                                VoxelType t = scratch[idx + dx];
                                if (t != first)
                                {
                                    uniform = false;
                                }
                                brickReadBuffer[localIdx] = new Voxel(t);
                            }
                        }
                    }

                    int brickIdx = BrickArrayIndex(bx, by, bz);
                    bricks[brickIdx].Dispose(); // sûr même si jamais alloué (no-op)

                    if (uniform)
                    {
                        bricks[brickIdx] = VoxelBrick.CreateHomogeneous(new Voxel(first));
                    }
                    else
                    {
                        VoxelBrick brick = VoxelBrick.CreateDense(Allocator.Persistent);
                        brick.CopyFromDense(brickReadBuffer);
                        bricks[brickIdx] = brick;
                    }
                }
            }
        }

        bricksBuilt = true;
    }

    // Passé par World.LoadChunk. Ne demande plus la heightmap elle-même (contrairement à
    // avant) : une colonne XZ peut désormais être couverte par plusieurs chunks verticaux
    // (roadmap phase SVO sous-étape 2), donc la heightmap est demandée UNE fois par colonne et
    // partagée par World (RequestColumnHeightmap) plutôt que redemandée par chaque étage — sinon
    // on multiplierait par (Size monde / Size chunk) le nombre de dispatchs GPU de bruit pour un
    // résultat identique. World appelle OnHeightmapReady une fois la heightmap disponible.
    public void Initialize(Vector3Int position, Material material)
    {
        this.chunkPosition = position;
        this.transform.position = new Vector3(position.x * Size, position.y * Size, position.z * Size);
        this.name = $"Chunk ({position.x}, {position.y}, {position.z})";
        this.meshRenderer.material = material; // Assigner le matériel (atlas de textures)

        // scratch sert de stockage rapide pendant toute la génération (terrain +
        // décoration), avant que les briques ne soient construites (cf.
        // BuildBricksFromScratch, appelé à la fin de OnHeightmapReady).
        if (scratch == null)
        {
            scratch = new VoxelType[Size * Size * Size];
        }
    }

    // Callback (via World, cf. Initialize) une fois la heightmap partagée de la colonne
    // disponible — peut arriver plusieurs frames après Initialize.
    public void OnHeightmapReady(float[,] heightmap)
    {
        // Le chunk a pu être déchargé (GameObject détruit) pendant que la requête était
        // en vol : "this == null" est le test standard Unity pour un objet détruit.
        if (this == null) return;

        GenerateTerrain(heightmap);

        TerrainDecoration decorator = new TerrainDecoration();
        decorator.DecorateChunk(this);

        BuildBricksFromScratch();

        // Marquer pour la génération initiale du mesh
        needsMeshUpdate = true;
    }

    // Méthode pour obtenir/définir un voxel (coordonnées locales au chunk)
    public Voxel GetVoxel(int x, int y, int z)
    {
        if (IsVoxelInChunk(x, y, z))
        {
            if (!bricksBuilt)
            {
                // Encore en génération (avant BuildBricksFromScratch) : scratch est la
                // seule source de données à ce stade.
                return new Voxel(scratch[FlatIndex(x, y, z)]);
            }
            ToBrickCoords(x, y, z, out int bx, out int by, out int bz, out int wx, out int wy, out int wz);
            return bricks[BrickArrayIndex(bx, by, bz)].Get(wx, wy, wz);
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
            if (!bricksBuilt)
            {
                // Encore en génération (appelé par TerrainDecoration via SetVoxel, avant
                // que les briques n'existent) : écrit directement dans scratch. Garde le
                // même comportement que le chemin normal (détection de changement,
                // extension de bornes Y, notification des voisins) pour rester fidèle au
                // comportement d'origine, juste sans passer par les briques.
                int idx = FlatIndex(x, y, z);
                if (scratch[idx] != type)
                {
                    scratch[idx] = type;
                    if (type != VoxelType.Air)
                    {
                        if (y < minSolidY) minSolidY = y;
                        if (y > maxSolidY) maxSolidY = y;
                    }
                    CheckNeighborChunksForUpdate(x, y, z);
                }
                return;
            }

            ToBrickCoords(x, y, z, out int bx, out int by, out int bz, out int wx, out int wy, out int wz);
            int brickIdx = BrickArrayIndex(bx, by, bz);

            if (bricks[brickIdx].Get(wx, wy, wz).type != type) // Vérifier si le type change réellement
            {
                 // Une brique compactée (homogène) doit être ré-étendue avant d'écrire dedans
                 // (cf. VoxelBrick.Expand, phase 1).
                 if (bricks[brickIdx].IsHomogeneous)
                 {
                     bricks[brickIdx].Expand(Allocator.Persistent);
                 }
                 bricks[brickIdx].Set(wx, wy, wz, new Voxel(type));
                 needsMeshUpdate = true; // Le mesh doit être regénéré

                 // Étend la plage Y solide utilisée par le meshing (cf. minSolidY/maxSolidY) :
                 // reste sûr même après une suppression (ne rétrécit jamais la plage).
                 if (type != VoxelType.Air)
                 {
                     if (y < minSolidY) minSolidY = y;
                     if (y > maxSolidY) maxSolidY = y;
                 }

                 // OPTIMISATION: Si le bloc est à la frontière (x=0, x=Size-1, y=0, y=Size-1,
                 // z=0, z=Size-1) il faut aussi notifier le chunk voisin de potentiellement
                 // mettre à jour son mesh.
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
        return x >= 0 && x < Size && y >= 0 && y < Size && z >= 0 && z < Size;
    }

    // Convertit les coordonnées locales du chunk en coordonnées mondiales
    public Vector3Int GetWorldPosition(int x, int y, int z) {
        return new Vector3Int(
            x + chunkPosition.x * Size,
            y + chunkPosition.y * Size,
            z + chunkPosition.z * Size
        );
    }

     // Convertit les coordonnées mondiales en coordonnées locales du chunk
    public Vector3Int GetLocalPosition(Vector3Int worldPos) {
        return new Vector3Int(
            worldPos.x - chunkPosition.x * Size,
            worldPos.y - chunkPosition.y * Size,
            worldPos.z - chunkPosition.z * Size
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

    void GenerateTerrain(float[,] heightmap)
    {
        NoiseSettings settings = Noise.CurrentSettings;
        minSolidY = int.MaxValue;
        maxSolidY = int.MinValue;

        int chunkWorldY0 = chunkPosition.y * Size;
        // groundHeight est en coordonnées MONDE (fonction pure de la heightmap, indépendante
        // du chunk vertical qui traite cette colonne) — conservé ici pour être réutilisé tel
        // quel par la boucle de décoration ci-dessous, sans le redériver ni scanner les voxels
        // (cf. l'ancien GetSurfaceY, supprimé : il ne pouvait de toute façon donner un résultat
        // correct que dans LE chunk vertical contenant réellement la surface).
        int[,] groundHeights = new int[Size, Size];

        // --- Génération du terrain de base ---
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                float heightValue = heightmap[x, z];
                int groundHeight = Mathf.FloorToInt(settings.baseHeight + (heightValue - 0.5f) * settings.heightMultiplier);
                groundHeights[x, z] = groundHeight;

                // Écrit directement dans scratch (tableau plat rapide) : les briques
                // n'existent pas encore à ce stade (construites après coup en une passe,
                // cf. BuildBricksFromScratch) — évite des écritures NativeArray individuelles
                // pendant le remplissage initial (régression mesurée en jeu, cf. roadmap
                // phase 4).
                int idx = FlatIndex(x, 0, z);
                for (int localY = 0; localY < Size; localY++, idx += Size)
                {
                    int worldY = chunkWorldY0 + localY;
                    VoxelType type;
                    if (worldY < groundHeight - 3)
                        type = VoxelType.Stone;
                    else if (worldY < groundHeight)
                        type = VoxelType.Dirt;
                    else if (worldY == groundHeight)
                        type = VoxelType.Grass;
                    else
                        type = VoxelType.Air;

                    scratch[idx] = type;
                    if (type != VoxelType.Air)
                    {
                        if (localY < minSolidY) minSolidY = localY;
                        if (localY > maxSolidY) maxSolidY = localY;
                    }
                }
            }
        }

        // --- Génération déterministe des décorations (arbres) ---
        // Le hash/la décision "y a-t-il un arbre ici" ne dépendent que de (worldX, worldZ,
        // worldSeed) — identiques quel que soit le chunk vertical qui traite cette colonne, donc
        // chaque étage retombe sur le même résultat. Seul WriteVoxelIfInChunk décide au final
        // quels voxels de l'arbre tombent dans LA plage Y locale de CE chunk (même logique de
        // troncature déjà utilisée pour les frontières X/Z, désormais étendue à Y).
        int worldSeed = World.Instance.worldSeed;
        for (int x = 0; x < Size; x++)
        {
            for (int z = 0; z < Size; z++)
            {
                int worldX = chunkPosition.x * Size + x;
                int worldZ = chunkPosition.z * Size + z;
                int hash = worldX * 73856093 ^ worldZ * 19349663 ^ worldSeed;
                Random.InitState(hash);
                if (Random.value < 0.05f) // 5% de chance de générer un arbre
                {
                    PlaceTree(worldX, groundHeights[x, z], worldZ);
                }
            }
        }
    }

    // Place un arbre déterministe (y = coordonnée MONDE de la base), en écrivant uniquement
    // les voxels qui tombent dans le chunk courant (X, Y et Z).
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

    // N'écrit que si la position (coordonnées MONDE) est dans le chunk courant
    void WriteVoxelIfInChunk(int wx, int y, int wz, VoxelType type)
    {
        int localX = wx - chunkPosition.x * Size;
        int localY = y - chunkPosition.y * Size;
        int localZ = wz - chunkPosition.z * Size;
        if (localX >= 0 && localX < Size && localY >= 0 && localY < Size && localZ >= 0 && localZ < Size)
        {
            // Appelé pendant la génération initiale (avant BuildBricksFromScratch) :
            // écrit directement dans scratch.
            scratch[FlatIndex(localX, localY, localZ)] = type;

            // Étend les bornes Y ici aussi (n'écrit pas via SetVoxel), sinon une partie
            // d'arbre au-dessus de maxSolidY serait coupée par le bornage du meshing
            // (cf. minSolidY/maxSolidY).
            if (type != VoxelType.Air)
            {
                if (localY < minSolidY) minSolidY = localY;
                if (localY > maxSolidY) maxSolidY = localY;
            }
        }
    }

    void CheckNeighborChunksForUpdate(int x, int y, int z)
    {
        if (World.Instance == null) return; // S'assurer que le monde existe

        // Si à la limite X=0, notifier le chunk voisin en -X
        if (x == 0) World.Instance.GetChunk(chunkPosition + Vector3Int.left)?.MarkForMeshUpdate();
        // Si à la limite X=Size-1, notifier le chunk voisin en +X
        else if (x == Size - 1) World.Instance.GetChunk(chunkPosition + Vector3Int.right)?.MarkForMeshUpdate();

        // Si à la limite Z=0, notifier le chunk voisin en -Z
        if (z == 0) World.Instance.GetChunk(chunkPosition + new Vector3Int(0,0,-1))?.MarkForMeshUpdate();
        // Si à la limite Z=Size-1, notifier le chunk voisin en +Z
        else if (z == Size - 1) World.Instance.GetChunk(chunkPosition + new Vector3Int(0,0,1))?.MarkForMeshUpdate();

        // Si à la limite Y=0, notifier le chunk voisin en -Y ; idem Y=Size-1 en +Y — les chunks
        // sont désormais cubiques et empilables verticalement (roadmap phase SVO sous-étape 2),
        // ce cas n'était plus à ignorer.
        if (y == 0) World.Instance.GetChunk(chunkPosition + new Vector3Int(0,-1,0))?.MarkForMeshUpdate();
        else if (y == Size - 1) World.Instance.GetChunk(chunkPosition + new Vector3Int(0,1,0))?.MarkForMeshUpdate();
    }

     public void MarkForMeshUpdate() {
        needsMeshUpdate = true;
    }


    // --- Logique de Mesh ---
    void GenerateMeshWithShader()
    {
        // GenerateMeshGreedy() (CPU) reste dans le fichier comme référence/repli — profilage
        // en jeu ayant montré 99% CPU / 1% GPU, le culling de faces est déplacé sur un
        // compute shader (VoxelFaceCulling.compute / VoxelMesherGpu) pour paralléliser le
        // gros du travail. Pas de fusion greedy côté GPU pour cette étape (voir décision
        // avec l'utilisateur) : chaque face exposée devient un quad 1x1 indépendant, en
        // réutilisant AddGreedyFace avec width=height=1.
        GenerateMeshGpu();
    }

    // Buffer aplati et paddé (1 voxel de bordure sur les 3 axes, cf. VoxelFaceCulling.compute)
    // envoyé au compute shader ; réutilisé d'un remaillage à l'autre pour éviter une
    // réallocation à chaque frame où un chunk change.
    private uint[] gpuPaddedBuffer;

    void GenerateMeshGpu()
    {
        if (minSolidY > maxSolidY)
        {
            AssignMesh(new System.Collections.Generic.List<Vector3>(), new System.Collections.Generic.List<int>(), new System.Collections.Generic.List<Vector2>());
            return;
        }

        // scratch doit refléter l'état courant des briques (un SetVoxel après la
        // génération initiale ne modifie que les briques, pas scratch) — même prérequis
        // que GenerateMeshGreedy.
        MaterializeScratch();

        int loY = Mathf.Clamp(minSolidY - 1, 0, Size - 1);
        int hiYExclusive = Mathf.Clamp(maxSolidY + 2, 0, Size);
        int innerHeight = hiYExclusive - loY;

        int paddedWidth = Size + 2;
        int paddedDepth = Size + 2;
        int paddedHeightStride = innerHeight + 2; // padding réel sur Y désormais (voisin vertical possible)
        int paddedLength = paddedWidth * paddedHeightStride * paddedDepth;

        if (gpuPaddedBuffer == null || gpuPaddedBuffer.Length < paddedLength)
        {
            gpuPaddedBuffer = new uint[paddedLength];
        }

        // Intérieur : copie en bloc depuis scratch (une ligne X à la fois). py décalé de +1
        // (padding Y, comme px/pz le sont déjà de +1 pour le padding X/Z).
        for (int pz = 1; pz <= Size; pz++)
        {
            int z = pz - 1;
            for (int py = 1; py <= innerHeight; py++)
            {
                int y = (py - 1) + loY;
                int scratchRowBase = FlatIndex(0, y, z);
                int paddedRowBase = 1 + (py * paddedWidth) + (pz * paddedWidth * paddedHeightStride);
                for (int px = 0; px < Size; px++)
                {
                    gpuPaddedBuffer[paddedRowBase + px] = (uint)scratch[scratchRowBase + px];
                }
            }
        }

        // Bordures X (px=0 et px=paddedWidth-1) : seule la solidité importe (jamais utilisées
        // comme "self", cf. VoxelFaceCulling.compute), interrogée via le voisin/World.
        for (int pz = 1; pz <= Size; pz++)
        {
            int z = pz - 1;
            for (int py = 1; py <= innerHeight; py++)
            {
                int y = (py - 1) + loY;
                bool solidLeft = GetVoxelInfoFast(-1, y, z, out _);
                bool solidRight = GetVoxelInfoFast(Size, y, z, out _);
                int rowBase = (py * paddedWidth) + (pz * paddedWidth * paddedHeightStride);
                gpuPaddedBuffer[rowBase] = solidLeft ? 1u : 0u;
                gpuPaddedBuffer[rowBase + paddedWidth - 1] = solidRight ? 1u : 0u;
            }
        }

        // Bordures Z (pz=0 et pz=paddedDepth-1), coins X inclus (jamais utilisés comme
        // voisin par un thread, mais autant les remplir correctement).
        for (int px = 0; px < paddedWidth; px++)
        {
            int x = px - 1;
            for (int py = 1; py <= innerHeight; py++)
            {
                int y = (py - 1) + loY;
                bool solidFront = GetVoxelInfoFast(x, y, -1, out _);
                bool solidBack = GetVoxelInfoFast(x, y, Size, out _);
                int frontBase = px + (py * paddedWidth);
                int backBase = px + (py * paddedWidth) + ((paddedDepth - 1) * paddedWidth * paddedHeightStride);
                gpuPaddedBuffer[frontBase] = solidFront ? 1u : 0u;
                gpuPaddedBuffer[backBase] = solidBack ? 1u : 0u;
            }
        }

        // Bordures Y (py=0 et py=paddedHeightStride-1) : voisin vertical (chunk au-dessus/en
        // dessous) si loY/hiYExclusive atteignent le vrai bord du chunk, sinon toujours de
        // l'air garanti par construction (cf. minSolidY/maxSolidY) — GetVoxelInfoFast gère les
        // deux cas uniformément, exactement comme pour X/Z.
        for (int pz = 0; pz < paddedDepth; pz++)
        {
            int z = pz - 1;
            for (int px = 0; px < paddedWidth; px++)
            {
                int x = px - 1;
                bool solidBelow = GetVoxelInfoFast(x, loY - 1, z, out _);
                bool solidAbove = GetVoxelInfoFast(x, hiYExclusive, z, out _);
                int belowBase = px + (0 * paddedWidth) + (pz * paddedWidth * paddedHeightStride);
                int aboveBase = px + ((paddedHeightStride - 1) * paddedWidth) + (pz * paddedWidth * paddedHeightStride);
                gpuPaddedBuffer[belowBase] = solidBelow ? 1u : 0u;
                gpuPaddedBuffer[aboveBase] = solidAbove ? 1u : 0u;
            }
        }

        int capturedLoY = loY;
        VoxelMesherGpu.RequestFaces(gpuPaddedBuffer, paddedLength, Size, innerHeight, Size, paddedWidth, paddedHeightStride, capturedLoY, OnGpuFacesReady);
    }

    // Callback du readback GPU (peut arriver plusieurs frames après GenerateMeshGpu).
    // Reconstruit le mesh à partir de la liste de faces exposées, en réutilisant
    // AddGreedyFace avec width=height=1 (pas de fusion côté GPU pour cette étape).
    void OnGpuFacesReady(VoxelMesherGpu.Face[] faces, int count)
    {
        if (this == null) return;

        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        int vertexIndex = 0;

        for (int i = 0; i < count; i++)
        {
            VoxelMesherGpu.Face f = faces[i];
            int axis = f.Direction / 2;
            bool back = (f.Direction & 1) != 0;

            // Le voxel plein occupe [pos, pos+1[ sur chaque axe : la face -axis est au plan
            // "pos", la face +axis au plan "pos+1" (dérivation identique à celle utilisée
            // par GenerateMeshGreedy pour basePos3[axis], cf. commentaire d'AddGreedyFace).
            Vector3Int basePos = new Vector3Int(f.X, f.Y, f.Z);
            if (!back)
            {
                switch (axis)
                {
                    case 0: basePos.x += 1; break;
                    case 1: basePos.y += 1; break;
                    default: basePos.z += 1; break;
                }
            }

            Rect uvRect = VoxelTypeToTexture(f.Type, DirectionVector(axis, back));
            vertexIndex = AddGreedyFace(vertices, triangles, uvs, vertexIndex, basePos, axis, back, 1, 1, uvRect);
        }

        AssignMesh(vertices, triangles, uvs);
    }

    // Greedy meshing : fusionne les faces adjacentes de même type/direction en quads plus
    // grands au lieu d'un quad par face de voxel visible (algorithme validé hors-Unity
    // avant portage, cf. roadmap phase 4). Réduit drastiquement le nombre de triangles
    // pour un terrain avec de grandes zones plates/homogènes (~80% de réduction observée
    // sur un terrain de test lors de la validation).
    //
    // Compromis assumé pour cette étape : les UVs de chaque quad fusionné réutilisent la
    // même tuile d'atlas unique que l'ancien code, étirée sur toute la surface fusionnée
    // (pas de tiling répété) — un étirement de texture est donc possible sur de grandes
    // zones. Correction (UV wrap/triplanaire) laissée pour un polish ultérieur, la
    // priorité de cette phase étant la réduction du nombre de triangles.
    //
    // Non appelée depuis le passage au meshing GPU (cf. GenerateMeshWithShader) ; conservée
    // comme référence/repli. Fonctionne nativement avec des chunks cubiques empilables : tous
    // les axes (y compris Y) délèguent déjà au World via GetVoxelInfoFast pour les positions
    // hors chunk, donc les voisins verticaux sont gérés sans changement de cette méthode.
    void GenerateMeshGreedy()
    {
        var vertices = new System.Collections.Generic.List<Vector3>();
        var triangles = new System.Collections.Generic.List<int>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        int vertexIndex = 0;

        // Aucun voxel solide (chunk pas encore rempli, ou entièrement air) : rien à mesher.
        if (minSolidY > maxSolidY)
        {
            AssignMesh(vertices, triangles, uvs);
            return;
        }

        int loY = Mathf.Clamp(minSolidY - 1, 0, Size - 1);
        int hiYExclusive = Mathf.Clamp(maxSolidY + 2, 0, Size);

        int[] loBound = { 0, loY, 0 };
        int[] hiBoundExcl = { Size, hiYExclusive, Size };

        MaterializeScratch();

        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3;
            int v = (axis + 2) % 3;
            int dimU = hiBoundExcl[u] - loBound[u];
            int dimV = hiBoundExcl[v] - loBound[v];
            int axisLo = loBound[axis];
            int axisHiExclusive = hiBoundExcl[axis];

            // mask[n] == 0 signifie "pas de face" ; sinon Encode(type, back).
            int[] mask = new int[dimU * dimV];
            int[] x = new int[3];

            for (x[axis] = axisLo - 1; x[axis] < axisHiExclusive;)
            {
                int n = 0;
                for (int jj = 0; jj < dimV; jj++)
                {
                    x[v] = loBound[v] + jj;
                    for (int ii = 0; ii < dimU; ii++, n++)
                    {
                        x[u] = loBound[u] + ii;

                        bool aSolid = GetVoxelInfoFast(x[0], x[1], x[2], out bool aInChunk);

                        int[] xb = { x[0], x[1], x[2] };
                        xb[axis] += 1;
                        bool bSolid = GetVoxelInfoFast(xb[0], xb[1], xb[2], out bool bInChunk);

                        if (aSolid == bSolid)
                        {
                            // Les deux solides (intérieur) ou les deux vides : pas de face.
                            mask[n] = 0;
                        }
                        else if (aSolid)
                        {
                            // Face côté +axis, appartient à 'a'. On ne la dessine que si
                            // 'a' est dans CE chunk (sinon elle appartient au voisin).
                            mask[n] = aInChunk ? Encode(scratch[FlatIndex(x[0], x[1], x[2])], false) : 0;
                        }
                        else
                        {
                            // Face côté -axis, appartient à 'b'.
                            mask[n] = bInChunk ? Encode(scratch[FlatIndex(xb[0], xb[1], xb[2])], true) : 0;
                        }
                    }
                }

                x[axis]++;

                n = 0;
                for (int j = 0; j < dimV; j++)
                {
                    for (int i = 0; i < dimU;)
                    {
                        int cur = mask[n];
                        if (cur != 0)
                        {
                            int w = 1;
                            while (i + w < dimU && mask[n + w] == cur) w++;

                            int hgt = 1;
                            bool done = false;
                            while (j + hgt < dimV)
                            {
                                for (int k = 0; k < w; k++)
                                {
                                    if (mask[n + k + (hgt * dimU)] != cur) { done = true; break; }
                                }
                                if (done) break;
                                hgt++;
                            }

                            int[] basePos3 = new int[3];
                            basePos3[axis] = x[axis];
                            basePos3[u] = loBound[u] + i;
                            basePos3[v] = loBound[v] + j;

                            (VoxelType type, bool back) = Decode(cur);
                            Vector3Int basePos = new Vector3Int(basePos3[0], basePos3[1], basePos3[2]);
                            Rect uvRect = VoxelTypeToTexture(type, DirectionVector(axis, back));
                            vertexIndex = AddGreedyFace(vertices, triangles, uvs, vertexIndex, basePos, axis, back, w, hgt, uvRect);

                            for (int l = 0; l < hgt; l++)
                            {
                                for (int k = 0; k < w; k++)
                                {
                                    mask[n + k + (l * dimU)] = 0;
                                }
                            }

                            i += w;
                            n += w;
                        }
                        else
                        {
                            i++;
                            n++;
                        }
                    }
                }

            }
        }

        AssignMesh(vertices, triangles, uvs);
    }

    void AssignMesh(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> triangles, System.Collections.Generic.List<Vector2> uvs)
    {

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
        // Mesh.Optimize() volontairement retiré : coûteux (réordonnancement du buffer de
        // vertices) pour un bénéfice marginal sur le matériel actuel, et régénéré à
        // chaque frame où un chunk change — contribuait aux freezes constatés (roadmap
        // phase 3/4).

        meshFilter.mesh = generatedMesh;
        meshCollider.sharedMesh = generatedMesh; // Mettre à jour le collider physique
    }

    // Recopie les briques dans "scratch" (tableau plat) en une seule passe avant le
    // balayage du masque de greedy meshing (nécessaire pour les remaillages après
    // édition d'un voxel via SetVoxel, qui modifie les briques mais pas scratch — pour
    // la génération initiale, scratch est déjà à jour, cf. BuildBricksFromScratch). Les
    // briques homogènes se remplissent par simples écritures de tableau, sans le moindre
    // accès NativeArray.
    void MaterializeScratch()
    {
        if (scratch == null)
        {
            scratch = new VoxelType[Size * Size * Size];
        }
        if (brickReadBuffer == null)
        {
            brickReadBuffer = new Voxel[VoxelBrick.VoxelCount];
        }

        for (int bz = 0; bz < BricksZ; bz++)
        {
            for (int by = 0; by < BricksY; by++)
            {
                for (int bx = 0; bx < BricksX; bx++)
                {
                    ref VoxelBrick brick = ref bricks[BrickArrayIndex(bx, by, bz)];
                    int baseX = bx * BrickSize;
                    int baseY = by * BrickSize;
                    int baseZ = bz * BrickSize;

                    if (brick.IsHomogeneous)
                    {
                        VoxelType t = brick.HomogeneousValue.type;
                        for (int dz = 0; dz < BrickSize; dz++)
                        {
                            for (int dy = 0; dy < BrickSize; dy++)
                            {
                                int idx = FlatIndex(baseX, baseY + dy, baseZ + dz);
                                for (int dx = 0; dx < BrickSize; dx++)
                                {
                                    scratch[idx + dx] = t;
                                }
                            }
                        }
                    }
                    else
                    {
                        brick.CopyDenseTo(brickReadBuffer);
                        int localIdx = 0;
                        for (int dz = 0; dz < BrickSize; dz++)
                        {
                            for (int dy = 0; dy < BrickSize; dy++)
                            {
                                int idx = FlatIndex(baseX, baseY + dy, baseZ + dz);
                                for (int dx = 0; dx < BrickSize; dx++, localIdx++)
                                {
                                    scratch[idx + dx] = brickReadBuffer[localIdx].type;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Lit depuis "scratch" (rapide, pas de vérification par accès) pour les positions dans
    // ce chunk ; délègue au World pour les positions hors chunk (X, Y ou Z — les chunks sont
    // désormais cubiques et empilables dans les 3 axes, roadmap phase SVO sous-étape 2).
    bool GetVoxelInfoFast(int x, int y, int z, out bool inChunk)
    {
        if (IsVoxelInChunk(x, y, z))
        {
            inChunk = true;
            return scratch[FlatIndex(x, y, z)] != VoxelType.Air;
        }

        inChunk = false;
        if (World.Instance != null)
        {
            return World.Instance.IsVoxelSolid(GetWorldPosition(x, y, z));
        }
        return false;
    }

    // Encode/decode un (VoxelType, direction) dans le masque de greedy meshing.
    static int Encode(VoxelType type, bool back) => (((int)type << 1) | (back ? 1 : 0)) + 1;

    static (VoxelType type, bool back) Decode(int code)
    {
        code -= 1;
        return ((VoxelType)(code >> 1), (code & 1) != 0);
    }

    static Vector3Int AxisVector(int axis)
    {
        switch (axis)
        {
            case 0: return new Vector3Int(1, 0, 0);
            case 1: return new Vector3Int(0, 1, 0);
            default: return new Vector3Int(0, 0, 1);
        }
    }

    static Vector3 DirectionVector(int axis, bool back)
    {
        Vector3 a = AxisVector(axis);
        return back ? -a : a;
    }

    // Ajoute un quad fusionné (largeur x hauteur, au lieu d'un quad 1x1 par voxel) au mesh.
    // basePos est déjà positionné sur le plan de la face (cf. dérivation dans la roadmap
    // phase 4) ; l'ordre des 4 coins est choisi pour conserver à la fois un winding
    // cohérent (normale sortante correcte, vérifié par équivalence avec l'ancien
    // GetFaceVertices avant portage) ET le même coin de départ (donc la même orientation
    // de texture) que l'ancien code par direction. Pour les axes X/Y (0/1) le coin de
    // départ générique coïncide déjà avec l'ancien ; pour l'axe Z (2), l'ancien code
    // démarrait le cycle un cran plus loin — sans ce décalage la texture apparaît pivotée
    // à 90° sur les faces avant/arrière (bug constaté en jeu sur les côtés terre/bois).
    int AddGreedyFace(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<int> triangles, System.Collections.Generic.List<Vector2> uvs, int vertexIndex, Vector3Int basePos, int axis, bool back, int width, int height, Rect uvRect)
    {
        Vector3Int uVec = AxisVector((axis + 1) % 3);
        Vector3Int vVec = AxisVector((axis + 2) % 3);

        Vector3 p00 = basePos;
        Vector3 p10 = basePos + (uVec * width);
        Vector3 p11 = basePos + (uVec * width) + (vVec * height);
        Vector3 p01 = basePos + (vVec * height);

        Vector3[] corners;
        if (axis == 2)
        {
            corners = back
                ? new[] { p00, p01, p11, p10 }
                : new[] { p10, p11, p01, p00 };
        }
        else
        {
            corners = back
                ? new[] { p01, p11, p10, p00 }
                : new[] { p00, p10, p11, p01 };
        }

        float uvPadding = 0.001f;
        Rect paddedUV = new Rect(
            uvRect.x + uvPadding,
            uvRect.y + uvPadding,
            uvRect.width - (uvPadding * 2),
            uvRect.height - (uvPadding * 2)
        );

        for (int i = 0; i < 4; i++)
        {
            vertices.Add(corners[i]);
            Vector2 uv;
            switch (i)
            {
                case 0: uv = new Vector2(paddedUV.xMin, paddedUV.yMin); break;
                case 1: uv = new Vector2(paddedUV.xMin, paddedUV.yMax); break;
                case 2: uv = new Vector2(paddedUV.xMax, paddedUV.yMax); break;
                default: uv = new Vector2(paddedUV.xMax, paddedUV.yMin); break;
            }
            uvs.Add(uv);
        }

        int[] faceTriangles = { 0, 1, 2, 0, 2, 3 };
        for (int i = 0; i < 6; i++)
        {
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

        return result;
    }

}
