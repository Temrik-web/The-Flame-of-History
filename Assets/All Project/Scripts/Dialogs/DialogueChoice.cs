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

    [Header("Требования без ассетов (пусто = не проверять)")]
    [Tooltip("Id предмета из ItemDatabase (например key_cellar). Кнопка видна, только если предмет есть.")]
    public string requiredItemId;
    [Tooltip("Сколько штук предмета нужно.")]
    public int requiredItemCount = 1;
    [Tooltip("Id квеста (например q_cart_key). Кнопка видна, только если статус совпал.")]
    public string requiredQuestId;
    [Tooltip("0 — любой взятый (активен или выполнен), 1 — активен, 2 — выполнен, 3 — провален.")]
    public int requiredQuestState = 1;

    [Header("Переход")]
    public string nextNodeID;
    public bool endDialogue = false;

    [Header("Команды при выборе")]
    public List<DialogueCommand> onSelectCommands = new List<DialogueCommand>();

    [Header("Событие Unity")]
    public UnityEvent onSelected;
}