using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD оружия на обычном Canvas: патроны, запас магазинов, режим огня.
///
/// Зачем: старый вывод в Wep.OnGUI использует IMGUI, а тот всегда рисуется
/// поверх любого Canvas и игнорирует сортировку. Из-за этого он налезал на
/// инвентарь и диалоги. Этот компонент выключает Wep.drawDebugGUI и рисует
/// то же самое на Canvas, который корректно скрывается вместе с остальным UI.
///
/// Вешается на игрока. UI собирается кодом, настраивать ничего не нужно.
/// </summary>
[DisallowMultipleComponent]
public class WeaponHudUI : MonoBehaviour
{
    [Header("Ссылки")]
    [Tooltip("Пусто — найдётся первый Wep в сцене, включая выключенные объекты.")]
    public Wep weapon;

    [Header("Оформление")]
    public Color textColor = new Color(0.94f, 0.95f, 0.97f);
    public Color accentColor = new Color(1f, 0.66f, 0.28f);
    public Color warningColor = new Color(0.88f, 0.42f, 0.35f);
    [Tooltip("Отступ от правого нижнего угла экрана.")]
    public Vector2 margin = new Vector2(46f, 38f);

    [Header("Поведение")]
    [Tooltip("Скрывать HUD, когда открыт инвентарь или идёт диалог.")]
    public bool hideWhenUIOpen = true;
    [Tooltip("Скрывать HUD, когда в руках нет оружия.")]
    public bool hideWhenUnarmed = true;
    [Tooltip("Выключить старый WeaponUI в сцене и Wep.OnGUI, чтобы счётчики не дублировались.")]
    public bool disableLegacyHud = true;

    [Header("Шрифт")]
    public TMP_FontAsset fontAsset;

    private CanvasGroup group;
    private TextMeshProUGUI ammoLabel;
    private TextMeshProUGUI magazinesLabel;
    private TextMeshProUGUI modeLabel;

    private float visibleAlpha;

    // =====================================================================
    void Awake()
    {
        if (fontAsset == null)
            fontAsset = Resources.Load<TMP_FontAsset>("InventoryFont SDF");

        BuildUI();
    }

    void Start()
    {
        FindWeapon();
        if (disableLegacyHud) DisableLegacyHud();
    }

    void FindWeapon()
    {
        if (weapon != null) return;

        // true — включая выключенные объекты: оружие спрятано до экипировки
        Wep[] found = FindObjectsOfType<Wep>(true);
        if (found.Length > 0) weapon = found[0];
    }

    /// <summary>
    /// Погасить дубли счётчиков: IMGUI-вывод внутри Wep и старый WeaponUI на Canvas.
    /// IMGUI рисуется поверх всего и игнорирует сортировку — именно он налезал на инвентарь.
    /// </summary>
    void DisableLegacyHud()
    {
        foreach (Wep w in FindObjectsOfType<Wep>(true))
            if (w != null) w.drawDebugGUI = false;

        foreach (WeaponUI legacy in FindObjectsOfType<WeaponUI>(true))
        {
            if (legacy == null) continue;
            legacy.enabled = false;

            // Гасим и сами тексты: без Update они застынут с последним значением
            if (legacy.ammoText != null) legacy.ammoText.gameObject.SetActive(false);
            if (legacy.modeText != null) legacy.modeText.gameObject.SetActive(false);
            if (legacy.hintText != null) legacy.hintText.gameObject.SetActive(false);

            Debug.Log($"[WeaponHud] Старый WeaponUI на «{legacy.name}» выключен — его заменил новый HUD.");
        }
    }

    void Update()
    {
        if (weapon == null)
        {
            FindWeapon();
            if (weapon == null) { SetVisible(false); return; }
        }

        // Оружие в руках = объект активен и скрипт включён
        bool armed = weapon.gameObject.activeInHierarchy && weapon.enabled;

        bool uiBusy = false;
        if (hideWhenUIOpen)
        {
            if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen) uiBusy = true;
            if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive) uiBusy = true;
        }

        bool show = !uiBusy && (armed || !hideWhenUnarmed);
        SetVisible(show);

        if (!show) return;

        int ammo = weapon.currentAmmo;
        int max = Mathf.Max(1, weapon.maxAmmo);
        int mags = weapon.spareMagazines;

        // Цвет по остатку: пусто — красный, меньше четверти — акцент
        string ammoHex = ColorUtility.ToHtmlStringRGB(
            ammo <= 0 ? warningColor
                      : ammo <= max * 0.25f ? accentColor
                                            : textColor);

        ammoLabel.text = $"<color=#{ammoHex}><size=140%>{ammo}</size></color>" +
                         $"<color=#5a5f69> / {max}</color>";

        string magHex = ColorUtility.ToHtmlStringRGB(mags <= 0 ? warningColor : textColor);
        magazinesLabel.text = weapon.IsReloading
            ? "<color=#e0b45a>перезарядка…</color>"
            : $"<color=#5a5f69>магазины</color>  <color=#{magHex}>{mags}</color>";

        modeLabel.text = weapon.currentFireMode == Wep.FireMode.Auto
            ? "<color=#5a5f69>режим</color>  Авто"
            : "<color=#5a5f69>режим</color>  Одиночный";
    }

    void SetVisible(bool show)
    {
        if (group == null) return;

        float target = show ? 1f : 0f;
        visibleAlpha = Mathf.MoveTowards(visibleAlpha, target, Time.unscaledDeltaTime * 8f);
        group.alpha = visibleAlpha;
    }

    // =====================================================================
    void BuildUI()
    {
        Sprite round = UIShapes.RoundedRect(48, 12);

        Canvas canvas = new GameObject("WeaponHudCanvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;   // ниже диалогов (90) и инвентаря (100)

        CanvasScaler scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject root = NewUI("WeaponHud", canvas.transform);
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = new Vector2(-margin.x, margin.y);
        rect.sizeDelta = new Vector2(320f, 132f);

        group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;   // HUD не должен перехватывать клики

        Image bg = root.AddComponent<Image>();
        bg.sprite = round;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.03f, 0.035f, 0.045f, 0.55f);
        bg.raycastTarget = false;

        // Акцентная полоска справа — привязывает блок к краю экрана
        GameObject edge = NewUI("AccentEdge", root.transform);
        RectTransform edgeRect = (RectTransform)edge.transform;
        edgeRect.anchorMin = new Vector2(1f, 0f);
        edgeRect.anchorMax = new Vector2(1f, 1f);
        edgeRect.pivot = new Vector2(1f, 0.5f);
        edgeRect.offsetMin = new Vector2(-3f, 16f);
        edgeRect.offsetMax = new Vector2(0f, -16f);
        Image edgeImg = edge.AddComponent<Image>();
        edgeImg.sprite = UIShapes.VerticalGradient(64, 0.15f, 0.8f);
        edgeImg.color = accentColor;
        edgeImg.raycastTarget = false;

        ammoLabel = MakeLabel("Ammo", root.transform, 30f);
        RectTransform ammoRect = (RectTransform)ammoLabel.transform;
        ammoRect.anchorMin = new Vector2(0f, 1f);
        ammoRect.anchorMax = new Vector2(1f, 1f);
        ammoRect.pivot = new Vector2(0.5f, 1f);
        ammoRect.offsetMin = new Vector2(20f, -58f);
        ammoRect.offsetMax = new Vector2(-18f, -12f);
        ammoLabel.alignment = TextAlignmentOptions.Right;

        magazinesLabel = MakeLabel("Magazines", root.transform, 19f);
        RectTransform magRect = (RectTransform)magazinesLabel.transform;
        magRect.anchorMin = new Vector2(0f, 1f);
        magRect.anchorMax = new Vector2(1f, 1f);
        magRect.pivot = new Vector2(0.5f, 1f);
        magRect.offsetMin = new Vector2(20f, -88f);
        magRect.offsetMax = new Vector2(-18f, -60f);
        magazinesLabel.alignment = TextAlignmentOptions.Right;

        modeLabel = MakeLabel("Mode", root.transform, 17f);
        RectTransform modeRect = (RectTransform)modeLabel.transform;
        modeRect.anchorMin = new Vector2(0f, 0f);
        modeRect.anchorMax = new Vector2(1f, 0f);
        modeRect.pivot = new Vector2(0.5f, 0f);
        modeRect.offsetMin = new Vector2(20f, 12f);
        modeRect.offsetMax = new Vector2(-18f, 38f);
        modeLabel.alignment = TextAlignmentOptions.Right;
    }

    static GameObject NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    TextMeshProUGUI MakeLabel(string name, Transform parent, float size)
    {
        TextMeshProUGUI label = NewUI(name, parent).AddComponent<TextMeshProUGUI>();
        if (fontAsset != null) label.font = fontAsset;
        label.fontSize = size;
        label.color = textColor;
        label.richText = true;
        label.raycastTarget = false;
        return label;
    }
}
