using UnityEngine;

/// <summary>Ключ q_cart_key на повозке: виден пока квест активен, модель заменяемая.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Pickup))]
public class QuestKeyPickup : MonoBehaviour
{
    [Header("Квест")]
    [Tooltip("Id квеста первого задания: ключ от дома деда.")]
    public string questId = "q_cart_key";

    [Tooltip("При каком статусе ключ виден: 0 — не начат, 1 — активен, 2 — выполнен, 3 — провален.")]
    public int visibleWhenState = 1;

    [Tooltip("Инверсия: прятать при указанном статусе, показывать во всех остальных.")]
    public bool invert;

    [Tooltip("Что включать/выключать. Пусто — свой объект. " +
             "Рекомендуется: корень CartKey (тогда прячется и коллайдер).")]
    public GameObject target;

    [Header("Заменяемая модель")]
    [Tooltip("Сюда клади НАСТОЯЩУЮ модель ключа. Сейчас там куб-заглушка CartKeyMesh — " +
             "удали куб, положи свою модель ребёнком этого же рута. " +
             "Подбор/квест не сломаются: они на корне, а не на модели.")]
    public Transform keyVisualRoot;

    [Tooltip("Ожидаемый id предмета в Pickup (проверка связки).")]
    public string expectedItemId = "key_cellar";

    void Reset()
    {
        if (target == null) target = gameObject;
        if (keyVisualRoot == null)
        {
            // По умолчанию визуал — первый дочерний Mesh (заглушка), иначе сам корень.
            var mesh = GetComponentInChildren<MeshRenderer>();
            keyVisualRoot = mesh != null ? mesh.transform : transform;
        }
    }

    void OnEnable()
    {
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

    void Awake()
    {
        ValidateWiring();
    }

    void Start()
    {
        WarnOnDuplicateActivator();
        Apply();
    }

    void OnQuestChanged(string changedId)
    {
        if (changedId == questId) Apply();
    }

    /// <summary>Применить видимость по текущему статусу квеста.</summary>
    public void Apply()
    {
        GameObject go = target != null ? target : gameObject;
        if (go == null) return;
        bool vis = QuestSystem.GetState(questId) == visibleWhenState;
        if (invert) vis = !vis;
        if (go.activeSelf != vis)
            go.SetActive(vis);
    }

    void ValidateWiring()
    {
        var pickup = GetComponent<Pickup>();
        if (pickup == null)
        {
            Debug.LogError($"[QuestKey] {name}: нет Pickup — подбирать будет нечего.", this);
            return;
        }
        if (pickup.item == null)
        {
            Debug.LogError($"[QuestKey] {name}: Pickup.item пуст — назначь Item_KeyCellar.", this);
        }
        else if (!string.IsNullOrEmpty(expectedItemId) && pickup.item.Id != expectedItemId)
        {
            Debug.LogWarning($"[QuestKey] {name}: Pickup.item = '{pickup.item.Id}', " +
                             $"а ожидался '{expectedItemId}'. Квест q_cart_key " +
                             $"закрывается только подборои key_cellar (см. QuestSystem.NotifyItemAdded).", this);
        }
        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[QuestKey] {name}: нет коллайдера — E-подбор не сработает. " +
                             "Добавь BoxCollider на корень.", this);
    }

    void WarnOnDuplicateActivator()
    {
        var activators = GetComponents<QuestActivator>();
        foreach (var a in activators)
        {
            if (a != null && a.questId == questId)
                Debug.LogWarning($"[QuestKey] {name}: рядом висит QuestActivator с тем же " +
                                 $"questId '{questId}' — оставь что-то одно (этот скрипт ИЛИ QuestActivator), " +
                                 "иначе они будут дёргать видимость вдвоём.", this);
        }
        // Держатель выше по иерархии тоже может дёргать наш target:
        // ищем только прямых соседей, глубокий поиск по всей сцене здесь не нужен.
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (target == null) target = gameObject;
        if (string.IsNullOrEmpty(questId)) questId = "q_cart_key";
        if (string.IsNullOrEmpty(expectedItemId)) expectedItemId = "key_cellar";
    }

    /// <summary>Показать ключ в редакторе принудительно (чтобы расставить модель).</summary>
    [ContextMenu("Показать ключ (редактор)")]
    void ShowInEditor()
    {
        GameObject go = target != null ? target : gameObject;
        if (go != null) go.SetActive(true);
        Debug.Log($"[QuestKey] {name}: target '{go.name}' включён для расстановки модели. " +
                  "Не забудь: в игре видимостью управляет квест.", this);
    }
#endif
}
