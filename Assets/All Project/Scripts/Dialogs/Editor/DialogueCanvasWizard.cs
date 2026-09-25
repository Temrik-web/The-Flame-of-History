using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Мастер канваса: Tools -> Диалоги -> Создать канвас (окно поверх игры).</summary>
public static class DialogueCanvasWizard
{
    private const string SpriteFolder = "Assets/GameData/UI/DialogueSprites";

    // Кэш спрайтов внутри одной сессии выполнения мастера
    private static Sprite buttonSprite;
    private static Sprite hoverSprite;
    private static Sprite pressedSprite;
    private static Sprite solidSprite;

    private const string LegacyNames =
        "DialoguePanel,ChoicesPanel,SpeakerNameText,DialogueText,InteractHint,InteractHintText";

    // =====================================================================
    public static void CreateCanvas()
    {
        // Старый канвас, собранный прошлым запуском (или старым DialogueUI),
        // пересоздаём целиком — чище, чем править руками.
        GameObject existing = GameObject.Find("DialogueCanvas");
        if (existing != null && existing.scene.IsValid())
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("[DialogueCanvas] Старый канвас удалён — создаётся заново.");
        }

        EnsureFolders();
        EnsureEventSystem();
        EnsureDialogueManager();

        buttonSprite = GetOrCreateSprite("RoundedButton", 64, 12, 0);
        solidSprite = GetOrCreateSolidSprite("Solid");
        hoverSprite = GetOrCreateVariant("RoundedButtonHover", 64, 12, RoundedVariant.Hover);
        pressedSprite = GetOrCreateVariant("RoundedButtonPressed", 64, 12, RoundedVariant.Pressed);

        // Стандартный TMP-шрифт (есть кириллица); свой красивый можно вписать
        // в DialogueCanvasUI -> Шрифт, и он применится на весь канвас в Awake.
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/All Project/From Unity(Base)/Settings 1/TextMesh Pro/Resources/Fonts & Materials/" +
            "LiberationSans SDF.asset");
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("InventoryFont SDF");

        Canvas canvas = BuildCanvas(out DialogueCanvasUI ui, out DialogueManager dm, font);
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Диалоги",
                "Канвас не создан. Посмотри консоль — там будет причина.", "Ок");
            return;
        }

        WireManager(dm, ui, ui.windowRoot, ui.speakerNameText, ui.dialogueText,
                    ui.answersColumn, ui.choicesContainer, ui.choiceTemplate,
                    ui.interactHint, ui.interactHintText);

        // Глушим старый интерфейс диалогов, собранный руками в сцене
        DisableLegacyObjects(canvas.transform);

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[DialogueCanvas] Канвас диалогов создан: клик по тексту — продолжить, " +
                  "варианты справа, «Отмена» — выйти. Сохрани сцену (Ctrl+S).");
    }

    // Кубы и цепочки собираются через Tools/Диалоги/«Создать NPC-куб (все диалоги)»
    // (один NPC = вся цепочка на NpcDialogueSequence).

    static void EnsureDialogueManager()
    {
        if (Object.FindObjectOfType<DialogueManager>() != null) return;
        var dmObj = new GameObject("DialogueManager", typeof(DialogueManager));
        Undo.RegisterCreatedObjectUndo(dmObj, "Create DialogueManager");
        Debug.Log("[DialogueCanvas] DialogueManager добавлен в сцену.");
    }

    // =====================================================================
    // Сборка канваса
    // =====================================================================
    static Canvas BuildCanvas(out DialogueCanvasUI ui, out DialogueManager dm, TMP_FontAsset font)
    {
        ui = null;
        dm = Object.FindObjectOfType<DialogueManager>();

        // Canvas
        var canvasObj = new GameObject("DialogueCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObj, "Create dialogue canvas");

        Canvas canvas = canvasObj.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90; // под инвентарём (100)

        CanvasScaler scaler = canvasObj.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Окно диалога (корень) — скрыто, пока идёт разговор
        GameObject window = NewUi("DialogueWindow", canvas.transform);
        Stretch((RectTransform)window.transform);
        CanvasGroup group = window.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // Окно интерфейса — одна плашка ПОВЕРХ игры, не на весь экран.
        // Выдели DialogueBox и двигай его в Scene view, чтобы поставить куда нужно.
        GameObject box = NewUi("DialogueBox", window.transform);
        RectTransform boxRect = (RectTransform)box.transform;
        boxRect.anchorMin = boxRect.anchorMax = new Vector2(0.5f, 0.5f);
        boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.anchoredPosition = Vector2.zero;
        boxRect.sizeDelta = new Vector2(1580f, 860f);

        // Фон окна: сюда вставляешь своё фото/арт —
        // спрайт меняется на DialogueCanvasUI -> «Фон меню».
        Image boxBg = AddImage(box, GetOrCreateGradientSprite("MenuBackground", 256,
            new Color(0.08f, 0.11f, 0.17f), new Color(0.23f, 0.14f, 0.11f)), Color.white);
        boxBg.type = Image.Type.Simple;

        // Тонкая рамка по краю окна (не участвует в раскладке)
        Sprite ringSprite = GetOrCreateSprite("RoundedRing", 64, 12, 3);
        GameObject edge = NewUi("RoundedEdge", box.transform);
        Stretch((RectTransform)edge.transform);
        Image edgeImg = AddImage(edge, ringSprite, new Color(1f, 1f, 1f, 0.30f));
        edgeImg.type = Image.Type.Sliced;
        edgeImg.raycastTarget = false;
        edge.AddComponent<LayoutElement>().ignoreLayout = true;

        // ---- Слева снизу окна: имя говорящего ----
        GameObject plate = NewUi("SpeakerPlate", box.transform);
        RectTransform plateRect = (RectTransform)plate.transform;
        plateRect.anchorMin = new Vector2(0f, 0f);
        plateRect.anchorMax = new Vector2(0f, 0f);
        plateRect.pivot = new Vector2(0f, 0f);
        plateRect.anchoredPosition = new Vector2(70f, 58f);

        Image plateBg = AddImage(plate, buttonSprite, new Color(0.07f, 0.075f, 0.10f, 0.96f));
        plateBg.type = Image.Type.Sliced;
        plateBg.raycastTarget = false;

        HorizontalLayoutGroup plateLayout = plate.AddComponent<HorizontalLayoutGroup>();
        plateLayout.padding = new RectOffset(20, 20, 8, 8);
        plateLayout.childControlWidth = false;
        plateLayout.childForceExpandWidth = false;
        plateLayout.childForceExpandHeight = true;
        plateLayout.childAlignment = TextAnchor.MiddleLeft;

        ContentSizeFitter plateFitter = plate.AddComponent<ContentSizeFitter>();
        plateFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        plateFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI nameLabel = AddLabel(
            NewUi("Text", plate.transform), font, "Собеседник", 24,
            new Color(1f, 0.78f, 0.42f), TextAlignmentOptions.Left);
        nameLabel.raycastTarget = false;

        // ---- Слева: текст реплики (клик по нему = продолжить) ----
        GameObject textArea = NewUi("TextArea", box.transform);
        RectTransform areaRect = (RectTransform)textArea.transform;
        areaRect.anchorMin = new Vector2(0f, 0f);
        areaRect.anchorMax = new Vector2(0.53f, 1f);
        areaRect.offsetMin = new Vector2(70f, 190f);  // снизу выше плашки имени
        areaRect.offsetMax = new Vector2(0f, -56f);   // сверху отступ

        // Невидимая кнопка-продолжение под текстом
        GameObject continueBtn = NewUi("ContinueButton", textArea.transform);
        Stretch((RectTransform)continueBtn.transform);
        Image contImg = AddImage(continueBtn, buttonSprite, new Color(1f, 1f, 1f, 0.01f));
        contImg.type = Image.Type.Sliced;
        Button contButton = continueBtn.AddComponent<Button>();
        contButton.transition = Selectable.Transition.None;
        contButton.targetGraphic = contImg;
        DialogueCanvasButton contStyle = continueBtn.AddComponent<DialogueCanvasButton>();
        contStyle.target = contImg;
        contStyle.normalColor = new Color(1f, 1f, 1f, 0.01f);
        contStyle.hoverColor = new Color(1f, 1f, 1f, 0.06f);
        contStyle.pressedColor = new Color(1f, 1f, 1f, 0.06f);
        contStyle.hoverScale = 1.004f;
        contStyle.pressedScale = 0.998f;

        GameObject body = NewUi("DialogueText", textArea.transform);
        Stretch((RectTransform)body.transform, 24f, 14f, 34f, 18f);
        TextMeshProUGUI bodyLabel = AddLabel(
            body, font, "", 26, new Color(0.94f, 0.95f, 0.97f), TextAlignmentOptions.TopLeft);
        bodyLabel.enableWordWrapping = true;
        bodyLabel.lineSpacing = 10f;
        bodyLabel.raycastTarget = false;

        // Подсказка «жми, чтобы продолжить»
        GameObject prompt = NewUi("ContinuePrompt", textArea.transform);
        RectTransform promptRect = (RectTransform)prompt.transform;
        promptRect.anchorMin = promptRect.anchorMax = new Vector2(1f, 0f);
        promptRect.pivot = new Vector2(1f, 0f);
        promptRect.anchoredPosition = new Vector2(-16f, 22f);
        promptRect.sizeDelta = new Vector2(250f, 28f);
        TextMeshProUGUI promptLabel = AddLabel(
            NewUi("Text", prompt.transform), font, "Продолжить >", 17,
            new Color(0.94f, 0.95f, 0.97f, 0.8f), TextAlignmentOptions.Right);
        Stretch((RectTransform)promptLabel.transform);
        promptLabel.raycastTarget = false;
        prompt.SetActive(false);

        // ---- Справа: колонка вариантов и отмены ----
        GameObject column = NewUi("AnswersColumn", box.transform);
        RectTransform columnRect = (RectTransform)column.transform;
        columnRect.anchorMin = columnRect.anchorMax = new Vector2(1f, 1f);
        columnRect.pivot = new Vector2(1f, 1f);
        columnRect.anchoredPosition = new Vector2(-64f, -64f);
        columnRect.sizeDelta = new Vector2(580f, 220f);

        VerticalLayoutGroup columnLayout = column.AddComponent<VerticalLayoutGroup>();
        columnLayout.spacing = 14f;
        columnLayout.childAlignment = TextAnchor.UpperRight;
        columnLayout.childControlWidth = true;
        columnLayout.childForceExpandWidth = false;
        columnLayout.childControlHeight = true;
        columnLayout.childForceExpandHeight = false;

        ContentSizeFitter columnFitter = column.AddComponent<ContentSizeFitter>();
        columnFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Контейнер кнопок вариантов — в него DialogueManager кладёт шаблон
        GameObject choices = NewUi("ChoicesPanel", column.transform);
        RectTransform choicesRect = (RectTransform)choices.transform;
        choicesRect.sizeDelta = new Vector2(660f, 0f);

        LayoutElement choicesLayoutEl = choices.AddComponent<LayoutElement>();
        choicesLayoutEl.minWidth = 560f;
        choicesLayoutEl.preferredWidth = 660f;

        VerticalLayoutGroup choicesLayout = choices.AddComponent<VerticalLayoutGroup>();
        choicesLayout.spacing = 10f;
        choicesLayout.childAlignment = TextAnchor.UpperCenter;
        choicesLayout.childControlWidth = true;
        choicesLayout.childForceExpandWidth = true;
        choicesLayout.childControlHeight = true;
        choicesLayout.childForceExpandHeight = false;

        ContentSizeFitter choicesFitter = choices.AddComponent<ContentSizeFitter>();
        choicesFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Шаблон кнопки варианта (в неактивном хозяине, вне раскладки)
        GameObject holder = NewUi("ChoiceTemplateHolder", column.transform);
        holder.SetActive(false);

        GameObject choice = NewUi("ChoiceTemplate", holder.transform);
        RectTransform choiceRect = (RectTransform)choice.transform;
        choiceRect.sizeDelta = new Vector2(660f, 56f);

        Image choiceBg = AddImage(choice, buttonSprite, new Color(0.09f, 0.10f, 0.13f, 0.96f));
        choiceBg.type = Image.Type.Sliced;

        Button choiceButton = choice.AddComponent<Button>();
        choiceButton.transition = Selectable.Transition.None;
        choiceButton.targetGraphic = choiceBg;

        LayoutElement choiceLayoutEl = choice.AddComponent<LayoutElement>();
        choiceLayoutEl.minHeight = 56f;

        VerticalLayoutGroup choiceInner = choice.AddComponent<VerticalLayoutGroup>();
        choiceInner.padding = new RectOffset(22, 22, 12, 12);
        choiceInner.childControlWidth = true;
        choiceInner.childForceExpandWidth = true;
        choiceInner.childControlHeight = true;
        choiceInner.childForceExpandHeight = false;
        choiceInner.childAlignment = TextAnchor.MiddleLeft;

        GameObject textHolder = NewUi("TextHolder", choice.transform);
        ContentSizeFitter textHolderFitter = textHolder.AddComponent<ContentSizeFitter>();
        textHolderFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI choiceLabel = AddLabel(
            NewUi("Text", textHolder.transform), font, "Вариант ответа", 21,
            new Color(0.96f, 0.97f, 0.99f), TextAlignmentOptions.MidlineLeft);
        // Текст растягиваем по всей ширине кнопки и равняем по левому краю —
        // иначе он сидит в точке якоря и выглядит как «центр кнопки».
        Stretch((RectTransform)choiceLabel.transform);
        choiceLabel.enableWordWrapping = true;
        choiceLabel.raycastTarget = false;

        DialogueCanvasButton choiceStyle = choice.AddComponent<DialogueCanvasButton>();
        choiceStyle.target = choiceBg;
        choiceStyle.normalSprite = buttonSprite;
        choiceStyle.hoverSprite = hoverSprite;
        choiceStyle.pressedSprite = pressedSprite;

        // ---- Отмена: справа ниже вариантов ----
        GameObject cancel = NewUi("CancelButton", column.transform);
        RectTransform cancelRect = (RectTransform)cancel.transform;
        cancelRect.sizeDelta = new Vector2(280f, 54f);

        Image cancelBg = AddImage(cancel, buttonSprite, new Color(0.18f, 0.10f, 0.10f, 0.96f));
        cancelBg.type = Image.Type.Sliced;

        Button cancelButton = cancel.AddComponent<Button>();
        cancelButton.transition = Selectable.Transition.None;
        cancelButton.targetGraphic = cancelBg;

        LayoutElement cancelLayoutEl = cancel.AddComponent<LayoutElement>();
        cancelLayoutEl.minHeight = 54f;
        cancelLayoutEl.preferredWidth = 280f;

        TextMeshProUGUI cancelLabel = AddLabel(
            NewUi("Text", cancel.transform), font, "Отмена", 20,
            new Color(0.95f, 0.9f, 0.9f), TextAlignmentOptions.Center);
        Stretch((RectTransform)cancelLabel.transform);
        cancelLabel.raycastTarget = false;

        DialogueCanvasButton cancelStyle = cancel.AddComponent<DialogueCanvasButton>();
        cancelStyle.target = cancelBg;
        cancelStyle.normalSprite = buttonSprite;
        cancelStyle.hoverSprite = hoverSprite;
        cancelStyle.pressedSprite = pressedSprite;
        cancelStyle.normalColor = new Color(0.18f, 0.10f, 0.10f, 0.96f);
        cancelStyle.hoverColor = new Color(0.45f, 0.15f, 0.13f, 1f);
        cancelStyle.pressedColor = new Color(0.62f, 0.13f, 0.11f, 1f);

        // ---- Подсказка «Нажмите E», вне окна (до начала диалога) ----
        GameObject hint = NewUi("InteractHint", canvas.transform);
        RectTransform hintRect = (RectTransform)hint.transform;
        hintRect.anchorMin = new Vector2(0.5f, 0f);
        hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 40f);
        hintRect.sizeDelta = new Vector2(460f, 46f);

        Image hintBg = AddImage(hint, buttonSprite, new Color(0.03f, 0.035f, 0.045f, 0.72f));
        hintBg.type = Image.Type.Sliced;
        hintBg.raycastTarget = false;

        TextMeshProUGUI hintLabel = AddLabel(
            NewUi("Text", hint.transform), font, "Нажмите E для разговора", 20,
            new Color(0.94f, 0.95f, 0.97f), TextAlignmentOptions.Center);
        Stretch((RectTransform)hintLabel.transform);
        hintLabel.raycastTarget = false;
        hint.SetActive(false);

        // ---- Компонент логики канваса ----
        if (canvasObj.GetComponent<DialogueCanvasUI>() == null)
            canvasObj.AddComponent<DialogueCanvasUI>();

        // Ссылки: заполняем самому компоненту (инспектор) и менеджеру
        DialogueCanvasUI uiComp = canvasObj.GetComponent<DialogueCanvasUI>();
        uiComp.manager = dm;
        uiComp.windowRoot = window;
        uiComp.windowGroup = group;
        uiComp.speakerNameText = nameLabel;
        uiComp.dialogueText = bodyLabel;
        uiComp.continueButton = contButton;
        uiComp.continuePrompt = prompt;
        uiComp.answersColumn = column;
        uiComp.choicesContainer = choices.transform;
        uiComp.choiceTemplate = choiceButton;
        uiComp.cancelButton = cancelButton;
        uiComp.interactHint = hint;
        uiComp.interactHintText = hintLabel;
        uiComp.backgroundImage = boxBg;
        uiComp.fontAsset = font;

        EditorUtility.SetDirty(canvasObj);

        ui = uiComp;
        return canvas;
    }

    // =====================================================================
    static void WireManager(DialogueManager dm, DialogueCanvasUI ui, GameObject window,
        TextMeshProUGUI nameLabel, TextMeshProUGUI bodyLabel, GameObject column,
        Transform choicesContainer, Button choiceTemplate, GameObject hint, TextMeshProUGUI hintLabel)
    {
        if (dm == null) return;

        dm.dialoguePanel = window;
        dm.dialogueCanvasGroup = ui.windowGroup != null
            ? ui.windowGroup
            : window.GetComponent<CanvasGroup>();
        dm.speakerNameText = nameLabel;
        dm.dialogueText = bodyLabel;
        dm.choicesPanel = column;
        dm.choicesContainer = choicesContainer;
        dm.choiceButtonPrefab = choiceTemplate;
        dm.interactHint = hint;
        dm.interactHintText = hintLabel;
        dm.panelStartScale = Vector3.one;

        EditorUtility.SetDirty(dm);
    }

    /// <summary>Выключить объекты старого UI, собранные руками в сцене.</summary>
    static void DisableLegacyObjects(Transform canvasRoot)
    {
        foreach (string name in LegacyNames.Split(','))
        {
            GameObject legacy = GameObject.Find(name.Trim());
            if (legacy == null) continue;
            if (legacy.transform.IsChildOf(canvasRoot)) continue;
            if (legacy.scene.name == null) continue;

            legacy.SetActive(false);
            Debug.Log($"[DialogueCanvas] Старый элемент «{name}» выключен.");
        }
    }

    // =====================================================================
    // Хелперы
    // =====================================================================
    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/GameData"))
            AssetDatabase.CreateFolder("Assets", "GameData");
        if (!AssetDatabase.IsValidFolder("Assets/GameData/UI"))
            AssetDatabase.CreateFolder("Assets/GameData", "UI");
        if (!AssetDatabase.IsValidFolder(SpriteFolder))
            AssetDatabase.CreateFolder("Assets/GameData/UI", "DialogueSprites");
    }

    static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
        Debug.Log("[DialogueCanvas] EventSystem добавлен — канвасу нужен модуль ввода.");
    }

    static GameObject NewUi(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Dialogue canvas element");
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rect, float padX = 0f, float padY = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padX, padY);
        rect.offsetMax = new Vector2(-padX, -padY);
    }

    static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    static Image AddImage(GameObject go, Sprite sprite, Color color)
    {
        Image img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Sliced;
        img.color = color;
        return img;
    }

    static TextMeshProUGUI AddLabel(GameObject go, TMP_FontAsset font, string text, int size,
        Color color, TextAlignmentOptions align)
    {
        var label = go.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.richText = true;
        return label;
    }

    // =====================================================================
    // Спрайты как ассеты проекта — иначе сцена потеряет ссылки после перезапуска
    // =====================================================================
    /// <summary>Варианты скруглённого фона.</summary>
    enum RoundedVariant
    {
        Normal,  // ровная заливка
        Hover,   // глянцевая подсветка сверху
        Pressed  // тёмный фон + светящийся ореол по контуру
    }

    static Sprite GetOrCreateSprite(string name, int size, int radius, int border)
    {
        string file = $"{SpriteFolder}/{name}.png";

        if (!File.Exists(file))
        {
            WriteRoundedPng(file, size, radius, border);
            AssetDatabase.ImportAsset(file);
        }

        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(file);
        if (ti == null)
        {
            AssetDatabase.ImportAsset(file);
            ti = (TextureImporter)AssetImporter.GetAtPath(file);
        }

        ConfigureImporter(ti, size, radius);
        ti.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(file);
    }

    /// <summary>Вертикально-градиентная картинка-заглушка для фона меню.</summary>
    static Sprite GetOrCreateGradientSprite(string name, int size, Color top, Color bottom)
    {
        string file = $"{SpriteFolder}/{name}.png";

        if (!File.Exists(file))
        {
            WriteGradientPng(file, size, top, bottom);
            AssetDatabase.ImportAsset(file);
        }

        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(file);
        if (ti == null)
        {
            AssetDatabase.ImportAsset(file);
            ti = (TextureImporter)AssetImporter.GetAtPath(file);
        }

        ConfigureImporter(ti, size, 0);
        ti.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(file);
    }

    static void WriteGradientPng(string path, int size, Color top, Color bottom)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        Color32 cc = default;
        for (int y = 0; y < size; y++)
        {
            float f = Mathf.Clamp01((float)y / (size - 1));
            cc = Color.Lerp(top, bottom, f);
            for (int x = 0; x < size; x++)
                px[y * size + x] = cc;
        }
        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static Sprite GetOrCreateVariant(string name, int size, int radius, RoundedVariant variant)
    {
        string file = $"{SpriteFolder}/{name}.png";

        if (!File.Exists(file))
        {
            WriteRoundedPng(file, size, radius, 0, variant);
            AssetDatabase.ImportAsset(file);
        }

        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(file);
        if (ti == null)
        {
            AssetDatabase.ImportAsset(file);
            ti = (TextureImporter)AssetImporter.GetAtPath(file);
        }

        ConfigureImporter(ti, size, radius);
        ti.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(file);
    }

    static Sprite GetOrCreateSolidSprite(string name)
    {
        string file = $"{SpriteFolder}/{name}.png";

        if (!File.Exists(file))
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(file);
        }

        TextureImporter solidTi = (TextureImporter)AssetImporter.GetAtPath(file);
        if (solidTi == null)
        {
            AssetDatabase.ImportAsset(file);
            solidTi = (TextureImporter)AssetImporter.GetAtPath(file);
        }
        ConfigureImporter(solidTi, 4, 0);
        solidTi.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(file);
    }

    static void ConfigureImporter(TextureImporter ti, int size, int radius)
    {
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Bilinear;
        // Бордюр для 9-slice: по радиусу со всех сторон
        int slice = Mathf.Max(1, radius);
        ti.spriteBorder = new Vector4(slice, slice, slice, slice);
    }

    /// <summary>Записать PNG скруглённого прямоугольника (тот же стиль, что UIShapes).</summary>
    static void WriteRoundedPng(string path, int size, int radius, int border,
        RoundedVariant variant = RoundedVariant.Normal)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float outer = Coverage(x, y, size, radius);
                float alpha = outer;

                if (border > 0)
                {
                    float inner = Coverage(x - border, y - border, size - border * 2,
                        Mathf.Max(0, radius - border));
                    alpha = Mathf.Clamp01(outer - inner);
                }

                switch (variant)
                {
                    case RoundedVariant.Hover:
                        // Глянцевый «свет» сверху — кнопка будто подсвечена
                        alpha *= Mathf.Lerp(0.5f, 1f, Mathf.Clamp01((y + 0.5f) / size));
                        break;
                    case RoundedVariant.Pressed:
                        // Светящийся ореол по контуру + затемнённый центр
                        float glow = Coverage(x, y, size, radius + 4) - outer;
                        alpha = Mathf.Clamp01(outer * 0.9f + glow * 0.6f);
                        break;
                }

                byte a = (byte)Mathf.RoundToInt(alpha * 255f);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }

    static float Coverage(float x, float y, int size, int radius)
    {
        if (size <= 0f) return 0f;
        if (x < -1f || y < -1f || x > size || y > size) return 0f;

        float px = x + 0.5f;
        float py = y + 0.5f;
        float cx = Mathf.Clamp(px, radius, size - radius);
        float cy = Mathf.Clamp(py, radius, size - radius);
        float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));

        if (radius <= 0f)
            return (px >= 0f && px <= size && py >= 0f && py <= size) ? 1f : 0f;

        return Mathf.Clamp01(radius - dist + 0.5f);
    }
}
