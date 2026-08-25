using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Мастер быстрой настройки инвентаря и интерфейса.
/// Меню: Tools -> Инвентарь -> Настроить всё автоматически.
///
/// Что делает:
/// 1) Создаёт папки Assets/Resources и Assets/GameData/{Items,Prefabs}.
/// 2) Создаёт набор тестовых ItemData всех категорий и редкостей.
/// 3) Создаёт Assets/Resources/ItemDatabase.asset и наполняет его.
/// 4) Создаёт TMP-шрифт с кириллицей (штатный LiberationSans SDF — только ASCII).
/// 5) Создаёт префаб GenericPickup (куб + коллайдер + Rigidbody + Pickup).
/// 6) Вешает InventorySystem / InventoryUI / InventoryInputBlocker на игрока.
/// 7) Вешает DialogueUI на DialogueManager и FloatingText в сцену.
/// 8) Раскладывает тестовые предметы перед игроком.
/// </summary>
public static class InventorySetupWizard
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string GameDataFolder = "Assets/GameData";
    private const string ItemsFolder = "Assets/GameData/Items";
    private const string PrefabsFolder = "Assets/GameData/Prefabs";

    [MenuItem("Tools/Инвентарь/Настроить всё автоматически", false, 0)]
    public static void SetupAll()
    {
        EnsureFolders();

        var items = new List<ItemData>
        {
            // Оружие: equipWeaponId должен совпадать с Weapon Id
            // у компонента EquippableWeapon на модели в сцене
            CreateOrUpdateItem(
                id: "ppsh41", fileName: "Item_Ppsh41",
                itemName: "ППШ-41",
                description: "Пистолет-пулемёт Шпагина. Дисковый магазин, высокий темп огня.",
                type: ItemType.Weapon, rarity: ItemRarity.Rare,
                useValue: 0f, stackable: false, maxStack: 1, consumeOnUse: false,
                equipWeaponId: "ppsh41"),

            CreateOrUpdateItem(
                id: "rgd33", fileName: "Item_Rgd33",
                itemName: "РГД-33",
                description: "Ручная граната образца 1933 года. Рукоятка с длинным замахом.",
                type: ItemType.Weapon, rarity: ItemRarity.Uncommon,
                useValue: 0f, stackable: false, maxStack: 1, consumeOnUse: false,
                equipWeaponId: "rgd33"),

            CreateOrUpdateItem(
                id: "knife", fileName: "Item_Knife",
                itemName: "Нож",
                description: "Простой окопный нож. Тихо и наверняка.",
                type: ItemType.Weapon, rarity: ItemRarity.Common,
                useValue: 0f, stackable: false, maxStack: 1, consumeOnUse: false,
                equipWeaponId: "knife"),

            CreateOrUpdateItem(
                id: "ammo_762", fileName: "Item_Ammo762",
                itemName: "Диск 7.62",
                description: "Дисковый магазин под ППШ. 71 патрон.",
                type: ItemType.Ammo, rarity: ItemRarity.Common,
                useValue: 1f, stackable: true, maxStack: 10, consumeOnUse: true),

            CreateOrUpdateItem(
                id: "ammo_loose", fileName: "Item_AmmoLoose",
                itemName: "Патроны 7.62 (россыпь)",
                description: "Горсть патронов. Надо чем-то набивать диск.",
                type: ItemType.Ammo, rarity: ItemRarity.Common,
                useValue: 1f, stackable: true, maxStack: 60, consumeOnUse: true),

            CreateOrUpdateItem(
                id: "medkit", fileName: "Item_Medkit",
                itemName: "Аптечка",
                description: "Бинты, жгут, ампула. Хватит, чтобы дойти до укрытия.",
                type: ItemType.Consumable, rarity: ItemRarity.Uncommon,
                useValue: 40f, stackable: true, maxStack: 5, consumeOnUse: true),

            CreateOrUpdateItem(
                id: "bandage", fileName: "Item_Bandage",
                itemName: "Бинт",
                description: "Останавливает кровь. Немного, но лучше, чем ничего.",
                type: ItemType.Consumable, rarity: ItemRarity.Common,
                useValue: 15f, stackable: true, maxStack: 12, consumeOnUse: true),

            CreateOrUpdateItem(
                id: "key_cellar", fileName: "Item_KeyCellar",
                itemName: "Ключ от подвала",
                description: "Ржавый ключ. Похоже, от подвальной двери.",
                type: ItemType.Key, rarity: ItemRarity.Rare,
                useValue: 0f, stackable: false, maxStack: 1, consumeOnUse: false,
                keyId: "cellar"),

            CreateOrUpdateItem(
                id: "letter_old", fileName: "Item_Letter",
                itemName: "Обгоревшее письмо",
                description: "Половина строк выцвела. Читается только дата — и она не сходится.",
                type: ItemType.Misc, rarity: ItemRarity.Legendary,
                useValue: 0f, stackable: false, maxStack: 1, consumeOnUse: false),

            CreateOrUpdateItem(
                id: "scrap", fileName: "Item_Scrap",
                itemName: "Металлолом",
                description: "Пригодится. Когда-нибудь.",
                type: ItemType.Misc, rarity: ItemRarity.Common,
                useValue: 0f, stackable: true, maxStack: 30, consumeOnUse: false)
        };

        GameObject pickupPrefab = CreateGenericPickupPrefab();
        CreateDatabase(items);
        TMP_FontAsset font = CreateCyrillicFont();

        GameObject player = FindPlayer();
        if (player == null)
        {
            EditorUtility.DisplayDialog(
                "Инвентарь",
                "Ассеты созданы, но игрок на сцене не найден.\n\n" +
                "Открой сцену с игроком (объект с FirstPersonController или CharacterController) " +
                "и запусти пункт меню снова.",
                "Ок");
            AssetDatabase.SaveAssets();
            return;
        }

        InventorySystem inv = SetupPlayer(player, pickupPrefab, font);
        SetupDialogueUI(font);
        SetupFloatingText(font);
        SetupWeaponSlots(player);
        SetupAmmoLink(player, inv, items);
        SetupWeaponHud(player, font);
        SpawnTestPickups(player, pickupPrefab, items.ToArray());

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(inv);

        Debug.Log("[InventorySetup] Готово. Нажми Play: смотри на предмет и жми E, инвентарь — Tab.");
        EditorUtility.DisplayDialog(
            "Инвентарь настроен",
            $"Игрок: {player.name}\n\n" +
            "Управление:\n" +
            "  E — подобрать предмет (наведись на него)\n" +
            "  Tab — открыть/закрыть инвентарь\n" +
            "  ЛКМ по ячейке — выбрать, повторный клик — действие\n" +
            "  ПКМ по ячейке — выбросить 1, Shift+ПКМ — всё\n" +
            "  R — сортировать по категориям (в инвентаре)\n" +
            "  Q / E — переключать вкладки категорий\n" +
            "  Колесо мыши — сменить оружие в руках\n" +
            "  0 — убрать оружие\n\n" +
            "Оружие (ППШ-41, РГД-33, Нож) выключено до подбора.\n" +
            "Подбери предмет, выбери его в инвентаре и нажми «Экипировать».\n\n" +
            $"Перед игроком разложено предметов: {items.Count}.\n" +
            "Не забудь сохранить сцену (Ctrl+S).",
            "Ок");
    }

    [MenuItem("Tools/Инвентарь/Разложить тестовые предметы перед игроком", false, 20)]
    public static void SpawnTestItemsOnly()
    {
        GameObject player = FindPlayer();
        if (player == null)
        {
            Debug.LogError("[InventorySetup] Игрок на сцене не найден.");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsFolder}/GenericPickup.prefab");
        if (prefab == null)
        {
            Debug.LogError("[InventorySetup] Префаб GenericPickup не найден. Запусти «Настроить всё автоматически».");
            return;
        }

        var items = new List<ItemData>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) items.Add(item);
        }

        if (items.Count == 0)
        {
            Debug.LogError("[InventorySetup] Не найдено ни одного ItemData.");
            return;
        }

        SpawnTestPickups(player, prefab, items.ToArray());
    }

    [MenuItem("Tools/Инвентарь/Удалить сохранение инвентаря", false, 40)]
    public static void DeleteSave()
    {
        PlayerPrefs.DeleteKey("inventory_v1");
        PlayerPrefs.Save();
        Debug.Log("[InventorySetup] Сохранение инвентаря удалено.");
    }

    [MenuItem("Tools/Инвентарь/Настроить только интерфейс диалогов", false, 21)]
    public static void SetupDialogueOnly()
    {
        EnsureFolders();
        TMP_FontAsset font = CreateCyrillicFont();

        if (Object.FindObjectOfType<DialogueManager>() == null)
        {
            EditorUtility.DisplayDialog(
                "Диалоги",
                "На сцене нет DialogueManager. Добавь его на объект и запусти пункт меню снова.",
                "Ок");
            return;
        }

        SetupDialogueUI(font);
        AssetDatabase.SaveAssets();
        Debug.Log("[InventorySetup] Интерфейс диалогов настроен. Сохрани сцену (Ctrl+S).");
    }

    // =====================================================================
    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(GameDataFolder))
            AssetDatabase.CreateFolder("Assets", "GameData");
        if (!AssetDatabase.IsValidFolder(ItemsFolder))
            AssetDatabase.CreateFolder(GameDataFolder, "Items");
        if (!AssetDatabase.IsValidFolder(PrefabsFolder))
            AssetDatabase.CreateFolder(GameDataFolder, "Prefabs");
    }

    private static ItemData CreateOrUpdateItem(
        string id, string fileName, string itemName, string description,
        ItemType type, ItemRarity rarity, float useValue, bool stackable, int maxStack,
        bool consumeOnUse, string keyId = "", string equipWeaponId = "")
    {
        string path = $"{ItemsFolder}/{fileName}.asset";
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        bool isNew = item == null;

        if (isNew) item = ScriptableObject.CreateInstance<ItemData>();

        item.itemId = id;
        item.itemName = itemName;
        item.description = description;
        item.itemType = type;
        item.rarity = rarity;
        item.useValue = useValue;
        item.stackable = stackable;
        item.maxStack = maxStack;
        item.consumeOnUse = consumeOnUse;
        item.keyId = keyId;
        item.equipWeaponId = equipWeaponId;

        if (isNew) AssetDatabase.CreateAsset(item, path);
        EditorUtility.SetDirty(item);
        return item;
    }

    private static ItemDatabase CreateDatabase(List<ItemData> items)
    {
        string path = $"{ResourcesFolder}/ItemDatabase.asset";
        ItemDatabase db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
        bool isNew = db == null;

        if (isNew) db = ScriptableObject.CreateInstance<ItemDatabase>();

        foreach (ItemData item in items)
            if (!db.items.Contains(item)) db.items.Add(item);

        db.RebuildCache();

        if (isNew) AssetDatabase.CreateAsset(db, path);
        EditorUtility.SetDirty(db);
        return db;
    }

    /// <summary>
    /// Создаёт TMP-шрифт с динамическим атласом из LiberationSans.ttf.
    /// Нужен потому, что штатный "LiberationSans SDF" в проекте статический (только ASCII)
    /// и русский текст в нём отображается пустыми квадратами.
    /// </summary>
    private static TMP_FontAsset CreateCyrillicFont()
    {
        string path = $"{ResourcesFolder}/InventoryFont SDF.asset";
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing != null) return existing;

        Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        if (source == null)
        {
            Debug.LogWarning("[InventorySetup] LiberationSans.ttf не найден — русский текст в UI может " +
                             "не отображаться. Задай свой TMP-шрифт в поле Font Asset у InventoryUI.");
            return null;
        }

        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source);
        if (font == null) return null;

        // Dynamic — символы добавляются в атлас по мере надобности, включая кириллицу
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.name = "InventoryFont SDF";

        AssetDatabase.CreateAsset(font, path);

        // Материал и атлас — подобъекты ассета шрифта
        if (font.material != null) AssetDatabase.AddObjectToAsset(font.material, font);
        if (font.atlasTextures != null)
            foreach (Texture2D tex in font.atlasTextures)
                if (tex != null) AssetDatabase.AddObjectToAsset(tex, font);

        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssets();
        return font;
    }

    private static GameObject CreateGenericPickupPrefab()
    {
        string path = $"{PrefabsFolder}/GenericPickup.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temp.name = "GenericPickup";
        temp.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);

        Rigidbody rb = temp.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.isKinematic = true; // чтобы предмет не улетал; выключи, если хочешь физику

        Pickup pickup = temp.AddComponent<Pickup>();
        pickup.amount = 1;
        pickup.spin = true;
        pickup.bob = true;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
        Object.DestroyImmediate(temp);
        return prefab;
    }

    private static GameObject FindPlayer()
    {
        var fps = Object.FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
        if (fps != null) return fps.gameObject;

        var cc = Object.FindObjectOfType<CharacterController>();
        if (cc != null) return cc.gameObject;

        var health = Object.FindObjectOfType<PlayerHealth>();
        if (health != null) return health.gameObject;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged;
    }

    private static InventorySystem SetupPlayer(GameObject player, GameObject pickupPrefab, TMP_FontAsset font)
    {
        InventorySystem inv = player.GetComponent<InventorySystem>();
        if (inv == null) inv = Undo.AddComponent<InventorySystem>(player);

        inv.genericPickupPrefab = pickupPrefab;
        inv.maxSlots = 20;
        inv.pickupRange = 3.5f;
        inv.pickupCastRadius = 0.2f;
        inv.autoLoadOnStart = false;
        inv.drawDebugGUI = true;

        if (inv.playerCamera == null)
        {
            var fps = player.GetComponent<EasyPeasyFirstPersonController.FirstPersonController>();
            if (fps != null && fps.playerCamera != null)
                inv.playerCamera = fps.playerCamera.GetComponent<Camera>();

            if (inv.playerCamera == null) inv.playerCamera = player.GetComponentInChildren<Camera>();
            if (inv.playerCamera == null) inv.playerCamera = Camera.main;
        }

        if (player.GetComponent<InventoryUI>() == null)
        {
            InventoryUI ui = Undo.AddComponent<InventoryUI>(player);
            ui.inventory = inv;
            ui.autoBuild = true;
            ui.columns = 5;
            ui.fontAsset = font;
        }

        if (player.GetComponent<InventoryInputBlocker>() == null)
        {
            InventoryInputBlocker blocker = Undo.AddComponent<InventoryInputBlocker>(player);
            blocker.inventory = inv;
            blocker.blockWeapons = true;
            blocker.blockMovement = true;
        }

        return inv;
    }

    /// <summary>Повесить красивый интерфейс диалогов на существующий DialogueManager.</summary>
    private static void SetupDialogueUI(TMP_FontAsset font)
    {
        DialogueManager dm = Object.FindObjectOfType<DialogueManager>();
        if (dm == null)
        {
            Debug.Log("[InventorySetup] DialogueManager на сцене не найден — интерфейс диалогов пропущен.");
            return;
        }

        DialogueUI ui = dm.GetComponent<DialogueUI>();
        if (ui == null) ui = Undo.AddComponent<DialogueUI>(dm.gameObject);

        ui.manager = dm;
        ui.fontAsset = font;
        EditorUtility.SetDirty(ui);

        Debug.Log($"[InventorySetup] DialogueUI подключён к {dm.name}.");
    }

    /// <summary>Менеджер всплывающих подписей «+2 Аптечка» в мире.</summary>
    private static void SetupFloatingText(TMP_FontAsset font)
    {
        FloatingText ft = Object.FindObjectOfType<FloatingText>();
        if (ft == null)
        {
            var holder = new GameObject("FloatingTextManager");
            Undo.RegisterCreatedObjectUndo(holder, "Create floating text manager");
            ft = holder.AddComponent<FloatingText>();
        }

        ft.fontAsset = font;
        EditorUtility.SetDirty(ft);
    }

    /// <summary>
    /// Настроить слот оружия: пометить модели в сцене компонентом EquippableWeapon
    /// и повесить WeaponSlotManager на игрока. Оружие выключается до подбора.
    /// </summary>
    private static void SetupWeaponSlots(GameObject player)
    {
        // Имя объекта в сцене -> (id, отображаемое имя)
        var known = new (string sceneName, string id, string display)[]
        {
            ("Ppsh-41(GR)", "ppsh41", "ППШ-41"),
            ("Rgd-33(GR)",  "rgd33",  "РГД-33"),
            ("Knife(GR)",   "knife",  "Нож"),
            ("Knife",       "knife",  "Нож")
        };

        var manager = Object.FindObjectOfType<WeaponSlotManager>();
        if (manager == null)
        {
            manager = player.GetComponent<WeaponSlotManager>();
            if (manager == null) manager = Undo.AddComponent<WeaponSlotManager>(player);
        }

        manager.weapons.Clear();
        manager.autoDisableOnStart = true;

        var seenIds = new HashSet<string>();
        var report = new List<string>();

        // Ищем среди всех Transform, включая выключенные объекты
        foreach (Transform tr in Resources.FindObjectsOfTypeAll<Transform>())
        {
            // Отсекаем ассеты и префабы, оставляем только объекты сцены
            if (tr.gameObject.scene.name == null) continue;
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewSceneObject(tr.gameObject)) continue;

            foreach (var entry in known)
            {
                if (tr.name != entry.sceneName) continue;
                if (seenIds.Contains(entry.id)) continue;

                EquippableWeapon eq = tr.GetComponent<EquippableWeapon>();
                if (eq == null) eq = Undo.AddComponent<EquippableWeapon>(tr.gameObject);

                eq.weaponId = entry.id;
                eq.displayName = entry.display;
                eq.equippedOnStart = false;   // до подбора руки пустые

                manager.weapons.Add(eq);
                seenIds.Add(entry.id);
                report.Add($"{entry.display} ({tr.name})");

                EditorUtility.SetDirty(eq);
                break;
            }
        }

        EditorUtility.SetDirty(manager);

        if (report.Count > 0)
            Debug.Log($"[InventorySetup] Слот оружия настроен. Найдено: {string.Join(", ", report)}");
        else
            Debug.LogWarning("[InventorySetup] Модели оружия в сцене не найдены. " +
                             "Ожидались объекты с именами Ppsh-41(GR), Rgd-33(GR), Knife(GR).");
    }

    /// <summary>
    /// Связать запас магазинов оружия с инвентарём: магазин расходуется
    /// при перезарядке и исчезает из сумки.
    /// </summary>
    private static void SetupAmmoLink(GameObject player, InventorySystem inv, List<ItemData> items)
    {
        WeaponAmmoLink link = player.GetComponent<WeaponAmmoLink>();
        if (link == null) link = Undo.AddComponent<WeaponAmmoLink>(player);

        link.inventory = inv;
        link.magazineItemId = "ammo_762";
        link.magazinesPerItem = 1;

        // Ассет диска — тот, что мастер создал выше
        foreach (ItemData item in items)
        {
            if (item != null && item.Id == "ammo_762") { link.magazineItem = item; break; }
        }

        // Оружие может быть выключено — ищем среди всех объектов сцены
        foreach (Wep w in Resources.FindObjectsOfTypeAll<Wep>())
        {
            if (w.gameObject.scene.name == null) continue;
            link.weapon = w;
            break;
        }

        EditorUtility.SetDirty(link);
        Debug.Log("[InventorySetup] Запас магазинов связан с предметом «Диск 7.62».");
    }

    /// <summary>HUD оружия на Canvas вместо IMGUI, который налезал на инвентарь.</summary>
    private static void SetupWeaponHud(GameObject player, TMP_FontAsset font)
    {
        WeaponHudUI hud = player.GetComponent<WeaponHudUI>();
        if (hud == null) hud = Undo.AddComponent<WeaponHudUI>(player);

        hud.fontAsset = font;
        hud.disableLegacyHud = true;
        EditorUtility.SetDirty(hud);

        // Гасим IMGUI сразу в редакторе, чтобы значение попало в сцену
        foreach (Wep w in Resources.FindObjectsOfTypeAll<Wep>())
        {
            if (w.gameObject.scene.name == null) continue;
            w.drawDebugGUI = false;
            EditorUtility.SetDirty(w);
        }

        Debug.Log("[InventorySetup] HUD оружия переведён на Canvas, старый IMGUI выключен.");
    }

    private static void SpawnTestPickups(GameObject player, GameObject prefab, ItemData[] items)
    {
        Transform root = GameObject.Find("--- Test Pickups ---")?.transform;
        if (root == null)
        {
            var holder = new GameObject("--- Test Pickups ---");
            Undo.RegisterCreatedObjectUndo(holder, "Create pickups holder");
            root = holder.transform;
        }

        // Раскладываем в два ряда, чтобы 9 предметов не растянулись в длинную линию
        const int perRow = 5;
        Vector3 forward = player.transform.forward;
        Vector3 right = player.transform.right;

        for (int i = 0; i < items.Length; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            int inThisRow = Mathf.Min(perRow, items.Length - row * perRow);

            Vector3 pos = player.transform.position
                          + forward * (2.5f + row * 1.3f)
                          + right * ((col - (inThisRow - 1) * 0.5f) * 1.2f)
                          + Vector3.up * 0.5f;

            GameObject obj = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            obj.transform.position = pos;
            obj.name = $"Pickup_{items[i].itemName}";

            Pickup p = obj.GetComponent<Pickup>();
            p.item = items[i];
            p.amount = items[i].stackable ? Random.Range(2, 6) : 1;
            p.highlightColor = items[i].RarityColor;

            Undo.RegisterCreatedObjectUndo(obj, "Create pickup");
            EditorUtility.SetDirty(obj);
        }

        Debug.Log($"[InventorySetup] Разложено предметов: {items.Length} перед {player.name}.");
    }
}
