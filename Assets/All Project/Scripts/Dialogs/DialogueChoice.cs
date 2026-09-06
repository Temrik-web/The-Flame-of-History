using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class DialogueChoice
{
    [Header("Текст кнопки")]
    public string choiceText;

    [Header("Условие показа (может быть пустым)")]
    public DialogueCondition condition; // ссылка на ассет условия

    [Header("Переход")]
    public string nextNodeID;
    public bool endDialogue = false;

    [Header("Команды при выборе")]
    public List<DialogueCommand> onSelectCommands = new List<DialogueCommand>();

    [Header("Событие Unity")]
    public UnityEvent onSelected;
}