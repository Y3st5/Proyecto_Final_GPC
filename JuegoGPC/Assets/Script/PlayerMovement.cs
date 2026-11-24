using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento del Player usando el New Input System (InputSystem_Actions).
/// Lee la acción "Move" del Action Map "Player" y mueve el CharacterController
/// relativo a la orientación de la cámara.
///
/// Resolución del input, en este orden (segura, sin errores ni duplicados):
///   1) Asset asignado en el Inspector (inputActionsAsset).
///   2) Resources.Load("InputSystem_Actions") si existe en Assets/Resources.
///   3) Clase generada InputSystem_Actions (Assets/InputSystem_Actions.cs), que
///      reconstruye el asset desde su JSON embebido. Siempre disponible.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerMovement : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Opcional. Asset de acciones (InputSystem_Actions). Si se deja vacío se resuelve automáticamente.")]
    [SerializeField] private InputActionAsset inputActionsAsset;
    [Tooltip("Cámara de referencia para el movimiento. Si es null se usa Camera.main.")]
    [SerializeField] private Camera referenceCamera;

    [Header("Movimiento")]
    [SerializeField, Min(0f)] private float walkSpeed = 5f;
    [SerializeField, Min(0f)] private float sprintSpeed = 9f;
    [SerializeField, Min(0f)] private float rotationSpeed = 12f;

    [Header("Física")]
    [SerializeField] private float gravity = -20f;
    [SerializeField, Min(0f)] private float jumpHeight = 1.4f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Suavizado")]
    [Tooltip("0 = sin suavizado, valores mayores = transición más suave del input.")]
    [SerializeField, Range(0f, 1f)] private float inputSmoothing = 0.15f;
    [Tooltip("Velocidad con la que la velocidad actual alcanza la objetivo.")]
    [SerializeField, Min(0f)] private float speedChangeRate = 12f;

    // --- Estado interno ---
    private CharacterController controller;
    private InputAction moveAction;
    private InputAction sprintAction;
    private InputAction jumpAction;
    private InputSystem_Actions generatedActions; // wrapper de respaldo (si no hay asset)
    private Vector2 rawInput;
    private Vector2 smoothedInput;
    private Vector3 velocity;
    private float currentSpeed;
    private bool isSprinting;

    // Nombres del asset (constantes para evitar strings mágicos)
    private const string PlayerMapName = "Player";
    private const string MoveActionName = "Move";
    private const string SprintActionName = "Sprint";
    private const string JumpActionName = "Jump";

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (referenceCamera == null) referenceCamera = Camera.main;

        InputActionAsset asset = ResolveAsset();

        if (asset == null)
        {
            Debug.LogError("[PlayerMovement] No se pudo resolver InputSystem_Actions. Se desactiva el movimiento.");
            enabled = false;
            return;
        }

        InputActionMap playerMap = asset.FindActionMap(PlayerMapName, throwIfNotFound: false);
        if (playerMap == null)
        {
            Debug.LogError($"[PlayerMovement] Action Map '{PlayerMapName}' no encontrado.");
            enabled = false;
            return;
        }

        moveAction = playerMap.FindAction(MoveActionName, throwIfNotFound: false);
        sprintAction = playerMap.FindAction(SprintActionName, throwIfNotFound: false);
        jumpAction = playerMap.FindAction(JumpActionName, throwIfNotFound: false);

        if (moveAction == null)
        {
            Debug.LogError($"[PlayerMovement] Acción '{MoveActionName}' no encontrada en el map '{PlayerMapName}'.");
            enabled = false;
        }
    }

    /// <summary>
    /// Devuelve el InputActionAsset a usar según prioridad: Inspector > Resources > wrapper generado.
    /// </summary>
    private InputActionAsset ResolveAsset()
    {
        if (inputActionsAsset != null) return inputActionsAsset;

        InputActionAsset fromResources = Resources.Load<InputActionAsset>("InputSystem_Actions");
        if (fromResources != null) return fromResources;

        // Respaldo definitivo: wrapper generado que arma el asset desde su JSON.
        generatedActions = new InputSystem_Actions();
        return generatedActions.asset;
    }

    private void OnEnable()
    {
        // Enabled ANTES de suscribir callbacks evita perder el primer performed.
        EnableMap(true);

        if (sprintAction != null)
        {
            sprintAction.performed += OnSprintPerformed;
            sprintAction.canceled += OnSprintCanceled;
        }
        if (jumpAction != null)
            jumpAction.performed += OnJumpPerformed;
    }

    private void OnDisable()
    {
        if (sprintAction != null)
        {
            sprintAction.performed -= OnSprintPerformed;
            sprintAction.canceled -= OnSprintCanceled;
        }
        if (jumpAction != null)
            jumpAction.performed -= OnJumpPerformed;

        EnableMap(false);
    }

    private void OnDestroy()
    {
        // Limpieza defensiva por si OnDisable no alcanzó a correr.
        if (sprintAction != null)
        {
            sprintAction.performed -= OnSprintPerformed;
            sprintAction.canceled -= OnSprintCanceled;
        }
        if (jumpAction != null)
            jumpAction.performed -= OnJumpPerformed;

        generatedActions?.Dispose();
        generatedActions = null;
    }

    private void EnableMap(bool enable)
    {
        if (inputActionsAsset != null)
        {
            InputActionMap map = inputActionsAsset.FindActionMap(PlayerMapName, throwIfNotFound: false);
            if (map != null) { if (enable) map.Enable(); else map.Disable(); }
        }
        else if (generatedActions != null)
        {
            if (enable) generatedActions.Player.Enable();
            else generatedActions.Player.Disable();
        }
    }

    private void Update()
    {
        if (moveAction == null) return;

        ReadInput();
        ApplyHorizontalMovement();
        ApplyRotation();
        ApplyGravityAndJump();
    }

    // =====================================================================
    // INPUT
    // =====================================================================
    private void ReadInput()
    {
        rawInput = moveAction.ReadValue<Vector2>();

        // Evita diagonales más rápidas en teclado.
        if (rawInput.sqrMagnitude > 1f) rawInput.Normalize();

        // Suavizado con factor independiente del framerate.
        float smoothFactor = inputSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-60f * inputSmoothing * Time.deltaTime);
        smoothedInput = Vector2.Lerp(smoothedInput, rawInput, smoothFactor);

        isSprinting = sprintAction != null && sprintAction.IsPressed();
    }

    // =====================================================================
    // MOVIMIENTO RELATIVO A CÁMARA
    // =====================================================================
    private void GetCameraBasis(out Vector3 forward, out Vector3 right)
    {
        if (referenceCamera != null)
        {
            forward = referenceCamera.transform.forward;
            right = referenceCamera.transform.right;
            forward.y = 0f;
            right.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            forward.Normalize();
            right.Normalize();
        }
        else
        {
            forward = Vector3.forward;
            right = Vector3.right;
        }
    }

    private void ApplyHorizontalMovement()
    {
        GetCameraBasis(out Vector3 forward, out Vector3 right);
        Vector3 moveDir = (forward * smoothedInput.y + right * smoothedInput.x);
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        float targetSpeed = (isSprinting ? sprintSpeed : walkSpeed) * smoothedInput.magnitude;
        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, speedChangeRate * Time.deltaTime);

        controller.Move(moveDir * (currentSpeed * Time.deltaTime));
    }

    private void ApplyRotation()
    {
        if (smoothedInput.sqrMagnitude < 0.0001f) return;

        GetCameraBasis(out Vector3 forward, out Vector3 right);
        Vector3 targetDir = forward * smoothedInput.y + right * smoothedInput.x;
        targetDir.y = 0f;
        if (targetDir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(targetDir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 1f - Mathf.Exp(-rotationSpeed * Time.deltaTime));
    }

    // =====================================================================
    // GRAVEDAD Y SUELO
    // =====================================================================
    private void ApplyGravityAndJump()
    {
        bool grounded = controller.isGrounded;
        if (!grounded)
        {
            // Raycast extra para bordes/escalones.
            grounded = Physics.Raycast(transform.position + Vector3.up * 0.15f, Vector3.down, 0.3f, groundMask, QueryTriggerInteraction.Ignore);
        }

        if (grounded && velocity.y < 0f)
            velocity.y = -2f; // mantiene pegado al suelo

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    // --- Callbacks ---
    private void OnSprintPerformed(InputAction.CallbackContext ctx) => isSprinting = true;
    private void OnSprintCanceled(InputAction.CallbackContext ctx) => isSprinting = false;

    private void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (controller.isGrounded)
            velocity.y = Mathf.Sqrt(-2f * gravity * Mathf.Max(0.01f, jumpHeight));
    }

    // --- API pública (Animator, UI, etc.) ---
    public float CurrentSpeed => currentSpeed;
    public bool IsGrounded => controller != null && controller.isGrounded;
    public bool IsSprinting => isSprinting;
    public Vector2 RawInput => rawInput;
    public Vector2 SmoothedInput => smoothedInput;

    /// <summary>Normaliza la velocidad horizontal actual respecto a walk/sprint (0..1) para Animator.</summary>
    public float NormalizedSpeed => sprintSpeed > 0.01f ? Mathf.Clamp01(currentSpeed / sprintSpeed) : 0f;
}