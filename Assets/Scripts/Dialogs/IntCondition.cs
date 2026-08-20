using UnityEngine;

[CreateAssetMenu(fileName = "IntCondition", menuName = "Dialogue System/Conditions/Int Condition")]
public class IntCondition : DialogueCondition
{
    public string variableName;
    public int requiredValue;
    public enum Comparison { Equal, Greater, Less, GreaterOrEqual, LessOrEqual }
    public Comparison comparison = Comparison.Equal;

    public override bool Evaluate()
    {
        int current = GameState.GetInt(variableName, 0);
        switch (comparison)
        {
            case Comparison.Equal: return current == requiredValue;
            case Comparison.Greater: return current > requiredValue;
            case Comparison.Less: return current < requiredValue;
            case Comparison.GreaterOrEqual: return current >= requiredValue;
            case Comparison.LessOrEqual: return current <= requiredValue;
        }
        return false;
    }
}