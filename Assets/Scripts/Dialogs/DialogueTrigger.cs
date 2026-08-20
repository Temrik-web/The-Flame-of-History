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

    void Update()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.transform.position);
        playerInRange = dist <= interactDistance;

        if (DialogueManager.Instance != null && DialogueManager.Instance.interactHint != null)
        {
            if (playerInRange && !isDialogueActive && (!playOnce || !hasPlayed))
            {
                DialogueManager.Instance.interactHint.SetActive(true);
                if (DialogueManager.Instance.interactHintText != null)
                    DialogueManager.Instance.interactHintText.text = interactMessage;
            }
            else
            {
                DialogueManager.Instance.interactHint.SetActive(false);
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
        if (dialogue == null || DialogueManager.Instance == null) return;
        isDialogueActive = true;
        hasPlayed = true;
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