using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [Header("Диалог")]
    public DialogueData dialogue;
    public bool playOnce = false;
    private bool hasPlayed = false;

    [Header("Взаимодействие")]
    public string interactMessage = "Нажмите E для разговора";
    public float interactDistance = 3f;
    public KeyCode interactKey = KeyCode.E;

    [Header("Сохранение прогресса")]
    [Tooltip("Продолжать с последнего узла, если игрок вышел из диалога досрочно (Esc/Отмена).")]
    public bool resumeFromSave = true;
    [Tooltip("Не предлагать диалог заново, если он уже пройден до конца и сохранён.")]
    public bool hideWhenDone = false;

    [Header("Цепочка (несколько диалогов на одном NPC)")]
    [Tooltip("Предшественник: этот диалог молчит, пока указанный не пройден до конца. " +
             "Так вешается «1 закончился — доступен 2-й» на одного персонажа: " +
             "два триггера рядом, у второго здесь выбран первый диалог.")]
    public DialogueData requireDialogueDone;
    [Tooltip("Квест-условие для доступности (например q_ammo). Пусто — не проверять.")]
    public string requireQuestId = "";
    [Tooltip("0 — взят (активен или выполнен), 1 — активен, 2 — выполнен, 3 — провален.")]
    public int requireQuestState = 1;

    private bool playerInRange = false;
    private bool isDialogueActive = false;
    private bool hintShownByUs = false;
    private GameObject cachedPlayer;
    private DialogueManager cachedManager;

    void Start()
    {
        cachedManager = DialogueManager.Instance;
        if (cachedManager == null)
            cachedManager = FindObjectOfType<DialogueManager>();

        // Прохождение переживает перезапуск: playOnce-триггер не оживает
        if (playOnce && dialogue != null && DialogueManager.IsDialogueDone(dialogue))
        {
            hasPlayed = true;
            enabled = false;
        }
    }

    /// <summary>
    /// Доступен ли диалог прямо сейчас: цепочка (предшественник пройден?)
    /// и квест-условие. Недоступный триггер ведёт себя так, будто игрока рядом нет:
    /// ни подсказки, ни запуска. Так второй диалог на том же NPC «ждёт» первый.
    /// </summary>
    public bool IsAvailable()
    {
        if (requireDialogueDone != null && !DialogueManager.IsDialogueDone(requireDialogueDone))
            return false;
        if (!string.IsNullOrEmpty(requireQuestId) &&
            !DialogueManager.IsQuestStateMatch(requireQuestId, requireQuestState))
            return false;
        return true;
    }

    void Update()
    {
        if (!IsAvailable())
        {
            // Цепочка не сошлась: прячем чужую подсказку, если она висела от нас.
            if (hintShownByUs && cachedManager != null && cachedManager.interactHint != null)
            {
                cachedManager.interactHint.SetActive(false);
                hintShownByUs = false;
            }
            return;
        }

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

        float dist = Vector3.Distance(transform.position, cachedPlayer.transform.position);
        playerInRange = dist <= interactDistance;

        if (cachedManager.interactHint != null)
        {
            if (playerInRange && !isDialogueActive && !cachedManager.isDialogueActive && (!playOnce || !hasPlayed))
            {
                cachedManager.interactHint.SetActive(true);
                hintShownByUs = true;
                if (cachedManager.interactHintText != null)
                    cachedManager.interactHintText.text = interactMessage;
            }
            else if (hintShownByUs)
            {
                cachedManager.interactHint.SetActive(false);
                hintShownByUs = false;
            }
        }

        if (playerInRange && !isDialogueActive && Input.GetKeyDown(interactKey))
        {
            if (!playOnce || !hasPlayed)
            {
                if (hideWhenDone && dialogue != null && DialogueManager.IsDialogueDone(dialogue))
                    return;
                StartDialogue();
            }
        }
    }

    void StartDialogue()
    {        if (dialogue == null)
        {
            Debug.LogWarning($"[DialogueTrigger] {name}: dialogue = null.", this);
            return;
        }
        if (!IsAvailable()) return; // цепочка/квест не сошлись — молчим
        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning($"[DialogueTrigger] {name}: DialogueManager.Instance = null.", this);
            return;
        }
        if (DialogueManager.Instance.isDialogueActive)
        {
            Debug.LogWarning($"[DialogueTrigger] {name}: менеджер уже занят другим диалогом.", this);
            return;
        }

        isDialogueActive = true;
        hasPlayed = true;
        Debug.Log($"[DialogueTrigger] {name}: старт диалога «{dialogue.dialogueName}» " +
                  $"(узелков: {dialogue.nodes?.Count ?? -1}).", this);
        string resumeNode = resumeFromSave ? DialogueManager.GetSavedNodeID(dialogue) : "";
        if (!string.IsNullOrEmpty(resumeNode) && dialogue.GetNodeByID(resumeNode) == null)
            resumeNode = "";
        DialogueManager.Instance.StartDialogue(dialogue, this, resumeNode);
    }

    public void OnDialogueEnded()
    {
        isDialogueActive = false;
        if (playOnce && hasPlayed)
            enabled = false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}