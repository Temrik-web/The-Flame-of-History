using UnityEngine;

/// <summary>
/// Кривые для анимации взятия/убирания предмета в руки.
/// Отдельный статический класс, чтобы Wep и HeldItem использовали
/// одинаковую «сочность» без дублирования кода.
/// </summary>
public static class WeaponEquipEases
{
    /// <summary>
    /// Подъём с лёгким перелётом в конце (предмет «выстреливает» в руки).
    /// s — сила отскока: 0 = гладко, ~1.4 = классический сочный overshoot.
    /// </summary>
    public static float EaseOutBack(float t, float s = 1.4f)
    {
        t = Mathf.Clamp01(t);
        float u = t - 1f;
        return 1f + (s + 1f) * u * u * u + s * u * u;
    }

    /// <summary>Быстрое убирание с ускорением (предмет «ныряет» вниз).</summary>
    public static float EaseInCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * t;
    }

    /// <summary>Убирание с мягким ускорением: старт без мёртвой паузы, в отличие от кубики.</summary>
    public static float EaseInQuad(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t;
    }

    /// <summary>Мягкое завершение без перелёта.</summary>
    public static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }
}
