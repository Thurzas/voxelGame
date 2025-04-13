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
    }

    private void Update()
    {
        HandleMovement();
        HandleLook();
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