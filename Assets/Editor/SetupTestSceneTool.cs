using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Narzędzie edytorskie do automatycznego budowania kompletnej sceny testowej (Combat Arena).
/// Integruje:
/// - Tekstury i kafelki podłoża z paczki Kenney (trawiaste podłoże, ścieżki, arena),
/// - Elementy otoczenia 2.5D z Billboard.cs (drzewa, skały, krzewy, płoty),
/// - Postaci i animacje z paczki Tiny RPG (Soldier jako Gracz, Orc jako Wrogowie i Boss),
/// - Wszystkie mechaniki walki, kamerę izometryczną i strefę walki (CombatZone).
/// Dostępne w menu: Tools -> Game Setup -> Build Full Combat Arena Scene
/// </summary>
public static class SetupTestSceneTool
{
    private const string MenuPath = "Tools/Game Setup/Build Full Combat Arena Scene";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    private const string PrefabFolderPath = "Assets/Prefabs";
    private const string MaterialsFolderPath = "Assets/Materials";
    private const string SpritesFolderPath = "Assets/Sprites";
    private const string ResourcesFolderPath = "Assets/Resources";

    private const string PlayerPrefabPath = "Assets/Prefabs/Player_Placeholder.prefab";
    private const string ResourcesPlayerPrefabPath = "Assets/Resources/Player_Placeholder.prefab";
    private const string EnemyPrefabPath = "Assets/Prefabs/Enemy_Goblin.prefab";
    private const string ResourcesEnemyPrefabPath = "Assets/Resources/Enemy_Goblin.prefab";
    private const string ChestPrefabPath = "Assets/Prefabs/Chest_Reward.prefab";

    // Materiały
    private const string GroundMaterialPath = "Assets/Materials/M_Ground_Sandbox.mat";
    private const string ArenaFloorMaterialPath = "Assets/Materials/M_Arena_Floor.mat";
    private const string PathMaterialPath = "Assets/Materials/M_Path.mat";
    private const string GateMaterialPath = "Assets/Materials/M_Gate_Stone.mat";
    private const string WallMaterialPath = "Assets/Materials/M_Arena_Fence.mat";

    // Tekstury kafelków z paczki Kenney (Assets/PNG)
    private const string GrassTilePath = "Assets/PNG/mapTile_022.png"; // Żywa zielona trawa
    private const string DirtTilePath = "Assets/PNG/mapTile_082.png";  // Podłoże areny / ubita ziemia
    private const string PathTilePath = "Assets/PNG/mapTile_017.png";  // Kamienna/piaskowa ścieżka
    private const string TreeTilePath = "Assets/PNG/mapTile_115.png";  // Drzewo 2.5D
    private const string PineTilePath = "Assets/PNG/mapTile_055.png";  // Sosna / krzew 2.5D
    private const string RockTilePath = "Assets/PNG/mapTile_100.png";  // Skała 2.5D
    private const string FenceTilePath = "Assets/PNG/mapTile_049.png"; // Płot drewniany 2.5D
    private const string BushTilePath = "Assets/PNG/mapTile_040.png";  // Niski krzaczek 2.5D

    private const string PlayerSpritePath = "Assets/Sprites/Player_Placeholder_Sprite.png";
    private const string GoblinSpritePath = "Assets/Sprites/Enemy_Goblin_Sprite.png";
    private const string DummySpritePath = "Assets/Sprites/Dummy_Placeholder_Sprite.png";
    private const string ChestSpritePath = "Assets/Sprites/Chest_Reward_Sprite.png";

    [InitializeOnLoadMethod]
    private static void RegisterSceneAutoVerification()
    {
        EditorApplication.delayCall += EnsureSceneReady;
        EditorApplication.playModeStateChanged += (state) =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                EnsureSceneReady();
            }
        };
    }

    public static void EnsureSceneReady()
    {
        if (EditorApplication.isPlaying) return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrEmpty(activeScene.path) && activeScene.path != ScenePath)
        {
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        GameObject dungeon = GameObject.Find("DungeonManager");

        if (player == null && dungeon != null)
        {
            Debug.Log("<b>[Combat Arena]</b> Wykryto brak gracza na scenie – dodawanie postaci gracza...");
            SpawnOrReconnectPlayer();
        }
        else if (player == null || dungeon == null)
        {
            Debug.Log("<b>[Combat Arena]</b> Scena nie jest w pełni skonfigurowana. Budowanie kompletnej sceny z lochami i UI...");
            BuildFullCombatArenaScene();
        }
    }

    [MenuItem("Tools/Game Setup/Spawn or Reconnect Player", false, 3)]
    public static GameObject SpawnOrReconnectPlayer()
    {
        EnsureFolder(PrefabFolderPath);
        EnsureFolder(ResourcesFolderPath);
        int enemiesLayerIndex = EnsureLayer("Enemies");

        Camera mainCam = Camera.main ?? Object.FindAnyObjectByType<Camera>();

        // 1. Sprawdź czy gracz już istnieje na scenie
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null)
        {
            PlayerMovement pm = Object.FindAnyObjectByType<PlayerMovement>();
            if (pm != null) playerObj = pm.gameObject;
        }

        if (playerObj == null)
        {
            // Przygotuj animacje i sprite
            CharacterAnimationSetupTool.SetupAllCharacters();
            Sprite playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Soldier/Soldier_Idle_0.png") 
                                 ?? GetOrCreateCapsuleSprite(PlayerSpritePath, new Color(0.24f, 0.55f, 0.88f), "PLAYER");

            playerObj = CreatePlayerObject(new Vector3(0f, 0f, -12f), playerSprite, mainCam, enemiesLayerIndex);
            PrefabUtility.SaveAsPrefabAssetAndConnect(playerObj, PlayerPrefabPath, InteractionMode.AutomatedAction);
            PrefabUtility.SaveAsPrefabAsset(playerObj, ResourcesPlayerPrefabPath);
            Debug.Log("<b>[Combat Arena]</b> Utworzono postać gracza (Soldier) na scenie.");
        }
        else
        {
            Debug.Log("<b>[Combat Arena]</b> Gracz już istnieje na scenie (" + playerObj.name + "). Łączenie z kamerą...");
        }

        // 2. Połącz kamerę z graczem
        if (mainCam != null)
        {
            IsometricCameraFollow camFollow = mainCam.GetComponent<IsometricCameraFollow>();
            if (camFollow != null)
            {
                camFollow.SetTarget(playerObj.transform, true);
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = playerObj;
        return playerObj;
    }

    [MenuItem(MenuPath, false, 1)]
    [MenuItem("Tools/Game Setup/Build Combat Sandbox", false, 2)]
    public static void BuildFullCombatArenaScene()
    {
        // 1. Otwórz lub przygotuj scenę SampleScene
        Scene activeScene;
        if (File.Exists(ScenePath))
        {
            activeScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }
        else
        {
            activeScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // Czyszczenie dotychczasowej zawartości sceny
        GameObject[] existingRoots = activeScene.GetRootGameObjects();
        foreach (GameObject root in existingRoots)
        {
            Object.DestroyImmediate(root);
        }

        // 2. Przygotowanie folderów i warstw
        EnsureFolder(PrefabFolderPath);
        EnsureFolder(ResourcesFolderPath);
        EnsureFolder(MaterialsFolderPath);
        EnsureFolder(SpritesFolderPath);

        int groundLayerIndex = EnsureLayer("Ground");
        int enemiesLayerIndex = EnsureLayer("Enemies");

        // 3. Konfiguracja i pocięcie assetów z paczek
        CharacterAnimationSetupTool.SetupAllCharacters();
        ConfigureKenneyTiles();

        Sprite playerSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Soldier/Soldier_Idle_0.png") 
                             ?? GetOrCreateCapsuleSprite(PlayerSpritePath, new Color(0.24f, 0.55f, 0.88f), "PLAYER");
        Sprite goblinSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/Orc/Orc_Idle_0.png") 
                             ?? GetOrCreateGoblinSprite(GoblinSpritePath, new Color(0.35f, 0.68f, 0.28f));
        Sprite dummySprite = GetOrCreateDummySprite(DummySpritePath, new Color(0.85f, 0.65f, 0.35f));
        Sprite chestSprite = GetOrCreateChestSprite(ChestSpritePath, new Color(0.92f, 0.72f, 0.18f));

        // 4. Utworzenie materiałów podłoża z kafelków z folderu Assets
        Material groundMat = GetOrCreateTexturedMaterial(GroundMaterialPath, GrassTilePath, new Vector2(30f, 30f));
        Material arenaFloorMat = GetOrCreateTexturedMaterial(ArenaFloorMaterialPath, DirtTilePath, new Vector2(8f, 10f));
        Material pathMat = GetOrCreateTexturedMaterial(PathMaterialPath, PathTilePath, new Vector2(2f, 8f));
        Material gateMat = GetOrCreateColorMaterial(GateMaterialPath, new Color(0.25f, 0.25f, 0.28f));
        Material wallMat = GetOrCreateColorMaterial(WallMaterialPath, new Color(0.38f, 0.26f, 0.16f));

        // 5. Generowanie oświetlenia i kamery izometrycznej
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.65f, 0.70f);

        GameObject lightObj = new GameObject("Directional Light");
        Light dirLight = lightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = new Color(1f, 0.96f, 0.88f);
        dirLight.intensity = 1.25f;
        dirLight.shadows = LightShadows.Soft;
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        lightObj.AddComponent<UniversalAdditionalLightData>();

        GameObject cameraObj = new GameObject("Main Camera");
        cameraObj.tag = "MainCamera";
        Camera mainCam = cameraObj.AddComponent<Camera>();
        cameraObj.AddComponent<AudioListener>();
        mainCam.clearFlags = CameraClearFlags.Skybox;
        mainCam.nearClipPlane = 0.3f;
        mainCam.farClipPlane = 1000f;
        mainCam.fieldOfView = 45f;

        // URP Camera Data
        UniversalAdditionalCameraData camData = cameraObj.AddComponent<UniversalAdditionalCameraData>();
        camData.renderShadows = true;
        camData.renderPostProcessing = false;

        // Domyślna bezpieczna pozycja izometryczna
        cameraObj.transform.position = new Vector3(0f, 11f, -22f);
        cameraObj.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

        IsometricCameraFollow cameraFollow = cameraObj.AddComponent<IsometricCameraFollow>();

        // 6. Tworzenie Prefabów (Wrogowie i Skrzynia)
        GameObject enemyPrefab = CreateOrUpdateEnemyPrefab(goblinSprite, mainCam, enemiesLayerIndex);
        GameObject chestPrefab = CreateOrUpdateChestPrefab(chestSprite, mainCam);

        // 7. Tworzenie Gracza na scenie z pełnym pakietem skryptów (ruch, walka, statystyki, życie IDamageable)
        GameObject playerObj = CreatePlayerObject(new Vector3(0f, 0f, 0f), playerSprite, mainCam, enemiesLayerIndex);
        PrefabUtility.SaveAsPrefabAssetAndConnect(playerObj, PlayerPrefabPath, InteractionMode.AutomatedAction);
        PrefabUtility.SaveAsPrefabAsset(playerObj, ResourcesPlayerPrefabPath);

        // Połączenie kamery z graczem
        SerializedObject soCam = new SerializedObject(cameraFollow);
        soCam.FindProperty("target").objectReferenceValue = playerObj.transform;
        soCam.FindProperty("pitchAngle").floatValue = 45f;
        soCam.FindProperty("distance").floatValue = 14f;
        soCam.FindProperty("followSmoothTime").floatValue = 0.2f;
        soCam.FindProperty("rotationSmoothTime").floatValue = 0.25f;
        soCam.ApplyModifiedProperties();
        cameraFollow.SnapToTarget();

        // 8. Utworzenie GameUIManager (Pasek zdrowia gracza i ekran Game Over z przyciskiem restartu)
        GameObject uiManagerObj = new GameObject("GameUIManager");
        GameUIManager uiManager = uiManagerObj.AddComponent<GameUIManager>();
        uiManager.ConnectPlayer();

        // 9. Utworzenie DungeonManager (Generator losowego rozstawu pomieszczeń bez blokujących bram/ścian)
        GameObject dungeonObj = new GameObject("DungeonManager");
        DungeonManager dungeonManager = dungeonObj.AddComponent<DungeonManager>();

        SerializedObject soDungeon = new SerializedObject(dungeonManager);
        soDungeon.FindProperty("numberOfRooms").intValue = 5;
        soDungeon.FindProperty("roomSize").floatValue = 18f;
        soDungeon.FindProperty("corridorLength").floatValue = 10f;
        soDungeon.FindProperty("corridorWidth").floatValue = 6f;
        soDungeon.FindProperty("minEnemiesPerRoom").intValue = 2;
        soDungeon.FindProperty("maxEnemiesPerRoom").intValue = 4;
        soDungeon.FindProperty("startRoomMaterial").objectReferenceValue = groundMat;
        soDungeon.FindProperty("combatRoomMaterial").objectReferenceValue = arenaFloorMat;
        soDungeon.FindProperty("corridorMaterial").objectReferenceValue = pathMat;
        soDungeon.FindProperty("wallMaterial").objectReferenceValue = wallMat;
        soDungeon.FindProperty("enemyPrefab").objectReferenceValue = enemyPrefab;
        soDungeon.FindProperty("chestPrefab").objectReferenceValue = chestPrefab;
        soDungeon.ApplyModifiedProperties();

        // Wygenerowanie układu pomieszczeń, wrogów i upieczenie NavMesha
        dungeonManager.GenerateDungeon();

        // 10. Zapis sceny
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);

        Selection.activeGameObject = playerObj;

        Debug.Log("<b>[Combat Dungeon]</b> Zbudowano scenę z losowymi pomieszczeniami!\n" +
                  "• <b>Pokoje:</b> Losowy rozstaw 5 pomieszczeń połączonych otwartymi korytarzami (brak blokujących bram).\n" +
                  "• <b>Wrogowie:</b> Po 2-4 przeciwników (Orc/Goblin) w każdym pokoju bojowym z upieczonym NavMeshem.\n" +
                  "• <b>Życie Gracza:</b> Pasek zdrowia UI w lewym górnym rogu, otrzymywanie obrażeń od wrogów (IDamageable) i odrzut.\n" +
                  "• <b>Koniec Gry:</b> Po spadku HP do 0 pojawia się plansza 'Koniec Gry' z przyciskiem 'Graj od nowa', który generuje świeży poziom.\n" +
                  "• <b>Postaci:</b> W pełni animowany Gracz (Soldier) i Wrogowie (Orc).");
    }

    #region Konfiguracja Kafelków Kenney (Podłoże i Otoczenie 2.5D)

    private static void ConfigureKenneyTiles()
    {
        // 1. Kafelki podłoża powtarzalnego (Repeat, Point)
        ConfigureTilingTexture(GrassTilePath);
        ConfigureTilingTexture(DirtTilePath);
        ConfigureTilingTexture(PathTilePath);

        // 2. Kafelki dekoracji 2.5D (Sprites z pivotem na dole)
        ConfigurePropSprite(TreeTilePath, 22f, new Vector2(0.5f, 0.12f));
        ConfigurePropSprite(PineTilePath, 22f, new Vector2(0.5f, 0.12f));
        ConfigurePropSprite(RockTilePath, 24f, new Vector2(0.5f, 0.20f));
        ConfigurePropSprite(BushTilePath, 24f, new Vector2(0.5f, 0.15f));
        ConfigurePropSprite(FenceTilePath, 24f, new Vector2(0.5f, 0.10f));
    }

    private static void ConfigureTilingTexture(string path)
    {
        if (!File.Exists(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }

        if (importer != null)
        {
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Default) { importer.textureType = TextureImporterType.Default; dirty = true; }
            if (importer.wrapMode != TextureWrapMode.Repeat) { importer.wrapMode = TextureWrapMode.Repeat; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }

            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }
    }

    private static void ConfigurePropSprite(string path, float ppu, Vector2 pivot)
    {
        if (!File.Exists(path)) return;

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
        }

        if (importer != null)
        {
            bool dirty = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
            if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; dirty = true; }
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
            if (Mathf.Abs(importer.spritePixelsPerUnit - ppu) > 0.01f) { importer.spritePixelsPerUnit = ppu; dirty = true; }

            TextureImporterSettings tis = new TextureImporterSettings();
            importer.ReadTextureSettings(tis);
            if (tis.spriteAlignment != (int)SpriteAlignment.Custom || tis.spritePivot != pivot)
            {
                tis.spriteAlignment = (int)SpriteAlignment.Custom;
                tis.spritePivot = pivot;
                importer.SetTextureSettings(tis);
                dirty = true;
            }

            if (dirty)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
            }
        }
    }

    #endregion

    #region Budowa Środowiska i Otoczenia 2.5D

    private static void BuildArenaEnvironment(out GameObject entranceGate, out GameObject exitGate, out Transform[] spawnPoints, out Transform chestSpawn, Material arenaFloorMat, Material pathMat, Material gateMat, Material wallMat, Camera cam)
    {
        GameObject arenaRoot = new GameObject("Arena_Environment");

        // 1. Podłoże areny (ubita ziemia mapTile_082 na płaszczyźnie 16x22m)
        GameObject arenaFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        arenaFloor.name = "Arena_Floor";
        arenaFloor.transform.SetParent(arenaRoot.transform);
        arenaFloor.transform.position = new Vector3(0f, 0.015f, 9.5f);
        arenaFloor.transform.localScale = new Vector3(1.6f, 1f, 2.2f); // 16x22m
        arenaFloor.GetComponent<MeshRenderer>().sharedMaterial = arenaFloorMat;
        Object.DestroyImmediate(arenaFloor.GetComponent<Collider>()); // kolizja obsługiwana przez Ground

        // 2. Ścieżka podejściowa (kamienna/piaskowa ścieżka mapTile_017 od startu do bramy)
        GameObject walkPath = GameObject.CreatePrimitive(PrimitiveType.Plane);
        walkPath.name = "Walkway_Path";
        walkPath.transform.SetParent(arenaRoot.transform);
        walkPath.transform.position = new Vector3(0f, 0.012f, -6.5f);
        walkPath.transform.localScale = new Vector3(0.4f, 1f, 1.1f); // 4x11m
        walkPath.GetComponent<MeshRenderer>().sharedMaterial = pathMat;
        Object.DestroyImmediate(walkPath.GetComponent<Collider>());

        // 3. Brama wejściowa (Z = -1)
        entranceGate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        entranceGate.name = "EntranceGate";
        entranceGate.transform.SetParent(arenaRoot.transform);
        entranceGate.transform.position = new Vector3(0f, 1.5f, -1f);
        entranceGate.transform.localScale = new Vector3(10f, 3f, 0.8f);
        entranceGate.GetComponent<MeshRenderer>().sharedMaterial = gateMat;

        // 4. Brama wyjściowa (Z = 20)
        exitGate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        exitGate.name = "ExitGate";
        exitGate.transform.SetParent(arenaRoot.transform);
        exitGate.transform.position = new Vector3(0f, 1.5f, 20f);
        exitGate.transform.localScale = new Vector3(10f, 3f, 0.8f);
        exitGate.GetComponent<MeshRenderer>().sharedMaterial = gateMat;

        // 5. Ściany boczne areny
        CreateFenceWall(new Vector3(-8f, 1f, 9.5f), new Vector3(0.8f, 2f, 21f), arenaRoot.transform, wallMat);
        CreateFenceWall(new Vector3(8f, 1f, 9.5f), new Vector3(0.8f, 2f, 21f), arenaRoot.transform, wallMat);

        // 6. Dekoracje otoczenia 2.5D (Billboard: drzewa, skały, krzaki, płotki)
        GameObject propsRoot = new GameObject("Environment_Props_2.5D");
        propsRoot.transform.SetParent(arenaRoot.transform);

        Sprite treeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(TreeTilePath);
        Sprite pineSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PineTilePath);
        Sprite rockSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RockTilePath);
        Sprite bushSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BushTilePath);

        // Drzewa wzdłuż ścieżki
        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(-3.5f, 0f, -12f), cam, 1.1f);
        CreateBillboardProp(propsRoot.transform, pineSprite, new Vector3(3.8f, 0f, -11f), cam, 1.0f);
        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(-4.2f, 0f, -8f), cam, 1.15f);
        CreateBillboardProp(propsRoot.transform, bushSprite, new Vector3(3.2f, 0f, -7.5f), cam, 0.9f);
        CreateBillboardProp(propsRoot.transform, rockSprite, new Vector3(-2.8f, 0f, -5f), cam, 0.85f);
        CreateBillboardProp(propsRoot.transform, pineSprite, new Vector3(4.0f, 0f, -4f), cam, 1.1f);

        // Drzewa i skały wokół areny
        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(-10.5f, 0f, 2f), cam, 1.2f);
        CreateBillboardProp(propsRoot.transform, pineSprite, new Vector3(-11f, 0f, 8f), cam, 1.25f);
        CreateBillboardProp(propsRoot.transform, rockSprite, new Vector3(-10f, 0f, 14f), cam, 1.0f);
        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(-11f, 0f, 19f), cam, 1.2f);

        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(10.5f, 0f, 2f), cam, 1.2f);
        CreateBillboardProp(propsRoot.transform, rockSprite, new Vector3(10.2f, 0f, 8f), cam, 1.0f);
        CreateBillboardProp(propsRoot.transform, pineSprite, new Vector3(11.2f, 0f, 14f), cam, 1.25f);
        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(10.8f, 0f, 19f), cam, 1.2f);

        // Za bramą wyjściową
        CreateBillboardProp(propsRoot.transform, pineSprite, new Vector3(-4f, 0f, 23f), cam, 1.2f);
        CreateBillboardProp(propsRoot.transform, treeSprite, new Vector3(4f, 0f, 23f), cam, 1.2f);

        // 7. Punkty Spawnu
        GameObject spawnsRoot = new GameObject("SpawnPoints");
        spawnsRoot.transform.SetParent(arenaRoot.transform);

        GameObject sp1 = new GameObject("Spawn_Left");
        sp1.transform.SetParent(spawnsRoot.transform);
        sp1.transform.position = new Vector3(-5f, 0f, 6f);

        GameObject sp2 = new GameObject("Spawn_Right");
        sp2.transform.SetParent(spawnsRoot.transform);
        sp2.transform.position = new Vector3(5f, 0f, 6f);

        GameObject sp3 = new GameObject("Spawn_Back");
        sp3.transform.SetParent(spawnsRoot.transform);
        sp3.transform.position = new Vector3(0f, 0f, 15f);

        spawnPoints = new Transform[] { sp1.transform, sp2.transform, sp3.transform };

        // 8. Punkt spawnu skrzyni
        GameObject chestSpawnObj = new GameObject("ChestSpawnPoint");
        chestSpawnObj.transform.SetParent(arenaRoot.transform);
        chestSpawnObj.transform.position = new Vector3(0f, 0f, 9.5f);
        chestSpawn = chestSpawnObj.transform;
    }

    private static void CreateBillboardProp(Transform parent, Sprite sprite, Vector3 pos, Camera cam, float scale)
    {
        if (sprite == null) return;

        GameObject prop = new GameObject("Prop_" + sprite.name);
        prop.transform.SetParent(parent);
        prop.transform.position = pos;
        prop.transform.localScale = Vector3.one * scale;

        SpriteRenderer sr = prop.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 1;

        Billboard billboard = prop.AddComponent<Billboard>();
        SerializedObject soBillboard = new SerializedObject(billboard);
        soBillboard.FindProperty("billboardMode").enumValueIndex = (int)Billboard.BillboardMode.MatchCameraRotation;
        soBillboard.FindProperty("targetCamera").objectReferenceValue = cam;
        soBillboard.ApplyModifiedProperties();

        // Kolizja, aby gracz nie przenikał przez drzewa/skały
        SphereCollider col = prop.AddComponent<SphereCollider>();
        col.center = new Vector3(0f, 0.4f, 0f);
        col.radius = 0.4f;
    }

    private static void CreateFenceWall(Vector3 pos, Vector3 scale, Transform parent, Material mat)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "ArenaWall";
        wall.transform.SetParent(parent);
        wall.transform.position = pos;
        wall.transform.localScale = scale;
        wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    #endregion

    #region Materiały i Obiekty Gry

    private static Material GetOrCreateTexturedMaterial(string matPath, string texPath, Vector2 tiling)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");

            mat = new Material(shader)
            {
                color = Color.white
            };
            AssetDatabase.CreateAsset(mat, matPath);
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex != null)
        {
            mat.mainTexture = tex;
            mat.mainTextureScale = tiling;
            mat.color = Color.white;
            EditorUtility.SetDirty(mat);
        }

        return mat;
    }

    private static Material GetOrCreateColorMaterial(string path, Color color)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null) shader = Shader.Find("Standard");

        mat = new Material(shader)
        {
            color = color
        };

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static GameObject CreatePlayerObject(Vector3 position, Sprite sprite, Camera cam, int enemyLayer)
    {
        GameObject playerObj = new GameObject("Player");
        playerObj.tag = "Player";
        playerObj.transform.position = position;

        CharacterController cc = playerObj.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.height = 1.8f;
        cc.radius = 0.45f;
        cc.stepOffset = 0.3f;

        GameObject visualObj = new GameObject("Visual");
        visualObj.transform.SetParent(playerObj.transform, false);
        visualObj.transform.localPosition = Vector3.zero;

        SpriteRenderer sr = visualObj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 2;

        Billboard billboard = visualObj.AddComponent<Billboard>();
        SerializedObject soBillboard = new SerializedObject(billboard);
        soBillboard.FindProperty("billboardMode").enumValueIndex = (int)Billboard.BillboardMode.MatchCameraRotation;
        soBillboard.FindProperty("targetCamera").objectReferenceValue = cam;
        soBillboard.ApplyModifiedProperties();

        Animator anim = visualObj.AddComponent<Animator>();
        RuntimeAnimatorController soldierCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterAnimationSetupTool.SoldierControllerPath);
        if (soldierCtrl != null)
        {
            anim.runtimeAnimatorController = soldierCtrl;
        }

        PlayerStats playerStats = playerObj.AddComponent<PlayerStats>();
        playerStats.BaseDamage = 25f;

        PlayerMovement movement = playerObj.AddComponent<PlayerMovement>();
        SerializedObject soMovement = new SerializedObject(movement);
        soMovement.FindProperty("spriteRenderer").objectReferenceValue = sr;
        soMovement.FindProperty("playerCamera").objectReferenceValue = cam;
        soMovement.FindProperty("animator").objectReferenceValue = anim;
        soMovement.FindProperty("moveSpeed").floatValue = 6f;
        soMovement.FindProperty("dashSpeed").floatValue = 16f;
        soMovement.FindProperty("dashCooldown").floatValue = 1.0f;
        soMovement.ApplyModifiedProperties();

        PlayerCombat combat = playerObj.AddComponent<PlayerCombat>();
        SerializedObject soCombat = new SerializedObject(combat);
        soCombat.FindProperty("playerStats").objectReferenceValue = playerStats;
        soCombat.FindProperty("playerMovement").objectReferenceValue = movement;
        soCombat.FindProperty("spriteRenderer").objectReferenceValue = sr;
        soCombat.FindProperty("animator").objectReferenceValue = anim;
        soCombat.FindProperty("mainCamera").objectReferenceValue = cam;
        soCombat.FindProperty("damageableLayers").intValue = (1 << 0) | (1 << enemyLayer);
        soCombat.ApplyModifiedProperties();

        // Dodanie komponentu życia gracza i podatności na obrażenia
        PlayerHealth health = playerObj.AddComponent<PlayerHealth>();

        return playerObj;
    }

    private static GameObject CreateOrUpdateEnemyPrefab(Sprite goblinSprite, Camera cam, int enemyLayer)
    {
        GameObject tempObj = new GameObject("Enemy_Goblin");
        tempObj.layer = enemyLayer;

        CapsuleCollider col = tempObj.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.9f, 0f);
        col.height = 1.8f;
        col.radius = 0.45f;

        NavMeshAgent agent = tempObj.AddComponent<NavMeshAgent>();
        agent.speed = 4.2f;
        agent.stoppingDistance = 1.2f;
        agent.radius = 0.45f;
        agent.height = 1.8f;
        agent.updateRotation = false;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(tempObj.transform, false);
        visual.transform.localPosition = Vector3.zero;

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = goblinSprite;
        sr.sortingOrder = 2;

        Billboard billboard = visual.AddComponent<Billboard>();
        SerializedObject soBillboard = new SerializedObject(billboard);
        soBillboard.FindProperty("billboardMode").enumValueIndex = (int)Billboard.BillboardMode.MatchCameraRotation;
        soBillboard.FindProperty("targetCamera").objectReferenceValue = cam;
        soBillboard.ApplyModifiedProperties();

        Animator anim = visual.AddComponent<Animator>();
        RuntimeAnimatorController orcCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterAnimationSetupTool.OrcControllerPath);
        if (orcCtrl != null)
        {
            anim.runtimeAnimatorController = orcCtrl;
        }

        EnemyAI enemyAI = tempObj.AddComponent<EnemyAI>();
        SerializedObject soAI = new SerializedObject(enemyAI);
        var srProp = soAI.FindProperty("spriteRenderer");
        if (srProp != null) srProp.objectReferenceValue = sr;
        var animProp = soAI.FindProperty("animator");
        if (animProp != null) animProp.objectReferenceValue = anim;
        var hpProp = soAI.FindProperty("maxHealth");
        if (hpProp != null) hpProp.floatValue = 75f;
        var detProp = soAI.FindProperty("detectionRadius");
        if (detProp != null) detProp.floatValue = 8f;
        var loseProp = soAI.FindProperty("loseTargetRadius");
        if (loseProp != null) loseProp.floatValue = 12f;
        var rangeProp = soAI.FindProperty("attackRange");
        if (rangeProp != null) rangeProp.floatValue = 1.6f;
        var dmgProp = soAI.FindProperty("attackDamage");
        if (dmgProp != null) dmgProp.floatValue = 12f;
        var windProp = soAI.FindProperty("windUpDuration");
        if (windProp != null) windProp.floatValue = 0.45f;
        soAI.ApplyModifiedProperties();

        // Pływający pasek zdrowia nad wrogiem
        tempObj.AddComponent<EnemyHealthBar>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempObj, EnemyPrefabPath);
        PrefabUtility.SaveAsPrefabAsset(tempObj, ResourcesEnemyPrefabPath);
        Object.DestroyImmediate(tempObj);
        return prefab;
    }

    private static GameObject CreateOrUpdateChestPrefab(Sprite chestSprite, Camera cam)
    {
        GameObject tempObj = new GameObject("Chest_Reward");

        BoxCollider col = tempObj.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 0.6f, 0f);
        col.size = new Vector3(1.2f, 1.2f, 1.2f);
        col.isTrigger = true;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(tempObj.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = chestSprite;
        sr.sortingOrder = 2;

        Billboard billboard = visual.AddComponent<Billboard>();
        SerializedObject soBillboard = new SerializedObject(billboard);
        soBillboard.FindProperty("billboardMode").enumValueIndex = (int)Billboard.BillboardMode.MatchCameraRotation;
        soBillboard.FindProperty("targetCamera").objectReferenceValue = cam;
        soBillboard.ApplyModifiedProperties();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempObj, ChestPrefabPath);
        Object.DestroyImmediate(tempObj);
        return prefab;
    }

    private static void CreateTrainingDummy(Vector3 position, Sprite dummySprite, Camera cam, int enemyLayer)
    {
        GameObject dummyObj = new GameObject("TrainingDummy");
        dummyObj.transform.position = position;
        dummyObj.layer = enemyLayer;

        CapsuleCollider col = dummyObj.AddComponent<CapsuleCollider>();
        col.center = new Vector3(0f, 0.9f, 0f);
        col.height = 1.8f;
        col.radius = 0.45f;

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(dummyObj.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);

        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = dummySprite;
        sr.sortingOrder = 2;

        Billboard billboard = visual.AddComponent<Billboard>();
        SerializedObject soBillboard = new SerializedObject(billboard);
        soBillboard.FindProperty("billboardMode").enumValueIndex = (int)Billboard.BillboardMode.MatchCameraRotation;
        soBillboard.FindProperty("targetCamera").objectReferenceValue = cam;
        soBillboard.ApplyModifiedProperties();

        dummyObj.AddComponent<EnemyHealth>();
        dummyObj.AddComponent<Rigidbody>().isKinematic = true;
    }

    #endregion

    #region Pomocnicze

    private static Sprite GetOrCreateCapsuleSprite(string path, Color fillColor, string label)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite GetOrCreateGoblinSprite(string path, Color fillColor)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite GetOrCreateDummySprite(string path, Color fillColor)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static Sprite GetOrCreateChestSprite(string path, Color fillColor)
    {
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static void EnsureFolder(string relativePath)
    {
        if (AssetDatabase.IsValidFolder(relativePath)) return;

        string parent = Path.GetDirectoryName(relativePath).Replace("\\", "/");
        string folderName = Path.GetFileName(relativePath);

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static int EnsureLayer(string layerName)
    {
        Object[] tagManagers = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagers == null || tagManagers.Length == 0) return 0;

        SerializedObject tagManager = new SerializedObject(tagManagers[0]);
        SerializedProperty layersProp = tagManager.FindProperty("layers");

        for (int i = 0; i < layersProp.arraySize; i++)
        {
            SerializedProperty layer = layersProp.GetArrayElementAtIndex(i);
            if (layer != null && layer.stringValue == layerName)
            {
                return i;
            }
        }

        for (int i = 6; i < layersProp.arraySize; i++)
        {
            SerializedProperty layer = layersProp.GetArrayElementAtIndex(i);
            if (layer != null && string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }

        return 0;
    }

    #endregion
}
