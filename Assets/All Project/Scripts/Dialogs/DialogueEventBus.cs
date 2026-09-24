using System;
using UnityEngine;

/// <summary>Шина событий диалогов: CustomEvent из узла/выбора -> реакция мира.</summary>
public static class DialogueEventBus
{
    /// <summary>Пришло событие из диалога (stringParam команды).</summary>
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
