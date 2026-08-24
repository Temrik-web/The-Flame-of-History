using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Блокирует стрельбу и (опционально) движение, пока инвентарь открыт.
/// Ничего не меняет в существующих скриптах: просто выключает их компоненты
/// на время открытия и включает обратно при закрытии.
///
/// Вешается на игрока рядом с InventorySystem.
/// </summary>
[DisallowMultipleComponent]
public class InventoryInputBlocker : MonoBehaviour
{
    [Header("Ссылки")]
    public InventorySystem inventory;

    [Header("Что блокировать")]
    [Tooltip("Выключать скрипты оружия (Wep, WeaponShooting) при открытом инвентаре.")]
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
        RestoreAll();
    }

    void HandleToggled(bool open)
    {
        if (open) BlockAll();
        else RestoreAll();
    }

    void BlockAll()
    {
        RestoreAll();

        if (blockWeapons)
        {
            foreach (var w in FindObjectsOfType<Wep>()) Disable(w);
            foreach (var w in FindObjectsOfType<WeaponShooting>()) Disable(w);
        }

        if (blockMovement)
        {
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

    void RestoreAll()
    {
        for (int i = 0; i < disabledByUs.Count; i++)
        {
            if (disabledByUs[i] != null) disabledByUs[i].enabled = true;
        }
        disabledByUs.Clear();
    }
}
