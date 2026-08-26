using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using FlameOfHistory.AI;

/// <summary>
/// Мастер быстрого создания и настройки врага для боевой системы (папка AI).
/// Меню: Tools -> Враги.
///
/// Что делает:
/// 1) Создаёт префаб врага Assets/GameData/Prefabs/Enemy.prefab с полным набором
///    компонентов (NavMeshAgent, CharacterHealth, EnemyAI, HitscanWeapon, SuppressionReceiver).
/// 2) Размещает «эталонного» врага перед игроком — его удобно брать для клонирования
///    (Ctrl+D) и расстановки по уровню.
/// 3) Настраивает игрока: вешает CameraShake и SuppressionReceiver на камеру,
///    чтобы работал свист пуль и тряска.
///
/// Сторонние скрипты не трогает — только читает сцену, чтобы найти игрока.
/// </summary>
public static class EnemySetupWizard
{
    private const string GameDataFolder = "Assets/GameData";
    private const string PrefabsFolder = "Assets/GameData/Prefabs";
    private const string EnemyPrefabPath = "Assets/GameData/Prefabs/Enemy.prefab";
    private const string EnemyTemplateName = "== Enemy Template (клонируй меня) ==";

    // =====================================================================
    // 1. Создать префаб врага
    // =====================================================================
    [MenuItem("Tools/Враги/Создать префаб врага", false, 0)]
    public static GameObject CreateEnemyPrefab()
    {
        EnsureFolders();

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (existing != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Префаб врага",
                "Префаб Enemy.prefab уже существует. Пересоздать заново?",
                "Пересоздать", "Оставить как есть");
            if (!overwrite) return existing;
        }

        GameObject root = BuildEnemyObject();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        Debug.Log($"[EnemySetup] Префаб врага создан: {EnemyPrefabPath}");
        return prefab;
    }

    // =====================================================================
    // 2. Разместить эталонного врага перед игроком
    // =====================================================================
    [MenuItem("Tools/Враги/Разместить врага перед игроком", false, 1)]
    public static void SpawnEnemyInFrontOfPlayer()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null)
        {
            prefab = CreateEnemyPrefab();
            if (prefab == null) return;
        }

        GameObject player = FindPlayer();

        Vector3 spawnPos;
        Quaternion spawnRot;

        if (player != null)
        {
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();

            spawnPos = player.transform.position + forward * 5f;
            // Враг смотрит на игрока
            spawnRot = Quaternion.LookRotation(-forward, Vector3.up);
        }
        else
        {
            spawnPos = Vector3.zero;
            spawnRot = Quaternion.identity;
            Debug.LogWarning("[EnemySetup] Игрок не найден — враг размещён в начале координат.");
        }

        // Прижать к NavMesh, если он запечён
        if (NavMesh.SamplePosition(spawnPos, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
            spawnPos = navHit.position;
        else
            Debug.LogWarning("[EnemySetup] NavMesh рядом не найден. Запеки NavMesh, иначе враг не будет двигаться.");

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = EnemyTemplateName;
        instance.transform.SetPositionAndRotation(spawnPos, spawnRot);

        Undo.RegisterCreatedObjectUndo(instance, "Spawn enemy template");
        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);

        // Навести камеру сцены на врага
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("[EnemySetup] Эталонный враг размещён перед игроком. " +
                  "Выдели его и жми Ctrl+D, чтобы клонировать и расставить по уровню.");
    }

    // =====================================================================
    // 3. Настроить игрока (подавление + тряска)
    // =====================================================================
    [MenuItem("Tools/Враги/Настроить игрока (свист пуль + тряска)", false, 20)]
    public static void SetupPlayerFeedback()
    {
        GameObject player = FindPlayer();
        if (player == null)
        {
            EditorUtility.DisplayDialog(
                "Настройка игрока",
                "Игрок на сцене не найден.\n\n" +
                "Открой сцену с игроком (CharacterController / камера) и запусти пункт меню снова.",
                "Ок");
            return;
        }

        // Ищем камеру игрока
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[EnemySetup] Камера игрока не найдена — обратная связь не настроена.");
            return;
        }

        GameObject camGo = cam.gameObject;

        // CameraShake
        CameraShake shake = camGo.GetComponent<CameraShake>();
        if (shake == null) shake = Undo.AddComponent<CameraShake>(camGo);

        // AudioSource под свист
        AudioSource whizz = camGo.GetComponent<AudioSource>();
        if (whizz == null) whizz = Undo.AddComponent<AudioSource>(camGo);
        whizz.playOnAwake = false;
        whizz.spatialBlend = 0f; // 2D, свист «у виска»

        // SuppressionReceiver (игрок)
        SuppressionReceiver receiver = camGo.GetComponent<SuppressionReceiver>();
        if (receiver == null) receiver = Undo.AddComponent<SuppressionReceiver>(camGo);

        var so = new SerializedObject(receiver);
        so.FindProperty("isPlayer").boolValue = true;
        so.FindProperty("nearMissRadius").floatValue = 2.5f;
        so.FindProperty("cameraShake").objectReferenceValue = shake;
        so.FindProperty("whizzSource").objectReferenceValue = whizz;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(camGo);

        Debug.Log($"[EnemySetup] Игрок настроен: CameraShake + SuppressionReceiver на камере «{camGo.name}». " +
                  "Не забудь закинуть звуки свиста в поле Whizz Clips.");
        EditorUtility.DisplayDialog(
            "Игрок настроен",
            $"Камера: {camGo.name}\n\n" +
            "Добавлены:\n" +
            "  • CameraShake — тряска при близких пролётах\n" +
            "  • SuppressionReceiver (isPlayer = true)\n" +
            "  • AudioSource для свиста\n\n" +
            "Осталось вручную: перетащить 2–4 звука свиста/хлопка\n" +
            "в поле «Whizz Clips» у SuppressionReceiver.",
            "Ок");
    }

    // =====================================================================
    // Построение объекта врага со всеми компонентами
    // =====================================================================
    private static GameObject BuildEnemyObject()
    {
        // Тело — капсула (даёт MeshRenderer + CapsuleCollider для обнаружения)
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "Enemy";

        // Слой Characters, если он есть в проекте
        int charLayer = LayerMask.NameToLayer("Characters");
        if (charLayer >= 0) root.layer = charLayer;

        // --- NavMeshAgent ---
        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.speed = 4.2f;
        agent.angularSpeed = 720f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.2f;
        agent.radius = 0.4f;
        agent.height = 2f;

        // --- CharacterHealth (Team.Axis) ---
        CharacterHealth health = root.AddComponent<CharacterHealth>();
        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("team").enumValueIndex = (int)Team.Axis;
        healthSo.FindProperty("maximumHealth").floatValue = 100f;
        healthSo.ApplyModifiedProperties();

        // --- Точка глаз ---
        GameObject eye = new GameObject("EyePoint");
        eye.transform.SetParent(root.transform, false);
        eye.transform.localPosition = new Vector3(0f, 0.7f, 0f);

        // --- Оружие + дуло ---
        GameObject weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(root.transform, false);
        weaponGo.transform.localPosition = new Vector3(0.25f, 0.5f, 0.3f);

        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(weaponGo.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 0.5f);

        AudioSource weaponAudio = weaponGo.AddComponent<AudioSource>();
        weaponAudio.playOnAwake = false;
        weaponAudio.spatialBlend = 1f; // 3D-звук выстрела

        HitscanWeapon weapon = weaponGo.AddComponent<HitscanWeapon>();
        var weaponSo = new SerializedObject(weapon);
        weaponSo.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        weaponSo.FindProperty("audioSource").objectReferenceValue = weaponAudio;
        weaponSo.ApplyModifiedProperties();

        // --- EnemyAI ---
        EnemyAI ai = root.AddComponent<EnemyAI>();
        var aiSo = new SerializedObject(ai);
        aiSo.FindProperty("eyePoint").objectReferenceValue = eye.transform;
        aiSo.FindProperty("weapon").objectReferenceValue = weapon;
        aiSo.FindProperty("enemyTeam").enumValueIndex = (int)Team.Allies;

        // targetMask -> Characters, если слой есть; иначе Everything
        SerializedProperty targetMask = aiSo.FindProperty("targetMask");
        targetMask.intValue = charLayer >= 0 ? (1 << charLayer) : ~0;

        aiSo.FindProperty("visibilityMask").intValue = ~0;
        aiSo.ApplyModifiedProperties();

        // --- SuppressionReceiver (враг) ---
        SuppressionReceiver receiver = root.AddComponent<SuppressionReceiver>();
        var recSo = new SerializedObject(receiver);
        recSo.FindProperty("isPlayer").boolValue = false;
        recSo.FindProperty("nearMissRadius").floatValue = 3f;
        recSo.FindProperty("enemyAI").objectReferenceValue = ai;
        recSo.ApplyModifiedProperties();

        return root;
    }

    // =====================================================================
    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(GameDataFolder))
            AssetDatabase.CreateFolder("Assets", "GameData");
        if (!AssetDatabase.IsValidFolder(PrefabsFolder))
            AssetDatabase.CreateFolder(GameDataFolder, "Prefabs");
    }

    /// <summary>Ищем игрока в сцене, не завязываясь на сторонние типы.</summary>
    private static GameObject FindPlayer()
    {
        // 1) Явный компонент боевой системы из этой папки
        var pc = Object.FindObjectOfType<PlayerCharacter>();
        if (pc != null) return pc.gameObject;

        // 2) Тег Player
        GameObject tagged = null;
        try { tagged = GameObject.FindGameObjectWithTag("Player"); }
        catch { /* тег может быть не определён */ }
        if (tagged != null) return tagged;

        // 3) Любой CharacterController
        var cc = Object.FindObjectOfType<CharacterController>();
        if (cc != null) return cc.gameObject;

        return null;
    }
}