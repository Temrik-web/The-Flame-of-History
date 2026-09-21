using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Dialogue", menuName = "Dialogue System/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    public string dialogueName;
    [TextArea(3, 10)]
    public string description;
    public List<DialogueNode> nodes = new List<DialogueNode>();

    public DialogueNode GetStartNode()
    {
        if (nodes.Count > 0) return nodes[0];
        return null;
    }

    public DialogueNode GetNodeByID(string id)
    {
        foreach (var node in nodes)
            if (node != null && node.nodeID == id)
                return node;
        return null;
    }

    /// <summary>
    /// Проверка связности в редакторе: пустым узлам выдаём ID, дубликаты ID
    /// и висячие ссылки (nextNodeID в никуда) подсвечиваем ошибками.
    /// Именно висячие ссылки раньше молча обрывали диалог («фраза NPC не видна»).
    /// </summary>
    void OnValidate()
    {
        if (nodes == null) return;

        var seen = new System.Collections.Generic.HashSet<string>();
        foreach (var node in nodes)
        {
            if (node == null) continue;
            if (string.IsNullOrEmpty(node.nodeID))
            {
                node.nodeID = "node_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
                Debug.LogWarning($"[DialogueData] «{dialogueName}»: узлу без ID выдан {node.nodeID}.", this);
            }
            else if (!seen.Add(node.nodeID))
            {
                Debug.LogError($"[DialogueData] «{dialogueName}»: дубликат nodeID «{node.nodeID}» — " +
                               "переходы по нему всегда попадут в первый узел.", this);
            }
        }

        foreach (var node in nodes)
        {
            if (node == null) continue;
            if (!string.IsNullOrEmpty(node.nextNodeID) && !seen.Contains(node.nextNodeID))
            {
                Debug.LogError($"[DialogueData] «{dialogueName}» узел «{node.nodeID}»: nextNodeID " +
                               $"«{node.nextNodeID}» не найден — ветка оборвётся.", this);
            }
            if (node.choices != null)
            {
                foreach (var c in node.choices)
                {
                    if (c == null) continue;
                    if (string.IsNullOrEmpty(c.choiceText))
                    {
                        Debug.LogWarning($"[DialogueData] «{dialogueName}» узел «{node.nodeID}»: " +
                                           "выбор с пустым текстом — кнопка будет пустой.", this);
                    }
                    if (!c.endDialogue && !string.IsNullOrEmpty(c.nextNodeID) && !seen.Contains(c.nextNodeID))
                    {
                        Debug.LogError($"[DialogueData] «{dialogueName}» узел «{node.nodeID}» " +
                                       $"выбор «{c.choiceText}»: nextNodeID «{c.nextNodeID}» не найден.", this);
                    }
                }
            }
            if (string.IsNullOrEmpty(node.dialogueText) &&
                (node.choices == null || node.choices.Count == 0) &&
                string.IsNullOrEmpty(node.nextNodeID) &&
                !string.Equals(node.nodeID, "N_end", System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[DialogueData] «{dialogueName}» узел «{node.nodeID}»: пустой тупик " +
                                   "(нет текста, выборов и перехода) — диалог закончится молча.", this);
            }
        }
    }
}
