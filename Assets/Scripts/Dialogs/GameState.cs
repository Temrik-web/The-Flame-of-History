using System.Collections.Generic;

public static class GameState
{
    private static Dictionary<string, bool> flags = new Dictionary<string, bool>();
    private static Dictionary<string, int> intVariables = new Dictionary<string, int>();
    private static Dictionary<string, string> stringVariables = new Dictionary<string, string>();

    public static void SetFlag(string flag, bool value) => flags[flag] = value;
    public static bool GetFlag(string flag, bool defaultValue = false) => flags.ContainsKey(flag) ? flags[flag] : defaultValue;

    public static void SetInt(string key, int value) => intVariables[key] = value;
    public static int GetInt(string key, int defaultValue = 0) => intVariables.ContainsKey(key) ? intVariables[key] : defaultValue;

    public static void SetString(string key, string value) => stringVariables[key] = value;
    public static string GetString(string key, string defaultValue = "") => stringVariables.ContainsKey(key) ? stringVariables[key] : defaultValue;

    public static void ResetAll()
    {
        flags.Clear();
        intVariables.Clear();
        stringVariables.Clear();
    }
}