using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Narzędzie edytorskie konfigurujące assety postaci z paczki:
/// "Tiny RPG Character Asset Pack 01 v2.0 -Free Soldier&Orc".
/// Tworzy ostre pikselowe Sprites, AnimationClips, AnimatorControllers
/// oraz podpina je pod Gracza (Soldier), Wrogów i Bossa (Orc).
/// Dostępne w menu: Tools -> Characters -> Setup Character Animations and Sprites
/// </summary>
public static class CharacterAnimationSetupTool
{
    private const string SoldierSpritesFolder = "Assets/Sprites/Soldier";
    private const string OrcSpritesFolder = "Assets/Sprites/Orc";

    private const string AnimationsRoot = "Assets/Animations";
    private const string SoldierAnimFolder = "Assets/Animations/Soldier";
    private const string OrcAnimFolder = "Assets/Animations/Orc";

    public const string SoldierControllerPath = "Assets/Animations/Soldier/Soldier_Controller.controller";
    public const string OrcControllerPath = "Assets/Animations/Orc/Orc_Controller.controller";

    private const string PlayerPrefabPath = "Assets/Prefabs/Player_Placeholder.prefab";
    private const string EnemyPrefabPath = "Assets/Prefabs/Enemy_Goblin.prefab";

    [InitializeOnLoadMethod]
    private static void AutoRunOnce()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorPrefs.GetBool("TinyRPG_Animations_Generated_v1", false))
            {
                EditorPrefs.SetBool("TinyRPG_Animations_Generated_v1", true);
                SetupAllCharacters();
            }
        };
    }

    [MenuItem("Tools/Characters/Setup Character Animations and Sprites", false, 1)]
    public static void SetupAllCharacters()
    {
        EnsureFolder(AnimationsRoot);
        EnsureFolder(SoldierAnimFolder);
        EnsureFolder(OrcAnimFolder);

        // 1. Konfiguracja ustawień importu pojedynczych sprite'ów (Point, Uncompressed, PPU 16, pivot na stopach)
        ConfigureSpriteFolder(SoldierSpritesFolder, 16f, new Vector2(0.5f, 0.40f));
        ConfigureSpriteFolder(OrcSpritesFolder, 16f, new Vector2(0.5f, 0.40f));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 2. Generowanie animacji dla Żołnierza (Gracz)
        SetupSoldierAnimations();

        // 3. Generowanie animacji dla Orka (Wrogowie i Boss)
        SetupOrcAnimations();

        // 4. Aktualizacja Prefabów
        UpdatePrefabs();

        // 5. Aktualizacja obiektów na aktywnej scenie
        UpdateSceneCharacters();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<b>[CharacterAnimationSetup]</b> Pomyślnie podpięto i zanimowano postaci!\n" +
                  "• <b>Soldier:</b> Gracz otrzymał animacje Idle, Walk, 3 ciosy Combo mieczem, Hurt i Death.\n" +
                  "• <b>Orc:</b> Wrogowie i Boss otrzymali animacje Idle, Walk, Ataki, Hurt i Death.");
    }

    private static void ConfigureSpriteFolder(string folderPath, float ppu, Vector2 pivot)
    {
        if (!Directory.Exists(folderPath)) return;

        string[] files = Directory.GetFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly);
        foreach (string file in files)
        {
            string assetPath = file.Replace("\\", "/");
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
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
    }

    private static void SetupSoldierAnimations()
    {
        Sprite[] idleFrames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Idle");
        Sprite[] walkFrames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Walk");
        Sprite[] atk1Frames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Attack01");
        Sprite[] atk2Frames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Attack02");
        Sprite[] atk3Frames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Attack03");
        Sprite[] hurtFrames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Hurt");
        Sprite[] deathFrames = LoadOrderedSprites(SoldierSpritesFolder, "Soldier_Death");

        AnimationClip idleClip = CreateClip(SoldierAnimFolder + "/Soldier_Idle.anim", idleFrames, 8f, true);
        AnimationClip walkClip = CreateClip(SoldierAnimFolder + "/Soldier_Walk.anim", walkFrames, 10f, true);
        AnimationClip atk1Clip = CreateClip(SoldierAnimFolder + "/Soldier_Attack01.anim", atk1Frames, 12f, false);
        AnimationClip atk2Clip = CreateClip(SoldierAnimFolder + "/Soldier_Attack02.anim", atk2Frames, 12f, false);
        AnimationClip atk3Clip = CreateClip(SoldierAnimFolder + "/Soldier_Attack03.anim", atk3Frames, 14f, false);
        AnimationClip hurtClip = CreateClip(SoldierAnimFolder + "/Soldier_Hurt.anim", hurtFrames, 10f, false);
        AnimationClip deathClip = CreateClip(SoldierAnimFolder + "/Soldier_Death.anim", deathFrames, 8f, false);

        BuildSoldierController(idleClip, walkClip, atk1Clip, atk2Clip, atk3Clip, hurtClip, deathClip);
    }

    private static void BuildSoldierController(AnimationClip idle, AnimationClip walk, AnimationClip atk1, AnimationClip atk2, AnimationClip atk3, AnimationClip hurt, AnimationClip death)
    {
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(SoldierControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsDashing", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("ComboStep", AnimatorControllerParameterType.Int);
        controller.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsStaggered", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Idle
        AnimatorState idleState = sm.AddState("Idle");
        idleState.motion = idle;
        sm.defaultState = idleState;

        // Walk
        AnimatorState walkState = sm.AddState("Walk");
        walkState.motion = walk;

        var toWalk = idleState.AddTransition(walkState);
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        toWalk.hasExitTime = false;
        toWalk.duration = 0.05f;

        var toIdle = walkState.AddTransition(idleState);
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        toIdle.hasExitTime = false;
        toIdle.duration = 0.05f;

        // 3 ciosy combo mieczem
        AddAttackTransition(sm, idleState, "Attack1", atk1, 1);
        AddAttackTransition(sm, idleState, "Attack2", atk2, 2);
        AddAttackTransition(sm, idleState, "Attack3", atk3, 3);

        // Hurt
        AnimatorState hurtState = sm.AddState("Hurt");
        hurtState.motion = hurt;
        var toHurt = sm.AddAnyStateTransition(hurtState);
        toHurt.AddCondition(AnimatorConditionMode.If, 0, "IsStaggered");
        toHurt.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
        toHurt.hasExitTime = false;
        toHurt.duration = 0f;
        toHurt.canTransitionToSelf = false;
        var hurtBack = hurtState.AddTransition(idleState);
        hurtBack.hasExitTime = true;
        hurtBack.exitTime = 0.9f;
        hurtBack.duration = 0.05f;

        // Death
        AnimatorState deathState = sm.AddState("Death");
        deathState.motion = death;
        var toDeath = sm.AddAnyStateTransition(deathState);
        toDeath.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
        toDeath.hasExitTime = false;
        toDeath.duration = 0f;
        toDeath.canTransitionToSelf = false;
    }

    private static void AddAttackTransition(AnimatorStateMachine sm, AnimatorState returnState, string stateName, AnimationClip clip, int comboIndex)
    {
        AnimatorState atkState = sm.AddState(stateName);
        atkState.motion = clip;

        var transition = sm.AddAnyStateTransition(atkState);
        transition.AddCondition(AnimatorConditionMode.If, 0, "Attack");
        transition.AddCondition(AnimatorConditionMode.Equals, comboIndex, "ComboStep");
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
        transition.hasExitTime = false;
        transition.duration = 0f;
        transition.canTransitionToSelf = false;

        var back = atkState.AddTransition(returnState);
        back.hasExitTime = true;
        back.exitTime = 0.95f;
        back.duration = 0.05f;
    }

    private static void SetupOrcAnimations()
    {
        Sprite[] idleFrames = LoadOrderedSprites(OrcSpritesFolder, "Orc_Idle");
        Sprite[] walkFrames = LoadOrderedSprites(OrcSpritesFolder, "Orc_Walk");
        Sprite[] atk1Frames = LoadOrderedSprites(OrcSpritesFolder, "Orc_Attack01");
        Sprite[] atk2Frames = LoadOrderedSprites(OrcSpritesFolder, "Orc_Attack02");
        Sprite[] hurtFrames = LoadOrderedSprites(OrcSpritesFolder, "Orc_Hurt");
        Sprite[] deathFrames = LoadOrderedSprites(OrcSpritesFolder, "Orc_Death");

        AnimationClip idleClip = CreateClip(OrcAnimFolder + "/Orc_Idle.anim", idleFrames, 8f, true);
        AnimationClip walkClip = CreateClip(OrcAnimFolder + "/Orc_Walk.anim", walkFrames, 10f, true);
        AnimationClip atk1Clip = CreateClip(OrcAnimFolder + "/Orc_Attack01.anim", atk1Frames, 10f, false);
        AnimationClip atk2Clip = CreateClip(OrcAnimFolder + "/Orc_Attack02.anim", atk2Frames, 11f, false);
        AnimationClip hurtClip = CreateClip(OrcAnimFolder + "/Orc_Hurt.anim", hurtFrames, 10f, false);
        AnimationClip deathClip = CreateClip(OrcAnimFolder + "/Orc_Death.anim", deathFrames, 8f, false);

        BuildOrcController(idleClip, walkClip, atk1Clip, atk2Clip, hurtClip, deathClip);
    }

    private static void BuildOrcController(AnimationClip idle, AnimationClip walk, AnimationClip atk1, AnimationClip atk2, AnimationClip hurt, AnimationClip death)
    {
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(OrcControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Slash", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Charge", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("GroundSlam", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Roar", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsStaggered", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // Idle
        AnimatorState idleState = sm.AddState("Idle");
        idleState.motion = idle;
        sm.defaultState = idleState;

        // Walk
        AnimatorState walkState = sm.AddState("Walk");
        walkState.motion = walk;

        var toWalk = idleState.AddTransition(walkState);
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        toWalk.hasExitTime = false;
        toWalk.duration = 0.05f;

        var toIdle = walkState.AddTransition(idleState);
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
        toIdle.hasExitTime = false;
        toIdle.duration = 0.05f;

        // Attack 1 (Cios mieczem)
        AnimatorState a1 = sm.AddState("Attack1");
        a1.motion = atk1;
        AddLivingAnyStateTrigger(sm, a1, "Attack");
        AddLivingAnyStateTrigger(sm, a1, "Slash");

        var a1Back = a1.AddTransition(idleState);
        a1Back.hasExitTime = true;
        a1Back.exitTime = 0.95f;
        a1Back.duration = 0.05f;

        // Attack 2 (Szarża / Ground Slam)
        AnimatorState a2 = sm.AddState("Attack2");
        a2.motion = atk2;
        AddLivingAnyStateTrigger(sm, a2, "Charge");
        AddLivingAnyStateTrigger(sm, a2, "GroundSlam");

        var a2Back = a2.AddTransition(idleState);
        a2Back.hasExitTime = true;
        a2Back.exitTime = 0.95f;
        a2Back.duration = 0.05f;

        // Roar
        AnimatorState roarState = sm.AddState("Roar");
        roarState.motion = atk1;
        AddLivingAnyStateTrigger(sm, roarState, "Roar");
        var roarBack = roarState.AddTransition(idleState);
        roarBack.hasExitTime = true;
        roarBack.exitTime = 0.95f;
        roarBack.duration = 0.05f;

        // Hurt
        AnimatorState hurtState = sm.AddState("Hurt");
        hurtState.motion = hurt;
        AddLivingAnyStateTrigger(sm, hurtState, "IsStaggered");
        var hurtBack = hurtState.AddTransition(idleState);
        hurtBack.hasExitTime = true;
        hurtBack.exitTime = 0.9f;
        hurtBack.duration = 0.05f;

        // Death
        AnimatorState deathState = sm.AddState("Death");
        deathState.motion = death;
        var toDeath = sm.AddAnyStateTransition(deathState);
        toDeath.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
        toDeath.hasExitTime = false;
        toDeath.duration = 0f;
        toDeath.canTransitionToSelf = false;

        var toDie = sm.AddAnyStateTransition(deathState);
        toDie.AddCondition(AnimatorConditionMode.If, 0, "Die");
        toDie.hasExitTime = false;
        toDie.duration = 0f;
        toDie.canTransitionToSelf = false;
    }

    private static void AddLivingAnyStateTrigger(AnimatorStateMachine sm, AnimatorState targetState, string triggerName)
    {
        var t = sm.AddAnyStateTransition(targetState);
        t.AddCondition(AnimatorConditionMode.If, 0, triggerName);
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
        t.hasExitTime = false;
        t.duration = 0f;
        t.canTransitionToSelf = false;
    }

    private static void UpdatePrefabs()
    {
        RuntimeAnimatorController soldierCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SoldierControllerPath);
        Sprite soldierFirst = AssetDatabase.LoadAssetAtPath<Sprite>(SoldierSpritesFolder + "/Soldier_Idle_0.png");

        if (File.Exists(PlayerPrefabPath))
        {
            GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            Transform visual = playerRoot.transform.Find("Visual");
            if (visual == null) visual = playerRoot.transform;

            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null && soldierFirst != null) sr.sprite = soldierFirst;

            Animator anim = visual.GetComponent<Animator>();
            if (anim == null) anim = visual.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = soldierCtrl;

            PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(playerRoot);
        }

        RuntimeAnimatorController orcCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(OrcControllerPath);
        Sprite orcFirst = AssetDatabase.LoadAssetAtPath<Sprite>(OrcSpritesFolder + "/Orc_Idle_0.png");

        if (File.Exists(EnemyPrefabPath))
        {
            GameObject enemyRoot = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
            Transform visual = enemyRoot.transform.Find("Visual");
            if (visual == null) visual = enemyRoot.transform;

            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null && orcFirst != null) sr.sprite = orcFirst;

            Animator anim = visual.GetComponent<Animator>();
            if (anim == null) anim = visual.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = orcCtrl;

            PrefabUtility.SaveAsPrefabAsset(enemyRoot, EnemyPrefabPath);
            if (File.Exists("Assets/Resources/Enemy_Goblin.prefab"))
            {
                PrefabUtility.SaveAsPrefabAsset(enemyRoot, "Assets/Resources/Enemy_Goblin.prefab");
            }
            PrefabUtility.UnloadPrefabContents(enemyRoot);
        }
    }

    private static void UpdateSceneCharacters()
    {
        RuntimeAnimatorController soldierCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(SoldierControllerPath);
        Sprite soldierFirst = AssetDatabase.LoadAssetAtPath<Sprite>(SoldierSpritesFolder + "/Soldier_Idle_0.png");

        RuntimeAnimatorController orcCtrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(OrcControllerPath);
        Sprite orcFirst = AssetDatabase.LoadAssetAtPath<Sprite>(OrcSpritesFolder + "/Orc_Idle_0.png");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            Transform visual = player.transform.Find("Visual");
            if (visual == null) visual = player.transform;

            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null && soldierFirst != null) sr.sprite = soldierFirst;

            Animator anim = visual.GetComponent<Animator>();
            if (anim == null) anim = visual.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = soldierCtrl;
        }

        EnemyAI[] enemies = Object.FindObjectsByType<EnemyAI>(FindObjectsInactive.Include);
        foreach (var enemy in enemies)
        {
            Transform visual = enemy.transform.Find("Visual");
            if (visual == null) visual = enemy.transform;

            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null && orcFirst != null) sr.sprite = orcFirst;

            Animator anim = visual.GetComponent<Animator>();
            if (anim == null) anim = visual.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = orcCtrl;
        }

        BossController boss = Object.FindAnyObjectByType<BossController>();
        if (boss != null)
        {
            Transform visual = boss.transform.Find("Visual");
            if (visual == null) visual = boss.transform;

            SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
            if (sr != null && orcFirst != null) sr.sprite = orcFirst;

            Animator anim = visual.GetComponent<Animator>();
            if (anim == null) anim = visual.gameObject.AddComponent<Animator>();
            anim.runtimeAnimatorController = orcCtrl;
        }
    }

    private static Sprite[] LoadOrderedSprites(string folder, string prefix)
    {
        string[] files = Directory.GetFiles(folder, $"{prefix}_*.png", SearchOption.TopDirectoryOnly);
        var ordered = files.OrderBy(f =>
        {
            string name = Path.GetFileNameWithoutExtension(f);
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore >= 0 && int.TryParse(name.Substring(lastUnderscore + 1), out int num))
            {
                return num;
            }
            return 0;
        });

        List<Sprite> sprites = new List<Sprite>();
        foreach (string file in ordered)
        {
            string assetPath = file.Replace("\\", "/");
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (s != null) sprites.Add(s);
        }

        return sprites.ToArray();
    }

    private static AnimationClip CreateClip(string clipPath, Sprite[] frames, float fps, bool isLooping)
    {
        AnimationClip clip = new AnimationClip();
        clip.frameRate = fps;

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "", // Bezpośrednio na komponencie Visual
            propertyName = "m_Sprite"
        };

        if (frames != null && frames.Length > 0)
        {
            ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[frames.Length];
            for (int i = 0; i < frames.Length; i++)
            {
                keyframes[i] = new ObjectReferenceKeyframe
                {
                    time = i / fps,
                    value = frames[i]
                };
            }

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keyframes);
        }

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = isLooping;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, clipPath);
        return clip;
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
}
