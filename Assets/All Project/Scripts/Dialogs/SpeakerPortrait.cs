using System.Collections.Generic;
using UnityEngine;

/// <summary>Заглушка портрета: цветной круг. Есть спрайт в узле — заглушка не используется.</summary>
public static class SpeakerPortrait
{
    private static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    public static Color GetSpeakerColor(string speakerName)
    {
        if (string.IsNullOrEmpty(speakerName)) return Color.white;
        string n = speakerName.Trim().ToLowerInvariant();
        if (n.Contains("тепан")) return new Color(1f, 0.78f, 0.42f);      // янтарь
        if (n.Contains("алесь")) return new Color(0.55f, 0.8f, 1f);        // голубой
        if (n.Contains("асили")) return new Color(0.6f, 0.9f, 0.55f);      // зелёный
        if (n.Contains("ихал")) return new Color(0.85f, 0.75f, 0.6f);      // тёплый серый (дед)
        if (n.Contains("арья")) return new Color(0.95f, 0.6f, 0.65f);      // розовый (Марья)
        // Остальные: стабильный оттенок по хешу имени
        int h = Mathf.Abs(speakerName.GetHashCode()) % 360;
        return Color.HSVToRGB(h / 360f, 0.45f, 0.95f);
    }

    /// <summary>Заглушка 96x96: тёмный круг в цвете персонажа + светлый ободок.</summary>
    public static Sprite GetPlaceholder(string speakerName)
    {
        string key = string.IsNullOrEmpty(speakerName) ? "?" : speakerName.Trim().ToLowerInvariant();
        Sprite s;
        if (cache.TryGetValue(key, out s) && s != null) return s;

        Color base_ = GetSpeakerColor(speakerName);
        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        float c = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c)) / c;
                Color px;
                if (d > 1f) px = new Color(0f, 0f, 0f, 0f);
                else if (d > 0.86f) px = new Color(base_.r, base_.g, base_.b, 1f); // ободок
                else px = new Color(base_.r * 0.28f, base_.g * 0.28f, base_.b * 0.32f, 1f); // фон
                tex.SetPixel(x, y, px);
            }
        }
        tex.Apply();
        s = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 96f);
        s.name = "PortraitPlaceholder_" + key;
        cache[key] = s;
        return s;
    }
}
