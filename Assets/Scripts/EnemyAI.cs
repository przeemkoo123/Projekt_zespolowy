using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

/// <summary>
/// Kontroler sztucznej inteligencji przeciwnika w grze 2.5D (styl Don't Starve).
/// Wykorzystuje NavMeshAgent na płaszczyźnie 3D (XZ) z wyłączonym obracaniem agenta (updateRotation = false),
/// zachowując orientację płaskiego sprite'a przez Billboard i odwracanie w lewo/prawo (flipX).
/// Posiada maszynę stanów: Idle, Patrol, Chase, Attack, Staggered, Dead oraz implementuje IDamageable.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[DisallowMultipleComponent]
public class EnemyAI : MonoBehaviour, IDamageable
{
    public enum State
    {
        Idle,
        Patrol,
        Chase,
        Attack,
        Staggered,
        Dead
    }

    [Header("Stan Początkowy")]
    [SerializeField] private State currentState = State.Idle;

    [Header("Zdrowie")]
    [SerializeField] private float maxHealth = 60f;
    private float currentHealth;

    [Header("Patrol i Spoczynek")]
    [Tooltip("Promień wokół pozycji startowej, w którym wróg szuka punktów patrolu.")]
    [SerializeField] private float patrolRadius = 8f;

    [Tooltip("Prędkość poruszania się podczas patrolu.")]
    [SerializeField] private float patrolSpeed = 2.2f;

    [Tooltip("Czas oczekiwania w stanie Idle po dotarciu do punktu patrolu.")]
    [SerializeField] private float idleWaitDuration = 2.5f;

    [Header("Pościg (Chase)")]
    [Tooltip("Promień wykrywania gracza.")]
    [SerializeField] private float detectionRadius = 7f;

    [Tooltip("Odległość, po której wróg gubi gracza i wraca do patrolu.")]
    [SerializeField] private float loseTargetRadius = 11f;

    [Tooltip("Prędkość biegu podczas pościgu.")]
    [SerializeField] private float chaseSpeed = 4.2f;

    [Tooltip("Odstęp czasowy między odświeżeniami ścieżki NavMesh do gracza.")]
    [SerializeField] private float pathUpdateInterval = 0.2f;

    [Header("Atak")]
    [Tooltip("Zasięg, w którym wróg zatrzymuje się i rozpoczyna sekwencję ataku.")]
    [SerializeField] private float attackRange = 1.6f;

    [Tooltip("Wartość obrażeń zadawanych graczowi.")]
    [SerializeField] private float attackDamage = 15f;

    [Tooltip("Siła odrzutu (knockback) zadawanego graczowi przy trafieniu.")]
    [SerializeField] private float attackKnockbackForce = 6f;

    [Tooltip("Czas trwania sygnalizacji ataku (wind-up), dający graczowi okno na wykonanie uniku/dasha.")]
    [SerializeField] private float windUpDuration = 0.45f;

    [Tooltip("Krótka pauza po ataku (recovery).")]
    [SerializeField] private float attackRecoveryDuration = 0.3f;

    [Tooltip("Czas odnowienia ataku (cooldown).")]
    [SerializeField] private float attackCooldown = 1.2f;

    [Header("Ogłuszenie (Staggered)")]
    [Tooltip("Czas trwania ogłuszenia po otrzymaniu obrażeń.")]
    [SerializeField] private float staggerDuration = 0.35f;

    [Header("Wizualia i Sprite (2.5D)")]
    [SerializeField] private SpriteRenderer spriteRenderer;

    [Tooltip("Kolor sygnalizacji ataku (wind-up) na spricie.")]
    [SerializeField] private Color telegraphColor = new Color(1f, 0.35f, 0.2f, 1f);

    [Tooltip("Kolor mignięcia po otrzymaniu ciosu.")]
    [SerializeField] private Color hitFlashColor = Color.white;

    [Tooltip("Odwraca domyślny kierunek flipX sprite'a.")]
    [SerializeField] private bool invertFlip = false;

    [Header("Loot i Śmierć")]
    [Tooltip("Prefabrykaty przedmiotów upuszczanych po śmierci wroga.")]
    [SerializeField] private GameObject[] lootPrefabs;

    [Range(0f, 1f)]
    [Tooltip("Szansa na upuszczenie każdego przedmiotu.")]
    [SerializeField] private float dropChance = 0.75f;

    [Tooltip("Czas po śmierci, po którym obiekt wroga jest usuwany ze sceny.")]
    [SerializeField] private float despawnDelay = 1.5f;

    [Tooltip("Zdarzenie Unity wywoływane w momencie śmierci (np. podpięcie audio lub efektów cząsteczkowych).")]
    [SerializeField] private UnityEvent<Vector3> onDeathEvent;

    // Zdarzenia w kodzie C#
    public event Action<EnemyAI> OnDeath;
    public event Action<Vector3> OnEnemyDiedWithPosition;

    // Komponenty wewnętrzne
    private NavMeshAgent agent;
    private Collider enemyCollider;
    private Transform playerTarget;
    private Transform cameraTransform;
    [SerializeField] private Animator animator;

    // Zmienne pomocnicze
    private Vector3 spawnPosition;
    private float stateTimer;
    private float pathTimer;
    private float attackCooldownTimer;
    private Color originalColor = Color.white;
    private Coroutine activeActionRoutine;

    // Hashe Animatora
    private static readonly int AnimParamSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimParamAttack = Animator.StringToHash("Attack");
    private static readonly int AnimParamIsStaggered = Animator.StringToHash("IsStaggered");
    private static readonly int AnimParamIsDead = Animator.StringToHash("IsDead");

    #region Publiczne Właściwości

    public State CurrentState => currentState;
    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDead => currentState == State.Dead;

    #endregion

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyCollider = GetComponent<Collider>();
        animator = GetComponentInChildren<Animator>();

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }

        // KLUCZOWE DLA 2.5D: wyłączamy obracanie obiektu przez agenta,
        // aby płaski sprite nie obracał się w osi Y/Z w świecie 3D (obrotem zajmuje się Billboard i flipX)
        agent.updateRotation = false;

        currentHealth = maxHealth;
        spawnPosition = transform.position;
    }

    private void Start()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        FindPlayer();
        ChangeState(State.Idle);
    }

    private void Update()
    {
        if (currentState == State.Dead) return;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (playerTarget == null)
        {
            FindPlayer();
        }

        // Odliczanie cooldownu ataku
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= Time.deltaTime;
        }

        // Aktualizacja maszyny stanów
        UpdateStateMachine();

        // Dopasowanie kierunku patrzenia sprite'a na ekranie
        UpdateSpriteFacing();

        // Aktualizacja Animatora
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

    #region Maszyna Stanów (State Machine)

    private void ChangeState(State newState)
    {
        if (currentState == State.Dead) return;

        // Sprzątanie po poprzednim stanie
        if (activeActionRoutine != null && (newState == State.Staggered || newState == State.Dead))
        {
            StopCoroutine(activeActionRoutine);
            activeActionRoutine = null;
            if (spriteRenderer != null) spriteRenderer.color = originalColor;
        }

        currentState = newState;
        stateTimer = 0f;

        switch (currentState)
        {
            case State.Idle:
                agent.isStopped = true;
                agent.ResetPath();
                break;

            case State.Patrol:
                agent.isStopped = false;
                agent.speed = patrolSpeed;
                SetRandomPatrolDestination();
                break;

            case State.Chase:
                agent.isStopped = false;
                agent.speed = chaseSpeed;
                pathTimer = 0f; // natychmiastowe wyznaczenie trasy do gracza
                break;

            case State.Attack:
                agent.isStopped = true;
                agent.ResetPath();
                activeActionRoutine = StartCoroutine(AttackSequenceRoutine());
                break;

            case State.Staggered:
                // Logika w StaggerAndKnockbackRoutine wywoływana z poziomu TakeDamage
                break;

            case State.Dead:
                HandleDeath();
                break;
        }
    }

    private void UpdateStateMachine()
    {
        float distanceToPlayer = playerTarget != null ? Vector3.Distance(transform.position, playerTarget.position) : float.MaxValue;

        switch (currentState)
        {
            case State.Idle:
                // Sprawdzenie wykrycia gracza
                if (distanceToPlayer <= detectionRadius)
                {
                    ChangeState(State.Chase);
                    return;
                }

                // Oczekiwanie i przejście do patrolu
                stateTimer += Time.deltaTime;
                if (stateTimer >= idleWaitDuration)
                {
                    ChangeState(State.Patrol);
                }
                break;

            case State.Patrol:
                // Sprawdzenie wykrycia gracza
                if (distanceToPlayer <= detectionRadius)
                {
                    ChangeState(State.Chase);
                    return;
                }

                // Sprawdzenie, czy agent dotarł do celu patrolu
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.25f)
                {
                    ChangeState(State.Idle);
                }
                break;

            case State.Chase:
                // Sprawdzenie zgubienia gracza
                if (distanceToPlayer > loseTargetRadius)
                {
                    ChangeState(State.Idle);
                    return;
                }

                // Sprawdzenie wejścia w zasięg ataku
                if (distanceToPlayer <= attackRange && attackCooldownTimer <= 0f)
                {
                    ChangeState(State.Attack);
                    return;
                }

                // Płynna aktualizacja celu pościgu w odstępach czasowych (optymalizacja NavMesh)
                pathTimer -= Time.deltaTime;
                if (pathTimer <= 0f && playerTarget != null)
                {
                    pathTimer = pathUpdateInterval;
                    agent.SetDestination(playerTarget.position);
                }
                break;

            case State.Attack:
            case State.Staggered:
            case State.Dead:
                // Obsługiwane przez coroutines lub zdarzenia
                break;
        }
    }

    #endregion

    #region Logika Patrolu

    private void SetRandomPatrolDestination()
    {
        if (GetRandomNavMeshPoint(spawnPosition, patrolRadius, out Vector3 targetPoint))
        {
            agent.SetDestination(targetPoint);
        }
        else
        {
            ChangeState(State.Idle);
        }
    }

    private bool GetRandomNavMeshPoint(Vector3 center, float radius, out Vector3 result)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * radius;
            Vector3 randomPoint = center + new Vector3(randomCircle.x, 0f, randomCircle.y);

            if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }

        result = center;
        return false;
    }

    #endregion

    #region Logika Ataku

    private IEnumerator AttackSequenceRoutine()
    {
        // 1. FAZA WIND-UP (Sygnalizacja ataku / telegrafowanie)
        if (spriteRenderer != null)
        {
            spriteRenderer.color = telegraphColor;
        }

        yield return new WaitForSeconds(windUpDuration);

        if (currentState != State.Attack) yield break;

        // 2. FAZA CIOSU (Strike)
        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        if (animator != null)
        {
            animator.SetTrigger(AnimParamAttack);
        }

        // Zadanie obrażeń graczowi (jeśli nadal jest w zasięgu)
        if (playerTarget != null)
        {
            float distance = Vector3.Distance(transform.position, playerTarget.position);
            if (distance <= attackRange * 1.3f)
            {
                // Sprawdzenie, czy gracz nie wykonuje dasha z i-frames
                PlayerMovement playerMovement = playerTarget.GetComponent<PlayerMovement>();
                bool isInvulnerable = playerMovement != null && playerMovement.IsInvulnerable;

                if (!isInvulnerable)
                {
                    IDamageable damageable = playerTarget.GetComponent<IDamageable>();
                    if (damageable != null)
                    {
                        Vector3 knockbackDir = playerTarget.position - transform.position;
                        knockbackDir.y = 0f;
                        knockbackDir = knockbackDir.sqrMagnitude > 0.001f ? knockbackDir.normalized : transform.forward;

                        damageable.TakeDamage(attackDamage, knockbackDir, attackKnockbackForce);
                    }
                }
            }
        }

        // 3. FAZA RECOVERY (Odpoczynek po zamachu)
        yield return new WaitForSeconds(attackRecoveryDuration);

        attackCooldownTimer = attackCooldown;

        if (currentState == State.Attack)
        {
            ChangeState(State.Chase);
        }
    }

    #endregion

    #region Implementacja IDamageable i Odrzut (Knockback)

    public void TakeDamage(float amount, Vector3 knockbackDirection, float knockbackForce)
    {
        if (currentState == State.Dead) return;

        currentHealth -= amount;

        if (currentHealth <= 0f)
        {
            ChangeState(State.Dead);
        }
        else
        {
            // Przerwij obecny stan i przejdź w Stagger z odrzutem
            if (activeActionRoutine != null)
            {
                StopCoroutine(activeActionRoutine);
            }
            activeActionRoutine = StartCoroutine(StaggerAndKnockbackRoutine(knockbackDirection, knockbackForce));
        }
    }

    private IEnumerator StaggerAndKnockbackRoutine(Vector3 knockbackDir, float knockbackForce)
    {
        currentState = State.Staggered;
        agent.isStopped = true;
        agent.ResetPath();

        if (animator != null)
        {
            animator.SetTrigger(AnimParamIsStaggered);
        }

        // Mignięcie sprita po uderzeniu
        if (spriteRenderer != null)
        {
            spriteRenderer.color = hitFlashColor;
        }

        float elapsed = 0f;
        float duration = staggerDuration;

        // Płynne przemieszczenie agenta w kierunku knockbacku (agent.Move respektuje granice NavMesha)
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float currentForce = Mathf.Lerp(knockbackForce, 0f, elapsed / duration);

            if (agent.isOnNavMesh && currentForce > 0.01f)
            {
                agent.Move(knockbackDir * (currentForce * Time.deltaTime));
            }

            yield return null;
        }

        if (spriteRenderer != null)
        {
            spriteRenderer.color = originalColor;
        }

        // Powrót do gry po ogłuszeniu
        if (currentState == State.Staggered)
        {
            agent.isStopped = false;
            float dist = playerTarget != null ? Vector3.Distance(transform.position, playerTarget.position) : float.MaxValue;

            if (dist <= loseTargetRadius)
            {
                ChangeState(State.Chase);
            }
            else
            {
                ChangeState(State.Idle);
            }
        }
    }

    #endregion

    #region Śmierć i Spawnowanie Lootu

    private void HandleDeath()
    {
        agent.isStopped = true;
        agent.enabled = false;

        if (enemyCollider != null)
        {
            enemyCollider.enabled = false;
        }

        if (animator != null)
        {
            animator.SetBool(AnimParamIsDead, true);
        }

        // Wywołanie zdarzeń śmierci
        Vector3 deathPos = transform.position;
        OnDeath?.Invoke(this);
        OnEnemyDiedWithPosition?.Invoke(deathPos);
        onDeathEvent?.Invoke(deathPos);

        // Wypadanie przedmiotów (Loot)
        SpawnLoot(deathPos);

        // Usunięcie obiektu ze sceny z opóźnieniem
        Destroy(gameObject, despawnDelay);
    }

    private void SpawnLoot(Vector3 position)
    {
        if (lootPrefabs == null || lootPrefabs.Length == 0) return;

        foreach (GameObject loot in lootPrefabs)
        {
            if (loot == null) continue;

            if (UnityEngine.Random.value <= dropChance)
            {
                // Lekki losowy rozrzut lootu na ziemi
                Vector2 randomOffset = UnityEngine.Random.insideUnitCircle * 0.6f;
                Vector3 spawnPos = position + new Vector3(randomOffset.x, 0.4f, randomOffset.y);
                Instantiate(loot, spawnPos, Quaternion.identity);
            }
        }
    }

    #endregion

    #region Orientacja Sprite'a (2.5D FlipX)

    private void UpdateSpriteFacing()
    {
        if (spriteRenderer == null || cameraTransform == null) return;

        Vector3 camRight = cameraTransform.right;
        camRight.y = 0f;
        camRight.Normalize();

        Vector3 directionToEvaluate = Vector3.zero;

        // Jeśli atakujemy lub jesteśmy bardzo blisko, patrzymy w stronę gracza
        if ((currentState == State.Attack || currentState == State.Staggered) && playerTarget != null)
        {
            directionToEvaluate = playerTarget.position - transform.position;
        }
        // W biegu/patrolu patrzymy w kierunku poruszania się
        else if (agent.enabled && agent.velocity.sqrMagnitude > 0.05f)
        {
            directionToEvaluate = agent.velocity;
        }

        if (directionToEvaluate.sqrMagnitude > 0.001f)
        {
            float relativeX = Vector3.Dot(directionToEvaluate, camRight);

            if (relativeX > 0.05f)
            {
                spriteRenderer.flipX = invertFlip;  // Spojrzenie w prawo
            }
            else if (relativeX < -0.05f)
            {
                spriteRenderer.flipX = !invertFlip; // Spojrzenie w lewo
            }
        }
    }

    private void UpdateAnimator()
    {
        if (animator == null) return;

        float speed = agent.enabled ? agent.velocity.magnitude : 0f;
        animator.SetFloat(AnimParamSpeed, speed);
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        // Promień patrolu
        Gizmos.color = Color.cyan;
        Vector3 origin = Application.isPlaying ? spawnPosition : transform.position;
        Gizmos.DrawWireSphere(origin, patrolRadius);

        // Promień wykrywania gracza
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Promień zgubienia gracza
        Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, loseTargetRadius);

        // Zasięg ataku
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }

    #endregion
}
