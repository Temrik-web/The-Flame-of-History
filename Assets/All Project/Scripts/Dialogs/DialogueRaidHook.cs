using UnityEngine;

/// <summary>Счётчик бесед -> событие мира (например "raid_germans" после N-й беседы).</summary>
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
        DialogueManager m = DialogueManager.Instance;
        // Esc — не беседа, счётчик не крутим.
        if (m != null && !m.LastFinishedCompleted)
            return;
        DialogueData finished = m != null ? m.LastFinishedDialogue : null;

        // Фильтр по диалогу: задан конкретный — считаем только его.
        if (dialogue != null && finished != null && finished != dialogue)
            return;

        // Без фильтра считаем только «свои» диалоги этого объекта.
        if (dialogue == null && finished != null)
        {
            DialogueTrigger trigger = GetComponent<DialogueTrigger>();
            NpcDialogueSequence seq = GetComponent<NpcDialogueSequence>();
            bool ours = false;
            if (trigger != null && trigger.dialogue != null && trigger.dialogue == finished)
                ours = true;
            if (seq != null && seq.dialogues != null && seq.dialogues.Contains(finished))
                ours = true;
        // Если на объекте нет ни триггера, ни цепочки — считаем любой.
            if ((trigger == null || trigger.dialogue == null) && seq == null)
                ours = true;
            if (!ours)
                return;
        }
        // Фолбэк для сторонних вызовов без LastFinished.
        if (finished == null)
        {
            DialogueTrigger trigger = GetComponent<DialogueTrigger>();
            if (dialogue != null && trigger != null && trigger.dialogue != dialogue)
                return;
        }

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
