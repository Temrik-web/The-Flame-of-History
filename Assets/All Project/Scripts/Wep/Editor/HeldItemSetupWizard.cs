using UnityEditor;
using UnityEngine;

/// <summary>
/// Мастер «предметы в руках»: ставит ножу и гранате те же правила, по которым
/// уже работает ППШ — модель цепляется к держателю оружия, получает скрипт
/// поведения (MeleeItem / GrenadeItem) и маркер EquippableWeapon.
///
/// Меню: Tools -> Оружие -> Взять нож и гранату в руки.
///
/// Зачем: граната и нож лежали в сцене как обычные объекты, поэтому при
/// экипировке появлялись в мире, а не в руках. Мастер переносит их под
/// держатель ППШ и задаёт стартовую позу.
/// </summary>
public static class HeldItemSetupWizard
{
    // Имя объекта в сцене -> (id, отображаемое имя, вид предмета)
    private enum ItemKind { Melee, Grenade }

    private struct Entry
    {
        public string sceneName;
        public string id;
        public string display;
        public ItemKind kind;
        public string assetPath;

        public Entry(string sceneName, string id, string display, ItemKind kind, string assetPath = "")
        {
            this.sceneName = sceneName;
            this.id = id;
            this.display = display;
            this.kind = kind;
            this.assetPath = assetPath;
        }
    }

    private static readonly Entry[] Known =
    {
        new Entry("Rgd-33(GR)", "rgd33", "РГД-33", ItemKind.Grenade,
                  "Assets/Models/Rgd-33/Rgd-33(GR).fbx"),
        new Entry("Rgd-33",     "rgd33", "РГД-33", ItemKind.Grenade),
        new Entry("Knife(GR)",  "knife", "Нож",    ItemKind.Melee,
                  "Assets/Prefabs/Knife(GR).fbx"),
        new Entry("Knife",      "knife", "Нож",    ItemKind.Melee)
    };

    // =====================================================================
    [MenuItem("Tools/Оружие/Взять нож и гранату в руки", false, 0)]
    public static void SetupHeldItems()
    {
        Transform holder = FindWeaponHolder();
        if (holder == null)
        {
            EditorUtility.DisplayDialog(
                "Предметы в руках",
                "Держатель оружия в сцене не найден.\n\n" +
                "Нужен объект-родитель модели ППШ (обычно WeaponHolder под камерой). " +
                "Открой сцену с игроком и запусти пункт меню снова.",
                "Ок");
            return;
        }

        var report = new System.Collections.Generic.List<string>();
        var seen = new System.Collections.Generic.HashSet<string>();

        foreach (Transform tr in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (tr.gameObject.scene.name == null) continue;
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(tr.gameObject)) continue;

            foreach (Entry entry in Known)
            {
                if (tr.name != entry.sceneName) continue;
                if (seen.Contains(entry.id)) continue;

                SetupOne(tr, entry, holder);
                seen.Add(entry.id);
                report.Add($"{entry.display} ({tr.name})");
                break;
            }
        }

        // Чего в сцене нет — создаём из модели. Нож, например, лежал только
        // как FBX в Assets/Prefabs и в сцене не присутствовал вовсе.
        var created = new System.Collections.Generic.List<string>();
        foreach (Entry entry in Known)
        {
            if (seen.Contains(entry.id)) continue;
            if (string.IsNullOrEmpty(entry.assetPath)) continue;

            Transform spawned = SpawnFromAsset(entry, holder);
            if (spawned == null) continue;

            SetupOne(spawned, entry, holder);
            seen.Add(entry.id);
            report.Add($"{entry.display} ({spawned.name})");
            created.Add(entry.display);
        }

        if (report.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Предметы в руках",
                "Модели ножа и гранаты не найдены ни в сцене, ни в проекте.\n\n" +
                "Ожидались объекты Rgd-33(GR) и Knife(GR). " +
                "Если они называются иначе — добавь скрипт GrenadeItem или MeleeItem вручную, " +
                "остальное он настроит сам.",
                "Ок");
            return;
        }

        RefreshWeaponSlotManager();

        string createdLine = created.Count > 0
            ? $"Создано в сцене заново: {string.Join(", ", created)}\n"
            : "";

        Debug.Log($"[HeldItemSetup] Настроено: {string.Join(", ", report)}. Держатель: {holder.name}.");
        EditorUtility.DisplayDialog(
            "Предметы в руках настроены",
            $"Держатель: {holder.name}\n" +
            $"Настроено: {string.Join(", ", report)}\n" +
            createdLine + "\n" +
            "Управление:\n" +
            "  Нож:     ЛКМ — быстрый удар, ПКМ+ЛКМ — сильный, F — в спину\n" +
            "  Граната: ЛКМ — бросок, ПКМ — прицелиться, ПКМ+ЛКМ — подкатить,\n" +
            "           X — выдернуть запал заранее\n\n" +
            "Позы настраиваются в инспекторе (Hip Position / Hip Rotation).\n" +
            "Удобный способ: подвинь модель в сцене, потом в контекстном меню\n" +
            "компонента выбери «Запомнить текущую позу как в руках».\n\n" +
            "Не забудь сохранить сцену (Ctrl+S).",
            "Ок");
    }

    [MenuItem("Tools/Оружие/Проверить настройку предметов в руках", false, 20)]
    public static void ValidateHeldItems()
    {
        HeldItem[] items = Resources.FindObjectsOfTypeAll<HeldItem>();
        int checkedCount = 0;

        foreach (HeldItem item in items)
        {
            if (item.gameObject.scene.name == null) continue;
            checkedCount++;

            var eq = item.GetComponent<EquippableWeapon>();
            if (eq == null)
                Debug.LogWarning($"[HeldItemSetup] {item.name}: нет EquippableWeapon — " +
                                 "предмет не попадёт в слот оружия.", item);
            else if (string.IsNullOrEmpty(eq.weaponId))
                Debug.LogWarning($"[HeldItemSetup] {item.name}: пустой Weapon Id — " +
                                 "инвентарь не сможет его экипировать.", item);

            if (item.holder == null)
                Debug.Log($"[HeldItemSetup] {item.name}: держатель не задан явно, " +
                          "найдётся автоматически при старте.", item);

            if (item is GrenadeItem grenade && grenade.grenadePrefab == null)
                Debug.Log($"[HeldItemSetup] {grenade.name}: префаб снаряда не задан — " +
                          "в полёте будет использована копия модели из рук.", grenade);
        }

        Debug.Log($"[HeldItemSetup] Проверено предметов в руках: {checkedCount}.");
    }

    // =====================================================================
    /// <summary>
    /// Создать модель в руках из ассета проекта. Нужно, когда предмет
    /// в сцену вообще не положили: нож лежал только как FBX.
    /// </summary>
    private static Transform SpawnFromAsset(Entry entry, Transform holder)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(entry.assetPath);
        if (asset == null)
        {
            Debug.LogWarning($"[HeldItemSetup] {entry.display}: ассет не найден по пути {entry.assetPath}.");
            return null;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, holder);
        if (instance == null) return null;

        instance.name = entry.sceneName;
        Undo.RegisterCreatedObjectUndo(instance, $"Create {entry.display}");
        FitScaleToHand(instance.transform, entry.kind);

        Debug.Log($"[HeldItemSetup] {entry.display} создан в руках из {entry.assetPath}.");
        return instance.transform;
    }

    /// <summary>
    /// Подогнать масштаб под руку. FBX приходят в разных единицах: без этого
    /// нож из импорта может оказаться в полэкрана или невидимой точкой.
    /// Применяется только к моделям, созданным мастером, — уже расставленные
    /// в сцене предметы сохраняют свой масштаб.
    /// </summary>
    private static void FitScaleToHand(Transform model, ItemKind kind)
    {
        float targetSize = kind == ItemKind.Grenade ? 0.22f : 0.3f;

        Bounds bounds = default;
        bool hasBounds = false;

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!hasBounds) return;

        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largest < 0.0001f) return;

        float factor = targetSize / largest;
        model.localScale *= factor;

        Debug.Log($"[HeldItemSetup] {model.name}: масштаб подогнан " +
                  $"(габарит {largest:0.###} -> {targetSize:0.##} м, множитель {factor:0.####}).");
    }

    private static void SetupOne(Transform model, Entry entry, Transform holder)
    {
        GameObject go = model.gameObject;

        // 1) Маркер для слота оружия
        EquippableWeapon eq = go.GetComponent<EquippableWeapon>();
        if (eq == null) eq = Undo.AddComponent<EquippableWeapon>(go);

        eq.weaponId = entry.id;
        eq.displayName = entry.display;
        eq.equippedOnStart = false;
        eq.attachToHolderOnStart = true;
        eq.weaponScripts = null;   // пересоберётся в Awake, включая HeldItem

        // 2) Поведение предмета
        HeldItem held = go.GetComponent<HeldItem>();
        if (held == null)
        {
            held = entry.kind == ItemKind.Grenade
                ? (HeldItem)Undo.AddComponent<GrenadeItem>(go)
                : Undo.AddComponent<MeleeItem>(go);
        }

        held.holder = holder;
        held.autoAttachToHolder = true;

        // 3) В руки прямо сейчас, чтобы позу было видно в редакторе
        Undo.RecordObject(model, "Attach held item");
        if (model.parent != holder) model.SetParent(holder, false);

        if (held.hipPosition == Vector3.zero)
            held.hipPosition = DefaultHipPosition(entry.kind);
        if (held.hipRotation == Vector3.zero)
            held.hipRotation = DefaultHipRotation(entry.kind);
        if (held.aimPosition == Vector3.zero)
            held.aimPosition = DefaultAimPosition(held.hipPosition);
        if (held.aimRotation == Vector3.zero)
            held.aimRotation = held.hipRotation;

        model.localPosition = held.hipPosition;
        model.localEulerAngles = held.hipRotation;

        // 4) Физика в руках не нужна: Rigidbody вырывает модель из держателя
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true))
        {
            Undo.RecordObject(rb, "Disable held physics");
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        EditorUtility.SetDirty(eq);
        EditorUtility.SetDirty(held);
        EditorUtility.SetDirty(model);
    }

    private static Vector3 DefaultHipPosition(ItemKind kind) =>
        kind == ItemKind.Grenade
            ? new Vector3(0.26f, -0.3f, 0.5f)
            : new Vector3(0.3f, -0.32f, 0.52f);

    private static Vector3 DefaultHipRotation(ItemKind kind) =>
        kind == ItemKind.Grenade
            ? new Vector3(-8f, -20f, 6f)
            : new Vector3(-6f, -25f, 10f);

    /// <summary>Поднятый предмет: ближе к центру экрана и к лицу.</summary>
    private static Vector3 DefaultAimPosition(Vector3 hip) =>
        new Vector3(hip.x * 0.45f, hip.y + 0.1f, hip.z * 0.85f);

    /// <summary>
    /// Держатель — родитель модели ППШ. Так нож и граната оказываются
    /// в той же системе координат, что уже настроенное оружие.
    /// </summary>
    private static Transform FindWeaponHolder()
    {
        foreach (Wep w in Resources.FindObjectsOfTypeAll<Wep>())
        {
            if (w.gameObject.scene.name == null) continue;
            if (w.transform.parent != null) return w.transform.parent;
        }

        GameObject named = GameObject.Find("WeaponHolder");
        if (named != null) return named.transform;

        var fps = Object.FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
        if (fps != null && fps.playerCamera != null) return fps.playerCamera;

        Camera cam = Camera.main;
        return cam != null ? cam.transform : null;
    }

    /// <summary>Пересобрать список оружия в слоте, чтобы новые предметы в него попали.</summary>
    private static void RefreshWeaponSlotManager()
    {
        var manager = Object.FindObjectOfType<WeaponSlotManager>();
        if (manager == null)
        {
            Debug.LogWarning("[HeldItemSetup] WeaponSlotManager в сцене не найден. " +
                             "Запусти Tools -> Инвентарь -> Настроить всё автоматически.");
            return;
        }

        Undo.RecordObject(manager, "Refresh weapon slot");
        manager.weapons.Clear();

        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (EquippableWeapon eq in Resources.FindObjectsOfTypeAll<EquippableWeapon>())
        {
            if (eq.gameObject.scene.name == null) continue;
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(eq.gameObject)) continue;
            if (!seen.Add(eq.weaponId)) continue;

            manager.weapons.Add(eq);
        }

        EditorUtility.SetDirty(manager);
        Debug.Log($"[HeldItemSetup] В слоте оружия теперь предметов: {manager.weapons.Count}.");
    }
}
