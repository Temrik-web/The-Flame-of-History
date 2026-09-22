using UnityEngine;

[System.Serializable]
public class DialogueCommand
{
    public enum CommandType
    {
        SetFlag,
        SetInt,
        SetString,
        GiveItem,
        RemoveItem,
        ChangeBackground,
        PlayBGM,
        StopBGM,
        PlaySFX,
        Teleport,
        CustomEvent,
        StartQuest,
        CompleteQuest,
        FailQuest,
        AddInt,
    }

    public CommandType type;
    [Tooltip("Имя флага/переменной, id предмета, квеста и т. п.")]
    public string stringParam;
    [Tooltip("Строковое значение для команды SetString.")]
    public string stringValueParam;
    public bool boolParam;
    public int intParam;
    public float floatParam;
    public GameObject gameObjectParam;
    public AudioClip clipParam;
    public Sprite spriteParam;

    public void Execute()
    {
        switch (type)
        {
            case CommandType.SetFlag:
                GameState.SetFlag(stringParam, boolParam);
                break;
            case CommandType.SetInt:
                GameState.SetInt(stringParam, intParam);
                break;
            case CommandType.SetString:
                GameState.SetString(stringParam, stringValueParam);
                break;
            case CommandType.GiveItem:
                GiveItemToPlayer(stringParam, Mathf.Max(1, intParam));
                break;
            case CommandType.RemoveItem:
                RemoveItemFromPlayer(stringParam, Mathf.Max(1, intParam));
                break;
            case CommandType.ChangeBackground:
                if (DialogueManager.Instance != null)
                    DialogueManager.Instance.SetBackground(spriteParam);
                break;
            case CommandType.PlayBGM:
                // Реализуйте свою логику музыки
                break;
            case CommandType.StopBGM:
                break;
            case CommandType.PlaySFX:
                if (clipParam != null)
                {
                    AudioListener listener = Object.FindObjectOfType<AudioListener>();
                    Vector3 position = listener != null ? listener.transform.position : Vector3.zero;
                    AudioSource.PlayClipAtPoint(clipParam, position);
                }
                break;
            case CommandType.Teleport:
                TeleportPlayer(gameObjectParam);
                break;
            case CommandType.CustomEvent:
                // Главный «отросток» механики: узел/выбор кидает строковое событие
                // в DialogueEventBus (например "raid_germans"), а мир реагирует:
                // GermanRaidDirector спавнит немцев, квесты стартуют и т.д.
                DialogueEventBus.Raise(stringParam);
                break;
            case CommandType.StartQuest:
                QuestSystem.StartQuest(stringParam);
                break;
            case CommandType.CompleteQuest:
                QuestSystem.CompleteQuest(stringParam);
                break;
            case CommandType.FailQuest:
                QuestSystem.FailQuest(stringParam);
                break;
            case CommandType.AddInt:
                GameState.AddInt(stringParam, intParam);
                break;
        }
    }

    /// <summary>
    /// Выдать предмет в инвентарь по строковому id (stringParam = itemId, intParam = количество).
    /// Работает через ItemDatabase + InventorySystem. Без них — только лог.
    /// </summary>
    public static void GiveItemToPlayer(string itemId, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            Debug.LogWarning("[DialogueCommand] GiveItem: пустой itemId.");
            return;
        }
        var db = ItemDatabase.Instance;
        if (db == null)
        {
            Debug.LogWarning($"[DialogueCommand] GiveItem {itemId}: ItemDatabase не найден.");
            return;
        }
        ItemData item = db.GetById(itemId);
        if (item == null)
        {
            Debug.LogWarning($"[DialogueCommand] GiveItem: предмет '{itemId}' не найден в базе.");
            return;
        }
        var inv = InventorySystem.Instance;
        if (inv == null) inv = Object.FindObjectOfType<InventorySystem>();
        if (inv == null)
        {
            Debug.LogWarning($"[DialogueCommand] GiveItem {itemId}: игрок без InventorySystem.");
            return;
        }
        int added = inv.AddItem(item, Mathf.Max(1, amount));
        Debug.Log($"[DialogueCommand] Выдано: {item.itemName} x{added} (просили {amount}).");
        if (added <= 0)
            Debug.LogWarning($"[DialogueCommand] GiveItem {itemId}: инвентарь полон.");
    }

    public static void RemoveItemFromPlayer(string itemId, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        var db = ItemDatabase.Instance;
        if (db == null) return;
        ItemData item = db.GetById(itemId);
        if (item == null)
        {
            Debug.LogWarning($"[DialogueCommand] RemoveItem: предмет '{itemId}' не найден в базе.");
            return;
        }
        var inv = InventorySystem.Instance;
        if (inv == null) inv = Object.FindObjectOfType<InventorySystem>();
        if (inv == null) return;
        inv.RemoveItem(item, Mathf.Max(1, amount));
        Debug.Log($"[DialogueCommand] Забрано: {item.itemName} x{amount}.");
    }

    /// <summary>Есть ли у игрока N штук предмета (для условий в выборах).</summary>
    public static bool PlayerHasItem(string itemId, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemId)) return true;
        var inv = InventorySystem.Instance;
        if (inv == null) inv = Object.FindObjectOfType<InventorySystem>();
        if (inv == null) return false;
        return inv.CountItemById(itemId) >= Mathf.Max(1, amount);
    }

    /// <summary>
    /// Телепортировать игрока к точке (gameObjectParam). Пусто — ничего не делать.
    /// Удобно для «отростков» после диалога: поговорил — очнулся у сарая и т.п.
    /// </summary>
    public static void TeleportPlayer(GameObject destination)
    {
        if (destination == null) return;
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[DialogueCommand] Teleport: игрок с тегом Player не найден.");
            return;
        }
        player.transform.SetPositionAndRotation(
            destination.transform.position, destination.transform.rotation);
        Debug.Log($"[DialogueCommand] Телепорт игрока к {destination.name}.");
    }
}
