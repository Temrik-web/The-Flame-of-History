using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Сеточный UI инвентаря с вкладками категорий.
/// Перерисовывается только по событию OnInventoryChanged.
///
/// Два режима работы:
/// 1) Ручной — задай Inventory Panel, Slots Container и Slot Prefab в инспекторе.
/// 2) Автоматический (autoBuild = true) — весь UI создаётся кодом при старте:
///    Canvas, затемняющий фон, панель со скруглёнными углами, вкладки категорий,
///    сетка ячеек, тултип и HUD-подсказка «E — подобрать».
/// </summary>
[DisallowMultipleComponent]
public class InventoryUI : MonoBehaviour
{
    [Header("Ссылки")]
    public InventorySystem inventory;

    [Header("Ручной режим (если autoBuild = false)")]
    public GameObject inventoryPanel;
    public Transform slotsContainer;
    public InventorySlotUI slotPrefab;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI capacityText;

    [Header("Тултип")]
    public GameObject tooltipRoot;
    public TextMeshProUGUI tooltipName;
    public TextMeshProUGUI tooltipCategory;
    public TextMeshProUGUI tooltipDescription;

    [Header("Подсказка подбора (HUD)")]
    public GameObject pickupHintRoot;
    public TextMeshProUGUI pickupHintText;

    [Header("Автосборка UI")]
    [Tooltip("Создать весь UI кодом при старте. Удобно, чтобы не собирать Canvas руками.")]
    public bool autoBuild = true;
    [Min(1)] public int columns = 5;
    public Vector2 cellSize = new Vector2(88f, 88f);
    public Vector2 cellSpacing = new Vector2(10f, 10f);

    [Header("Оформление")]
    public Color panelColor = new Color(0.07f, 0.075f, 0.095f, 0.96f);
    public Color accentColor = new Color(1f, 0.66f, 0.28f);
    public Color textColor = new Color(0.93f, 0.94f, 0.96f);
    [Tooltip("Затемнение мира за панелью инвентаря.")]
    [Range(0f, 1f)] public float backdropOpacity = 0.62f;
    [Tooltip("Плавное появление панели.")]
    public float fadeDuration = 0.18f;

    [Header("Вкладки категорий")]
    [Tooltip("Показывать вкладки Всё / Оружие / Патроны / Медикаменты / Ключи / Прочее.")]
    public bool showCategoryTabs = true;
    public KeyCode nextTabKey = KeyCode.E;
    public KeyCode prevTabKey = KeyCode.Q;

    [Header("Шрифт")]
    [Tooltip("TMP-шрифт с поддержкой кириллицы. Если пусто — берётся Resources/InventoryFont SDF. " +
             "Стандартный LiberationSans SDF в проекте статический (только ASCII) и русский текст не покажет.")]
    public TMP_FontAsset fontAsset;

    // ---------- внутреннее ----------
    private readonly List<InventorySlotUI> slotViews = new List<InventorySlotUI>();
    private readonly List<CategoryTab> tabs = new List<CategoryTab>();
    private int hoveredIndex = -1;
    private bool built;

    private CanvasGroup panelGroup;
    private Image backdrop;
    private RectTransform panelRect;
    private Coroutine fadeRoutine;

    // null = вкладка «Всё»
    private ItemType? activeFilter;

    private class CategoryTab
    {
        public ItemType? type;   // null = «Всё»
        public Image background;
        public Image underline;
        public TextMeshProUGUI label;
        public Color color;
    }

    // =====================================================================
    void Awake()
    {
        if (inventory == null) inventory = GetComponent<InventorySystem>();
        if (inventory == null) inventory = InventorySystem.Instance;
        if (inventory == null) inventory = FindObjectOfType<InventorySystem>();

        if (inventory == null)
        {
            Debug.LogError("[InventoryUI] InventorySystem не найден. UI отключён.");
            enabled = false;
            return;
        }

        if (fontAsset == null)
            fontAsset = Resources.Load<TMP_FontAsset>("InventoryFont SDF");

        if (autoBuild && inventoryPanel == null) BuildUI();

        // Панелью управляем сами (нужен fade), поэтому у системы ссылку не ставим
        if (inventoryPanel != null)
        {
            inventory.inventoryPanel = null;
            inventory.drawDebugGUI = false;
        }
    }

    void OnEnable()
    {
        if (inventory == null) return;
        inventory.OnInventoryChanged += Redraw;
        inventory.OnToggled += HandleToggled;
        inventory.OnTargetChanged += HandleTargetChanged;
        inventory.OnSorted += HandleSorted;
    }

    void OnDisable()
    {
        if (inventory == null) return;
        inventory.OnInventoryChanged -= Redraw;
        inventory.OnToggled -= HandleToggled;
        inventory.OnTargetChanged -= HandleTargetChanged;
        inventory.OnSorted -= HandleSorted;
    }

    void Start()
    {
        EnsureSlotViews();
        Redraw();
        HideTooltip();
        HandleTargetChanged(inventory.CurrentTarget);

        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(inventory.IsOpen);
            if (panelGroup != null) panelGroup.alpha = inventory.IsOpen ? 1f : 0f;
        }
    }

    void Update()
    {
        if (inventory == null || !inventory.IsOpen || !showCategoryTabs) return;

        if (Input.GetKeyDown(nextTabKey)) CycleTab(1);
        else if (Input.GetKeyDown(prevTabKey)) CycleTab(-1);
    }

    // =====================================================================
    // Вкладки
    // =====================================================================
    void CycleTab(int direction)
    {
        if (tabs.Count == 0) return;

        int current = 0;
        for (int i = 0; i < tabs.Count; i++)
        {
            if (Equals(tabs[i].type, activeFilter)) { current = i; break; }
        }

        int next = (current + direction + tabs.Count) % tabs.Count;
        SetFilter(tabs[next].type);
    }

    public void SetFilter(ItemType? type)
    {
        activeFilter = type;
        UpdateTabVisuals();
        Redraw();

        // Ячейки заново «выпрыгивают» — переключение вкладки становится заметным
        foreach (InventorySlotUI view in slotViews)
            if (view.gameObject.activeSelf && !view.IsEmpty) view.Pop();
    }

    void UpdateTabVisuals()
    {
        foreach (CategoryTab tab in tabs)
        {
            bool active = Equals(tab.type, activeFilter);

            if (tab.label != null)
                tab.label.color = active ? tab.color
                                         : new Color(textColor.r, textColor.g, textColor.b, 0.45f);

            if (tab.background != null)
                tab.background.color = active ? new Color(tab.color.r, tab.color.g, tab.color.b, 0.14f)
                                              : new Color(1f, 1f, 1f, 0.03f);

            if (tab.underline != null)
                tab.underline.color = active ? tab.color : new Color(0f, 0f, 0f, 0f);
        }
    }

    // =====================================================================
    // Отрисовка
    // =====================================================================
    void EnsureSlotViews()
    {
        if (slotsContainer == null || slotPrefab == null) return;
        if (slotViews.Count >= inventory.MaxSlots) return;

        while (slotViews.Count < inventory.MaxSlots)
        {
            InventorySlotUI view = Instantiate(slotPrefab, slotsContainer);
            view.gameObject.SetActive(true);
            view.name = $"Slot_{slotViews.Count}";
            view.Init(this, slotViews.Count);
            slotViews.Add(view);
        }

        // Новые ячейки нужно расставить — просим раскладку отработать ещё раз
        var freezer = slotsContainer.GetComponent<GridLayoutFreezer>();
        if (freezer != null) freezer.Rebuild();
    }

    public void Redraw()
    {
        if (inventory == null) return;
        EnsureSlotViews();

        if (activeFilter == null)
        {
            // «Всё»: слоты по порядку, остальные ячейки пустые
            for (int i = 0; i < slotViews.Count; i++)
            {
                slotViews[i].gameObject.SetActive(true);
                slotViews[i].SetSlot(inventory.GetSlot(i));
            }
        }
        else
        {
            // Фильтр: показываем только подходящие предметы, добиваем пустыми ячейками
            List<int> indices = inventory.GetSlotIndicesOfCategory(activeFilter.Value);

            for (int i = 0; i < slotViews.Count; i++)
            {
                slotViews[i].gameObject.SetActive(true);
                slotViews[i].SetSlot(i < indices.Count ? inventory.GetSlot(indices[i]) : null);
            }
        }

        if (titleText != null)
        {
            string suffix = activeFilter == null
                ? ""
                : $"  <size=70%><color=#8a8f99>/ {ItemData.GetCategoryName(activeFilter.Value)}</color></size>";
            titleText.text = "ИНВЕНТАРЬ" + suffix;
        }

        if (capacityText != null)
        {
            float fill = inventory.MaxSlots > 0 ? inventory.SlotCount / (float)inventory.MaxSlots : 0f;
            string color = fill >= 1f ? "#e06c5a" : fill > 0.8f ? "#e0b45a" : "#8a8f99";
            capacityText.text = $"<color={color}>{inventory.SlotCount}</color>" +
                                $"<color=#5a5f69> / {inventory.MaxSlots}</color>";
        }

        UpdateTabCounts();

        if (hoveredIndex >= 0) ShowTooltip(hoveredIndex);
    }

    void UpdateTabCounts()
    {
        foreach (CategoryTab tab in tabs)
        {
            if (tab.label == null) continue;

            string name = tab.type == null ? "Всё" : ItemData.GetCategoryName(tab.type.Value);
            int count = tab.type == null
                ? inventory.SlotCount
                : inventory.GetSlotIndicesOfCategory(tab.type.Value).Count;

            tab.label.text = count > 0 ? $"{name} <size=80%><color=#6f747e>{count}</color></size>" : name;
        }
    }

    void HandleSorted()
    {
        // Волна «выпрыгивания» слева-направо, чтобы сортировка читалась глазом
        StartCoroutine(SortWave());
    }

    System.Collections.IEnumerator SortWave()
    {
        for (int i = 0; i < slotViews.Count; i++)
        {
            if (slotViews[i].gameObject.activeSelf && !slotViews[i].IsEmpty)
                slotViews[i].Pop();

            if (i % columns == columns - 1)
                yield return new WaitForSecondsRealtime(0.03f);
        }
    }

    void HandleToggled(bool open)
    {
        if (inventoryPanel == null) return;

        if (fadeRoutine != null) StopCoroutine(fadeRoutine);

        if (open)
        {
            inventoryPanel.SetActive(true);
            // При открытии всегда начинаем со вкладки «Всё» — предсказуемо
            activeFilter = null;
            UpdateTabVisuals();
            Redraw();
            fadeRoutine = StartCoroutine(FadePanel(1f));
        }
        else
        {
            HideTooltip();
            fadeRoutine = StartCoroutine(FadePanel(0f));
        }

        if (pickupHintRoot != null && open) pickupHintRoot.SetActive(false);
    }

    System.Collections.IEnumerator FadePanel(float target)
    {
        if (panelGroup == null)
        {
            inventoryPanel.SetActive(target > 0f);
            yield break;
        }

        float start = panelGroup.alpha;
        float startScale = target > 0f ? 0.94f : 1f;
        float endScale = target > 0f ? 1f : 0.97f;
        float elapsed = 0f;

        // Время не масштабируется: инвентарь может ставить игру на паузу
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDuration));

            panelGroup.alpha = Mathf.Lerp(start, target, t);
            if (backdrop != null)
                backdrop.color = new Color(0f, 0f, 0f, backdropOpacity * panelGroup.alpha);
            if (panelRect != null)
            {
                float s = Mathf.Lerp(startScale, endScale, t);
                panelRect.localScale = new Vector3(s, s, 1f);
            }
            yield return null;
        }

        panelGroup.alpha = target;
        if (backdrop != null)
            backdrop.color = new Color(0f, 0f, 0f, backdropOpacity * target);
        if (panelRect != null) panelRect.localScale = Vector3.one;

        if (target <= 0f) inventoryPanel.SetActive(false);
        fadeRoutine = null;
    }

    void HandleTargetChanged(Pickup target)
    {
        if (pickupHintRoot == null) return;

        bool show = target != null && !inventory.IsOpen;
        pickupHintRoot.SetActive(show);

        if (show && pickupHintText != null)
        {
            ItemData item = target.item;
            if (item != null)
            {
                string hex = ColorUtility.ToHtmlStringRGB(item.RarityColor);
                string amount = target.amount > 1 ? $" <color=#8a8f99>x{target.amount}</color>" : "";
                pickupHintText.text = $"<color=#8a8f99>[E]</color>  <color=#{hex}>{item.itemName}</color>{amount}";
            }
            else
            {
                pickupHintText.text = target.GetPrompt();
            }
        }
    }

    // =====================================================================
    // Обработчики от ячеек
    // =====================================================================
    /// <summary>Индекс в реальном списке слотов с учётом активного фильтра.</summary>
    int ResolveIndex(int viewIndex)
    {
        if (activeFilter == null) return viewIndex;

        List<int> indices = inventory.GetSlotIndicesOfCategory(activeFilter.Value);
        return viewIndex < indices.Count ? indices[viewIndex] : -1;
    }

    public void OnSlotLeftClick(int viewIndex)
    {
        int real = ResolveIndex(viewIndex);
        if (real >= 0) inventory.UseSlot(real);
    }

    public void OnSlotRightClick(int viewIndex)
    {
        int real = ResolveIndex(viewIndex);
        if (real < 0) return;

        if (Input.GetKey(KeyCode.LeftShift)) inventory.DropSlot(real);
        else inventory.DropOne(real);
    }

    public void OnSlotHover(int viewIndex, bool entered)
    {
        if (entered)
        {
            hoveredIndex = viewIndex;
            ShowTooltip(viewIndex);
        }
        else if (hoveredIndex == viewIndex)
        {
            hoveredIndex = -1;
            HideTooltip();
        }
    }

    void ShowTooltip(int viewIndex)
    {
        if (tooltipRoot == null) return;

        int real = ResolveIndex(viewIndex);
        InventorySystem.Slot slot = real >= 0 ? inventory.GetSlot(real) : null;

        if (slot == null || slot.IsEmpty)
        {
            HideTooltip();
            return;
        }

        tooltipRoot.SetActive(true);
        ItemData item = slot.item;
        string rarityHex = ColorUtility.ToHtmlStringRGB(item.RarityColor);

        if (tooltipName != null)
        {
            tooltipName.text = slot.amount > 1
                ? $"{item.itemName} <color=#8a8f99>x{slot.amount}</color>"
                : item.itemName;
            tooltipName.color = item.RarityColor;
        }

        if (tooltipCategory != null)
        {
            string catHex = ColorUtility.ToHtmlStringRGB(ItemData.GetCategoryColor(item.itemType));
            tooltipCategory.text = $"<color=#{catHex}>{item.CategoryName}</color>" +
                                   $"<color=#5a5f69>  ·  </color>" +
                                   $"<color=#{rarityHex}>{GetRarityName(item.rarity)}</color>";
        }

        if (tooltipDescription != null)
        {
            string action = "";
            switch (item.itemType)
            {
                case ItemType.Consumable:
                    action = $"\n<color=#7fd694>ЛКМ — восстановить {item.useValue:0} HP</color>";
                    break;
                case ItemType.Ammo:
                    action = $"\n<color=#7fd694>ЛКМ — +{item.useValue:0} магазин(ов)</color>";
                    break;
                case ItemType.Key:
                    action = $"\n<color=#e0c86a>Отпирает: {item.keyId}</color>";
                    break;
            }

            string desc = string.IsNullOrEmpty(item.description) ? "" : item.description;
            tooltipDescription.text = desc + action +
                "\n<color=#5a5f69>ПКМ — выбросить 1 · Shift+ПКМ — всё</color>";
        }
    }

    static string GetRarityName(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Uncommon:  return "Необычный";
            case ItemRarity.Rare:      return "Редкий";
            case ItemRarity.Epic:      return "Эпический";
            case ItemRarity.Legendary: return "Легендарный";
            default:                   return "Обычный";
        }
    }

    void HideTooltip()
    {
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    // =====================================================================
    // Автосборка UI кодом
    // =====================================================================
    void BuildUI()
    {
        if (built) return;
        built = true;

        Sprite roundFill = UIShapes.RoundedRect(64, 14);
        Sprite roundThin = UIShapes.RoundedRect(48, 10);
        Sprite solid = UIShapes.Solid();

        // --- Canvas ---
        Canvas canvas = new GameObject("InventoryCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // --- Корень панели (fade целиком) ---
        GameObject root = CreateUIObject("InventoryRoot", canvas.transform);
        RectTransform rootRect = (RectTransform)root.transform;
        Stretch(rootRect);

        panelGroup = root.AddComponent<CanvasGroup>();
        panelGroup.alpha = 0f;
        inventoryPanel = root;

        // --- Затемнение мира ---
        GameObject dim = CreateUIObject("Backdrop", root.transform);
        Stretch((RectTransform)dim.transform);
        backdrop = dim.AddComponent<Image>();
        backdrop.sprite = solid;
        backdrop.color = new Color(0f, 0f, 0f, backdropOpacity);
        backdrop.raycastTarget = true; // перехватывает клики мимо панели

        // --- Размеры панели ---
        int rows = Mathf.CeilToInt(inventory.MaxSlots / (float)columns);
        float gridW = columns * cellSize.x + (columns - 1) * cellSpacing.x;
        float gridH = rows * cellSize.y + (rows - 1) * cellSpacing.y;
        float width = gridW + 56f;
        float height = gridH + (showCategoryTabs ? 190f : 140f);

        // --- Панель ---
        GameObject panel = CreateUIObject("Panel", root.transform);
        panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(width, height);

        Image panelBg = panel.AddComponent<Image>();
        panelBg.sprite = roundFill;
        panelBg.type = Image.Type.Sliced;
        panelBg.color = panelColor;

        // Тонкая светлая окантовка — панель «отрывается» от фона
        GameObject outline = CreateUIObject("Outline", panel.transform);
        Stretch((RectTransform)outline.transform);
        Image outlineImg = outline.AddComponent<Image>();
        outlineImg.sprite = UIShapes.RoundedRect(64, 14, 2);
        outlineImg.type = Image.Type.Sliced;
        outlineImg.color = new Color(1f, 1f, 1f, 0.09f);
        outlineImg.raycastTarget = false;

        // --- Заголовок ---
        titleText = CreateLabel("Title", panel.transform);
        RectTransform titleRect = (RectTransform)titleText.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(28f, -62f);
        titleRect.offsetMax = new Vector2(-28f, -18f);
        titleText.text = "ИНВЕНТАРЬ";
        titleText.fontSize = 28f;
        titleText.characterSpacing = 8f;
        titleText.color = textColor;
        titleText.alignment = TextAlignmentOptions.Left;

        // --- Счётчик занятых слотов ---
        capacityText = CreateLabel("Capacity", panel.transform);
        RectTransform capRect = (RectTransform)capacityText.transform;
        capRect.anchorMin = new Vector2(0f, 1f);
        capRect.anchorMax = new Vector2(1f, 1f);
        capRect.pivot = new Vector2(0.5f, 1f);
        capRect.offsetMin = new Vector2(28f, -62f);
        capRect.offsetMax = new Vector2(-28f, -18f);
        capacityText.fontSize = 24f;
        capacityText.alignment = TextAlignmentOptions.Right;

        // --- Разделитель под заголовком (градиент акцентного цвета) ---
        GameObject divider = CreateUIObject("Divider", panel.transform);
        RectTransform divRect = (RectTransform)divider.transform;
        divRect.anchorMin = new Vector2(0f, 1f);
        divRect.anchorMax = new Vector2(1f, 1f);
        divRect.pivot = new Vector2(0.5f, 1f);
        divRect.offsetMin = new Vector2(28f, -68f);
        divRect.offsetMax = new Vector2(-28f, -66f);
        Image divImg = divider.AddComponent<Image>();
        divImg.sprite = solid;
        divImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.35f);
        divImg.raycastTarget = false;

        float gridTopOffset = -78f;

        // --- Вкладки категорий ---
        if (showCategoryTabs)
        {
            GameObject tabsRow = CreateUIObject("Tabs", panel.transform);
            RectTransform tabsRect = (RectTransform)tabsRow.transform;
            tabsRect.anchorMin = new Vector2(0f, 1f);
            tabsRect.anchorMax = new Vector2(1f, 1f);
            tabsRect.pivot = new Vector2(0.5f, 1f);
            tabsRect.offsetMin = new Vector2(28f, -122f);
            tabsRect.offsetMax = new Vector2(-28f, -80f);

            HorizontalLayoutGroup tabsLayout = tabsRow.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 6f;
            tabsLayout.childForceExpandWidth = true;
            tabsLayout.childForceExpandHeight = true;
            tabsLayout.childAlignment = TextAnchor.MiddleLeft;

            CreateTab(tabsRow.transform, null, accentColor, roundThin);
            foreach (ItemType type in ItemData.DisplayOrder)
                CreateTab(tabsRow.transform, type, ItemData.GetCategoryColor(type), roundThin);

            gridTopOffset = -132f;
            UpdateTabVisuals();
        }

        // --- Сетка ---
        GameObject grid = CreateUIObject("SlotsGrid", panel.transform);
        RectTransform gridRect = (RectTransform)grid.transform;
        gridRect.anchorMin = new Vector2(0f, 0f);
        gridRect.anchorMax = new Vector2(1f, 1f);
        gridRect.offsetMin = new Vector2(28f, 46f);
        gridRect.offsetMax = new Vector2(-28f, gridTopOffset);

        GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
        layout.cellSize = cellSize;
        layout.spacing = cellSpacing;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        layout.childAlignment = TextAnchor.UpperCenter;
        slotsContainer = grid.transform;

        // Ячейки анимируют свой anchoredPosition, поэтому раскладка должна
        // отработать один раз и больше не перетирать позиции.
        grid.AddComponent<GridLayoutFreezer>();

        // --- Подпись управления ---
        TextMeshProUGUI hintLabel = CreateLabel("ControlsHint", panel.transform);
        RectTransform hintRect = (RectTransform)hintLabel.transform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(28f, 14f);
        hintRect.offsetMax = new Vector2(-28f, 38f);
        hintLabel.text = "<color=#8a8f99>ЛКМ</color> использовать   " +
                         "<color=#8a8f99>ПКМ</color> выбросить   " +
                         "<color=#8a8f99>R</color> сортировать   " +
                         "<color=#8a8f99>Q/E</color> вкладки   " +
                         "<color=#8a8f99>Tab</color> закрыть";
        hintLabel.fontSize = 16f;
        hintLabel.color = new Color(textColor.r, textColor.g, textColor.b, 0.5f);
        hintLabel.alignment = TextAlignmentOptions.Center;

        // --- Префаб ячейки ---
        slotPrefab = BuildSlotPrefab(canvas.transform, roundThin);

        BuildTooltip(canvas.transform, roundFill);
        BuildPickupHint(canvas.transform, roundThin);

        root.SetActive(false);
    }

    void CreateTab(Transform parent, ItemType? type, Color color, Sprite roundSprite)
    {
        GameObject tabObj = CreateUIObject(type == null ? "Tab_All" : $"Tab_{type}", parent);

        Image bg = tabObj.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 1f, 1f, 0.03f);

        Button button = tabObj.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        ItemType? captured = type;
        button.onClick.AddListener(() => SetFilter(captured));

        TextMeshProUGUI label = CreateLabel("Label", tabObj.transform);
        Stretch((RectTransform)label.transform, 6f, 2f);
        label.text = type == null ? "Всё" : ItemData.GetCategoryName(type.Value);
        label.fontSize = 17f;
        label.alignment = TextAlignmentOptions.Center;

        // Подчёркивание активной вкладки
        GameObject line = CreateUIObject("Underline", tabObj.transform);
        RectTransform lineRect = (RectTransform)line.transform;
        lineRect.anchorMin = new Vector2(0f, 0f);
        lineRect.anchorMax = new Vector2(1f, 0f);
        lineRect.pivot = new Vector2(0.5f, 0f);
        lineRect.offsetMin = new Vector2(8f, 0f);
        lineRect.offsetMax = new Vector2(-8f, 2.5f);
        Image lineImg = line.AddComponent<Image>();
        lineImg.sprite = UIShapes.Solid();
        lineImg.color = new Color(0f, 0f, 0f, 0f);
        lineImg.raycastTarget = false;

        tabs.Add(new CategoryTab
        {
            type = type,
            background = bg,
            underline = lineImg,
            label = label,
            color = color
        });
    }

    InventorySlotUI BuildSlotPrefab(Transform canvasRoot, Sprite roundSprite)
    {
        GameObject slot = CreateUIObject("SlotTemplate", canvasRoot);
        RectTransform rect = (RectTransform)slot.transform;
        rect.sizeDelta = cellSize;

        Image bg = slot.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 1f, 1f, 0.06f);

        InventorySlotUI view = slot.AddComponent<InventorySlotUI>();
        view.background = bg;

        // Рамка редкости
        GameObject frame = CreateUIObject("RarityFrame", slot.transform);
        Stretch((RectTransform)frame.transform);
        Image frameImg = frame.AddComponent<Image>();
        frameImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        frameImg.type = Image.Type.Sliced;
        frameImg.raycastTarget = false;
        frameImg.enabled = false;
        view.rarityFrame = frameImg;

        // Иконка
        GameObject icon = CreateUIObject("Icon", slot.transform);
        Stretch((RectTransform)icon.transform, 12f, 16f);
        Image iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.enabled = false;
        view.iconImage = iconImage;

        // Полоска заполнения стака
        GameObject bar = CreateUIObject("StackBar", slot.transform);
        RectTransform barRect = (RectTransform)bar.transform;
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(1f, 0f);
        barRect.pivot = new Vector2(0f, 0f);
        barRect.offsetMin = new Vector2(8f, 6f);
        barRect.offsetMax = new Vector2(-8f, 9f);
        Image barImg = bar.AddComponent<Image>();
        barImg.sprite = UIShapes.Solid();
        barImg.type = Image.Type.Filled;
        barImg.fillMethod = Image.FillMethod.Horizontal;
        barImg.raycastTarget = false;
        bar.SetActive(false);
        view.stackBar = barImg;

        // Количество
        TextMeshProUGUI amountLabel = CreateLabel("Amount", slot.transform);
        RectTransform amountRect = (RectTransform)amountLabel.transform;
        amountRect.anchorMin = new Vector2(0f, 0f);
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(0.5f, 0f);
        amountRect.offsetMin = new Vector2(6f, 10f);
        amountRect.offsetMax = new Vector2(-8f, 34f);
        amountLabel.text = "";
        amountLabel.fontSize = 18f;
        amountLabel.fontStyle = FontStyles.Bold;
        amountLabel.color = textColor;
        amountLabel.alignment = TextAlignmentOptions.BottomRight;
        view.amountText = amountLabel;

        // Рамка выделения
        GameObject selection = CreateUIObject("Selection", slot.transform);
        Stretch((RectTransform)selection.transform);
        Image selectionImg = selection.AddComponent<Image>();
        selectionImg.sprite = UIShapes.RoundedRect(48, 10, 3);
        selectionImg.type = Image.Type.Sliced;
        selectionImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.9f);
        selectionImg.raycastTarget = false;
        selection.SetActive(false);
        view.selectionFrame = selection;

        slot.SetActive(false);
        return view;
    }

    void BuildTooltip(Transform canvasRoot, Sprite roundSprite)
    {
        GameObject tip = CreateUIObject("Tooltip", canvasRoot);
        RectTransform rect = (RectTransform)tip.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 48f);
        rect.sizeDelta = new Vector2(560f, 176f);

        Image bg = tip.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.04f, 0.045f, 0.06f, 0.96f);
        bg.raycastTarget = false;

        GameObject outline = CreateUIObject("Outline", tip.transform);
        Stretch((RectTransform)outline.transform);
        Image outlineImg = outline.AddComponent<Image>();
        outlineImg.sprite = UIShapes.RoundedRect(64, 14, 2);
        outlineImg.type = Image.Type.Sliced;
        outlineImg.color = new Color(1f, 1f, 1f, 0.10f);
        outlineImg.raycastTarget = false;

        tooltipName = CreateLabel("Name", tip.transform);
        RectTransform nameRect = (RectTransform)tooltipName.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.offsetMin = new Vector2(20f, -48f);
        nameRect.offsetMax = new Vector2(-20f, -12f);
        tooltipName.fontSize = 24f;
        tooltipName.fontStyle = FontStyles.Bold;

        tooltipCategory = CreateLabel("Category", tip.transform);
        RectTransform catRect = (RectTransform)tooltipCategory.transform;
        catRect.anchorMin = new Vector2(0f, 1f);
        catRect.anchorMax = new Vector2(1f, 1f);
        catRect.pivot = new Vector2(0.5f, 1f);
        catRect.offsetMin = new Vector2(20f, -74f);
        catRect.offsetMax = new Vector2(-20f, -48f);
        tooltipCategory.fontSize = 16f;

        tooltipDescription = CreateLabel("Description", tip.transform);
        RectTransform descRect = (RectTransform)tooltipDescription.transform;
        Stretch(descRect);
        descRect.offsetMin = new Vector2(20f, 14f);
        descRect.offsetMax = new Vector2(-20f, -78f);
        tooltipDescription.fontSize = 17f;
        tooltipDescription.color = new Color(textColor.r, textColor.g, textColor.b, 0.85f);

        tooltipRoot = tip;
        tip.SetActive(false);
    }

    void BuildPickupHint(Transform canvasRoot, Sprite roundSprite)
    {
        GameObject hint = CreateUIObject("PickupHint", canvasRoot);
        RectTransform rect = (RectTransform)hint.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -56f);
        rect.sizeDelta = new Vector2(460f, 46f);

        Image bg = hint.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.03f, 0.035f, 0.045f, 0.72f);
        bg.raycastTarget = false;

        pickupHintText = CreateLabel("Text", hint.transform);
        Stretch((RectTransform)pickupHintText.transform, 12f, 5f);
        pickupHintText.fontSize = 21f;
        pickupHintText.color = textColor;
        pickupHintText.alignment = TextAlignmentOptions.Center;

        pickupHintRoot = hint;
        hint.SetActive(false);
    }

    // =====================================================================
    static GameObject CreateUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>Растянуть RectTransform по родителю с отступами.</summary>
    static void Stretch(RectTransform rect, float padX = 0f, float padY = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padX, padY);
        rect.offsetMax = new Vector2(-padX, -padY);
    }

    /// <summary>Создать TMP-текст с нужным шрифтом (кириллица).</summary>
    TextMeshProUGUI CreateLabel(string name, Transform parent)
    {
        TextMeshProUGUI label = CreateUIObject(name, parent).AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) label.font = fontAsset;
        label.raycastTarget = false;
        label.richText = true;
        return label;
    }
}
