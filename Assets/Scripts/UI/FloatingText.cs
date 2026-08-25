using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Всплывающие подписи в мире: при подборе предмета над точкой подбора
/// поднимается и растворяется надпись «+2 Аптечка» цвета редкости.
///
/// Вешается на любой объект в сцене (или создаётся автоматически, если
/// вызвать FloatingText.Show — компонент появится сам).
///
/// Метки хранятся в пуле, поэтому подбор не создаёт мусора для GC.
/// </summary>
[DisallowMultipleComponent]
public class FloatingText : MonoBehaviour
{
    [Header("Поведение")]
    [Tooltip("Сколько секунд живёт одна надпись.")]
    public float lifetime = 1.4f;
    [Tooltip("На сколько метров надпись поднимается за время жизни.")]
    public float riseDistance = 0.9f;
    [Tooltip("Размер шрифта в мировых единицах.")]
    public float fontSize = 3.2f;
    [Tooltip("Максимум одновременных надписей.")]
    public int poolSize = 16;
    [Tooltip("Небольшой случайный сдвиг, чтобы надписи не накладывались.")]
    public float scatter = 0.18f;

    [Header("Шрифт")]
    public TMP_FontAsset fontAsset;

    private static FloatingText instance;

    private readonly List<Entry> pool = new List<Entry>();
    private Camera targetCamera;

    private class Entry
    {
        public TextMeshPro text;
        public Transform tr;
        public float elapsed;
        public bool active;
        public Vector3 origin;
        public Vector3 drift;
    }

    /// <summary>
    /// Показать надпись в мировой точке. Если менеджера в сцене нет — создаётся сам.
    /// </summary>
    public static void Show(string message, Vector3 worldPosition, Color color)
    {
        if (instance == null)
        {
            instance = FindObjectOfType<FloatingText>();
            if (instance == null)
            {
                var go = new GameObject("FloatingTextManager");
                instance = go.AddComponent<FloatingText>();
            }
        }

        instance.Spawn(message, worldPosition, color);
    }

    void Awake()
    {
        if (instance == null) instance = this;
        if (fontAsset == null) fontAsset = Resources.Load<TMP_FontAsset>("InventoryFont SDF");
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Spawn(string message, Vector3 worldPosition, Color color)
    {
        Entry entry = GetFree();
        if (entry == null) return;

        entry.text.text = message;
        entry.text.color = color;
        entry.text.alpha = 1f;

        entry.origin = worldPosition;
        entry.drift = new Vector3(
            Random.Range(-scatter, scatter),
            0f,
            Random.Range(-scatter, scatter));

        entry.tr.position = worldPosition + entry.drift;
        entry.elapsed = 0f;
        entry.active = true;
        entry.text.gameObject.SetActive(true);
    }

    Entry GetFree()
    {
        foreach (Entry e in pool)
            if (!e.active) return e;

        if (pool.Count >= poolSize)
        {
            // Пул исчерпан — переиспользуем самую старую надпись
            Entry oldest = null;
            foreach (Entry e in pool)
                if (oldest == null || e.elapsed > oldest.elapsed) oldest = e;
            return oldest;
        }

        return CreateEntry();
    }

    Entry CreateEntry()
    {
        var go = new GameObject($"FloatingLabel_{pool.Count}");
        go.transform.SetParent(transform, false);

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (fontAsset != null) tmp.font = fontAsset;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontStyle = FontStyles.Bold;
        tmp.richText = true;

        // Мировой размер задаётся RectTransform, а не масштабом объекта
        RectTransform rect = tmp.rectTransform;
        rect.sizeDelta = new Vector2(6f, 1.4f);

        go.SetActive(false);

        var entry = new Entry { text = tmp, tr = go.transform, active = false };
        pool.Add(entry);
        return entry;
    }

    void LateUpdate()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        for (int i = 0; i < pool.Count; i++)
        {
            Entry e = pool[i];
            if (!e.active) continue;

            e.elapsed += Time.unscaledDeltaTime;
            float t = e.elapsed / lifetime;

            if (t >= 1f)
            {
                e.active = false;
                e.text.gameObject.SetActive(false);
                continue;
            }

            // Подъём замедляется к концу — движение выглядит инерционным
            float rise = riseDistance * Mathf.Sin(t * Mathf.PI * 0.5f);
            e.tr.position = e.origin + e.drift + Vector3.up * rise;

            // Растворяется во второй половине жизни
            e.text.alpha = t < 0.5f ? 1f : Mathf.SmoothStep(1f, 0f, (t - 0.5f) / 0.5f);

            // Небольшой «наплыв» в начале
            float scale = t < 0.15f ? Mathf.Lerp(0.6f, 1f, t / 0.15f) : 1f;
            e.tr.localScale = new Vector3(scale, scale, scale);

            // Всегда лицом к камере
            if (targetCamera != null)
                e.tr.rotation = targetCamera.transform.rotation;
        }
    }
}
