using System;
using UnityEngine;

/// <summary>
/// Шина событий диалогов: «отросток» механики под будущий спавн немцев и прочие
/// реакции мира на разговоры.
///
/// Как пользоваться:
///  - в DialogueNode.onEnterCommands или DialogueChoice.onSelectCommands добавь
///    команду CustomEvent (type = 10), stringParam = id события (например "raid_germans");
///  - любой скрипт сцены подписывается на DialogueEventBus.OnEvent и реагирует;
///  - GermanRaidDirector уже слушает "raid_germans" и поднимает налёт;
///  - DialogueRaidHook считает завершённые разговоры и сам кидает событие
///    после N-й беседы («поговорил 2 диалогом — идёт событие»).
/// </summary>
public static class DialogueEventBus
{
    /// <summary>Пришло событие из диалога. Параметр — stringParam команды (id события).</summary>
    public static event Action<string> OnEvent;

    /// <summary>Поднять событие. Пустые id игнорируются.</summary>
    public static void Raise(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return;
        Debug.Log($"[DialogueEvent] {eventId}");
        try
        {
            OnEvent?.Invoke(eventId);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}
