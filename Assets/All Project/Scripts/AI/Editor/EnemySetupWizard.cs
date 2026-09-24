using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using FlameOfHistory.AI;

using CombatEnemyAI = FlameOfHistory.AI.EnemyAI;
using CombatTeam = FlameOfHistory.AI.Team;

/// <summary>Создание префаба врага, шаблона и настройка игрока. Меню Tools -> Враги.</summary>
public static class EnemySetupWizard
{
    private const string GameDataFolder = "Assets/GameData";
    private const string PrefabsFolder = "Assets/GameData/Prefabs";
    private const string EnemyPrefabPath = "Assets/GameData/Prefabs/Enemy.prefab";
    private const string EnemyTemplateName = "== Enemy Template (клонируй меня) ==";

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
            spawnRot = Quaternion.LookRotation(-forward, Vector3.up);
        }
        else
        {
            spawnPos = Vector3.zero;
            spawnRot = Quaternion.identity;
            Debug.LogWarning("[EnemySetup] Игрок не найден — враг размещён в начале координат.");
        }

        if (NavMesh.SamplePosition(spawnPos, out NavMeshHit navHit, 8f, NavMesh.AllAreas))
            spawnPos = navHit.position;
        else
            Debug.Log("[EnemySetup] NavMesh рядом не найден. Враг будет ходить в режиме " +
                      "EnemyMotor.Fallback (по коллайдерам земли). Для полноценной навигации " +
                      "с обходом препятствий запеки NavMesh.");

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = EnemyTemplateName;
        instance.transform.SetPositionAndRotation(spawnPos, spawnRot);

        Undo.RegisterCreatedObjectUndo(instance, "Spawn enemy template");
        Selection.activeGameObject = instance;
        EditorGUIUtility.PingObject(instance);

        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        Debug.Log("[EnemySetup] Эталонный враг размещён перед игроком. " +
                  "Выдели его и жми Ctrl+D, чтобы клонировать и расставить по уровню.");
    }

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

        CharacterHealth playerHealth = player.GetComponent<CharacterHealth>();
        if (playerHealth == null) playerHealth = Undo.AddComponent<CharacterHealth>(player);
        var healthSettings = new SerializedObject(playerHealth);
        healthSettings.FindProperty("team").enumValueIndex = (int)CombatTeam.Allies;
        healthSettings.ApplyModifiedProperties();

        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[EnemySetup] Камера игрока не найдена — обратная связь не настроена.");
            return;
        }

        GameObject camGo = cam.gameObject;

        // SuppressionReceiver тряску не вызывает (только звук), но CameraShake нужен другим системам.
        CameraShake shake = camGo.GetComponent<CameraShake>();
        if (shake == null) Undo.AddComponent<CameraShake>(camGo);

        AudioSource whizz = camGo.GetComponent<AudioSource>();
        if (whizz == null) whizz = Undo.AddComponent<AudioSource>(camGo);
        whizz.playOnAwake = false;
        whizz.spatialBlend = 0f;
        SuppressionReceiver receiver = camGo.GetComponent<SuppressionReceiver>();
        if (receiver == null) receiver = Undo.AddComponent<SuppressionReceiver>(camGo);

        var so = new SerializedObject(receiver);
        so.FindProperty("isPlayer").boolValue = true;
        so.FindProperty("nearMissRadius").floatValue = 2.5f;
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

    [MenuItem("Tools/Враги/Обновить врагов на сцене (ходьба + звуки + оружие)", false, 21)]
    public static void UpgradeSceneEnemies()
    {
        CombatEnemyAI[] enemies = Object.FindObjectsOfType<CombatEnemyAI>(true);
        if (enemies.Length == 0)
        {
            EditorUtility.DisplayDialog("Обновление врагов",
                "На активных сценах не найдено ни одного EnemyAI.", "Ок");
            return;
        }

        int upgraded = 0;

        foreach (CombatEnemyAI ai in enemies)
        {
            GameObject go = ai.gameObject;
            bool changed = false;

            if (go.GetComponent<NavMeshAgent>() == null)
            {
                ConfigureAgent(Undo.AddComponent<NavMeshAgent>(go));
                changed = true;
            }

            if (go.GetComponent<EnemyMotor>() == null)
            {
                ConfigureMotor(Undo.AddComponent<EnemyMotor>(go), go.layer);
                changed = true;
            }

            if (go.GetComponent<EnemyVoice>() == null)
            {
                Undo.AddComponent<EnemyVoice>(go);
                changed = true;
            }

            if (go.GetComponent<EnemyLoadout>() == null)
            {
                Undo.AddComponent<EnemyLoadout>(go);
                changed = true;
            }

            if (changed)
            {
                upgraded++;
                EditorUtility.SetDirty(go);
            }
        }

        Debug.Log($"[EnemySetup] Обновлено врагов: {upgraded} из {enemies.Length}.");
        EditorUtility.DisplayDialog("Обновление врагов",
            $"Найдено врагов: {enemies.Length}\nДобавлены недостающие компоненты: {upgraded}\n\n" +
            "Осталось вручную:\n" +
            "  • закинуть звуки в EnemyVoice (крики, боль, смерть, шаги)\n" +
            "  • указать префаб оружия в EnemyLoadout → Weapon Prefab",
            "Ок");
    }

    private static GameObject BuildEnemyObject()
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "Enemy";

        int charLayer = LayerMask.NameToLayer("Characters");
        if (charLayer >= 0) root.layer = charLayer;
        ConfigureAgent(root.AddComponent<NavMeshAgent>());
        ConfigureMotor(root.AddComponent<EnemyMotor>(), root.layer);
        CharacterHealth health = root.AddComponent<CharacterHealth>();
        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("team").enumValueIndex = (int)CombatTeam.Axis;
        healthSo.FindProperty("maximumHealth").floatValue = 100f;
        healthSo.ApplyModifiedProperties();

        GameObject eye = new GameObject("EyePoint");
        eye.transform.SetParent(root.transform, false);
        eye.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        GameObject weaponGo = new GameObject("Weapon");
        weaponGo.transform.SetParent(root.transform, false);
        weaponGo.transform.localPosition = new Vector3(0.25f, 0.5f, 0.3f);

        GameObject muzzle = new GameObject("Muzzle");
        muzzle.transform.SetParent(weaponGo.transform, false);
        muzzle.transform.localPosition = new Vector3(0f, 0f, 0.5f);

        AudioSource weaponAudio = weaponGo.AddComponent<AudioSource>();
        weaponAudio.playOnAwake = false;
        weaponAudio.spatialBlend = 1f;
        HitscanWeapon weapon = weaponGo.AddComponent<HitscanWeapon>();
        var weaponSo = new SerializedObject(weapon);
        weaponSo.FindProperty("muzzle").objectReferenceValue = muzzle.transform;
        weaponSo.FindProperty("audioSource").objectReferenceValue = weaponAudio;
        weaponSo.ApplyModifiedProperties();

        EnemyVoice voice = root.AddComponent<EnemyVoice>();
        CombatEnemyAI ai = root.AddComponent<CombatEnemyAI>();
        var aiSo = new SerializedObject(ai);
        aiSo.FindProperty("eyePoint").objectReferenceValue = eye.transform;
        aiSo.FindProperty("weapon").objectReferenceValue = weapon;
        aiSo.FindProperty("voice").objectReferenceValue = voice;
        aiSo.FindProperty("enemyTeam").enumValueIndex = (int)CombatTeam.Axis;

        // Свой коллайдер не должен закрывать обзор (глаза внутри тела).
        int selfLayerMask = charLayer >= 0 ? (1 << charLayer) : 0;

        SerializedProperty targetMask = aiSo.FindProperty("targetMask");
        SerializedProperty visibilityMask = aiSo.FindProperty("visibilityMask");
        targetMask.intValue = selfLayerMask != 0 ? ~selfLayerMask : ~0;
        visibilityMask.intValue = selfLayerMask != 0 ? ~selfLayerMask : ~0;
        aiSo.ApplyModifiedProperties();

        // existingWeapon не проставляем: Loadout сам найдёт оружие, не сбивая позицию.
        root.AddComponent<EnemyLoadout>();
        SuppressionReceiver receiver = root.AddComponent<SuppressionReceiver>();
        var recSo = new SerializedObject(receiver);
        recSo.FindProperty("isPlayer").boolValue = false;
        recSo.FindProperty("nearMissRadius").floatValue = 3f;
        recSo.FindProperty("enemyAI").objectReferenceValue = ai;
        recSo.ApplyModifiedProperties();

        return root;
    }

    private static void ConfigureAgent(NavMeshAgent agent)
    {
        agent.speed = 4.2f;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.2f;
        agent.radius = 0.4f;
        agent.height = 2f;
    }

    private static void ConfigureMotor(EnemyMotor motor, int ownerLayer)
    {
        var so = new SerializedObject(motor);

        so.FindProperty("allowFallbackMovement").boolValue = true;
        so.FindProperty("arriveRadius").floatValue = 1.2f;
        so.FindProperty("bodyRadius").floatValue = 0.4f;
        so.FindProperty("groundOffset").floatValue = 1f;
        // Исключаем слой персонажей, иначе враги спотыкаются друг о друга.
        int exclude = ownerLayer >= 0 ? ~(1 << ownerLayer) : ~0;
        so.FindProperty("groundMask").intValue = exclude;
        so.FindProperty("obstacleMask").intValue = exclude;

        so.ApplyModifiedProperties();
    }

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
        var pc = Object.FindObjectOfType<PlayerHealth>();
        if (pc != null) return pc.gameObject;
        GameObject tagged = null;
        try { tagged = GameObject.FindGameObjectWithTag("Player"); }
        catch { /* тег может быть не определён */ }
        if (tagged != null) return tagged;
        var cc = Object.FindObjectOfType<CharacterController>();
        if (cc != null) return cc.gameObject;

        return null;
    }
}
