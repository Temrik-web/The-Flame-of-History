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
            if (node.nodeID == id)
                return node;
        return null;
    }
}