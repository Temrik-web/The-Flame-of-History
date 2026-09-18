using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Редактор интерфейса диалогов: Tools -> Диалоги -> Редактор интерфейса.
///
/// Канвас диалогов живёт в сцене (создаётся мастером «Создать канвас»),
/// поэтому каждый его элемент можно выбрать и покрутить руками.
/// Это окно — пульт для этого:
///  - список всех элементов, которые создаёт код/мастер, с кнопкой «Выбрать»
///    (объект подсвечивается в Hierarchy и Scene);
///  - общие стили (цвета, шрифт, размеры) с кнопкой «Применить»;
///  - показ/скрытие шаблона кнопки варианта (он лежит в выключенном контейнере).
///
/// Важно: кнопки вариантов в игре — клоны шаблона ChoiceTemplate.
/// Правь сам шаблон — все ответы в игре унаследуют вид.
///
/// Если в сцене только кодовый DialogueUI (строит всё на лету),
/// сначала нажми «Создать канвас» — появится редактируемая версия.
/// </summary>
public class DialogueUIEditor : EditorWindow
{
    private DialogueCanvasUI ui;

    private Vector2 scroll;

    // Стили (подтягиваются из сцены кнопкой «Обновить»)
    private Color bgColor = Color.white;
    private Color nameColor = new Color(1f, 0.78f, 0.42f);
    private Color textColor = new Color(0.94f, 0.95f, 0.97f);
    private Color btnNormal = new Color(0.09f, 0.10f, 0.13f, 0.96f);
    private Color btnHover = new Color(0.21f, 0.23f, 0.29f, 1f);
    private Color btnPressed = new Color(0.32f, 0.24f, 0.13f, 1f);
    private TMP_FontAsset font;
    private float nameSize = 24f;
    private float bodySize = 26f;
    private float choiceSize = 21f;
    private bool stylesLoaded;

    [MenuItem("Tools/Диалоги/Редактор интерфейса", false, 1)]
    public static void Open()
    {
        GetWindow<DialogueUIEditor>("Интерфейс диалогов");
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Канвас диалогов", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        ui = (DialogueCanvasUI)EditorGUILayout.ObjectField(ui, typeof(DialogueCanvasUI), true);
        if (GUILayout.Button("Найти", GUILayout.Width(70)))
        {
            ui = FindObjectOfType<DialogueCanvasUI>();
            stylesLoaded = false;
        }
        if (GUILayout.Button("Создать", GUILayout.Width(70)))
        {
            DialogueCanvasWizard.CreateCanvas();
            ui = FindObjectOfType<DialogueCanvasUI>();
            stylesLoaded = false;
        }
        EditorGUILayout.EndHorizontal();

        if (ui == null)
        {
            EditorGUILayout.HelpBox(
                "Канвас не найден. Нажми «Создать» — мастер соберёт редактируемый " +
                "интерфейс в сцену (окно, имя, текст, варианты, отмена, подсказка E).",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        if (!stylesLoaded)
        {
            LoadStyles();
            stylesLoaded = true;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Элементы (кнопка — выбрать в сцене)", EditorStyles.boldLabel);
        ElementRow("Окно диалога", ui.windowRoot);
        ElementRow("Фон окна", ui.backgroundImage != null ? ui.backgroundImage.gameObject : FindChild("DialogueBox"));
        ElementRow("Плашка имени", FindChild("SpeakerPlate"));
        ElementRow("Имя говорящего", ui.speakerNameText != null ? ui.speakerNameText.gameObject : null);
        ElementRow("Текст реплики", ui.dialogueText != null ? ui.dialogueText.gameObject : null);
        ElementRow("Кнопка-продолжение", FindChild("ContinueButton"));
        ElementRow("Подсказка продолжения", ui.continuePrompt);
        ElementRow("Колонка ответов", ui.answersColumn);
        ElementRow("Контейнер вариантов", ui.choicesContainer != null ? ui.choicesContainer.gameObject : null);
        ElementRow("Шаблон варианта", FindTemplate());
        ElementRow("Кнопка «Отмена»", ui.cancelButton != null ? ui.cancelButton.gameObject : null);
        ElementRow("Подсказка «Нажмите E»", ui.interactHint);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Шаблон кнопки варианта", EditorStyles.boldLabel);
        GameObject holder = FindChild("ChoiceTemplateHolder");
        bool holderActive = holder != null && holder.activeSelf;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(holderActive ? "Шаблон ВИДЕН в сцене" : "Шаблон скрыт", GUILayout.ExpandWidth(true));
        if (GUILayout.Button(holderActive ? "Скрыть" : "Показать", GUILayout.Width(90)))
        {
            if (holder != null)
            {
                Undo.RecordObject(holder, "Показать шаблон варианта");
                holder.SetActive(!holderActive);
            }
        }
        EditorGUILayout.EndHorizontal();
        if (holderActive)
            EditorGUILayout.HelpBox(
                "Не забудь Скрыть шаблон перед запуском игры, иначе в углу будет " +
                "висеть лишняя кнопка-образец.",
                MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Общий стиль", EditorStyles.boldLabel);
        bgColor = EditorGUILayout.ColorField("Фон окна", bgColor);
        nameColor = EditorGUILayout.ColorField("Имя говорящего", nameColor);
        textColor = EditorGUILayout.ColorField("Текст реплики", textColor);
        btnNormal = EditorGUILayout.ColorField("Кнопки: обычная", btnNormal);
        btnHover = EditorGUILayout.ColorField("Кнопки: наведение", btnHover);
        btnPressed = EditorGUILayout.ColorField("Кнопки: нажатие", btnPressed);
        font = (TMP_FontAsset)EditorGUILayout.ObjectField("Шрифт (все надписи)", font, typeof(TMP_FontAsset), false);
        nameSize = EditorGUILayout.FloatField("Размер: имя", nameSize);
        bodySize = EditorGUILayout.FloatField("Размер: текст", bodySize);
        choiceSize = EditorGUILayout.FloatField("Размер: варианты", choiceSize);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Применить стиль"))
            ApplyStyles();
        if (GUILayout.Button("Обновить из сцены", GUILayout.Width(140)))
            LoadStyles();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Кнопки ответов в игре — клоны шаблона ChoiceTemplate: цвет, шрифт и " +
            "размер меняются в шаблоне и у кнопок «Отмена»/вариантов через стиль выше. " +
            "Положение и размер окна меняются прямо в Scene view.",
            MessageType.None);

        EditorGUILayout.EndScrollView();
    }

    // =====================================================================
    void ElementRow(string label, GameObject go)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.ExpandWidth(true));
        using (new EditorGUI.DisabledScope(go == null))
        {
            if (GUILayout.Button(go == null ? "нет" : "Выбрать", GUILayout.Width(80)) && go != null)
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    Transform FindRoot()
    {
        if (ui == null) return null;
        return ui.transform;
    }

    GameObject FindChild(string name)
    {
        Transform root = FindRoot();
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name) return t.gameObject;
        }
        return null;
    }

    GameObject FindTemplate()
    {
        if (ui != null && ui.choiceTemplate != null) return ui.choiceTemplate.gameObject;
        return FindChild("ChoiceTemplate");
    }

    // =====================================================================
    void LoadStyles()
    {
        if (ui == null) return;

        if (ui.backgroundImage != null) bgColor = ui.backgroundImage.color;
        if (ui.speakerNameText != null)
        {
            nameColor = ui.speakerNameText.color;
            nameSize = ui.speakerNameText.fontSize;
        }
        if (ui.dialogueText != null)
        {
            textColor = ui.dialogueText.color;
            bodySize = ui.dialogueText.fontSize;
        }
        font = ui.fontAsset;

        GameObject template = FindTemplate();
        if (template != null)
        {
            var style = template.GetComponent<DialogueCanvasButton>();
            if (style != null)
            {
                btnNormal = style.normalColor;
                btnHover = style.hoverColor;
                btnPressed = style.pressedColor;
            }
            var label = template.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) choiceSize = label.fontSize;
        }
    }

    void ApplyStyles()
    {
        if (ui == null) return;

        Undo.RecordObject(ui, "Стиль интерфейса диалогов");

        if (ui.backgroundImage != null)
        {
            Undo.RecordObject(ui.backgroundImage, "Фон окна диалога");
            ui.backgroundImage.color = bgColor;
        }
        if (ui.speakerNameText != null)
        {
            Undo.RecordObject(ui.speakerNameText, "Имя говорящего");
            ui.speakerNameText.color = nameColor;
            ui.speakerNameText.fontSize = Mathf.Max(1f, nameSize);
        }
        if (ui.dialogueText != null)
        {
            Undo.RecordObject(ui.dialogueText, "Текст реплики");
            ui.dialogueText.color = textColor;
            ui.dialogueText.fontSize = Mathf.Max(1f, bodySize);
        }

        // Шрифт — на все надписи канваса сразу
        if (font != null)
        {
            ui.fontAsset = font;
            foreach (TextMeshProUGUI label in ui.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                Undo.RecordObject(label, "Шрифт диалогов");
                label.font = font;
            }
        }

        // Шаблон варианта: цвета состояний + размер текста
        GameObject template = FindTemplate();
        if (template != null)
        {
            var style = template.GetComponent<DialogueCanvasButton>();
            if (style != null)
            {
                Undo.RecordObject(style, "Цвета кнопки варианта");
                style.normalColor = btnNormal;
                style.hoverColor = btnHover;
                style.pressedColor = btnPressed;
                if (style.target != null)
                {
                    Undo.RecordObject(style.target, "Фон кнопки варианта");
                    style.target.color = btnNormal;
                }
            }
            var label = template.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                Undo.RecordObject(label, "Текст варианта");
                label.fontSize = Mathf.Max(1f, choiceSize);
            }
        }

        // Кнопка «Отмена» — те же цвета наведения
        if (ui.cancelButton != null)
        {
            var cancelStyle = ui.cancelButton.GetComponent<DialogueCanvasButton>();
            if (cancelStyle != null)
            {
                Undo.RecordObject(cancelStyle, "Цвета кнопки Отмена");
                cancelStyle.hoverColor = btnHover;
                cancelStyle.pressedColor = btnPressed;
            }
        }

        EditorUtility.SetDirty(ui);
        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Debug.Log("[DialogueUIEditor] Стиль применён. Сохрани сцену (Ctrl+S).");
    }
}
