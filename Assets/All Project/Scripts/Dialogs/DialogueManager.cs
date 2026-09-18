using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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

    [Header("Свои кнопки (канвас пользователя)")]
    [Tooltip("Включено — варианты показываются на готовых кнопках сцены (Button1..3), " +
             "а не создаются из шаблона.")]
    public bool useStaticChoiceButtons = false;
    [Tooltip("Кнопки вариантов в порядке Button1, Button2, Button3. Лишние прячутся сами.")]
    public Button[] staticChoiceButtons = new Button[0];
    [Tooltip("Подписи кнопок (тексты 1, 2, 3 внутри). Если пусто — найдутся сами.")]
    public TextMeshProUGUI[] staticChoiceLabels = new TextMeshProUGUI[0];
    [Tooltip("Спрайт заблокированного варианта (UI5). Пусто — обычный спрайт + серый цвет.")]
    public Sprite lockedChoiceSprite;
    [Tooltip("Цвет подписи заблокированного варианта.")]
    public Color lockedChoiceLabelColor = new Color(0.55f, 0.55f, 0.6f, 0.8f);
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

    // Исходные цвета подписей кнопок — чтобы вернуть их после разблокировки.
    private readonly System.Collections.Generic.Dictionary<TextMeshProUGUI, Color> labelBaseColors =
        new System.Collections.Generic.Dictionary<TextMeshProUGUI, Color>();

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
    /// <summary>Текущий диалог (может быть null).</summary>
    public DialogueData CurrentDialogue => currentDialogue;

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
        GameState.Load();
        QuestSystem.Load();
    }

    void OnApplicationQuit()
    {
        GameState.Save();
        QuestSystem.Save();
        PlayerPrefs.Save();
    }

    // =====================================================================
    // Сохранение прогресса: последний узел + признак прохождения (PlayerPrefs).
    // =====================================================================
    const string ProgressPrefix = "flame_dlg_";

    /// <summary>
    /// Гарантия интерфейса перед стартом: привязки нет — ищем канвас,
    /// адаптер есть, но не привязал — повторяем поиск.
    /// </summary>
    void EnsureUserInterface()
    {
        if (dialogueText != null) return;

        UserDialogueUI ui = ExistingAdapter();
        if (ui == null)
        {
            TryAutoWireUserCanvas();
            return;
        }
        ui.RetryWire();
    }

    /// <summary>Адаптер в сцене (включая скрытые и выключенные).</summary>
    static UserDialogueUI ExistingAdapter()
    {
        foreach (UserDialogueUI ui in FindObjectsOfType<UserDialogueUI>(true))
        {
            if (ui != null) return ui;
        }
        return null;
    }

    /// <summary>
    /// Сам находит канвас пользователя (ищет кнопку Button1, включая скрытые)
    /// и вешает на него адаптер. Работает без единого клика в редакторе.
    /// </summary>
    void TryAutoWireUserCanvas()
    {
        if (dialogueText != null) return;
        if (ExistingAdapter() != null) return;

        Transform btn = FindInSceneByName("Button1");
        if (btn == null) return;

        // Хост — самый верхний предок кнопки (канвас), НИКОГДА сама кнопка:
        // иначе прятанье окна спрячет кнопку, а проверки её потом не найдут
        Transform top = btn;
        while (top.parent != null) top = top.parent;

        if (top.GetComponent<UserDialogueUI>() != null) return;
        top.gameObject.AddComponent<UserDialogueUI>(); // Awake адаптера всё найдёт и спрячет окно
        Debug.Log($"[DialogueManager] Нашёл твой канвас ({top.gameObject.name}) — адаптер подключён сам.", this);

        // Убираем остатки моего старого канваса, чтобы не мешался
        foreach (Canvas c in FindObjectsOfType<Canvas>(true))
        {
            if (c != null && c.gameObject.name == "DialogueCanvas" && c.gameObject != top.gameObject)
                c.gameObject.SetActive(false);
        }
    }

    static Transform FindInSceneByName(string name)
    {
        foreach (Transform t in FindObjectsOfType<Transform>(true))
        {
            if (t != null && t.name == name) return t;
        }
        return null;
    }

    /// <summary>Спрятать фиксированные кнопки поштучно (панель целиком не трогаем).</summary>
    void HideStaticButtons()
    {
        ClearSelection();
        if (staticChoiceButtons == null) return;
        foreach (Button b in staticChoiceButtons)
        {
            if (b != null && b.gameObject.activeSelf)
                b.gameObject.SetActive(false);
        }
    }

    static string DialogueKey(DialogueData dialogue)
    {
        if (dialogue == null) return "";
        // Имя ассета стабильнее русского dialogueName
        return string.IsNullOrEmpty(dialogue.name) ? dialogue.dialogueName : dialogue.name;
    }

    /// <summary>Узел, на котором остановились в прошлый раз ("" — нет сохранения).</summary>
    public static string GetSavedNodeID(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return "";
        return PlayerPrefs.GetString(ProgressPrefix + "node_" + key, "");
    }

    public static bool IsDialogueDone(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return false;
        return PlayerPrefs.GetInt(ProgressPrefix + "done_" + key, 0) == 1;
    }

    public static void MarkDialogueDone(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.SetString(ProgressPrefix + "node_" + key, "");
        PlayerPrefs.SetInt(ProgressPrefix + "done_" + key, 1);
        PlayerPrefs.Save();
    }

    static void SaveDialogueProgress(DialogueData dialogue, string nodeID)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(nodeID)) return;
        PlayerPrefs.SetString(ProgressPrefix + "node_" + key, nodeID);
        PlayerPrefs.Save();
    }

    static void ClearDialogueProgress(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.SetString(ProgressPrefix + "node_" + key, "");
        PlayerPrefs.Save();
    }

    /// <summary>Сбросить прогресс и прохождение одного диалога (для тестов/новой игры).</summary>
    public static void ResetDialogue(DialogueData dialogue)
    {
        string key = DialogueKey(dialogue);
        if (string.IsNullOrEmpty(key)) return;
        PlayerPrefs.DeleteKey(ProgressPrefix + "node_" + key);
        PlayerPrefs.DeleteKey(ProgressPrefix + "done_" + key);
        PlayerPrefs.Save();
    }

    // =====================================================================
    // Говорящие: цвет имени + портрет (или заглушка, пока нет арта).
    // =====================================================================
    /// <summary>Цвет имени: явный из узла или авто-палитра по имени.</summary>
    public static Color ResolveSpeakerColor(DialogueNode node)
    {
        if (node == null) return Color.white;
        if (node.speakerColor.a > 0.01f) return node.speakerColor;
        return SpeakerPortrait.GetSpeakerColor(node.speakerName);
    }

    /// <summary>
    /// Совпал ли статус квеста с требованием выбора:
    /// 0 — взят (активен или выполнен), 1 — активен, 2 — выполнен, 3 — провален.
    /// </summary>
    public static bool IsQuestStateMatch(string questId, int requiredState)
    {
        if (string.IsNullOrEmpty(questId)) return true;
        int s = QuestSystem.GetState(questId);
        if (requiredState == 0) return s == QuestSystem.StateActive || s == QuestSystem.StateDone;
        return s == requiredState;
    }

    /// <summary>Портрет: спрайт из узла или круглая заглушка в цвете персонажа.</summary>
    public static Sprite ResolveSpeakerPortrait(DialogueNode node)
    {
        if (node == null) return null;
        if (node.speakerPortrait != null) return node.speakerPortrait;
        if (string.IsNullOrEmpty(node.speakerName)) return null;
        return SpeakerPortrait.GetPlaceholder(node.speakerName);
    }

    void Start()
    {
        // Сцена уже загружена целиком — тут поиск канваса надёжен (в Awake рано)
        TryAutoWireUserCanvas();

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
            EndDialogue(false);
            return;
        }

        // Пока показаны варианты — пробел и продвижение не работают:
        // выбор делается только мышкой (клик по кнопке).
        if (isShowingChoices) return;

        if (isTyping && allowSkipTyping && Input.GetKeyDown(advanceKey))
        {
            CompleteTyping();
        }
        else if (!isTyping && Input.GetKeyDown(advanceKey))
        {
            AdvanceDialogue();
        }
    }

    /// <summary>
    /// Сбросить фокус EventSystem: иначе кликнутая кнопка остаётся выбранной
    /// и следующий пробел/Enter срабатывает как клик по ней (скип выбора).
    /// </summary>
    static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    /// <summary>Запретить кнопке фокус с клавиатуры: выбор только мышкой.</summary>
    static void DisableKeyboardFocus(Button button)
    {
        if (button == null) return;
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
    }

    /// <summary>
    /// Начать диалог. startNodeID — продолжить с узла (загрузка прогресса);
    /// пусто/не найден — с начала.
    /// </summary>
    public void StartDialogue(DialogueData dialogue, DialogueTrigger trigger, string startNodeID = null)
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
        HideStaticButtons();

        // Вдруг канвас появился позже / привязка не взлетела — чиним прямо сейчас
        EnsureUserInterface();

        if (dialogueText == null)
            Debug.LogError("[DialogueManager] Нет текста реплики (dialogueText пуст): канвас не открывается. " +
                "Выдели свой канвас в Hierarchy и нажми Tools -> Диалоги -> Подключить мой канвас.", this);

        var start = !string.IsNullOrEmpty(startNodeID) ? dialogue.GetNodeByID(startNodeID) : null;
        if (start == null) start = dialogue.GetStartNode();
        if (start != null && !string.IsNullOrEmpty(startNodeID) && start.nodeID == startNodeID)
            Debug.Log($"[DialogueManager] Продолжаем «{dialogue.dialogueName}» с узла {start.nodeID} (сейв).", this);

        if (dialogueCanvasGroup == null && dialoguePanel != null)
            dialogueCanvasGroup = dialoguePanel.GetComponent<CanvasGroup>();
        if (dialogueCanvasGroup == null && dialoguePanel != null)
            dialogueCanvasGroup = dialoguePanel.AddComponent<CanvasGroup>();

        if (dialoguePanel != null && dialogueCanvasGroup != null)
        {
            fadeRoutine = StartCoroutine(FadeAndScale(dialoguePanel.transform, dialogueCanvasGroup, Vector3.one, 1f, fadeInDuration, fadeInCurve));
        }

        if (interactHint != null) interactHint.SetActive(false);
        if (pauseGameDuringDialogue) Time.timeScale = 0f;

        OnDialogueStarted?.Invoke();

        MoveToNode(start);
    }

    /// <summary>
    /// Закончить диалог. completed=true — прошли до конца (прогресс стираем,
    /// диалог помечаем пройденным); false — вышли вручную (Esc/Отмена),
    /// прогресс остаётся и в следующий раз предложим продолжить.
    /// </summary>
    public void EndDialogue(bool completed = true)
    {
        if (!isDialogueActive) return;

        DialogueData finishedDialogue = currentDialogue;
        DialogueNode lastNode = currentNode;

        isDialogueActive = false;
        currentNode = null;
        currentDialogue = null;
        isShowingChoices = false;
        isTyping = false;
        revealedCharacters = 0;
        ClearSelection();

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
        HideStaticButtons();

        if (currentTrigger != null)
            currentTrigger.OnDialogueEnded();

        OnDialogueEnded?.Invoke();

        if (finishedDialogue != null)
        {
            if (completed)
            {
                ClearDialogueProgress(finishedDialogue);
                MarkDialogueDone(finishedDialogue);
            }
            else if (lastNode != null)
            {
                SaveDialogueProgress(finishedDialogue, lastNode.nodeID);
            }
            GameState.Save();
            QuestSystem.Save();
        }
    }

    void MoveToNode(DialogueNode node)
    {
        if (node == null)
        {
            Debug.LogWarning("[DialogueManager] MoveToNode: узел null — диалог завершён досрочно.", this);
            EndDialogue();
            return;
        }

        currentNode = node;
        isShowingChoices = false;
        HideStaticButtons();

        if (currentDialogue != null)
            SaveDialogueProgress(currentDialogue, node.nodeID);

        // История (бэклог на H): кто что сказал, переживает перезапуск
        DialogueHistory history = GetComponent<DialogueHistory>();
        if (history == null) history = gameObject.AddComponent<DialogueHistory>();
        history.Record(DialogueKey(currentDialogue), node.speakerName, node.dialogueText, ResolveSpeakerColor(node));

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
                speakerNameText.color = ResolveSpeakerColor(node);
                speakerNameText.gameObject.SetActive(true);
            }
            else
            {
                speakerNameText.gameObject.SetActive(false);
            }
        }

        if (speakerPortraitImage != null)
        {
            Sprite portrait = ResolveSpeakerPortrait(node);
            if (portrait != null)
            {
                speakerPortraitImage.sprite = portrait;
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

        if (dialogueText == null)
        {
            // Интерфейс не привязан: показать нечего, но логика (выборы, квесты) идёт дальше
            isTyping = false;
            typingCoroutine = null;
            revealedCharacters = text.Length;
            if (currentNode != null && currentNode.choices.Count > 0 && !isShowingChoices)
                StartCoroutine(ShowChoicesAfterDelay(choicesDelay));
            yield break;
        }

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

    /// <summary>
    /// Мгновенно допечатать текущую реплику. Вызывается кнопкой продолжения
    /// (клик по тексту) или пробелом.
    /// </summary>
    public void CompleteTyping()
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

        if (dialogueText == null)
        {
            isTyping = false;
            revealedCharacters = (currentNode.dialogueText ?? "").Length;
            if (currentNode.choices.Count > 0 && !isShowingChoices)
                StartCoroutine(ShowChoicesAfterDelay(0f));
            return;
        }

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

    /// <summary>
    /// Двигает диалог дальше (следующий узел или показ вариантов).
    /// Вызывается кнопкой продолжения, Space или автоматически.
    /// </summary>
    public void AdvanceDialogue()
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
        if (currentNode == null) return;
        if (!useStaticChoiceButtons && (choicesContainer == null || choiceButtonPrefab == null)) return;
        isShowingChoices = true;
        ClearSelection();

        if (cursorBlinkCoroutine != null)
        {
            StopCoroutine(cursorBlinkCoroutine);
            cursorBlinkCoroutine = null;
            if (dialogueText != null && currentNode != null)
                dialogueText.text = currentNode.dialogueText;
        }

        if (choicesPanel != null) choicesPanel.SetActive(true);

        // Варианты с учётом условий, предметов и квестов.
        // Скрытые (showWhenLocked выключен) выпадают из списка — нижние кнопки
        // сдвигаются вверх, пробелов нет. Помеченные showWhenLocked показываются
        // заблокированными (UI5, нажать нельзя).
        var entries = new System.Collections.Generic.List<ChoiceEntry>();
        foreach (var choice in currentNode.choices)
        {
            bool available = IsChoiceAvailable(choice);
            if (!available && (choice == null || !choice.showWhenLocked))
                continue;
            entries.Add(new ChoiceEntry(choice, available));
        }

        if (useStaticChoiceButtons)
            ShowStaticChoices(entries);
        else
            ShowTemplateChoices(entries);
    }

    /// <summary>Вариант + доступен ли он прямо сейчас.</summary>
    private struct ChoiceEntry
    {
        public DialogueChoice choice;
        public bool available;
        public ChoiceEntry(DialogueChoice choice, bool available)
        {
            this.choice = choice;
            this.available = available;
        }
    }

    /// <summary>Проверка условий варианта: ассет-условие, предмет, квест.</summary>
    static bool IsChoiceAvailable(DialogueChoice choice)
    {
        if (choice == null) return false;
        if (choice.condition != null && !choice.condition.Evaluate())
            return false;
        if (!string.IsNullOrEmpty(choice.requiredItemId) &&
            !DialogueCommand.PlayerHasItem(choice.requiredItemId, Mathf.Max(1, choice.requiredItemCount)))
            return false;
        if (!string.IsNullOrEmpty(choice.requiredQuestId) &&
            !IsQuestStateMatch(choice.requiredQuestId, choice.requiredQuestState))
            return false;
        return true;
    }

    /// <summary>Варианты на готовых кнопках сцены (Button1..3). Скрытые пропускаем без пробелов, заблокированные — стиль UI5.</summary>
    void ShowStaticChoices(System.Collections.Generic.List<ChoiceEntry> entries)
    {
        // Слоты сортируем сверху вниз по позиции: нумерация кнопок в сцене
        // может не совпадать с визуальным порядком (Button1, Button3, Button2).
        // Видимые ветки занимают верхние слоты подряд — дырок не бывает.
        var slots = new System.Collections.Generic.List<Button>();
        if (staticChoiceButtons != null)
        {
            foreach (Button b in staticChoiceButtons)
            {
                if (b != null) slots.Add(b);
            }
        }
        slots.Sort((a, b) => SlotY(b).CompareTo(SlotY(a)));

        if (slots.Count == 0 && entries.Count > 0)
        {
            Debug.LogWarning("[DialogueManager] Есть варианты, но нет кнопок (staticChoiceButtons пуст). " +
                "Добавь UserDialogueUI на свой канвас: Tools -> Диалоги -> Подключить мой канвас.", this);
            isShowingChoices = false;
            EndDialogue();
            return;
        }
        for (int i = 0; i < slots.Count; i++)
        {
            Button button = slots[i];

            if (i < entries.Count)
            {
                ChoiceEntry entry = entries[i];
                DisableKeyboardFocus(button);
                TextMeshProUGUI label = GetStaticLabel(StaticButtonIndex(button), button);
                if (label != null)
                {
                    label.text = entry.choice.choiceText;
                    label.gameObject.SetActive(true);
                    RememberLabelColor(label);
                    label.color = entry.available ? labelBaseColors[label] : lockedChoiceLabelColor;
                }
                button.onClick.RemoveAllListeners();
                if (entry.available)
                {
                    DialogueChoice capturedChoice = entry.choice;
                    button.onClick.AddListener(() => OnChoiceSelected(capturedChoice));
                    button.interactable = true;
                }
                else
                {
                    // Заблокировано: нажать нельзя, кликов нет
                    button.interactable = false;
                }
                ApplyLockedStyle(button);
                if (!button.gameObject.activeSelf) button.gameObject.SetActive(true);
            }
            else
            {
                button.gameObject.SetActive(false);
            }
        }

        if (entries.Count == 0)
        {
            if (choicesPanel != null) choicesPanel.SetActive(false);
            isShowingChoices = false;
            EndDialogue();
        }
        else if (entries.Count > slots.Count)
        {
            Debug.LogWarning($"[DialogueManager] Вариантов {entries.Count}, а кнопок {slots.Count} — " +
                              "показаны первые. Добавь кнопки в staticChoiceButtons.", this);
        }
    }

    /// <summary>Y-позиция слота (чем больше, тем выше на экране).</summary>
    static float SlotY(Button button)
    {
        if (button == null) return 0f;
        RectTransform rect = button.GetComponent<RectTransform>();
        return rect != null ? rect.anchoredPosition.y : 0f;
    }

    /// <summary>Индекс кнопки в staticChoiceButtons (для поиска её подписи).</summary>
    int StaticButtonIndex(Button button)
    {
        if (staticChoiceButtons == null || button == null) return -1;
        for (int i = 0; i < staticChoiceButtons.Length; i++)
        {
            if (staticChoiceButtons[i] == button) return i;
        }
        return -1;
    }

    /// <summary>Запомнить исходный цвет подписи, чтобы вернуть его после разблокировки.</summary>
    void RememberLabelColor(TextMeshProUGUI label)
    {
        if (label != null && !labelBaseColors.ContainsKey(label))
            labelBaseColors[label] = label.color;
    }

    /// <summary>Спрайт UI5 на кнопку (если задан и у стиля его ещё нет).</summary>
    void ApplyLockedStyle(Button button)
    {
        if (button == null || lockedChoiceSprite == null) return;
        DialogueCanvasButton style = button.GetComponent<DialogueCanvasButton>();
        if (style != null && style.disabledSprite == null)
            style.disabledSprite = lockedChoiceSprite;
    }

    TextMeshProUGUI GetStaticLabel(int index, Button button)
    {
        if (index >= 0 && staticChoiceLabels != null && index < staticChoiceLabels.Length &&
            staticChoiceLabels[index] != null)
            return staticChoiceLabels[index];
        return button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
    }

    /// <summary>Варианты из шаблона (создание/удаление кнопок). Заблокированные — UI5, нажать нельзя.</summary>
    void ShowTemplateChoices(System.Collections.Generic.List<ChoiceEntry> entries)
    {
        if (choicesContainer == null || choiceButtonPrefab == null) return;

        foreach (Transform child in choicesContainer)
            Destroy(child.gameObject);

        foreach (var entry in entries)
        {
            Button button = Instantiate(choiceButtonPrefab, choicesContainer);
            DisableKeyboardFocus(button);
            TextMeshProUGUI buttonText = button.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = entry.choice.choiceText;
                if (!entry.available)
                    buttonText.color = lockedChoiceLabelColor;
            }

            if (entry.available)
            {
                DialogueChoice capturedChoice = entry.choice;
                button.onClick.AddListener(() => OnChoiceSelected(capturedChoice));
                button.interactable = true;
            }
            else
            {
                button.interactable = false;
            }
            ApplyLockedStyle(button);
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
        HideStaticButtons();
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