using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Кинематографичный интерфейс диалогов. Собирает весь UI кодом и подключает
/// его к существующему DialogueManager — сам менеджер не меняется.
///
/// Вешается на любой объект в сцене (например, на тот же, где DialogueManager).
///
/// Что даёт:
///  - панель реплики со скруглёнными углами и мягкой окантовкой;
///  - рамка портрета с подсветкой цвета акцента;
///  - плашка имени говорящего;
///  - чёрные кинематографичные полосы сверху и снизу;
///  - виньетка, фокусирующая взгляд на тексте;
///  - кнопки ответов с наведением, нумерацией и выбором цифрами 1..9;
///  - индикатор «жми пробел» с плавной пульсацией.
/// </summary>
[DisallowMultipleComponent]
public class DialogueUI : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Если пусто — берётся DialogueManager.Instance.")]
    public DialogueManager manager;

    [Header("Оформление")]
    public Color panelColor = new Color(0.055f, 0.06f, 0.08f, 0.95f);
    public Color accentColor = new Color(1f, 0.66f, 0.28f);
    public Color textColor = new Color(0.94f, 0.95f, 0.97f);
    public Color speakerNameColor = new Color(1f, 0.78f, 0.42f);

    [Header("Размеры")]
    [Tooltip("Высота панели реплики в пикселях (при разрешении 1920x1080).")]
    public float panelHeight = 260f;
    [Tooltip("Отступ панели от низа экрана.")]
    public float panelBottomMargin = 48f;
    [Tooltip("Боковые отступы панели.")]
    public float panelSideMargin = 180f;
    public float portraitSize = 190f;

    [Header("Кинематографичные полосы")]
    public bool useCinematicBars = true;
    [Tooltip("Высота чёрной полосы сверху и снизу.")]
    public float barHeight = 74f;
    public float barAnimationDuration = 0.45f;

    [Header("Виньетка")]
    public bool useVignette = true;
    [Range(0f, 1f)] public float vignetteStrength = 0.55f;

    [Header("Ответы")]
    [Tooltip("Выбирать вариант ответа цифрами 1..9.")]
    public bool numberKeysSelectChoices = true;
    public float choiceHeight = 52f;
    public float choiceSpacing = 8f;

    [Header("Индикатор продолжения")]
    public bool showContinuePrompt = true;
    public string continueText = "Пробел";

    [Header("Шрифт")]
    [Tooltip("TMP-шрифт с кириллицей. Если пусто — Resources/InventoryFont SDF.")]
    public TMP_FontAsset fontAsset;

    // ---------- собранные элементы ----------
    private Canvas canvas;
    private CanvasGroup rootGroup;
    private GameObject dialogueRoot;
    private RectTransform panelRect;

    private Image portraitFrame;
    private Image portraitImage;
    private TextMeshProUGUI speakerLabel;
    private GameObject speakerPlate;
    private TextMeshProUGUI bodyLabel;

    private GameObject choicesRoot;
    private RectTransform choicesContainer;
    private Button choiceTemplate;

    private GameObject continueRoot;
    private TextMeshProUGUI continueLabel;

    private RectTransform topBar;
    private RectTransform bottomBar;
    private Image vignette;

    private GameObject interactHintRoot;
    private TextMeshProUGUI interactHintLabel;

    private Coroutine barRoutine;
    private Coroutine fadeRoutine;
    private Coroutine choiceCollectRoutine;
    private readonly List<DialogueChoiceButton> activeChoices = new List<DialogueChoiceButton>();

    private bool built;

    // =====================================================================
    void Awake()
    {
        if (manager == null) manager = DialogueManager.Instance;
        if (manager == null) manager = FindObjectOfType<DialogueManager>();

        if (manager == null)
        {
            Debug.LogWarning("[DialogueUI] DialogueManager не найден. Компонент отключён.");
            enabled = false;
            return;
        }

        if (fontAsset == null)
            fontAsset = Resources.Load<TMP_FontAsset>("InventoryFont SDF");

        BuildUI();
        WireToManager();
    }

    void OnEnable()
    {
        if (manager == null) return;
        manager.OnDialogueStarted += HandleDialogueStarted;
        manager.OnDialogueEnded += HandleDialogueEnded;
        manager.OnNodeChanged += HandleNodeChanged;
    }

    void OnDisable()
    {
        if (manager == null) return;
        manager.OnDialogueStarted -= HandleDialogueStarted;
        manager.OnDialogueEnded -= HandleDialogueEnded;
        manager.OnNodeChanged -= HandleNodeChanged;
    }

    void Update()
    {
        if (manager == null || !manager.isDialogueActive) return;

        UpdateContinuePrompt();

        if (numberKeysSelectChoices && manager.IsShowingChoices)
            HandleNumberKeys();
    }

    void HandleNumberKeys()
    {
        for (int i = 0; i < activeChoices.Count && i < 9; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i)) continue;
            if (activeChoices[i] == null) continue;

            activeChoices[i].Invoke();
            return;
        }
    }

    void UpdateContinuePrompt()
    {
        if (!showContinuePrompt || continueRoot == null) return;

        // Показываем только когда текст допечатан и нет вариантов ответа
        bool show = !manager.IsTyping && !manager.IsShowingChoices;
        if (continueRoot.activeSelf != show) continueRoot.SetActive(show);

        if (show && continueLabel != null)
        {
            // Плавная пульсация: не мигание, а дыхание
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.2f);
            continueLabel.color = new Color(textColor.r, textColor.g, textColor.b, pulse * 0.8f);
        }
    }

    // =====================================================================
    // Подключение к менеджеру
    // =====================================================================
    void WireToManager()
    {
        manager.dialoguePanel = dialogueRoot;
        manager.dialogueCanvasGroup = rootGroup;
        manager.speakerNameText = speakerLabel;
        manager.speakerPortraitImage = portraitImage;
        manager.dialogueText = bodyLabel;
        manager.choicesPanel = choicesRoot;
        manager.choicesContainer = choicesContainer;
        manager.choiceButtonPrefab = choiceTemplate;

        // Подсказка «Нажмите E для разговора» — DialogueTrigger включает её сам
        manager.interactHint = interactHintRoot;
        manager.interactHintText = interactHintLabel;

        // Свою анимацию появления делаем сами — у менеджера отключаем масштабирование
        manager.panelStartScale = Vector3.one;
        manager.fadeInDuration = 0.01f;
    }

    void HandleDialogueStarted()
    {
        if (useCinematicBars) AnimateBars(true);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeVignette(useVignette ? vignetteStrength : 0f));

        if (panelRect != null) StartCoroutine(SlidePanelIn());
    }

    void HandleDialogueEnded()
    {
        if (useCinematicBars) AnimateBars(false);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeVignette(0f));

        if (choiceCollectRoutine != null)
        {
            StopCoroutine(choiceCollectRoutine);
            choiceCollectRoutine = null;
        }

        if (continueRoot != null) continueRoot.SetActive(false);
        activeChoices.Clear();
    }

    void HandleNodeChanged(DialogueNode node)
    {
        // Плашка имени скрывается, если говорящий не назван
        if (speakerPlate != null)
            speakerPlate.SetActive(node != null && !string.IsNullOrEmpty(node.speakerName));

        // Портрет и его рамка появляются вместе
        bool hasPortrait = node != null && node.speakerPortrait != null;
        if (portraitFrame != null) portraitFrame.gameObject.SetActive(hasPortrait);

        // Текст сдвигается, освобождая место под портрет
        if (bodyLabel != null)
        {
            RectTransform r = (RectTransform)bodyLabel.transform;
            float left = hasPortrait ? portraitSize + 52f : 34f;
            r.offsetMin = new Vector2(left, 46f);
        }

        if (speakerPlate != null && speakerPlate.activeSelf)
        {
            RectTransform r = (RectTransform)speakerPlate.transform;
            float left = hasPortrait ? portraitSize + 52f : 34f;
            r.anchoredPosition = new Vector2(left, 10f);
        }

        activeChoices.Clear();
        if (continueRoot != null) continueRoot.SetActive(false);

        if (choiceCollectRoutine != null) StopCoroutine(choiceCollectRoutine);
        choiceCollectRoutine = StartCoroutine(CollectChoiceButtonsNextFrame());
    }

    /// <summary>
    /// Менеджер создаёт кнопки сам. Ждём кадр и находим их,
    /// чтобы навесить номера и поддержку цифровых клавиш.
    /// </summary>
    IEnumerator CollectChoiceButtonsNextFrame()
    {
        activeChoices.Clear();

        // Ждём, пока менеджер допечатает текст и создаст кнопки
        while (manager != null && manager.isDialogueActive && !manager.IsShowingChoices)
            yield return null;

        yield return null;

        if (manager == null || !manager.isDialogueActive || choicesContainer == null)
        {
            choiceCollectRoutine = null;
            yield break;
        }

        int number = 1;
        foreach (Transform child in choicesContainer)
        {
            Button button = child.GetComponent<Button>();
            if (button == null) continue;

            var helper = child.GetComponent<DialogueChoiceButton>();
            if (helper == null) helper = child.gameObject.AddComponent<DialogueChoiceButton>();
            helper.Setup(button, number, accentColor, textColor);

            activeChoices.Add(helper);
            number++;
        }

        choiceCollectRoutine = null;
    }

    // =====================================================================
    // Анимации
    // =====================================================================
    IEnumerator SlidePanelIn()
    {
        const float duration = 0.28f;
        float elapsed = 0f;

        Vector2 target = new Vector2(0f, panelBottomMargin);
        Vector2 from = target + new Vector2(0f, -40f);

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            panelRect.anchoredPosition = Vector2.Lerp(from, target, t);
            yield return null;
        }

        panelRect.anchoredPosition = target;
    }

    void AnimateBars(bool show)
    {
        if (topBar == null || bottomBar == null) return;
        if (barRoutine != null) StopCoroutine(barRoutine);
        barRoutine = StartCoroutine(BarRoutine(show ? barHeight : 0f));
    }

    IEnumerator BarRoutine(float targetHeight)
    {
        float startTop = topBar.sizeDelta.y;
        float elapsed = 0f;

        while (elapsed < barAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / barAnimationDuration));
            float h = Mathf.Lerp(startTop, targetHeight, t);

            topBar.sizeDelta = new Vector2(0f, h);
            bottomBar.sizeDelta = new Vector2(0f, h);
            yield return null;
        }

        topBar.sizeDelta = new Vector2(0f, targetHeight);
        bottomBar.sizeDelta = new Vector2(0f, targetHeight);
        barRoutine = null;
    }

    IEnumerator FadeVignette(float target)
    {
        if (vignette == null) yield break;

        const float duration = 0.4f;
        float start = vignette.color.a;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float a = Mathf.Lerp(start, target, t);
            vignette.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }

        vignette.color = new Color(0f, 0f, 0f, target);
        fadeRoutine = null;
    }

    // =====================================================================
    // Сборка UI
    // =====================================================================
    void BuildUI()
    {
        if (built) return;
        built = true;

        Sprite round = UIShapes.RoundedRect(64, 16);
        Sprite roundSmall = UIShapes.RoundedRect(48, 10);
        Sprite solid = UIShapes.Solid();

        // --- Canvas ---
        canvas = new GameObject("DialogueCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // под инвентарём (100)

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        // --- Виньетка (всегда в сцене, прозрачная вне диалога) ---
        if (useVignette)
        {
            GameObject vig = NewUI("Vignette", canvas.transform);
            Stretch((RectTransform)vig.transform);
            vignette = vig.AddComponent<Image>();
            vignette.sprite = UIShapes.Vignette(256, 0.30f, 1.8f);
            vignette.color = new Color(0f, 0f, 0f, 0f);
            vignette.raycastTarget = false;
        }

        // --- Кинематографичные полосы ---
        if (useCinematicBars)
        {
            topBar = MakeBar("BarTop", canvas.transform, true, solid);
            bottomBar = MakeBar("BarBottom", canvas.transform, false, solid);
        }

        // --- Корень диалога ---
        dialogueRoot = NewUI("DialogueRoot", canvas.transform);
        Stretch((RectTransform)dialogueRoot.transform);
        rootGroup = dialogueRoot.AddComponent<CanvasGroup>();
        rootGroup.alpha = 0f;

        // --- Панель реплики ---
        GameObject panel = NewUI("Panel", dialogueRoot.transform);
        panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 0f);
        panelRect.pivot = new Vector2(0.5f, 0f);
        panelRect.offsetMin = new Vector2(panelSideMargin, 0f);
        panelRect.offsetMax = new Vector2(-panelSideMargin, 0f);
        panelRect.sizeDelta = new Vector2(-panelSideMargin * 2f, panelHeight);
        panelRect.anchoredPosition = new Vector2(0f, panelBottomMargin);

        Image panelBg = panel.AddComponent<Image>();
        panelBg.sprite = round;
        panelBg.type = Image.Type.Sliced;
        panelBg.color = panelColor;

        GameObject panelOutline = NewUI("Outline", panel.transform);
        Stretch((RectTransform)panelOutline.transform);
        Image outlineImg = panelOutline.AddComponent<Image>();
        outlineImg.sprite = UIShapes.RoundedRect(64, 16, 2);
        outlineImg.type = Image.Type.Sliced;
        outlineImg.color = new Color(1f, 1f, 1f, 0.10f);
        outlineImg.raycastTarget = false;

        // Акцентная полоска слева — вертикальный «корешок» панели
        GameObject accentEdge = NewUI("AccentEdge", panel.transform);
        RectTransform edgeRect = (RectTransform)accentEdge.transform;
        edgeRect.anchorMin = new Vector2(0f, 0f);
        edgeRect.anchorMax = new Vector2(0f, 1f);
        edgeRect.pivot = new Vector2(0f, 0.5f);
        edgeRect.offsetMin = new Vector2(0f, 22f);
        edgeRect.offsetMax = new Vector2(4f, -22f);
        Image edgeImg = accentEdge.AddComponent<Image>();
        edgeImg.sprite = UIShapes.VerticalGradient(64, 0.15f, 0.85f);
        edgeImg.color = accentColor;
        edgeImg.raycastTarget = false;

        // --- Портрет ---
        GameObject frame = NewUI("PortraitFrame", panel.transform);
        RectTransform frameRect = (RectTransform)frame.transform;
        frameRect.anchorMin = new Vector2(0f, 0f);
        frameRect.anchorMax = new Vector2(0f, 0f);
        frameRect.pivot = new Vector2(0f, 0f);
        frameRect.anchoredPosition = new Vector2(26f, 30f);
        frameRect.sizeDelta = new Vector2(portraitSize, portraitSize);

        portraitFrame = frame.AddComponent<Image>();
        portraitFrame.sprite = roundSmall;
        portraitFrame.type = Image.Type.Sliced;
        portraitFrame.color = new Color(1f, 1f, 1f, 0.06f);

        GameObject frameEdge = NewUI("Edge", frame.transform);
        Stretch((RectTransform)frameEdge.transform);
        Image frameEdgeImg = frameEdge.AddComponent<Image>();
        frameEdgeImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        frameEdgeImg.type = Image.Type.Sliced;
        frameEdgeImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.45f);
        frameEdgeImg.raycastTarget = false;

        GameObject portrait = NewUI("Portrait", frame.transform);
        Stretch((RectTransform)portrait.transform, 5f, 5f);
        portraitImage = portrait.AddComponent<Image>();
        portraitImage.preserveAspect = true;
        portraitImage.raycastTarget = false;

        frame.SetActive(false);

        // --- Плашка имени ---
        speakerPlate = NewUI("SpeakerPlate", panel.transform);
        RectTransform plateRect = (RectTransform)speakerPlate.transform;
        plateRect.anchorMin = new Vector2(0f, 1f);
        plateRect.anchorMax = new Vector2(0f, 1f);
        plateRect.pivot = new Vector2(0f, 0f);
        // Плашка поднята над верхним краем панели — приём из визуальных новелл
        plateRect.anchoredPosition = new Vector2(34f, 10f);
        plateRect.sizeDelta = new Vector2(360f, 44f);

        Image plateBg = speakerPlate.AddComponent<Image>();
        plateBg.sprite = roundSmall;
        plateBg.type = Image.Type.Sliced;
        plateBg.color = new Color(0.10f, 0.10f, 0.13f, 0.98f);

        GameObject plateEdge = NewUI("Edge", speakerPlate.transform);
        Stretch((RectTransform)plateEdge.transform);
        Image plateEdgeImg = plateEdge.AddComponent<Image>();
        plateEdgeImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        plateEdgeImg.type = Image.Type.Sliced;
        plateEdgeImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.55f);
        plateEdgeImg.raycastTarget = false;
        // Окантовка растянута по родителю и не должна участвовать в раскладке
        plateEdge.AddComponent<LayoutElement>().ignoreLayout = true;

        speakerLabel = MakeLabel("Name", speakerPlate.transform);
        speakerLabel.fontSize = 22f;
        speakerLabel.fontStyle = FontStyles.Bold;
        speakerLabel.color = speakerNameColor;
        speakerLabel.alignment = TextAlignmentOptions.Left;

        // Плашка сжимается по длине имени, а не висит фиксированным прямоугольником
        HorizontalLayoutGroup plateLayout = speakerPlate.AddComponent<HorizontalLayoutGroup>();
        plateLayout.padding = new RectOffset(18, 18, 5, 5);
        plateLayout.childForceExpandWidth = false;
        plateLayout.childForceExpandHeight = false;
        plateLayout.childControlWidth = true;
        plateLayout.childControlHeight = true;
        plateLayout.childAlignment = TextAnchor.MiddleLeft;

        ContentSizeFitter plateFitter = speakerPlate.AddComponent<ContentSizeFitter>();
        plateFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        plateFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        speakerPlate.SetActive(false);

        // --- Текст реплики ---
        bodyLabel = MakeLabel("Body", panel.transform);
        RectTransform bodyRect = (RectTransform)bodyLabel.transform;
        Stretch(bodyRect);
        bodyRect.offsetMin = new Vector2(34f, 46f);
        bodyRect.offsetMax = new Vector2(-34f, -34f);
        bodyLabel.fontSize = 23f;
        bodyLabel.lineSpacing = 8f;
        bodyLabel.color = textColor;
        bodyLabel.alignment = TextAlignmentOptions.TopLeft;
        bodyLabel.enableWordWrapping = true;

        // --- Индикатор продолжения ---
        continueRoot = NewUI("ContinuePrompt", panel.transform);
        RectTransform contRect = (RectTransform)continueRoot.transform;
        contRect.anchorMin = new Vector2(1f, 0f);
        contRect.anchorMax = new Vector2(1f, 0f);
        contRect.pivot = new Vector2(1f, 0f);
        contRect.anchoredPosition = new Vector2(-28f, 14f);
        contRect.sizeDelta = new Vector2(240f, 28f);

        continueLabel = MakeLabel("Label", continueRoot.transform);
        Stretch((RectTransform)continueLabel.transform);
        continueLabel.text = $"{continueText}  ▸";
        continueLabel.fontSize = 17f;
        continueLabel.alignment = TextAlignmentOptions.Right;
        continueRoot.SetActive(false);

        // --- Варианты ответа ---
        choicesRoot = NewUI("ChoicesPanel", dialogueRoot.transform);
        RectTransform choicesRect = (RectTransform)choicesRoot.transform;
        choicesRect.anchorMin = new Vector2(0f, 0f);
        choicesRect.anchorMax = new Vector2(1f, 0f);
        choicesRect.pivot = new Vector2(0.5f, 0f);
        choicesRect.offsetMin = new Vector2(panelSideMargin + 40f, 0f);
        choicesRect.offsetMax = new Vector2(-panelSideMargin - 40f, 0f);
        choicesRect.sizeDelta = new Vector2(-(panelSideMargin + 40f) * 2f, 300f);
        choicesRect.anchoredPosition = new Vector2(0f, panelBottomMargin + panelHeight + 16f);

        VerticalLayoutGroup choicesLayout = choicesRoot.AddComponent<VerticalLayoutGroup>();
        choicesLayout.spacing = choiceSpacing;
        choicesLayout.childForceExpandWidth = true;
        choicesLayout.childForceExpandHeight = false;
        choicesLayout.childControlHeight = true;
        choicesLayout.childAlignment = TextAnchor.LowerCenter;
        choicesLayout.reverseArrangement = true;

        ContentSizeFitter fitter = choicesRoot.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        choicesContainer = choicesRect;
        choiceTemplate = BuildChoiceTemplate(canvas.transform, roundSmall);

        choicesRoot.SetActive(false);
        dialogueRoot.SetActive(false);

        BuildInteractHint(canvas.transform, roundSmall);
    }

    /// <summary>Плашка «Нажмите E для разговора» над центром экрана.</summary>
    void BuildInteractHint(Transform canvasRoot, Sprite roundSprite)
    {
        GameObject hint = NewUI("InteractHint", canvasRoot);
        RectTransform rect = (RectTransform)hint.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 64f);
        rect.sizeDelta = new Vector2(480f, 46f);

        Image bg = hint.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.03f, 0.035f, 0.045f, 0.72f);
        bg.raycastTarget = false;

        GameObject edge = NewUI("Edge", hint.transform);
        Stretch((RectTransform)edge.transform);
        Image edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        edgeImg.type = Image.Type.Sliced;
        edgeImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0.40f);
        edgeImg.raycastTarget = false;

        interactHintLabel = MakeLabel("Text", hint.transform);
        Stretch((RectTransform)interactHintLabel.transform, 14f, 5f);
        interactHintLabel.fontSize = 20f;
        interactHintLabel.alignment = TextAlignmentOptions.Center;

        interactHintRoot = hint;
        hint.SetActive(false);
    }

    RectTransform MakeBar(string name, Transform parent, bool top, Sprite solid)
    {
        GameObject bar = NewUI(name, parent);
        RectTransform rect = (RectTransform)bar.transform;
        rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
        rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(0f, 0f);

        Image img = bar.AddComponent<Image>();
        img.sprite = solid;
        img.color = Color.black;
        img.raycastTarget = false;
        return rect;
    }

    Button BuildChoiceTemplate(Transform parent, Sprite roundSprite)
    {
        // Шаблон лежит внутри выключенного контейнера, но сам остаётся активным.
        // DialogueManager делает Instantiate без SetActive(true), а Instantiate
        // копирует activeSelf — поэтому выключать сам шаблон нельзя, иначе
        // созданные кнопки окажутся невидимыми.
        GameObject holder = NewUI("ChoiceTemplateHolder", parent);
        holder.SetActive(false);

        GameObject choice = NewUI("ChoiceTemplate", holder.transform);
        RectTransform rect = (RectTransform)choice.transform;
        rect.sizeDelta = new Vector2(600f, choiceHeight);

        LayoutElement le = choice.AddComponent<LayoutElement>();
        le.minHeight = choiceHeight;
        le.preferredHeight = choiceHeight;

        Image bg = choice.AddComponent<Image>();
        bg.sprite = roundSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.09f, 0.095f, 0.12f, 0.95f);

        Button button = choice.AddComponent<Button>();
        button.targetGraphic = bg;
        button.transition = Selectable.Transition.None; // анимацию делает DialogueChoiceButton

        GameObject edge = NewUI("Edge", choice.transform);
        Stretch((RectTransform)edge.transform);
        Image edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = UIShapes.RoundedRect(48, 10, 2);
        edgeImg.type = Image.Type.Sliced;
        edgeImg.color = new Color(1f, 1f, 1f, 0.08f);
        edgeImg.raycastTarget = false;

        // Маркер слева — заполняется акцентом при наведении
        GameObject marker = NewUI("Marker", choice.transform);
        RectTransform markerRect = (RectTransform)marker.transform;
        markerRect.anchorMin = new Vector2(0f, 0f);
        markerRect.anchorMax = new Vector2(0f, 1f);
        markerRect.pivot = new Vector2(0f, 0.5f);
        markerRect.offsetMin = new Vector2(0f, 10f);
        markerRect.offsetMax = new Vector2(3.5f, -10f);
        Image markerImg = marker.AddComponent<Image>();
        markerImg.sprite = UIShapes.Solid();
        markerImg.color = new Color(accentColor.r, accentColor.g, accentColor.b, 0f);
        markerImg.raycastTarget = false;

        // Номер варианта
        TextMeshProUGUI number = MakeLabel("Number", choice.transform);
        RectTransform numRect = (RectTransform)number.transform;
        numRect.anchorMin = new Vector2(0f, 0f);
        numRect.anchorMax = new Vector2(0f, 1f);
        numRect.pivot = new Vector2(0f, 0.5f);
        numRect.offsetMin = new Vector2(16f, 0f);
        numRect.offsetMax = new Vector2(48f, 0f);
        number.text = "1";
        number.fontSize = 17f;
        number.alignment = TextAlignmentOptions.Left;
        number.color = new Color(textColor.r, textColor.g, textColor.b, 0.35f);

        // Текст варианта. DialogueManager ищет его через GetComponentInChildren,
        // поэтому номер добавлен ПЕРЕД ним — иначе менеджер записал бы текст в номер.
        TextMeshProUGUI label = MakeLabel("Label", choice.transform);
        Stretch((RectTransform)label.transform);
        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.offsetMin = new Vector2(50f, 2f);
        labelRect.offsetMax = new Vector2(-20f, -2f);
        label.fontSize = 20f;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.Left;

        var helper = choice.AddComponent<DialogueChoiceButton>();
        helper.background = bg;
        helper.edge = edgeImg;
        helper.marker = markerImg;
        helper.numberLabel = number;
        helper.textLabel = label;

        return button;
    }

    // =====================================================================
    static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rect, float padX = 0f, float padY = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padX, padY);
        rect.offsetMax = new Vector2(-padX, -padY);
    }

    TextMeshProUGUI MakeLabel(string name, Transform parent)
    {
        TextMeshProUGUI label = NewUI(name, parent).AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) label.font = fontAsset;
        label.raycastTarget = false;
        label.richText = true;
        label.color = textColor;
        return label;
    }
}
