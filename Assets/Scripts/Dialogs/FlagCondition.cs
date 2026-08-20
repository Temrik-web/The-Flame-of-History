using UnityEngine;

[CreateAssetMenu(fileName = "FlagCondition", menuName = "Dialogue System/Conditions/Flag Condition")]
public class FlagCondition : DialogueCondition
{
    public string flagName;
    public bool expectedValue = true;

    public override bool Evaluate()
    {
        return GameState.GetFlag(flagName, false) == expectedValue;
    }
}