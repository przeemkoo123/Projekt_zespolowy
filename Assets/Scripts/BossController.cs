using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Kontroler finałowego bossa dla gry 2.5D Action-RPG (styl Don't Starve).
/// Wykorzystuje NavMeshAgent (updateRotation = false) oraz implementuje IDamageable.
/// Posiada dwie fazy:
/// - Faza 1: Naprzemienny atak (Szeroki zamach mieczem oraz Szarża w linii prostej z czerwonym wskaźnikiem).
/// - Przejście do Fazy 2 (<50% HP): Okrzyk bojowy, chwilowa nietykalność, podbicie prędkości o 30%.
/// - Faza 2: Podstawowe ataki + co 15 sekund potężny atak obszarowy (AoE Ground Slam) i przyzwanie 2 sługusów.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class BossController : MonoBehaviour, IDamageable
{
    public enum BossPhase
    {
        Phase1,
        Phase2
    }

    public enum BossActionState
    {
        Idle,
        Chasing,
        Attacking,
        Charging,
        GroundSlamAoE,
        PhaseTransition,
        Dead
    }

    [Header("Informacje i Zdrowie")]
    [SerializeField] private string bossName = "Władca Cieni";
    [SerializeField] private float maxHealth = 500f;
    private float currentHealth;

    [Header("Fazy i Prędkość")]
    [Tooltip("Bazowa prędkość poruszania się w Fazie 1.")]
    [SerializeField] private float phase1MoveSpeed = 3.8f;

    [Tooltip("Mnożnik prędkości w Fazie 2 (domyślnie +30%).")]
    [SerializeField] private float phase2SpeedMultiplier = 1.3f;

    [Header("Atak 1: Szeroki Zamach Mieczem")]
    [SerializeField] private float slashRange = 2.4f;
    [SerializeField] private float slashDamage = 25f;
    [SerializeField] private float slashRadius = 2.2f;
    [SerializeField] private float slashKnockbackForce = 10f;
    [SerializeField] private float slashWindUpDuration = 0.5f;
    [SerializeField] private float slashRecoveryDuration = 0.4f;

    [Header("Atak 2: Szarża w Linii Prostej")]
    [SerializeField] private float chargeDistance = 12f;
    [SerializeField] private float chargeSpeed = 16f;
    [SerializeField] private float chargeDamage = 35f;
    [SerializeField] private float chargeKnockbackForce = 18f;
    [SerializeField] private float chargeWindUpDuration = 0.85f;
    [SerializeField] private float chargeRecoveryDuration = 0.6f;
    [Tooltip("Szerokość czerwonej linii wskaźnika szarży na ziemi.")]
    [SerializeField] private float chargeIndicatorWidth = 1.2f;

    [Header("Faza 2: Atak AoE (Ground Slam) i Przywołanie")]
    [Tooltip("Odstęp czasowy w sekundach pomiędzy atakami AoE w Fazie 2.")]
    [SerializeField] private float aoeInterval = 15.0f;
    [SerializeField] private float aoeRadius = 5.5f;
    [SerializeField] private float aoeDamage = 40f;
    [SerializeField] private float aoeKnockbackForce = 14f;
    [SerializeField] private float aoeWindUpDuration = 1.0f;
    [SerializeField] private float aoeRecoveryDuration = 0.5f;

    [Tooltip("Prefab miniona przywoływanego podczas ataku AoE.")]
    [SerializeField] private GameObject minionPrefab;

    [Tooltip("Punkty spawnu minionów wokół bossa. Jeśli puste, powstaną po bokach bossa.")]
    [SerializeField] private Transform[] minionSpawnPoints;

    [Header("Warstwy Trafień")]
    [Tooltip("Warstwy, na których boss zadaje obrażenia (zazwyczaj Gracz).")]
    [SerializeField] private LayerMask targetLayers;

    [Header("Wizualia 2.5D")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Color phase1Color = Color.white;
    [SerializeField] private Color phase2Color = new Color(1f, 0.5f, 0.5f, 1f);
    [SerializeField] private Color telegraphColor = new Color(1f, 0.25f, 0.15f, 1f);
    [SerializeField] private Color invulnerableColor = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private bool invertFlip = false;

    [Header("Zdarzenia Unity")]
    [SerializeField] private UnityEvent<float, float> onHealthChanged; // current, max
    [SerializeField] private UnityEvent onPhase2Started;
    [SerializeField] private UnityEvent onBossDefeated;

    // Zdarzenia w C#
    public event Action<float, float> OnHealthChanged;
    public event Action<BossPhase> OnPhaseChanged;
    public event Action OnBossDefeated;

    // Komponenty wewnętrzne
    private NavMeshAgent agent;
    private Collider bossCollider;
    private Transform playerTarget;
    private Transform cameraTransform;
    private Animator animator;
    private LineRenderer chargeLineRenderer;

    // Stan walki
    private BossPhase currentPhase = BossPhase.Phase1;
    private BossActionState currentActionState = BossActionState.Idle;
    private bool isInvulnerable = false;
    private bool useSlashNext = true;
    private float aoeCooldownTimer = 0f;
    private Coroutine mainCombatLoopRoutine;
    private readonly Collider[] hitBuffer = new Collider[16];

    // Hashe Animatora
    private static readonly int AnimParamSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimParamSlash = Animator.StringToHash("Slash");
    private static readonly int AnimParamCharge = Animator.StringToHash("Charge");
    private static readonly int AnimParamSlam = Animator.StringToHash("GroundSlam");
    private static readonly int AnimParamRoar = Animator.StringToHash("Roar");

    #region Publiczne Właściwości

    public string BossName => bossName;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float HealthNormalized => Mathf.Clamp01(currentHealth / maxHealth);
    public BossPhase CurrentPhase => currentPhase;
    public BossActionState CurrentActionState => currentActionState;
    public bool IsInvulnerable => isInvulnerable;
    public bool IsDead => currentActionState == BossActionState.Dead;

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        bossCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>();

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        // KLUCZOWE DLA 2.5D: NavMeshAgent nie obraca obiektu 3D
        agent.updateRotation = false;

        currentHealth = maxHealth;
        agent.speed = phase1MoveSpeed;

        SetupChargeLineRenderer();
    }

    private void Start()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        FindPlayer();
        mainCombatLoopRoutine = StartCoroutine(BossBehaviorLoop());
    }

    private void Update()
    {
        if (currentActionState == BossActionState.Dead) return;

        if (playerTarget == null)
        {
            FindPlayer();
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Odliczanie timera AoE w Fazie 2
        if (currentPhase == BossPhase.Phase2 && aoeCooldownTimer > 0f)
        {
            aoeCooldownTimer -= Time.deltaTime;
        }

        UpdateSpriteFacing();
        UpdateAnimator();
    }

    private void FindPlayer()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            playerTarget = playerObj.transform;
        }
    }

    /// <summary>
    /// Główna pętla zachowania bossa naprzemiennie realizująca ataki.
    /// </summary>
    private IEnumerator BossBehaviorLoop()
    {
        // Krótka pauza po starcie walki
        yield return new WaitForSeconds(1.2f);

        while (currentActionState != BossActionState.Dead)
        {
            if (playerTarget == null)
            {
                yield return new WaitForSeconds(0.5f);
                continue;
            }

            // FAZA 2: Priorytet dla ataku AoE i przywołania co 15 sekund
            if (currentPhase == BossPhase.Phase2 && aoeCooldownTimer <= 0f)
            {
                yield return StartCoroutine(GroundSlamAndSummonRoutine());
                aoeCooldownTimer = aoeInterval;
            }
            else
            {
                // Naprzemienny wybór ataków: (1) Zamach mieczem, (2) Szarża
                if (useSlashNext)
                {
                    yield return StartCoroutine(SwordSlashRoutine());
                    useSlashNext = false;
                }
                else
                {
                    yield return StartCoroutine(StraightChargeRoutine());
                    useSlashNext = true;
                }
            }

            // Odpoczynek pomiędzy atakami (krótszy w Fazie 2 dla większej dynamiki)
            float pauseBetweenAttacks = (currentPhase == BossPhase.Phase1) ? 1.0f : 0.65f;
            currentActionState = BossActionState.Idle;
            yield return new WaitForSeconds(pauseBetweenAttacks);
        }
    }

    #region Atak 1: Szeroki Zamach Mieczem

    private IEnumerator SwordSlashRoutine()
    {
        currentActionState = BossActionState.Chasing;
        agent.isStopped = false;

        // 1. Podbieg do gracza na odległość ataku
        float chaseTimeout = 6.0f;
        while (playerTarget != null && Vector3.Distance(transform.position, playerTarget.position) > slashRange && chaseTimeout > 0f)
        {
            agent.SetDestination(playerTarget.position);
            chaseTimeout -= Time.deltaTime;
            yield return null;
        }

        // 2. Faza Wind-up (sygnalizacja ataku)
        currentActionState = BossActionState.Attacking;
        agent.isStopped = true;
        agent.ResetPath();

        SetSpriteColor(telegraphColor);
        yield return new WaitForSeconds(slashWindUpDuration);

        // 3. Faza Ciosu (Strike)
        ResetSpriteColor();
        if (animator != null) animator.SetTrigger(AnimParamSlash);

        // Kierunek zamachu w stronę gracza
        Vector3 forwardDir = GetDirectionToPlayer();
        Vector3 slashCenter = transform.position + forwardDir * 1.4f + Vector3.up * 0.8f;

        int hitCount = Physics.OverlapSphereNonAlloc(slashCenter, slashRadius, hitBuffer, targetLayers);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = hitBuffer[i];
            if (col == null || col.transform.root == transform.root) continue;

            DealDamageToTarget(col, slashDamage, forwardDir, slashKnockbackForce);
        }

        // 4. Faza Recovery
        yield return new WaitForSeconds(slashRecoveryDuration);
    }

    #endregion

    #region Atak 2: Szarża w Linii Prostej ze Wskaźnikiem

    private IEnumerator StraightChargeRoutine()
    {
        currentActionState = BossActionState.Charging;
        agent.isStopped = true;
        agent.ResetPath();

        // 1. Zablokowanie pozycji gracza i kierunku szarży
        Vector3 chargeDirection = GetDirectionToPlayer();
        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + chargeDirection * chargeDistance;

        // 2. Rysowanie czerwonego wskaźnika szarży na podłożu
        ShowChargeIndicator(startPos, targetPos);
        SetSpriteColor(telegraphColor);

        yield return new WaitForSeconds(chargeWindUpDuration);

        HideChargeIndicator();
        ResetSpriteColor();

        if (animator != null) animator.SetTrigger(AnimParamCharge);

        // 3. Dynamiczna szarża w linii prostej
        float chargeTime = chargeDistance / chargeSpeed;
        float elapsed = 0f;
        bool hasHitPlayer = false;

        while (elapsed < chargeTime)
        {
            elapsed += Time.deltaTime;
            Vector3 movement = chargeDirection * (chargeSpeed * Time.deltaTime);

            if (agent.isOnNavMesh)
            {
                agent.Move(movement);
            }

            // Sprawdzanie trafienia gracza w trakcie biegu
            if (!hasHitPlayer)
            {
                int hits = Physics.OverlapSphereNonAlloc(transform.position + Vector3.up * 0.8f, 1.3f, hitBuffer, targetLayers);
                for (int i = 0; i < hits; i++)
                {
                    Collider col = hitBuffer[i];
                    if (col == null || col.transform.root == transform.root) continue;

                    DealDamageToTarget(col, chargeDamage, chargeDirection, chargeKnockbackForce);
                    hasHitPlayer = true;
                    break;
                }
            }

            yield return null;
        }

        // 4. Krótki postój / ochłonięcie po uderzeniu szarży
        yield return new WaitForSeconds(chargeRecoveryDuration);
    }

    #endregion

    #region Faza 2: AoE Ground Slam i Przywołanie

    private IEnumerator GroundSlamAndSummonRoutine()
    {
        currentActionState = BossActionState.GroundSlamAoE;
        agent.isStopped = true;
        agent.ResetPath();

        // 1. Sygnalizacja AoE (wind-up)
        SetSpriteColor(new Color(1f, 0.15f, 0.15f, 1f));
        yield return new WaitForSeconds(aoeWindUpDuration);

        // 2. Uderzenie w ziemię (Slam)
        ResetSpriteColor();
        if (animator != null) animator.SetTrigger(AnimParamSlam);

        // Zadanie obrażeń w promieniu 360 stopni wokół bossa
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, aoeRadius, hitBuffer, targetLayers);
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = hitBuffer[i];
            if (col == null || col.transform.root == transform.root) continue;

            Vector3 knockbackDir = (col.bounds.center - transform.position);
            knockbackDir.y = 0f;
            knockbackDir = knockbackDir.sqrMagnitude > 0.001f ? knockbackDir.normalized : transform.forward;

            DealDamageToTarget(col, aoeDamage, knockbackDir, aoeKnockbackForce);
        }

        // 3. Przywołanie 2 minionów
        SummonMinions();

        yield return new WaitForSeconds(aoeRecoveryDuration);
    }

    private void SummonMinions()
    {
        if (minionPrefab == null) return;

        int summonCount = 2;

        for (int i = 0; i < summonCount; i++)
        {
            Vector3 spawnPos;

            if (minionSpawnPoints != null && minionSpawnPoints.Length > i && minionSpawnPoints[i] != null)
            {
                spawnPos = minionSpawnPoints[i].position;
            }
            else
            {
                // Generowanie punktu na NavMeshu po lewej i prawej stronie bossa
                float sideOffset = (i == 0) ? -2.8f : 2.8f;
                Vector3 roughPos = transform.position + transform.right * sideOffset + transform.forward * 1.5f;

                if (NavMesh.SamplePosition(roughPos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                {
                    spawnPos = hit.position;
                }
                else
                {
                    spawnPos = transform.position;
                }
            }

            Instantiate(minionPrefab, spawnPos, Quaternion.identity);
        }
    }

    #endregion

    #region Przejście do Fazy 2 (< 50% HP)

    private void CheckPhaseTransition()
    {
        if (currentPhase == BossPhase.Phase1 && currentHealth <= (maxHealth * 0.5f))
        {
            StartCoroutine(PhaseTransitionRoutine());
        }
    }

    private IEnumerator PhaseTransitionRoutine()
    {
        currentPhase = BossPhase.Phase2;
        currentActionState = BossActionState.PhaseTransition;

        // Przerwanie bieżących działań
        if (mainCombatLoopRoutine != null)
        {
            StopCoroutine(mainCombatLoopRoutine);
        }

        agent.isStopped = true;
        agent.ResetPath();
        HideChargeIndicator();

        // 1. Chwilowa nieśmiertelność
        isInvulnerable = true;
        SetSpriteColor(invulnerableColor);

        // 2. Animacja / Okrzyk bojowy
        if (animator != null) animator.SetTrigger(AnimParamRoar);

        yield return new WaitForSeconds(2.0f);

        // 3. Włączenie bonusów Fazy 2 (+30% prędkości)
        isInvulnerable = false;
        agent.speed = phase1MoveSpeed * phase2SpeedMultiplier;
        aoeCooldownTimer = 2.0f; // pierwszy slam wkrótce po przejściu

        ResetSpriteColor();

        onPhase2Started?.Invoke();
        OnPhaseChanged?.Invoke(BossPhase.Phase2);

        // Wznowienie pętli walki
        mainCombatLoopRoutine = StartCoroutine(BossBehaviorLoop());
    }

    #endregion

    #region Implementacja IDamageable

    public void TakeDamage(float amount, Vector3 knockbackDirection, float knockbackForce)
    {
        if (isInvulnerable || currentActionState == BossActionState.Dead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);

        onHealthChanged?.Invoke(currentHealth, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        // Krótkie mignięcie sprita na biało
        StartCoroutine(HitFlashRoutine());

        // Sprawdzenie progu Fazy 2
        CheckPhaseTransition();

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private IEnumerator HitFlashRoutine()
    {
        if (spriteRenderer != null && currentActionState != BossActionState.PhaseTransition)
        {
            Color prev = (currentPhase == BossPhase.Phase2) ? phase2Color : phase1Color;
            spriteRenderer.color = Color.white;
            yield return new WaitForSeconds(0.08f);
            if (spriteRenderer != null && currentActionState != BossActionState.Dead)
            {
                spriteRenderer.color = prev;
            }
        }
    }

    private void Die()
    {
        currentActionState = BossActionState.Dead;

        if (mainCombatLoopRoutine != null)
        {
            StopCoroutine(mainCombatLoopRoutine);
        }

        if (agent != null)
        {
            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
            agent.enabled = false;
        }

        if (bossCollider != null)
        {
            bossCollider.enabled = false;
        }

        HideChargeIndicator();

        bool hasDeathClip = DeathAnimationHelper.TryTriggerDeathState(
            animator, AnimParamSlash, AnimParamCharge, AnimParamSlam, AnimParamRoar);

        onBossDefeated?.Invoke();
        OnBossDefeated?.Invoke();

        Debug.Log($"<b>[BossController]</b> Boss {bossName} został pokonany!");
        StartCoroutine(BossDeathFadeRoutine(hasDeathClip));
    }

    private IEnumerator BossDeathFadeRoutine(bool hasDeathClip)
    {
        float totalDuration = 3.5f;
        float fadeDuration = 0.8f;
        float fadeStartTime = totalDuration - fadeDuration;

        Color baseColor = (currentPhase == BossPhase.Phase2) ? phase2Color : phase1Color;
        Transform visualTransform = spriteRenderer != null ? spriteRenderer.transform : transform;
        Vector3 startScale = visualTransform.localScale;
        DeathAnimationHelper.PrepareFallbackVisual(spriteRenderer, transform, hasDeathClip);

        float elapsed = 0f;
        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;
            DeathAnimationHelper.UpdateVisualDeathFrame(
                spriteRenderer, transform, startScale, baseColor, Color.white,
                hasDeathClip, elapsed, 0.08f, fadeStartTime, fadeDuration);
            yield return null;
        }

        Destroy(gameObject);
    }

    #endregion

    #region Zadawanie Obrażeń z Uwzględnieniem Dash Gracza

    private void DealDamageToTarget(Collider col, float damage, Vector3 knockbackDir, float force)
    {
        // Sprawdzenie, czy gracz nie posiada aktywnych klatek nieśmiertelności (Dash)
        PlayerMovement playerMovement = col.GetComponent<PlayerMovement>();
        if (playerMovement != null && playerMovement.IsInvulnerable)
        {
            Debug.Log("[BossController] Gracz uniknął ataku bossa dzięki i-frames (Dash)!");
            return;
        }

        IDamageable damageable = col.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damage, knockbackDir, force);
        }
    }

    #endregion

    #region Wskaźnik Szarży i Wizualia 2.5D

    private void SetupChargeLineRenderer()
    {
        chargeLineRenderer = gameObject.AddComponent<LineRenderer>();
        chargeLineRenderer.positionCount = 2;
        chargeLineRenderer.startWidth = chargeIndicatorWidth;
        chargeLineRenderer.endWidth = chargeIndicatorWidth;
        chargeLineRenderer.useWorldSpace = true;
        chargeLineRenderer.enabled = false;

        // Materiał wskaźnika (płaski czerwony kolor)
        Material lineMat = new Material(Shader.Find("Sprites/Default"));
        Color redTelegraph = new Color(1f, 0f, 0f, 0.45f);
        lineMat.color = redTelegraph;
        chargeLineRenderer.material = lineMat;
        chargeLineRenderer.startColor = redTelegraph;
        chargeLineRenderer.endColor = redTelegraph;
    }

    private void ShowChargeIndicator(Vector3 start, Vector3 end)
    {
        if (chargeLineRenderer == null) return;

        // Lekkie podniesienie Y, aby linia nie przecinała się z podłożem (Z-fighting)
        Vector3 offset = Vector3.up * 0.08f;
        chargeLineRenderer.SetPosition(0, start + offset);
        chargeLineRenderer.SetPosition(1, end + offset);
        chargeLineRenderer.enabled = true;
    }

    private void HideChargeIndicator()
    {
        if (chargeLineRenderer != null)
        {
            chargeLineRenderer.enabled = false;
        }
    }

    private Vector3 GetDirectionToPlayer()
    {
        if (playerTarget == null) return transform.forward;

        Vector3 dir = playerTarget.position - transform.position;
        dir.y = 0f;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
    }

    private void UpdateSpriteFacing()
    {
        if (spriteRenderer == null || cameraTransform == null) return;

        Vector3 camRight = cameraTransform.right;
        camRight.y = 0f;
        camRight.Normalize();

        Vector3 evaluateDir = Vector3.zero;

        // Podczas celowania/ataku patrzymy w stronę gracza
        if (currentActionState == BossActionState.Attacking || currentActionState == BossActionState.Charging)
        {
            evaluateDir = GetDirectionToPlayer();
        }
        else if (agent.enabled && agent.velocity.sqrMagnitude > 0.05f)
        {
            evaluateDir = agent.velocity;
        }

        if (evaluateDir.sqrMagnitude > 0.001f)
        {
            float relativeX = Vector3.Dot(evaluateDir, camRight);

            if (relativeX > 0.05f)
            {
                spriteRenderer.flipX = invertFlip;  // Prawo na ekranie
            }
            else if (relativeX < -0.05f)
            {
                spriteRenderer.flipX = !invertFlip; // Lewo na ekranie
            }
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        float speed = agent.enabled ? agent.velocity.magnitude : 0f;
        animator.SetFloat(AnimParamSpeed, speed);
    }

    private void SetSpriteColor(Color color)
    {
        if (spriteRenderer != null) spriteRenderer.color = color;
    }

    private void ResetSpriteColor()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = (currentPhase == BossPhase.Phase2) ? phase2Color : phase1Color;
        }
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        // Zasięg i sfera zamachu mieczem
        Gizmos.color = Color.yellow;
        Vector3 forward = Application.isPlaying ? GetDirectionToPlayer() : transform.forward;
        Gizmos.DrawWireSphere(transform.position, slashRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + forward * 1.4f + Vector3.up * 0.8f, slashRadius);

        // Zasięg szarży
        Gizmos.color = Color.magenta;
        Gizmos.DrawRay(transform.position + Vector3.up * 0.1f, forward * chargeDistance);

        // Promień ataku AoE
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aoeRadius);
    }

    #endregion
}
