using System;
using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Modularny system walki mieczem w 2.5D (styl Don't Starve).
/// Obsługuje:
/// - 3-etapowe combo mieczem na LPM (zwiększające się obrażenia i odrzut w każdym etapie),
/// - Reset combo po przekroczeniu okna czasowego (np. 0.8s) lub po ukończeniu 3 ciosu,
/// - Wykrywanie trafień przez Physics.OverlapSphereNonAlloc w punkcie przed graczem,
/// - Celowanie w stronę kursora myszy (Plane.Raycast na płaszczyźnie podłoża XZ) lub w stronę ruchu,
/// - Pobieranie bazowych obrażeń z komponentu PlayerStats,
/// - Odwracanie sprite'a w stronę ataku i integrację z Animatorem.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCombat : MonoBehaviour
{
    public enum AimMode
    {
        [InspectorName("Kursor myszy (Plane.Raycast)")]
        MouseCursor,

        [InspectorName("Kierunek poruszania się")]
        MovementDirection,

        [InspectorName("Mysz z przejściem na ruch (Auto)")]
        MouseWithMovementFallback
    }

    [System.Serializable]
    public struct ComboStep
    {
        [Tooltip("Nazwa etapu combo (dla czytelności w Inspektorze).")]
        public string stepName;

        [Tooltip("Mnożnik obrażeń bazowych dla tego ataku (np. 1.0, 1.35, 1.8).")]
        public float damageMultiplier;

        [Tooltip("Siła odrzutu (knockback) zadawana przeciwnikowi.")]
        public float knockbackForce;

        [Tooltip("Zasięg ataku (odległość środka sfery ataku przed graczem).")]
        public float attackRange;

        [Tooltip("Promień sfery trafienia (hitbox).")]
        public float attackRadius;

        [Tooltip("Minimalny odstęp czasowy przed możliwością wyprowadzenia kolejnego ataku.")]
        public float attackDuration;
    }

    [Header("Referencje")]
    [Tooltip("Komponent statystyk gracza, z którego pobierane są bazowe obrażenia.")]
    [SerializeField] private PlayerStats playerStats;

    [Tooltip("Kamera używana do rzutowania promienia kursora myszy. Domyślnie Camera.main.")]
    [SerializeField] private Camera mainCamera;

    [Tooltip("Opcjonalny komponent PlayerMovement do sprawdzania dasha i kierunku ruchu.")]
    [SerializeField] private PlayerMovement playerMovement;

    [Tooltip("Komponent SpriteRenderer do odwracania postaci w stronę ataku.")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("Opcjonalny Animator do odtwarzania animacji ataku.")]
    [SerializeField] private Animator animator;

    [Header("Tryb celowania")]
    [Tooltip("Wybierz, w jaki sposób wyznaczany jest kierunek ataku mieczem.")]
    [SerializeField] private AimMode aimMode = AimMode.MouseWithMovementFallback;

    [Tooltip("Wysokość punktu ataku względem pozycji gracza (np. na wysokości pasa/klatki).")]
    [SerializeField] private float attackHeightOffset = 0.5f;

    [Header("Ustawienia Combo")]
    [Tooltip("Maksymalny czas (w sekundach) od ostatniego ataku, w którym można kontynuować combo.")]
    [SerializeField] private float comboResetTime = 0.8f;

    [Tooltip("Konfiguracja poszczególnych 3 etapów combo.")]
    [SerializeField] private ComboStep[] comboStages = new ComboStep[3]
    {
        new ComboStep { stepName = "Cios 1 (Lekki)", damageMultiplier = 1.0f, knockbackForce = 5f, attackRange = 1.2f, attackRadius = 0.9f, attackDuration = 0.25f },
        new ComboStep { stepName = "Cios 2 (Średni)", damageMultiplier = 1.35f, knockbackForce = 7.5f, attackRange = 1.3f, attackRadius = 1.0f, attackDuration = 0.28f },
        new ComboStep { stepName = "Cios 3 (Finisher)", damageMultiplier = 1.85f, knockbackForce = 13f, attackRange = 1.5f, attackRadius = 1.25f, attackDuration = 0.45f }
    };

    [Header("Warstwy trafień")]
    [Tooltip("Warstwy (Layers), na których znajdują się obiekty mogące otrzymać obrażenia (np. Enemies, Destructible).")]
    [SerializeField] private LayerMask damageableLayers;

    // Zdarzenia dla efektów dźwiękowych, cząsteczkowych i shake'a kamery
    public event Action<int, float> OnAttackStarted; // int comboStep, float damage
    public event Action<IDamageable, float, Vector3> OnHitLanded; // target, damage, hitPosition

    // Stan wewnętrzny combo
    private int currentComboIndex = 0; // 0, 1, 2
    private float comboTimer = 0f;
    private float attackCooldownTimer = 0f;
    private bool isAttacking = false;
    private Vector3 lastAimDirection = Vector3.forward;

    // Bufor do bezalokacyjnego wykrywania kolizji (Physics.OverlapSphereNonAlloc)
    private readonly Collider[] hitBuffer = new Collider[32];
    private readonly HashSet<IDamageable> damagedEntitiesThisSwing = new HashSet<IDamageable>();

    // Hashe Animatora
    private static readonly int AnimParamAttackTrigger = Animator.StringToHash("Attack");
    private static readonly int AnimParamComboStep = Animator.StringToHash("ComboStep");
    private static readonly int AnimParamIsAttacking = Animator.StringToHash("IsAttacking");

    #region Publiczne Właściwości

    /// <summary> Aktualny etap combo (1, 2 lub 3). Zwraca 0, jeśli gracz nie jest w trakcie combo. </summary>
    public int CurrentComboStage => isAttacking || comboTimer > 0f ? (currentComboIndex + 1) : 0;

    /// <summary> Czy postać w tej chwili wykonuje zamach mieczem. </summary>
    public bool IsAttacking => isAttacking;

    /// <summary> Pozostały czas okna czasowego combo (w sekundach). </summary>
    public float RemainingComboTime => Mathf.Max(0f, comboResetTime - comboTimer);

    /// <summary> Czy gracz może w tej klatce wyprowadzić kolejny atak. </summary>
    public bool CanAttack
    {
        get
        {
            if (attackCooldownTimer > 0f) return false;
            if (playerMovement != null && playerMovement.IsDashing) return false;
            return true;
        }
    }

    #endregion

    private void Awake()
    {
        if (playerStats == null) playerStats = GetComponent<PlayerStats>();
        if (playerMovement == null) playerMovement = GetComponent<PlayerMovement>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (mainCamera == null) mainCamera = Camera.main;

        // Upewnij się, że tablica combo posiada co najmniej 3 elementy
        if (comboStages == null || comboStages.Length == 0)
        {
            ResetToDefaultCombo();
        }
    }

    private void Update()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        UpdateTimers();
        HandleInput();
    }

    private void UpdateTimers()
    {
        // Odliczanie czasu zamachu
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
            if (attackCooldownTimer <= 0f)
            {
                isAttacking = false;
                if (animator != null) animator.SetBool(AnimParamIsAttacking, false);
            }
        }

        // Odliczanie okna czasowego combo
        if (!isAttacking && comboTimer > 0f)
        {
            comboTimer += Time.deltaTime;
            if (comboTimer >= comboResetTime)
            {
                ResetCombo();
            }
        }
    }

    private void HandleInput()
    {
        // Jeśli kliknięto w UI (np. przycisk restartu na ekranie Game Over), nie atakuj
        if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        bool attackPressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            attackPressed = true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            attackPressed = true;
        }
#endif

        if (attackPressed && CanAttack)
        {
            ExecuteComboAttack();
        }
    }

    /// <summary>
    /// Wykonuje kolejny cios z sekwencji combo.
    /// </summary>
    private void ExecuteComboAttack()
    {
        // Jeśli okno combo wygasło lub skończyliśmy 3 ciosy, zacznij od początku
        if (comboTimer >= comboResetTime || currentComboIndex >= comboStages.Length)
        {
            currentComboIndex = 0;
        }

        ComboStep currentStep = comboStages[currentComboIndex];

        // Wyznacz kierunek ataku (kursor myszy na ziemi lub kierunek ruchu)
        Vector3 aimDirection = CalculateAimDirection();
        lastAimDirection = aimDirection;

        // Obróć sprite'a w stronę ataku (względem kamery)
        AlignSpriteFacing(aimDirection);

        // Oblicz obrażenia: bazowe ze statystyk gracza * mnożnik aktualnego etapu combo
        float baseDamage = playerStats != null ? playerStats.GetCalculatedDamage() : 20f;
        float finalDamage = baseDamage * currentStep.damageMultiplier;

        // Ustawienie stanów i timerów
        isAttacking = true;
        comboTimer = 0.001f; // rozpoczynamy odliczanie combo
        attackCooldownTimer = currentStep.attackDuration;

        // Wykrywanie trafień i zadanie obrażeń
        DetectAndApplyHits(currentStep, aimDirection, finalDamage);

        // Powiadomienie Animatora
        TriggerAnimator(currentComboIndex + 1);

        // Wywołanie zdarzenia
        OnAttackStarted?.Invoke(currentComboIndex + 1, finalDamage);

        // Przygotuj indeks pod kolejny cios (jeśli gracz kliknie przed upływem comboResetTime)
        currentComboIndex++;
        if (currentComboIndex >= comboStages.Length)
        {
            // Po 3 ciosie combo zresetuje się po zakończeniu zamachu
            currentComboIndex = 0;
            comboTimer = comboResetTime; // wymuszenie resetu po ukończeniu pełnego combo
        }
    }

    /// <summary>
    /// Wykrywa trafienia za pomocą Physics.OverlapSphereNonAlloc w punkcie przed graczem.
    /// </summary>
    private void DetectAndApplyHits(ComboStep step, Vector3 aimDirection, float damage)
    {
        // Punkt ataku przed graczem na odpowiedniej wysokości
        Vector3 attackPoint = transform.position + (aimDirection * step.attackRange) + (Vector3.up * attackHeightOffset);

        damagedEntitiesThisSwing.Clear();

        // Bezalokacyjne pobranie koliderów w zasięgu sfery
        int hitCount = Physics.OverlapSphereNonAlloc(attackPoint, step.attackRadius, hitBuffer, damageableLayers);

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = hitBuffer[i];
            if (col == null) continue;

            // Zabezpieczenie przed samookaleczeniem gracza
            if (col.transform.root == transform.root) continue;

            // Wyszukanie interfejsu IDamageable na obiekcie lub rodzicach
            IDamageable damageable = col.GetComponentInParent<IDamageable>();
            if (damageable != null && damagedEntitiesThisSwing.Add(damageable))
            {
                // Wektor odrzutu w przestrzeni poziomej XZ
                Vector3 knockbackDir = col.bounds.center - transform.position;
                knockbackDir.y = 0f;

                if (knockbackDir.sqrMagnitude < 0.001f)
                {
                    knockbackDir = aimDirection;
                }
                else
                {
                    knockbackDir.Normalize();
                }

                damageable.TakeDamage(damage, knockbackDir, step.knockbackForce);
                OnHitLanded?.Invoke(damageable, damage, col.bounds.center);
            }
        }
    }

    /// <summary>
    /// Oblicza kierunek ataku na podstawie kursora myszy rzutowanego na płaszczyznę ziemi (Plane.Raycast)
    /// lub kierunku ruchu postaci.
    /// </summary>
    private Vector3 CalculateAimDirection()
    {
        if (aimMode == AimMode.MouseCursor || aimMode == AimMode.MouseWithMovementFallback)
        {
            Vector2 mouseScreenPos = GetMouseScreenPosition();
            Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);

            // 1. Sprawdź czy kursor myszy bezpośrednio wskazuje na wroga/obiekt do zaatakowania
            if (Physics.Raycast(ray, out RaycastHit hitInfo, 100f, damageableLayers))
            {
                if (hitInfo.collider.transform.root != transform.root)
                {
                    Vector3 toEnemy = hitInfo.collider.bounds.center - transform.position;
                    toEnemy.y = 0f;
                    if (toEnemy.sqrMagnitude > 0.04f)
                    {
                        return toEnemy.normalized;
                    }
                }
            }

            // 2. Rzutowanie promienia na płaszczyznę podłoża XZ na wysokości gracza
            Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

            if (groundPlane.Raycast(ray, out float enterDistance))
            {
                Vector3 worldHitPoint = ray.GetPoint(enterDistance);
                Vector3 directionToMouse = worldHitPoint - transform.position;
                directionToMouse.y = 0f;

                if (directionToMouse.sqrMagnitude > 0.04f)
                {
                    return directionToMouse.normalized;
                }
            }
        }

        // Fallback: kierunek ruchu z PlayerMovement
        if (playerMovement != null && playerMovement.WorldMoveDirection.sqrMagnitude > 0.01f)
        {
            Vector3 moveDir = playerMovement.WorldMoveDirection;
            moveDir.y = 0f;
            return moveDir.normalized;
        }

        // Ostateczny fallback: ostatnio zapamiętany kierunek ataku
        if (lastAimDirection.sqrMagnitude > 0.01f)
        {
            return lastAimDirection.normalized;
        }

        return transform.forward;
    }

    private Vector2 GetMouseScreenPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#endif

        return Vector2.zero;
    }

    /// <summary>
    /// Odwraca sprite'a (flipX) tak, aby podczas zamachu patrzył w kierunku ataku na ekranie.
    /// </summary>
    private void AlignSpriteFacing(Vector3 attackWorldDirection)
    {
        if (spriteRenderer == null || mainCamera == null) return;

        Vector3 camRight = mainCamera.transform.right;
        camRight.y = 0f;
        camRight.Normalize();

        float relativeX = Vector3.Dot(attackWorldDirection, camRight);

        if (relativeX > 0.05f)
        {
            spriteRenderer.flipX = false; // Spojrzenie w prawo
        }
        else if (relativeX < -0.05f)
        {
            spriteRenderer.flipX = true;  // Spojrzenie w lewo
        }
    }

    private void TriggerAnimator(int comboStep)
    {
        if (animator == null) return;

        animator.SetInteger(AnimParamComboStep, comboStep);
        animator.SetBool(AnimParamIsAttacking, true);
        animator.SetTrigger(AnimParamAttackTrigger);
    }

    public void ResetCombo()
    {
        currentComboIndex = 0;
        comboTimer = 0f;
        isAttacking = false;
    }

    private void ResetToDefaultCombo()
    {
        comboStages = new ComboStep[3]
        {
            new ComboStep { stepName = "Cios 1 (Lekki)", damageMultiplier = 1.0f, knockbackForce = 5f, attackRange = 1.2f, attackRadius = 0.9f, attackDuration = 0.25f },
            new ComboStep { stepName = "Cios 2 (Średni)", damageMultiplier = 1.35f, knockbackForce = 7.5f, attackRange = 1.3f, attackRadius = 1.0f, attackDuration = 0.28f },
            new ComboStep { stepName = "Cios 3 (Finisher)", damageMultiplier = 1.85f, knockbackForce = 13f, attackRange = 1.5f, attackRadius = 1.25f, attackDuration = 0.45f }
        };
    }

    /// <summary>
    /// Rysowanie sfery ataku (hitboxa) w edytorze Unity dla ułatwienia kalibracji.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (comboStages == null || comboStages.Length == 0) return;

        Vector3 forward = Application.isPlaying ? lastAimDirection : transform.forward;
        int previewIndex = Mathf.Clamp(currentComboIndex, 0, comboStages.Length - 1);
        ComboStep step = comboStages[previewIndex];

        Vector3 center = transform.position + (forward * step.attackRange) + (Vector3.up * attackHeightOffset);

        Gizmos.color = isAttacking ? new Color(1f, 0f, 0f, 0.45f) : new Color(1f, 0.92f, 0.016f, 0.35f);
        Gizmos.DrawSphere(center, step.attackRadius);
        Gizmos.color = isAttacking ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(center, step.attackRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position + Vector3.up * attackHeightOffset, forward * step.attackRange);
    }
}
