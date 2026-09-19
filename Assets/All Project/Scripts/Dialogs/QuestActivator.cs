using UnityEngine;

/// <summary>
/// Включение/выключение объекта по статусу квеста.
///
/// Связка: DialogueCommand (StartQuest/CompleteQuest) -> QuestSystem -> сюда.
///
/// ВАЖНО: вешать на АКТИВНЫЙ объект — компонент должен получать события.
/// Скрывать можно что угодно через поле target (цель может стартовать выключенной).
/// Сохранения переживает: состояние применяется в Start (после QuestSystem.Load).
///
/// Примеры:
///  - ключ на повозке: компонент на группе повозки, target = ключ,
///    questId = q_cart_key, visibleWhenState = 1 (виден, пока квест активен);
///  - сундук в лесу: компонент на корне сундука, target = содержимое,
///    questId = q_forest_chest, visibleWhenState = 1.
/// </summary>
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
