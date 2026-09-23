using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Мастер цепочки Степана. Меню: Tools -> Диалоги.
///  - «Проверить связки»: каждый ассет D1..D9 (пустые узлы? битые переходы?),
///    каждый триггер/цепочка в сцене (пустой диалог? дубли на одном кубе?).
///  - «Собрать Степана»: один куб + NpcDialogueSequence с D1..D8 по порядку,
///    старые DialogueTrigger на D1..D8 гасятся (D9/D10/D11 других НПС не трогаем).
///  - «Сбросить сохранения диалогов/квестов»: чистый тест с нуля.
/// </summary>
public static class NpcSequenceSetupWizard
{
    const string MenuRoot = "Tools/Диалоги/";

    // Только Степан: D1..D8. D10/D11 специально исключены шаблоном (^D1_...).
    static readonly Regex StepanPattern = new Regex(@"^D[1-8]_", RegexOptions.Compiled);

    [MenuItem(MenuRoot + "Проверить связки (диалоги + сцена)", false, 21)]
    public static void ValidateAll()
    {
        int errors = 0, warnings = 0;
        var log = new System.Text.StringBuilder();

        // 1) Ассеты диалогов.
        string[] guids = AssetDatabase.FindAssets("t:DialogueData");
        var byName = new Dictionary<string, DialogueData>();
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var d = AssetDatabase.LoadAssetAtPath<DialogueData>(path);
            if (d == null) continue;
            byName[d.name] = d;
            int n = d.nodes != null ? d.nodes.Count : 0;
            if (n == 0)
            {
                log.AppendLine($"[ОШИБКА] Ассет {d.name} ({path}): 0 узлов — в игре откроется и сразу закроется.");
                errors++;
            }
            else
            {
                var ids = new HashSet<string>();
                foreach (var node in d.nodes)
                    if (node != null && !string.IsNullOrEmpty(node.nodeID))
                        ids.Add(node.nodeID);
                foreach (var node in d.nodes)
                {
                    if (node == null) continue;
                    if (!string.IsNullOrEmpty(node.nextNodeID) && !ids.Contains(node.nextNodeID))
                    {
                        log.AppendLine($"[ОШИБКА] {d.name} узел {node.nodeID}: nextNodeID {node.nextNodeID} в никуда.");
                        errors++;
                    }
                    if (node.choices != null)
                        foreach (var c in node.choices)
                        {
                            if (c == null) continue;
                            if (!c.endDialogue && !string.IsNullOrEmpty(c.nextNodeID) && !ids.Contains(c.nextNodeID))
                            {
                                log.AppendLine($"[ОШИБКА] {d.name} узел {node.nodeID} выбор «{c.choiceText}»: nextNodeID {c.nextNodeID} в никуда.");
                                errors++;
                            }
                        }
                }
            }
        }
        log.AppendLine($"Ассетов DialogueData: {byName.Count}.");

        // 2) Триггеры и цепочки в сцене.
        var triggers = Object.FindObjectsOfType<DialogueTrigger>(true);
        var seqs = Object.FindObjectsOfType<NpcDialogueSequence>(true);
        log.AppendLine($"В сцене: DialogueTrigger={triggers.Length}, NpcDialogueSequence={seqs.Length}.");
        foreach (var t in triggers)
        {
            if (t.dialogue == null)
            {
                log.AppendLine($"[ОШИБКА] Триггер на «{t.name}»: dialogue пуст.");
                errors++;
                continue;
            }
            int n = t.dialogue.nodes != null ? t.dialogue.nodes.Count : 0;
            if (n == 0)
            {
                log.AppendLine($"[ОШИБКА] Триггер на «{t.name}» → «{t.dialogue.name}»: 0 узлов. E откроет пустоту.");
                errors++;
            }
            if (t.GetComponent<NpcDialogueSequence>() != null)
            {
                log.AppendLine($"[ПРЕДУПРЕЖДЕНИЕ] На «{t.name}» висят И триггер, И цепочка — дерутся за E. Триггер надо выключить.");
                warnings++;
            }
        }
        foreach (var s in seqs)
        {
            if (s.dialogues == null || s.dialogues.Count == 0)
            {
                log.AppendLine($"[ОШИБКА] Цепочка на «{s.name}»: список Dialogues пуст — скрипт гаснет при старте.");
                errors++;
                continue;
            }
            for (int i = 0; i < s.dialogues.Count; i++)
            {
                var d = s.dialogues[i];
                if (d == null)
                {
                    log.AppendLine($"[ОШИБКА] Цепочка на «{s.name}»: слот {i + 1} пуст.");
                    errors++;
                    continue;
                }
                int n = d.nodes != null ? d.nodes.Count : 0;
                if (n == 0)
                {
                    log.AppendLine($"[ОШИБКА] Цепочка на «{s.name}»: «{d.name}» — 0 узлов.");
                    errors++;
                }
            }
            if (!s.enabled)
            {
                log.AppendLine($"[ПРЕДУПРЕЖДЕНИЕ] Цепочка на «{s.name}» выключена (галка снята).");
                warnings++;
            }
        }

        Debug.Log($"[Проверка диалогов]\n{log}");
        EditorUtility.DisplayDialog("Проверка диалогов",
            $"Ошибок: {errors}, предупреждений: {warnings}.\nДетали — в консоли.", "Ок");
    }

    [MenuItem(MenuRoot + "Собрать Степана (1 куб, D1-D8)", false, 22)]
    public static void BuildStepan()
    {
        // D1..D8 по порядку.
        var ordered = new List<DialogueData>();
        string[] guids = AssetDatabase.FindAssets("t:DialogueData");
        var found = new Dictionary<int, DialogueData>();
        foreach (string g in guids)
        {
            var d = AssetDatabase.LoadAssetAtPath<DialogueData>(AssetDatabase.GUIDToAssetPath(g));
            if (d == null) continue;
            Match m = Regex.Match(d.name, @"^D([1-8])_");
            if (!m.Success) continue;
            found[int.Parse(m.Groups[1].Value)] = d;
        }
        for (int i = 1; i <= 8; i++)
        {
            if (!found.TryGetValue(i, out DialogueData d) || d == null)
            {
                EditorUtility.DisplayDialog("Степан",
                    $"Не найден ассет диалога D{i} (имя вида D{i}_...). Сборка отменена.", "Ок");
                return;
            }
            ordered.Add(d);
        }

        // Куб: цепочка уже есть — берём её; нет — выделенный объект; нет — новый.
        NpcDialogueSequence seq = Object.FindObjectOfType<NpcDialogueSequence>(true);
        GameObject go;
        if (seq != null)
        {
            go = seq.gameObject;
        }
        else if (Selection.activeGameObject != null)
        {
            go = Selection.activeGameObject;
            seq = go.AddComponent<NpcDialogueSequence>();
        }
        else
        {
            go = new GameObject("Stepan (NPC)");
            Undo.RegisterCreatedObjectUndo(go, "Создать Степана");
            seq = go.AddComponent<NpcDialogueSequence>();
        }

        Undo.RecordObject(seq, "Назначить D1-D8");
        seq.dialogues = ordered;
        seq.enabled = true;
        EditorUtility.SetDirty(seq);

        // Гасим только Степановы триггеры (D1..D8). D9/D10/D11 — чужие НПС, не трогаем.
        var stepanSet = new HashSet<DialogueData>(ordered);
        int off = 0;
        foreach (var t in Object.FindObjectsOfType<DialogueTrigger>(true))
        {
            if (t != null && t.dialogue != null && stepanSet.Contains(t.dialogue) && t.enabled)
            {
                Undo.RecordObject(t, "Выключить старый триггер");
                t.enabled = false;
                EditorUtility.SetDirty(t);
                off++;
                Debug.Log($"[Степан] Триггер на «{t.name}» («{t.dialogue.name}») выключен — теперь рулит цепочка.", t);
            }
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("Степан",
            $"Готово: куб «{go.name}», D1..D8 по порядку.\nВыключено старых триггеров: {off}.\nСохрани сцену (Ctrl+S), сотри сейвы и жми Play.", "Ок");
    }

    [MenuItem(MenuRoot + "Сбросить сохранения диалогов", false, 23)]
    public static void ResetDialogueSaves()
    {
        int n = 0;
        string[] guids = AssetDatabase.FindAssets("t:DialogueData");
        foreach (string g in guids)
        {
            var d = AssetDatabase.LoadAssetAtPath<DialogueData>(AssetDatabase.GUIDToAssetPath(g));
            if (d == null) continue;
            string key = string.IsNullOrEmpty(d.name) ? d.dialogueName : d.name;
            if (string.IsNullOrEmpty(key)) continue;
            PlayerPrefs.DeleteKey("flame_dlg_node_" + key);
            PlayerPrefs.DeleteKey("flame_dlg_done_" + key);
            n++;
        }
        PlayerPrefs.Save();
        // Оживляем цепочки: выключенные по «всё пройдено» снова включатся.
        foreach (var s in Object.FindObjectsOfType<NpcDialogueSequence>(true))
        {
            s.enabled = true;
            EditorUtility.SetDirty(s);
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[Диалоги] Сейвы стёрты ({n} диалогов). Цепочки включены. Сохрани сцену и жми Play.");
    }

    [MenuItem(MenuRoot + "Сбросить квесты", false, 24)]
    public static void ResetQuests()
    {
        QuestSystem.ResetAll(true);
        Debug.Log("[Квесты] Сброшены (PlayerPrefs flame_q_states стёрт).");
    }
}
