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

    [Header("Панель выбранного предмета")]
    public GameObject detailsRoot;
    public Image detailsIcon;
    public TextMeshProUGUI detailsName;
    public TextMeshProUGUI detailsCategory;
    public TextMeshProUGUI detailsDescription;
    public Button primaryActionButton;
    public TextMeshProUGUI primaryActionLabel;
    public Button dropActionButton;
    public TextMeshProUGUI dropActionLabel;

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
    public Vector2 cellSize = new Vector2(112f, 112f);
    public Vector2 cellSpacing = new Vector2(14f, 14f);
    [Tooltip("Ширина боковой панели с описанием выбранного предмета.")]
    public float detailsWidth = 420f;

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
    private int selectedViewIndex = -1;
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

        // Кнопка «Экипировать» должна пропадать сразу после смены оружия
        if (WeaponSlotManager.Instance != null)
            WeaponSlotManager.Instance.OnEquippedChanged += HandleEquippedChanged;
    }

    void OnDisable()
    {
        if (inventory == null) return;
        inventory.OnInventoryChanged -= Redraw;
        inventory.OnToggled -= HandleToggled;
        inventory.OnTargetChanged -= HandleTargetChanged;
        inventory.OnSorted -= HandleSorted;

        if (WeaponSlotManager.Instance != null)
            WeaponSlotManager.Instance.OnEquippedChanged -= HandleEquippedChanged;
    }

    void HandleEquippedChanged(EquippableWeapon weapon)
    {
        RefreshDetails();
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
        selectedViewIndex = -1;   // при смене вкладки выбор сбрасывается
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
        UpdateSelectionVisuals();
        RefreshDetails();

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
            selectedViewIndex = -1;
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
        if (real < 0) return;

        // ЛКМ выбирает предмет и показывает панель с действиями.
        // Повторный клик по уже выбранному — быстрое применение действия.
        if (selectedViewIndex == viewIndex)
        {
            InvokePrimaryAction();
            return;
        }

        selectedViewIndex = viewIndex;
        UpdateSelectionVisuals();
        RefreshDetails();
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
                    action = "\n<color=#8a8f99>Расходуется при перезарядке (R)</color>";
                    break;

                case ItemType.Key:
                    action = $"\n<color=#e0c86a>Отпирает: {item.keyId}</color>";
                    break;
            }

            string desc = string.IsNullOrEmpty(item.description) ? "" : item.description;
            tooltipDescription.text = desc + action +
                "\n<color=#5a5f69>ЛКМ — выбрать · ПКМ — выбросить 1</color>";
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
    // Выбор предмета и панель действий
    // =====================================================================
    void UpdateSelectionVisuals()
    {
        // Выбор с пустой ячейки снимаем: предмет мог быть израсходован
        if (selectedViewIndex >= 0)
        {
            int real = ResolveIndex(selectedViewIndex);
            InventorySystem.Slot slot = real >= 0 ? inventory.GetSlot(real) : null;
            if (slot == null || slot.IsEmpty) selectedViewIndex = -1;
        }

        for (int i = 0; i < slotViews.Count; i++)
            slotViews[i].SetSelected(i == selectedViewIndex);
    }

    /// <summary>Слот, который сейчас выбран (null — ничего не выбрано).</summary>
    InventorySystem.Slot GetSelectedSlot()
    {
        if (selectedViewIndex < 0) return null;
        int real = ResolveIndex(selectedViewIndex);
        return real >= 0 ? inventory.GetSlot(real) : null;
    }

    /// <summary>Перерисовать боковую панель под выбранный предмет.</summary>
    void RefreshDetails()
    {
        if (detailsRoot == null) return;

        InventorySystem.Slot slot = GetSelectedSlot();

        if (slot == null || slot.IsEmpty)
        {
            ShowEmptyDetails();
            return;
        }

        ItemData item = slot.item;
        detailsRoot.SetActive(true);

        if (detailsIcon != null)
        {
            bool hasIcon = item.icon != null;
            detailsIcon.enabled = true;
            detailsIcon.sprite = hasIcon ? item.icon : null;
            detailsIcon.color = hasIcon
                ? Color.white
                : new Color(item.RarityColor.r, item.RarityColor.g, item.RarityColor.b, 0.28f);
        }

        if (detailsName != null)
        {
            detailsName.text = slot.amount > 1
                ? $"{item.itemName}  <color=#8a8f99>x{slot.amount}</color>"
                : item.itemName;
            detailsName.color = item.RarityColor;
        }

        if (detailsCategory != null)
        {
            string catHex = ColorUtility.ToHtmlStringRGB(ItemData.GetCategoryColor(item.itemType));
            string rarHex = ColorUtility.ToHtmlStringRGB(item.RarityColor);
            detailsCategory.text = $"<color=#{catHex}>{item.CategoryName}</color>" +
                                   "<color=#4f545e>   ·   </color>" +
                                   $"<color=#{rarHex}>{GetRarityName(item.rarity)}</color>";
        }

        if (detailsDescription != null)
            detailsDescription.text = BuildDetailsText(item, slot);

        UpdateActionButtons(item);
    }

    string BuildDetailsText(ItemData item, InventorySystem.Slot slot)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(item.description))
            sb.Append(item.description).Append("\n\n");

        switch (item.itemType)
        {
            case ItemType.Consumable:
                sb.Append($"<color=#7fd694>Восстанавливает {item.useValue:0} HP</color>\n");
                break;
            case ItemType.Ammo:
                sb.Append($"<color=#8a8f99>Расходуется при перезарядке (R)</color>\n");
                break;
            case ItemType.Key:
                sb.Append($"<color=#e0c86a>Отпирает: {item.keyId}</color>\n");
                break;
            case ItemType.Weapon:
                if (item.IsEquippable)
                {
                    bool inHands = WeaponSlotManager.IsEquippedById(item.equipWeaponId);
                    sb.Append(inHands
                        ? "<color=#7fd694>В руках</color>\n"
                        : "<color=#8a8f99>Не экипировано</color>\n");
                }
                else
                {
                    sb.Append("<color=#c0704f>Не привязано к модели в сцене</color>\n");
                }
                break;
        }

        if (item.stackable && item.maxStack > 1)
            sb.Append($"<color=#5a5f69>В стаке: {slot.amount} / {item.maxStack}</color>");

        return sb.ToString();
    }

    void ShowEmptyDetails()
    {
        if (detailsRoot == null) return;
        detailsRoot.SetActive(true);

        if (detailsIcon != null) detailsIcon.enabled = false;
        if (detailsName != null)
        {
            detailsName.text = "<color=#4f545e>Ничего не выбрано</color>";
            detailsName.color = textColor;
        }
        if (detailsCategory != null) detailsCategory.text = "";
        if (detailsDescription != null)
            detailsDescription.text = "<color=#4f545e>Нажми на предмет, чтобы посмотреть\nописание и доступные действия.</color>";

        if (primaryActionButton != null) primaryActionButton.gameObject.SetActive(false);
        if (dropActionButton != null) dropActionButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// Настроить кнопки действий под тип предмета.
    /// Для оружия «Экипировать» пропадает, когда оно уже в руках.
    /// </summary>
    void UpdateActionButtons(ItemData item)
    {
        if (dropActionButton != null)
        {
            dropActionButton.gameObject.SetActive(true);
            if (dropActionLabel != null) dropActionLabel.text = "Выбросить";
        }

        if (primaryActionButton == null) return;

        string label = null;
        bool interactable = true;

        switch (item.itemType)
        {
            case ItemType.Weapon:
                if (item.IsEquippable)
                {
                    // Уже в руках — кнопка не нужна
                    if (WeaponSlotManager.IsEquippedById(item.equipWeaponId)) label = null;
                    else if (WeaponSlotManager.Instance != null &&
                             !WeaponSlotManager.Instance.Has(item.equipWeaponId))
                    {
                        label = "Модель не найдена";
                        interactable = false;
                    }
                    else label = "Экипировать";
                }
                break;

            case ItemType.Consumable:
                label = "Использовать";
                break;

            case ItemType.Ammo:
                // Магазины расходуются сами при перезарядке — кнопка не нужна
                label = null;
                break;

            case ItemType.Key:
                label = string.IsNullOrEmpty(item.keyId) ? null : "Активировать";
                break;
        }

        bool show = !string.IsNullOrEmpty(label);
        primaryActionButton.gameObject.SetActive(show);
        primaryActionButton.interactable = interactable;

        if (show && primaryActionLabel != null)
            primaryActionLabel.text = label;
    }

    /// <summary>Выполнить основное действие над выбранным предметом.</summary>
    public void InvokePrimaryAction()
    {
        InventorySystem.Slot slot = GetSelectedSlot();
        if (slot == null || slot.IsEmpty) return;

        int real = ResolveIndex(selectedViewIndex);
        if (real < 0) return;

        ItemData item = slot.item;

        // Оружие: экипировка вместо расхода предмета
        if (item.itemType == ItemType.Weapon)
        {
            if (!item.IsEquippable) return;
            if (WeaponSlotManager.IsEquippedById(item.equipWeaponId)) return;

            WeaponSlotManager.EquipById(item.equipWeaponId);
            RefreshDetails();
            return;
        }

        inventory.UseSlot(real);
        // Redraw вызовется по событию OnInventoryChanged, если предмет израсходован
    }

    /// <summary>Выбросить один выбранный предмет.</summary>
    public void InvokeDropAction()
    {
        int real = ResolveIndex(selectedViewIndex);
        if (real < 0) return;

        if (Input.GetKey(KeyCode.LeftShift)) inventory.DropSlot(real);
        else inventory.DropOne(real);
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
        // Панель = сетка слева + панель описания справа.
        int rows = Mathf.CeilToInt(inventory.MaxSlots / (float)columns);
        float gridW = columns * cellSize.x + (columns - 1) * cellSpacing.x;
        float gridH = rows * cellSize.y + (rows - 1) * cellSpacing.y;

        const float padding = 32f;      // отступ от краёв панели
        const float columnGap = 24f;    // между сеткой и описанием
        const float headerHeight = 74f; // заголовок + разделитель
        const float footerHeight = 46f; // подпись управления
        float tabsHeight = showCategoryTabs ? 54f : 0f;

        float width = padding * 2f + gridW + columnGap + detailsWidth;
        float height = padding * 2f + headerHeight + tabsHeight + gridH + footerHeight;

        // Не даём панели выйти за пределы экрана на низких разрешениях
        height = Mathf.Min(height, 1020f);

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

        // Правый край сетки в координатах панели
        float gridRightEdge = -(padding + detailsWidth + columnGap);

        // --- Заголовок ---
        titleText = CreateLabel("Title", panel.transform);
        RectTransform titleRect = (RectTransform)titleText.transform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.offsetMin = new Vector2(padding, -62f);
        titleRect.offsetMax = new Vector2(gridRightEdge, -20f);
        titleText.text = "ИНВЕНТАРЬ";
        titleText.fontSize = 30f;
        titleText.characterSpacing = 8f;
        titleText.color = textColor;
        titleText.alignment = TextAlignmentOptions.Left;

        // --- Счётчик занятых слотов ---
        capacityText = CreateLabel("Capacity", panel.transform);
        RectTransform capRect = (RectTransform)capacityText.transform;
        capRect.anchorMin = new Vector2(0f, 1f);
        capRect.anchorMax = new Vector2(1f, 1f);
        capRect.pivot = new Vector2(0.5f, 1f);
        capRect.offsetMin = new Vector2(padding, -62f);
        capRect.offsetMax = new Vector2(gridRightEdge, -20f);
        capacityText.fontSize = 25f;
        capacityText.alignment = TextAlignmentOptions.Right;

        // --- Разделитель под заголовком (акцентная линия) ---
        GameObject divider = CreateUIObject("Divider", panel.transform);
        RectTransform divRect = (RectTransform)divider.transform;
        divRect.anchorMin = new Vector2(0f, 1f);
        divRect.anchorMax = new Vector2(1f, 1f);
        divRect.pivot = new Vector2(0.5f, 1f);
        divRect.offsetMin = new Vector2(padding, -68f);
        divRect.offsetMax = new Vector2(gridRightEdge, -66f);
        Image divImg = divider.AddComponent<Image>();
        divImg.sprite = solid;
        divImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.35f);
        divImg.raycastTarget = false;

        float gridTopOffset = -(padding + headerHeight - 20f);

        // --- Вкладки категорий ---
        if (showCategoryTabs)
        {
            GameObject tabsRow = CreateUIObject("Tabs", panel.transform);
            RectTransform tabsRect = (RectTransform)tabsRow.transform;
            tabsRect.anchorMin = new Vector2(0f, 1f);
            tabsRect.anchorMax = new Vector2(1f, 1f);
            tabsRect.pivot = new Vector2(0.5f, 1f);
            tabsRect.offsetMin = new Vector2(padding, -128f);
            tabsRect.offsetMax = new Vector2(gridRightEdge, -80f);

            HorizontalLayoutGroup tabsLayout = tabsRow.AddComponent<HorizontalLayoutGroup>();
            tabsLayout.spacing = 7f;
            tabsLayout.childForceExpandWidth = true;
            tabsLayout.childForceExpandHeight = true;
            tabsLayout.childAlignment = TextAnchor.MiddleLeft;

            CreateTab(tabsRow.transform, null, accentColor, roundThin);
            foreach (ItemType type in ItemData.DisplayOrder)
                CreateTab(tabsRow.transform, type, ItemData.GetCategoryColor(type), roundThin);

            gridTopOffset = -138f;
            UpdateTabVisuals();
        }

        // --- Сетка ---
        GameObject grid = CreateUIObject("SlotsGrid", panel.transform);
        RectTransform gridRect = (RectTransform)grid.transform;
        gridRect.anchorMin = new Vector2(0f, 0f);
        gridRect.anchorMax = new Vector2(1f, 1f);
        gridRect.offsetMin = new Vector2(padding, footerHeight);
        gridRect.offsetMax = new Vector2(gridRightEdge, gridTopOffset);

        GridLayoutGroup layout = grid.AddComponent<GridLayoutGroup>();
        layout.cellSize = cellSize;
        layout.spacing = cellSpacing;
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = columns;
        layout.childAlignment = TextAnchor.UpperLeft;
        slotsContainer = grid.transform;

        // Ячейки анимируют свой anchoredPosition, поэтому раскладка должна
        // отработать один раз и больше не перетирать позиции.
        grid.AddComponent<GridLayoutFreezer>();

        // --- Панель описания выбранного предмета ---
        BuildDetailsPanel(panel.transform, roundThin, padding, detailsWidth,
                          footerHeight, padding + 20f);

        // --- Подпись управления ---
        TextMeshProUGUI hintLabel = CreateLabel("ControlsHint", panel.transform);
        RectTransform hintRect = (RectTransform)hintLabel.transform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(padding, 14f);
        hintRect.offsetMax = new Vector2(gridRightEdge, 40f);
        hintLabel.text = "<color=#8a8f99>ЛКМ</color> выбрать   " +
                         "<color=#8a8f99>ПКМ</color> выбросить   " +
                         "<color=#8a8f99>R</color> сортировать   " +
                         "<color=#8a8f99>Q/E</color> вкладки   " +
                         "<color=#8a8f99>Tab</color> закрыть";
        hintLabel.fontSize = 17f;
        hintLabel.color = new Color(textColor.r, textColor.g, textColor.b, 0.5f);
        hintLabel.alignment = TextAlignmentOptions.Left;

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

    /// <summary>
    /// Боковая панель: крупная иконка, название, категория, описание
    /// и кнопки действий («Экипировать» / «Использовать» / «Выбросить»).
    /// </summary>
    void BuildDetailsPanel(Transform panel, Sprite roundSprite,
                           float padding, float width, float bottom, float top)
    {
        GameObject root = CreateUIObject("Details", panel);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.offsetMin = new Vector2(-(padding + width), bottom);
        rect.offsetMax = new Vector2(-padding, -top);

        Image bg = root.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 1f, 1f, 0.035f);

        GameObject edge = CreateUIObject("Edge", root.transform);
        Stretch((RectTransform)edge.transform);
        Image edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        edgeImg.type = Image.Type.Sliced;
        edgeImg.color = new Color(1f, 1f, 1f, 0.07f);
        edgeImg.raycastTarget = false;

        const float inner = 22f;
        float iconSize = Mathf.Min(width - inner * 2f, 180f);

        // --- Иконка ---
        GameObject iconFrame = CreateUIObject("IconFrame", root.transform);
        RectTransform iconFrameRect = (RectTransform)iconFrame.transform;
        iconFrameRect.anchorMin = new Vector2(0.5f, 1f);
        iconFrameRect.anchorMax = new Vector2(0.5f, 1f);
        iconFrameRect.pivot = new Vector2(0.5f, 1f);
        iconFrameRect.anchoredPosition = new Vector2(0f, -inner);
        iconFrameRect.sizeDelta = new Vector2(iconSize, iconSize);

        Image iconBg = iconFrame.AddComponent<Image>();
        iconBg.sprite = roundSprite;
        iconBg.type = Image.Type.Sliced;
        iconBg.color = new Color(0f, 0f, 0f, 0.28f);
        iconBg.raycastTarget = false;

        detailsIcon = CreateUIObject("Icon", iconFrame.transform).AddComponent<Image>();
        Stretch((RectTransform)detailsIcon.transform, 14f, 14f);
        detailsIcon.preserveAspect = true;
        detailsIcon.raycastTarget = false;
        detailsIcon.enabled = false;

        float cursor = inner + iconSize + 18f;

        // --- Название ---
        detailsName = CreateLabel("Name", root.transform);
        RectTransform nameRect = (RectTransform)detailsName.transform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.offsetMin = new Vector2(inner, -(cursor + 62f));
        nameRect.offsetMax = new Vector2(-inner, -cursor);
        detailsName.fontSize = 25f;
        detailsName.fontStyle = FontStyles.Bold;
        detailsName.alignment = TextAlignmentOptions.Top;
        detailsName.enableWordWrapping = true;

        cursor += 66f;

        // --- Категория и редкость ---
        detailsCategory = CreateLabel("Category", root.transform);
        RectTransform catRect = (RectTransform)detailsCategory.transform;
        catRect.anchorMin = new Vector2(0f, 1f);
        catRect.anchorMax = new Vector2(1f, 1f);
        catRect.pivot = new Vector2(0.5f, 1f);
        catRect.offsetMin = new Vector2(inner, -(cursor + 26f));
        catRect.offsetMax = new Vector2(-inner, -cursor);
        detailsCategory.fontSize = 17f;
        detailsCategory.alignment = TextAlignmentOptions.Top;

        cursor += 34f;

        // --- Разделитель ---
        GameObject sep = CreateUIObject("Separator", root.transform);
        RectTransform sepRect = (RectTransform)sep.transform;
        sepRect.anchorMin = new Vector2(0f, 1f);
        sepRect.anchorMax = new Vector2(1f, 1f);
        sepRect.pivot = new Vector2(0.5f, 1f);
        sepRect.offsetMin = new Vector2(inner, -(cursor + 1.5f));
        sepRect.offsetMax = new Vector2(-inner, -cursor);
        Image sepImg = sep.AddComponent<Image>();
        sepImg.sprite = UIShapes.Solid();
        sepImg.color = new Color(1f, 1f, 1f, 0.09f);
        sepImg.raycastTarget = false;

        cursor += 14f;

        const float buttonHeight = 48f;
        const float buttonGap = 10f;
        float buttonsBlock = buttonHeight * 2f + buttonGap + inner;

        // --- Описание (занимает всё между категорией и кнопками) ---
        detailsDescription = CreateLabel("Description", root.transform);
        RectTransform descRect = (RectTransform)detailsDescription.transform;
        descRect.anchorMin = new Vector2(0f, 0f);
        descRect.anchorMax = new Vector2(1f, 1f);
        descRect.offsetMin = new Vector2(inner, buttonsBlock);
        descRect.offsetMax = new Vector2(-inner, -cursor);
        detailsDescription.fontSize = 18f;
        detailsDescription.lineSpacing = 6f;
        detailsDescription.alignment = TextAlignmentOptions.TopLeft;
        detailsDescription.enableWordWrapping = true;
        detailsDescription.color = new Color(textColor.r, textColor.g, textColor.b, 0.85f);

        // --- Кнопка основного действия ---
        primaryActionButton = BuildActionButton(
            root.transform, "PrimaryAction", roundSprite,
            inner, buttonHeight, inner + buttonHeight + buttonGap,
            new Color(accentColor.r * 0.28f, accentColor.g * 0.22f, accentColor.b * 0.12f, 1f),
            accentColor,
            out primaryActionLabel);
        primaryActionButton.onClick.AddListener(InvokePrimaryAction);

        // --- Кнопка «Выбросить» ---
        dropActionButton = BuildActionButton(
            root.transform, "DropAction", roundSprite,
            inner, buttonHeight, inner,
            new Color(0.16f, 0.10f, 0.10f, 1f),
            new Color(0.85f, 0.45f, 0.40f),
            out dropActionLabel);
        dropActionButton.onClick.AddListener(InvokeDropAction);

        detailsRoot = root;
    }

    Button BuildActionButton(Transform parent, string name, Sprite roundSprite,
                             float sideInset, float height, float bottomOffset,
                             Color fill, Color accent, out TextMeshProUGUI label)
    {
        GameObject obj = CreateUIObject(name, parent);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(sideInset, bottomOffset);
        rect.offsetMax = new Vector2(-sideInset, bottomOffset + height);

        Image bg = obj.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = fill;

        Button button = obj.AddComponent<Button>();
        button.targetGraphic = bg;

        // Штатный ColorTint даёт наведение и нажатие без своего скрипта
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        GameObject edge = CreateUIObject("Edge", obj.transform);
        Stretch((RectTransform)edge.transform);
        Image edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        edgeImg.type = Image.Type.Sliced;
        edgeImg.color = new Color(accent.r, accent.g, accent.b, 0.55f);
        edgeImg.raycastTarget = false;

        label = CreateLabel("Label", obj.transform);
        Stretch((RectTransform)label.transform, 10f, 2f);
        label.fontSize = 20f;
        label.fontStyle = FontStyles.Bold;
        label.color = accent;
        label.alignment = TextAlignmentOptions.Center;

        obj.SetActive(false);
        return button;
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
