using UnityEngine;

public class DialogueTrigger : MonoBehaviour
{
    [Header("Диалог")]
    public DialogueData dialogue;
    public bool playOnce = false;
    private bool hasPlayed = false;

    [Header("Взаимодействие")]
    public string interactMessage = "Нажмите E, чтобы говорить";
    public float interactDistance = 3f;
    public KeyCode interactKey = KeyCode.E;
    [Tooltip("Диалог только при прямой видимости (без стен между). " +
             "Выключи, если НПС стоит за низким забором, который перекрывает луч.")]
    public bool requireLineOfSight = true;
    [Tooltip("Что считается стеной. Триггеры игнорятся всегда.")]
    public LayerMask losBlockMask = ~0;

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
    private float nextPlayerWarnTime = 0f;

    void Start()
    {
        // На объекте уже есть цепочка — триггер гасится, чтобы не драться за E.
        NpcDialogueSequence seq = GetComponent<NpcDialogueSequence>();
        if (seq != null && seq.enabled)
        {
            Debug.LogWarning($"[DialogueTrigger] {name}: рядом NpcDialogueSequence — триггер выключен, " +
                             "чтобы не драться за E. Удали лишний триггер.", this);
            enabled = false;
            return;
        }

        cachedManager = DialogueManager.Instance;
        if (cachedManager == null)
            cachedManager = FindObjectOfType<DialogueManager>();

        // Прохождение переживает перезапуск.
        if (playOnce && dialogue != null && DialogueManager.IsDialogueDone(dialogue))
        {
            hasPlayed = true;
            enabled = false;
        }
    }

    /// <summary>Доступен ли диалог: предшественник пройден + квест сошёлся.</summary>
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
        {
            try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
            catch { cachedPlayer = null; }
        }
        if (cachedPlayer == null)
        {
            if (Time.time >= nextPlayerWarnTime)
            {
                nextPlayerWarnTime = Time.time + 5f;
                Debug.LogWarning($"[DialogueTrigger] {name}: игрок с тегом «Player» не найден — E не сработает. Поставь тег на игрока.", this);
            }
            return;
        }

        // Открыт инвентарь — разговор не предлагаем.
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen)
        {
            if (hintShownByUs && cachedManager.interactHint != null)
            {
                cachedManager.interactHint.SetActive(false);
                hintShownByUs = false;
            }
            return;
        }

        float dist = Vector3.Distance(transform.position, cachedPlayer.transform.position);
        playerInRange = dist <= interactDistance;
        bool visible = !requireLineOfSight || DialogueManager.HasLineOfSight(
            transform.position + Vector3.up * 1.6f, cachedPlayer, gameObject, losBlockMask);

        if (cachedManager.interactHint != null)
        {
            if (playerInRange && visible && !isDialogueActive && !cachedManager.isDialogueActive && (!playOnce || !hasPlayed))
            {
                cachedManager.interactHint.SetActive(true);
                hintShownByUs = true;
                if (cachedManager.interactHintText != null)
                    cachedManager.interactHintText.text =
                        interactMessage + DialogueManager.GetQuestGateHint(dialogue);
            }
            else if (hintShownByUs)
            {
                cachedManager.interactHint.SetActive(false);
                hintShownByUs = false;
            }
        }

        if (playerInRange && visible && !isDialogueActive && Input.GetKeyDown(interactKey))
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
        if (dialogue.nodes == null || dialogue.nodes.Count == 0)
        {
            Debug.LogError($"[DialogueTrigger] {name}: диалог «{dialogue.dialogueName}» пуст " +
                           $"(0 узлов, ассет {dialogue.name}) — E откроет пустоту. Проверь ассет в папке Dialog.", this);
            return;
        }
        if (!IsAvailable()) return; // цепочка/квест не сошлись
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
        // Инвентарь и диалог не совмещаем.
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen)
            return;

        isDialogueActive = true;
        Debug.Log($"[DialogueTrigger] {name}: старт диалога «{dialogue.dialogueName}» " +
                  $"(узелков: {dialogue.nodes?.Count ?? -1}).", this);
        string resumeNode = resumeFromSave ? DialogueManager.GetSavedNodeID(dialogue) : "";
        if (!string.IsNullOrEmpty(resumeNode) && dialogue.GetNodeByID(resumeNode) == null)
            resumeNode = "";
        DialogueManager.Instance.StartDialogue(dialogue, this, resumeNode);
        if (!DialogueManager.Instance.isDialogueActive)
        {
            // Менеджер не взял диалог (нет UI и т.п.) — не считаем сыгранным,
            // иначе с playOnce триггер умрёт, так и не показав разговор.
            isDialogueActive = false;
            return;
        }
        if (playOnce)
            hasPlayed = true;
    }

    public void OnDialogueEnded()
    {
        isDialogueActive = false;
        // Гасим одноразовый триггер только если диалог реально прошли
        // (done/completed). Выход по Esc оставляет триггер живым — иначе
        // разговор нельзя продолжить, а соседний триггер/D2 выглядит как
        // «перескок на второй диалог».
        if (!playOnce || !hasPlayed)
            return;
        DialogueManager m = DialogueManager.Instance;
        bool completed = m != null ? m.LastFinishedCompleted : true;
        bool done = dialogue != null && DialogueManager.IsDialogueDone(dialogue);
        if (completed || done)
            enabled = false;
        else
            hasPlayed = false;
    }

    /// <summary>Сбросить прогресс этого диалога (тесты: следующий разговор с начала).</summary>
    [ContextMenu("Сбросить прогресс диалога")]
    public void ResetProgress()
    {
        if (dialogue != null)
            DialogueManager.ResetDialogue(dialogue);
        hasPlayed = false;
        isDialogueActive = false;
        enabled = true;
        Debug.Log($"[DialogueTrigger] {name}: прогресс диалога сброшен.", this);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}