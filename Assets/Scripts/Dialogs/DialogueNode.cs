using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class DialogueNode
{
    [Header("Идентификатор узла")]
    public string nodeID;
    public string nextNodeID;  // для линейных переходов, если нет ответов

    [Header("Персонаж и текст")]
    public string speakerName;
    public Sprite speakerPortrait;
    [TextArea(3, 10)]
    public string dialogueText;
    public AudioClip voiceClip;

    [Header("Скорость и паузы")]
    public float textSpeed = 0.05f;
    public float autoAdvanceDelay = 0f; // 0 = ждать ввода

    [Header("Команды при входе")]
    public List<DialogueCommand> onEnterCommands = new List<DialogueCommand>();

    [Header("Ответы (ветвление)")]
    public List<DialogueChoice> choices = new List<DialogueChoice>();

    [Header("События Unity")]
    public UnityEvent onNodeEnter;
    public UnityEvent onNodeExit;
}