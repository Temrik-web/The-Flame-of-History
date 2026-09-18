using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// История диалога (бэклог как в визуальных новеллах).
/// Открывается клавишей H в любой момент: кто что говорил, имена в цветах персонажей.
/// Пишет текущий диалог в память + в PlayerPrefs, переживает перезапуск.
///
/// Вешается рядом с DialogueManager. Если его нет — DialogueManager добавит сам.
/// </summary>
[DisallowMultipleComponent]
public class DialogueHistory : MonoBehaviour
{
    struct Entry
    {
        public string speaker;
        public string text;
        public Color color;
    }

    [Header("Управление")]
    public KeyCode toggleKey = KeyCode.H;

    [Header("Оформление")]
    [Tooltip("TMP-шрифт с кириллицей. Если пусто — Resources/InventoryFont SDF.")]
    public TMP_FontAsset fontAsset;
    public Color panelColor = new Color(0.04f, 0.045f, 0.06f, 0.96f);
    public Color textColor = new Color(0.92f, 0.93f, 0.95f);

    const int MaxEntries = 200;   // в памяти
    const int SaveEntries = 100;  // в PlayerPrefs
    const string Prefix = "flame_hist_";

    private readonly List<Entry> entries = new List<Entry>();
    private readonly HashSet<string> touchedKeys = new HashSet<string>();
    private string loadedKey = "";

    private DialogueManager manager;
    private Canvas canvas;
    private GameObject panelRoot;
    private TextMeshProUGUI logText;
    private ScrollRect scroll;

    void Awake()
    {
        manager = GetComponent<DialogueManager>();
        if (fontAsset == null)
            fontAsset = Resources.Load<TMP_FontAsset>("InventoryFont SDF");
        BuildUI();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Toggle();
    }

    /// <summary>Показать/скрыть историю текущего диалога.</summary>
    public void Toggle()
    {
        if (panelRoot == null) return;
        bool show = !panelRoot.activeSelf;
        if (show) RebuildText();
        panelRoot.SetActive(show);
    }

    public void Show() { if (panelRoot != null && !panelRoot.activeSelf) Toggle(); }
    public void Hide() { if (panelRoot != null && panelRoot.activeSelf) Toggle(); }

    /// <summary>
    /// Записать реплику. Вызывает DialogueManager при переходе к узлу.
    /// Пустые служебные узлы (без имени и текста) пропускаем.
    /// </summary>
    public void Record(string dialogueKey, string speaker, string text, Color color)
    {
        if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(speaker)) return;

        if (dialogueKey != loadedKey)
            LoadFor(dialogueKey);

        // Дедуп: при продолжении с того же узла узел запишется повторно
        if (entries.Count > 0)
        {
            Entry last = entries[entries.Count - 1];
            if (last.speaker == (speaker ?? "") && last.text == (text ?? ""))
                return;
        }

        entries.Add(new Entry { speaker = speaker ?? "", text = text ?? "", color = color });
        while (entries.Count > MaxEntries)
            entries.RemoveAt(0);

        SaveFor(dialogueKey);

        if (panelRoot != null && panelRoot.activeSelf)
            RebuildText();
    }

    /// <summary>Строка истории для внешнего показа (чат подтягивает сейв).</summary>
    public struct HistoryLine
    {
        public string speaker;
        public string text;
        public Color color;
    }

    /// <summary>Все сохранённые реплики диалога (копия).</summary>
    public List<HistoryLine> GetLines(string dialogueKey)
    {
        if (dialogueKey != loadedKey)
            LoadFor(dialogueKey);
        var result = new List<HistoryLine>(entries.Count);
        foreach (Entry e in entries)
            result.Add(new HistoryLine { speaker = e.speaker, text = e.text, color = e.color });
        return result;
    }

    /// <summary>Сколько реплик сохранено для диалога (для дебага/тестов).</summary>
    public int CountFor(string dialogueKey)
    {
        if (dialogueKey != loadedKey)
            LoadFor(dialogueKey);
        return entries.Count;
    }

    // =====================================================================
    // Текст лога: «Имя: реплика», имя в цвете персонажа
    // =====================================================================
    void RebuildText()
    {
        if (logText == null) return;

        var sb = new System.Text.StringBuilder();
        foreach (Entry e in entries)
        {
            if (!string.IsNullOrEmpty(e.speaker))
            {
                sb.Append("<color=").Append(ColorToHex(e.color)).Append("><b>")
                  .Append(Escape(e.speaker)).Append("</b></color>: ");
            }
            sb.Append(Escape(e.text)).Append("\n\n");
        }
        if (entries.Count == 0)
            sb.Append("<i>Пока пусто — поговори с кем-нибудь.</i>");

        logText.text = sb.ToString();

        // Мотаем вниз к свежим репликам
        if (scroll != null)
        {
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
        }
    }

    static string ColorToHex(Color c)
    {
        Color32 c32 = c;
        return string.Format("#{0:X2}{1:X2}{2:X2}", c32.r, c32.g, c32.b);
    }

    // TMP rich text: экранируем только угловые скобки, остальное (ё, тире) можно как есть
    static string Escape(string s) => (s ?? "").Replace("<", "&lt;").Replace(">", "&gt;");

    // =====================================================================
    // Сохранение: записи разделяем \x1E, внутри — \x1F (имя/текст)
    // =====================================================================
    void LoadFor(string dialogueKey)
    {
        entries.Clear();
        loadedKey = dialogueKey ?? "";
        if (string.IsNullOrEmpty(loadedKey)) return;

        string raw = PlayerPrefs.GetString(Prefix + loadedKey, "");
        if (string.IsNullOrEmpty(raw)) return;

        foreach (string rec in raw.Split('\x1E'))
        {
            if (string.IsNullOrEmpty(rec)) continue;
            string[] parts = rec.Split('\x1F');
            if (parts.Length < 2) continue;
            string speaker = Unescape(parts[0]);
            entries.Add(new Entry
            {
                speaker = speaker,
                text = Unescape(parts[1]),
                color = SpeakerPortrait.GetSpeakerColor(speaker)
            });
        }
        touchedKeys.Add(loadedKey);
    }

    void SaveFor(string dialogueKey)
    {
        if (string.IsNullOrEmpty(dialogueKey)) return;

        int start = Mathf.Max(0, entries.Count - SaveEntries);
        var sb = new System.Text.StringBuilder();
        for (int i = start; i < entries.Count; i++)
        {
            if (sb.Length > 0) sb.Append('\x1E');
            sb.Append(EscapeRaw(entries[i].speaker)).Append('\x1F').Append(EscapeRaw(entries[i].text));
        }
        PlayerPrefs.SetString(Prefix + dialogueKey, sb.ToString());
        touchedKeys.Add(dialogueKey);
        PlayerPrefs.Save();
    }

    /// <summary>Стереть историю всех диалогов (для новой игры).</summary>
    public void ResetAll()
    {
        foreach (string k in touchedKeys)
            PlayerPrefs.DeleteKey(Prefix + k);
        touchedKeys.Clear();
        entries.Clear();
        loadedKey = "";
        PlayerPrefs.Save();
    }

    static string EscapeRaw(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\x1E", "\\e").Replace("\x1F", "\\u");

    static string Unescape(string s) =>
        (s ?? "").Replace("\\u", "\x1F").Replace("\\e", "\x1E").Replace("\\n", "\n").Replace("\\\\", "\\");

    // =====================================================================
    // Панель: затемнение + окно с прокруткой, строится кодом
    // =====================================================================
    void BuildUI()
    {
        canvas = new GameObject("DialogueHistoryCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 96; // над диалогами (90), под инвентарём (100)
        // Без DontDestroyOnLoad: канвас живёт в сцене как остальной UI диалогов,
        // записи подтягиваются из PlayerPrefs при следующей реплике

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        panelRoot = new GameObject("HistoryPanel", typeof(RectTransform));
        panelRoot.transform.SetParent(canvas.transform, false);
        Stretch((RectTransform)panelRoot.transform);

        Image dim = panelRoot.AddComponent<Image>();
        dim.sprite = UIShapes.Solid();
        dim.color = new Color(0f, 0f, 0f, 0.6f);

        GameObject window = new GameObject("Window", typeof(RectTransform));
        window.transform.SetParent(panelRoot.transform, false);
        RectTransform wr = (RectTransform)window.transform;
        wr.anchorMin = new Vector2(0.5f, 0.5f);
        wr.anchorMax = new Vector2(0.5f, 0.5f);
        wr.pivot = new Vector2(0.5f, 0.5f);
        wr.sizeDelta = new Vector2(1100f, 760f);
        wr.anchoredPosition = Vector2.zero;

        Image bg = window.AddComponent<Image>();
        bg.sprite = UIShapes.RoundedRect(64, 16);
        bg.type = Image.Type.Sliced;
        bg.color = panelColor;

        GameObject title = new GameObject("Title", typeof(RectTransform));
        title.transform.SetParent(window.transform, false);
        RectTransform tr = (RectTransform)title.transform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.offsetMin = new Vector2(30f, -64f);
        tr.offsetMax = new Vector2(-30f, -16f);
        TextMeshProUGUI titleLabel = title.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) titleLabel.font = fontAsset;
        titleLabel.text = "История разговора  <size=60%>(H — закрыть)</size>";
        titleLabel.fontSize = 26f;
        titleLabel.fontStyle = FontStyles.Bold;
        titleLabel.color = textColor;
        titleLabel.alignment = TextAlignmentOptions.Left;
        titleLabel.raycastTarget = false;

        GameObject scrollObj = new GameObject("Scroll", typeof(RectTransform));
        scrollObj.transform.SetParent(window.transform, false);
        RectTransform sr = (RectTransform)scrollObj.transform;
        sr.anchorMin = new Vector2(0f, 0f);
        sr.anchorMax = new Vector2(1f, 1f);
        sr.offsetMin = new Vector2(30f, 24f);
        sr.offsetMax = new Vector2(-30f, -78f);

        scroll = scrollObj.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        Mask mask = scrollObj.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        Image view = scrollObj.AddComponent<Image>();
        view.color = new Color(1f, 1f, 1f, 0.03f);

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(scrollObj.transform, false);
        RectTransform cr = (RectTransform)content.transform;
        cr.anchorMin = new Vector2(0f, 1f);
        cr.anchorMax = new Vector2(1f, 1f);
        cr.pivot = new Vector2(0.5f, 1f);
        cr.offsetMin = new Vector2(0f, 0f);
        cr.offsetMax = new Vector2(0f, 0f);

        VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject logObj = new GameObject("Log", typeof(RectTransform));
        logObj.transform.SetParent(content.transform, false);

        logText = logObj.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) logText.font = fontAsset;
        logText.fontSize = 22f;
        logText.lineSpacing = 6f;
        logText.color = textColor;
        logText.alignment = TextAlignmentOptions.TopLeft;
        logText.enableWordWrapping = true;
        logText.richText = true;
        logText.raycastTarget = false;

        scroll.content = (RectTransform)content.transform;
        scroll.viewport = sr;

        // Кнопка закрытия для мыши
        GameObject closeObj = new GameObject("Close", typeof(RectTransform));
        closeObj.transform.SetParent(window.transform, false);
        RectTransform cor = (RectTransform)closeObj.transform;
        cor.anchorMin = new Vector2(1f, 1f);
        cor.anchorMax = new Vector2(1f, 1f);
        cor.pivot = new Vector2(1f, 1f);
        cor.anchoredPosition = new Vector2(-20f, -14f);
        cor.sizeDelta = new Vector2(200f, 48f);

        Image closeBg = closeObj.AddComponent<Image>();
        closeBg.sprite = UIShapes.RoundedRect(48, 10);
        closeBg.type = Image.Type.Sliced;
        closeBg.color = new Color(0.25f, 0.12f, 0.12f, 1f);

        Button closeBtn = closeObj.AddComponent<Button>();
        closeBtn.targetGraphic = closeBg;
        closeBtn.onClick.AddListener(Hide);

        GameObject closeLabel = new GameObject("Label", typeof(RectTransform));
        closeLabel.transform.SetParent(closeObj.transform, false);
        Stretch((RectTransform)closeLabel.transform);
        TextMeshProUGUI closeText = closeLabel.AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) closeText.font = fontAsset;
        closeText.text = "Закрыть";
        closeText.fontSize = 20f;
        closeText.color = Color.white;
        closeText.alignment = TextAlignmentOptions.Center;
        closeText.raycastTarget = false;

        panelRoot.SetActive(false);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
