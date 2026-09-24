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
        // На одном NPC должен рулить кто-то один: если рядом висит NpcDialogueSequence
        // (1 NPC = все диалоги 1->2->3), триггер гасится сам — иначе оба дерутся за E
        // и подсказку, а повторное E может перезапустить D1 вместо D2.
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

        // Открыт инвентарь — разговор не предлагаем и не стартуем (см. StartDialogue).
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
        // Стена между (игрок в доме, НПС на улице): подсказки нет, E молчит.
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
        // Открытый инвентарь и диалог не совмещаем: иначе менеджер включит
        // контроллер при закрытии диалога, а сумка ещё открыта — игрок пойдёт с инвентарём.
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen)
            return;

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