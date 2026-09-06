using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Запрещает игровой ввод, пока открыт инвентарь.
///
/// Оружие блокируется через PlayerInputLock: скрипты остаются включёнными,
/// но сами пропускают Update. Так надёжнее, чем выключать enabled — иначе
/// смена оружия в инвентаре и восстановление компонентов конфликтуют,
/// и спрятанное оружие снова начинает стрелять.
///
/// Контроллер движения выключается компонентом: у него нет своей проверки,
/// а он один и не участвует в переключении.
///
/// Вешается на игрока рядом с InventorySystem.
/// </summary>
[DisallowMultipleComponent]
public class InventoryInputBlocker : MonoBehaviour
{
    [Header("Ссылки")]
    public InventorySystem inventory;

    [Header("Что блокировать")]
    [Tooltip("Запрещать стрельбу, перезарядку и смену оружия при открытом инвентаре.")]
    public bool blockWeapons = true;

    [Tooltip("Выключать контроллер игрока (движение + обзор мышью).")]
    public bool blockMovement = true;

    [Header("Дополнительные скрипты")]
    [Tooltip("Любые свои компоненты, которые надо выключить при открытии инвентаря.")]
    public List<MonoBehaviour> extraToDisable = new List<MonoBehaviour>();

    // Что было выключено нами — чтобы не включить то, что было выключено раньше
    private readonly List<MonoBehaviour> disabledByUs = new List<MonoBehaviour>();

    void Awake()
    {
        if (inventory == null) inventory = GetComponent<InventorySystem>();
        if (inventory == null) inventory = InventorySystem.Instance;
        if (inventory == null) inventory = FindObjectOfType<InventorySystem>();

        if (inventory == null)
        {
            Debug.LogWarning("[InventoryInputBlocker] InventorySystem не найден. Компонент отключён.");
            enabled = false;
        }
    }

    void OnEnable()
    {
        if (inventory != null) inventory.OnToggled += HandleToggled;
    }

    void OnDisable()
    {
        if (inventory != null) inventory.OnToggled -= HandleToggled;
        Unblock();
    }

    void HandleToggled(bool open)
    {
        if (open) Block();
        else Unblock();
    }

    void Block()
    {
        Unblock();

        // Оружие: замок, а не выключение компонентов
        if (blockWeapons) PlayerInputLock.SetWeaponLock(this, true);

        if (blockMovement)
        {
            PlayerInputLock.SetMovementLock(this, true);

            foreach (var c in FindObjectsOfType<EasyPeasyFirstPersonController.FirstPersonController>())
                Disable(c);
        }

        foreach (var m in extraToDisable) Disable(m);
    }

    void Disable(MonoBehaviour target)
    {
        if (target == null || !target.enabled) return;
        target.enabled = false;
        disabledByUs.Add(target);
    }

    void Unblock()
    {
        PlayerInputLock.ReleaseAll(this);

        for (int i = 0; i < disabledByUs.Count; i++)
        {
            if (disabledByUs[i] != null) disabledByUs[i].enabled = true;
        }
        disabledByUs.Clear();
    }
}
