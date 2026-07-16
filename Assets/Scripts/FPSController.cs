using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float jumpForce = 5f;
    [SerializeField] private float gravity = -9.81f;

    [Header("Look Settings")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float maxLookAngle = 90f;
    [SerializeField] private bool invertY = false;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Téléportation / grand déplacement")]
    // Un déplacement d'un frame à l'autre au-delà de ce seuil est considéré comme un
    // téléport (une téléportation future, un spawn, une correction de position...) plutôt
    // qu'un mouvement normal — même en sprint, on ne parcourt jamais des centaines d'unités
    // en un seul frame. Le joueur est alors gelé (cf. isFrozen) le temps que le terrain de
    // destination ait fini de charger, pour éviter de tomber à travers le décor.
    [SerializeField] private float teleportDistanceThreshold = 100f;
    // Durée maximale de gel : garde-fou pour ne jamais bloquer le joueur indéfiniment si le
    // terrain de destination ne finit jamais de charger pour une raison quelconque.
    [SerializeField] private float maxFreezeDuration = 8f;

    // Components
    private CharacterController characterController;
    private PlayerInput playerInput;

    // Movement
    private Vector2 moveInput;
    private Vector2 lookInput;
    private Vector3 currentMovement;
    private Vector3 velocity;
    private bool isRunning;
    private bool jumpPressed;

    // Camera
    private float cameraPitch;

    // Gel après un grand déplacement (cf. teleportDistanceThreshold ci-dessus)
    private Vector3 previousPosition;
    private bool isFrozen;
    private float frozenSince;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        // Lock and hide cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (cameraTransform == null)
        {
            Camera mainCamera = GetComponentInChildren<Camera>();
            if (mainCamera != null)
                cameraTransform = mainCamera.transform;
            else
                Debug.LogError("No camera found for FPSController!");
        }

        // Initialisé à la position de spawn (pas Vector3.zero) : on ne veut détecter que les
        // grands déplacements EN COURS DE JEU, pas geler systématiquement au premier frame si
        // le point de spawn est loin de l'origine du monde.
        previousPosition = transform.position;
    }

    private void Update()
    {
        DetectTeleportAndFreeze();

        if (isFrozen)
        {
            TryUnfreeze();
        }
        else
        {
            HandleMovement();
        }

        HandleLook();
        previousPosition = transform.position;
    }

    private void DetectTeleportAndFreeze()
    {
        if (isFrozen)
        {
            return; // déjà gelé, pas besoin de re-détecter
        }

        float distanceSq = (transform.position - previousPosition).sqrMagnitude;
        if (distanceSq > teleportDistanceThreshold * teleportDistanceThreshold)
        {
            isFrozen = true;
            frozenSince = Time.time;
            velocity = Vector3.zero; // pas de chute héritée d'avant le saut une fois dégelé
        }
    }

    private void TryUnfreeze()
    {
        bool groundReady = World.Instance != null && World.Instance.IsGroundReadyAt(transform.position);
        bool timedOut = Time.time - frozenSince > maxFreezeDuration;

        if (!groundReady && !timedOut)
        {
            return; // toujours en attente
        }

        if (groundReady)
        {
            // Le terrain est chargé, mais rien ne garantit que la position exacte visée par la
            // téléportation n'est pas en plein dans un mur/sous le sol — à vérifier seulement une
            // fois les données du chunk fiables (pas dans la branche timeout ci-dessous, où le
            // chargement peut être resté incomplet).
            EnsureNotEmbeddedInSolidGround();
        }
        else
        {
            Debug.LogWarning("FPSController: dégel forcé après le délai de sécurité, le terrain de destination n'a pas fini de charger à temps.");
        }

        isFrozen = false;
    }

    private void EnsureNotEmbeddedInSolidGround()
    {
        if (World.Instance == null)
        {
            return;
        }

        int clearance = Mathf.Max(1, Mathf.CeilToInt(characterController.height));
        if (World.Instance.HasClearance(transform.position, clearance))
        {
            return; // pas de mur/sol traversé, rien à corriger
        }

        if (World.Instance.TryFindSafeSpawnPosition(transform.position, clearance, out Vector3 safePosition))
        {
            // Remettre à jour transform.position pendant que le CharacterController est actif
            // se fait fight/écraser par son propre suivi interne — le désactiver le temps du
            // déplacement est le pattern standard Unity pour un repositionnement "dur".
            characterController.enabled = false;
            transform.position = safePosition;
            characterController.enabled = true;
        }
        else
        {
            Debug.LogWarning("FPSController: aucune position sûre trouvée au-dessus du point de téléportation (mur trop épais ?).");
        }
    }

    private void HandleMovement()
    {
        // Calculate movement direction
        Vector3 moveDirection = transform.right * moveInput.x + transform.forward * moveInput.y;
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        // Apply movement
        currentMovement.x = moveDirection.x * currentSpeed;
        currentMovement.z = moveDirection.z * currentSpeed;

        // Apply gravity
        if (characterController.isGrounded)
        {
            velocity.y = -2f; // Small downward force when grounded

            // Handle jumping
            if (jumpPressed)
            {
                velocity.y = jumpForce;
                jumpPressed = false;
            }
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        currentMovement.y = velocity.y;

        // Move the character
        characterController.Move(currentMovement * Time.deltaTime);
    }

    private void HandleLook()
    {
        // Horizontal rotation
        transform.Rotate(Vector3.up, lookInput.x * mouseSensitivity);

        // Vertical rotation
        cameraPitch += lookInput.y * mouseSensitivity * (invertY ? 1 : -1);
        cameraPitch = Mathf.Clamp(cameraPitch, -maxLookAngle, maxLookAngle);
        cameraTransform.localRotation = Quaternion.Euler(cameraPitch, 0, 0);
    }

    // Input System callbacks
    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    public void OnLook(InputValue value)
    {
        lookInput = value.Get<Vector2>();
    }

    public void OnJump(InputValue value)
    {
        if (characterController.isGrounded)
            jumpPressed = value.isPressed;
    }

    public void OnRun(InputValue value)
    {
        isRunning = value.isPressed;
    }
}