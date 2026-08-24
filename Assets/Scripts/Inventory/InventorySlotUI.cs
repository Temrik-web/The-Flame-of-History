using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Одна ячейка инвентаря в UI. Вешается на префаб ячейки (Image + вложенные Image/Text).
/// Заполняется из InventoryUI, сама ничего не ищет.
/// </summary>
public class InventorySlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("Ссылки (перетащить дочерние объекты)")]
    public Image iconImage;
    public TextMeshProUGUI amountText;
    public Image background;
    public GameObject selectionFrame;

    [Header("Цвета фона")]
    public Color normalColor = new Color(1f, 1f, 1f, 0.10f);
    public Color hoverColor = new Color(1f, 1f, 1f, 0.25f);
    public Color emptyColor = new Color(1f, 1f, 1f, 0.05f);

    private InventoryUI owner;
    private int index = -1;
    private bool isEmpty = true;

    public int Index => index;

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
        isEmpty = slot == null || slot.IsEmpty;

        if (iconImage != null)
        {
            iconImage.enabled = !isEmpty && slot.item.icon != null;
            if (!isEmpty) iconImage.sprite = slot.item.icon;
        }

        if (amountText != null)
        {
            bool showAmount = !isEmpty && slot.amount > 1;
            amountText.gameObject.SetActive(showAmount);
            if (showAmount) amountText.text = slot.amount.ToString();
        }

        if (background != null)
            background.color = isEmpty ? emptyColor : normalColor;
    }

    public void SetSelected(bool selected)
    {
        if (selectionFrame != null) selectionFrame.SetActive(selected);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (background != null && !isEmpty) background.color = hoverColor;
        if (owner != null) owner.OnSlotHover(index, true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (background != null) background.color = isEmpty ? emptyColor : normalColor;
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
