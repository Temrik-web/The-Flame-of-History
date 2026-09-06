using UnityEngine;

/// <summary>
/// Генератор простых UI-спрайтов кодом: скруглённые прямоугольники, рамки,
/// вертикальные градиенты, виньетка. Нужен, чтобы интерфейс выглядел опрятно
/// без единого импортированного изображения.
///
/// Спрайты кэшируются: повторные запросы с теми же параметрами не создают
/// новых текстур.
/// </summary>
public static class UIShapes
{
    private static readonly System.Collections.Generic.Dictionary<string, Sprite> cache
        = new System.Collections.Generic.Dictionary<string, Sprite>();

    /// <summary>
    /// Скруглённый прямоугольник. border = 0 — залитый, больше 0 — рамка
    /// указанной толщины в пикселях.
    /// </summary>
    public static Sprite RoundedRect(int size = 64, int radius = 12, int border = 0)
    {
        size = Mathf.Max(8, size);
        radius = Mathf.Clamp(radius, 0, size / 2);
        border = Mathf.Max(0, border);

        string key = $"round_{size}_{radius}_{border}";
        if (cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float outer = RoundedRectCoverage(x, y, size, radius);
                float alpha = outer;

                if (border > 0)
                {
                    // Внутренний контур: вычитаем уменьшенный прямоугольник
                    float innerCoverage = RoundedRectCoverage(
                        x - border, y - border,
                        size - border * 2,
                        Mathf.Max(0, radius - border));
                    alpha = Mathf.Clamp01(outer - innerCoverage);
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        // Border у спрайта включает 9-slice: рамка не растягивается при ресайзе
        int slice = radius > 0 ? radius : Mathf.Max(1, size / 4);
        var sprite = Sprite.Create(
            tex,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(slice, slice, slice, slice));

        sprite.name = key;
        cache[key] = sprite;
        return sprite;
    }

    /// <summary>Мягкое покрытие пикселя скруглённым прямоугольником (антиалиасинг).</summary>
    private static float RoundedRectCoverage(float x, float y, float size, float radius)
    {
        if (size <= 0f) return 0f;
        if (x < -1f || y < -1f || x > size || y > size) return 0f;

        float px = x + 0.5f;
        float py = y + 0.5f;

        // Расстояние до ближайшего центра скругления
        float cx = Mathf.Clamp(px, radius, size - radius);
        float cy = Mathf.Clamp(py, radius, size - radius);
        float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));

        if (radius <= 0f)
            return (px >= 0f && px <= size && py >= 0f && py <= size) ? 1f : 0f;

        // 1 пиксель мягкого края
        return Mathf.Clamp01(radius - dist + 0.5f);
    }

    /// <summary>Круг. border = 0 — залитый диск, больше 0 — кольцо.</summary>
    public static Sprite Circle(int size = 64, int border = 0)
    {
        size = Mathf.Max(8, size);
        string key = $"circle_{size}_{border}";
        if (cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];
        float r = size * 0.5f;
        Vector2 center = new Vector2(r, r);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(r - d);
                if (border > 0)
                    alpha = Mathf.Min(alpha, Mathf.Clamp01(d - (r - border) + 1f));

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sprite.name = key;
        cache[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// Вертикальный градиент из белого (сверху) в прозрачный (снизу).
    /// Растягивается по ширине, поэтому текстура 1 пиксель шириной.
    /// </summary>
    public static Sprite VerticalGradient(int height = 128, float topAlpha = 1f, float bottomAlpha = 0f)
    {
        height = Mathf.Max(2, height);
        string key = $"vgrad_{height}_{topAlpha:F2}_{bottomAlpha:F2}";
        if (cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(1, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < height; y++)
        {
            float t = y / (float)(height - 1);
            // Плавное сглаживание вместо линейного — переход выглядит естественнее
            float a = Mathf.Lerp(bottomAlpha, topAlpha, Mathf.SmoothStep(0f, 1f, t));
            tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
        }

        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f));
        sprite.name = key;
        cache[key] = sprite;
        return sprite;
    }

    /// <summary>
    /// Радиальная виньетка: прозрачная в центре, плотная по краям.
    /// Используется как затемнение вокруг диалоговой панели.
    /// </summary>
    public static Sprite Vignette(int size = 256, float innerRadius = 0.25f, float power = 1.6f)
    {
        size = Mathf.Max(16, size);
        string key = $"vign_{size}_{innerRadius:F2}_{power:F2}";
        if (cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / maxDist;
                float t = Mathf.InverseLerp(innerRadius, 1f, Mathf.Clamp01(d));
                float a = Mathf.Pow(Mathf.Clamp01(t), power);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sprite.name = key;
        cache[key] = sprite;
        return sprite;
    }

    /// <summary>Плоский белый спрайт 4x4 — база для однотонных заливок.</summary>
    public static Sprite Solid()
    {
        const string key = "solid";
        if (cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply();

        var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        sprite.name = key;
        cache[key] = sprite;
        return sprite;
    }
}
