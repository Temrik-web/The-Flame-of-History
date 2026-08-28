using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инвентарь игрока: наведение на предмет, подбор, хранение со стаками,
/// выбрасывание, использование, открытие/закрытие панели, сохранение.
/// Вешается на игрока (или на объект с камерой).
/// </summary>
[DisallowMultipleComponent]
public class InventorySystem : MonoBehaviour
{
    // ---------- Слот ----------
    [Serializable]
    public class Slot
    {
        public ItemData item;
        public int amount;

        public Slot(ItemData item, int amount)
        {
            this.item = item;
            this.amount = amount;
        }

        public bool IsEmpty => item == null || amount <= 0;
        public int FreeSpace => item == null ? 0 : Mathf.Max(0, item.maxStack - amount);
    }

    // ---------- Singleton (удобно для UI и других скриптов) ----------
    public static InventorySystem Instance { get; private set; }

    // ---------- Ссылки ----------
    [Header("Ссылки")]
    [Tooltip("Если пусто — возьмётся Camera.main.")]
    public Camera playerCamera;
    [Tooltip("UI-панель инвентаря. Можно оставить пустой — тогда работает только логика.")]
    public GameObject inventoryPanel;
    public AudioSource audioSource;

    // ---------- Подбор ----------
    [Header("Подбор")]
    public float pickupRange = 3f;
    [Tooltip("По каким слоям искать предметы.")]
    public LayerMask pickupMask = ~0;
    public KeyCode pickupKey = KeyCode.E;
    [Tooltip("Радиус SphereCast. 0 — обычный тонкий луч (сложнее прицелиться).")]
    [Range(0f, 0.5f)] public float pickupCastRadius = 0.15f;

    [Tooltip("Радиус вокруг игрока, в котором предмет считается доступным даже без " +
             "попадания луча. Лечит случай «стою вплотную и подсказка исчезла»: " +
             "SphereCast не видит коллайдер, внутри которого начинается сфера.")]
    [Range(0f, 3f)] public float nearPickupRadius = 1.2f;

    [Tooltip("Максимальный угол от центра экрана, при котором предмет считается " +
             "тем, на который смотрит игрок. Больше — легче навести, но можно " +
             "случайно подобрать предмет сбоку.")]
    [Range(5f, 90f)] public float maxPickupAngle = 35f;

    [Tooltip("Проверять, не закрыт ли предмет стеной. Выключи, если предметы " +
             "лежат внутри сложной геометрии и подсказка мерцает.")]
    public bool requireLineOfSight = true;

    [Tooltip("Слои, которые могут заслонять предмет. Убери отсюда мелкий декор, " +
             "чтобы трава и мусор не перекрывали подсказку.")]
    public LayerMask occlusionMask = ~0;

    [Tooltip("Автоподбор при касании триггера, без нажатия клавиши.")]
    public bool autoPickupOnTouch = false;

    // ---------- Инвентарь ----------
    [Header("Инвентарь")]
    public KeyCode toggleKey = KeyCode.Tab;
    [Min(1)] public int maxSlots = 20;
    [Tooltip("Использовать предмет по цифрам 1..9.")]
    public bool useHotkeys = true;
    [Tooltip("Клавиша сортировки по категориям.")]
    public KeyCode sortKey = KeyCode.R;
    [Tooltip("Автоматически сортировать после каждого подбора.")]
    public bool autoSortOnPickup = false;

    // ---------- Выбрасывание ----------
    [Header("Выбрасывание")]
    public KeyCode dropKey = KeyCode.G;
    [Tooltip("Универсальный префаб с компонентом Pickup для дропа предметов без своего worldPrefab.")]
    public GameObject genericPickupPrefab;
    public float dropForwardOffset = 1.2f;
    public float dropUpOffset = 0.4f;
    public float dropThrowForce = 2.5f;

    // ---------- Курсор / пауза ----------
    [Header("Поведение при открытии")]
    public bool manageCursor = true;
    [Tooltip("Ставить Time.timeScale = 0, пока инвентарь открыт.")]
    public bool pauseGameWhenOpen = false;

    // ---------- Отладочный HUD ----------
    [Header("Отладочный HUD (OnGUI)")]
    [Tooltip("Рисовать подсказку и список через OnGUI. Выключи, когда сделаешь нормальный UI.")]
    public bool drawDebugGUI = true;

    [Header("Всплывающие подписи")]
    [Tooltip("Показывать «+2 Аптечка» в мире при подборе.")]
    public bool showFloatingText = true;

    // ---------- Сохранение ----------
    [Header("Сохранение")]
    public bool autoSaveOnQuit = true;
    public bool autoLoadOnStart = false;
    public string saveKey = "inventory_v1";

    // ---------- Состояние ----------
    public List<Slot> slots = new List<Slot>();

    private bool isOpen;
    private Pickup currentTarget;
    private float lastSortTime = -1f;

    /// <summary>Открыт ли инвентарь. Другие скрипты читают это, чтобы блокировать ввод.</summary>
    public bool IsOpen => isOpen;

    /// <summary>Предмет, на который сейчас смотрит игрок (может быть null).</summary>
    public Pickup CurrentTarget => currentTarget;

    public int SlotCount => slots.Count;
    public int MaxSlots => maxSlots;

    // ---------- События ----------
    /// <summary>Инвентарь изменился (добавили/убрали/выбросили). Для перерисовки UI.</summary>
    public event Action OnInventoryChanged;
    /// <summary>Инвентарь открыли/закрыли. Параметр — новое состояние.</summary>
    public event Action<bool> OnToggled;
    /// <summary>Цель наведения изменилась (может быть null).</summary>
    public event Action<Pickup> OnTargetChanged;
    /// <summary>Предмет подобран: (предмет, количество).</summary>
    public event Action<ItemData, int> OnItemPickedUp;
    /// <summary>Инвентарь отсортирован — для анимации перестроения UI.</summary>
    public event Action OnSorted;

    // =====================================================================
    // Жизненный цикл
    // =====================================================================
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[Inventory] На сцене уже есть InventorySystem ({Instance.name}). " +
                             $"Компонент на {name} отключён.");
            enabled = false;
            return;
        }
        Instance = this;

        if (playerCamera == null) playerCamera = Camera.main;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (playerCamera == null)
            Debug.LogWarning("[Inventory] Камера не найдена — наведение на предметы работать не будет.");

        if (inventoryPanel != null) inventoryPanel.SetActive(false);
        isOpen = false;

        if (autoLoadOnStart) Load();

        OnInventoryChanged?.Invoke();
    }

    void OnApplicationQuit()
    {
        if (autoSaveOnQuit) Save();
    }

    void Update()
    {
        // Во время диалога инвентарь не мешает
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
        {
            SetTarget(null);
            return;
        }

        if (Input.GetKeyDown(toggleKey))
            ToggleInventory();

        if (isOpen)
        {
            SetTarget(null);

            // Сортировка работает только при открытом инвентаре —
            // иначе R конфликтовал бы с перезарядкой оружия.
            if (Input.GetKeyDown(sortKey)) SortAndStack();

            if (useHotkeys) HandleHotkeys();
            return;
        }

        DetectPickup();

        if (currentTarget != null && Input.GetKeyDown(pickupKey))
            TryPickUp(currentTarget);

        if (useHotkeys) HandleHotkeys();
    }

    /// <summary>
    /// Цифры 1..9 — быстрый доступ. Номер берётся из ItemData.hotbarSlot,
    /// а не из позиции в списке: слот предмета не должен зависеть
    /// от сортировки и порядка подбора.
    /// </summary>
    void HandleHotkeys()
    {
        for (int digit = 1; digit <= 9; digit++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + (digit - 1))) continue;

            int index = FindSlotByHotbar(digit);
            if (index < 0)
            {
                Debug.Log($"[Inventory] В слоте {digit} ничего нет.");
                return;
            }

            if (Input.GetKey(dropKey)) DropSlot(index);
            else UseSlot(index);
            return;
        }
    }

    /// <summary>Индекс первого слота с предметом, назначенным на данную цифру.</summary>
    public int FindSlotByHotbar(int digit)
    {
        if (digit < 1 || digit > 9) return -1;

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty) continue;
            if (slots[i].item.hotbarSlot == digit) return i;
        }
        return -1;
    }

    /// <summary>Предмет, назначенный на цифру (null — ничего).</summary>
    public ItemData GetHotbarItem(int digit)
    {
        int index = FindSlotByHotbar(digit);
        return index >= 0 ? slots[index].item : null;
    }

    // =====================================================================
    // Наведение
    // =====================================================================
    /// <summary>
    /// Найти предмет под прицелом.
    ///
    /// Почему не один SphereCast: он не находит коллайдер, внутри которого уже
    /// находится начало сферы. Стоя вплотную к предмету игрок оказывался внутри
    /// зоны каста, и подсказка пропадала — ровно в тот момент, когда предмет
    /// занимает пол экрана. Плюс каст останавливался на первом же коллайдере,
    /// поэтому предмет, лежащий на столе, перекрывался столешницей.
    ///
    /// Теперь собираются все кандидаты (каст + сфера вокруг игрока для вплотную),
    /// а из них выбирается тот, что ближе к центру экрана и реально виден.
    /// </summary>
    void DetectPickup()
    {
        if (playerCamera == null)
        {
            SetTarget(null);
            return;
        }

        Transform cam = playerCamera.transform;
        Vector3 origin = cam.position;
        Vector3 dir = cam.forward;

        Pickup best = null;
        float bestScore = float.MaxValue;

        // 1) Всё, что задел луч прицела. SphereCastAll вместо SphereCast:
        //    нужен весь список, а не только первое попадание.
        float radius = Mathf.Max(0.01f, pickupCastRadius);
        RaycastHit[] hits = Physics.SphereCastAll(origin, radius, dir, pickupRange,
                                                  pickupMask, QueryTriggerInteraction.Collide);

        foreach (RaycastHit hit in hits)
            Consider(hit.collider, cam, ref best, ref bestScore);

        // 2) Предметы вплотную: их каст не видит, потому что сфера уже внутри них
        Collider[] near = Physics.OverlapSphere(origin, nearPickupRadius, pickupMask,
                                                QueryTriggerInteraction.Collide);

        foreach (Collider col in near)
            Consider(col, cam, ref best, ref bestScore);

        SetTarget(best);
    }

    /// <summary>
    /// Проверить кандидата и запомнить, если он лучше текущего.
    /// Оценка — угол от центра экрана: предмет прямо под прицелом всегда
    /// выигрывает у того, что задет краем сферы.
    /// </summary>
    void Consider(Collider col, Transform cam, ref Pickup best, ref float bestScore)
    {
        if (col == null) return;

        Pickup pickup = col.GetComponentInParent<Pickup>();
        if (pickup == null || pickup.item == null) return;

        Vector3 point = col.bounds.center;
        Vector3 toTarget = point - cam.position;
        float distance = toTarget.magnitude;

        if (distance > pickupRange) return;

        // Угол от центра экрана в градусах. Вплотную угол считать бессмысленно:
        // предмет занимает полкадра, поэтому такие цели получают приоритет.
        float angle = distance < nearPickupRadius
            ? 0f
            : Vector3.Angle(cam.forward, toTarget.normalized);

        if (angle > maxPickupAngle) return;

        // Оценка: сначала угол, при равном угле — что ближе
        float score = angle * 10f + distance;
        if (score >= bestScore) return;

        if (requireLineOfSight && !HasLineOfSight(cam.position, pickup, col)) return;

        best = pickup;
        bestScore = score;
    }

    /// <summary>
    /// Виден ли предмет: не закрыт ли он стеной. Собственные коллайдеры предмета
    /// и коллайдеры игрока преградой не считаются.
    /// </summary>
    bool HasLineOfSight(Vector3 eye, Pickup pickup, Collider target)
    {
        Vector3 point = target.bounds.center;
        Vector3 dir = point - eye;
        float distance = dir.magnitude;

        if (distance < 0.05f) return true;

        RaycastHit[] hits = Physics.RaycastAll(eye, dir.normalized, distance,
                                              occlusionMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Transform pickupRoot = pickup.transform;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider == target) return true;
            if (hit.collider.transform.IsChildOf(pickupRoot)) return true;

            // Сам игрок и его оружие в руках не заслоняют предмет
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (playerCamera != null && hit.collider.transform.IsChildOf(playerCamera.transform)) continue;

            return false;
        }

        return true;
    }

    void SetTarget(Pickup next)
    {
        if (currentTarget == next) return;

        if (currentTarget != null) currentTarget.SetHighlight(false);
        currentTarget = next;
        if (currentTarget != null) currentTarget.SetHighlight(true);

        OnTargetChanged?.Invoke(currentTarget);
    }

    // Автоподбор при касании
    void OnTriggerEnter(Collider other)
    {
        if (!autoPickupOnTouch) return;
        Pickup p = other.GetComponentInParent<Pickup>();
        if (p != null) TryPickUp(p);
    }

    // =====================================================================
    // Подбор
    // =====================================================================
    public void TryPickUp(Pickup pickup)
    {
        if (pickup == null) return;

        if (pickup.item == null)
        {
            Debug.LogWarning($"[Inventory] У {pickup.name} не задан ItemData — подбирать нечего.");
            return;
        }

        int taken = AddItem(pickup.item, pickup.amount);

        if (taken <= 0)
        {
            Debug.Log("[Inventory] Инвентарь полон.");
            return;
        }

        PlayClip(pickup.item.pickupSound);
        Debug.Log($"[Inventory] Подобрано: {pickup.item.itemName} x{taken}");
        OnItemPickedUp?.Invoke(pickup.item, taken);

        if (showFloatingText)
        {
            string label = taken > 1
                ? $"+{taken}  {pickup.item.itemName}"
                : $"+ {pickup.item.itemName}";
            FloatingText.Show(label, pickup.transform.position + Vector3.up * 0.35f,
                              pickup.item.RarityColor);
        }

        if (pickup == currentTarget && taken >= pickup.amount) SetTarget(null);
        pickup.OnPickedUp(taken);

        if (autoSortOnPickup) SortAndStack();
    }

    /// <summary>
    /// Добавить предмет. Возвращает СКОЛЬКО реально влезло (0 — не влезло ничего).
    /// </summary>
    public int AddItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;

        int remaining = amount;

        // 1) Досыпаем в существующие стаки
        if (item.stackable)
        {
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                Slot slot = slots[i];
                if (slot.item != item || slot.FreeSpace <= 0) continue;

                int toAdd = Mathf.Min(slot.FreeSpace, remaining);
                slot.amount += toAdd;
                remaining -= toAdd;
            }
        }

        // 2) Создаём новые слоты
        while (remaining > 0 && slots.Count < maxSlots)
        {
            int perSlot = item.stackable ? Mathf.Min(item.maxStack, remaining) : 1;
            slots.Add(new Slot(item, perSlot));
            remaining -= perSlot;
        }

        int added = amount - remaining;
        if (added > 0) OnInventoryChanged?.Invoke();
        return added;
    }

    /// <summary>Совместимость: true — влезло всё целиком.</summary>
    public bool AddItemFull(ItemData item, int amount) => AddItem(item, amount) == amount;

    // =====================================================================
    // Удаление / использование / выбрасывание
    // =====================================================================
    /// <summary>Убрать количество предмета. true — убрали всё запрошенное.</summary>
    public bool RemoveItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;

        int remaining = amount;
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (slots[i].item != item) continue;

            int taken = Mathf.Min(slots[i].amount, remaining);
            slots[i].amount -= taken;
            remaining -= taken;
            if (slots[i].amount <= 0) slots.RemoveAt(i);
        }

        if (remaining < amount) OnInventoryChanged?.Invoke();
        return remaining <= 0;
    }

    /// <summary>Использовать предмет из слота.</summary>
    public void UseSlot(int index)
    {
        if (!IsValidIndex(index)) return;

        Slot slot = slots[index];
        ItemData item = slot.item;
        if (item == null) return;

        if (!item.Use(gameObject)) return;

        PlayClip(item.useSound);

        if (item.consumeOnUse)
        {
            slot.amount--;
            if (slot.amount <= 0) slots.RemoveAt(index);
        }

        OnInventoryChanged?.Invoke();
        Debug.Log($"[Inventory] Использовано: {item.itemName}");
    }

    /// <summary>Выбросить один предмет из слота в мир.</summary>
    public void DropOne(int index) => Drop(index, 1);

    /// <summary>Выбросить весь слот в мир.</summary>
    public void DropSlot(int index)
    {
        if (!IsValidIndex(index)) return;
        Drop(index, slots[index].amount);
    }

    void Drop(int index, int count)
    {
        if (!IsValidIndex(index)) return;

        Slot slot = slots[index];
        ItemData item = slot.item;
        count = Mathf.Clamp(count, 1, slot.amount);

        SpawnInWorld(item, count);

        slot.amount -= count;
        if (slot.amount <= 0) slots.RemoveAt(index);

        OnInventoryChanged?.Invoke();
        Debug.Log($"[Inventory] Выброшено: {item.itemName} x{count}");
    }

    void SpawnInWorld(ItemData item, int count)
    {
        GameObject prefab = item.worldPrefab != null ? item.worldPrefab : genericPickupPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[Inventory] Нет префаба для дропа {item.itemName}. " +
                             "Задай Generic Pickup Prefab или World Prefab в ItemData.");
            return;
        }

        Transform origin = playerCamera != null ? playerCamera.transform : transform;
        Vector3 pos = origin.position + origin.forward * dropForwardOffset + Vector3.up * dropUpOffset;

        GameObject obj = Instantiate(prefab, pos, Quaternion.LookRotation(origin.forward));

        Pickup p = obj.GetComponent<Pickup>();
        if (p == null) p = obj.GetComponentInChildren<Pickup>();
        if (p != null)
        {
            p.item = item;
            p.amount = count;
            p.promptText = "";
        }

        Rigidbody rb = obj.GetComponent<Rigidbody>();
        if (rb != null && dropThrowForce > 0f)
            rb.AddForce(origin.forward * dropThrowForce, ForceMode.Impulse);
    }

    // =====================================================================
    // Запросы
    // =====================================================================
    /// <summary>Влезет ли столько предметов без фактического добавления.</summary>
    public bool HasSpaceFor(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return false;

        int remaining = amount;

        if (item.stackable)
            foreach (Slot s in slots)
                if (s.item == item) remaining -= s.FreeSpace;

        if (remaining <= 0) return true;

        int freeSlots = maxSlots - slots.Count;
        int perSlot = item.stackable ? item.maxStack : 1;
        return freeSlots * perSlot >= remaining;
    }

    /// <summary>Сколько всего таких предметов в инвентаре.</summary>
    public int CountItem(ItemData item)
    {
        if (item == null) return 0;
        int total = 0;
        foreach (Slot s in slots)
            if (s.item == item) total += s.amount;
        return total;
    }

    /// <summary>Сколько предметов с данным id (удобно для квестов и дверей).</summary>
    public int CountItemById(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        int total = 0;
        foreach (Slot s in slots)
            if (s.item != null && s.item.Id == id) total += s.amount;
        return total;
    }

    public bool HasItem(ItemData item, int amount = 1) => CountItem(item) >= amount;
    public bool HasKey(string keyId) => CountItemByKeyId(keyId) > 0;

    /// <summary>
    /// Есть ли в инвентаре предмет, экипирующий оружие с данным id.
    /// Нужно WeaponSlotManager, чтобы не давать переключиться на неподобранное оружие.
    /// </summary>
    public bool HasWeaponItem(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return false;

        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.equipWeaponId == weaponId) return true;

        return false;
    }

    /// <summary>
    /// Ассет предмета, экипирующего оружие с данным id (null — такого нет).
    /// Нужен расходуемому снаряжению: граната после броска должна пропасть
    /// из сумки, а сама она знает только свой weaponId.
    /// </summary>
    public ItemData GetWeaponItem(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;

        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.equipWeaponId == weaponId) return s.item;

        return null;
    }

    /// <summary>Сколько предметов, экипирующих оружие с данным id, лежит в сумке.</summary>
    public int CountWeaponItem(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return 0;

        int total = 0;
        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.equipWeaponId == weaponId) total += s.amount;

        return total;
    }

    public int CountItemByKeyId(string keyId)
    {
        if (string.IsNullOrEmpty(keyId)) return 0;
        int total = 0;
        foreach (Slot s in slots)
            if (s.item != null && s.item.itemType == ItemType.Key && s.item.keyId == keyId)
                total += s.amount;
        return total;
    }

    public Slot GetSlot(int index) => IsValidIndex(index) ? slots[index] : null;

    bool IsValidIndex(int index) => index >= 0 && index < slots.Count;

    /// <summary>Поменять два слота местами (для drag&drop в UI).</summary>
    public void SwapSlots(int a, int b)
    {
        if (!IsValidIndex(a) || !IsValidIndex(b) || a == b) return;
        Slot tmp = slots[a];
        slots[a] = slots[b];
        slots[b] = tmp;
        OnInventoryChanged?.Invoke();
    }

    /// <summary>Собрать одинаковые предметы в минимум стаков и разложить по категориям.</summary>
    public void SortAndStack()
    {
        var merged = new List<Slot>();

        foreach (Slot s in slots)
        {
            if (s.IsEmpty) continue;

            int remaining = s.amount;
            if (s.item.stackable)
            {
                foreach (Slot m in merged)
                {
                    if (m.item != s.item || m.FreeSpace <= 0) continue;
                    int move = Mathf.Min(m.FreeSpace, remaining);
                    m.amount += move;
                    remaining -= move;
                    if (remaining <= 0) break;
                }
            }
            while (remaining > 0)
            {
                int perSlot = s.item.stackable ? Mathf.Min(s.item.maxStack, remaining) : 1;
                merged.Add(new Slot(s.item, perSlot));
                remaining -= perSlot;
            }
        }

        merged.Sort(CompareSlots);

        slots = merged;
        lastSortTime = Time.unscaledTime;
        OnInventoryChanged?.Invoke();
        OnSorted?.Invoke();
        Debug.Log("[Inventory] Отсортировано по категориям.");
    }

    /// <summary>
    /// Порядок: категория (оружие → патроны → медикаменты → ключи → прочее),
    /// затем редкость (сначала ценное), затем ручной sortOrder, затем имя,
    /// затем крупные стаки выше.
    /// </summary>
    static int CompareSlots(Slot a, Slot b)
    {
        int byCategory = a.item.CategoryOrder.CompareTo(b.item.CategoryOrder);
        if (byCategory != 0) return byCategory;

        int byRarity = ((int)b.item.rarity).CompareTo((int)a.item.rarity);
        if (byRarity != 0) return byRarity;

        int byManual = a.item.sortOrder.CompareTo(b.item.sortOrder);
        if (byManual != 0) return byManual;

        int byName = string.Compare(a.item.itemName, b.item.itemName, StringComparison.CurrentCulture);
        if (byName != 0) return byName;

        return b.amount.CompareTo(a.amount);
    }

    /// <summary>Категории, реально присутствующие в инвентаре, в порядке отображения.</summary>
    public List<ItemType> GetPresentCategories()
    {
        var result = new List<ItemType>();
        foreach (ItemType type in ItemData.DisplayOrder)
        {
            foreach (Slot s in slots)
            {
                if (s.IsEmpty || s.item.itemType != type) continue;
                result.Add(type);
                break;
            }
        }
        return result;
    }

    /// <summary>Индексы слотов данной категории (для фильтра по вкладкам).</summary>
    public List<int> GetSlotIndicesOfCategory(ItemType type)
    {
        var result = new List<int>();
        for (int i = 0; i < slots.Count; i++)
            if (!slots[i].IsEmpty && slots[i].item.itemType == type) result.Add(i);
        return result;
    }

    /// <summary>Сколько слотов занято предметами данной категории.</summary>
    public int CountCategory(ItemType type)
    {
        int total = 0;
        foreach (Slot s in slots)
            if (!s.IsEmpty && s.item.itemType == type) total += s.amount;
        return total;
    }

    public void Clear()
    {
        slots.Clear();
        OnInventoryChanged?.Invoke();
    }

    // =====================================================================
    // Открытие / закрытие
    // =====================================================================
    public void ToggleInventory() => SetOpen(!isOpen);

    public void SetOpen(bool open)
    {
        if (isOpen == open) return;
        isOpen = open;

        if (inventoryPanel != null) inventoryPanel.SetActive(isOpen);

        if (manageCursor)
        {
            Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isOpen;
        }

        if (pauseGameWhenOpen)
            Time.timeScale = isOpen ? 0f : 1f;

        if (isOpen) SetTarget(null);

        OnToggled?.Invoke(isOpen);
        OnInventoryChanged?.Invoke();
    }

    void OnDisable()
    {
        // Не оставляем игру на паузе, если компонент выключили при открытом инвентаре
        if (isOpen && pauseGameWhenOpen) Time.timeScale = 1f;
    }

    // =====================================================================
    // Сохранение / загрузка
    // =====================================================================
    [Serializable]
    private class SaveEntry
    {
        public string id;
        public int amount;
    }

    [Serializable]
    private class SaveData
    {
        public List<SaveEntry> entries = new List<SaveEntry>();
    }

    public void Save()
    {
        var data = new SaveData();
        foreach (Slot s in slots)
        {
            if (s.IsEmpty) continue;
            data.entries.Add(new SaveEntry { id = s.item.Id, amount = s.amount });
        }

        PlayerPrefs.SetString(saveKey, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
        Debug.Log($"[Inventory] Сохранено слотов: {data.entries.Count}");
    }

    public void Load()
    {
        if (!PlayerPrefs.HasKey(saveKey))
        {
            Debug.Log("[Inventory] Сохранения нет.");
            return;
        }

        ItemDatabase db = ItemDatabase.Instance;
        if (db == null)
        {
            Debug.LogWarning("[Inventory] ItemDatabase не найден в Resources — загрузка невозможна. " +
                             "Создай ассет Inventory/Item Database и положи его в Assets/Resources/ItemDatabase.asset");
            return;
        }

        SaveData data = JsonUtility.FromJson<SaveData>(PlayerPrefs.GetString(saveKey));
        if (data == null) return;

        slots.Clear();
        foreach (SaveEntry e in data.entries)
        {
            ItemData item = db.GetById(e.id);
            if (item == null)
            {
                Debug.LogWarning($"[Inventory] Предмет с id '{e.id}' не найден в базе — пропущен.");
                continue;
            }
            AddItem(item, e.amount);
        }

        OnInventoryChanged?.Invoke();
        Debug.Log($"[Inventory] Загружено слотов: {slots.Count}");
    }

    public void DeleteSave() => PlayerPrefs.DeleteKey(saveKey);

    // =====================================================================
    // Прочее
    // =====================================================================
    void PlayClip(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource != null) audioSource.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, transform.position);
    }

    void OnGUI()
    {
        if (!drawDebugGUI) return;

        // Подсказка по центру — только когда инвентарь закрыт
        if (!isOpen && currentTarget != null)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.5f + 30f, 400f, 30f),
                      currentTarget.GetPrompt(), style);
            return;
        }

        // Список содержимого — когда открыт и нет настоящего UI
        if (isOpen && inventoryPanel == null)
        {
            float w = 340f;
            float h = 40f + slots.Count * 22f;
            GUI.Box(new Rect(20f, 90f, w, h), $"Инвентарь ({slots.Count}/{maxSlots})");

            for (int i = 0; i < slots.Count; i++)
            {
                Slot s = slots[i];
                string line = $"{i + 1}. {s.item.itemName} x{s.amount}";
                GUI.Label(new Rect(35f, 115f + i * 22f, w - 30f, 22f), line);
            }

            GUI.Label(new Rect(20f, 90f + h + 4f, 600f, 22f),
                      "1..9 — использовать | G+цифра — выбросить");
        }
    }

    void OnDrawGizmosSelected()
    {
        Camera cam = playerCamera != null ? playerCamera : Camera.main;
        if (cam == null) return;

        Vector3 origin = cam.transform.position;
        Vector3 end = origin + cam.transform.forward * pickupRange;

        // Луч прицела
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, end);
        if (pickupCastRadius > 0f) Gizmos.DrawWireSphere(end, pickupCastRadius);

        // Зона «вплотную»: здесь предмет берётся без попадания луча
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(origin, nearPickupRadius);

        // Конус допустимого угла наведения
        Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.4f);
        float rad = maxPickupAngle * Mathf.Deg2Rad;
        float coneRadius = Mathf.Tan(rad) * pickupRange;

        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            Vector3 offset = cam.transform.right * Mathf.Cos(a) * coneRadius
                             + cam.transform.up * Mathf.Sin(a) * coneRadius;
            Gizmos.DrawLine(origin, end + offset);
        }
    }
}
