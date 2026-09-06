using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI элементы")]
    public GameObject dialoguePanel;
    public TextMeshProUGUI speakerNameText;
    public Image speakerPortraitImage;
    public TextMeshProUGUI dialogueText;
    public GameObject choicesPanel;
    public Button choiceButtonPrefab;
    public Transform choicesContainer;
    public GameObject interactHint;
    public TextMeshProUGUI interactHintText;
    public CanvasGroup dialogueCanvasGroup;

    [Header("Глобальные настройки")]
    [Tooltip("Задержка между символами в секундах. Меньше = быстрее (0.01 = очень быстро)")]
    public float defaultTextSpeed = 0.02f;
    public KeyCode advanceKey = KeyCode.Space;
    public bool pauseGameDuringDialogue = true;
    public bool allowSkipTyping = true;
    public bool showCursorDuringDialogue = true;
    public KeyCode exitDialogueKey = KeyCode.Escape;

    [Header("Анимации")]
    public float fadeInDuration = 0.4f;
    public Vector3 panelStartScale = new Vector3(0.8f, 0.8f, 1f);
    public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Дополнительно")]
    [Tooltip("Задержка перед показом кнопок после завершения текста")]
    public float choicesDelay = 0.5f;
    [Tooltip("Показывать мигающий курсор в конце строки")]
    public bool showTypingCursor = true;
    [Tooltip("Символ курсора")]
    public string cursorSymbol = "_";
    [Tooltip("Скорость мигания курсора")]
    public float cursorBlinkSpeed = 0.5f;
    [Tooltip("Звук печати (необязательно)")]
    public AudioClip typingSound;
    [Range(0f, 1f)]
    public float typingSoundVolume = 0.3f;
    public int typingSoundFrequency = 3;

    private DialogueData currentDialogue;
    private DialogueNode currentNode;
    public bool isDialogueActive = false;
    private bool isTyping = false;
    private Coroutine typingCoroutine;
    private Coroutine autoAdvanceCoroutine;
    private DialogueTrigger currentTrigger;
    private bool previousCursorVisible;
    private CursorLockMode previousLockMode;
    private Coroutine fadeRoutine;
    private Coroutine cursorBlinkCoroutine;
    private bool isShowingChoices = false;

    // Сколько символов текста уже показано.
    private int revealedCharacters = 0;
    // Текст, содержащий rich-text теги, нельзя резать через Substring —
    // для него используется режим maxVisibleCharacters (курсор при этом не рисуется).
    private bool textHasRichTags = false;

    private AudioSource typingAudioSource;

    /// <summary>Идёт ли посимвольная печать прямо сейчас.</summary>
    public bool IsTyping => isTyping;
    /// <summary>Показаны ли варианты ответа.</summary>
    public bool IsShowingChoices => isShowingChoices;
    /// <summary>Текущий узел диалога (может быть null).</summary>
    public DialogueNode CurrentNode => currentNode;

    /// <summary>Диалог начался. Для кинематографики, звука, аналитики.</summary>
    public event System.Action OnDialogueStarted;
    /// <summary>Диалог закончился.</summary>
    public event System.Action OnDialogueEnded;
    /// <summary>Перешли к новому узлу.</summary>
    public event System.Action<DialogueNode> OnNodeChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
            if (dialogueCanvasGroup == null)
                dialogueCanvasGroup = dialoguePanel.GetComponent<CanvasGroup>();
            if (dialogueCanvasGroup == null)
                dialogueCanvasGroup = dialoguePanel.AddComponent<CanvasGroup>();
            dialogueCanvasGroup.alpha = 0f;
            dialoguePanel.transform.localScale = panelStartScale;
        }
        if (choicesPanel != null) choicesPanel.SetActive(false);
        if (interactHint != null) interactHint.SetActive(false);
    }

    void Update()
    {
        if (!isDialogueActive) return;

        if (Input.GetKeyDown(exitDialogueKey))
        {
            EndDialogue();
            return;
        }

        if (isTyping && allowSkipTyping && Input.GetKeyDown(advanceKey))
        {
            CompleteTyping();
        }
        else if (!isTyping && Input.GetKeyDown(advanceKey))
        {
            if (!isShowingChoices)
                AdvanceDialogue();
        }
    }

    public void StartDialogue(DialogueData dialogue, DialogueTrigger trigger)
    {
        if (isDialogueActive) return;

        // Останавливаем возможную старую анимацию fade out и мгновенно готовим панель
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (dialoguePanel != null && dialogueCanvasGroup != null)
        {
            dialoguePanel.SetActive(true);
            dialogueCanvasGroup.alpha = 0f;
            dialoguePanel.transform.localScale = panelStartScale;
        }

        // Курсор
        if (showCursorDuringDialogue)
        {
            previousCursorVisible = Cursor.visible;
            previousLockMode = Cursor.lockState;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        // Блокировка камеры
        var fpsController = FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
        if (fpsController != null)
            fpsController.enabled = false;

        currentDialogue = dialogue;
        currentTrigger = trigger;
        isDialogueActive = true;
        isShowingChoices = false;

        // Запускаем fade in
        if (dialoguePanel != null)
        {
            fadeRoutine = StartCoroutine(FadeAndScale(dialoguePanel.transform, dialogueCanvasGroup, Vector3.one, 1f, fadeInDuration, fadeInCurve));
        }

        if (interactHint != null) interactHint.SetActive(false);
        if (pauseGameDuringDialogue) Time.timeScale = 0f;

        OnDialogueStarted?.Invoke();

        MoveToNode(dialogue.GetStartNode());
    }

    public void EndDialogue()
    {
        if (!isDialogueActive) return;

        isDialogueActive = false;
        currentNode = null;
        currentDialogue = null;
        isShowingChoices = false;
        isTyping = false;
        revealedCharacters = 0;

        if (typingCoroutine != null) { StopCoroutine(typingCoroutine); typingCoroutine = null; }
        if (autoAdvanceCoroutine != null) { StopCoroutine(autoAdvanceCoroutine); autoAdvanceCoroutine = null; }
        if (cursorBlinkCoroutine != null) { StopCoroutine(cursorBlinkCoroutine); cursorBlinkCoroutine = null; }
        if (fadeRoutine != null) { StopCoroutine(fadeRoutine); fadeRoutine = null; }

        if (pauseGameDuringDialogue) Time.timeScale = 1f;

        // Восстановление курсора
        if (showCursorDuringDialogue)
        {
            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousLockMode;
        }

        // Разблокировка камеры
        var fpsController = FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
        if (fpsController != null)
            fpsController.enabled = true;

        // Мгновенно скрываем панель, чтобы не было мигания
        if (dialoguePanel != null)
        {
            dialoguePanel.SetActive(false);
            dialoguePanel.transform.localScale = panelStartScale;
            if (dialogueCanvasGroup != null)
                dialogueCanvasGroup.alpha = 0f;
        }
        if (choicesPanel != null) choicesPanel.SetActive(false);

        if (currentTrigger != null)
            currentTrigger.OnDialogueEnded();

        OnDialogueEnded?.Invoke();
    }

    void MoveToNode(DialogueNode node)
    {
        if (node == null)
        {
            EndDialogue();
            return;
        }

        currentNode = node;
        isShowingChoices = false;

        if (choicesPanel != null) choicesPanel.SetActive(false);

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }

        foreach (var cmd in node.onEnterCommands)
            cmd.Execute();

        node.onNodeEnter?.Invoke();
        OnNodeChanged?.Invoke(node);

        if (speakerNameText != null)
        {
            if (!string.IsNullOrEmpty(node.speakerName))
            {
                speakerNameText.text = node.speakerName;
                speakerNameText.gameObject.SetActive(true);
            }
            else
            {
                speakerNameText.gameObject.SetActive(false);
            }
        }

        if (speakerPortraitImage != null)
        {
            if (node.speakerPortrait != null)
            {
                speakerPortraitImage.sprite = node.speakerPortrait;
                speakerPortraitImage.gameObject.SetActive(true);
            }
            else
            {
                speakerPortraitImage.gameObject.SetActive(false);
            }
        }

        float speed = node.textSpeed > 0 ? node.textSpeed : defaultTextSpeed;
        if (typingCoroutine != null) StopCoroutine(typingCoroutine);
        typingCoroutine = StartCoroutine(TypeText(node.dialogueText, speed));

        if (autoAdvanceCoroutine != null) StopCoroutine(autoAdvanceCoroutine);
        if (node.autoAdvanceDelay > 0 && node.choices.Count == 0)
        {
            autoAdvanceCoroutine = StartCoroutine(AutoAdvance(node.autoAdvanceDelay));
        }
    }

    IEnumerator TypeText(string text, float speed)
    {
        isTyping = true;
        if (text == null) text = "";

        revealedCharacters = 0;
        textHasRichTags = text.IndexOf('<') >= 0;

        // Курсор несовместим с rich-text: обрезка строки посреди тега её ломает.
        bool useCursor = showTypingCursor && !string.IsNullOrEmpty(cursorSymbol) && !textHasRichTags;

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }

        if (textHasRichTags)
        {
            // Печатаем через maxVisibleCharacters — теги остаются целыми
            dialogueText.text = text;
            dialogueText.maxVisibleCharacters = 0;
        }
        else
        {
            dialogueText.text = "";
            dialogueText.maxVisibleCharacters = int.MaxValue;
        }

        int soundCounter = 0;
        int length = textHasRichTags ? GetVisibleLength(text) : text.Length;

        for (int i = 1; i <= length; i++)
        {
            revealedCharacters = i;

            if (textHasRichTags) dialogueText.maxVisibleCharacters = i;
            else dialogueText.text = text.Substring(0, i);

            if (typingSound != null && ++soundCounter % Mathf.Max(1, typingSoundFrequency) == 0)
                PlayTypingSound();

            yield return new WaitForSecondsRealtime(speed);
        }

        revealedCharacters = length;

        if (textHasRichTags)
        {
            dialogueText.maxVisibleCharacters = length;
        }
        else
        {
            dialogueText.text = text;
            dialogueText.maxVisibleCharacters = int.MaxValue;
        }

        isTyping = false;
        typingCoroutine = null;

        // Мигающий курсор в конце реплики — знак «жми пробел»
        if (useCursor)
            cursorBlinkCoroutine = StartCoroutine(BlinkCursor());

        if (currentNode != null && currentNode.choices.Count > 0)
        {
            yield return new WaitForSecondsRealtime(choicesDelay);
            if (isDialogueActive && !isTyping)
                ShowChoices();
        }
    }

    /// <summary>Число печатаемых символов без учёта rich-text тегов.</summary>
    static int GetVisibleLength(string text)
    {
        int count = 0;
        bool inTag = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) count++;
        }
        return count;
    }

    void PlayTypingSound()
    {
        if (typingSound == null) return;

        if (typingAudioSource == null)
        {
            typingAudioSource = GetComponent<AudioSource>();
            if (typingAudioSource == null)
            {
                typingAudioSource = gameObject.AddComponent<AudioSource>();
                typingAudioSource.playOnAwake = false;
                typingAudioSource.spatialBlend = 0f;
            }
        }

        // Легкий разброс тона — печать перестаёт звучать механически
        typingAudioSource.pitch = Random.Range(0.94f, 1.06f);
        typingAudioSource.PlayOneShot(typingSound, typingSoundVolume);
    }

    IEnumerator BlinkCursor()
    {
        // Курсор дописывается к уже показанному тексту, а не вырезается из него —
        // поэтому отрицательная длина в Substring больше невозможна.
        string baseText = currentNode != null ? currentNode.dialogueText : "";
        if (baseText == null) baseText = "";

        int safeCount = Mathf.Clamp(revealedCharacters, 0, baseText.Length);
        string shown = baseText.Substring(0, safeCount);

        bool show = true;
        while (true)
        {
            if (dialogueText == null) yield break;

            dialogueText.text = show ? shown + cursorSymbol : shown;
            dialogueText.maxVisibleCharacters = int.MaxValue;
            show = !show;

            yield return new WaitForSecondsRealtime(cursorBlinkSpeed);
        }
    }

    void CompleteTyping()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }
        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
        }

        if (currentNode == null) return;

        string full = currentNode.dialogueText ?? "";
        dialogueText.text = full;
        dialogueText.maxVisibleCharacters = int.MaxValue;
        revealedCharacters = full.Length;
        isTyping = false;

        if (showTypingCursor && !string.IsNullOrEmpty(cursorSymbol) && !textHasRichTags)
            cursorBlinkCoroutine = StartCoroutine(BlinkCursor());

        if (currentNode.choices.Count > 0 && !isShowingChoices)
            StartCoroutine(ShowChoicesAfterDelay(0f));
    }

    void AdvanceDialogue()
    {
        if (isTyping) return;
        if (isShowingChoices) return;
        if (currentNode == null) { EndDialogue(); return; }

        if (currentNode.choices != null && currentNode.choices.Count > 0)
        {
            ShowChoices();
            return;
        }

        if (autoAdvanceCoroutine != null) return;

        if (!string.IsNullOrEmpty(currentNode.nextNodeID))
        {
            MoveToNode(currentDialogue.GetNodeByID(currentNode.nextNodeID));
        }
        else
        {
            EndDialogue();
        }
    }

    IEnumerator AutoAdvance(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (isDialogueActive && !isTyping && !isShowingChoices)
        {
            AdvanceDialogue();
        }
        autoAdvanceCoroutine = null;
    }

    IEnumerator ShowChoicesAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (isDialogueActive && !isTyping)
            ShowChoices();
    }

    void ShowChoices()
    {
        if (isShowingChoices) return;
        if (currentNode == null || choicesContainer == null || choiceButtonPrefab == null) return;
        isShowingChoices = true;

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
            if (dialogueText != null && currentNode != null)
                dialogueText.text = currentNode.dialogueText;
        }

        if (choicesPanel != null) choicesPanel.SetActive(true);

        foreach (Transform child in choicesContainer)
            Destroy(child.gameObject);

        foreach (var choice in currentNode.choices)
        {
            if (choice.condition != null && !choice.condition.Evaluate())
                continue;

            Button button = Instantiate(choiceButtonPrefab, choicesContainer);
            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
                buttonText.text = choice.choiceText;

            DialogueChoice capturedChoice = choice;
            button.onClick.AddListener(() => OnChoiceSelected(capturedChoice));
        }

        if (choicesContainer.childCount == 0)
        {
            if (choicesPanel != null) choicesPanel.SetActive(false);
            isShowingChoices = false;
            EndDialogue();
        }
    }

    void OnChoiceSelected(DialogueChoice choice)
    {
        if (choicesPanel != null) choicesPanel.SetActive(false);
        isShowingChoices = false;

        foreach (var cmd in choice.onSelectCommands)
            cmd.Execute();

        choice.onSelected?.Invoke();

        if (choice.endDialogue)
        {
            EndDialogue();
            return;
        }

        if (!string.IsNullOrEmpty(choice.nextNodeID))
        {
            MoveToNode(currentDialogue.GetNodeByID(choice.nextNodeID));
        }
        else
        {
            EndDialogue();
        }
    }

    public void SetBackground(Sprite bg)
    {
        // Реализуйте свой способ установки фона
    }

    IEnumerator FadeAndScale(Transform target, CanvasGroup cg, Vector3 targetScale, float targetAlpha, float duration, AnimationCurve curve, System.Action onComplete = null)
    {
        float elapsed = 0f;
        Vector3 startScale = target.localScale;
        float startAlpha = cg.alpha;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curvedT = curve.Evaluate(t);

            target.localScale = Vector3.Lerp(startScale, targetScale, curvedT);
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, curvedT);
            yield return null;
        }

        target.localScale = targetScale;
        cg.alpha = targetAlpha;
        onComplete?.Invoke();
    }
}