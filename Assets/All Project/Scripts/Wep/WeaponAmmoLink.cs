using UnityEngine;

/// <summary>Инвентарь — источник правды по магазинам: сколько предметов в сумке, столько и Wep.spareMagazines.</summary>
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

    // Флаг от рекурсии: RemoveItem дёрнет OnInventoryChanged обратно
    private bool isSyncing;
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
        SubscribeWeapon();
        SyncToWeapon();
    }
    void Update()
    {
        if (weapon == null) SubscribeWeapon();
    }
    void SubscribeWeapon()
    {
        if (weapon == null)
        {
            Wep[] found = FindObjectsOfType<Wep>(true);
            if (found.Length > 0) weapon = found[0];
            if (weapon == null) return;
        }
        weapon.OnMagazinesChanged -= HandleWeaponMagazinesChanged;
        weapon.OnMagazinesChanged += HandleWeaponMagazinesChanged;
        SyncToWeapon();
    }

    void UnsubscribeWeapon()
    {
        if (weapon != null) weapon.OnMagazinesChanged -= HandleWeaponMagazinesChanged;
    }

    void HandleInventoryChanged()
    {
        if (isSyncing) return;
        SyncToWeapon();
    }
    // Wep потратил магазин на R — убираем 1 предмет из сумки
    void HandleWeaponMagazinesChanged(int newCount)
    {
        if (isSyncing || inventory == null || magazineItem == null) return;

        int inInventory = inventory.CountItem(magazineItem) * magazinesPerItem;
        if (newCount >= inInventory) return;
        int spent = inInventory - newCount;
        int itemsToRemove = Mathf.Max(1, spent / Mathf.Max(1, magazinesPerItem));
        isSyncing = true;
        inventory.RemoveItem(magazineItem, itemsToRemove);
        isSyncing = false;
        Debug.Log($"[AmmoLink] Израсходован магазин. Осталось: {inventory.CountItem(magazineItem)}");
    }
    public void SyncToWeapon()
    {
        if (weapon == null || inventory == null || magazineItem == null) return;

        int available = inventory.CountItem(magazineItem) * magazinesPerItem;
        if (weapon.spareMagazines == available) return;

        isSyncing = true;
        weapon.SetSpareMagazines(available);
        isSyncing = false;
    }

    public int AvailableMagazines =>
        inventory != null && magazineItem != null
            ? inventory.CountItem(magazineItem) * magazinesPerItem
            : 0;
}
