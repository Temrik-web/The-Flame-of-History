using UnityEngine;

/// <summary>Превращает модель «Rusty key» в подбираемый ключ q_cart_key: коллайдер + Pickup + гейт.</summary>
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
    [Tooltip("Ключ лежит в сене без спецэффектов: вращение/покачивание выключены, " +
             "свет и подсветка модели убираются полностью в SetupNow.")]
    public bool keySpin = false;
    public bool keyBob = false;
    [Tooltip("Не перекрашивать ржавую модель в цвет редкости.")]
    public bool keepRustyLook = true;

    [Header("Размер")]
    [Tooltip("Во сколько раз увеличить модель ключа. Квестовый предмет должен " +
             "торчать из сена: мелкий ключ и лучом не выцепить, и сено его перекрывает.")]
    [Range(1f, 4f)] public float keyScale = 2f;
    [HideInInspector] public Vector3 baseKeyScale = Vector3.zero;

    [Header("Дубли")]
    [Tooltip("Найденный настоящий ключ заменяет куб-заглушку CartKey: " +
             "заглушку прячем, чтобы не было двух ключей.")]
    public bool disablePlaceholderKey = true;
    public string placeholderName = "CartKey";

    void Start() => SetupNow();

    void OnEnable()
    {
        // Сетап висит на активном объекте и форвардит события на гейт скрытого ключа.
        QuestSystem.OnQuestStarted += OnQuestChanged;
        QuestSystem.OnQuestCompleted += OnQuestChanged;
        QuestSystem.OnQuestFailed += OnQuestChanged;
    }

    void OnDisable()
    {
        QuestSystem.OnQuestStarted -= OnQuestChanged;
        QuestSystem.OnQuestCompleted -= OnQuestChanged;
        QuestSystem.OnQuestFailed -= OnQuestChanged;
    }

    void OnQuestChanged(string changedId)
    {
        if (changedId != questId) return;
        GameObject key = FindByName(keyObjectName);
        if (key == null) return;
        var gate = key.GetComponent<QuestKeyPickup>();
        if (gate != null) gate.Apply();
    }

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

        // Размер до коллайдера: бокс считается от мировых габаритов.
        if (baseKeyScale == Vector3.zero)
            baseKeyScale = key.transform.localScale;
        key.transform.localScale = baseKeyScale * Mathf.Max(0.5f, keyScale);

        // Коллайдер нужен лучу подбора.
        Collider col = key.GetComponentInChildren<Collider>();
        if (col == null)
        {
            var rend = key.GetComponentInChildren<Renderer>();
            var box = key.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (rend != null)
            {
                // С запасом: по маленькому ключу лучом сложно попасть.
                Vector3 size = rend.bounds.size;
                size.x = Mathf.Max(size.x * 6f, 0.6f);
                size.y = Mathf.Max(size.y * 6f, 0.6f);
                size.z = Mathf.Max(size.z * 6f, 0.6f);
                // BoxCollider.size в локальных единицах — делим на мировой масштаб.
                Vector3 lossy = key.transform.lossyScale;
                box.size = new Vector3(
                    lossy.x > 0f ? size.x / lossy.x : 0.6f,
                    lossy.y > 0f ? size.y / lossy.y : 0.6f,
                    lossy.z > 0f ? size.z / lossy.z : 0.6f);
                box.center = key.transform.InverseTransformPoint(rend.bounds.center);
            }
            else
            {
                box.size = Vector3.one * 0.6f;
            }
            Debug.Log($"[RustyKey] {key.name}: добавлен BoxCollider для подбора.", key);
        }

        // Дотягиваем мелкий бокс до удобного размера.
        BoxCollider owned = key.GetComponent<BoxCollider>();
        if (owned != null)
        {
            Vector3 ws = new Vector3(
                owned.size.x * Mathf.Abs(key.transform.lossyScale.x),
                owned.size.y * Mathf.Abs(key.transform.lossyScale.y),
                owned.size.z * Mathf.Abs(key.transform.lossyScale.z));
            if (ws.x < 0.6f || ws.y < 0.6f || ws.z < 0.6f)
            {
                Vector3 want = new Vector3(
                    Mathf.Max(ws.x, 0.6f), Mathf.Max(ws.y, 0.6f), Mathf.Max(ws.z, 0.6f));
                Vector3 lossy = key.transform.lossyScale;
                owned.size = new Vector3(
                    Mathf.Abs(lossy.x) > 0.001f ? want.x / Mathf.Abs(lossy.x) : 0.6f,
                    Mathf.Abs(lossy.y) > 0.001f ? want.y / Mathf.Abs(lossy.y) : 0.6f,
                    Mathf.Abs(lossy.z) > 0.001f ? want.z / Mathf.Abs(lossy.z) : 0.6f);
                Debug.Log($"[RustyKey] {key.name}: коллайдер увеличен для удобного подбора.", key);
            }
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
        pickup.createGlowLight = false;
        pickup.highlightIntensity = 0f;
        Transform oldGlow = key.transform.Find("Glow");
        if (oldGlow != null)
        {
            Object.Destroy(oldGlow.gameObject);
            Debug.Log($"[RustyKey] {key.name}: удалён старый объект свечения.", key);
        }
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

        // Диагностика: ключ по квесту должен быть виден, но висит под
        // выключенным родителем — тогда SetActive на нём не поможет.
        if (gate != null)
        {
            bool shouldShow = QuestSystem.GetState(questId) == gate.visibleWhenState;
            if (gate.invert) shouldShow = !shouldShow;
            if (shouldShow)
            {
                Transform p = key.transform.parent;
                while (p != null)
                {
                    if (!p.gameObject.activeSelf)
                    {
                        Debug.LogWarning($"[RustyKey] {key.name}: должен быть виден, но родитель " +
                                         $"«{p.name}» выключен — ключа не будет видно!", key);
                        break;
                    }
                    p = p.parent;
                }
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
