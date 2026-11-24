using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cámara en tercera persona que sigue al Player y orbita con la acción "Look"
/// del Action Map "Player" (ratón / stick derecho).
/// Incluye colisión básica (SphereCast) para no atravesar obstáculos y límites de pitch.
///
/// Resolución del input (segura, sin depender del Inspector):
///   1) Asset asignado en el Inspector (inputActionsAsset).
///   2) Resources.Load("InputSystem_Actions").
///   3) Clase generada InputSystem_Actions (JSON embebido). Siempre disponible.
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class PlayerCameraFollow : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Transform a seguir. Si es null se busca un GameObject con tag 'Player'.")]
    [SerializeField] private Transform playerTarget;
    [Tooltip("Opcional. Asset de acciones (InputSystem_Actions). Si queda vacío se resuelve automáticamente.")]
    [SerializeField] private InputActionAsset inputActionsAsset;

    [Header("Órbita")]
    [SerializeField, Min(0.5f)] private float distance = 4.5f;
    [SerializeField, Min(0f)] private float heightOffset = 1.6f;
    [SerializeField, Min(0.01f)] private float mouseSensitivity = 0.12f;
    [SerializeField] private bool invertY = false;
    [SerializeField] private float pitchMin = -35f;
    [SerializeField] private float pitchMax = 70f;

    [Header("Colisión")]
    [SerializeField, Min(0.01f)] private float collisionRadius = 0.3f;
    [SerializeField, Min(0.1f)] private float minDistance = 0.6f;
    [SerializeField] private LayerMask collisionMask = ~0;

    [Header("Suavizado")]
    [SerializeField, Min(0.001f)] private float positionSmoothTime = 0.06f;
    [SerializeField, Min(0.001f)] private float rotationSmoothTime = 0.05f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursorOnStart = true;

    // --- Estado ---
    private InputAction lookAction;
    private InputSystem_Actions generatedActions;
    private float yaw;
    private float pitch;
    private float currentDistance;
    private Vector3 positionVelocity;

    private const string PlayerMapName = "Player";
    private const string LookActionName = "Look";

    private void Awake()
    {
        if (playerTarget == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) playerTarget = go.transform;
            else Debug.LogWarning("[PlayerCameraFollow] No se encontró 'Player'. Asígnalo en el Inspector.");
        }

        InputActionAsset asset = ResolveAsset();
        if (asset != null)
        {
            InputActionMap map = asset.FindActionMap(PlayerMapName, throwIfNotFound: false);
            if (map != null) lookAction = map.FindAction(LookActionName, throwIfNotFound: false);
        }

        // Estado inicial: mirando al player desde atrás.
        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        pitch = NormalizePitch(e.x);
        currentDistance = distance;

        if (playerTarget != null)
            SnapBehindPlayer();
    }

    private InputActionAsset ResolveAsset()
    {
        if (inputActionsAsset != null) return inputActionsAsset;

        InputActionAsset fromResources = Resources.Load<InputActionAsset>("InputSystem_Actions");
        if (fromResources != null) return fromResources;

        generatedActions = new InputSystem_Actions();
        return generatedActions.asset;
    }

    private void OnEnable()
    {
        EnableLook(true);
        if (lockCursorOnStart) SetCursorLock(true);
    }

    private void OnDisable()
    {
        EnableLook(false);
        SetCursorLock(false);
    }

    private void OnDestroy()
    {
        generatedActions?.Dispose();
        generatedActions = null;
    }

    private void EnableLook(bool enable)
    {
        if (lookAction != null)
        {
            if (enable) lookAction.Enable(); else lookAction.Disable();
        }
        else if (generatedActions != null)
        {
            if (enable) generatedActions.Player.Enable(); else generatedActions.Player.Disable();
        }
    }

    private void LateUpdate()
    {
        if (playerTarget == null || lookAction == null) return;

        ReadLook();
        UpdatePosition();
        UpdateRotation();
    }

    private void ReadLook()
    {
        Vector2 look = lookAction.ReadValue<Vector2>();
        yaw += look.x * mouseSensitivity;
        pitch += (invertY ? look.y : -look.y) * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
    }

    private void UpdatePosition()
    {
        Vector3 pivot = playerTarget.position + Vector3.up * heightOffset;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = orbit * Vector3.back;

        float desired = distance;
        if (Physics.SphereCast(pivot, collisionRadius, back, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
            desired = Mathf.Max(minDistance, hit.distance - collisionRadius);

        // Alejar suavemente, acercar rápido (evita atravesar paredes).
        currentDistance = Mathf.Lerp(currentDistance, desired, desired < currentDistance ? 0.9f : 0.12f);

        Vector3 targetPos = pivot + back * currentDistance;
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref positionVelocity, positionSmoothTime);
    }

    private void UpdateRotation()
    {
        Vector3 pivot = playerTarget.position + Vector3.up * heightOffset;
        Vector3 dir = pivot - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        // Slerp con factor independiente del framerate.
        float t = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, rotationSmoothTime));
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
    }

    /// <summary>Coloca la cámara detrás del player mirando en la dirección en que mira.</summary>
    private void SnapBehindPlayer()
    {
        Vector3 pivot = playerTarget.position + Vector3.up * heightOffset;
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        transform.position = pivot + orbit * Vector3.back * distance;
        Vector3 dir = pivot - transform.position;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        currentDistance = distance;
    }

    private static float NormalizePitch(float x) => x > 180f ? x - 360f : x;

    public void SetCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    public void AdjustDistance(float delta)
    {
        distance = Mathf.Clamp(distance + delta, minDistance, 20f);
    }

    // --- API pública ---
    public Transform PlayerTarget => playerTarget;
    public float CurrentYaw => yaw;
    public float CurrentPitch => pitch;
}