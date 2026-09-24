using UnityEngine;
/// <summary>ВАЖНО: порядок enum менять нельзя — Unity хранит его как int в ассетах.</summary>
public enum ItemType
{
    Misc = 0,
    Weapon = 1,
    Consumable = 2,
    Ammo = 3,
    Key = 4
}
public enum ItemRarity
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}
[CreateAssetMenu(fileName = "NewItem", menuName = "Inventory/Item")]
public class ItemData : ScriptableObject
{
    [Header("Идентификация")]
    [Tooltip("Уникальный строковый id. Используется при сохранении/загрузке. " +
             "Если пусто — берётся имя ассета.")]
    public string itemId = "";

    public string itemName = "Предмет";
    [TextArea] public string description;
    public Sprite icon;

    [Header("Стакование")]
    public bool stackable = true;
    [Min(1)] public int maxStack = 99;

    [Header("Тип и поведение")]
    public ItemType itemType = ItemType.Misc;
    public ItemRarity rarity = ItemRarity.Common;

    [Tooltip("Порядок внутри своей категории при сортировке. Меньше — выше.")]
    public int sortOrder = 0;

    [Tooltip("Consumable: сколько HP восстанавливает. Ammo: сколько магазинов даёт.")]
    public float useValue = 25f;

    [Tooltip("Тратится ли предмет при использовании (аптечка — да, оружие — нет).")]
    public bool consumeOnUse = true;

    [Header("Key (только для ItemType.Key)")]
    [Tooltip("Идентификатор двери/замка, который открывает этот ключ.")]
    public string keyId = "";

    [Header("Экипировка (только для ItemType.Weapon)")]
    [Tooltip("Id оружия в сцене, которое даёт этот предмет. Должен совпадать с " +
             "полем Weapon Id у компонента EquippableWeapon. Например: ppsh41, rgd33, knife.")]
    public string equipWeaponId = "";

    [Header("Быстрый слот")]
    [Tooltip("Цифра 1..9 для быстрого доступа. 0 — не назначен.\n" +
             "Оружие по нажатию экипируется, расходники применяются.")]
    [Range(0, 9)] public int hotbarSlot = 0;

    [Header("Мир")]
    [Tooltip("Префаб для выбрасывания именно этого предмета. " +
             "Если пусто — инвентарь возьмёт универсальный префаб.")]
    public GameObject worldPrefab;

    [Header("Звук")]
    public AudioClip pickupSound;
    public AudioClip useSound;

    public string Id => string.IsNullOrEmpty(itemId) ? name : itemId;
    public int CategoryOrder => GetCategoryOrder(itemType);
    public string CategoryName => GetCategoryName(itemType);
    public Color RarityColor => GetRarityColor(rarity);
    // Порядок отображения отделён от enum, чтобы не ломать ассеты при пересортировке
    public static int GetCategoryOrder(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon:     return 0;
            case ItemType.Ammo:       return 1;
            case ItemType.Consumable: return 2;
            case ItemType.Key:        return 3;
            default:                  return 4;
        }
    }

    public static readonly ItemType[] DisplayOrder =
    {
        ItemType.Weapon,
        ItemType.Ammo,
        ItemType.Consumable,
        ItemType.Key,
        ItemType.Misc
    };
    public static string GetCategoryName(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon:     return "Оружие";
            case ItemType.Ammo:       return "Патроны";
            case ItemType.Consumable: return "Медикаменты";
            case ItemType.Key:        return "Ключи";
            default:                  return "Прочее";
        }
    }

    public static Color GetRarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Uncommon:  return new Color(0.45f, 0.85f, 0.40f);
            case ItemRarity.Rare:      return new Color(0.35f, 0.65f, 1.00f);
            case ItemRarity.Epic:      return new Color(0.72f, 0.45f, 0.95f);
            case ItemRarity.Legendary: return new Color(1.00f, 0.78f, 0.28f);
            default:                   return new Color(0.72f, 0.74f, 0.78f);
        }
    }

    public static Color GetCategoryColor(ItemType type)
    {
        switch (type)
        {
            case ItemType.Weapon:     return new Color(0.95f, 0.55f, 0.40f);
            case ItemType.Ammo:       return new Color(0.95f, 0.82f, 0.45f);
            case ItemType.Consumable: return new Color(0.50f, 0.90f, 0.65f);
            case ItemType.Key:        return new Color(0.85f, 0.75f, 0.45f);
            default:                  return new Color(0.70f, 0.75f, 0.85f);
        }
    }

    public bool IsEquippable =>
        itemType == ItemType.Weapon && !string.IsNullOrEmpty(equipWeaponId);
    public bool IsCurrentlyEquipped =>
        IsEquippable && WeaponSlotManager.IsEquippedById(equipWeaponId);
    public bool HasHotbarSlot => hotbarSlot >= 1 && hotbarSlot <= 9;
    public bool Use(GameObject user)
    {
        if (user == null) return false;

        switch (itemType)
        {
            case ItemType.Consumable:
            {
                PlayerHealth health = user.GetComponentInParent<PlayerHealth>();
                if (health == null) health = Object.FindObjectOfType<PlayerHealth>();
                if (health == null)
                {
                    Debug.LogWarning($"[ItemData] {itemName}: PlayerHealth не найден.");
                    return false;
                }
                if (health.HealthPercent >= 1f)
                {
                    Debug.Log($"[ItemData] {itemName}: здоровье уже полное.");
                    return false;
                }
                health.Heal(useValue);
                return true;
            }

            case ItemType.Ammo:
            {
                // Магазины расходуются сами при перезарядке через WeaponAmmoLink.
                // Прибавлять spareMagazines здесь нельзя: счётчики разойдутся
                Debug.Log($"[ItemData] {itemName}: расходуется автоматически при перезарядке (R).");
                return false;
            }

            case ItemType.Key:
            {
                if (!string.IsNullOrEmpty(keyId))
                {
                    GameState.SetFlag("key_" + keyId, true);
                    Debug.Log($"[ItemData] Ключ активирован: {keyId}");
                    return true;
                }
                return false;
            }

            case ItemType.Weapon:
            {
                // Оружие экипируется через WeaponSlotManager и не тратится (return false)
                if (IsEquippable)
                {
                    WeaponSlotManager.EquipById(equipWeaponId);
                    return false;
                }
                Debug.Log($"[ItemData] {itemName}: не задан Equip Weapon Id.");
                return false;
            }

            case ItemType.Misc:
            default:
                Debug.Log($"[ItemData] {itemName}: нет действия при использовании.");
                return false;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!stackable) maxStack = 1;
        if (maxStack < 1) maxStack = 1;
    }
#endif
}
