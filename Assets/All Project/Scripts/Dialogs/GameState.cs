using System.Collections.Generic;
using UnityEngine;

public static class GameState
{
    private static Dictionary<string, bool> flags = new Dictionary<string, bool>();
    private static Dictionary<string, int> intVariables = new Dictionary<string, int>();
    private static Dictionary<string, string> stringVariables = new Dictionary<string, string>();

    // Ключи, которых касались в этой сессии — только их пишем/читаем в PlayerPrefs.
    private static readonly HashSet<string> touchedFlags = new HashSet<string>();
    private static readonly HashSet<string> touchedInts = new HashSet<string>();
    private static readonly HashSet<string> touchedStrings = new HashSet<string>();

    private const string Prefix = "flame_gs_";

    public static void SetFlag(string flag, bool value)
    {
        if (string.IsNullOrEmpty(flag)) return;
        flags[flag] = value;
        touchedFlags.Add(flag);
        Save();
    }
    public static bool GetFlag(string flag, bool defaultValue = false) => flags.ContainsKey(flag) ? flags[flag] : defaultValue;

    public static void SetInt(string key, int value)
    {
        if (string.IsNullOrEmpty(key)) return;
        intVariables[key] = value;
        touchedInts.Add(key);
        Save();
    }
    public static int GetInt(string key, int defaultValue = 0) => intVariables.ContainsKey(key) ? intVariables[key] : defaultValue;

    /// <summary>Прибавить к счётчику (удобно для доверия/репутации).</summary>
    public static void AddInt(string key, int delta) => SetInt(key, GetInt(key) + delta);

    public static void SetString(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        stringVariables[key] = value ?? "";
        touchedStrings.Add(key);
        Save();
    }
    public static string GetString(string key, string defaultValue = "") => stringVariables.ContainsKey(key) ? stringVariables[key] : defaultValue;

    public static void ResetAll(bool deleteSave = true)
    {
        flags.Clear();
        intVariables.Clear();
        stringVariables.Clear();
        touchedFlags.Clear();
        touchedInts.Clear();
        touchedStrings.Clear();
        if (deleteSave)
        {
            PlayerPrefs.DeleteKey(Prefix + "flags");
            PlayerPrefs.DeleteKey(Prefix + "ints");
            PlayerPrefs.DeleteKey(Prefix + "strings");
            PlayerPrefs.Save();
        }
    }

    /// <summary>Загрузить сохранение (вызывать при старте сцены до диалогов).</summary>
    public static void Load()
    {
        flags.Clear();
        intVariables.Clear();
        stringVariables.Clear();

        foreach (string e in Split(PlayerPrefs.GetString(Prefix + "flags", "")))
        {
            int sep = e.IndexOf('=');
            if (sep <= 0) continue;
            string k = e.Substring(0, sep);
            flags[k] = e.Substring(sep + 1) == "1";
            touchedFlags.Add(k);
        }
        foreach (string e in Split(PlayerPrefs.GetString(Prefix + "ints", "")))
        {
            int sep = e.IndexOf('=');
            if (sep <= 0) continue;
            string k = e.Substring(0, sep);
            int v;
            if (!int.TryParse(e.Substring(sep + 1), out v)) continue;
            intVariables[k] = v;
            touchedInts.Add(k);
        }
        foreach (string e in Split(PlayerPrefs.GetString(Prefix + "strings", "")))
        {
            int sep = e.IndexOf('=');
            if (sep <= 0) continue;
            string k = e.Substring(0, sep);
            stringVariables[k] = Unescape(e.Substring(sep + 1));
            touchedStrings.Add(k);
        }
    }

    /// <summary>Сохранить всё затронутое в PlayerPrefs.</summary>
    public static void Save()
    {
        var fb = new System.Text.StringBuilder();
        foreach (string k in touchedFlags)
        {
            bool v;
            if (!flags.TryGetValue(k, out v)) continue;
            if (fb.Length > 0) fb.Append(';');
            fb.Append(k).Append('=').Append(v ? "1" : "0");
        }
        var ib = new System.Text.StringBuilder();
        foreach (string k in touchedInts)
        {
            int v;
            if (!intVariables.TryGetValue(k, out v)) continue;
            if (ib.Length > 0) ib.Append(';');
            ib.Append(k).Append('=').Append(v);
        }
        var sb = new System.Text.StringBuilder();
        foreach (string k in touchedStrings)
        {
            string v;
            if (!stringVariables.TryGetValue(k, out v)) continue;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(k).Append('=').Append(Escape(v));
        }
        PlayerPrefs.SetString(Prefix + "flags", fb.ToString());
        PlayerPrefs.SetString(Prefix + "ints", ib.ToString());
        PlayerPrefs.SetString(Prefix + "strings", sb.ToString());
        PlayerPrefs.Save();
    }

    static string[] Split(string s)
    {
        if (string.IsNullOrEmpty(s)) return new string[0];
        return s.Split(';');
    }

    // Строки храним с экранированием ; = \ — ключи считаем простыми идентификаторами.
    static string Escape(string s) => (s ?? "").Replace("\\", "\\\\").Replace(";", "\\s").Replace("=", "\\e");
    static string Unescape(string s) => (s ?? "").Replace("\\e", "=").Replace("\\s", ";").Replace("\\\\", "\\");
}