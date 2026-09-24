using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Лёгкая система заданий без ассетов: id квеста -> статус + счётчик.
/// Определения (название/описание) лежат в коде, состояние — в PlayerPrefs.
/// Связка с остальным:
///  - DialogueCommand: StartQuest / CompleteQuest / FailQuest (stringParam = id квеста);
///  - DialogueChoice: requiredQuestId + requiredQuestState (ветвление по квестам);
///  - DoorController: questToCompleteOnOpen;
///  - InventorySystem.AddItem -> NotifyItemAdded (авто-закрытие q_cart_key).
///
/// Квесты сюжета (id зафиксированы, используются в диалогах D1/D2/D4/D5/D9/D10/D11):
///  q_cart_key    «Ключ от дома деда» — ПЕРВЫЙ квест: игрок отбился от отряда и попал
///                в деревню покойного деда (Михалыча). Степан отправляет к повозке —
///                найти ключ от дома деда. Ключ (Rusty key, key_cellar) виден на повозке
///                только пока квест активен (QuestKeyPickup); подбор ставит флаг
///                has_cellar_key, а закрывает квест только ПЕРЕДАЧА ключа Степану
///                («Ключ у меня» в D1 → N_give): там же стартует q_cellar_door.
///  q_forest_chest «Сундук в лесу» — проверить сундук у вишен, забрать письмо.
///  q_ammo        «Мне бы патронов» — Степан один раз делится диском к ППШ;
///  q_stepan_aid  «Чем перевязаться» — Степан делится бинтами (раз, за доверие);
///  q_raid_watch  «Дозор» — подменить Василия на шухере (второй разговор — налёт);
///  q_raid        «Налёт» — отбить немецкий патруль у околицы.
/// </summary>
public static class QuestSystem
{
    public const int StateNone = 0;
    public const int StateActive = 1;
    public const int StateDone = 2;
    public const int StateFailed = 3;

    [Serializable]
    public struct QuestDef
    {
        public string id;
        public string title;
        public string description;
        public QuestDef(string id, string title, string description)
        {
            this.id = id;
            this.title = title;
            this.description = description;
        }
    }

    public static readonly QuestDef[] Quests =
    {
        new QuestDef("q_cart_key", "Ключ от дома деда",
            "Алесь отбился от отряда и вышел к деревне покойного деда (Михалыча). " +
            "Степан сказал: ключ от дома деда лежит в сене на повозке. " +
            "Найти повозку и достать ключ."),
        new QuestDef("q_cellar_door", "Что в подвале",
            "Открыть подвал дома Михалыча найденным ключом."),
        new QuestDef("q_knife", "Нож Степана",
            "Степан присматривается к Алесю. Заслужить его доверие и получить нож."),
        new QuestDef("q_grenade", "Граната для Алеся",
            "Василий обещал отблагодарить за ключ. Вернуться к нему."),
        new QuestDef("q_forest_chest", "Сундук в лесу",
            "Василий просил проверить сундук у вишен в лесу — Степан мог оставить там письмо. " +
            "Найти сундук и забрать содержимое."),
        new QuestDef("q_ammo", "Мне бы патронов",
            "У Алеся ППШ с одним магазином. Попросить у Степана патроны — " +
            "он делится один раз, больше не даст."),
        new QuestDef("q_stepan_aid", "Чем перевязаться",
            "Степан намекнул, что поделится бинтами, если Алесь заслужит доверие. " +
            "Спросить у него перевязку."),
        new QuestDef("q_raid_watch", "Дозор",
            "Подменить Василия на шухере у околицы. Поговорить с ним ещё раз — " +
            "может, что-то случится..."),
        new QuestDef("q_raid", "Налёт",
            "Немцы идут к деревне! Отбить патруль у околицы и вернуться к своим."),
    };

    private static readonly Dictionary<string, int> states = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> counters = new Dictionary<string, int>();
    private static readonly HashSet<string> touched = new HashSet<string>();

    private const string Prefix = "flame_q_";

    /// <summary>Новое задание / журнал. Подпишись, чтобы показывать тосты в UI.</summary>
    public static event Action<string> OnQuestStarted;
    public static event Action<string> OnQuestCompleted;
    public static event Action<string> OnQuestFailed;

    public static string GetTitle(string questId)
    {
        foreach (var q in Quests)
            if (q.id == questId) return q.title;
        return questId;
    }

    public static int GetState(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return StateNone;
        int s;
        return states.TryGetValue(questId, out s) ? s : StateNone;
    }

    public static bool IsActive(string questId) => GetState(questId) == StateActive;
    public static bool IsDone(string questId) => GetState(questId) == StateDone;

    public static int GetCounter(string questId)
    {
        int c;
        return counters.TryGetValue(questId, out c) ? c : 0;
    }

    public static void StartQuest(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return;
        if (GetState(questId) == StateActive) return;
        if (GetState(questId) == StateDone) return; // выполненное не переоткрываем
        states[questId] = StateActive;
        touched.Add(questId);
        Save();
        Debug.Log($"[Quest] Новое задание: {GetTitle(questId)} ({questId})");
        OnQuestStarted?.Invoke(questId);
    }

    public static void CompleteQuest(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return;
        if (GetState(questId) == StateDone) return;
        states[questId] = StateDone;
        touched.Add(questId);
        Save();
        Debug.Log($"[Quest] Задание выполнено: {GetTitle(questId)} ({questId})");
        OnQuestCompleted?.Invoke(questId);
    }

    public static void FailQuest(string questId)
    {
        if (string.IsNullOrEmpty(questId)) return;
        states[questId] = StateFailed;
        touched.Add(questId);
        Save();
        Debug.Log($"[Quest] Задание провалено: {GetTitle(questId)} ({questId})");
        OnQuestFailed?.Invoke(questId);
    }

    /// <summary>Прибавить счётчик (например, «осмотрено повозок»).</summary>
    public static void AddCounter(string questId, int delta = 1)
    {
        if (string.IsNullOrEmpty(questId)) return;
        counters[questId] = GetCounter(questId) + delta;
        touched.Add(questId);
        Save();
    }

    /// <summary>
    /// Вызывается из InventorySystem.AddItem. Реакции на подбор без правок сцены:
    /// ключ подвала взводит флаг has_cellar_key, письмо из лесного сундука
    /// закрывает q_forest_chest. ВАЖНО: подбор ключа НЕ закрывает q_cart_key —
    /// квест закрывается только передачей ключа Степану («Ключ у меня» в D1),
    /// там же стартует q_cellar_door.
    /// </summary>
    public static void NotifyItemAdded(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return;
        if (itemId == "key_cellar" && IsActive("q_cart_key"))
        {
            GameState.SetFlag("has_cellar_key", true);
        }
        if (itemId == "letter_old" && IsActive("q_forest_chest"))
        {
            CompleteQuest("q_forest_chest");
            GameState.SetFlag("found_chest_letter", true);
        }
    }

    public static void Load()
    {
        states.Clear();
        counters.Clear();
        foreach (string e in Split(PlayerPrefs.GetString(Prefix + "states", "")))
        {
            int sep = e.IndexOf('=');
            if (sep <= 0) continue;
            string k = e.Substring(0, sep);
            string[] parts = e.Substring(sep + 1).Split(':');
            int s;
            if (!int.TryParse(parts[0], out s)) continue;
            states[k] = s;
            if (parts.Length > 1)
            {
                int c;
                if (int.TryParse(parts[1], out c)) counters[k] = c;
            }
            touched.Add(k);
        }
    }

    public static void Save()
    {
        var sb = new System.Text.StringBuilder();
        foreach (string k in touched)
        {
            int s;
            if (!states.TryGetValue(k, out s)) continue;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(k).Append('=').Append(s).Append(':').Append(GetCounter(k));
        }
        PlayerPrefs.SetString(Prefix + "states", sb.ToString());
        PlayerPrefs.Save();
    }

    public static void ResetAll(bool deleteSave = true)
    {
        states.Clear();
        counters.Clear();
        touched.Clear();
        if (deleteSave)
        {
            PlayerPrefs.DeleteKey(Prefix + "states");
            PlayerPrefs.Save();
        }
    }

    static string[] Split(string s)
    {
        if (string.IsNullOrEmpty(s)) return new string[0];
        return s.Split(';');
    }
}
