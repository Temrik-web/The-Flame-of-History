using UnityEngine;

/// <summary>
/// Чекпоинт-сохранения вместо "сейва на каждый чих".
/// Логика:
/// - При старте отключает дисковую запись у промежуточных сейвов
///   (GameState / QuestSystem / DialogueManager / DialogueHistory пишут
///   только в память PlayerPrefs через Set*, без PlayerPrefs.Save()).
/// - Пишет на диск только в двух случаях:
///   1) Checkpoint() — событие (по умолчанию: вход в узел N_give диалога D1 — отдача ключа Степану).
///   2) OnApplicationQuit — штатные сейвы при выходе (оставлены по требованию).
/// Вешать на любой объект в игровой сцене (например рядом с DialogueManager).
/// </summary>
[DisallowMultipleComponent]
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Чекпоинт: отдача ключа Степану")]
    [Tooltip("Имя ассета диалога (m_Name). По умолчанию D1_Znakomstvo.")]
    public string checkpointDialogueName = "D1_Znakomstvo";
    [Tooltip("Узел отдачи ключа в D1.")]
    public string checkpointNodeID = "N_give";
    [Tooltip("Id чекпоинта для логов и ключа flame_checkpoint_<id>.")]
    public string checkpointId = "key_given_to_stepan";
    [Tooltip("Дополнительно слушать шину CustomEvent (DialogueEventBus). Пусто — не слушать.")]
    public string checkpointEventId = "key_given_to_stepan";

    [Header("Политика")]
    [Tooltip("Включено — при старте отключить промежуточный PlayerPrefs.Save() (Set остаётся, диск пишется только на чекпоинте/выходе).")]
    public bool disableAutoDiskWriteOnAwake = true;
    [Tooltip("Показать 'Игра сохранена' над игроком (FloatingText).")]
    public bool showSavedToast = true;
    public bool verboseLog = true;

    /// <summary>Глобальный флаг: разрешена ли промежуточная запись на диск. False = только буфер.</summary>
    public static bool EnableAutoDiskWrite = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (disableAutoDiskWriteOnAwake)
            SetAutoDiskWrite(false);
    }

    void OnEnable()
    {
        TrySubscribe();
        DialogueEventBus.OnEvent += OnBusEvent;
    }

    void OnDisable()
    {
        DialogueEventBus.OnEvent -= OnBusEvent;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnNodeChanged -= OnNodeChanged;
    }

    void Start()
    {
        // DialogueManager мог проснуться позже — подписываемся ещё раз.
        TrySubscribe();
    }

    void TrySubscribe()
    {
        DialogueManager m = DialogueManager.Instance;
        if (m == null)
            m = FindObjectOfType<DialogueManager>();
        if (m != null)
        {
            m.OnNodeChanged -= OnNodeChanged;
            m.OnNodeChanged += OnNodeChanged;
        }
    }

    /// <summary>Включить/выключить промежуточную запись на диск у всех систем.</summary>
    public static void SetAutoDiskWrite(bool enabled)
    {
        EnableAutoDiskWrite = enabled;
        GameState.EnableDiskWrite = enabled;
        QuestSystem.EnableDiskWrite = enabled;
        DialogueManager.EnableDiskWrite = enabled;
        DialogueHistory.EnableDiskWrite = enabled;
    }

    void OnBusEvent(string eventId)
    {
        if (string.IsNullOrEmpty(checkpointEventId) || string.IsNullOrEmpty(eventId)) return;
        if (eventId == checkpointEventId)
            Checkpoint(checkpointId);
    }

    void OnNodeChanged(DialogueNode node)
    {
        if (node == null) return;
        if (node.nodeID != checkpointNodeID) return;

        DialogueManager m = DialogueManager.Instance;
        if (m == null) return;
        DialogueData dlg = m.CurrentDialogue;
        if (dlg == null) return;

        // DialogueKey: name (имя ассета) приоритетнее dialogueName.
        string key = !string.IsNullOrEmpty(dlg.name) ? dlg.name : dlg.dialogueName;
        if (key != checkpointDialogueName) return;

        Checkpoint(checkpointId);
    }

    /// <summary>Сохранить всё сразу и сбросить на диск. Вызывается на событии + можно дёрнуть из кода/кнопки.</summary>
    public void Checkpoint(string id)
    {
        if (string.IsNullOrEmpty(id)) id = checkpointId;

        // Порядок важен: сначала буферизуем Set* (GameState/Quest без диска),
        // затем Inventory.Save() + финальный PlayerPrefs.Save() сбрасывают всё на диск.
        try { GameState.Save(); }
        catch (System.Exception e) { Debug.LogException(e); }
        try { QuestSystem.Save(); }
        catch (System.Exception e) { Debug.LogException(e); }

        int slots = -1;
        if (InventorySystem.Instance != null)
        {
            try
            {
                InventorySystem.Instance.Save();
                slots = InventorySystem.Instance.SlotCount;
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }
        else
        {
            Debug.LogWarning("[Save] Checkpoint: InventorySystem.Instance не найден — инвентарь не сохранён.", this);
        }

        try
        {
            PlayerPrefs.SetString("flame_checkpoint_last", id);
            PlayerPrefs.SetInt("flame_checkpoint_" + id, 1);
        }
        catch (System.Exception e) { Debug.LogException(e); }

        // Форсированный сброс на диск — игнорирует EnableDiskWrite (это и есть чекпоинт).
        PlayerPrefs.Save();

        if (verboseLog)
            Debug.Log($"[Save] Checkpoint '{id}': инвентарь слотов={slots}, квесты/флаги/диалоги сброшены на диск.", this);

        if (showSavedToast)
        {
            try
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                Vector3 pos = player != null
                    ? player.transform.position + Vector3.up * 2f
                    : Vector3.zero;
                FloatingText.Show("Игра сохранена", pos, Color.green);
            }
            catch { /* тост не должен ронять сейв */ }
        }
    }

    /// <summary>Статический доступ для CustomEvent/кнопок: SaveManager.CheckpointNow("key_given_to_stepan").</summary>
    public static void CheckpointNow(string id)
    {
        if (Instance != null) Instance.Checkpoint(id);
        else
        {
            // Менеджера нет в сцене — делаем минимальный сейв напрямую (всё + диск).
            try { GameState.Save(); } catch { }
            try { QuestSystem.Save(); } catch { }
            try
            {
                var inv = InventorySystem.Instance;
                if (inv == null) inv = FindObjectOfType<InventorySystem>();
                if (inv != null) inv.Save();
            }
            catch { }
            PlayerPrefs.Save();
            Debug.Log($"[Save] Checkpoint '{id}' (без инстанса менеджера).");
        }
    }

    [ContextMenu("Сохранить чекпоинт сейчас")]
    void ContextCheckpoint() => Checkpoint(checkpointId);

    [ContextMenu("Вернуть промежуточные сейвы на диск (дебаг)")]
    void ContextEnableAuto() => SetAutoDiskWrite(true);
}
