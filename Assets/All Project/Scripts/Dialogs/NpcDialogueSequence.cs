using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Цепочка диалогов на ОДНОМ кубе НПС (например Степан: D1 -> D2 -> ... -> D8).
///
/// Как работает:
///  - в список dialogues положи ассеты по порядку (D1_Znakomstvo ... D8_Proschanie);
///  - куб каждый кадр ищет ПЕРВЫЙ непройденный диалог (DialogueManager.IsDialogueDone)
///    и предлагает его по E;
///  - диалог закончился (completed=true) -> менеджер сам пометил его done,
///    значит в следующий раз предложится уже СЛЕДУЮЩИЙ по списку;
///  - вышел досрочно (Esc) -> прогресс внутри диалога сохранён (resumeFromSave),
///    предложится ТОТ ЖЕ диалог с того же узла.
///
/// Зачем вместо 8 кубов: один НПС = один куб, порядок гарантирован,
///  игроку не надо искать «какой куб следующий».
/// Старые DialogueTrigger на отдельных кубах после переезда можно выключить/удалить.
///
/// Сохранения: прохождение диалогов лежит в PlayerPrefs (flame_dlg_done_*),
///  переживает перезапуск. Сброс — кнопка в контекстном меню.
/// </summary>
[DisallowMultipleComponent]
public class NpcDialogueSequence : MonoBehaviour
{
    [Header("Диалоги по порядку")]
    [Tooltip("Положи D1..D8 по порядку. Первый непройденный предложится по E.")]
    public List<DialogueData> dialogues = new List<DialogueData>();

    [Header("Взаимодействие")]
    public string interactMessage = "Нажмите E для разговора";
    public float interactDistance = 3f;
    public KeyCode interactKey = KeyCode.E;

    [Header("Поведение")]
    [Tooltip("Продолжать диалог с сохранённого узла, если вышли досрочно (Esc).")]
    public bool resumeFromSave = true;
    [Tooltip("Молчать, когда вся цепочка пройдена (иначе повторять последний).")]
    public bool hideWhenAllDone = true;
    [Tooltip("Показывать в подсказке номер беседы: «Поговорить (2/8)».")]
    public bool showCounterInHint = true;
    [Tooltip("Сразу запускать следующий диалог цепочки, когда предыдущий " +
             "пройден до конца. Вышел по Esc — не запускаем, висит «продолжить».")]
    public bool autoStartNext = true;
    [Tooltip("Пауза перед автозапуском следующего (сек, реального времени).")]
    public float autoStartDelay = 0.6f;

    GameObject cachedPlayer;
    DialogueManager cachedManager;
    bool hintShownByUs;
    DialogueData lastStarted;

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
        if (m == null)
            m = FindObjectOfType<DialogueManager>();
        if (m != null && m != cachedManager)
        {
            if (cachedManager != null)
                cachedManager.OnDialogueEnded -= OnManagerDialogueEnded;
            cachedManager = m;
            cachedManager.OnDialogueEnded += OnManagerDialogueEnded;
        }
        // Диагностика связок: сразу видно пустой ассет (узлов 0) до нажатия E.
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
            cachedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (cachedPlayer == null) return;

        // Менеджер занят чужим диалогом — прячем свою подсказку и ждём.
        if (cachedManager.isDialogueActive)
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

        if (cachedManager.interactHint != null)
        {
            if (inRange)
            {
                string msg = interactMessage;
                if (showCounterInHint && dialogues != null && dialogues.Count > 1)
                {
                    int idx = CurrentIndex();
                    if (idx >= 0)
                        msg += $" ({idx + 1}/{dialogues.Count})";
                }
                // Диалог бросили на середине (Esc) — честно пишем, что продолжим,
                // а не начнём сначала (прогресс лежит в flame_dlg_node_*).
                if (resumeFromSave && !string.IsNullOrEmpty(DialogueManager.GetSavedNodeID(current)))
                    msg += " — продолжить";
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

        if (inRange && Input.GetKeyDown(interactKey))
            StartCurrentDialogue(current);
    }

    void StartCurrentDialogue(DialogueData dialogue)
    {
        if (dialogue == null) return;
        if (DialogueManager.Instance == null) return;
        if (DialogueManager.Instance.isDialogueActive) return;

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
        // Триггер не передаём (null): EndDialogue переживёт null-триггер,
        // а done-флаг менеджер выставит сам. Цепочка движется по done-флагам.
        DialogueManager.Instance.StartDialogue(dialogue, null, resumeNode);
    }

    /// <summary>
    /// Диалог закрылся. Если это был НАШ и он пройден до конца (done) —
    /// тянем следующий по цепочке сами, жать E не надо.
    /// Вышел по Esc (не done) — молчим, в подсказке будет «продолжить».
    /// </summary>
    void OnManagerDialogueEnded()
    {
        if (lastStarted == null) return;
        DialogueData finished = lastStarted;
        lastStarted = null;
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
