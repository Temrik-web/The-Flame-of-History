using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Отключает LayoutGroup после того, как раскладка отработала один раз.
///
/// Зачем: ячейки инвентаря анимируют свой anchoredPosition (подъём при наведении,
/// «пружинка» при подборе). Активная GridLayoutGroup перетирает эти значения при
/// каждом пересчёте раскладки, и анимация не видна. Заморозка оставляет
/// расставленные позиции и передаёт управление ячейкам.
///
/// Если состав детей меняется, вызови Rebuild().
/// </summary>
[RequireComponent(typeof(LayoutGroup))]
public class GridLayoutFreezer : MonoBehaviour
{
    private LayoutGroup layoutGroup;
    private int frames;
    private int knownChildCount = -1;

    void Awake()
    {
        layoutGroup = GetComponent<LayoutGroup>();
    }

    void OnEnable()
    {
        Rebuild();
    }

    /// <summary>Включить раскладку заново — например, после добавления ячеек.</summary>
    public void Rebuild()
    {
        frames = 0;
        if (layoutGroup != null) layoutGroup.enabled = true;
    }

    void LateUpdate()
    {
        if (layoutGroup == null) return;

        // Состав детей изменился — пересчитываем раскладку
        if (transform.childCount != knownChildCount)
        {
            knownChildCount = transform.childCount;
            Rebuild();
        }

        if (!layoutGroup.enabled) return;

        // Двух кадров достаточно: первый создаёт элементы, второй расставляет их
        if (++frames >= 2)
            layoutGroup.enabled = false;
    }
}
