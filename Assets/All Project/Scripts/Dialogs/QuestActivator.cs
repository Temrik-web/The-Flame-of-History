using UnityEngine;

/// <summary>Включение объекта по статусу квеста. Вешать на активный объект, прятать через target.</summary>
[DisallowMultipleComponent]
public class QuestActivator : MonoBehaviour
{
    [Tooltip("Id квеста (например q_cart_key, q_forest_chest, q_knife).")]
    public string questId;

    [Tooltip("При каком статусе объект виден: 0 — не начат, 1 — активен, 2 — выполнен, 3 — провален.")]
    public int visibleWhenState = 1;

    [Tooltip("Инверсия: прятать при указанном статусе, показывать во всех остальных.")]
    public bool invert = false;

    [Tooltip("Что включать/выключать. Пусто — свой объект.")]
    public GameObject target;

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

    void Start()
    {
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
}
