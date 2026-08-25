using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Визуальное поведение одной кнопки ответа в диалоге: подсветка при наведении,
/// выезжающий акцентный маркер, сдвиг текста, номер для выбора с клавиатуры.
///
/// Добавляется автоматически из DialogueUI — вручную вешать не нужно.
///
/// Позицию самой кнопки не трогаем: её задаёт VerticalLayoutGroup, и любые
/// правки anchoredPosition были бы перетёрты при следующем пересчёте раскладки.
/// Двигаем внутренние элементы и localScale, которые раскладка не контролирует.
/// </summary>
public class DialogueChoiceButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Ссылки (заполняет DialogueUI)")]
    public Image background;
    public Image edge;
    public Image marker;
    public TextMeshProUGUI numberLabel;
    public TextMeshProUGUI textLabel;

    [Header("Анимация")]
    public float animationSpeed = 14f;
    [Tooltip("На сколько пикселей текст сдвигается вправо при наведении.")]
    public float textShift = 10f;
    [Tooltip("Толщина акцентного маркера слева в наведённом состоянии.")]
    public float markerWidth = 4f;
    [Tooltip("Легкое увеличение кнопки при наведении.")]
    public float hoverScale = 1.012f;

    private Button button;
    private Color accent = new Color(1f, 0.66f, 0.28f);
    private Color baseTextColor = Color.white;

    private RectTransform rect;
    private RectTransform textRect;
    private RectTransform markerRect;
    private RectTransform numberRect;

    private float textBaseLeft;
    private float numberBaseLeft;
    private bool basesCaptured;

    private float blend;        // 0 — обычное состояние, 1 — наведение
    private float targetBlend;

    private static readonly Color IdleBg = new Color(0.09f, 0.095f, 0.12f, 0.95f);

    void Awake()
    {
        rect = (RectTransform)transform;
        if (button == null) button = GetComponent<Button>();

        if (textLabel != null) textRect = (RectTransform)textLabel.transform;
        if (numberLabel != null) numberRect = (RectTransform)numberLabel.transform;
        if (marker != null) markerRect = (RectTransform)marker.transform;
    }

    void OnEnable()
    {
        blend = targetBlend = 0f;
        if (rect != null) rect.localScale = Vector3.one;
    }

    /// <summary>Настроить номер и цвета. Вызывается из DialogueUI.</summary>
    public void Setup(Button button, int number, Color accent, Color textColor)
    {
        this.button = button;
        this.accent = accent;
        baseTextColor = textColor;

        if (numberLabel != null)
            numberLabel.text = number.ToString();

        if (textLabel != null)
            textLabel.color = textColor;
    }

    /// <summary>Программно нажать кнопку (используется для клавиш 1..9).</summary>
    public void Invoke()
    {
        if (button != null && button.interactable)
            button.onClick.Invoke();
    }

    void Update()
    {
        if (!basesCaptured)
        {
            if (textRect != null) textBaseLeft = textRect.offsetMin.x;
            if (numberRect != null) numberBaseLeft = numberRect.offsetMin.x;
            basesCaptured = true;
        }

        float k = 1f - Mathf.Exp(-animationSpeed * Time.unscaledDeltaTime);
        blend = Mathf.Lerp(blend, targetBlend, k);

        // Текст и номер уезжают вправо — глаз сразу видит выбранный вариант
        if (textRect != null)
        {
            Vector2 min = textRect.offsetMin;
            min.x = textBaseLeft + textShift * blend;
            textRect.offsetMin = min;
        }

        if (numberRect != null)
        {
            Vector2 min = numberRect.offsetMin;
            min.x = numberBaseLeft + textShift * 0.5f * blend;
            numberRect.offsetMin = min;
        }

        // Маркер слева «наливается» акцентом и растёт в толщину
        if (marker != null)
        {
            marker.color = new Color(accent.r, accent.g, accent.b, blend);
            if (markerRect != null)
            {
                Vector2 max = markerRect.offsetMax;
                max.x = Mathf.Lerp(1.5f, markerWidth, blend);
                markerRect.offsetMax = max;
            }
        }

        if (background != null)
            background.color = Color.Lerp(
                IdleBg,
                new Color(accent.r * 0.22f, accent.g * 0.18f, accent.b * 0.12f, 0.98f),
                blend);

        if (edge != null)
            edge.color = Color.Lerp(
                new Color(1f, 1f, 1f, 0.08f),
                new Color(accent.r, accent.g, accent.b, 0.75f),
                blend);

        if (numberLabel != null)
            numberLabel.color = Color.Lerp(
                new Color(baseTextColor.r, baseTextColor.g, baseTextColor.b, 0.35f),
                accent,
                blend);

        if (textLabel != null)
            textLabel.color = Color.Lerp(baseTextColor, Color.white, blend * 0.6f);

        // localScale раскладка не трогает, поэтому его анимировать безопасно
        if (rect != null)
        {
            float s = Mathf.Lerp(1f, hoverScale, blend);
            rect.localScale = new Vector3(s, s, 1f);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        targetBlend = 1f;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        targetBlend = 0f;
    }
}
