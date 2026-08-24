using UnityEngine;

/// <summary>
/// Предмет, лежащий в мире и доступный для подбора.
/// Вешается на GameObject с коллайдером.
/// </summary>
[DisallowMultipleComponent]
public class Pickup : MonoBehaviour
{
    [Header("Что это за предмет")]
    public ItemData item;
    [Min(1)] public int amount = 1;

    [Header("Подсказка")]
    [Tooltip("Если пусто — соберётся автоматически: «E — Название (x2)».")]
    public string promptText = "";

    [Header("Анимация в мире")]
    public bool spin = true;
    public float spinSpeed = 60f;
    public bool bob = true;
    public float bobAmplitude = 0.12f;
    public float bobSpeed = 2f;

    [Header("Подсветка при наведении (необязательно)")]
    [Tooltip("Рендереры, которым при наведении включится подсветка. Пусто — возьмутся все дочерние.")]
    public Renderer[] highlightRenderers;
    public Color highlightColor = new Color(1f, 0.9f, 0.4f, 1f);
    [Range(0f, 3f)] public float highlightIntensity = 0.6f;

    private Vector3 startLocalPos;
    private float bobTimer;
    private bool isHighlighted;
    private MaterialPropertyBlock[] cachedBlocks;

    // Кэш id свойства эмиссии (работает и в URP/HDRP, и в Built-in Standard)
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");

    void Awake()
    {
        startLocalPos = transform.localPosition;
        bobTimer = Random.Range(0f, Mathf.PI * 2f);

        if (highlightRenderers == null || highlightRenderers.Length == 0)
            highlightRenderers = GetComponentsInChildren<Renderer>();

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[Pickup] У {name} нет коллайдера — подобрать не получится.");
    }

    void Update()
    {
        if (spin)
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        if (bob)
        {
            bobTimer += Time.deltaTime * bobSpeed;
            Vector3 p = startLocalPos;
            p.y += Mathf.Sin(bobTimer) * bobAmplitude;
            transform.localPosition = p;
        }
    }

    /// <summary>Текст подсказки для HUD.</summary>
    public string GetPrompt()
    {
        if (!string.IsNullOrEmpty(promptText)) return promptText;
        if (item == null) return "Предмет не настроен";
        return amount > 1
            ? $"E — {item.itemName} (x{amount})"
            : $"E — {item.itemName}";
    }

    /// <summary>Включить/выключить подсветку. Вызывает инвентарь при наведении.</summary>
    public void SetHighlight(bool on)
    {
        if (isHighlighted == on || highlightRenderers == null) return;
        isHighlighted = on;

        Color c = on ? highlightColor * highlightIntensity : Color.black;

        if (cachedBlocks == null || cachedBlocks.Length != highlightRenderers.Length)
        {
            cachedBlocks = new MaterialPropertyBlock[highlightRenderers.Length];
            for (int i = 0; i < cachedBlocks.Length; i++)
                cachedBlocks[i] = new MaterialPropertyBlock();
        }

        for (int i = 0; i < highlightRenderers.Length; i++)
        {
            Renderer r = highlightRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(cachedBlocks[i]);
            cachedBlocks[i].SetColor(EmissionColorId, c);
            cachedBlocks[i].SetColor(EmissiveColorId, c);
            r.SetPropertyBlock(cachedBlocks[i]);
        }
    }

    /// <summary>
    /// Забрано столько-то штук. Если забрали всё — объект уничтожается,
    /// иначе остаётся с уменьшенным количеством (инвентарь был почти полон).
    /// </summary>
    public void OnPickedUp(int takenAmount)
    {
        amount -= takenAmount;
        if (amount <= 0)
        {
            SetHighlight(false);
            Destroy(gameObject);
        }
    }

    /// <summary>Совместимость со старым кодом: забрать всё.</summary>
    public void OnPickedUp() => OnPickedUp(amount);

#if UNITY_EDITOR
    void OnValidate()
    {
        if (amount < 1) amount = 1;
        if (item != null && !item.stackable) amount = 1;
    }
#endif
}
