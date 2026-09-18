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
/// Связь нод с интерфейсом (канвас Dialogs):
///  - Top <- тема диалога (краткое заглавие, одно на весь диалог; обычный Text);
///  - Speeker <- реплика говорящего (печатается по буквам, цвет — цвет персонажа; клик = дальше);
///  - TopImage <- статичное фото дизайна (не перезаписывается);
///  - Окно <- Backg (прячется/показывается целиком);
///  - Button1..3 <- варианты ответа ноды (подписи 1..3 внутри, лишние прячутся сами).
///
/// Стили кнопок: обычная — UI_2, наведение — UI_3, нажатие — UI_4, недоступна — UI_5.
/// Спрайты забираются из объектов-сэмплов сцены (SpriteRenderer), сами сэмплы прячутся.
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
    [Tooltip("TopImage — часть дизайна (статичное фото). Не перезаписывается репликами.")]
    public Image portraitImage;
    [Tooltip("Включено — TopImage не трогаем (менеджеру портрет не отдаём, спрайт статичен).")]
    public bool keepPortraitStatic = true;

    [Header("Варианты (Button1, Button2, Button3)")]
    [Tooltip("Кнопки в порядке Button1, Button2, Button3.")]
    public Button[] choiceButtons = new Button[0];
    [Tooltip("Подписи кнопок (тексты 1, 2, 3 внутри). Если пусто — найдутся сами.")]
    public TextMeshProUGUI[] choiceLabels = new TextMeshProUGUI[0];

    [Header("Стили кнопок (UI_2 / UI_3 / UI_4)")]
    [Tooltip("UI_2 — обычная кнопка. Пусто — заберу спрайт из объекта UI_2 в сцене.")]
    public Sprite normalSprite;
    [Tooltip("UI_3 — наведение. Пусто — заберу спрайт из объекта UI_3 в сцене.")]
    public Sprite hoverSprite;
    [Tooltip("UI_4 — нажатие. Пусто — заберу спрайт из UI_4, иначе как наведение.")]
    public Sprite pressedSprite;
    [Tooltip("UI_5 — заблокированная ветка (недоступный вариант). Пусто — заберу из UI_5, иначе серый.")]
    public Sprite lockedSprite;
    [Tooltip("Цвет подписи заблокированного варианта.")]
    public Color lockedLabelColor = new Color(0.55f, 0.55f, 0.6f, 0.8f);
    public Color normalColor = new Color(1f, 1f, 1f, 1f);
    public Color hoverColor = new Color(0.82f, 0.90f, 1f, 1f);
    public Color pressedColor = new Color(0.65f, 0.78f, 0.96f, 1f);
    [Range(1f, 1.3f)] public float hoverScale = 1.03f;
    [Range(0.6f, 1f)] public float pressedScale = 0.97f;

    [Header("Продолжить (клик по реплике)")]
    [Tooltip("Клик — допечатать/дойти дальше. Если пусто — клик вешается на родителя Speeker (Image) сам.")]
    public Button continueButton;
    [Tooltip("Клик по области реплики двигает диалог дальше (для канваса Dialogs).")]
    public bool clickReplicaToAdvance = true;

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
        EnsureReplicaClick();
        RetryWire();

        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(HandleContinueClick);
            continueButton.onClick.AddListener(HandleContinueClick);
        }
    }

    /// <summary>
    /// Клик по реплике: если отдельной кнопки продолжения нет,
    /// вешаем клик на родителя Speeker (Image-контейнер).
    /// Текст клик не перехватывает — отдаём его родителю.
    /// </summary>
    void EnsureReplicaClick()
    {
        if (!clickReplicaToAdvance || continueButton != null || replicaText == null) return;
        Transform holder = replicaText.transform.parent;
        if (holder == null) holder = replicaText.transform;
        Button btn = holder.GetComponent<Button>();
        if (btn == null)
        {
            btn = holder.gameObject.AddComponent<Button>();
            Debug.Log($"[UserDialogueUI] На «{holder.name}» не было Button — добавил для клика-продолжения.", this);
        }
        // Прозрачная кнопка: свой Image оставляем как есть, переход убираем.
        // Фокус с клавиатуры запрещаем: продолжение — пробелом через менеджер
        // или кликом, но не Enter/Space по сфокусированной кнопке (двойной шаг).
        btn.transition = Selectable.Transition.None;
        Navigation nav = btn.navigation;
        nav.mode = Navigation.Mode.None;
        btn.navigation = nav;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(HandleContinueClick);
        continueButton = btn;
        // Текст не должен съедать клик
        replicaText.raycastTarget = false;
        Image holderImg = holder.GetComponent<Image>();
        if (holderImg != null) holderImg.raycastTarget = true;
    }

    /// <summary>Повторить поиск и привязку (менеджер зовёт сам, если с первого раза не вышло).</summary>
    public void RetryWire()
    {
        AutoDiscover();
        StyleButtons();
        EnsureReplicaClick();
        WireToManager();

        // Корень канваса Dialogs мог остаться с масштабом 0 / выключенным —
        // чиним и корень, и окно, иначе ничего не будет видно.
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null && searchRoot != null)
            canvas = searchRoot.GetComponentInChildren<Canvas>(true);
        if (canvas != null)
        {
            if (!canvas.gameObject.activeSelf)
            {
                canvas.gameObject.SetActive(true);
                Debug.Log("[UserDialogueUI] Корень канваса был выключен — включил.", this);
            }
            if (canvas.transform.localScale.magnitude < 0.05f)
            {
                canvas.transform.localScale = Vector3.one;
                Debug.Log("[UserDialogueUI] Масштаб корня канваса был ~0 — вернул 1.", this);
            }
        }

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
        // TopImage — статичное фото дизайна: спрайт репликами не перезаписываем
        manager.speakerPortraitImage = keepPortraitStatic ? null : portraitImage;

        manager.useStaticChoiceButtons = true;
        manager.staticChoiceButtons = choiceButtons;
        manager.staticChoiceLabels = choiceLabels;
        if (lockedSprite != null)
            manager.lockedChoiceSprite = lockedSprite;
        manager.lockedChoiceLabelColor = lockedLabelColor;
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

        // Подписи 1..3 должны быть TextMeshPro (иначе текст вариантов не встанет).
        // Для канваса Dialogs подписи лежат в детях Button1..3 с именами "1","2","3".
        if (choiceButtons != null)
        {
            var labels = new System.Collections.Generic.List<TextMeshProUGUI>();
            bool needLabels = choiceLabels == null || choiceLabels.Length != choiceButtons.Length;
            for (int i = 0; i < choiceButtons.Length; i++)
            {
                Button b = choiceButtons[i];
                if (b == null)
                {
                    if (needLabels) labels.Add(null);
                    continue;
                }
                TextMeshProUGUI label = null;
                if (!needLabels && i < choiceLabels.Length)
                    label = choiceLabels[i];
                if (label == null)
                {
                    // Сначала ищем ребёнка с именем "1"/"2"/"3", потом любой TMP внутри
                    string want = (i + 1).ToString();
                    foreach (TextMeshProUGUI t in b.GetComponentsInChildren<TextMeshProUGUI>(true))
                    {
                        if (t != null && t.gameObject.name == want) { label = t; break; }
                    }
                    if (label == null)
                        label = b.GetComponentInChildren<TextMeshProUGUI>(true);
                }
                if (needLabels) labels.Add(label);
                if (label == null)
                {
                    if (b.GetComponentInChildren<Text>(true) != null)
                        Debug.LogWarning($"[UserDialogueUI] Подпись кнопки «{b.name}» — обычный Text. " +
                            "Замени на TextMeshProUGUI, иначе варианты будут пустыми.", this);
                    else
                        Debug.LogWarning($"[UserDialogueUI] В кнопке «{b.name}» нет текста-подписи.", this);
                }
            }
            if (needLabels && labels.Count == choiceButtons.Length)
                choiceLabels = labels.ToArray();
        }

        if (dialogueWindow == null)
        {
            // Для канваса Dialogs окном является Backg (панель с кнопками),
            // а не весь Canvas — иначе прятанье/масштаб ломают весь канвас.
            GameObject backg = FindPieceGO(scope, new[] { "Backg" }, "Backg", "Background", "Window", "DialogueWindow");
            dialogueWindow = backg != null ? backg : (canvas != null ? canvas.gameObject : gameObject);
        }

        // Стили кнопок: UI_2 обычная, UI_3 наведение, UI_4 нажатие.
        // Объекты-сэмплы в сцене — это SpriteRenderer (не UI Image),
        // имена могут быть UI_2 / UI2 / UI 2 — ищем по нормализованному имени.
        if (normalSprite == null) normalSprite = FindButtonSprite("UI_2", "UI2", "UI 2");
        if (hoverSprite == null) hoverSprite = FindButtonSprite("UI_3", "UI3", "UI 3");
        if (pressedSprite == null) pressedSprite = FindButtonSprite("UI_4", "UI4", "UI 4");
        if (pressedSprite == null) pressedSprite = hoverSprite;
        if (lockedSprite == null) lockedSprite = FindButtonSprite("UI_5", "UI5", "UI 5");

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
    // Стили кнопок: UI_2 обычная, UI_3 наведение, UI_4 нажатие
    // =====================================================================
    void StyleButtons()
    {
        if (choiceButtons == null) return;

        for (int i = 0; i < choiceButtons.Length; i++)
        {
            Button button = choiceButtons[i];
            if (button == null) continue;

            button.transition = Selectable.Transition.None;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            Image graphic = button.GetComponent<Image>();
            if (graphic != null) button.targetGraphic = graphic;

            // Обычный вид — спрайт UI_2 сразу на фон кнопки
            if (graphic != null && normalSprite != null)
            {
                graphic.sprite = normalSprite;
                graphic.type = Image.Type.Sliced;
            }
            else if (normalSprite == null && graphic != null && graphic.sprite != null)
            {
                normalSprite = graphic.sprite;
            }

            DialogueCanvasButton style = button.GetComponent<DialogueCanvasButton>();
            if (style == null) style = button.gameObject.AddComponent<DialogueCanvasButton>();

            style.target = graphic;
            style.normalSprite = normalSprite;
            style.hoverSprite = hoverSprite;
            style.pressedSprite = pressedSprite;
            if (lockedSprite != null)
                style.disabledSprite = lockedSprite;
            style.normalColor = normalColor;
            style.hoverColor = hoverColor;
            style.pressedColor = pressedColor;
            style.hoverScale = hoverScale;
            style.pressedScale = pressedScale;
        }

        HideStyleSamples();
    }

    /// <summary>
    /// Прячем объекты-сэмплы UI_2/UI_3/UI_4/UI_5 после того, как забрали спрайты:
    /// они лежат внутри Backg и иначе будут мусорить в окне диалога.
    /// </summary>
    void HideStyleSamples()
    {
        if (searchRoot == null) return;
        foreach (string sample in new[] { "UI_2", "UI_3", "UI_4", "UI_5" })
        {
            Transform t = FindByAliases(searchRoot, sample, sample.Replace("_", ""), sample.Replace("_", " "));
            if (t == null) continue;
            // Сэмпл — это SpriteRenderer-образец, а не кнопка: кнопки не трогаем
            if (t.GetComponent<Button>() != null) continue;
            if (t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(false);
                Debug.Log($"[UserDialogueUI] Сэмпл «{t.name}» спрятан (спрайт уже на кнопках).", this);
            }
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

    /// <summary>
    /// Спрайт стиля кнопки из объекта-сэмпла сцены.
    /// Имена нормализуются (UI_2 = UI2 = UI 2), берём спрайт из
    /// SpriteRenderer (твои UI_1..UI_4) или из UI Image — что найдётся первым.
    /// </summary>
    static Sprite FindButtonSprite(params string[] aliases)
    {
        var wanted = new System.Collections.Generic.HashSet<string>();
        foreach (string a in aliases) wanted.Add(Norm(a));

        foreach (SpriteRenderer sr in Object.FindObjectsOfType<SpriteRenderer>(true))
        {
            if (sr != null && sr.sprite != null && wanted.Contains(Norm(sr.gameObject.name)))
                return sr.sprite;
        }
        foreach (Image img in Object.FindObjectsOfType<Image>(true))
        {
            if (img != null && img.sprite != null && wanted.Contains(Norm(img.gameObject.name)))
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
