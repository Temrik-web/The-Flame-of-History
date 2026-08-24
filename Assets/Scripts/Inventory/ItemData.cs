using UnityEngine;

/// <summary>
/// Категория предмета. Определяет, что произойдёт при использовании.
/// </summary>
public enum ItemType
{
    Misc,        // хлам / квестовое / просто лежит в инвентаре
    Weapon,      // оружие
    Consumable,  // аптечка, еда — лечит
    Ammo,        // патроны / магазины
    Key          // ключ для двери
}

/// <summary>
/// Описание типа предмета (ассет). Один ассет = один вид предмета:
/// "Автомат", "Аптечка", "Патроны 5.45" и т.д.
/// Создаётся через Assets -> Create -> Inventory -> Item.
/// </summary>
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

    [Tooltip("Consumable: сколько HP восстанавливает. Ammo: сколько магазинов даёт.")]
    public float useValue = 25f;

    [Tooltip("Тратится ли предмет при использовании (аптечка — да, оружие — нет).")]
    public bool consumeOnUse = true;

    [Header("Key (только для ItemType.Key)")]
    [Tooltip("Идентификатор двери/замка, который открывает этот ключ.")]
    public string keyId = "";

    [Header("Мир")]
    [Tooltip("Префаб для выбрасывания именно этого предмета. " +
             "Если пусто — инвентарь возьмёт универсальный префаб.")]
    public GameObject worldPrefab;

    [Header("Звук")]
    public AudioClip pickupSound;
    public AudioClip useSound;

    /// <summary>Безопасный id: если поле пустое — имя ассета.</summary>
    public string Id => string.IsNullOrEmpty(itemId) ? name : itemId;

    /// <summary>
    /// Применить предмет. Возвращает true, если использование сработало
    /// (и предмет нужно потратить, если consumeOnUse).
    /// </summary>
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
                Wep weapon = Object.FindObjectOfType<Wep>();
                if (weapon == null)
                {
                    Debug.LogWarning($"[ItemData] {itemName}: оружие (Wep) не найдено.");
                    return false;
                }
                weapon.spareMagazines += Mathf.Max(1, Mathf.RoundToInt(useValue));
                return true;
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
