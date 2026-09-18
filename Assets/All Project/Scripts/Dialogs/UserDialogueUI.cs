using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Адаптер системы диалогов под СВОЙ канвас пользователя.
///
/// Как пользоваться:
///  1. Выдели свой канвас в Hierarchy.
///  2. Меню Tools -> Диалоги -> Подключить мой канвас (или добавь компонент вручную).
///  3. Проверь в инспекторе, что всё нашлось само: Speeker, Top, TopImage,
///     Button1..3, тексты 1..3, окно. Пустые поля можно перетащить руками.
///
/// Связь нод с интерфейсом:
///  - Top <- тема диалога (краткое заглавие, одно на весь диалог);
///  - Speeker <- реплика говорящего (печатается по буквам, цвет — цвет персонажа);
///  - TopImage <- портрет (если у ноды нет арта — круглая заглушка);
///  - Button1..3 <- варианты ответа ноды (лишние прячутся сами).
///
/// Стили кнопок: обычная — UI2, наведение и нажатие — UI3.
/// Спрайты можно перетащить в поля, либо положить в сцену объекты с именами
/// UI2/UI3 (с компонентом Image) — адаптер заберёт картинку оттуда.
/// </summary>
[DisallowMultipleComponent]
public class UserDialogueUI : MonoBehaviour
{
    [Header("Менеджер")]
    [Tooltip("Если пусто — берётся DialogueManager.Instance.")]
    public DialogueManager manager;

    [Header("Окно (показать/скрыть целиком)")]
    [Tooltip("Корень окна диалога. Если пусто — возьмётся канвас кнопок.")]
    public GameObject dialogueWindow;

    [Header("Ноды -> объекты")]
    [Tooltip("Top — тема диалога (краткое заглавие, одно на весь диалог). TextMeshPro.")]
    public TextMeshProUGUI topicText;
    [Tooltip("Top как обычный Text (если не TextMeshPro). Заполняется само.")]
    public Text topicTextLegacy;
    [Tooltip("Speeker — реплика говорящего. Цвет текста = цвет персонажа.")]
    public TextMeshProUGUI replicaText;
    [Tooltip("TopImage — портрет говорящего.")]
    public Image portraitImage;

    [Header("Варианты (Button1, Button2, Button3)")]
    [Tooltip("Кнопки в порядке Button1, Button2, Button3.")]
    public Button[] choiceButtons = new Button[0];
    [Tooltip("Подписи кнопок (тексты 1, 2, 3 внутри). Если пусто — найдутся сами.")]
    public TextMeshProUGUI[] choiceLabels = new TextMeshProUGUI[0];

    [Header("Стили кнопок")]
    [Tooltip("UI2 — обычная кнопка. Пусто — попробую найти объект UI2 в сцене.")]
    public Sprite normalSprite;
    [Tooltip("UI3 — наведение. Пусто — попробую найти объект UI3 в сцене.")]
    public Sprite hoverSprite;
    [Tooltip("UI3 — нажатие. Пусто — как наведение.")]
    public Sprite pressedSprite;
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color hoverColor = new Color(0.82f, 0.90f, 1f, 1f);
    public Color pressedColor = new Color(0.65f, 0.78f, 0.96f, 1f);
    [Range(1f, 1.3f)] public float hoverScale = 1.03f;
    [Range(0.6f, 1f)] public float pressedScale = 0.97f;

    [Header("Продолжить (необязательно)")]
    [Tooltip("Клик — допечатать/дойти дальше. Если пусто — только пробел.")]
    public Button continueButton;

    [Header("Подсказка «Нажмите E» (необязательно)")]
    public GameObject interactHint;
    public TextMeshProUGUI interactHintText;

    void Awake()
    {
        if (manager == null) manager = DialogueManager.Instance;
        if (manager == null) manager = FindObjectOfType<DialogueManager>();
        if (manager == null)
        {
            Debug.LogWarning("[UserDialogueUI] DialogueManager не найден.");
            enabled = false;
            return;
        }

        AutoDiscover();
        StyleButtons();
        RetryWire();

        if (continueButton != null)
            continueButton.onClick.AddListener(HandleContinueClick);
    }

    /// <summary>Повторить поиск и привязку (менеджер зовёт сам, если с первого раза не вышло).</summary>
    public void RetryWire()
    {
        AutoDiscover();
        StyleButtons();
        WireToManager();

        // Корень с масштабом ~0 невидим всегда — чиним молча
        if (dialogueWindow != null && dialogueWindow.transform.localScale.magnitude < 0.05f)
        {
            dialogueWindow.transform.localScale = Vector3.one;
            Debug.Log("[UserDialogueUI] Масштаб окна был ~0 — вернул 1, иначе ничего не было бы видно.", this);
        }

        // Окно — это НЕ одна из кнопок: иначе прятанье убьёт саму кнопку
        if (dialogueWindow != null && choiceButtons != null)
        {
            foreach (Button b in choiceButtons)
            {
                if (b != null && b.gameObject == dialogueWindow)
                {
                    dialogueWindow = null;
                    Debug.LogWarning("[UserDialogueUI] Окном оказалась сама кнопка — окно не трогаю, " +
                        "реплики и выборы всё равно работают.", this);
                    break;
                }
            }
        }

        // Окно прячем до первого диалога, но только если в нём реально что-то нашлось
        bool uiFound = replicaText != null || topicText != null || topicTextLegacy != null ||
                       (choiceButtons != null && choiceButtons.Length > 0);
        if (dialogueWindow != null && dialogueWindow.activeSelf && uiFound)
            dialogueWindow.SetActive(false);

        Debug.Log($"[UserDialogueUI] Привязка: тема={ObjName(topicText, topicTextLegacy)}, " +
                  $"реплика={ObjName(replicaText)}, портрет={ObjName(portraitImage)}, " +
                   $"кнопок={(choiceButtons != null ? choiceButtons.Length : 0)}.", this);
    }

    static string ObjName(params Component[] comps)
    {
        foreach (Component c in comps)
        {
            if (c != null) return c.gameObject.name;
        }
        return "-";
    }

    void OnEnable()
    {
        if (manager == null) return;
        manager.OnDialogueStarted += HandleDialogueStarted;
        manager.OnNodeChanged += HandleNodeChanged;
    }

    void OnDisable()
    {
        if (manager == null) return;
        manager.OnDialogueStarted -= HandleDialogueStarted;
        manager.OnNodeChanged -= HandleNodeChanged;
    }

    void HandleDialogueStarted()
    {
        WireToManager();
        // Тема — название диалога (одно на весь разговор)
        if (manager != null && manager.CurrentDialogue != null)
        {
            string title = manager.CurrentDialogue.dialogueName;
            if (string.IsNullOrEmpty(title)) title = manager.CurrentDialogue.name;
            if (topicText != null) topicText.text = title;
            else if (topicTextLegacy != null) topicTextLegacy.text = title;
        }
    }

    void HandleNodeChanged(DialogueNode node)
    {
        // Кто говорит — видно по цвету реплики
        if (replicaText != null && node != null)
            replicaText.color = DialogueManager.ResolveSpeakerColor(node);
    }

    void HandleContinueClick()
    {
        if (manager == null || !manager.isDialogueActive) return;
        if (manager.IsTyping) manager.CompleteTyping();
        else if (!manager.IsShowingChoices) manager.AdvanceDialogue();
    }

    // =====================================================================
    // Привязка менеджера к объектам пользователя
    // =====================================================================
    void WireToManager()
    {
        if (manager == null) return;

        // Чужой кодовый UI больше не существует (DialogueUI.cs удалён),
        // а канвас мастера глушим, если встретится в сцене
        foreach (DialogueCanvasUI old in FindObjectsOfType<DialogueCanvasUI>())
        {
            if (old != null && old.gameObject != gameObject)
                old.enabled = false;
        }

        if (dialogueWindow != null)
        {
            manager.dialoguePanel = dialogueWindow;
            CanvasGroup group = dialogueWindow.GetComponent<CanvasGroup>();
            if (group == null) group = dialogueWindow.AddComponent<CanvasGroup>();
            manager.dialogueCanvasGroup = group;
        }

        manager.speakerNameText = null; // отдельного имени нет: говорящий виден по цвету реплики
        manager.dialogueText = replicaText;
        manager.speakerPortraitImage = portraitImage;

        manager.useStaticChoiceButtons = true;
        manager.staticChoiceButtons = choiceButtons;
        manager.staticChoiceLabels = choiceLabels;
        manager.choiceButtonPrefab = null;
        manager.choicesContainer = null;

        // Панель выборов отдельно не трогаем (кнопки лежат вместе со всем окном):
        // кнопки прячутся менеджером поштучно, иначе можно спрятать всё окно
        manager.choicesPanel = null;

        manager.interactHint = interactHint;
        manager.interactHintText = interactHintText;

        manager.panelStartScale = Vector3.one;
        manager.fadeInDuration = 0.15f;

        Debug.Log("[UserDialogueUI] Менеджер подключён к твоему канвасу.", this);
    }

    // =====================================================================
    // Автопоиск по именам: Speeker, Top, TopImage, Button1..3, UI2, UI3
    // =====================================================================
    private Transform searchRoot;

    void AutoDiscover()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Transform scope = canvas != null ? canvas.transform : transform.root;
        searchRoot = scope;

        if (topicText == null)
            topicText = FindPieceTMP(scope, new[] { "Top" }, "Top");
        if (topicText == null && topicTextLegacy == null)
            topicTextLegacy = FindPieceLegacy(scope, new[] { "Top" }, "Top");
        if (replicaText == null)
            replicaText = FindPieceTMP(scope, new[] { "Speeker", "Speaker" },
                "Speeker", "Speaker", "Replica", "DialogText", "Text");
        if (portraitImage == null)
            portraitImage = FindPieceImage(scope, new[] { "TopImage" }, "TopImage", "Portrait", "Avatar");

        if (choiceButtons == null || choiceButtons.Length == 0)
        {
            var found = new System.Collections.Generic.List<Button>();
            for (int i = 1; i <= 3; i++)
            {
                GameObject go = FindPieceGO(scope, new[] { "Button" + i },
                    "Button" + i, "Btn" + i, "Choice" + i, "Option" + i);
                if (go == null) continue;
                Button b = go.GetComponent<Button>();
                if (b == null)
                {
                    b = go.AddComponent<Button>();
                    Debug.LogWarning($"[UserDialogueUI] На «Button{i}» не было компонента Button — добавил сам.", this);
                }
                found.Add(b);
            }
            if (found.Count > 0) choiceButtons = found.ToArray();
        }

        // Подписи 1..3 должны быть TextMeshPro (иначе текст вариантов не встанет)
        if (choiceButtons != null)
        {
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                Button b = choiceButtons[i];
                if (b == null) continue;
                if (b.GetComponentInChildren<TextMeshProUGUI>(true) == null)
                {
                    if (b.GetComponentInChildren<Text>(true) != null)
                        Debug.LogWarning($"[UserDialogueUI] Подпись кнопки «{b.name}» — обычный Text. " +
                            "Замени на TextMeshProUGUI, иначе варианты будут пустыми.", this);
                    else
                        Debug.LogWarning($"[UserDialogueUI] В кнопке «{b.name}» нет текста-подписи.", this);
                }
            }
        }

        if (dialogueWindow == null)
            dialogueWindow = canvas != null ? canvas.gameObject : gameObject;

        // UI2/UI3 как отдельных спрайтов нет: обычная кнопка — её же картинка,
        // наведение/нажатие — тонировка (положи спрайты в поля — станут картинками)
        if (normalSprite == null) normalSprite = FindSpriteInScene("UI2");
        if (hoverSprite == null) hoverSprite = FindSpriteInScene("UI3");
        if (pressedSprite == null) pressedSprite = hoverSprite;

        Report("Top (тема)", topicText != null ? topicText.gameObject :
            (topicTextLegacy != null ? topicTextLegacy.gameObject : null), typeof(TextMeshProUGUI));
        Report("Speeker (реплика)", replicaText != null ? replicaText.gameObject : null, typeof(TextMeshProUGUI));
        Report("TopImage", portraitImage != null ? portraitImage.gameObject : null, typeof(Image));
        for (int i = 0; i < (choiceButtons != null ? choiceButtons.Length : 0); i++)
            Report("Button" + (i + 1), choiceButtons[i] != null ? choiceButtons[i].gameObject : null, typeof(Button));
    }

    void Report(string name, GameObject go, System.Type need)
    {
        if (go != null) return;
        // Объект есть, а компонента нет — подсказываем точно
        string shortName = name.Split(' ')[0];
        GameObject any = FindGO(searchRoot, shortName);
        if (any != null)
            Debug.LogWarning($"[UserDialogueUI] Объект «{shortName}» есть, но на нём нет {need.Name} — " +
                "замени/добавь компонент.", this);
        else
            Debug.LogWarning($"[UserDialogueUI] Не нашёл объект «{name}» — перетащи его в поле вручную.", this);
    }

    // =====================================================================
    // Стили кнопок: UI2 обычная, UI3 наведение/нажатие
    // =====================================================================
    void StyleButtons()
    {
        if (choiceButtons == null) return;

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            Button button = choiceButtons[i];
            if (button == null) continue;

            button.transition = Selectable.Transition.None;
            Image graphic = button.GetComponent<Image>();
            if (graphic != null) button.targetGraphic = graphic;

            // Обычный вид — родная картинка кнопки (UI2 как объекта нет)
            if (normalSprite == null && graphic != null && graphic.sprite != null)
                normalSprite = graphic.sprite;

            DialogueCanvasButton style = button.GetComponent<DialogueCanvasButton>();
            if (style == null) style = button.gameObject.AddComponent<DialogueCanvasButton>();

            style.target = graphic;
            style.normalSprite = normalSprite;
            style.hoverSprite = hoverSprite;
            style.pressedSprite = pressedSprite;
            style.normalColor = normalColor;
            style.hoverColor = hoverColor;
            style.pressedColor = pressedColor;
            style.hoverScale = hoverScale;
            style.pressedScale = pressedScale;
        }
    }

    // =====================================================================
    // Поиск: сначала точное имя, потом нормализованное (регистр/пробелы/тире)
    // =====================================================================
    static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
    }

    static Transform FindByAliases(Transform scope, params string[] aliases)
    {
        if (scope == null) return null;
        var all = scope.GetComponentsInChildren<Transform>(true);
        foreach (string a in aliases)
        {
            foreach (Transform t in all)
            {
                if (t.name == a) return t;
            }
        }
        foreach (string a in aliases)
        {
            string na = Norm(a);
            foreach (Transform t in all)
            {
                if (Norm(t.name) == na) return t;
            }
        }
        return null;
    }

    static TextMeshProUGUI FindByAliasesTMP(Transform scope, params string[] aliases)
    {
        Transform t = FindByAliases(scope, aliases);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    static Image FindByAliasesImage(Transform scope, params string[] aliases)
    {
        Transform t = FindByAliases(scope, aliases);
        return t != null ? t.GetComponent<Image>() : null;
    }

    static GameObject FindByAliasesGO(Transform scope, params string[] aliases)
    {
        Transform t = FindByAliases(scope, aliases);
        return t != null ? t.gameObject : null;
    }
    static TextMeshProUGUI FindTMP(Transform scope, string name)
    {
        Transform t = FindChild(scope, name);
        if (t == null) return null;
        return t.GetComponent<TextMeshProUGUI>();
    }

    static Text FindLegacyText(Transform scope, string name)
    {
        Transform t = FindChild(scope, name);
        if (t == null) return null;
        return t.GetComponent<Text>();
    }

    static Image FindImage(Transform scope, string name)
    {
        Transform t = FindChild(scope, name);
        if (t == null) return null;
        return t.GetComponent<Image>();
    }

    static GameObject FindGO(Transform scope, string name)
    {
        Transform t = FindChild(scope, name);
        return t != null ? t.gameObject : null;
    }

    static Transform FindChild(Transform scope, string name)
    {
        if (scope == null) return null;
        foreach (Transform t in scope.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t;
        }
        return null;
    }

    /// <summary>
    /// Поиск объекта: сначала в своей ветке (все варианты имён),
    /// потом по всей сцене (только точные имена — чтобы не схватить чужое).
    /// </summary>
    static GameObject FindPieceGO(Transform scope, string[] sceneNames, params string[] scopeAliases)
    {
        if (scope != null)
        {
            Transform t = FindByAliases(scope, scopeAliases);
            if (t != null) return t.gameObject;
        }
        foreach (string a in sceneNames)
        {
            Transform t = FindInSceneByName(a);
            if (t != null) return t.gameObject;
        }
        return null;
    }

    static TextMeshProUGUI FindPieceTMP(Transform scope, string[] sceneNames, params string[] scopeAliases)
    {
        GameObject go = FindPieceGO(scope, sceneNames, scopeAliases);
        return go != null ? go.GetComponent<TextMeshProUGUI>() : null;
    }

    static Text FindPieceLegacy(Transform scope, string[] sceneNames, params string[] scopeAliases)
    {
        GameObject go = FindPieceGO(scope, sceneNames, scopeAliases);
        return go != null ? go.GetComponent<Text>() : null;
    }

    static Image FindPieceImage(Transform scope, string[] sceneNames, params string[] scopeAliases)
    {
        GameObject go = FindPieceGO(scope, sceneNames, scopeAliases);
        return go != null ? go.GetComponent<Image>() : null;
    }

    static Transform FindInSceneByName(string name)
    {
        foreach (Transform t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t != null && t.name == name) return t;
        }
        return null;
    }

    /// <summary>Картинка из объекта сцены с именем (UI2/UI3): берём его спрайт.</summary>
    static Sprite FindSpriteInScene(string objectName)
    {
        foreach (Image img in Object.FindObjectsOfType<Image>(true))
        {
            if (img != null && img.gameObject.name == objectName && img.sprite != null)
                return img.sprite;
        }
        return null;
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Диалоги/Подключить мой канвас", false, 2)]
    static void AttachToSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("Мой канвас",
                "Выдели в Hierarchy свой канвас (или любой объект внутри него) и повтори.",
                "Ок");
            return;
        }

        UserDialogueUI ui = selected.GetComponent<UserDialogueUI>();
        if (ui == null) ui = Undo.AddComponent<UserDialogueUI>(selected);

        Selection.activeGameObject = selected;
        EditorGUIUtility.PingObject(selected);
        Debug.Log("[UserDialogueUI] Компонент добавлен. Нажми Play — объекты Speeker/Top/TopImage/Button1..3 найдутся сами, недостающие перетащи в инспекторе.");
    }
#endif
}
