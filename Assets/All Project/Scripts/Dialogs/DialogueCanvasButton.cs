using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Визуальное поведение кнопки диалогового канваса. При наведении меняет
/// свою ФОНОВУЮ картинку (спрайт), при нажатии — на свою. Текст внутри
/// кнопки при этом не трогается.
///
/// Три состояния задаются слотaми спрайтов (normal/hover/pressed) прямо в
/// инспекторе на компоненте. Если спрайты не назначены — кнопка работает
/// только цветом + лёгким масштабированием, как раньше.
///
/// Вешается мастером «Tools -> Диалоги -> Создать канвас» на кнопки-варианты,
/// кнопку отмены и кнопку-продолжение. Работает вместе с Button: у самой
/// Button переход ставится в None, чтобы состояния не конфликтовали.
/// </summary>
[DisallowMultipleComponent]
public class DialogueCanvasButton : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [Header("Что подсвечивать")]
    [Tooltip("Image/Graphic, чью картинку и цвет меняем. Если пусто — берётся Graphic на этом же объекте.")]
    public Graphic target;

    [Header("Спрайты состояний (меняется фон, текст внутри кнопки не трогаем)")]
    [Tooltip("Обычное состояние. Спрайт задаётся в инспекторе; для частей игры просто цвет.")]
    public Sprite normalSprite;
    [Tooltip("Наведение: фоновая картинка меняется на эту. Можно не задавать.")]
    public Sprite hoverSprite;
    [Tooltip("Нажатие: фоновая картинка меняется на эту. Можно не задавать.")]
    public Sprite pressedSprite;
    [Tooltip("Плавное проявление нового спрайта, сек. 0 = мгновенная смена.")]
    [Range(0f, 0.5f)]
    public float spriteCrossfade = 0.05f;

    [Header("Цвета состояний")]
    public Color normalColor = new Color(0.09f, 0.10f, 0.13f, 0.96f);
    public Color hoverColor = new Color(0.21f, 0.23f, 0.29f, 1f);
    public Color pressedColor = new Color(0.32f, 0.24f, 0.13f, 1f);

    [Header("Масштаб")]
    [Range(1f, 1.3f)]
    public float hoverScale = 1.02f;
    [Range(0.6f, 1f)]
    public float pressedScale = 0.97f;

    [Header("Скорость перехода")]
    public float animationSpeed = 14f;

    private RectTransform rect;
    private Image image;
    private float blend;        // 0 — обычное, 1 — наведение, 2 — нажатие
    private float targetBlend;
    private int state = -1;     // применённый спрайт-стате (обновляется только при смене)

    void Awake()
    {
        rect = (RectTransform)transform;
        if (target == null) target = GetComponent<Graphic>();
        if (target != null) image = target as Image;
    }

    void OnEnable()
    {
        blend = targetBlend = 0f;
        state = -1;
        if (rect != null)
        {
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }
        if (target != null)
        {
            target.color = normalColor;
            target.CrossFadeAlpha(1f, 0f, true);
        }
    }

    void Update()
    {
        // Плавное «дыхание» между состояниями (игнорирует паузу)
        float k = 1f - Mathf.Exp(-animationSpeed * Time.unscaledDeltaTime);
        blend = Mathf.Lerp(blend, targetBlend, k);

        // Спрайт переключается, когда бленда перевалила середину состояния
        int newState = state;
        if (blend >= 1.5f) newState = 2;
        else if (blend >= 0.5f) newState = 1;
        else newState = 0;

        if (newState != state)
        {
            state = newState;
            ApplyStateSprite();
        }

        if (target != null)
            target.color = BlendColor();

        if (rect == null) return;

        float scale;
        if (blend >= 2f) scale = pressedScale;
        else if (blend >= 1f) scale = Mathf.Lerp(hoverScale, pressedScale, blend - 1f);
        else scale = Mathf.Lerp(1f, hoverScale, blend);

        rect.localScale = new Vector3(scale, scale, scale);
    }

    /// <summary>Выбрать и применить спрайт текущего состояния (с мягким проявлением).</summary>
    void ApplyStateSprite()
    {
        if (image == null) return;

        Sprite s = state == 2 ? pressedSprite : (state == 1 ? hoverSprite : normalSprite);
        // Если для состояния нет своего спрайта — берём ближайшее слева
        if (s == null)
        {
            if (state == 2) s = hoverSprite != null ? hoverSprite : normalSprite;
            else if (state == 1) s = normalSprite;
        }

        if (image.sprite == s) return;

        image.sprite = s;
        if (spriteCrossfade > 0f && state != 0)
        {
            image.CrossFadeAlpha(0.001f, 0f, true);
            image.CrossFadeAlpha(1f, spriteCrossfade, true);
        }
        else
        {
            image.CrossFadeAlpha(1f, 0f, true);
        }
    }

    Color BlendColor()
    {
        if (blend >= 2f) return pressedColor;
        if (blend >= 1f) return Color.Lerp(hoverColor, pressedColor, blend - 1f);
        return Color.Lerp(normalColor, hoverColor, blend);
    }

    public void OnPointerEnter(PointerEventData eventData) => targetBlend = 1f;
    public void OnPointerExit(PointerEventData eventData) => targetBlend = 0f;
    public void OnPointerDown(PointerEventData eventData) => targetBlend = 2f;
    public void OnPointerUp(PointerEventData eventData) => targetBlend = 1f;
}