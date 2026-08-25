using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Одна ячейка инвентаря в UI. Вешается на префаб ячейки.
/// Заполняется из InventoryUI, сама ничего не ищет.
///
/// Визуал: рамка цвета редкости, подсветка и подъём при наведении,
/// «пружинка» при появлении предмета, полоска заполнения стака.
/// </summary>
public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Ссылки (перетащить дочерние объекты)")]
    public Image iconImage;
    public TextMeshProUGUI amountText;
    public Image background;
    public Image rarityFrame;
    public Image stackBar;
    public GameObject selectionFrame;

    [Header("Цвета фона")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.06f);
    public Color hoverColor = new Color(1f, 1f, 1f, 0.18f);
    public Color emptyColor = new Color(1f, 1f, 1f, 0.03f);

    [Header("Анимация")]
    public float hoverScale = 1.08f;
    public float hoverLift = 6f;
    public float animationSpeed = 12f;
    [Tooltip("Насколько ячейка «выпрыгивает» при появлении нового предмета.")]
    public float popScale = 1.35f;

    private InventoryUI owner;
    private int index = -1;
    private bool isEmpty = true;
    private bool isHovered;

    private RectTransform rect;
    private Vector2 basePosition;
    private bool baseCaptured;
    private UnityEngine.UI.LayoutGroup parentLayout;

    // Текущее и целевое состояние анимации
    private float scale = 1f;
    private float targetScale = 1f;
    private float lift;
    private float targetLift;
    private float popTimer;

    private ItemData lastItem;
    private int lastAmount;

    public int Index => index;
    public bool IsEmpty => isEmpty;

    void Awake()
    {
        rect = (RectTransform)transform;
    }

    void OnEnable()
    {
        ResetAnimation();
    }

    void ResetAnimation()
    {
        baseCaptured = false;
        parentLayout = null;
        scale = targetScale = 1f;
        lift = targetLift = 0f;
        if (rect != null) rect.localScale = Vector3.one;
    }

    /// <summary>Привязать ячейку к владельцу и её позиции в сетке.</summary>
    public void Init(InventoryUI owner, int index)
    {
        this.owner = owner;
        this.index = index;
        if (selectionFrame != null) selectionFrame.SetActive(false);
    }

    /// <summary>Отрисовать содержимое слота. null — пустая ячейка.</summary>
    public void SetSlot(InventorySystem.Slot slot)
    {
        bool wasEmpty = isEmpty;
        ItemData previousItem = lastItem;
        int previousAmount = lastAmount;

        isEmpty = slot == null || slot.IsEmpty;
        lastItem = isEmpty ? null : slot.item;
        lastAmount = isEmpty ? 0 : slot.amount;

        if (iconImage != null)
        {
            bool hasIcon = !isEmpty && slot.item.icon != null;
            iconImage.enabled = hasIcon;
            if (hasIcon) iconImage.sprite = slot.item.icon;

            // Без иконки показываем цветной силуэт редкости, чтобы ячейка не выглядела пустой
            if (!isEmpty && !hasIcon)
            {
                iconImage.enabled = true;
                iconImage.sprite = null;
                iconImage.color = new Color(slot.item.RarityColor.r,
                                            slot.item.RarityColor.g,
                                            slot.item.RarityColor.b, 0.30f);
            }
            else if (hasIcon)
            {
                iconImage.color = Color.white;
            }
        }

        if (amountText != null)
        {
            bool showAmount = !isEmpty && slot.amount > 1;
            amountText.gameObject.SetActive(showAmount);
            if (showAmount) amountText.text = slot.amount.ToString();
        }

        if (rarityFrame != null)
        {
            rarityFrame.enabled = !isEmpty;
            if (!isEmpty)
            {
                Color c = slot.item.RarityColor;
                // Обычные предметы почти без рамки, редкие — заметно
                float a = slot.item.rarity == ItemRarity.Common ? 0.25f : 0.85f;
                rarityFrame.color = new Color(c.r, c.g, c.b, a);
            }
        }

        if (stackBar != null)
        {
            bool showBar = !isEmpty && slot.item.stackable && slot.item.maxStack > 1;
            stackBar.gameObject.SetActive(showBar);
            if (showBar)
            {
                stackBar.fillAmount = Mathf.Clamp01(slot.amount / (float)slot.item.maxStack);
                Color c = slot.item.RarityColor;
                stackBar.color = new Color(c.r, c.g, c.b, 0.7f);
            }
        }

        ApplyBackgroundColor();

        // Пружинка, если предмет появился или количество выросло
        bool appeared = !isEmpty && (wasEmpty || previousItem != lastItem);
        bool grew = !isEmpty && previousItem == lastItem && lastAmount > previousAmount;
        if (appeared || grew) Pop();
    }

    /// <summary>Запустить анимацию «выпрыгивания».</summary>
    public void Pop()
    {
        popTimer = 1f;
    }

    public void SetSelected(bool selected)
    {
        if (selectionFrame != null) selectionFrame.SetActive(selected);
    }

    void Update()
    {
        if (rect == null) return;

        // Позицию задаёт GridLayoutGroup: базу берём после того, как раскладка
        // отработала и отключилась (GridLayoutFreezer), иначе запомним промежуточное
        // значение и ячейки «уползут».
        if (!baseCaptured)
        {
            if (parentLayout == null && transform.parent != null)
                parentLayout = transform.parent.GetComponent<UnityEngine.UI.LayoutGroup>();

            if (parentLayout != null && parentLayout.enabled) return;

            basePosition = rect.anchoredPosition;
            baseCaptured = true;
        }

        // Пружинка затухает по синусоиде — читается как упругий отскок
        float popExtra = 0f;
        if (popTimer > 0f)
        {
            popTimer -= Time.unscaledDeltaTime * 3.5f;
            float t = Mathf.Clamp01(popTimer);
            popExtra = Mathf.Sin(t * Mathf.PI) * (popScale - 1f);
            if (popTimer <= 0f) popTimer = 0f;
        }

        float k = 1f - Mathf.Exp(-animationSpeed * Time.unscaledDeltaTime);
        scale = Mathf.Lerp(scale, targetScale + popExtra, k);
        lift = Mathf.Lerp(lift, targetLift, k);

        rect.localScale = new Vector3(scale, scale, 1f);
        rect.anchoredPosition = basePosition + new Vector2(0f, lift);
    }

    void ApplyBackgroundColor()
    {
        if (background == null) return;
        if (isEmpty) { background.color = emptyColor; return; }
        background.color = isHovered ? hoverColor : normalColor;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovered = true;
        ApplyBackgroundColor();

        if (!isEmpty)
        {
            targetScale = hoverScale;
            targetLift = hoverLift;
        }

        if (owner != null) owner.OnSlotHover(index, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovered = false;
        ApplyBackgroundColor();
        targetScale = 1f;
        targetLift = 0f;

        if (owner != null) owner.OnSlotHover(index, false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (owner == null || isEmpty) return;

        if (eventData.button == PointerEventData.InputButton.Left)
            owner.OnSlotLeftClick(index);
        else if (eventData.button == PointerEventData.InputButton.Right)
            owner.OnSlotRightClick(index);
    }
}
