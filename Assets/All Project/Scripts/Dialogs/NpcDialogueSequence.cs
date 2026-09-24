using System.Collections.Generic;
using UnityEngine;

/// <summary>Цепочка диалогов на одном NPC: первый непройденный предлагается по E.</summary>
[DisallowMultipleComponent]
public class NpcDialogueSequence : MonoBehaviour
{
    [Header("Диалоги по порядку")]
    [Tooltip("Положи D1..D8 по порядку. Первый непройденный предложится по E.")]
    public List<DialogueData> dialogues = new List<DialogueData>();

    [Header("Взаимодействие")]
    public string interactMessage = "Нажмите E, чтобы говорить";
    public float interactDistance = 3f;
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Диалог только при прямой видимости (без стен между). " +
             "Выключи, если НПС стоит за низким забором, который перекрывает луч.")]
    public bool requireLineOfSight = true;
    [Tooltip("Что считается стеной. Триггеры игнорятся всегда.")]
    public LayerMask losBlockMask = ~0;

    [Header("Поведение")]
    [Tooltip("Продолжать диалог с сохранённого узла, если вышли досрочно (Esc).")]
    public bool resumeFromSave = true;
    [Tooltip("Молчать, когда вся цепочка пройдена (иначе повторять последний).")]
    public bool hideWhenAllDone = true;
    [Tooltip("Показывать в подсказке номер беседы: «Поговорить (2/8)».")]
    public bool showCounterInHint = true;
    [Tooltip("Сразу запускать следующий диалог цепочки, когда предыдущий " +
             "пройден до конца. Выключено по умолчанию: по задаче «1 закрылся — " +
             "потом МОЖНО открыть 2,3,4» каждый следующий открывается по E. " +
             "Включи, если хочешь автопродолжение без нажатия. Вышел по Esc — не запускаем, висит «продолжить».")]
    public bool autoStartNext = false;
    [Tooltip("Пауза перед автозапуском следующего (сек, реального времени).")]
    public float autoStartDelay = 0.6f;

    GameObject cachedPlayer;
    DialogueManager cachedManager;
    bool hintShownByUs;
    DialogueData lastStarted;
    float nextPlayerWarnTime = 0f;
    float nextEmptyWarnTime = 0f;

    void Awake()
    {
        // Глушим чужие триггеры на этом же NPC.
        foreach (DialogueTrigger t in GetComponents<DialogueTrigger>())
        {
            if (t != null && t.enabled)
            {
                t.enabled = false;
                Debug.LogWarning($"[NpcSequence] {name}: выключил DialogueTrigger " +
                                 $"«{(t.dialogue != null ? t.dialogue.name : "null")}» на том же объекте — " +
                                 "цепочкой рулит NpcDialogueSequence.", this);
            }
        }
    }

    void OnEnable()
    {
        if (cachedManager == null)
        {
            cachedManager = DialogueManager.Instance;
            if (cachedManager == null)
                cachedManager = FindObjectOfType<DialogueManager>();
        }
        if (cachedManager != null)
            cachedManager.OnDialogueEnded += OnManagerDialogueEnded;
    }

    void OnDisable()
    {
        HideHint();
        if (cachedManager != null)
            cachedManager.OnDialogueEnded -= OnManagerDialogueEnded;
    }

    void Start()
    {
        DialogueManager m = DialogueManager.Instance;
        if (m == null) m = FindObjectOfType<DialogueManager>();
        if (m != null && m != cachedManager)
        {
            if (cachedManager != null) cachedManager.OnDialogueEnded -= OnManagerDialogueEnded;
            cachedManager = m;
            cachedManager.OnDialogueEnded += OnManagerDialogueEnded;
        }
        if (dialogues != null)
            foreach (var d in dialogues)
            {
                if (d == null) { Debug.LogWarning($"[NpcSequence] {name}: пустой слот в списке.", this); continue; }
                int n = d.nodes != null ? d.nodes.Count : 0;
                if (n == 0)
                    Debug.LogError($"[NpcSequence] {name}: «{d.name}» — 0 узлов! Проверь ассет в папке Dialog.", this);
            }
    }

    /// <summary>Первый непройденный диалог (null — вся цепочка done или список пуст).</summary>
    public DialogueData CurrentDialogue()
    {
        if (dialogues == null) return null;
        foreach (var d in dialogues)
        {
            if (d == null) continue;
            if (!DialogueManager.IsDialogueDone(d))
                return d;
        }
        return null;
    }

    public int CurrentIndex()
    {
        if (dialogues == null) return -1;
        for (int i = 0; i < dialogues.Count; i++)
        {
            var d = dialogues[i];
            if (d == null) continue;
            if (!DialogueManager.IsDialogueDone(d))
                return i;
        }
        return -1;
    }

    public bool IsAllDone()
    {
        if (dialogues == null || dialogues.Count == 0) return true;
        foreach (var d in dialogues)
        {
            if (d == null) continue;
            if (!DialogueManager.IsDialogueDone(d))
                return false;
        }
        return true;
    }

    void Update()
    {
        if (cachedManager == null)
        {
            cachedManager = DialogueManager.Instance;
            if (cachedManager == null)
                cachedManager = FindObjectOfType<DialogueManager>();
            if (cachedManager == null) return;
        }

        if (cachedPlayer == null)
        {
            try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
            catch { cachedPlayer = null; }
        }
        if (cachedPlayer == null)
        {
            if (Time.time >= nextPlayerWarnTime)
            {
                nextPlayerWarnTime = Time.time + 5f;
                Debug.LogWarning($"[NpcSequence] {name}: игрок с тегом «Player» не найден — E не сработает. Поставь тег на игрока.", this);
            }
            return;
        }

        // Менеджер занят чужим диалогом — ждём.
        if (cachedManager.isDialogueActive)
        {
            HideHint();
            return;
        }

        // Открыт инвентарь — разговор не предлагаем и не стартуем.
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen)
        {
            HideHint();
            return;
        }

        DialogueData current = CurrentDialogue();
        if (current == null)
        {
            // Вся цепочка пройдена.
            HideHint();
            if (hideWhenAllDone)
            {
                if (dialogues == null || dialogues.Count == 0)
                    Debug.LogWarning($"[NpcSequence] {name}: список Dialogues ПУСТ — " +
                                     "перетащи D1..D8 в инспекторе (до запуска), иначе играть нечего.", this);
                else
                    Debug.Log($"[NpcSequence] {name}: все диалоги цепочки уже пройдены " +
                              "(PlayerPrefs). Для перепроверки: правый клик → Сбросить цепочку.", this);
                enabled = false;
            }
            return;
        }

        float dist = Vector3.Distance(transform.position, cachedPlayer.transform.position);
        bool inRange = dist <= interactDistance;
        bool visible = !requireLineOfSight || DialogueManager.HasLineOfSight(
            transform.position + Vector3.up * 1.6f, cachedPlayer, gameObject, losBlockMask);

        if (cachedManager.interactHint != null)
        {
            if (inRange && visible)
            {
                string msg = interactMessage;
                if (showCounterInHint && dialogues != null && dialogues.Count > 1)
                {
                    int idx = CurrentIndex();
                    if (idx >= 0)
                        msg += $" ({idx + 1}/{dialogues.Count})";
                }
                // Бросили на середине (Esc) — честно пишем «продолжить».
                if (resumeFromSave && !string.IsNullOrEmpty(DialogueManager.GetSavedNodeID(current)))
                    msg += " — продолжить";
                // Квест-гейт (D1 + ключ): сразу видно, почему цепочка стоит.
                msg += DialogueManager.GetQuestGateHint(current);
                cachedManager.interactHint.SetActive(true);
                hintShownByUs = true;
                if (cachedManager.interactHintText != null)
                    cachedManager.interactHintText.text = msg;
            }
            else if (hintShownByUs)
            {
                HideHint();
            }
        }

        if (inRange && visible && Input.GetKeyDown(interactKey))
            StartCurrentDialogue(current);
    }

    void StartCurrentDialogue(DialogueData dialogue)
    {
        if (dialogue == null) return;
        if (DialogueManager.Instance == null) return;
        if (DialogueManager.Instance.isDialogueActive) return;
        // Открытый инвентарь и диалог не совмещаем (см. DialogueTrigger).
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen)
            return;

        // Пустой ассет в рантайме не стартуем.
        if (dialogue.nodes == null || dialogue.nodes.Count == 0)
        {
            if (Time.time >= nextEmptyWarnTime)
            {
                nextEmptyWarnTime = Time.time + 5f;
                string state = dialogue.nodes == null
                    ? "null (импорт сломан)"
                    : "пуст (0)";
                Debug.LogError($"[NpcSequence] {name}: «{dialogue.name}» — nodes {state} в рантайме! " +
                               "Сверься с «Проверить связки» -> [УЗЛЫ]. Если в редакторе узлы есть — " +
                               "Tools -> Диалоги -> Переимпортировать диалоги.", this);
            }
            return;
        }

        string resumeNode = "";
        if (resumeFromSave)
        {
            resumeNode = DialogueManager.GetSavedNodeID(dialogue);
            if (!string.IsNullOrEmpty(resumeNode) && dialogue.GetNodeByID(resumeNode) == null)
                resumeNode = "";
        }

        Debug.Log($"[NpcSequence] {name}: старт «{dialogue.dialogueName}» " +
                  $"({CurrentIndex() + 1}/{dialogues.Count}).", this);
        lastStarted = dialogue;
        DialogueManager.Instance.StartDialogue(dialogue, null, resumeNode);
        if (!DialogueManager.Instance.isDialogueActive)
            lastStarted = null;
    }

    /// <summary>Диалог закрылся: наш и пройден — тянем следующий, Esc — молчим.</summary>
    void OnManagerDialogueEnded()
    {
        if (lastStarted == null) return;
        DialogueData finished = lastStarted;
        lastStarted = null;
        DialogueManager m = cachedManager;
        if (m == null) m = DialogueManager.Instance;
        if (m != null && m.LastFinishedDialogue != null && m.LastFinishedDialogue != finished)
            return;
        // Esc — не тянем дальше.
        if (m != null && !m.LastFinishedCompleted && !DialogueManager.IsDialogueDone(finished))
            return;
        if (!DialogueManager.IsDialogueDone(finished)) return;
        if (!autoStartNext) return;
        if (CurrentDialogue() == null) return;
        if (autoStartDelay > 0f)
            StartCoroutine(AutoStartNext());
        else
            TryAutoStartNext();
    }

    System.Collections.IEnumerator AutoStartNext()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, autoStartDelay));
        TryAutoStartNext();
    }

    void TryAutoStartNext()
    {
        if (cachedManager == null || cachedManager.isDialogueActive) return;
        if (cachedPlayer == null)
            cachedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (cachedPlayer == null) return;
        // Игрок отошёл — не дёргаем, следующий предложится по E.
        if (Vector3.Distance(transform.position, cachedPlayer.transform.position) > interactDistance)
            return;
        DialogueData next = CurrentDialogue();
        if (next == null) return;
        StartCurrentDialogue(next);
    }

    void HideHint()
    {
        if (hintShownByUs && cachedManager != null && cachedManager.interactHint != null)
            cachedManager.interactHint.SetActive(false);
        hintShownByUs = false;
    }

    /// <summary>Сбросить прохождение всей цепочки (тесты / новая игра).</summary>
    [ContextMenu("Сбросить цепочку (пройти заново)")]
    public void ResetSequence()
    {
        if (dialogues == null) return;
        foreach (var d in dialogues)
        {
            if (d == null) continue;
            DialogueManager.ResetDialogue(d);
        }
        enabled = true;
        Debug.Log($"[NpcSequence] {name}: цепочка сброшена.", this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}
