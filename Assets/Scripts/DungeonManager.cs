using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Generator losowego rozkładu pomieszczeń (dungeon) oraz menedżer rozgrywki.
/// - Generuje proceduralny układ pomieszczeń połączonych otwartymi korytarzami (bez blokujących bram/ścian),
/// - Umieszcza w każdym pomieszczeniu wrogów ze skryptem EnemyAI,
/// - Piecze w locie NavMesh (NavMeshSurface) dla całego wygenerowanego poziomu,
/// - Obsługuje restart gry ("Graj od nowa") z nowym losowym układem i zresetowanymi przeciwnikami.
/// </summary>
[DisallowMultipleComponent]
public class DungeonManager : MonoBehaviour
{
    public static DungeonManager Instance { get; private set; }

    [Header("Ustawienia Układu Pomieszczeń")]
    [Tooltip("Liczba generowanych pomieszczeń.")]
    [Range(3, 10)]
    [SerializeField] private int numberOfRooms = 5;

    [Tooltip("Szerokość i długość pojedynczego pomieszczenia w metrach.")]
    [SerializeField] private float roomSize = 18f;

    [Tooltip("Długość korytarza łączącego dwa sąsiednie pomieszczenia.")]
    [SerializeField] private float corridorLength = 10f;

    [Tooltip("Szerokość przejścia / korytarza (otwarta przestrzeń bez ścian).")]
    [SerializeField] private float corridorWidth = 6f;

    [Header("Wrogowie w pomieszczeniach")]
    [Tooltip("Minimalna liczba wrogów w pojedynczym pokoju bojowym.")]
    [SerializeField] private int minEnemiesPerRoom = 2;

    [Tooltip("Maksymalna liczba wrogów w pojedynczym pokoju bojowym.")]
    [SerializeField] private int maxEnemiesPerRoom = 4;

    [Header("Materiały Podłoża (Opcjonalne - pobiorą się automatycznie jeśli puste)")]
    [SerializeField] private Material startRoomMaterial;
    [SerializeField] private Material combatRoomMaterial;
    [SerializeField] private Material corridorMaterial;
    [SerializeField] private Material wallMaterial;

    [Header("Sprite'y Dekoracji 2.5D (Opcjonalne)")]
    [SerializeField] private Sprite treeSprite;
    [SerializeField] private Sprite pineSprite;
    [SerializeField] private Sprite rockSprite;
    [SerializeField] private Sprite fenceSprite;
    [SerializeField] private Sprite bushSprite;

    [Header("Prefaby")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject chestPrefab;

    private GameObject dungeonRoot;
    private NavMeshSurface navSurface;
    private List<GameObject> activeEnemies = new List<GameObject>();
    private List<RoomData> generatedRooms = new List<RoomData>();

    private float RoomSpacing => roomSize + corridorLength;

    public class RoomData
    {
        public int index;
        public Vector2Int gridPos;
        public Vector3 worldPos;
        public bool isStartRoom;
        public bool isFinalRoom;

        public bool doorNorth;
        public bool doorSouth;
        public bool doorEast;
        public bool doorWest;

        public GameObject rootObj;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        InitializeAssets();
    }

    private void Start()
    {
        // Sprawdź czy lochy już istnieją na scenie (np. wygenerowane w edytorze)
        Transform existing = transform.Find("Dungeon_Generated");
        if (existing != null)
        {
            dungeonRoot = existing.gameObject;
            activeEnemies.Clear();
            foreach (var ai in dungeonRoot.GetComponentsInChildren<EnemyAI>())
            {
                activeEnemies.Add(ai.gameObject);
            }

            if (GameUIManager.Instance != null)
            {
                GameUIManager.Instance.ConnectPlayer();
                GameUIManager.Instance.HideGameOver();
            }

            Debug.Log($"<b>[DungeonManager]</b> Połączono z istniejącymi lochami na scenie: {activeEnemies.Count} wrogów.");
            return;
        }

        // Jeśli lochy nie zostały jeszcze wygenerowane, stwórz je na starcie
        GenerateDungeon();
    }

    /// <summary>
    /// Pobiera materiały i sprite'y z projektu, jeśli nie zostały przypisane w Inspektorze.
    /// </summary>
    public void InitializeAssets()
    {
#if UNITY_EDITOR
        if (startRoomMaterial == null)
            startRoomMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Ground_Sandbox.mat");
        if (combatRoomMaterial == null)
            combatRoomMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Arena_Floor.mat");
        if (corridorMaterial == null)
            corridorMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Path.mat");
        if (wallMaterial == null)
            wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_Arena_Fence.mat");

        if (treeSprite == null) treeSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PNG/mapTile_115.png");
        if (pineSprite == null) pineSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PNG/mapTile_055.png");
        if (rockSprite == null) rockSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PNG/mapTile_100.png");
        if (fenceSprite == null) fenceSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PNG/mapTile_049.png");
        if (bushSprite == null) bushSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/PNG/mapTile_040.png");
#endif

        if (enemyPrefab == null)
            enemyPrefab = Resources.Load<GameObject>("Enemy_Goblin");
        if (chestPrefab == null)
            chestPrefab = Resources.Load<GameObject>("Chest_Reward");
    }

    /// <summary>
    /// Główna metoda generująca losowy rozkład połączonych pomieszczeń, wrogów i NavMesh.
    /// </summary>
    [ContextMenu("Generate Dungeon")]
    public void GenerateDungeon()
    {
        InitializeAssets();
        ClearExistingDungeon();

        dungeonRoot = new GameObject("Dungeon_Generated");
        dungeonRoot.transform.SetParent(transform, false);

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) groundLayer = 0;

        // 1. Wygenerowanie spójnego grafu pomieszczeń na siatce 2D
        GenerateRoomLayoutGraph();

        // 2. Budowa podłóg pomieszczeń i korytarzy
        BuildRoomFloors(groundLayer);
        BuildCorridors(groundLayer);

        // 3. Budowa granic i dekoracji 2.5D wokół pomieszczeń (z otwartymi przejściami!)
        BuildRoomPerimeters();

        // 4. Upieczenie NavMesh dla wygenerowanego układu
        BakeNavMesh(groundLayer);

        // 5. Ustawienie gracza w pokoju startowym
        SetupPlayerAtStart();

        // 6. Rozmieszczenie wrogów w pokojach bojowych
        SpawnEnemiesInRooms();

        // 7. Upewnienie się, że GameUIManager jest aktywny i podłączony
        if (GameUIManager.Instance != null)
        {
            GameUIManager.Instance.ConnectPlayer();
            GameUIManager.Instance.HideGameOver();
        }

        Debug.Log($"<b>[DungeonManager]</b> Pomyślnie wygenerowano lochy: {generatedRooms.Count} pomieszczeń, {activeEnemies.Count} wrogów. Przejścia są otwarte!");
    }

    /// <summary>
    /// Restartuje całą grę od nowa z nowym stanem przeciwników i układem pomieszczeń.
    /// </summary>
    public void RestartGame()
    {
        Debug.Log("<b>[DungeonManager]</b> Restart gry – generowanie nowego układu i reset gracza...");
        GenerateDungeon();

        // Znajdź i zresetuj gracza
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            PlayerHealth health = playerObj.GetComponent<PlayerHealth>();
            if (health != null)
            {
                health.ResetHealth();
            }
        }

        if (GameUIManager.Instance != null)
        {
            GameUIManager.Instance.HideGameOver();
        }
    }

    private void SafeDestroy(GameObject obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }

    private void ClearExistingDungeon()
    {
        // Usunięcie wrogów
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null) SafeDestroy(enemy);
        }
        activeEnemies.Clear();

        // Usunięcie starego korzenia lochów
        if (dungeonRoot != null)
        {
            SafeDestroy(dungeonRoot);
            dungeonRoot = null;
        }

        Transform existing = transform.Find("Dungeon_Generated");
        if (existing != null)
        {
            SafeDestroy(existing.gameObject);
        }

        generatedRooms.Clear();
    }

    #region 1. Algorytm Grafu Pomieszczeń

    private void GenerateRoomLayoutGraph()
    {
        generatedRooms.Clear();
        Dictionary<Vector2Int, RoomData> roomGrid = new Dictionary<Vector2Int, RoomData>();

        // Pokój startowy na pozycji (0, 0)
        RoomData startRoom = new RoomData
        {
            index = 0,
            gridPos = Vector2Int.zero,
            worldPos = Vector3.zero,
            isStartRoom = true,
            isFinalRoom = false
        };
        roomGrid.Add(Vector2Int.zero, startRoom);
        generatedRooms.Add(startRoom);

        Vector2Int[] cardinalDirections = new Vector2Int[]
        {
            Vector2Int.up,    // Północ (+Z)
            Vector2Int.right, // Wschód (+X)
            Vector2Int.down,  // Południe (-Z)
            Vector2Int.left   // Zachód (-X)
        };

        // Generujemy kolejne sąsiadujące pokoje losowo odchodzące od już istniejących
        int targetRooms = Mathf.Max(3, numberOfRooms);
        int attempts = 0;

        while (generatedRooms.Count < targetRooms && attempts < 200)
        {
            attempts++;

            // Wybierz losowy pokój z dotychczas utworzonych
            RoomData parentRoom = generatedRooms[Random.Range(0, generatedRooms.Count)];

            // Wybierz losowy kierunek
            Vector2Int chosenDir = cardinalDirections[Random.Range(0, cardinalDirections.Length)];
            Vector2Int targetPos = parentRoom.gridPos + chosenDir;

            if (!roomGrid.ContainsKey(targetPos))
            {
                // Tworzymy nowe pomieszczenie
                RoomData newRoom = new RoomData
                {
                    index = generatedRooms.Count,
                    gridPos = targetPos,
                    worldPos = new Vector3(targetPos.x * RoomSpacing, 0f, targetPos.y * RoomSpacing),
                    isStartRoom = false,
                    isFinalRoom = (generatedRooms.Count == targetRooms - 1)
                };

                // Łączymy drzwiami pokój nadrzędny z nowym
                ConnectRooms(parentRoom, newRoom, chosenDir);

                roomGrid.Add(targetPos, newRoom);
                generatedRooms.Add(newRoom);
            }
        }

        // Ostatni pokój oznaczamy jako finałowy (boss / skarb)
        if (generatedRooms.Count > 1)
        {
            generatedRooms[generatedRooms.Count - 1].isFinalRoom = true;
        }
    }

    private void ConnectRooms(RoomData roomA, RoomData roomB, Vector2Int directionFromAToB)
    {
        if (directionFromAToB == Vector2Int.up)
        {
            roomA.doorNorth = true;
            roomB.doorSouth = true;
        }
        else if (directionFromAToB == Vector2Int.down)
        {
            roomA.doorSouth = true;
            roomB.doorNorth = true;
        }
        else if (directionFromAToB == Vector2Int.right)
        {
            roomA.doorEast = true;
            roomB.doorWest = true;
        }
        else if (directionFromAToB == Vector2Int.left)
        {
            roomA.doorWest = true;
            roomB.doorEast = true;
        }
    }

    #endregion

    #region 2. Budowa Podłóg i Korytarzy

    private void BuildRoomFloors(int groundLayer)
    {
        foreach (RoomData room in generatedRooms)
        {
            GameObject roomObj = new GameObject($"Room_{room.index}_{(room.isStartRoom ? "Start" : (room.isFinalRoom ? "Final" : "Combat"))}");
            roomObj.transform.SetParent(dungeonRoot.transform, false);
            roomObj.transform.position = room.worldPos;
            room.rootObj = roomObj;

            // Płaszczyzna podłogi (Plane ma bazowy rozmiar 10x10)
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(roomObj.transform, false);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = new Vector3(roomSize / 10f, 1f, roomSize / 10f);
            floor.layer = groundLayer;

            Material mat = room.isStartRoom ? startRoomMaterial : combatRoomMaterial;
            if (mat != null)
            {
                floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }
    }

    private void BuildCorridors(int groundLayer)
    {
        GameObject corridorsRoot = new GameObject("Corridors");
        corridorsRoot.transform.SetParent(dungeonRoot.transform, false);

        foreach (RoomData room in generatedRooms)
        {
            // Budujemy korytarze w kierunku Północnym i Wschodnim, aby uniknąć duplikatów
            if (room.doorNorth)
            {
                Vector3 corridorCenter = room.worldPos + new Vector3(0f, 0.005f, (roomSize + corridorLength) * 0.5f);
                CreateCorridorSegment(corridorsRoot.transform, corridorCenter, corridorWidth, corridorLength, groundLayer);
            }

            if (room.doorEast)
            {
                Vector3 corridorCenter = room.worldPos + new Vector3((roomSize + corridorLength) * 0.5f, 0.005f, 0f);
                CreateCorridorSegment(corridorsRoot.transform, corridorCenter, corridorLength, corridorWidth, groundLayer);
            }
        }
    }

    private void CreateCorridorSegment(Transform parent, Vector3 center, float width, float length, int layer)
    {
        GameObject corridor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        corridor.name = "Corridor_Floor";
        corridor.transform.SetParent(parent, false);
        corridor.transform.position = center;
        corridor.transform.localScale = new Vector3(width / 10f, 1f, length / 10f);
        corridor.layer = layer;

        if (corridorMaterial != null)
        {
            corridor.GetComponent<MeshRenderer>().sharedMaterial = corridorMaterial;
        }

        // Ściany boczne korytarza chroniące przed wypadnięciem w próżnię
        AddCorridorSideWalls(parent, center, width, length);
    }

    private void AddCorridorSideWalls(Transform parent, Vector3 center, float width, float length)
    {
        if (width < length)
        {
            float halfW = width * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject wall = new GameObject("Corridor_BoundaryWall");
                wall.transform.SetParent(parent, false);
                wall.transform.position = center + new Vector3(s * halfW, 1.5f, 0f);
                BoxCollider box = wall.AddComponent<BoxCollider>();
                box.size = new Vector3(0.5f, 3f, length);
            }
        }
        else
        {
            float halfL = length * 0.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                GameObject wall = new GameObject("Corridor_BoundaryWall");
                wall.transform.SetParent(parent, false);
                wall.transform.position = center + new Vector3(0f, 1.5f, s * halfL);
                BoxCollider box = wall.AddComponent<BoxCollider>();
                box.size = new Vector3(width, 3f, 0.5f);
            }
        }
    }

    #endregion

    #region 3. Granice Pomieszczeń i Dekoracje 2.5D (Otwarte Drzwi)

    private void BuildRoomPerimeters()
    {
        Camera mainCam = Camera.main ?? Object.FindAnyObjectByType<Camera>();

        foreach (RoomData room in generatedRooms)
        {
            Transform roomParent = room.rootObj.transform;
            float halfSize = roomSize * 0.5f;

            // Północ (+Z)
            BuildWallOrDoorEdge(roomParent, room.worldPos, Vector3.forward * halfSize, Vector3.right, roomSize, room.doorNorth, mainCam);

            // Południe (-Z)
            BuildWallOrDoorEdge(roomParent, room.worldPos, Vector3.back * halfSize, Vector3.right, roomSize, room.doorSouth, mainCam);

            // Wschód (+X)
            BuildWallOrDoorEdge(roomParent, room.worldPos, Vector3.right * halfSize, Vector3.forward, roomSize, room.doorEast, mainCam);

            // Zachód (-X)
            BuildWallOrDoorEdge(roomParent, room.worldPos, Vector3.left * halfSize, Vector3.forward, roomSize, room.doorWest, mainCam);

            // Dodatkowe dekoracje w narożnikach pokoju
            SpawnCornerProps(roomParent, room.worldPos, halfSize, mainCam);
        }
    }

    private void BuildWallOrDoorEdge(Transform parent, Vector3 roomCenter, Vector3 edgeOffset, Vector3 edgeTangent, float length, bool hasDoor, Camera cam)
    {
        float halfLength = length * 0.5f;
        float doorHalfWidth = corridorWidth * 0.5f;

        // Liczba elementów na krawędzi
        int propCount = 6;
        float step = length / propCount;

        for (int i = 0; i <= propCount; i++)
        {
            float t = -halfLength + i * step;

            // Jeśli na tej krawędzi są drzwi/korytarz, ZOSTAWIAMY CAŁKOWICIE WOLNE PRZEJŚCIE!
            if (hasDoor && Mathf.Abs(t) < doorHalfWidth + 0.3f)
            {
                continue; // Pomiń przeszkody w obszarze drzwi
            }

            Vector3 propPos = roomCenter + edgeOffset + edgeTangent * t;

            // Wybierz losową dekorację (płot, skała lub drzewo)
            Sprite propSprite = (i % 2 == 0) ? fenceSprite : ((i % 3 == 0) ? rockSprite : bushSprite);
            if (propSprite != null)
            {
                CreateBillboardProp(parent, propSprite, propPos, cam, 1.0f);
            }
        }

        // Dodanie niewidzialnych ścian kolizyjnych, aby żaden wróg ani gracz nie spadł w próżnię
        AddEdgeBoundaryColliders(parent, roomCenter, edgeOffset, edgeTangent, length, hasDoor, doorHalfWidth);
    }

    private void AddEdgeBoundaryColliders(Transform parent, Vector3 roomCenter, Vector3 edgeOffset, Vector3 edgeTangent, float length, bool hasDoor, float doorHalfWidth)
    {
        Vector3 wallNormal = edgeOffset.normalized;

        if (!hasDoor)
        {
            GameObject wall = new GameObject("Room_BoundaryWall");
            wall.transform.SetParent(parent, false);
            wall.transform.position = roomCenter + edgeOffset + Vector3.up * 1.5f;

            BoxCollider box = wall.AddComponent<BoxCollider>();
            Vector3 size = Vector3.zero;
            size.y = 3f;
            if (Mathf.Abs(wallNormal.x) > 0.5f)
            {
                size.x = 0.5f;
                size.z = length;
            }
            else
            {
                size.x = length;
                size.z = 0.5f;
            }
            box.size = size;
        }
        else
        {
            float segmentLength = (length - corridorWidth) * 0.5f;
            float centerDist = (corridorWidth * 0.5f) + (segmentLength * 0.5f);

            for (int s = -1; s <= 1; s += 2)
            {
                GameObject wall = new GameObject("Room_BoundaryWall_Segment");
                wall.transform.SetParent(parent, false);
                Vector3 segPos = roomCenter + edgeOffset + (edgeTangent * (s * centerDist)) + Vector3.up * 1.5f;
                wall.transform.position = segPos;

                BoxCollider box = wall.AddComponent<BoxCollider>();
                Vector3 size = Vector3.zero;
                size.y = 3f;
                if (Mathf.Abs(wallNormal.x) > 0.5f)
                {
                    size.x = 0.5f;
                    size.z = segmentLength;
                }
                else
                {
                    size.x = segmentLength;
                    size.z = 0.5f;
                }
                box.size = size;
            }
        }
    }

    private void SpawnCornerProps(Transform parent, Vector3 roomCenter, float halfSize, Camera cam)
    {
        Vector3[] corners = new Vector3[]
        {
            new Vector3(-halfSize, 0f, -halfSize),
            new Vector3(halfSize, 0f, -halfSize),
            new Vector3(-halfSize, 0f, halfSize),
            new Vector3(halfSize, 0f, halfSize)
        };

        foreach (var c in corners)
        {
            Sprite cornerTree = (Random.value > 0.5f) ? treeSprite : pineSprite;
            if (cornerTree != null)
            {
                CreateBillboardProp(parent, cornerTree, roomCenter + c, cam, 1.25f);
            }
        }
    }

    private void CreateBillboardProp(Transform parent, Sprite sprite, Vector3 pos, Camera cam, float scale)
    {
        if (sprite == null) return;

        GameObject prop = new GameObject("Prop_" + sprite.name);
        prop.transform.SetParent(parent, false);
        prop.transform.position = pos;
        prop.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = prop.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 1;

        Billboard billboard = prop.AddComponent<Billboard>();
        if (cam != null)
        {
            billboard.SetTargetCamera(cam);
        }

        // Kolizja fizyczna, aby gracz nie przechodził przez dekoracje
        SphereCollider col = prop.AddComponent<SphereCollider>();
        col.center = new Vector3(0f, 0.4f, 0f);
        col.radius = 0.45f;
    }

    #endregion

    #region 4. Pieczenie NavMesh

    private void BakeNavMesh(int groundLayer)
    {
        if (navSurface == null)
        {
            navSurface = GetComponent<NavMeshSurface>();
            if (navSurface == null)
            {
                navSurface = gameObject.AddComponent<NavMeshSurface>();
            }
        }

        navSurface.collectObjects = CollectObjects.Children;
        navSurface.layerMask = 1 << groundLayer;
        navSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navSurface.BuildNavMesh();
    }

    #endregion

    #region 5. Ustawienie Gracza i Wrogów

    private void SetupPlayerAtStart()
    {
        Camera mainCam = Camera.main ?? Object.FindAnyObjectByType<Camera>();
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

        Vector3 startPos = generatedRooms[0].worldPos + new Vector3(0f, 0.1f, 0f);

        if (playerObj == null)
        {
            // Załaduj prefab gracza
            GameObject playerPrefab = Resources.Load<GameObject>("Player_Placeholder");
            if (playerPrefab != null)
            {
                playerObj = Instantiate(playerPrefab, startPos, Quaternion.identity);
                playerObj.name = "Player";
            }
        }
        else
        {
            // Przemieszczamy gracza do pokoju startowego
            CharacterController cc = playerObj.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            playerObj.transform.position = startPos;
            if (cc != null) cc.enabled = true;
        }

        // Podpięcie komponentu PlayerHealth jeśli go brakowało
        if (playerObj != null)
        {
            if (playerObj.GetComponent<PlayerHealth>() == null)
            {
                playerObj.AddComponent<PlayerHealth>();
            }

            // Połączenie z kamerą
            if (mainCam != null)
            {
                IsometricCameraFollow follow = mainCam.GetComponent<IsometricCameraFollow>();
                if (follow != null)
                {
                    follow.SetTarget(playerObj.transform, true);
                }
            }
        }
    }

    private void SpawnEnemiesInRooms()
    {
        if (enemyPrefab == null)
        {
            enemyPrefab = Resources.Load<GameObject>("Enemy_Goblin");
            if (enemyPrefab == null) return;
        }

        // Spawnowanie w każdym pokoju oprócz pokoju startowego (pokój 0 jest bezpieczny)
        for (int i = 1; i < generatedRooms.Count; i++)
        {
            RoomData room = generatedRooms[i];
            int count = Random.Range(minEnemiesPerRoom, maxEnemiesPerRoom + 1);

            for (int k = 0; k < count; k++)
            {
                // Losowa pozycja wewnątrz pokoju (z marginesem od ścian)
                float offsetRange = (roomSize * 0.5f) - 3.5f;
                Vector3 spawnOffset = new Vector3(
                    Random.Range(-offsetRange, offsetRange),
                    0f,
                    Random.Range(-offsetRange, offsetRange)
                );

                Vector3 spawnPos = room.worldPos + spawnOffset;

                // Sprawdzenie czy punkt jest na NavMeshu
                if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                {
                    spawnPos = hit.position;
                }

                GameObject enemy = Instantiate(enemyPrefab, spawnPos, Quaternion.identity);
                enemy.name = $"Enemy_Room{room.index}_{k + 1}";
                enemy.transform.SetParent(room.rootObj.transform, true);

                NavMeshAgent enemyAgent = enemy.GetComponent<NavMeshAgent>();
                if (enemyAgent != null)
                {
                    enemyAgent.Warp(spawnPos);
                }

                activeEnemies.Add(enemy);
            }

            // W pokoju finałowym dodajemy skrzynię ze skarbem
            if (room.isFinalRoom && chestPrefab != null)
            {
                Vector3 chestPos = room.worldPos + new Vector3(0f, 0f, 2f);
                GameObject chest = Instantiate(chestPrefab, chestPos, Quaternion.identity);
                chest.name = "Final_Treasure_Chest";
                chest.transform.SetParent(room.rootObj.transform, true);
            }
        }
    }

    #endregion
}
