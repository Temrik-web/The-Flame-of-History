using UnityEngine;

/// <summary>
/// Маркер оружия в сцене, которое можно взять в руки.
/// Вешается на объект модели: Ppsh-41(GR), Rgd-33(GR), Knife и т.д.
///
/// Сам ничего не делает — только объявляет свой id и умеет
/// показываться/скрываться. Переключением занимается WeaponSlotManager.
///
/// Важно: все такие объекты должны быть детьми одного держателя
/// (например, WeaponHolder под камерой) и иметь корректные локальные
/// позицию/поворот/масштаб — они не меняются при переключении.
/// </summary>
[DisallowMultipleComponent]
public class EquippableWeapon : MonoBehaviour
{
    [Header("Идентификация")]
    [Tooltip("Уникальный id. Должен совпадать с полем Equip Weapon Id у ItemData. " +
             "Например: ppsh41, rgd33, knife.")]
    public string weaponId = "";

    [Tooltip("Название для интерфейса.")]
    public string displayName = "Оружие";

    [Header("Состояние")]
    [Tooltip("Экипировано ли это оружие при старте сцены. " +
             "Если ни одно не помечено — руки будут пустыми.")]
    public bool equippedOnStart = false;

    [Header("Скрипты оружия")]
    [Tooltip("Скрипты, которые включаются вместе с моделью (Wep, WeaponShooting и т.п.). " +
             "Пусто — соберутся автоматически с этого объекта и его детей.")]
    public MonoBehaviour[] weaponScripts;

    [Header("Звук")]
    public AudioClip equipSound;

    private bool isEquipped;

    /// <summary>Экипировано ли сейчас.</summary>
    public bool IsEquipped => isEquipped;

    void Awake()
    {
        if (string.IsNullOrEmpty(weaponId))
            weaponId = name;

        if (weaponScripts == null || weaponScripts.Length == 0)
            weaponScripts = CollectOwnScripts();
    }

    /// <summary>
    /// Собрать скрипты оружия с этого объекта и детей.
    /// Себя и посторонние компоненты вроде Pickup не берём.
    /// </summary>
    MonoBehaviour[] CollectOwnScripts()
    {
        var found = new System.Collections.Generic.List<MonoBehaviour>();

        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb == this) continue;
            if (mb is Wep || mb is WeaponShooting) found.Add(mb);
        }

        return found.ToArray();
    }

    /// <summary>Показать или спрятать оружие вместе с его скриптами.</summary>
    public void SetEquipped(bool equipped, bool playSound = false)
    {
        isEquipped = equipped;

        // Скрипты выключаем до скрытия объекта: иначе Wep продолжит
        // считать отдачу и разброс в кадре, когда модель уже не видна
        if (weaponScripts != null)
        {
            foreach (MonoBehaviour mb in weaponScripts)
                if (mb != null) mb.enabled = equipped;
        }

        gameObject.SetActive(equipped);

        if (equipped && playSound && equipSound != null)
            AudioSource.PlayClipAtPoint(equipSound, transform.position, 0.8f);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
#endif
}
