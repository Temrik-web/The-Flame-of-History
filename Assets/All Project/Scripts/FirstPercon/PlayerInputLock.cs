using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Глобальный «стоп-кран» для игрового ввода.
///
/// Зачем нужен вместо выключения компонентов: если инвентарь выключал
/// Wep.enabled, а игрок в это время менял оружие, при закрытии инвентаря
/// включались сразу оба скрипта — и спрятанное оружие продолжало стрелять.
/// Флаг решает конфликт: скрипты остаются включёнными, но сами
/// пропускают обработку ввода, пока замок стоит.
///
/// Замки считаются по владельцам, поэтому инвентарь и диалог могут
/// держать замок одновременно и не мешать друг другу.
/// </summary>
public static class PlayerInputLock
{
    private static readonly HashSet<object> weaponLocks = new HashSet<object>();
    private static readonly HashSet<object> movementLocks = new HashSet<object>();

    /// <summary>Заблокирована ли стрельба, перезарядка и смена оружия.</summary>
    public static bool WeaponsLocked => weaponLocks.Count > 0;

    /// <summary>Заблокировано ли движение и обзор.</summary>
    public static bool MovementLocked => movementLocks.Count > 0;

    /// <summary>Поставить/снять замок на оружие от лица владельца.</summary>
    public static void SetWeaponLock(object owner, bool locked)
    {
        if (owner == null) return;
        if (locked) weaponLocks.Add(owner);
        else weaponLocks.Remove(owner);
    }

    /// <summary>Поставить/снять замок на движение от лица владельца.</summary>
    public static void SetMovementLock(object owner, bool locked)
    {
        if (owner == null) return;
        if (locked) movementLocks.Add(owner);
        else movementLocks.Remove(owner);
    }

    /// <summary>Снять все замки данного владельца.</summary>
    public static void ReleaseAll(object owner)
    {
        if (owner == null) return;
        weaponLocks.Remove(owner);
        movementLocks.Remove(owner);
    }

    /// <summary>
    /// Полный сброс. Нужен при смене сцены: статические поля живут дольше
    /// объектов, и «повисший» замок оставил бы игрока обездвиженным.
    /// </summary>
    public static void Clear()
    {
        weaponLocks.Clear();
        movementLocks.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => Clear();
}
