using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Kontroler liniowej strefy walki (Combat Zone / Arena) w stylu Don't Starve.
/// Po wejściu gracza w Trigger:
/// 1. Zamyka bramy wejściowe i wyjściowe,
/// 2. Spawnuje wrogów w falach w wyznaczonych punktach (Transform[] spawnPoints),
/// 3. Monitoruje eliminację przeciwników (poprzez zdarzenie OnDeath w EnemyAI lub zniszczenie obiektu),
/// 4. Po pokonaniu ostatniej fali otwiera bramę wyjściową i opcjonalnie spawnuje skrzynię z nagrodą.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class CombatZone : MonoBehaviour
{
    [System.Serializable]
    public class Wave
    {
        [Tooltip("Nazwa fali (np. Fala 1, Fala 2, Miniboss).")]
        public string waveName = "Fala 1";

        [Tooltip("Lista prefabrykatów wrogów do zrespawnowania w tej fali.")]
        public GameObject[] enemyPrefabs;

        [Tooltip("Opóźnienie w sekundach przed rozpoczęciem tej fali.")]
        public float delayBeforeWave = 1.0f;

        [Tooltip("Odstęp czasowy w sekundach pomiędzy pojawieniem się kolejnych wrogów w fali.")]
        public float delayBetweenEnemySpawns = 0.35f;
    }

    [Header("Bramy i Blokady")]
    [Tooltip("Obiekty bram wejściowych (zamykane za graczem po wejściu do strefy).")]
    [SerializeField] private GameObject[] entranceGates;

    [Tooltip("Obiekty bram wyjściowych (blokują przejście do kolejnego etapu do momentu oczyszczenia strefy).")]
    [SerializeField] private GameObject[] exitGates;

    [Tooltip("Czy otworzyć bramę wejściową po wygranej walce? (domyślnie false, aby wymusić ruch w przód).")]
    [SerializeField] private bool reopenEntranceOnClear = false;

    [Header("Punkty Spawnu")]
    [Tooltip("Wyznaczone punkty na mapie, w których pojawiają się wrogowie.")]
    [SerializeField] private Transform[] spawnPoints;

    [Tooltip("Opcjonalny prefab efektu cząsteczkowego pojawienia się wroga (np. dym, portal).")]
    [SerializeField] private GameObject spawnVfxPrefab;

    [Header("Fale Wrogów")]
    [Tooltip("Konfiguracja fal przeciwników w tej strefie walki.")]
    [SerializeField] private Wave[] waves = new Wave[1]
    {
        new Wave { waveName = "Fala 1", delayBeforeWave = 1f, delayBetweenEnemySpawns = 0.3f }
    };

    [Header("Nagroda (Skrzynka ze skarbem)")]
    [Tooltip("Opcjonalny prefab skrzynki z nagrodą pojawiającej się po ukończeniu walki.")]
    [SerializeField] private GameObject rewardChestPrefab;

    [Tooltip("Punkt spawnu skrzynki. Jeśli pusty, skrzynia pojawi się na środku strefy walki.")]
    [SerializeField] private Transform chestSpawnPoint;

    [Tooltip("Opcjonalny efekt cząsteczkowy pojawienia się skrzyni.")]
    [SerializeField] private GameObject chestSpawnVfxPrefab;

    [Header("Zdarzenia Unity")]
    [Tooltip("Wywoływane w momencie wejścia gracza i rozpoczęcia walki (zamknięcie bram).")]
    [SerializeField] private UnityEvent onCombatStarted;

    [Tooltip("Wywoływane przy starcie każdej kolejnej fali (np. do aktualizacji UI: aktualnaFala, liczbaFal).")]
    [SerializeField] private UnityEvent<int, int> onWaveStarted; // currentWave (1-based), totalWaves

    [Tooltip("Wywoływane po pokonaniu wszystkich wrogów i otwarciu bramy.")]
    [SerializeField] private UnityEvent onCombatCompleted;

    // Zdarzenia w kodzie C#
    public event Action OnZoneStarted;
    public event Action<int, int> OnZoneWaveStarted;
    public event Action OnZoneCompleted;

    // Stan wewnętrzny
    private Collider triggerCollider;
    private bool isZoneActive = false;
    private bool isZoneCompleted = false;
    private int currentWaveIndex = 0;
    private int aliveEnemiesInWave = 0;
    private readonly List<GameObject> currentWaveEnemies = new List<GameObject>();
    private Coroutine zoneSequenceRoutine;

    #region Publiczne Właściwości

    public bool IsActive => isZoneActive;
    public bool IsCompleted => isZoneCompleted;
    public int CurrentWave => isZoneActive ? (currentWaveIndex + 1) : 0;
    public int TotalWaves => waves != null ? waves.Length : 0;
    public int AliveEnemiesCount => aliveEnemiesInWave;

    #endregion

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }

        // Bramy wyjściowe powinny być zablokowane od początku lub otwarte w zależności od projektu
        // Domyślnie upewniamy się, że bramy wyjściowe są zamknięte, a wejściowe otwarte
        SetGatesState(entranceGates, false); // wejście otwarte
        SetGatesState(exitGates, true);      // wyjście zamknięte
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isZoneActive || isZoneCompleted) return;

        // Sprawdzamy czy wchodzący obiekt to Gracz
        if (other.CompareTag("Player") || other.GetComponent<PlayerMovement>() != null)
        {
            StartCombatZone();
        }
    }

    /// <summary>
    /// Aktywuje strefę walki, zatrzaskuje bramy i rozpoczyna sekwencję fal.
    /// </summary>
    public void StartCombatZone()
    {
        if (isZoneActive || isZoneCompleted) return;

        isZoneActive = true;

        // Wyłączamy trigger, aby nie wywoływał się ponownie
        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }

        // 1. Zamykamy bramy wejściowe i wyjściowe
        SetGatesState(entranceGates, true);
        SetGatesState(exitGates, true);

        // Wywołanie zdarzeń startowych
        onCombatStarted?.Invoke();
        OnZoneStarted?.Invoke();

        // 2. Rozpoczynamy sekwencję spawnu fal
        zoneSequenceRoutine = StartCoroutine(CombatZoneSequenceRoutine());
    }

    /// <summary>
    /// Główna pętla obsługująca kolejne fale wrogów w strefie.
    /// </summary>
    private IEnumerator CombatZoneSequenceRoutine()
    {
        if (waves == null || waves.Length == 0)
        {
            CompleteCombatZone();
            yield break;
        }

        for (currentWaveIndex = 0; currentWaveIndex < waves.Length; currentWaveIndex++)
        {
            Wave currentWave = waves[currentWaveIndex];

            // Odczekaj czas przed rozpoczęciem fali
            if (currentWave.delayBeforeWave > 0f)
            {
                yield return new WaitForSeconds(currentWave.delayBeforeWave);
            }

            // Powiadomienie o starcie fali
            onWaveStarted?.Invoke(currentWaveIndex + 1, waves.Length);
            OnZoneWaveStarted?.Invoke(currentWaveIndex + 1, waves.Length);

            currentWaveEnemies.Clear();
            aliveEnemiesInWave = 0;

            // Spawnowanie wrogów z danej fali
            if (currentWave.enemyPrefabs != null && currentWave.enemyPrefabs.Length > 0)
            {
                aliveEnemiesInWave = currentWave.enemyPrefabs.Length;

                for (int i = 0; i < currentWave.enemyPrefabs.Length; i++)
                {
                    GameObject enemyPrefab = currentWave.enemyPrefabs[i];
                    if (enemyPrefab != null)
                    {
                        Transform spawnPoint = GetSpawnPoint(i);
                        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : transform.position;
                        Quaternion spawnRotation = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;

                        // Efekt cząsteczkowy pojawienia się
                        if (spawnVfxPrefab != null)
                        {
                            Instantiate(spawnVfxPrefab, spawnPosition, Quaternion.identity);
                        }

                        // Utworzenie wroga
                        GameObject spawnedEnemy = Instantiate(enemyPrefab, spawnPosition, spawnRotation);
                        currentWaveEnemies.Add(spawnedEnemy);

                        // Podpięcie pod zdarzenie śmierci wroga
                        SubscribeToEnemyDeath(spawnedEnemy);
                    }
                    else
                    {
                        aliveEnemiesInWave--;
                    }

                    if (currentWave.delayBetweenEnemySpawns > 0f)
                    {
                        yield return new WaitForSeconds(currentWave.delayBetweenEnemySpawns);
                    }
                }
            }

            // Oczekiwanie na śmierć wszystkich wrogów z bieżącej fali
            while (aliveEnemiesInWave > 0)
            {
                // Zabezpieczenie: jeśli wrogowie zostali usunięci w inny sposób ze sceny (np. Destroy poza AI)
                ValidateLivingEnemies();

                yield return new WaitForSeconds(0.2f);
            }
        }

        // 3. Wszystkie fale ukończone -> zwycięstwo!
        CompleteCombatZone();
    }

    /// <summary>
    /// Nasłuchuje śmierci przeciwnika poprzez EnemyAI lub powiadomienie ogólne.
    /// </summary>
    private void SubscribeToEnemyDeath(GameObject enemyObj)
    {
        if (enemyObj == null) return;

        EnemyAI enemyAI = enemyObj.GetComponent<EnemyAI>();
        if (enemyAI != null)
        {
            enemyAI.OnDeath += HandleEnemyAIDeath;
        }
    }

    private void HandleEnemyAIDeath(EnemyAI enemyAI)
    {
        if (enemyAI != null)
        {
            enemyAI.OnDeath -= HandleEnemyAIDeath;
            currentWaveEnemies.Remove(enemyAI.gameObject);
        }

        aliveEnemiesInWave = Mathf.Max(0, aliveEnemiesInWave - 1);
    }

    /// <summary>
    /// Weryfikuje listę żyjących wrogów, usuwając obiekty będące null (np. zniszczone z zewnątrz).
    /// </summary>
    private void ValidateLivingEnemies()
    {
        int countBefore = currentWaveEnemies.Count;
        currentWaveEnemies.RemoveAll(enemy => enemy == null);
        int removedCount = countBefore - currentWaveEnemies.Count;

        if (removedCount > 0)
        {
            aliveEnemiesInWave = Mathf.Max(0, aliveEnemiesInWave - removedCount);
        }
    }

    /// <summary>
    /// Zakończenie strefy walki: otwarcie wyjścia i spawn skrzyni z nagrodą.
    /// </summary>
    private void CompleteCombatZone()
    {
        isZoneActive = false;
        isZoneCompleted = true;

        // Otwieramy bramy wyjściowe (wyłączamy obiekty blokujące)
        SetGatesState(exitGates, false);

        // Opcjonalne ponowne otwarcie wejścia
        if (reopenEntranceOnClear)
        {
            SetGatesState(entranceGates, false);
        }

        // Spawnowanie skrzyni z nagrodą
        SpawnRewardChest();

        // Wywołanie zdarzeń ukończenia
        onCombatCompleted?.Invoke();
        OnZoneCompleted?.Invoke();

        Debug.Log($"<b>[CombatZone]</b> Strefa walki {gameObject.name} została ukończona! Brama wyjściowa otwarta.");
    }

    private void SpawnRewardChest()
    {
        if (rewardChestPrefab == null) return;

        Vector3 spawnPos = chestSpawnPoint != null ? chestSpawnPoint.position : transform.position;

        if (chestSpawnVfxPrefab != null)
        {
            Instantiate(chestSpawnVfxPrefab, spawnPos, Quaternion.identity);
        }

        Instantiate(rewardChestPrefab, spawnPos, Quaternion.identity);
    }

    /// <summary>
    /// Aktywuje lub dezaktywuje obiekty bram.
    /// </summary>
    private void SetGatesState(GameObject[] gates, bool isClosed)
    {
        if (gates == null) return;

        foreach (GameObject gate in gates)
        {
            if (gate != null)
            {
                gate.SetActive(isClosed);
            }
        }
    }

    private Transform GetSpawnPoint(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return transform;
        return spawnPoints[index % spawnPoints.Length];
    }

    #region Gizmos (Podgląd w Edytorze)

    private void OnDrawGizmos()
    {
        // Rysowanie obszaru triggera
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.color = isZoneCompleted ? new Color(0f, 1f, 0f, 0.2f) :
                          (isZoneActive ? new Color(1f, 0f, 0f, 0.25f) : new Color(1f, 0.92f, 0.016f, 0.2f));

            if (col is BoxCollider box)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.DrawWireCube(box.center, box.size);
                Gizmos.matrix = Matrix4x4.identity;
            }
        }

        // Rysowanie punktów spawnu
        if (spawnPoints != null)
        {
            Gizmos.color = Color.magenta;
            foreach (Transform sp in spawnPoints)
            {
                if (sp != null)
                {
                    Gizmos.DrawSphere(sp.position, 0.45f);
                    Gizmos.DrawLine(transform.position, sp.position);
                }
            }
        }

        // Rysowanie punktu skrzyni
        if (chestSpawnPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(chestSpawnPoint.position + Vector3.up * 0.4f, new Vector3(0.8f, 0.8f, 0.8f));
        }
    }

    #endregion
}
