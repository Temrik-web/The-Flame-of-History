using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Сеточный UI инвентаря. Перерисовывается только по событию OnInventoryChanged.
///
/// Два режима работы:
/// 1) Ручной — задай Inventory Panel, Slots Container и Slot Prefab в инспекторе.
/// 2) Автоматический (autoBuild = true) — весь UI (Canvas, панель, сетка, ячейки,
///    тултип и подсказка «E — подобрать») создаётся кодом при старте.
///    Ничего настраивать не нужно: просто повесь этот скрипт на игрока рядом с InventorySystem.
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

    [Header("Тултип")]
    public GameObject tooltipRoot;
    public TextMeshProUGUI tooltipName;
    public TextMeshProUGUI tooltipDescription;

    [Header("Подсказка подбора (HUD)")]
    public GameObject pickupHintRoot;
    public TextMeshProUGUI pickupHintText;

    [Header("Автосборка UI")]
    [Tooltip("Создать весь UI кодом при старте. Удобно, чтобы не собирать Canvas руками.")]
    public bool autoBuild = true;
    [Min(1)] public int columns = 5;
    public Vector2 cellSize = new Vector2(84f, 84f);
    public Vector2 cellSpacing = new Vector2(8f, 8f);
    public Color panelColor = new Color(0.06f, 0.06f, 0.08f, 0.92f);
    public Color textColor = Color.white;

    [Header("Шрифт")]
    [Tooltip("TMP-шрифт с поддержкой кириллицы. Если пусто — берётся Resources/InventoryFont SDF. " +
             "Стандартный LiberationSans SDF в проекте статический (только ASCII) и русский текст не покажет.")]
    public TMP_FontAsset fontAsset;

    private readonly List<InventorySlotUI> slotViews = new List<InventorySlotUI>();
    private int hoveredIndex = -1;
    private bool built;

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

        // Панель инвентаря переключает сам InventorySystem
        if (inventory.inventoryPanel == null && inventoryPanel != null)
            inventory.inventoryPanel = inventoryPanel;

        // Наш UI заменяет отладочный OnGUI
        if (inventoryPanel != null) inventory.drawDebugGUI = false;
    }

    void OnEnable()
    {
        if (inventory == null) return;
        inventory.OnInventoryChanged += Redraw;
        inventory.OnToggled += HandleToggled;
        inventory.OnTargetChanged += HandleTargetChanged;
    }

    void OnDisable()
    {
        if (inventory == null) return;
        inventory.OnInventoryChanged -= Redraw;
        inventory.OnToggled -= HandleToggled;
        inventory.OnTargetChanged -= HandleTargetChanged;
    }

    void Start()
    {
        EnsureSlotViews();
        Redraw();
        HideTooltip();
        HandleTargetChanged(inventory.CurrentTarget);
        if (inventoryPanel != null) inventoryPanel.SetActive(inventory.IsOpen);
    }

    // =====================================================================
    // Отрисовка
    // =====================================================================
    void EnsureSlotViews()
    {
        if (slotsContainer == null || slotPrefab == null) return;

        while (slotViews.Count < inventory.MaxSlots)
        {
            InventorySlotUI view = Instantiate(slotPrefab, slotsContainer);
            view.gameObject.SetActive(true);
            view.name = $"Slot_{slotViews.Count}";
            view.Init(this, slotViews.Count);
            slotViews.Add(view);
        }
    }

    public void Redraw()
    {
        if (inventory == null) return;
        EnsureSlotViews();

        for (int i = 0; i < slotViews.Count; i++)
            slotViews[i].SetSlot(inventory.GetSlot(i));

        if (titleText != null)
            titleText.text = $"Инвентарь  {inventory.SlotCount}/{inventory.MaxSlots}";

        if (hoveredIndex >= 0) ShowTooltip(hoveredIndex);
    }

    void HandleToggled(bool open)
    {
        if (inventoryPanel != null) inventoryPanel.SetActive(open);
        if (!open) HideTooltip();
        if (pickupHintRoot != null && open) pickupHintRoot.SetActive(false);
    }

    void HandleTargetChanged(Pickup target)
    {
        if (pickupHintRoot == null) return;

        bool show = target != null && !inventory.IsOpen;
        pickupHintRoot.SetActive(show);
        if (show && pickupHintText != null) pickupHintText.text = target.GetPrompt();
    }

    // =====================================================================
    // Обработчики от ячеек
    // =====================================================================
    public void OnSlotLeftClick(int index) => inventory.UseSlot(index);

    public void OnSlotRightClick(int index)
    {
        if (Input.GetKey(KeyCode.LeftShift)) inventory.DropSlot(index);
        else inventory.DropOne(index);
    }

    public void OnSlotHover(int index, bool entered)
    {
        if (entered)
        {
            hoveredIndex = index;
            ShowTooltip(index);
        }
        else if (hoveredIndex == index)
        {
            hoveredIndex = -1;
            HideTooltip();
        }
    }

    void ShowTooltip(int index)
    {
        if (tooltipRoot == null) return;

        InventorySystem.Slot slot = inventory.GetSlot(index);
        if (slot == null || slot.IsEmpty)
        {
            HideTooltip();
            return;
        }

        tooltipRoot.SetActive(true);
        if (tooltipName != null)
            tooltipName.text = slot.amount > 1
                ? $"{slot.item.itemName} x{slot.amount}"
                : slot.item.itemName;

        if (tooltipDescription != null)
        {
            string extra = "";
            switch (slot.item.itemType)
            {
                case ItemType.Consumable: extra = $"\n<color=#8f8>ЛКМ — восстановить {slot.item.useValue} HP</color>"; break;
                case ItemType.Ammo:       extra = "\n<color=#8f8>ЛКМ — пополнить магазины</color>"; break;
                case ItemType.Key:        extra = $"\n<color=#ff8>Ключ: {slot.item.keyId}</color>"; break;
            }
            tooltipDescription.text = slot.item.description + extra +
                                      "\n<color=#aaa>ПКМ — выбросить 1, Shift+ПКМ — всё</color>";
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

        // --- Canvas ---
        Canvas canvas = new GameObject("InventoryCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // --- Панель ---
        int rows = Mathf.CeilToInt(inventory.MaxSlots / (float)columns);
        float width = columns * cellSize.x + (columns - 1) * cellSpacing.x + 48f;
        float height = rows * cellSize.y + (rows - 1) * cellSpacing.y + 110f;

        GameObject panel = CreateUIObject("InventoryPanel", canvas.transform);
        RectTransform panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(width, height);

        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = panelColor;
        inventoryPanel = panel;

        // --- Заголовок ---
        titleText = CreateLabel("Title", panel.transform);
        RectTransform titleRect = (RectTransform)titleText.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(24f, -60f);
        titleRect.offsetMax = new Vector2(-24f, -14f);

        titleText.text = "Инвентарь";
        titleText.fontSize = 30f;
        titleText.color = textColor;
        titleText.alignment = TextAlignmentOptions.Left;

        // --- Сетка ---
        GameObject grid = CreateUIObject("SlotsGrid", panel.transform);
        RectTransform gridRect = (RectTransform)grid.transform;
        gridRect.anchorMin = new Vector2(0f, 0f);
        gridRect.anchorMax = new Vector2(1f, 1f);
        gridRect.offsetMin = new Vector2(24f, 40f);
        gridRect.offsetMax = new Vector2(-24f, -66f);

        GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
        layout.cellSize = cellSize;
        layout.spacing = cellSpacing;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        layout.childAlignment = TextAnchor.UpperCenter;
        slotsContainer = grid.transform;

        // --- Подпись управления ---
        TextMeshProUGUI hintLabel = CreateLabel("ControlsHint", panel.transform);
        RectTransform hintRect = (RectTransform)hintLabel.transform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(24f, 10f);
        hintRect.offsetMax = new Vector2(-24f, 34f);

        hintLabel.text = "ЛКМ — использовать | ПКМ — выбросить 1 | Shift+ПКМ — выбросить всё | Tab — закрыть";
        hintLabel.fontSize = 16f;
        hintLabel.color = new Color(textColor.r, textColor.g, textColor.b, 0.6f);
        hintLabel.alignment = TextAlignmentOptions.Center;

        // --- Префаб ячейки ---
        slotPrefab = BuildSlotPrefab(canvas.transform);

        // --- Тултип ---
        BuildTooltip(canvas.transform);

        // --- HUD-подсказка подбора ---
        BuildPickupHint(canvas.transform);

        panel.SetActive(false);
    }

    InventorySlotUI BuildSlotPrefab(Transform canvasRoot)
    {
        GameObject slot = CreateUIObject("SlotTemplate", canvasRoot);
        RectTransform rect = (RectTransform)slot.transform;
        rect.sizeDelta = cellSize;

        Image bg = slot.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.10f);

        InventorySlotUI view = slot.AddComponent<InventorySlotUI>();
        view.background = bg;

        // Иконка
        GameObject icon = CreateUIObject("Icon", slot.transform);
        RectTransform iconRect = (RectTransform)icon.transform;
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(8f, 8f);
        iconRect.offsetMax = new Vector2(-8f, -8f);
        Image iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.enabled = false;
        view.iconImage = iconImage;

        // Количество
        TextMeshProUGUI amountLabel = CreateLabel("Amount", slot.transform);
        RectTransform amountRect = (RectTransform)amountLabel.transform;
        amountRect.anchorMin = new Vector2(0f, 0f);
        amountRect.anchorMax = new Vector2(1f, 0f);
        amountRect.pivot = new Vector2(0.5f, 0f);
        amountRect.offsetMin = new Vector2(4f, 2f);
        amountRect.offsetMax = new Vector2(-6f, 26f);
        amountLabel.text = "";
        amountLabel.fontSize = 18f;
        amountLabel.color = textColor;
        amountLabel.alignment = TextAlignmentOptions.BottomRight;
        view.amountText = amountLabel;

        // Рамка выделения
        GameObject frame = CreateUIObject("Selection", slot.transform);
        RectTransform frameRect = (RectTransform)frame.transform;
        frameRect.anchorMin = Vector2.zero;
        frameRect.anchorMax = Vector2.one;
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;
        Image frameImage = frame.AddComponent<Image>();
        frameImage.color = new Color(1f, 0.85f, 0.35f, 0.35f);
        frameImage.raycastTarget = false;
        frame.SetActive(false);
        view.selectionFrame = frame;

        slot.SetActive(false);
        return view;
    }

    void BuildTooltip(Transform canvasRoot)
    {
        GameObject tip = CreateUIObject("Tooltip", canvasRoot);
        RectTransform rect = (RectTransform)tip.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 60f);
        rect.sizeDelta = new Vector2(520f, 150f);

        Image bg = tip.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);
        bg.raycastTarget = false;

        tooltipName = CreateLabel("Name", tip.transform);
        RectTransform nameRect = (RectTransform)tooltipName.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.offsetMin = new Vector2(16f, -46f);
        nameRect.offsetMax = new Vector2(-16f, -8f);
        tooltipName.fontSize = 24f;
        tooltipName.color = new Color(1f, 0.92f, 0.6f);

        tooltipDescription = CreateLabel("Description", tip.transform);
        RectTransform descRect = (RectTransform)tooltipDescription.transform;
        descRect.anchorMin = Vector2.zero;
        descRect.anchorMax = Vector2.one;
        descRect.offsetMin = new Vector2(16f, 12f);
        descRect.offsetMax = new Vector2(-16f, -50f);
        tooltipDescription.fontSize = 18f;
        tooltipDescription.color = textColor;

        tooltipRoot = tip;
        tip.SetActive(false);
    }

    void BuildPickupHint(Transform canvasRoot)
    {
        GameObject hint = CreateUIObject("PickupHint", canvasRoot);
        RectTransform rect = (RectTransform)hint.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -60f);
        rect.sizeDelta = new Vector2(600f, 44f);

        Image bg = hint.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);
        bg.raycastTarget = false;

        pickupHintText = CreateLabel("Text", hint.transform);
        RectTransform labelRect = (RectTransform)pickupHintText.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 4f);
        labelRect.offsetMax = new Vector2(-8f, -4f);
        pickupHintText.fontSize = 22f;
        pickupHintText.color = textColor;
        pickupHintText.alignment = TextAlignmentOptions.Center;

        pickupHintRoot = hint;
        hint.SetActive(false);
    }

    static GameObject CreateUIObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    /// <summary>Создать TMP-текст с нужным шрифтом (кириллица).</summary>
    TextMeshProUGUI CreateLabel(string name, Transform parent)
    {
        TextMeshProUGUI label = CreateUIObject(name, parent).AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) label.font = fontAsset;
        label.raycastTarget = false;
        return label;
    }
}
