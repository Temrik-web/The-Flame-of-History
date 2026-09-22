using System.Collections;
using UnityEngine;

/// <summary>
/// Режиссёр налётов: слушает DialogueEventBus и поднимает немцев.
///
/// Сценарий «иногда могут приходить немцы»:
///  1. Игрок второй раз говорит с дозорным (DialogueRaidHook считает беседы
///     и кидает "raid_germans") — или узел диалога сам кидает CustomEvent
///     с тем же id через DialogueCommand;
///  2. GermanRaidDirector ждёт warningDelay, показывает предупреждение
///     («Немцы у околицы!») и спавнит префабы из germanPrefabs в точках
///     spawnPoints (по кругу, если точек меньше, чем немцев);
///  3. После спавна стартует квест questOnRaid (по умолчанию q_raid)
///     и взводит флаг GameState raid_active — диалоги могут ветвиться
///     по нему («Тихо... Слышишь?»).
///
/// Если префабы/точки не заданы — налёт всё равно «происходит» логически
/// (флаг + квест + предупреждение в консоль), так что диалоги и квесты
/// можно отлаживать до того, как настроен спавн. Позже просто перетащи
/// префаб немца (с EnemyAI) и точки — код трогать не нужно.
/// </summary>
[DisallowMultipleComponent]
public class GermanRaidDirector : MonoBehaviour
{
    [Header("Что слушаем")]
    [Tooltip("Id события из диалогов. Совпадает с DialogueRaidHook.eventId и CustomEvent.stringParam.")]
    public string raidEventId = "raid_germans";

    [Header("Кого поднимаем")]
    [Tooltip("Префабы немцев (с EnemyAI/CharacterHealth). Пусто — только логика без спавна.")]
    public GameObject[] germanPrefabs = new GameObject[0];

    [Tooltip("Точки появления (пустые GameObject у околицы/леса). Пусто — спавн рядом с игроком.")]
    public Transform[] spawnPoints = new Transform[0];

    [Tooltip("Сколько немцев поднять за раз.")]
    [Min(1)] public int germansPerRaid = 2;

    [Header("Тайминг")]
    [Tooltip("Пауза между событием из диалога и появлением (дать игроку дойти до укрытия).")]
    [Min(0f)] public float warningDelay = 6f;

    [Tooltip("Текст предупреждения (показывается через FloatingText над игроком, если он есть).")]
    public string warningText = "Тревога! Немцы у околицы!";

    [Header("Мир и квест")]
    [Tooltip("Квест, стартующий с налётом.")]
    public string questOnRaid = "q_raid";

    [Tooltip("Флаг GameState, взводимый на время налёта (ветвление диалогов).")]
    public string activeFlag = "raid_active";

    [Tooltip("Только один налёт за жизнь сцены (повторные события игнорируются).")]
    public bool oncePerScene = false;

    [Tooltip("Считать убитых: закрывать квест, когда все заспавненные мертвы. Выкл — квест висит до ручного закрытия.")]
    public bool completeQuestWhenCleared = true;

    bool raidFired = false;
    readonly System.Collections.Generic.List<GameObject> alive = new System.Collections.Generic.List<GameObject>();

    void OnEnable() => DialogueEventBus.OnEvent += OnDialogueEvent;
    void OnDisable() => DialogueEventBus.OnEvent -= OnDialogueEvent;

    void OnDialogueEvent(string eventId)
    {
        if (eventId != raidEventId) return;
        if (oncePerScene && raidFired) return;
        raidFired = true;
        StartCoroutine(RaidRoutine());
    }

    IEnumerator RaidRoutine()
    {
        GameState.SetFlag(activeFlag, true);
        if (!string.IsNullOrEmpty(questOnRaid))
            QuestSystem.StartQuest(questOnRaid);

        Warn(warningText);
        Debug.Log($"[Raid] Сигнал «{raidEventId}»: немцы будут через {warningDelay} c.", this);

        if (warningDelay > 0f)
            yield return new WaitForSeconds(warningDelay);

        SpawnWave(germansPerRaid);

        if (completeQuestWhenCleared && alive.Count > 0)
            yield return StartCoroutine(WaitCleared());

        GameState.SetFlag(activeFlag, false);
    }

    void Warn(string text)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            // FloatingText есть в проекте (мирные подписи «+ предмет») — переиспользуем.
            try { FloatingText.Show(text, player.transform.position + Vector3.up * 2f, Color.red); }
            catch { Debug.Log($"[Raid] {text}", this); }
        }
        Debug.Log($"[Raid] {text}", this);
    }

    void SpawnWave(int count)
    {
        if (germanPrefabs == null || germanPrefabs.Length == 0)
        {
            Debug.LogWarning("[Raid] Префабы немцев не заданы — спавна нет, но флаг и квест уже подняты. " +
                             "Перетащи префаб врага в GermanRaidDirector.germanPrefabs.", this);
            // Без тел ждать некого — сразу гасим флаг и закрываем квест как «ложную тревогу».
            GameState.SetFlag(activeFlag, false);
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        Vector3 fallback = player != null ? player.transform.position + player.transform.forward * 18f : transform.position;

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = germanPrefabs[i % germanPrefabs.Length];
            if (prefab == null) continue;

            Vector3 pos;
            Quaternion rot = Quaternion.identity;
            if (spawnPoints != null && spawnPoints.Length > 0 && spawnPoints[i % spawnPoints.Length] != null)
            {
                pos = spawnPoints[i % spawnPoints.Length].position;
                rot = spawnPoints[i % spawnPoints.Length].rotation;
            }
            else
            {
                pos = fallback + new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-6f, 6f));
            }

            GameObject g = Instantiate(prefab, pos, rot);
            alive.Add(g);
            Debug.Log($"[Raid] Немец {i + 1}/{count}: {g.name} в {pos}.", g);
        }
    }

    IEnumerator WaitCleared()
    {
        while (true)
        {
            alive.RemoveAll(g => g == null);
            if (alive.Count == 0) break;
            yield return new WaitForSeconds(1f);
        }
        Debug.Log("[Raid] Налёт отбит — все немцы мертвы.", this);
        if (!string.IsNullOrEmpty(questOnRaid))
            QuestSystem.CompleteQuest(questOnRaid);
    }

    /// <summary>Ручной запуск из инспектора (тест без диалогов).</summary>
    [ContextMenu("Тест: поднять налёт сейчас")]
    public void TestRaidNow() => OnDialogueEvent(raidEventId);
}
