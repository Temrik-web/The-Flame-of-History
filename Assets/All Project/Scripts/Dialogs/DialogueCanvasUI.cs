using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Кнопочный интерфейс диалогов. Вешается на созданный мастером канвас
/// («Tools -> Диалоги -> Создать канвас») и подключает его элементы к
/// DialogueManager.
///
/// Раскладка (всё внутри одного окна DialogueBox, поверх игры):
///  - слева снизу — имя говорящего;
///  - слева — текст реплики (клик по нему = продолжить);
///  - справа — кнопки вариантов ответа и «Отмена».
///
/// DialogueBox — плавающее окно не на весь экран; двигается в Scene view,
/// и весь интерфейс диалога переезжает вместе с ним.
///
/// Все кнопки настраиваются в инспекторе: стандартный Button + компонент
/// DialogueCanvasButton на каждом элементе (цвета наведения/нажатия и масштаб).
///
/// Ссылки заполняет мастер, но если компонент повесить вручную на канвас
/// с правильными именами элементов, он найдёт всё сам.
/// </summary>
[DisallowMultipleComponent]
public class DialogueCanvasUI : MonoBehaviour
{
    [Header("Менеджер")]
    [Tooltip("Если пусто — берётся DialogueManager.Instance.")]
    public DialogueManager manager;

    [Header("Оформление")]
    [Tooltip("Фон меню: вставь сюда спрайт своего фонового фото/арта.")]
    public Image backgroundImage;
    [Tooltip("Шрифт всех надписей канваса. Поменяй на свой красивый — применится на всё при запуске.")]
    public TMP_FontAsset fontAsset;

    [Header("Окно диалога")]
    [Tooltip("Корень окна. Диалоговый панель для DialogueManager.")]
    public GameObject windowRoot;
    public CanvasGroup windowGroup;

    [Header("Слева: имя и текст")]
    public TextMeshProUGUI speakerNameText;
    public TextMeshProUGUI dialogueText;
    [Tooltip("Кнопка «продолжить» — невидимая область под текстом (клик по тексту).")]
    public Button continueButton;
    [Tooltip("Подсказка «жми, чтобы продолжить».")]
    public GameObject continuePrompt;

    [Header("Справа: варианты ответа")]
    [Tooltip("Вся правая колонка (варианты + отмена). Включается на время выбора.")]
    public GameObject answersColumn;
    [Tooltip("Контейнер, куда DialogueManager кладёт кнопки вариантов.")]
    public Transform choicesContainer;
    [Tooltip("Шаблон кнопки варианта, который размножает DialogueManager.")]
    public Button choiceTemplate;

    [Header("Отмена")]
    [Tooltip("Кнопка «Отмена» — выходит из диалога.")]
    public Button cancelButton;

    [Header("Подсказка взаимодействия (E)")]
    public GameObject interactHint;
    public TextMeshProUGUI interactHintText;

    [Header("Пульсация подсказки продолжения")]
    public bool pulseContinuePrompt = true;
    public float promptPulseSpeed = 3.2f;

    private TextMeshProUGUI promptLabel;

    void Awake()
    {
        if (manager == null) manager = DialogueManager.Instance;
        if (manager == null) manager = FindObjectOfType<DialogueManager>();

        if (manager == null)
        {
            Debug.LogWarning("[DialogueCanvasUI] DialogueManager не найден. Компонент отключён.");
            enabled = false;
            return;
        }

        // Если мастер не разложил ссылки — найдём элементы по именам
        AutoDiscover();

        ApplyFont();

        WireToManager();

        if (continueButton != null)
            continueButton.onClick.AddListener(HandleContinueClick);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(HandleCancelClick);

        if (continuePrompt != null)
            promptLabel = continuePrompt.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    void OnEnable()
    {
        if (manager == null) return;
        manager.OnDialogueEnded += HandleDialogueEnded;
    }

    void OnDisable()
    {
        if (manager == null) return;
        manager.OnDialogueEnded -= HandleDialogueEnded;
    }

    void Update()
    {
        if (manager == null || !manager.isDialogueActive)
        {
            if (continuePrompt != null && continuePrompt.activeSelf)
                continuePrompt.SetActive(false);
            return;
        }

        UpdateContinuePrompt();
    }

    // =====================================================================
    // Применение оформления
    // =====================================================================
    /// <summary>Перекрасить все надписи канваса выбранным в инспекторе шрифтом.</summary>
    void ApplyFont()
    {
        if (fontAsset == null) return;

        foreach (TextMeshProUGUI label in GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label != null) label.font = fontAsset;
        }
    }

    // =====================================================================
    void HandleContinueClick()
    {
        if (manager == null || !manager.isDialogueActive) return;

        if (manager.IsTyping)
        {
            manager.CompleteTyping();
            return;
        }

        if (!manager.IsShowingChoices)
            manager.AdvanceDialogue();
    }

    void HandleCancelClick()
    {
        if (manager != null) manager.EndDialogue();
    }

    void HandleDialogueEnded()
    {
        if (continuePrompt != null) continuePrompt.SetActive(false);
    }

    void UpdateContinuePrompt()
    {
        if (continuePrompt == null) return;

        bool show = !manager.IsTyping && !manager.IsShowingChoices;
        if (continuePrompt.activeSelf != show) continuePrompt.SetActive(show);

        if (show && pulseContinuePrompt && promptLabel != null)
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * promptPulseSpeed);
            promptLabel.color = new Color(promptLabel.color.r, promptLabel.color.g,
                                          promptLabel.color.b, pulse * 0.8f);
        }
    }

    /// <summary>Подключить собранные элементы к DialogueManager.</summary>
    void WireToManager()
    {
        if (windowRoot != null)
        {
            manager.dialoguePanel = windowRoot;
            if (windowGroup == null) windowGroup = windowRoot.GetComponent<CanvasGroup>();
            manager.dialogueCanvasGroup = windowGroup;
        }

        manager.speakerNameText = speakerNameText;
        manager.dialogueText = dialogueText;
        manager.choicesPanel = answersColumn;
        manager.choicesContainer = choicesContainer;
        manager.choiceButtonPrefab = choiceTemplate;
        manager.interactHint = interactHint;
        manager.interactHintText = interactHintText;

        // Своё появление не масштабируем — анимируем только прозрачность
        manager.panelStartScale = Vector3.one;
    }

    // =====================================================================
    // Самопоиск элементов по именам — на случай ручного добавления компонента
    // =====================================================================
    void AutoDiscover()
    {
        Transform t = null;

        if (backgroundImage == null)
        {
            t = FindChild("DialogueBox");
            if (t == null) t = FindChild("Background");
            if (t != null) backgroundImage = t.GetComponent<Image>();
        }

        if (windowRoot == null)
        {
            t = FindChild("DialogueWindow");
            if (t != null) windowRoot = t.gameObject;
        }

        if (speakerNameText == null)
        {
            t = FindChild("SpeakerPlate");
            if (t != null) speakerNameText = t.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (dialogueText == null)
        {
            t = FindChild("DialogueText");
            if (t != null) dialogueText = t.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (continueButton == null)
        {
            t = FindChild("ContinueButton");
            if (t != null) continueButton = t.GetComponent<Button>();
        }

        if (continuePrompt == null)
        {
            t = FindChild("ContinuePrompt");
            if (t != null) continuePrompt = t.gameObject;
        }

        if (answersColumn == null)
        {
            t = FindChild("AnswersColumn");
            if (t != null) answersColumn = t.gameObject;
        }

        if (choicesContainer == null)
        {
            t = FindChild("ChoicesPanel");
            if (t != null) choicesContainer = t;
        }

        if (choiceTemplate == null)
        {
            t = FindChild("ChoiceTemplate");
            if (t != null) choiceTemplate = t.GetComponent<Button>();
        }

        if (cancelButton == null)
        {
            t = FindChild("CancelButton");
            if (t != null) cancelButton = t.GetComponent<Button>();
        }

        if (interactHint == null)
        {
            t = FindChild("InteractHint");
            if (t != null) interactHint = t.gameObject;
        }

        if (interactHintText == null && interactHint != null)
            interactHintText = interactHint.GetComponentsInChildren<TextMeshProUGUI>(true)[0];
    }

    Transform FindChild(string name)
    {
        return FindChildRec(transform, name);
    }

    static Transform FindChildRec(Transform root, string name)
    {
        if (root == null) return null;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.name == name) return c;
            Transform r = FindChildRec(c, name);
            if (r != null) return r;
        }
        return null;
    }
}