using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Kontroler poruszania się gracza w świecie 2.5D / 3D (styl Don't Starve) z użyciem CharacterController.
/// Ruch odbywa się po płaszczyźnie XZ w 8 kierunkach (WSAD) i jest zawsze relatywny do aktualnego obrotu kamery.
/// Zawiera:
/// - Odwracanie sprite'a (SpriteRenderer.flipX) przy ruchu lewo/prawo,
/// - Mechanikę Dash (Spacja) z czasem odnowienia (cooldown) i klatkami nieśmiertelności (i-frames),
/// - Grawitację i przyciąganie do podłoża (CharacterController.isGrounded),
/// - Publiczne właściwości do łatwego spięcia z Animatorem i UI.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerMovement : MonoBehaviour
{
    [Header("Referencje")]
    [Tooltip("Komponent SpriteRenderer odpowiadający za wygląd postaci. Jeśli pusty, zostanie pobrany z tego obiektu lub dzieci.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("Kamera referencyjna. Ruch gracza jest zawsze relatywny do jej kąta. Jeśli pusta, pobierze Camera.main.")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("Opcjonalny Animator. Jeśli przypisany, skrypt może automatycznie aktualizować parametry animacji.")]
    [SerializeField] private Animator animator;

    [Header("Ustawienia poruszania się")]
    [Tooltip("Prędkość standardowego chodu.")]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("Wartość grawitacji przyciągającej postać do podłoża.")]
    [SerializeField] private float gravity = -20f;

    [Tooltip("Dociąg do ziemi, gdy postać stoi na podłożu (zapobiega podskakiwaniu na nierównościach terenu).")]
    [SerializeField] private float groundedGravity = -2f;

    [Header("Odwracanie Sprite'a (Flip X)")]
    [Tooltip("Odwraca logikę SpriteRenderer.flipX (przydatne, gdy domyślna grafika sprite'a patrzy w lewo).")]
    [SerializeField] private bool invertFlip = false;

    [Header("Mechanika Uniku (Dash)")]
    [Tooltip("Prędkość poruszania się podczas uniku.")]
    [SerializeField] private float dashSpeed = 16f;

    [Tooltip("Czas trwania uniku w sekundach.")]
    [SerializeField] private float dashDuration = 0.2f;

    [Tooltip("Czas odnowienia uniku w sekundach (cooldown).")]
    [SerializeField] private float dashCooldown = 1.0f;

    [Tooltip("Czas trwania nieśmiertelności (i-frames) po rozpoczęciu uniku.")]
    [SerializeField] private float iFrameDuration = 0.25f;

    [Header("Parametry Animatora (Opcjonalne nazwy)")]
    [Tooltip("Czy automatycznie przesyłać parametry ruchu do komponentu Animator (jeśli istnieje)?")]
    [SerializeField] private bool updateAnimatorParameters = true;
    [SerializeField] private string animParamSpeed = "Speed";
    [SerializeField] private string animParamIsMoving = "IsMoving";
    [SerializeField] private string animParamIsDashing = "IsDashing";
    [SerializeField] private string animParamInputX = "InputX";
    [SerializeField] private string animParamInputY = "InputY";

    // Komponenty wewnętrzne
    private CharacterController characterController;

    // Stan ruchu
    private Vector2 rawInput;
    private Vector3 moveDirection;
    private Vector3 lastFacingDirection = Vector3.forward;
    private float verticalVelocity;
    private float currentSpeed;

    // Stan dasha i i-frames
    private bool isDashing;
    private float dashTimer;
    private float cooldownTimer;
    private float iFrameTimer;
    private Vector3 dashDirection;

    // Hashe parametrów Animatora (optymalizacja wydajności)
    private int hashSpeed;
    private int hashIsMoving;
    private int hashIsDashing;
    private int hashInputX;
    private int hashInputY;

    #region Publiczne Właściwości dla Animatora, UI i Systemu Walki

    /// <summary> Czy postać w tej klatce wykonuje unik (Dash). </summary>
    public bool IsDashing => isDashing;

    /// <summary> Czy gracz aktualnie posiada nietykalność (i-frames). </summary>
    public bool IsInvulnerable => iFrameTimer > 0f;

    /// <summary> Czy gracz aktualnie się porusza (na podstawie inputu lub dasha). </summary>
    public bool IsMoving => isDashing || rawInput.sqrMagnitude > 0.001f;

    /// <summary> Aktualna prędkość pozioma gracza (w jednostkach Unity na sekundę). </summary>
    public float CurrentSpeed => currentSpeed;

    /// <summary> Znormalizowana prędkość (0 do 1) – idealna do blend tree chodu. </summary>
    public float NormalizedSpeed => Mathf.Clamp01(currentSpeed / moveSpeed);

    /// <summary> Surowy wektor wejściowy gracza (X: lewo/prawo, Y: przód/tył) z zakresu [-1, 1]. </summary>
    public Vector2 MovementInput => rawInput;

    /// <summary> Kierunek ruchu w przestrzeni świata 3D (z uwzględnieniem kamery). </summary>
    public Vector3 WorldMoveDirection => isDashing ? dashDirection : moveDirection;

    /// <summary> Czy postać stoi na podłożu. </summary>
    public bool IsGrounded => characterController.isGrounded;

    /// <summary> Czy umiejętność Dash jest gotowa do użycia (cooldown minął). </summary>
    public bool CanDash => !isDashing && cooldownTimer <= 0f;

    /// <summary> Postęp odnowienia Dasha (od 0.0 po użyciu do 1.0 gdy gotowy) – idealne do fillAmount w UI. </summary>
    public float DashCooldownProgress => dashCooldown > 0f ? Mathf.Clamp01(1f - (cooldownTimer / dashCooldown)) : 1f;

    /// <summary> Czy postać patrzy w prawo względem ekranu/kamery. </summary>
    public bool IsFacingRight => spriteRenderer != null && (invertFlip ? spriteRenderer.flipX : !spriteRenderer.flipX);

    #endregion

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        CacheAnimatorHashes();
    }

    private void Start()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }
    }

    private void Update()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null) return;
        }

        UpdateTimers();
        HandleInput();
        ApplyMovement();
        HandleSpriteFlipping();
        UpdateAnimator();
    }

    /// <summary>
    /// Odliczanie czasu trwania dasha, cooldownu oraz klatek nietykalności.
    /// </summary>
    private void UpdateTimers()
    {
        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
        }

        if (iFrameTimer > 0f)
        {
            iFrameTimer -= Time.deltaTime;
        }

        if (isDashing)
        {
            dashTimer -= Time.deltaTime;
            if (dashTimer <= 0f)
            {
                EndDash();
            }
        }
    }

    /// <summary>
    /// Odczyt danych wejściowych WSAD i Spacji (wsparcie New Input System oraz Legacy).
    /// </summary>
    private void HandleInput()
    {
        Vector2 input = Vector2.zero;
        bool dashPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                dashPressed = true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        input.x += Input.GetAxisRaw("Horizontal");
        input.y += Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(KeyCode.Space))
        {
            dashPressed = true;
        }
#endif

        // Normalizacja wektora wejściowego, aby ruch po skosach nie był szybszy o pierwiastek z 2
        rawInput = Vector2.ClampMagnitude(input, 1f);

        // Próba aktywacji Dasha
        if (dashPressed && CanDash)
        {
            StartDash();
        }
    }

    /// <summary>
    /// Wylicza wektor ruchu relatywny do kamery i przemieszcza CharacterController.
    /// </summary>
    private void ApplyMovement()
    {
        // Obsługa grawitacji
        if (characterController.isGrounded)
        {
            if (verticalVelocity < 0f)
            {
                verticalVelocity = groundedGravity;
            }
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 horizontalMove = Vector3.zero;

        if (isDashing)
        {
            // Podczas dasha poruszamy się ze stałą dużą prędkością w zablokowanym kierunku
            horizontalMove = dashDirection * dashSpeed;
            currentSpeed = dashSpeed;
        }
        else
        {
            // 1. Pobieramy wektory kamery i rzutujemy je na płaszczyznę XZ (ignorujemy pochylenie góra/dół)
            Vector3 camForward = playerCamera.transform.forward;
            camForward.y = 0f;
            camForward.Normalize();

            Vector3 camRight = playerCamera.transform.right;
            camRight.y = 0f;
            camRight.Normalize();

            // 2. Wyliczamy kierunek w świecie: W/S idzie wzdłuż camForward, A/D wzdłuż camRight
            moveDirection = (camForward * rawInput.y + camRight * rawInput.x);

            if (moveDirection.sqrMagnitude > 0.001f)
            {
                moveDirection.Normalize();
                lastFacingDirection = moveDirection;
                currentSpeed = moveSpeed * rawInput.magnitude;
            }
            else
            {
                currentSpeed = 0f;
            }

            horizontalMove = moveDirection * (moveSpeed * rawInput.magnitude);
        }

        // Złożenie wektora prędkości poziomej i pionowej (grawitacji)
        Vector3 finalVelocity = horizontalMove + Vector3.up * verticalVelocity;

        // Przesunięcie przez CharacterController
        characterController.Move(finalVelocity * Time.deltaTime);
    }

    /// <summary>
    /// Odwraca sprite'a w zależności od ruchu w lewo lub prawo względem kamery.
    /// </summary>
    private void HandleSpriteFlipping()
    {
        if (spriteRenderer == null) return;

        // Sprawdzamy komponent poziomy wejścia (rawInput.x) lub ruch relatywny do kamery
        float horizontalViewMovement = rawInput.x;

        if (isDashing)
        {
            // Podczas dasha sprawdzamy, czy kierunek dasha jest w lewo czy prawo względem kamery
            Vector3 camRight = playerCamera.transform.right;
            camRight.y = 0f;
            camRight.Normalize();
            horizontalViewMovement = Vector3.Dot(dashDirection, camRight);
        }

        // Odwracamy sprite tylko wtedy, gdy występuje zauważalny ruch w lewo bądź prawo
        if (horizontalViewMovement > 0.05f)
        {
            // Ruch w prawo na ekranie
            spriteRenderer.flipX = invertFlip;
        }
        else if (horizontalViewMovement < -0.05f)
        {
            // Ruch w lewo na ekranie
            spriteRenderer.flipX = !invertFlip;
        }
    }

    /// <summary>
    /// Rozpoczyna manewr uniku (Dash) i aktywuje klatki nietykalności.
    /// </summary>
    private void StartDash()
    {
        isDashing = true;
        dashTimer = dashDuration;
        cooldownTimer = dashCooldown;
        iFrameTimer = iFrameDuration;

        // Jeśli gracz trzyma klawisze ruchu, dashujemy w tym kierunku.
        // Jeśli stoi w miejscu, dashujemy w ostatnio obranym kierunku lub w przód kamery.
        if (moveDirection.sqrMagnitude > 0.001f)
        {
            dashDirection = moveDirection;
        }
        else if (lastFacingDirection.sqrMagnitude > 0.001f)
        {
            dashDirection = lastFacingDirection;
        }
        else
        {
            Vector3 camForward = playerCamera.transform.forward;
            camForward.y = 0f;
            dashDirection = camForward.normalized;
        }
    }

    /// <summary>
    /// Zakończenie dasha.
    /// </summary>
    private void EndDash()
    {
        isDashing = false;
        dashTimer = 0f;
    }

    /// <summary>
    /// Buforuje identyfikatory parametrów Animatora, co znacząco przyspiesza wywołania.
    /// </summary>
    private void CacheAnimatorHashes()
    {
        hashSpeed = Animator.StringToHash(animParamSpeed);
        hashIsMoving = Animator.StringToHash(animParamIsMoving);
        hashIsDashing = Animator.StringToHash(animParamIsDashing);
        hashInputX = Animator.StringToHash(animParamInputX);
        hashInputY = Animator.StringToHash(animParamInputY);
    }

    /// <summary>
    /// Opcjonalne automatyczne przekazanie stanu do komponentu Animator.
    /// </summary>
    private void UpdateAnimator()
    {
        if (!updateAnimatorParameters || animator == null) return;

        animator.SetFloat(hashSpeed, currentSpeed);
        animator.SetBool(hashIsMoving, IsMoving);
        animator.SetBool(hashIsDashing, isDashing);
        animator.SetFloat(hashInputX, rawInput.x);
        animator.SetFloat(hashInputY, rawInput.y);
    }

    /// <summary>
    /// Umożliwia wymuszenie dasha z zewnątrz (np. przycisk UI na telefonie lub skill).
    /// </summary>
    public bool TryPerformDash()
    {
        if (CanDash)
        {
            StartDash();
            return true;
        }
        return false;
    }
}
