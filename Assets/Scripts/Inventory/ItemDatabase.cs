using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр всех предметов игры. Нужен для сохранения/загрузки инвентаря:
/// в файл пишется строковый id, при загрузке по нему находится ассет ItemData.
///
/// Создать: Assets -> Create -> Inventory -> Item Database.
/// Положить ассет в папку Resources и назвать "ItemDatabase",
/// чтобы он подхватывался автоматически (Instance).
/// </summary>
[CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
public class ItemDatabase : ScriptableObject
{
    private const string ResourcePath = "ItemDatabase";

    [Tooltip("Все предметы игры. Кнопка ниже соберёт их автоматически (только в редакторе).")]
    public List<ItemData> items = new List<ItemData>();

    private Dictionary<string, ItemData> lookup;
    private static ItemDatabase instance;

    /// <summary>Единственный экземпляр из Resources/ItemDatabase.asset (может быть null).</summary>
    public static ItemDatabase Instance
    {
        get
        {
            if (instance == null)
                instance = Resources.Load<ItemDatabase>(ResourcePath);
            return instance;
        }
    }

    /// <summary>Найти предмет по id. Возвращает null, если не найден.</summary>
    public ItemData GetById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        if (lookup == null)
        {
            lookup = new Dictionary<string, ItemData>();
            foreach (var item in items)
            {
                if (item == null) continue;
                if (!lookup.ContainsKey(item.Id)) lookup.Add(item.Id, item);
                else Debug.LogWarning($"[ItemDatabase] Дубликат id '{item.Id}' у ассета {item.name}.");
            }
        }

        return lookup.TryGetValue(id, out ItemData found) ? found : null;
    }

    /// <summary>Сбросить кэш (например, после правки списка в редакторе).</summary>
    public void RebuildCache() => lookup = null;

#if UNITY_EDITOR
    [ContextMenu("Собрать все ItemData из проекта")]
    void CollectAllItems()
    {
        items.Clear();
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemData");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            ItemData item = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null) items.Add(item);
        }
        RebuildCache();
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[ItemDatabase] Найдено предметов: {items.Count}");
    }
#endif
}
