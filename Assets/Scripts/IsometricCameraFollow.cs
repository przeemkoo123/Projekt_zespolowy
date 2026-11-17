using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Kamera izometryczna / 2.5D w stylu Don't Starve.
/// Podąża płynnie (Vector3.SmoothDamp) za celem (graczem) pod stałym kątem nachylenia (oś X)
/// oraz obraca się o 90 stopni wokół gracza po wciśnięciu klawiszy Q i E.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)] // Wykonuje się przed skryptami Billboard, aby zapewnić aktualne dane kamery
public class IsometricCameraFollow : MonoBehaviour
{
    [Header("Cel kamery")]
    [Tooltip("Obiekt, za którym podąża kamera (zazwyczaj Gracz). Jeśli puste, wyszuka obiekt z tagiem 'Player'.")]
    [SerializeField] private Transform target;

    [Tooltip("Przesunięcie punktu skupienia kamery względem pozycji celu (np. Y = 1, aby patrzeć na klatkę piersiową postaci).")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1f, 0f);

    [Header("Kąt i odległość")]
    [Tooltip("Stały kąt pochylenia kamery w osi X w stopniach (np. 45 dla rzutu izometrycznego).")]
    [Range(10f, 85f)]
    [SerializeField] private float pitchAngle = 45f;

    [Tooltip("Odległość kamery od obserwowanego punktu.")]
    [SerializeField] private float distance = 14f;

    [Header("Wygładzanie podążania")]
    [Tooltip("Czas w sekundach potrzebny na osiągnięcie pozycji docelowej przez Vector3.SmoothDamp.")]
    [SerializeField] private float followSmoothTime = 0.2f;

    [Header("Obrót wokół celu (Klawisze Q / E)")]
    [Tooltip("Kąt o jaki kamera obraca się przy pojedynczym wciśnięciu (domyślnie 90 stopni).")]
    [SerializeField] private float rotationStep = 90f;

    [Tooltip("Czas w sekundach potrzebny na płynne obrócenie kamery o zadany kąt.")]
    [SerializeField] private float rotationSmoothTime = 0.25f;

    [Tooltip("Odwraca kierunek obrotu klawiszy Q i E.")]
    [SerializeField] private bool invertRotationDirection = false;

    // Zmienne wewnętrzne do płynnego ruchu i obrotu
    private Vector3 positionVelocity;
    private float currentYaw = 0f;
    private float targetYaw = 0f;
    private float yawVelocity;

    private void Awake()
    {
        InitializeAndSnap();
    }

    private void Start()
    {
        InitializeAndSnap();
    }

    private void InitializeAndSnap()
    {
        if (target == null)
        {
            FindTarget();
        }

        targetYaw = transform.eulerAngles.y;
        currentYaw = targetYaw;
        SnapToTarget();
    }

    private void FindTarget()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
            if (pm != null) playerObj = pm.gameObject;
        }

        if (playerObj == null)
        {
            // Bezpieczny fallback w trybie gry: jeśli na scenie nie ma gracza, zespawnuj go z Resources
            GameObject playerPrefab = Resources.Load<GameObject>("Player_Placeholder");
            if (playerPrefab != null)
            {
                playerObj = Instantiate(playerPrefab, new Vector3(0f, 0f, -12f), Quaternion.identity);
                playerObj.name = "Player";
            }
        }

        if (playerObj != null)
        {
            target = playerObj.transform;
        }
    }

    private Vector3 GetFocusPoint()
    {
        if (target != null)
        {
            return target.position + targetOffset;
        }

        FindTarget();
        if (target != null)
        {
            return target.position + targetOffset;
        }

        // Awaryjny punkt skupienia (środek sceny), aby kamera zawsze miała stabilną pozycję izometryczną
        return targetOffset;
    }

    private void Update()
    {
        HandleInput();
    }

    private void LateUpdate()
    {
        UpdateCameraTransform();
    }

    /// <summary>
    /// Odczytuje wciśnięcia klawiszy Q i E, obsługując zarówno New Input System, jak i Legacy Input.
    /// </summary>
    private void HandleInput()
    {
        bool rotateLeft = false;
        bool rotateRight = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.qKey.wasPressedThisFrame) rotateLeft = true;
            if (Keyboard.current.eKey.wasPressedThisFrame) rotateRight = true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Q)) rotateLeft = true;
        if (Input.GetKeyDown(KeyCode.E)) rotateRight = true;
#endif

        if (rotateLeft)
        {
            RotateLeft();
        }
        else if (rotateRight)
        {
            RotateRight();
        }
    }

    /// <summary>
    /// Obraca kamerę o 90 stopni w lewo (klawisz Q).
    /// </summary>
    public void RotateLeft()
    {
        float direction = invertRotationDirection ? 1f : -1f;
        targetYaw += direction * rotationStep;
    }

    /// <summary>
    /// Obraca kamerę o 90 stopni w prawo (klawisz E).
    /// </summary>
    public void RotateRight()
    {
        float direction = invertRotationDirection ? -1f : 1f;
        targetYaw += direction * rotationStep;
    }

    /// <summary>
    /// Wylicza pozycję i rotację kamery z wykorzystaniem Vector3.SmoothDamp oraz Mathf.SmoothDampAngle.
    /// </summary>
    private void UpdateCameraTransform()
    {
        // Płynny obrót wokół osi Y
        if (rotationSmoothTime > 0.001f)
        {
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, rotationSmoothTime);
        }
        else
        {
            currentYaw = targetYaw;
        }

        // Aktualna rotacja kamery: stały kąt pitchAngle w osi X oraz dynamiczny currentYaw w osi Y
        Quaternion cameraRotation = Quaternion.Euler(pitchAngle, currentYaw, 0f);

        // Obliczenie docelowej pozycji kamery względem celu lub środka sceny
        Vector3 targetFocusPoint = GetFocusPoint();
        Vector3 desiredCameraPosition = targetFocusPoint + (cameraRotation * new Vector3(0f, 0f, -distance));

        // Płynne podążanie za pozycją (Vector3.SmoothDamp)
        if (followSmoothTime > 0.001f)
        {
            transform.position = Vector3.SmoothDamp(transform.position, desiredCameraPosition, ref positionVelocity, followSmoothTime);
        }
        else
        {
            transform.position = desiredCameraPosition;
        }

        // Zastosowanie wyliczonej rotacji
        transform.rotation = cameraRotation;
    }

    /// <summary>
    /// Błyskawicznie ustawia kamerę na docelowej pozycji i kącie bez interpolacji (np. przy respawnie lub wczytaniu mapy).
    /// </summary>
    public void SnapToTarget()
    {
        currentYaw = targetYaw;
        yawVelocity = 0f;
        positionVelocity = Vector3.zero;

        Quaternion cameraRotation = Quaternion.Euler(pitchAngle, currentYaw, 0f);
        Vector3 targetFocusPoint = GetFocusPoint();
        transform.position = targetFocusPoint + (cameraRotation * new Vector3(0f, 0f, -distance));
        transform.rotation = cameraRotation;
    }

    /// <summary>
    /// Pozwala zmienić śledzony obiekt w trakcie gry.
    /// </summary>
    public void SetTarget(Transform newTarget, bool snapImmediately = false)
    {
        target = newTarget;
        if (snapImmediately)
        {
            SnapToTarget();
        }
    }
}
