using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Один слот в руках: экипирует одно оружие, остальные прячет. Подбор: до подбора всё выключено, экипировка включает модель и скрипты.</summary>
[DisallowMultipleComponent]
public class WeaponSlotManager : MonoBehaviour
{
    public static WeaponSlotManager Instance { get; private set; }

    [Header("Оружие в сцене")]
    [Tooltip("Все EquippableWeapon, участвующие в слоте. " +
             "Пусто — соберутся автоматически со всей сцены при старте.")]
    public List<EquippableWeapon> weapons = new List<EquippableWeapon>();

    [Header("Старт")]
    [Tooltip("Выключить всё оружие при старте сцены — до того, как игрок его подберёт. " +
             "Исключение: оружие с галочкой Equipped On Start.")]
    public bool autoDisableOnStart = true;

    [Header("Переключение с клавиатуры")]
    [Tooltip("Циклическая смена оружия колесом мыши.")]
    public bool cycleWithScrollWheel = true;
    [Tooltip("Спрятать оружие (пустые руки).")]
    public KeyCode holsterKey = KeyCode.Alpha0;

    [Header("Прицел")]
    [Tooltip("Скрывать перекрестие, когда в руках ничего нет. " +
             "Владелец объекта — Wep, но при пустых руках он выключен и " +
             "спрятать прицел больше некому.")]
    public bool hideCrosshairWhenUnarmed = true;

    [Tooltip("Объект перекрестия. Пусто — возьмётся у первого Wep в сцене.")]
    public GameObject crosshairObject;

    [Header("Анимация смены")]
    [Tooltip("Плавно опускать старое оружие и поднимать новое. " +
             "Длительности и амплитуды настраиваются на самом предмете " +
             "(Wep / HeldItem → «Появление в руках»).")]
    public bool animateSwitch = true;

    [Tooltip("Страховка: дольше этого опускание не ждём, даже если на предмете стоит больше.")]
    [Min(0.05f)] public float maxHolsterWait = 0.45f;

    /// <summary>Экипировано другое оружие. Может быть null (пустые руки).</summary>
    public event Action<EquippableWeapon> OnEquippedChanged;
    private EquippableWeapon current;
    private Coroutine switchRoutine;
    private int switchVersion;
    private EquippableWeapon holstering;
    /// <summary>Что сейчас в руках (может быть null).</summary>
    public EquippableWeapon Current => current;
    /// <summary>Id того, что в руках, или пустая строка.</summary>
    public string CurrentId => current != null ? current.weaponId : "";
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[WeaponSlot] На сцене уже есть WeaponSlotManager ({Instance.name}). " +
                             $"Компонент на {name} отключён.");
            enabled = false;
            return;
        }
        Instance = this;

        if (weapons == null || weapons.Count == 0) CollectFromScene();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EquippableWeapon startWeapon = null;
        foreach (EquippableWeapon w in weapons)
        {
            if (w == null) continue;
            if (w.equippedOnStart && startWeapon == null) startWeapon = w;
        }
        if (autoDisableOnStart)
        {
            foreach (EquippableWeapon w in weapons)
                if (w != null) w.SetEquipped(w == startWeapon);
        }
        current = startWeapon;
        OnEquippedChanged?.Invoke(current);
        ApplyCrosshairVisibility();
    }
    // При пустых руках Wep выключен и прицел гасить некому — показываем только когда есть что-то в руках
    void ApplyCrosshairVisibility()
    {
        if (!hideCrosshairWhenUnarmed) return;

        GameObject cross = ResolveCrosshair();
        if (cross == null) return;

        bool show = current != null && !ItemHidesCrosshair(current);
        if (cross.activeSelf != show) cross.SetActive(show);
    }

    /// <summary>Прячет ли экипированный предмет перекрестие сам (нож, лопата).</summary>
    static bool ItemHidesCrosshair(EquippableWeapon weapon)
    {
        if (weapon == null) return false;

        foreach (HeldItem held in weapon.GetComponentsInChildren<HeldItem>(true))
            if (held != null && held.HidesCrosshair) return true;

        return false;
    }

    GameObject ResolveCrosshair()
    {
        if (crosshairObject != null) return crosshairObject;

        foreach (Wep w in FindObjectsOfType<Wep>(true))
        {
            if (w != null && w.crosshairObject != null)
            {
                crosshairObject = w.crosshairObject;
                break;
            }
        }

        return crosshairObject;
    }

    void Update()
    {
        // При пустых руках гасим каждый кадр: порядок включения компонентов может быть обратным
        if (current == null) ApplyCrosshairVisibility();
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive) return;
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen) return;
        if (Input.GetKeyDown(holsterKey)) Holster();
        if (cycleWithScrollWheel)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f) CycleOwned(scroll > 0f ? 1 : -1);
        }
    }
    public void CollectFromScene()
    {
        weapons.Clear();
        foreach (EquippableWeapon w in FindObjectsOfType<EquippableWeapon>(true))
            weapons.Add(w);
    }

    /// <summary>Найти оружие по id. Null если нет.</summary>
    public EquippableWeapon Find(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;
        foreach (EquippableWeapon w in weapons)
            if (w != null && w.weaponId == weaponId) return w;
        return null;
    }
    public bool Has(string weaponId) => Find(weaponId) != null;
    public bool IsEquipped(string weaponId) =>
        !string.IsNullOrEmpty(weaponId) && CurrentId == weaponId;
    /// <summary>Взять в руки по id, остальное прячется. False если id нет в сцене.</summary>
    public bool Equip(string weaponId)
    {
        EquippableWeapon target = Find(weaponId);
        if (target == null)
        {
            Debug.LogWarning($"[WeaponSlot] Оружие с id '{weaponId}' не найдено в сцене. " +
                              "Проверь, что на модели висит EquippableWeapon с этим Weapon Id.");
            return false;
        }

        if (current == target)
        {
            // Возврат к оружию, которое прямо сейчас убирается: отменяем опускание.
            if (holstering == target)
            {
                switchVersion++;
                if (switchRoutine != null) { StopCoroutine(switchRoutine); switchRoutine = null; }
                holstering = null;
                target.EquipNow(playSound: false);
                OnEquippedChanged?.Invoke(current);
                ApplyCrosshairVisibility();
            }
            return true;
        }

        StartSwitch(target);
        return true;
    }

    /// <summary>Спрятать всё — пустые руки.</summary>
    public void Holster()
    {
        if (current == null && holstering == null) return;
        switchVersion++;
        if (switchRoutine != null) { StopCoroutine(switchRoutine); switchRoutine = null; }
        switchRoutine = StartCoroutine(HolsterRoutine(switchVersion));
    }

    void StartSwitch(EquippableWeapon target)
    {
        switchVersion++;
        // Прерванное опускание чужого — гасим сразу, очередь не копим (защита от спама колесом).
        if (holstering != null && holstering != target)
        {
            holstering.SetEquipped(false);
            holstering = null;
        }
        if (switchRoutine != null) { StopCoroutine(switchRoutine); switchRoutine = null; }
        switchRoutine = StartCoroutine(SwitchRoutine(target, switchVersion));
    }

    // Старое красиво опускается, потом включается новое с подъёмом.
    // Подъём нового проигрывается сам через OnEnable (Wep/HeldItem).
    IEnumerator SwitchRoutine(EquippableWeapon target, int version)
    {
        EquippableWeapon previous = current;
        current = target;

        if (previous != null && previous != target && previous.gameObject.activeSelf)
        {
            holstering = previous;
            if (animateSwitch && previous.BeginHolster())
            {
                float wait = Mathf.Min(previous.HolsterWaitTime, maxHolsterWait);
                float t = 0f;
                while (t < wait)
                {
                    if (version != switchVersion) yield break;
                    t += Time.deltaTime;
                    yield return null;
                }
                if (version != switchVersion) yield break;
            }
            previous.SetEquipped(false);
            if (holstering == previous) holstering = null;
        }

        // Всё лишнее — сразу (рассинхрон после быстрого листания).
        foreach (EquippableWeapon w in weapons)
            if (w != null && w != target && w.gameObject.activeSelf) w.SetEquipped(false);
        if (holstering != null && holstering != target)
        {
            holstering.SetEquipped(false);
            holstering = null;
        }

        // Включаем новое; если это возврат к недотушенному — подъём переигрывается.
        if (holstering == target) holstering = null;
        target.EquipNow(playSound: true);

        OnEquippedChanged?.Invoke(current);
        ApplyCrosshairVisibility();
        switchRoutine = null;
    }

    IEnumerator HolsterRoutine(int version)
    {
        // Хвост прерванного переключения — сразу, иначе зависнет в полуопущенной позе.
        if (holstering != null)
        {
            holstering.SetEquipped(false);
            holstering = null;
        }
        EquippableWeapon previous = current;
        current = null;

        if (previous != null && previous.gameObject.activeSelf)
        {
            holstering = previous;
            if (animateSwitch && previous.BeginHolster())
            {
                float wait = Mathf.Min(previous.HolsterWaitTime, maxHolsterWait);
                float t = 0f;
                while (t < wait)
                {
                    if (version != switchVersion) yield break;
                    t += Time.deltaTime;
                    yield return null;
                }
                if (version != switchVersion) yield break;
            }
            previous.SetEquipped(false);
            if (holstering == previous) holstering = null;
        }

        OnEquippedChanged?.Invoke(null);
        ApplyCrosshairVisibility();
        switchRoutine = null;
    }
    // Листает только подобранное (есть ItemData в инвентаре), остальное пропускает
    public void CycleOwned(int direction)
    {
        List<EquippableWeapon> owned = GetOwnedWeapons();
        if (owned.Count == 0) return;

        int index = current != null ? owned.IndexOf(current) : -1;
        int next = index < 0
            ? (direction > 0 ? 0 : owned.Count - 1)
            : (index + direction + owned.Count) % owned.Count;

        Equip(owned[next].weaponId);
    }

    public List<EquippableWeapon> GetOwnedWeapons()
    {
        var result = new List<EquippableWeapon>();
        InventorySystem inv = InventorySystem.Instance;
        foreach (EquippableWeapon w in weapons)
        {
            if (w == null) continue;
            if (inv == null) { result.Add(w); continue; }
            if (inv.HasWeaponItem(w.weaponId)) result.Add(w);
        }
        return result;
    }
    public static bool EquipById(string weaponId)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[WeaponSlot] WeaponSlotManager не найден в сцене — экипировать нечем.");
            return false;
        }
        return Instance.Equip(weaponId);
    }

    public static bool IsEquippedById(string weaponId) =>
        Instance != null && Instance.IsEquipped(weaponId);
}
