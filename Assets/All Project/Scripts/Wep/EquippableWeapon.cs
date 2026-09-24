using UnityEngine;

/// <summary>Маркер оружия для WeaponSlotManager. Модель кладёт на слой HeldObjects, чтобы фонарик её не подсвечивал.</summary>
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

    [Header("Держатель")]
    [Tooltip("Перецепить модель в руки при старте, если у неё есть HeldItem " +
             "(нож, граната). Лечит случай, когда предмет лежит в мире " +
             "вместо рук игрока.")]
    public bool attachToHolderOnStart = true;

    [Tooltip("Класть модель оружия на слой HeldObjects, чтобы фонарик её не " +
             "подсвечивал и не считал препятствием для засветки.")]
    public bool putOnHeldLayer = true;

    [Header("Скрипты оружия")]
    [Tooltip("Скрипты, которые включаются вместе с моделью (Wep, HeldItem и т.п.). " +
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
        else
            weaponScripts = MergeMissingScripts(weaponScripts);
        if (attachToHolderOnStart) AttachHeldItems();
    }
    void Start()
    {
        if (putOnHeldLayer && equippedOnStart)
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("HeldObjects"));
    }
    MonoBehaviour[] CollectOwnScripts()
    {
        var found = new System.Collections.Generic.List<MonoBehaviour>();
        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb == this) continue;
            if (IsWeaponScript(mb)) found.Add(mb);
        }
        return found.ToArray();
    }
    // Добирает скрипты, добавленные позже ручной настройки (иначе HeldItem остался бы всегда включённым)
    MonoBehaviour[] MergeMissingScripts(MonoBehaviour[] existing)
    {
        var result = new System.Collections.Generic.List<MonoBehaviour>(existing);

        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null || mb == this) continue;
            if (!IsWeaponScript(mb)) continue;
            if (result.Contains(mb)) continue;

            result.Add(mb);
        }

        return result.ToArray();
    }

    static bool IsWeaponScript(MonoBehaviour mb) =>
        mb is Wep || mb is HeldItem;
    void AttachHeldItems()
    {
        foreach (HeldItem held in GetComponentsInChildren<HeldItem>(true))
            if (held != null) held.AttachToHolder();
    }
    /// <summary>Показать/спрятать оружие вместе со скриптами.</summary>
    public void SetEquipped(bool equipped, bool playSound = false)
    {
        isEquipped = equipped;
        // Скрипты гасим до скрытия объекта, иначе Wep досчитает отдачу в невидимом кадре
        if (weaponScripts != null)
        {
            foreach (MonoBehaviour mb in weaponScripts)
                if (mb != null) mb.enabled = equipped;
        }
        if (equipped && putOnHeldLayer)
            SetLayerRecursively(gameObject, LayerMask.NameToLayer("HeldObjects"));
        gameObject.SetActive(equipped);
        if (equipped && playSound && equipSound != null)
            AudioSource.PlayClipAtPoint(equipSound, transform.position, 0.8f);
    }
    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (layer < 0) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (string.IsNullOrEmpty(displayName)) displayName = name;
    }
#endif
}
