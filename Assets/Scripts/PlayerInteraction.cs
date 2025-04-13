// Fichier: PlayerInteraction.cs (attaché au joueur)
using UnityEngine;

public class PlayerInteraction : MonoBehaviour
{
    public float reach = 5f; // Portée du joueur
    public Camera playerCamera; // Référence à la caméra FPS
    public GameObject blockHighlightPrefab; // Un prefab simple pour montrer quel bloc est ciblé (ex: un cube filaire)
    public VoxelType selectedBlockType = VoxelType.Dirt; // Le type de bloc à placer

    private GameObject currentHighlight;
    private Vector3Int lastHitBlockPos;
    private bool isHighlightVisible = false;

    void Start()
    {
        if (playerCamera == null)
        {
            // Cherche la caméra dans les enfants du FPSController
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null)
                Debug.LogError("No camera found for PlayerInteraction!");
        }

        if (playerCamera == null) playerCamera = Camera.main;
        
        if (blockHighlightPrefab != null)
        {
             currentHighlight = Instantiate(blockHighlightPrefab);
             currentHighlight.SetActive(false);
        }
    }

    void Update()
    {
        HandleHighlight();
        HandleInteraction();
    }

    void HandleHighlight()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, reach))
        {
            // Position du bloc visé (légèrement à l'intérieur de la face touchée)
            Vector3 pointInBlock = hit.point - hit.normal * 0.01f;
            Vector3Int blockPos = Vector3Int.FloorToInt(pointInBlock);

             // Si on vise un nouveau bloc, mettre à jour le highlight
             if (!isHighlightVisible || blockPos != lastHitBlockPos)
             {
                 if (currentHighlight != null)
                 {
                     // Positionner le highlight au centre du voxel visé
                     currentHighlight.transform.position = blockPos + new Vector3(0.5f, 0.5f, 0.5f);
                     currentHighlight.SetActive(true);
                 }
                 lastHitBlockPos = blockPos;
                 isHighlightVisible = true;
             }
        }
        else // Si on ne vise rien
        {
            if (isHighlightVisible && currentHighlight != null)
            {
                currentHighlight.SetActive(false);
                isHighlightVisible = false;
            }
        }
    }


    void HandleInteraction()
    {
        // Clic Gauche: Casser
        if (Input.GetMouseButtonDown(0))
        {
            TryBreakBlock();
        }

        // Clic Droit: Poser
        if (Input.GetMouseButtonDown(1))
        {
            TryPlaceBlock();
        }

         // Changer le bloc sélectionné (exemple simple avec les touches numériques)
         if (Input.GetKeyDown(KeyCode.Alpha1)) selectedBlockType = VoxelType.Stone;
         if (Input.GetKeyDown(KeyCode.Alpha2)) selectedBlockType = VoxelType.Dirt;
         if (Input.GetKeyDown(KeyCode.Alpha3)) selectedBlockType = VoxelType.Grass;
         if (Input.GetKeyDown(KeyCode.Alpha4)) selectedBlockType = VoxelType.Wood;
         if (Input.GetKeyDown(KeyCode.Alpha5)) selectedBlockType = VoxelType.Leaves;
    }


    void TryBreakBlock()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, reach))
        {
            Vector3 pointInBlock = hit.point - hit.normal * 0.01f;
            Vector3Int blockPos = Vector3Int.FloorToInt(pointInBlock);

            Debug.Log($"Trying to break block at {blockPos}");
            World.Instance?.SetVoxel(blockPos, VoxelType.Air); // Demander au monde de supprimer le bloc
        }
    }

    void TryPlaceBlock()
    {
        Ray ray = playerCamera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f));
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, reach))
        {
            // Position où placer le nouveau bloc (adjacent au bloc touché, sur la face touchée)
            Vector3 pointOutsideBlock = hit.point + hit.normal * 0.01f;
            Vector3Int placePos = Vector3Int.FloorToInt(pointOutsideBlock);

            // --- Vérification anti-collision simple (Optionnel mais recommandé) ---
            // Créer un AABB (Axis-Aligned Bounding Box) pour la position du nouveau bloc
            Bounds placeBounds = new Bounds(placePos + new Vector3(0.5f, 0.5f, 0.5f), Vector3.one * 0.9f); // Légèrement plus petit qu'un bloc

            // Obtenir le collider du joueur (ou une référence à celui-ci)
            Collider playerCollider = GetComponent<Collider>(); // Assurez-vous que le joueur a un collider

            // Vérifier si la zone de placement chevauche le collider du joueur
            if (playerCollider != null && placeBounds.Intersects(playerCollider.bounds))
            {
                 Debug.Log("Cannot place block inside player.");
                 return; // Ne pas placer le bloc si le joueur est à l'intérieur
            }
            //---------------------------------------------------------------------


            Debug.Log($"Trying to place block at {placePos}");
            World.Instance?.SetVoxel(placePos, selectedBlockType); // Demander au monde de placer le bloc
        }
    }
}