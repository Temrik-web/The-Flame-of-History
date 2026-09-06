using UnityEngine;

[System.Serializable]
public class DialogueCommand
{
    public enum CommandType
    {
        SetFlag,
        SetInt,
        SetString,
        GiveItem,
        RemoveItem,
        ChangeBackground,
        PlayBGM,
        StopBGM,
        PlaySFX,
        Teleport,
        CustomEvent
    }

    public CommandType type;
    public string stringParam;
    public bool boolParam;
    public int intParam;
    public float floatParam;
    public GameObject gameObjectParam;
    public AudioClip clipParam;
    public Sprite spriteParam;

    public void Execute()
    {
        switch (type)
        {
            case CommandType.SetFlag:
                GameState.SetFlag(stringParam, boolParam);
                break;
            case CommandType.SetInt:
                GameState.SetInt(stringParam, intParam);
                break;
            case CommandType.SetString:
                GameState.SetString(stringParam, boolParam ? "true" : "false");
                break;
            case CommandType.GiveItem:
                Debug.Log($"Giving item: {stringParam}");
                break;
            case CommandType.RemoveItem:
                Debug.Log($"Removing item: {stringParam}");
                break;
            case CommandType.ChangeBackground:
                if (DialogueManager.Instance != null)
                    DialogueManager.Instance.SetBackground(spriteParam);
                break;
            case CommandType.PlayBGM:
                // Реализуйте свою логику музыки
                break;
            case CommandType.StopBGM:
                break;
            case CommandType.PlaySFX:
                if (clipParam != null)
                    AudioSource.PlayClipAtPoint(clipParam, Camera.main.transform.position);
                break;
            case CommandType.Teleport:
                break;
            case CommandType.CustomEvent:
                break;
        }
    }
}