using UnityEngine;

/// <summary>
/// Счётчик бесед → событие мира. Тот самый «отросток» механики:
/// «поговорил 2 диалогом — идёт событие», позже — спавн немцев.
///
/// Как вешать:
///  1. Положи компонент на тот же объект, где DialogueTrigger (например, на Степана
///     или Василия), укажи тот же DialogueData в поле dialogue;
///  2. talksBeforeEvent = 2 — на второй завершённый разговор кидаем eventId;
///  3. eventId = "raid_germans" — GermanRaidDirector его уже слушает.
///
/// Состояние переживает перезапуск (PlayerPrefs): повторные прохождения
/// после сейва не накручивают счётчик заново, а «одноразовость» честно
/// соблюдается через fireOnce.
/// </summary>
[DisallowMultipleComponent]
public class DialogueRaidHook : MonoBehaviour
{
    [Header("Какой диалог считаем")]
    [Tooltip("Тот же ассет, что висит на DialogueTrigger рядом. Пусто — считаем любой диалог этого объекта.")]
    public DialogueData dialogue;

    [Header("Когда стрелять")]
    [Tooltip("После скольких завершённых бесед кидать событие. 2 = «поговорил вторым диалогом — идёт событие».")]
    [Min(1)] public int talksBeforeEvent = 2;

    [Tooltip("Id события в DialogueEventBus (слушает GermanRaidDirector).")]
    public string eventId = "raid_germans";

    [Tooltip("Кидать только один раз (потом молчать до сброса сейва).")]
    public bool fireOnce = true;

    [Tooltip("Квест, который стартует вместе с событием (например q_raid). Пусто — не стартовать.")]
    public string questToStart = "";

    [Tooltip("Флаг GameState, который взводим вместе с событием (например raid_warned).")]
    public string flagToSet = "";

    const string CountPrefix = "flame_hook_talks_";
    const string FiredPrefix = "flame_hook_fired_";

    string KeyBase
    {
        get
        {
            string d = dialogue != null
                ? (!string.IsNullOrEmpty(dialogue.name) ? dialogue.name : dialogue.dialogueName)
                : gameObject.name;
            return $"{gameObject.name}_{d}_{eventId}";
        }
    }

    void OnEnable()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnDialogueEnded += OnAnyDialogueEnded;
        else
            Invoke(nameof(LateSubscribe), 0.5f);
    }

    void LateSubscribe()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnDialogueEnded -= OnAnyDialogueEnded;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnDialogueEnded += OnAnyDialogueEnded;
    }

    void OnDisable()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnDialogueEnded -= OnAnyDialogueEnded;
    }

    void OnAnyDialogueEnded()
    {
        // Какой диалог только что закрыли — спрашиваем у триггера рядом.
        // Триггер уже сбросил свой флаг, поэтому сверяемся мягко:
        // если dialogue задан — считаем только его (по имени объекта-триггера),
        // иначе — любой диалог, начатый с этого объекта.
        DialogueTrigger trigger = GetComponent<DialogueTrigger>();
        if (dialogue != null && trigger != null && trigger.dialogue != dialogue)
            return;

        string countKey = CountPrefix + KeyBase;
        string firedKey = FiredPrefix + KeyBase;

        if (fireOnce && PlayerPrefs.GetInt(firedKey, 0) == 1)
            return;

        int talks = PlayerPrefs.GetInt(countKey, 0) + 1;
        PlayerPrefs.SetInt(countKey, talks);
        PlayerPrefs.Save();
        Debug.Log($"[RaidHook] {KeyBase}: беседа №{talks} (нужно {talksBeforeEvent}).", this);

        if (talks < talksBeforeEvent) return;

        if (!string.IsNullOrEmpty(questToStart))
            QuestSystem.StartQuest(questToStart);
        if (!string.IsNullOrEmpty(flagToSet))
            GameState.SetFlag(flagToSet, true);

        if (fireOnce)
        {
            PlayerPrefs.SetInt(firedKey, 1);
            PlayerPrefs.Save();
        }

        DialogueEventBus.Raise(eventId);
    }

    /// <summary>Сбросить счётчик (для тестов и новой игры).</summary>
    [ContextMenu("Сбросить счётчик бесед")]
    public void ResetCounter()
    {
        PlayerPrefs.DeleteKey(CountPrefix + KeyBase);
        PlayerPrefs.DeleteKey(FiredPrefix + KeyBase);
        PlayerPrefs.Save();
    }
}
