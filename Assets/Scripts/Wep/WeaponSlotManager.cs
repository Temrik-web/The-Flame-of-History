using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Один слот в руках: в нём одновременно находится только одно оружие.
/// Управляет всеми EquippableWeapon в сцене — при экипировке одного
/// остальные скрываются вместе со своими скриптами.
///
/// Вешается на игрока (или на держатель оружия).
///
/// Логика подбора:
///   1) До подбора оружие в сцене выключено (Auto Disable On Start).
///   2) Игрок подбирает предмет — оружие остаётся выключенным, но появляется
///      в инвентаре с кнопкой «Экипировать».
///   3) Экипировка включает модель и её скрипты; кнопка исчезает.
///   4) Смена на другое оружие прячет предыдущее.
/// </summary>
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

    /// <summary>Экипировано другое оружие. Параметр может быть null (пустые руки).</summary>
    public event Action<EquippableWeapon> OnEquippedChanged;

    private EquippableWeapon current;

    /// <summary>Что сейчас в руках (может быть null).</summary>
    public EquippableWeapon Current => current;

    /// <summary>Id того, что в руках, или пустая строка.</summary>
    public string CurrentId => current != null ? current.weaponId : "";

    // =====================================================================
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
            // Всё выключаем, кроме помеченного как стартовое
            foreach (EquippableWeapon w in weapons)
                if (w != null) w.SetEquipped(w == startWeapon);
        }

        current = startWeapon;
        OnEquippedChanged?.Invoke(current);

        if (current != null)
            Debug.Log($"[WeaponSlot] Стартовое оружие: {current.displayName}");
        else
            Debug.Log("[WeaponSlot] Руки пустые. Оружие появится после подбора и экипировки.");
    }

    void Update()
    {
        // Во время диалога и при открытом инвентаре не переключаем
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive) return;
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen) return;

        if (Input.GetKeyDown(holsterKey)) Holster();

        if (cycleWithScrollWheel)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f) CycleOwned(scroll > 0f ? 1 : -1);
        }
    }

    // =====================================================================
    /// <summary>Найти все EquippableWeapon в сцене, включая выключенные объекты.</summary>
    public void CollectFromScene()
    {
        weapons.Clear();
        // true — включая неактивные объекты: оружие до подбора выключено
        foreach (EquippableWeapon w in FindObjectsOfType<EquippableWeapon>(true))
            weapons.Add(w);

        Debug.Log($"[WeaponSlot] Найдено оружия в сцене: {weapons.Count}");
    }

    /// <summary>Найти оружие по id. Возвращает null, если такого нет.</summary>
    public EquippableWeapon Find(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return null;

        foreach (EquippableWeapon w in weapons)
            if (w != null && w.weaponId == weaponId) return w;

        return null;
    }

    /// <summary>Есть ли такое оружие в сцене.</summary>
    public bool Has(string weaponId) => Find(weaponId) != null;

    /// <summary>Экипировано ли сейчас именно это оружие.</summary>
    public bool IsEquipped(string weaponId) =>
        !string.IsNullOrEmpty(weaponId) && CurrentId == weaponId;

    /// <summary>
    /// Взять в руки оружие по id. Остальное скрывается.
    /// Возвращает false, если оружия с таким id в сцене нет.
    /// </summary>
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
            Debug.Log($"[WeaponSlot] {target.displayName} уже в руках.");
            return true;
        }

        foreach (EquippableWeapon w in weapons)
            if (w != null && w != target) w.SetEquipped(false);

        target.SetEquipped(true, playSound: true);
        current = target;

        OnEquippedChanged?.Invoke(current);
        Debug.Log($"[WeaponSlot] Экипировано: {target.displayName}");
        return true;
    }

    /// <summary>Спрятать всё — пустые руки.</summary>
    public void Holster()
    {
        if (current == null) return;

        foreach (EquippableWeapon w in weapons)
            if (w != null) w.SetEquipped(false);

        current = null;
        OnEquippedChanged?.Invoke(null);
        Debug.Log("[WeaponSlot] Оружие убрано.");
    }

    /// <summary>
    /// Переключиться на следующее/предыдущее оружие, которое есть в инвентаре.
    /// Оружие, которого игрок не подобрал, пропускается.
    /// </summary>
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

    /// <summary>Оружие, которое игрок подобрал (есть соответствующий ItemData в инвентаре).</summary>
    public List<EquippableWeapon> GetOwnedWeapons()
    {
        var result = new List<EquippableWeapon>();
        InventorySystem inv = InventorySystem.Instance;

        foreach (EquippableWeapon w in weapons)
        {
            if (w == null) continue;

            // Без инвентаря считаем всё оружие доступным — удобно для тестов
            if (inv == null) { result.Add(w); continue; }

            if (inv.HasWeaponItem(w.weaponId)) result.Add(w);
        }

        return result;
    }

    // =====================================================================
    /// <summary>Статический помощник: экипировать по id, если менеджер есть в сцене.</summary>
    public static bool EquipById(string weaponId)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[WeaponSlot] WeaponSlotManager не найден в сцене — экипировать нечем.");
            return false;
        }
        return Instance.Equip(weaponId);
    }

    /// <summary>Статический помощник: проверить, что оружие уже в руках.</summary>
    public static bool IsEquippedById(string weaponId) =>
        Instance != null && Instance.IsEquipped(weaponId);
}
