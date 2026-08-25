using UnityEngine;

/// <summary>
/// Связывает запас магазинов оружия с инвентарём.
///
/// Инвентарь — единственный источник правды: сколько предметов-магазинов
/// лежит в сумке, столько и покажет Wep.spareMagazines. Благодаря этому
/// счётчики не расходятся, даже если игрок подобрал, выбросил или
/// израсходовал патроны.
///
/// Как работает:
///   - Подобрал магазин  -> инвентарь изменился -> spareMagazines пересчитан.
///   - Нажал R           -> Wep потратил магазин -> из инвентаря убран 1 предмет.
///   - Выбросил магазин  -> spareMagazines уменьшился.
///
/// Вешается на игрока рядом с InventorySystem.
/// </summary>
[DisallowMultipleComponent]
public class WeaponAmmoLink : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Если пусто — берётся InventorySystem.Instance.")]
    public InventorySystem inventory;

    [Tooltip("Оружие, чей запас магазинов синхронизируется. " +
             "Пусто — найдётся первый Wep в сцене (включая выключенные объекты).")]
    public Wep weapon;

    [Header("Предмет-магазин")]
    [Tooltip("ItemData, который считается одним запасным магазином. " +
             "Например Item_Ammo762 (Диск 7.62).")]
    public ItemData magazineItem;

    [Tooltip("Если ассет не назначен, магазин будет найден по этому id.")]
    public string magazineItemId = "ammo_762";

    [Header("Поведение")]
    [Tooltip("Сколько магазинов даёт один предмет. Обычно 1.")]
    [Min(1)] public int magazinesPerItem = 1;

    // Защита от рекурсии: RemoveItem вызовет OnInventoryChanged,
    // который иначе снова полез бы пересчитывать запас
    private bool isSyncing;

    // =====================================================================
    void Awake()
    {
        if (inventory == null) inventory = InventorySystem.Instance;
        if (inventory == null) inventory = FindObjectOfType<InventorySystem>();

        if (magazineItem == null) magazineItem = ResolveMagazineItem();

        if (inventory == null)
        {
            Debug.LogWarning("[AmmoLink] InventorySystem не найден. Синхронизация отключена.");
            enabled = false;
            return;
        }

        if (magazineItem == null)
        {
            Debug.LogWarning($"[AmmoLink] Предмет-магазин с id '{magazineItemId}' не найден " +
                             "ни в поле Magazine Item, ни в ItemDatabase. Синхронизация отключена.");
            enabled = false;
        }
    }

    ItemData ResolveMagazineItem()
    {
        if (string.IsNullOrEmpty(magazineItemId)) return null;

        ItemDatabase db = ItemDatabase.Instance;
        return db != null ? db.GetById(magazineItemId) : null;
    }

    void OnEnable()
    {
        if (inventory != null) inventory.OnInventoryChanged += HandleInventoryChanged;
        SubscribeWeapon();
    }

    void OnDisable()
    {
        if (inventory != null) inventory.OnInventoryChanged -= HandleInventoryChanged;
        UnsubscribeWeapon();
    }

    void Start()
    {
        // Оружие может быть выключено до подбора, поэтому ищем и подписываемся
        // не только в Awake, но и здесь — и далее по мере необходимости
        SubscribeWeapon();
        SyncToWeapon();
    }

    void Update()
    {
        // Оружие могло появиться позже (экипировка включает объект).
        // Проверка дешёвая: сравнение ссылки.
        if (weapon == null) SubscribeWeapon();
    }

    // =====================================================================
    void SubscribeWeapon()
    {
        if (weapon == null)
        {
            // true — включая выключенные объекты: оружие спрятано до экипировки
            Wep[] found = FindObjectsOfType<Wep>(true);
            if (found.Length > 0) weapon = found[0];
            if (weapon == null) return;
        }

        // Повторная подписка безвредна только если сначала отписаться
        weapon.OnMagazinesChanged -= HandleWeaponMagazinesChanged;
        weapon.OnMagazinesChanged += HandleWeaponMagazinesChanged;

        SyncToWeapon();
    }

    void UnsubscribeWeapon()
    {
        if (weapon != null) weapon.OnMagazinesChanged -= HandleWeaponMagazinesChanged;
    }

    /// <summary>Инвентарь изменился — пересчитываем запас у оружия.</summary>
    void HandleInventoryChanged()
    {
        if (isSyncing) return;
        SyncToWeapon();
    }

    /// <summary>
    /// Оружие потратило магазин при перезарядке — убираем один предмет из инвентаря.
    /// </summary>
    void HandleWeaponMagazinesChanged(int newCount)
    {
        if (isSyncing || inventory == null || magazineItem == null) return;

        int inInventory = inventory.CountItem(magazineItem) * magazinesPerItem;
        if (newCount >= inInventory) return;   // не расход, а пополнение — инвентарь уже прав

        int spent = inInventory - newCount;
        int itemsToRemove = Mathf.Max(1, spent / Mathf.Max(1, magazinesPerItem));

        isSyncing = true;
        inventory.RemoveItem(magazineItem, itemsToRemove);
        isSyncing = false;

        Debug.Log($"[AmmoLink] Израсходован магазин. Осталось: {inventory.CountItem(magazineItem)}");
    }

    /// <summary>Привести spareMagazines к числу магазинов в инвентаре.</summary>
    public void SyncToWeapon()
    {
        if (weapon == null || inventory == null || magazineItem == null) return;

        int available = inventory.CountItem(magazineItem) * magazinesPerItem;
        if (weapon.spareMagazines == available) return;

        isSyncing = true;
        weapon.SetSpareMagazines(available);
        isSyncing = false;
    }

    /// <summary>Сколько магазинов доступно по данным инвентаря.</summary>
    public int AvailableMagazines =>
        inventory != null && magazineItem != null
            ? inventory.CountItem(magazineItem) * magazinesPerItem
            : 0;
}
