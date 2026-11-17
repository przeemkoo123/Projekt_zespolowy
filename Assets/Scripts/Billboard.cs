using UnityEngine;

/// <summary>
/// Skrypt odpowiedzialny za efekt Billboard dla obiektów 2D (np. ze SpriteRenderer) w świecie 3D (2.5D w stylu Don't Starve).
/// Zapewnia, że płaski sprite jest zawsze odpowiednio zwrócony w stronę kamery.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // Wykonuje się po kamerze, aby wyeliminować drgania (jitter)
public class Billboard : MonoBehaviour
{
    public enum BillboardMode
    {
        [InspectorName("Pełne dopasowanie rotacji (Styl Don't Starve)")]
        MatchCameraRotation,

        [InspectorName("Zablokowana oś X (Pionowa wycinanka / stały kąt)")]
        LockXAxis,

        [InspectorName("Zwrócony w stronę punktu kamery (LookAt)")]
        LookAtCameraPosition,

        [InspectorName("Niestandardowa blokada osi (Custom)")]
        CustomAxisLock
    }

    [Header("Tryb działania")]
    [Tooltip("MatchCameraRotation: płaszczyzna sprite'a jest idealnie równoległa do ekranu kamery (klasyczny papierowy styl Don't Starve).\n" +
             "LockXAxis: sprite zachowuje pion (lub stały kąt X) i obraca się jedynie w osi Y za kamerą.\n" +
             "LookAtCameraPosition: celuje bezpośrednio w pozycję kamery w przestrzeni.\n" +
             "CustomAxisLock: pozwala zablokować dowolne wybrane osie rotacji.")]
    [SerializeField] private BillboardMode billboardMode = BillboardMode.MatchCameraRotation;

    [Header("Kamera")]
    [Tooltip("Referencja do kamery. Jeśli pole pozostanie puste, skrypt automatycznie użyje Camera.main.")]
    [SerializeField] private Camera targetCamera;

    [Header("Ustawienia dla trybu LockXAxis")]
    [Tooltip("Stały kąt pochylenia w osi X (0 = idealnie pionowa wycinanka stojąca na płaszczyźnie XZ).")]
    [SerializeField] private float lockedXAngle = 0f;

    [Header("Ustawienia dla trybu CustomAxisLock")]
    [SerializeField] private bool lockX = false;
    [SerializeField] private float customXAngle = 0f;
    [SerializeField] private bool lockY = false;
    [SerializeField] private float customYAngle = 0f;
    [SerializeField] private bool lockZ = true;
    [SerializeField] private float customZAngle = 0f;

    [Header("Offset")]
    [Tooltip("Dodatkowe stałe przesunięcie rotacji (w stopniach), np. gdy grafika sprite'a wymaga korekty.")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    private Transform cameraTransform;

    private void Awake()
    {
        InitializeCamera();
    }

    private void OnEnable()
    {
        InitializeCamera();
    }

    /// <summary>
    /// Pobiera i buforuje referencję do kamery.
    /// </summary>
    private void InitializeCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            cameraTransform = targetCamera.transform;
        }
    }

    private void LateUpdate()
    {
        // Sprawdzenie referencji w razie dynamicznego tworzenia lub przełączania kamery
        if (cameraTransform == null)
        {
            InitializeCamera();
            if (cameraTransform == null) return;
        }

        UpdateRotation();
    }

    private void UpdateRotation()
    {
        Quaternion targetRotation = Quaternion.identity;

        switch (billboardMode)
        {
            case BillboardMode.MatchCameraRotation:
                // Pełne odwzorowanie kąta widzenia kamery - sprite jest idealnie płaski względem ekranu
                targetRotation = cameraTransform.rotation;
                break;

            case BillboardMode.LockXAxis:
                // Oś Y dopasowuje się do obrotu kamery (np. po wciśnięciu Q/E), a oś X ma stały kąt (np. 0 = pionowo)
                float cameraYaw = cameraTransform.eulerAngles.y;
                targetRotation = Quaternion.Euler(lockedXAngle, cameraYaw, 0f);
                break;

            case BillboardMode.LookAtCameraPosition:
                // W Unity standardowy front SpriteRenderer skierowany jest w stronę -Z,
                // dlatego kierunek 'LookRotation' liczony od kamery do obiektu ustawia przód sprite'a twarzą do kamery.
                Vector3 dir = transform.position - cameraTransform.position;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    targetRotation = Quaternion.LookRotation(dir);
                }
                else
                {
                    targetRotation = transform.rotation;
                }
                break;

            case BillboardMode.CustomAxisLock:
                Vector3 camEuler = cameraTransform.eulerAngles;
                float x = lockX ? customXAngle : camEuler.x;
                float y = lockY ? customYAngle : camEuler.y;
                float z = lockZ ? customZAngle : camEuler.z;
                targetRotation = Quaternion.Euler(x, y, z);
                break;
        }

        // Zastosowanie ewentualnego offsetu
        if (rotationOffset != Vector3.zero)
        {
            targetRotation *= Quaternion.Euler(rotationOffset);
        }

        transform.rotation = targetRotation;
    }

    /// <summary>
    /// Pozwala programowo przypisać inną kamerę docelową w trakcie gry.
    /// </summary>
    public void SetTargetCamera(Camera newCamera)
    {
        targetCamera = newCamera;
        cameraTransform = newCamera != null ? newCamera.transform : null;
    }
}
