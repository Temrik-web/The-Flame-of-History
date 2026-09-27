using UnityEngine;
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
    [Tooltip("Брать цвет подсветки из редкости предмета.")]
    public bool useRarityColor = true;

    [Header("Свечение в мире")]
    [Tooltip("Создать точечный источник света цвета редкости. Работает в любом рендер-пайплайне, " +
             "в отличие от эмиссии материала.")]
    public bool createGlowLight = true;
    [Range(0f, 5f)] public float glowIntensity = 1.1f;
    public float glowRange = 2.2f;
    [Tooltip("Насколько свет «дышит» (0 — ровный).")]
    [Range(0f, 1f)] public float glowPulse = 0.35f;
    [Tooltip("Во сколько раз ярче светится предмет, на который смотрит игрок.")]
    public float glowHoverBoost = 2.2f;

    [Header("Тонирование материала")]
    [Tooltip("Перекрасить материал в цвет редкости. Полезно для универсального куба-заглушки.")]
    public bool tintMaterialByRarity = true;

    private Vector3 startLocalPos;
    private float bobTimer;
    private bool isHighlighted;
    private MaterialPropertyBlock[] cachedBlocks;

    private Light glowLight;
    private float glowBaseIntensity;
    private float glowBlend;
    private Rigidbody cachedRb;
    // Id эмиссии закэшированы: разные пайплайны используют разные имена свойств
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        cachedRb = GetComponent<Rigidbody>();
        bobTimer = Random.Range(0f, Mathf.PI * 2f);

        if (highlightRenderers == null || highlightRenderers.Length == 0)
            highlightRenderers = GetComponentsInChildren<Renderer>();

        if (GetComponentInChildren<Collider>() == null)
            Debug.LogWarning($"[Pickup] У {name} нет коллайдера — подобрать не получится.");

        if (createGlowLight) CreateGlow();
        ApplyItemAppearance();
    }

    void Start()
    {
        // Базу bob берём в Start, а не в Awake: при Instantiate(prefab, pos)
        // и при расстановке через wizard позиция выставляется уже после Awake,
        // иначе предмет «стрибає» назад к нулю префаба.
        startLocalPos = transform.localPosition;
    }

    /// <summary>Оновити базу bob після зовнішнього переміщення (телепорт, дроп).</summary>
    public void RecaptureBase()
    {
        // Rigidbody могли причепити вже після Awake (дроп клонує модель
        // і вішає фізику постфактум) — оновлюємо кеш, інакше bob пришиє
        // об'єкт до точки і він зависне в повітрі.
        cachedRb = GetComponent<Rigidbody>();
        startLocalPos = transform.localPosition;
    }

    /// <summary>Настроить предмет после Instantiate, когда Awake уже выполнился.</summary>
    public void Configure(ItemData newItem, int newAmount)
    {
        item = newItem;
        amount = Mathf.Max(1, newAmount);
        ApplyItemAppearance();
    }

    void ApplyItemAppearance()
    {
        if (item == null) return;
        if (useRarityColor) highlightColor = item.RarityColor;
        if (tintMaterialByRarity) TintMaterial(item.RarityColor);
        if (glowLight != null) glowLight.color = highlightColor;
    }

    void TintMaterial(Color color)
    {
        if (highlightRenderers == null) return;
        // Приглушаем цвет редкости (0.45), чтобы не выглядело игрушечным
        Color tint = Color.Lerp(color, new Color(0.35f, 0.36f, 0.40f), 0.45f);
        var block = new MaterialPropertyBlock();
        foreach (Renderer r in highlightRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            r.SetPropertyBlock(block);
        }
    }

    void CreateGlow()
    {
        var go = new GameObject("Glow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        glowLight = go.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = highlightColor;
        glowLight.range = glowRange;
        glowLight.intensity = glowIntensity;
        glowLight.shadows = LightShadows.None;
        glowBaseIntensity = glowIntensity;
    }

    void Update()
    {
        if (spin)
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        // Фізика веде об'єкт сама: bob через localPosition боровся б із гравітацією
        // і дроп зависав би в повітрі. Поки Rigidbody не kinematic — не чіпаємо позицію.
        // Ліниво підхоплюємо Rigidbody, доданий після Awake (так робить дроп клонів).
        if (cachedRb == null) cachedRb = GetComponent<Rigidbody>();
        bool physicsDriven = cachedRb != null && !cachedRb.isKinematic && !cachedRb.IsSleeping();
        if (bob && !physicsDriven)
        {
            bobTimer += Time.deltaTime * bobSpeed;
            Vector3 p = startLocalPos;
            p.y += Mathf.Sin(bobTimer) * bobAmplitude;
            transform.localPosition = p;
        }
        else if (bob && physicsDriven)
        {
            // Щоб не було стрибка фази, коли тіло засне
            bobTimer += Time.deltaTime * bobSpeed;
        }

        UpdateGlow();
    }

    void UpdateGlow()
    {
        if (glowLight == null) return;
        float target = isHighlighted ? 1f : 0f;
        glowBlend = Mathf.Lerp(glowBlend, target, 1f - Mathf.Exp(-10f * Time.deltaTime));

        float pulse = 1f + Mathf.Sin(Time.time * 2.1f + bobTimer) * glowPulse * 0.5f;
        float boost = Mathf.Lerp(1f, glowHoverBoost, glowBlend);

        glowLight.intensity = glowBaseIntensity * pulse * boost;
        glowLight.range = glowRange * Mathf.Lerp(1f, 1.35f, glowBlend);
    }

    public string GetPrompt()
    {
        if (!string.IsNullOrEmpty(promptText)) return promptText;
        if (item == null) return "Предмет не настроен";
        return amount > 1
            ? $"E — {item.itemName} (x{amount})"
            : $"E — {item.itemName}";
    }

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

    // Частичный подбор: остаток остаётся лежать, если инвентарь был почти полон
    public void OnPickedUp(int takenAmount)
    {
        amount -= takenAmount;
        if (amount <= 0)
        {
            SetHighlight(false);
            Destroy(gameObject);
        }
    }

    public void OnPickedUp() => OnPickedUp(amount);

#if UNITY_EDITOR
    void OnValidate()
    {
        if (amount < 1) amount = 1;
        if (item != null && !item.stackable) amount = 1;
        if (useRarityColor && item != null) highlightColor = item.RarityColor;
    }

    void OnDrawGizmos()
    {
        if (item == null) return;
        Gizmos.color = item.RarityColor;
        Gizmos.DrawWireSphere(transform.position, 0.28f);
    }
#endif
}
