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

    private bool playerInRange = false;
    private bool isDialogueActive = false;
    private GameObject cachedPlayer;
    private DialogueManager cachedManager;

    void Start()
    {
        cachedManager = DialogueManager.Instance;
        if (cachedManager == null)
            cachedManager = FindObjectOfType<DialogueManager>();
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

        float dist = Vector3.Distance(transform.position, cachedPlayer.transform.position);
        playerInRange = dist <= interactDistance;

        if (cachedManager.interactHint != null)
        {
            if (playerInRange && !isDialogueActive && !cachedManager.isDialogueActive && (!playOnce || !hasPlayed))
            {
                cachedManager.interactHint.SetActive(true);
                if (cachedManager.interactHintText != null)
                    cachedManager.interactHintText.text = interactMessage;
            }
            else
            {
                cachedManager.interactHint.SetActive(false);
            }
        }

        if (playerInRange && !isDialogueActive && Input.GetKeyDown(interactKey))
        {
            if (!playOnce || !hasPlayed)
            {
                StartDialogue();
            }
        }
    }

    void StartDialogue()
    {
        if (dialogue == null)
        {
            Debug.LogWarning($"[DialogueTrigger] {name}: dialogue = null.", this);
            return;
        }
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
        DialogueManager.Instance.StartDialogue(dialogue, this);
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