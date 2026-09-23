using UnityEngine;

/// <summary>
/// Находит в сцене модель «Rusty key» (FBX без логики) и превращает её в
/// подбираемый квестовый ключ первого квеста:
///
///  - добавляет BoxCollider (иначе E-подбор невозможен — лучу не во что попасть);
///  - добавляет Pickup (item = key_cellar): даёт стандартную подсказку
///    инвентаря «E — Ключ от подвала» по центру экрана + подбор по E;
///  - добавляет QuestKeyPickup (questId = q_cart_key): ключ виден только
///    пока квест активен (до D1 и после подбора его нет).
///
/// Как пользоваться:
///  1. Создай пустой объект (например «RustyKeySetup») рядом с повозкой;
///  2. Повесь этот скрипт;
///  3. Play — в консоли увидишь «[RustyKey] Готов: ...».
/// Никаких правок префаба/меша не нужно, модель остаётся родной.
///
/// Подбор закрывает q_cart_key сам: InventorySystem.AddItem ->
/// QuestSystem.NotifyItemAdded (key_cellar -> Complete q_cart_key).
/// </summary>
[DisallowMultipleComponent]
public class RustyKeyPickupSetup : MonoBehaviour
{
    [Header("Что ищем")]
    [Tooltip("Имя модели в сцене (переименованный чайлд FBX).")]
    public string keyObjectName = "Rusty key";

    [Header("Связка")]
    [Tooltip("Id квеста первого задания.")]
    public string questId = "q_cart_key";
    [Tooltip("Id предмета-ключа из ItemDatabase.")]
    public string itemId = "key_cellar";
    [Tooltip("Необязательно: прямой ассет (если ItemDatabase не найдётся).")]
    public ItemData itemOverride;

    [Header("Вид в сене")]
    [Tooltip("Ключ лежит в сене — вращение/покачивание выключаем, свет оставляем чтобы было видно.")]
    public bool keySpin = false;
    public bool keyBob = false;
    public bool keyGlow = true;
    [Tooltip("Не перекрашивать ржавую модель в цвет редкости.")]
    public bool keepRustyLook = true;

    [Header("Дубли")]
    [Tooltip("Найденный настоящий ключ заменяет куб-заглушку CartKey: " +
             "заглушку прячем, чтобы не было двух ключей.")]
    public bool disablePlaceholderKey = true;
    public string placeholderName = "CartKey";

    void Start() => SetupNow();

    /// <summary>Найти и настроить ключ (можно вызвать из контекстного меню).</summary>
    [ContextMenu("Настроить ржавый ключ сейчас")]
    public void SetupNow()
    {
        GameObject key = FindByName(keyObjectName);
        if (key == null)
        {
            Debug.LogError($"[RustyKey] Объект «{keyObjectName}» не найден в сцене. " +
                           "Проверь имя модели на повозке.", this);
            return;
        }

        // 1) Коллайдер — без него InventorySystem лучом не найдёт предмет.
        Collider col = key.GetComponentInChildren<Collider>();
        if (col == null)
        {
            var rend = key.GetComponentInChildren<Renderer>();
            var box = key.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (rend != null)
            {
                // Чуть увеличиваем, чтобы по маленькому ключу было легко попасть.
                Vector3 size = rend.bounds.size;
                size.x = Mathf.Max(size.x * 4f, 0.4f);
                size.y = Mathf.Max(size.y * 4f, 0.4f);
                size.z = Mathf.Max(size.z * 4f, 0.4f);
                // BoxCollider.size в локальных единицах — делим на мировой масштаб.
                Vector3 lossy = key.transform.lossyScale;
                box.size = new Vector3(
                    lossy.x > 0f ? size.x / lossy.x : 0.4f,
                    lossy.y > 0f ? size.y / lossy.y : 0.4f,
                    lossy.z > 0f ? size.z / lossy.z : 0.4f);
                box.center = key.transform.InverseTransformPoint(rend.bounds.center);
            }
            else
            {
                box.size = Vector3.one * 0.4f;
            }
            Debug.Log($"[RustyKey] {key.name}: добавлен BoxCollider для подбора.", key);
        }

        // 2) Pickup — предмет + подсказка «E — ...».
        var pickup = key.GetComponent<Pickup>();
        if (pickup == null)
            pickup = key.AddComponent<Pickup>();

        ItemData item = itemOverride;
        if (item == null && ItemDatabase.Instance != null)
            item = ItemDatabase.Instance.GetById(itemId);
        if (item == null)
        {
            Debug.LogError($"[RustyKey] Предмет '{itemId}' не найден (ItemDatabase пуст?). " +
                           "Назначь itemOverride вручную.", key);
        }
        else
        {
            pickup.item = item;
        }
        pickup.amount = 1;
        pickup.spin = keySpin;
        pickup.bob = keyBob;
        pickup.createGlowLight = keyGlow;
        if (keepRustyLook)
            pickup.tintMaterialByRarity = false;

        // 3) Квест-гейт: виден только пока q_cart_key активен.
        var gate = key.GetComponent<QuestKeyPickup>();
        if (gate == null)
            gate = key.AddComponent<QuestKeyPickup>();
        gate.questId = questId;
        gate.target = key;
        gate.keyVisualRoot = key.transform;
        gate.expectedItemId = itemId;
        gate.Apply();

        // 4) Прячем куб-заглушку, чтобы не было двух ключей.
        if (disablePlaceholderKey && !string.IsNullOrEmpty(placeholderName))
        {
            GameObject ph = FindByName(placeholderName);
            if (ph != null && ph != key && ph.activeSelf)
            {
                ph.SetActive(false);
                Debug.Log($"[RustyKey] Заглушка '{placeholderName}' спрятана — " +
                          "теперь ключ только ржавый.", ph);
            }
        }

        Debug.Log($"[RustyKey] Готов: '{key.name}' (E-подбор, подсказка, квест {questId}).", key);
    }

    /// <summary>Поиск по имени среди всех трансформов, включая скрытые.</summary>
    static GameObject FindByName(string n)
    {
        if (string.IsNullOrEmpty(n)) return null;
        var all = FindObjectsOfType<Transform>(true);
        foreach (var t in all)
        {
            if (t != null && t.name == n)
                return t.gameObject;
        }
        return null;
    }
}
