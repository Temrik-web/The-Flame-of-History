using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Мастер цепочек диалогов. Меню: Tools -> Диалоги.
///  - «Проверить связки»: ассеты диалогов, триггеры/цепочки в сцене,
///    инфраструктура (игрок, менеджер, канвас).
///  - «Создать NPC-куб»: один куб + NpcDialogueSequence с диалогами по порядку
///    (выделение в Project, иначе цепочка D1..D8 одного НПС), конфликтующие
///    DialogueTrigger на кубе удаляются, сейвы этих диалогов сбрасываются.
///  - «Сбросить ВСЁ (новая игра)»: полный вайп — диалоги, квесты, флаги,
///    инвентарь, счётчики. Для честного ретеста с D1.
///  - «Сбросить сохранения диалогов/квестов»: точечный сброс.
/// </summary>
public static class NpcSequenceSetupWizard
{
    const string MenuRoot = "Tools/Диалоги/";

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
            // Квест-гейт на несуществующий квест = диалог не пройти никогда.
            if (!string.IsNullOrEmpty(d.completionQuestId) &&
                System.Array.FindIndex(QuestSystem.Quests, q => q.id == d.completionQuestId) < 0)
            {
                log.AppendLine($"[ОШИБКА] {d.name}: completionQuestId «{d.completionQuestId}» — нет такого квеста.");
                errors++;
            }
            // Таблица узлов: редакторская правда (что видит AssetDatabase).
            // Если тут 0, а в файле узлы есть — сломан импорт (переимпорт/удали Library).
            // Если тут 17, а в Play 0 — проблема в сцене/рантайме, смотри Play-лог.
            int nn = d.nodes != null ? d.nodes.Count : -1;
            log.AppendLine($"[УЗЛЫ] {d.name}: nodes={(nn < 0 ? "null" : nn.ToString())}" +
                           (nn == 0 ? " <-- ПУСТО В РЕДАКТОРЕ! Жми «Переимпортировать диалоги», не поможет — удали Library." : ""));
            // Пустые строки в .asset ломают YAML-парсер Unity (кейс D7_Eda:
            // файл цел и скрипты видят узлы, а импорт даёт 0). Ловим текстом.
            try
            {
                int blanks = 0;
                foreach (string ln in System.IO.File.ReadAllLines(path))
                    if (string.IsNullOrWhiteSpace(ln)) blanks++;
                if (blanks > 0)
                {
                    log.AppendLine($"[ПРЕДУПРЕЖДЕНИЕ] {d.name}: в файле {blanks} пустых строк — " +
                                   "Unity может импортировать 0 узлов. Удали пустые строки из файла.");
                    warnings++;
                }
            }
            catch { }
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
            if (!t.enabled && t.GetComponent<NpcDialogueSequence>() == null)
            {
                log.AppendLine($"[ПРЕДУПРЕЖДЕНИЕ] Триггер на «{t.name}» выключен и цепочки на объекте нет — куб мёртв, E ничего не даст. Включи триггер или создай NPC-куб.");
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
            // Молчаливый куб: всё пройдено в сейвах — E ничего не даст без сброса.
            bool allDone = s.dialogues.Count > 0;
            foreach (var d in s.dialogues)
            {
                if (d != null && !DialogueManager.IsDialogueDone(d)) { allDone = false; break; }
                if (d == null) allDone = false;
            }
            if (allDone)
            {
                log.AppendLine($"[ПРЕДУПРЕЖДЕНИЕ] Цепочка на «{s.name}»: все диалоги уже пройдены (сейвы PlayerPrefs) — куб молчит. Жми «Сбросить сохранения диалогов».");
                warnings++;
            }
        }

        // 3) Инфраструктура: без игрока/менеджера/канваса E у куба не сработает или не видно.
        GameObject player = null;
        try { player = GameObject.FindGameObjectWithTag("Player"); } catch { player = null; }
        if (player == null && Object.FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>(true) == null)
        {
            log.AppendLine("[ОШИБКА] Игрок не найден (нет тега «Player» и нет FirstPersonController). E у куба не сработает.");
            errors++;
        }
        DialogueManager dm = Object.FindObjectOfType<DialogueManager>(true);
        if (dm == null)
        {
            log.AppendLine("[ОШИБКА] В сцене нет DialogueManager. Запусти «Создать NPC-куб (все диалоги)» или «Создать канвас».");
            errors++;
        }
        else if (dm.dialogueText == null &&
                 Object.FindObjectOfType<UserDialogueUI>(true) == null &&
                 Object.FindObjectOfType<DialogueCanvasUI>(true) == null)
        {
            log.AppendLine("[ОШИБКА] У DialogueManager нет текста реплики и в сцене нет канваса (UserDialogueUI/DialogueCanvasUI). Диалог стартует, но его не видно.");
            errors++;
        }

        // 4) Битые скрипты в сцене (missing MonoBehaviour — «The referenced script
        // is missing»). Ломают объекты молча, в том числе диалоговые.
        int broken = 0;
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>(true))
        {
            Component[] comps = go.GetComponents<Component>();
            bool hasBroken = false;
            foreach (Component c in comps)
            {
                if (c == null) { hasBroken = true; break; }
            }
            if (hasBroken)
            {
                broken++;
                if (broken <= 10)
                    log.AppendLine($"[ОШИБКА] На «{FullPath(go)}» висит missing-скрипт. Удали компонент (в инспекторе — «...» -> Remove).");
            }
        }
        if (broken > 0)
        {
            errors++;
            if (broken > 10)
                log.AppendLine($"[ОШИБКА] ...и ещё {broken - 10} объектов с missing-скриптами.");
        }

        Debug.Log($"[Проверка диалогов]\n{log}");
        EditorUtility.DisplayDialog("Проверка диалогов",
            $"Ошибок: {errors}, предупреждений: {warnings}.\nДетали — в консоли.", "Ок");
    }

    [MenuItem(MenuRoot + "Создать NPC-куб (все диалоги)", false, 22)]
    public static void CreateNpcCube()
    {
        if (!RequireEditMode("Создать NPC-куб")) return;
        // 1) Диалоги: выделенные DialogueData в окне Project (числовой порядок:
        // D1,D2,..,D10, а не D1,D10,D11,D2) — иначе цепочка Степана D1..D8 —
        // иначе все DialogueData проекта, кроме legacy-теста.
        List<DialogueData> ordered = Selection.objects.OfType<DialogueData>()
            .Where(d => d != null)
            .OrderBy(d => LeadingNumber(d.name))
            .ThenBy(d => d.name)
            .ToList();
        string source = "выделение в Project";
        if (ordered.Count == 0)
        {
            ordered = FindChainDialogues();
            source = "цепочка D1..D8 (1 НПС)";
        }
        if (ordered.Count == 0)
        {
            foreach (string g in AssetDatabase.FindAssets("t:DialogueData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<DialogueData>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null && d.name != "OldManDialogue") ordered.Add(d);
            }
            ordered = ordered.OrderBy(d => LeadingNumber(d.name)).ThenBy(d => d.name).ToList();
            source = "все DialogueData проекта";
        }
        if (ordered.Count == 0)
        {
            DialogueData test = GetOrCreateTestDialogue();
            if (test == null)
            {
                EditorUtility.DisplayDialog("NPC-куб",
                    "Нет ни одного DialogueData и не удалось создать тестовый.", "Ок");
                return;
            }
            ordered.Add(test);
            source = "создан тестовый диалог";
        }

        // 2) Инфраструктура: менеджер + канвас, иначе говорить некому и негде.
        EnsureDialogueManager();
        if (Object.FindObjectOfType<DialogueCanvasUI>(true) == null &&
            Object.FindObjectOfType<UserDialogueUI>(true) == null)
        {
            DialogueCanvasWizard.CreateCanvas();
        }

        // 3) Игрок: точка спавна куба (перед игроком) и проверка тега для E.
        GameObject player = null;
        try { player = GameObject.FindGameObjectWithTag("Player"); } catch { player = null; }
        if (player == null)
        {
            var fps = Object.FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
            if (fps != null) player = fps.gameObject;
        }

        // 4) Цель: выделенный объект сцены — иначе новый куб.
        GameObject go = Selection.activeGameObject;
        if (go == null || !go.scene.IsValid())
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "NPC (Диалоги)";
            Undo.RegisterCreatedObjectUndo(go, "Создать NPC-куб");
            if (player != null)
            {
                Vector3 spawn = player.transform.position + player.transform.forward * 2.5f;
                spawn.y = player.transform.position.y + 1f;
                go.transform.position = spawn;
            }
            else
            {
                go.transform.position = new Vector3(0f, 1f, 0f);
            }
            go.transform.localScale = new Vector3(1f, 2f, 1f);
        }

        NpcDialogueSequence seq = go.GetComponent<NpcDialogueSequence>();
        if (seq == null) seq = Undo.AddComponent<NpcDialogueSequence>(go);

        // 5) Конфликтующие триггеры на этом кубе удаляем: за E рулит цепочка.
        int removed = 0;
        foreach (var t in go.GetComponents<DialogueTrigger>())
        {
            Undo.DestroyObjectImmediate(t);
            removed++;
        }

        Undo.RecordObject(seq, "Назначить диалоги");
        seq.dialogues = ordered;
        seq.interactDistance = Mathf.Max(seq.interactDistance, 3f);
        seq.enabled = true;
        EditorUtility.SetDirty(seq);

        // 6) Сейвы этих диалогов сбрасываем — иначе после старых тестов
        // куб молчит (всё уже done) и кажется сломанным.
        foreach (var d in ordered)
        {
            if (d == null) continue;
            string key = string.IsNullOrEmpty(d.name) ? d.dialogueName : d.name;
            if (string.IsNullOrEmpty(key)) continue;
            PlayerPrefs.DeleteKey("flame_dlg_node_" + key);
            PlayerPrefs.DeleteKey("flame_dlg_done_" + key);
        }
        PlayerPrefs.Save();
        foreach (var s in Object.FindObjectsOfType<NpcDialogueSequence>(true))
        {
            s.enabled = true;
            EditorUtility.SetDirty(s);
        }

        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        string warn = player == null
            ? "\nВНИМАНИЕ: игрок не найден (нет тега «Player» и нет FirstPersonController) — E у куба не сработает!"
            : "";
        string names = string.Join(", ", ordered.Where(d => d != null).Select(d => d.name).ToArray());
        EditorUtility.DisplayDialog("NPC-куб",
            $"Готово: куб «{go.name}», диалогов: {ordered.Count} ({source}).\n" +
            $"Порядок: {names}.\n" +
            $"Удалено триггеров: {removed}. Сейвы этих диалогов сброшены.\n" +
            $"Сохрани сцену (Ctrl+S) и жми Play, затем E у куба.{warn}", "Ок");
    }

    [MenuItem(MenuRoot + "Связать ключ и дверь (квест 1)", false, 26)]
    public static void WireKeyAndDoor()
    {
        if (!RequireEditMode("Связать ключ и дверь")) return;
        var log = new System.Text.StringBuilder();
        int errors = 0;

        // 1) Ключ в ОТКРЫТОЙ сцене: точное имя, иначе любой «Rusty».
        GameObject key = FindInSceneByName("Rusty key");
        if (key == null)
        {
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
            {
                if (t != null && t.name.IndexOf("Rusty",
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                { key = t.gameObject; break; }
            }
            if (key != null)
                log.AppendLine($"Точного «Rusty key» нет — взят похожий: {FullPath(key)}. " +
                               "Лучше переименуй модель ровно в «Rusty key».");
        }
        if (key == null)
        {
            log.AppendLine("[ОШИБКА] Ключ не найден в открытой сцене (искал «Rusty key» / *Rusty*). " +
                           "Открой нужную сцену, затем повтори.");
            errors++;
        }
        else log.AppendLine($"Ключ найден: {FullPath(key)}.");

        // 2) Дверь дома деда в ОТКРЫТОЙ сцене.
        GameObject door = FindInSceneByName("DoorPivotDEDA");
        if (door == null)
        {
            var cands = new List<string>();
            foreach (var t in Object.FindObjectsOfType<Transform>(true))
                if (t != null && t.name.StartsWith("DoorPivot") && cands.Count < 10)
                    cands.Add(FullPath(t.gameObject));
            log.AppendLine("[ОШИБКА] «DoorPivotDEDA» не найден в открытой сцене." +
                           (cands.Count > 0 ? " Похожие: " + string.Join(", ", cands.ToArray()) : ""));
            errors++;
        }
        else log.AppendLine($"Дверь найдена: {FullPath(door)}.");

        // 3) Сетап ключа (рантайм-проводка: коллайдер + Pickup + квест-гейт).
        RustyKeyPickupSetup setup = Object.FindObjectOfType<RustyKeyPickupSetup>(true);
        if (setup == null)
        {
            var go = new GameObject("RustyKeySetup");
            Undo.RegisterCreatedObjectUndo(go, "Создать RustyKeySetup");
            setup = go.AddComponent<RustyKeyPickupSetup>();
            log.AppendLine("Создан RustyKeySetup (настроит ключ на старте игры).");
        }
        if (setup != null)
        {
            setup.keyObjectName = key != null ? key.name : "Rusty key";
            setup.itemId = "key_cellar";
            setup.disablePlaceholderKey = true;
            setup.placeholderName = "CartKey";
            EditorUtility.SetDirty(setup);
        }

        // 4) Дверь: замок + ключ cellar + автооткрытие по квесту.
        if (door != null)
        {
            DoorController dc = door.GetComponent<DoorController>();
            if (dc == null)
            {
                dc = Undo.AddComponent<DoorController>(door);
                log.AppendLine("ВНИМАНИЕ: на двери не было DoorController — добавлен, проверь дистанцию/слой.");
            }
            Undo.RecordObject(dc, "Настроить дверь деда");
            dc.isLocked = true;
            dc.requiredKeyId = "cellar";
            dc.lockedHintMessage = "Дверь закрыта — найдите ключ";
            dc.autoUnlockQuestId = "q_cellar_door";
            dc.autoUnlockQuestState = 0;
            EditorUtility.SetDirty(dc);
            log.AppendLine("Дверь: isLocked + ключ «cellar» + автооткрытие по q_cellar_door.");
        }

        // 4б) Полное удаление заглушки CartKey: теперь ключ только ржавый.
        // Куб-заглушка больше не прячется, а сносится из сцены.
        int cartGone = 0;
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
        {
            if (t == null) continue;
            if (t.name != "CartKey" && t.name != "CartKeyMesh") continue;
            Undo.DestroyObjectImmediate(t.gameObject);
            cartGone++;
        }
        if (cartGone > 0)
            log.AppendLine($"Удалена заглушка CartKey: {cartGone} шт. (рантайм-прятание больше не нужно).");

        // 4в) Лишние QuestActivator на повозках с q_cart_key: один прятал ВСЮ
        // повозку (target пуст = сам объект), второй дёргал уже удалённый CartKey.
        // Видимостью ключа рулит QuestKeyPickup на самом ключе, повозка видна всегда.
        // Чужие квесты (q_forest_chest и т.п.) не трогаем.
        int actGone = 0;
        foreach (var a in Object.FindObjectsOfType<QuestActivator>(true))
        {
            if (a == null || a.questId != "q_cart_key") continue;
            string host = a.gameObject.name;
            bool hostIsCart = host.IndexOf("cart", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool targetIsCartKey = a.target != null &&
                (a.target.name == "CartKey" || a.target.name == "CartKeyMesh");
            if (!hostIsCart && !targetIsCartKey) continue;
            Undo.DestroyObjectImmediate(a);
            actGone++;
            log.AppendLine($"Удалён лишний QuestActivator q_cart_key на «{FullPath(a.gameObject)}».");
        }
        if (actGone == 0)
            log.AppendLine("Лишних активаторов q_cart_key на повозках нет.");
        // 5) Гейт D1: без выполненного q_cart_key цепочка дальше не идёт.
        DialogueData d1 = null;
        foreach (string g in AssetDatabase.FindAssets("D1_Znakomstvo t:DialogueData"))
        {
            d1 = AssetDatabase.LoadAssetAtPath<DialogueData>(AssetDatabase.GUIDToAssetPath(g));
            if (d1 != null) break;
        }
        if (d1 == null)
        {
            log.AppendLine("[ОШИБКА] Ассет D1_Znakomstvo не найден в проекте.");
            errors++;
        }
        else if (d1.completionQuestId != "q_cart_key")
        {
            Undo.RecordObject(d1, "Гейт D1");
            d1.completionQuestId = "q_cart_key";
            EditorUtility.SetDirty(d1);
            AssetDatabase.SaveAssets();
            log.AppendLine("D1: поставлен гейт completionQuestId = q_cart_key.");
        }
        else log.AppendLine("D1: гейт q_cart_key уже стоит.");

        // 6) Предмет ключа обязан быть в базе, иначе подбор не сработает.
        ItemDatabase db = ItemDatabase.Instance;
        if (db == null)
        {
            log.AppendLine("[ОШИБКА] ItemDatabase не найден в Resources.");
            errors++;
        }
        else if (db.GetById("key_cellar") == null)
        {
            ItemData found = null;
            foreach (string g in AssetDatabase.FindAssets("Item_KeyCellar t:ItemData"))
            {
                found = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g));
                if (found != null) break;
            }
            if (found == null)
            {
                log.AppendLine("[ОШИБКА] Ассет Item_KeyCellar не найден в проекте.");
                errors++;
            }
            else
            {
                Undo.RecordObject(db, "Добавить ключ в базу");
                db.items.Add(found);
                db.RebuildCache();
                EditorUtility.SetDirty(db);
                AssetDatabase.SaveAssets();
                log.AppendLine("Item_KeyCellar добавлен в ItemDatabase.");
            }
        }
        else log.AppendLine("ItemDatabase: key_cellar на месте.");

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[Ключ и дверь]\n{log}");
        EditorUtility.DisplayDialog("Ключ и дверь",
            (errors == 0 ? "Всё связано.\n\n" : $"Ошибок: {errors}.\n\n") +
            log + "\nСохрани сцену (Ctrl+S).", "Ок");
    }

    /// <summary>Поиск объекта по имени в открытой сцене, включая скрытые.</summary>
    static GameObject FindInSceneByName(string n)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t != null && t.name == n) return t.gameObject;
        return null;
    }
    static string FullPath(GameObject go)
    {
        string path = go != null ? go.name : "?";
        Transform t = go != null ? go.transform.parent : null;
        while (t != null)
        {
            path = t.name + "/" + path;
            t = t.parent;
        }
        return path;
    }

    /// <summary>Число после D в имени (D1..D8 -> 1..8). Без числа — в конец.</summary>
    static int LeadingNumber(string name)
    {
        Match m = Regex.Match(name ?? "", @"^D(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) return n;
        return int.MaxValue;
    }

    /// <summary>
    /// Цепочка одного НПС (Степан): D1..D8 по числовому порядку.
    /// D9/D10/D11 — другие НПС, сюда не берём.
    /// </summary>
    static List<DialogueData> FindChainDialogues()
    {
        var list = new List<DialogueData>();
        foreach (string g in AssetDatabase.FindAssets("t:DialogueData"))
        {
            var d = AssetDatabase.LoadAssetAtPath<DialogueData>(AssetDatabase.GUIDToAssetPath(g));
            if (d == null) continue;
            Match m = Regex.Match(d.name, @"^D([1-8])_");
            if (m.Success) list.Add(d);
        }
        return list.OrderBy(d => LeadingNumber(d.name)).ThenBy(d => d.name).ToList();
    }

    /// <summary>Менеджер в сцене (для цепочки и канваса).</summary>
    static void EnsureDialogueManager()
    {
        if (Object.FindObjectOfType<DialogueManager>() != null) return;
        var dm = new GameObject("DialogueManager", typeof(DialogueManager));
        Undo.RegisterCreatedObjectUndo(dm, "Создать DialogueManager");
    }

    /// <summary>Тестовый диалог-болванка, если в проекте вообще нет DialogueData.</summary>
    static DialogueData GetOrCreateTestDialogue()
    {
        foreach (string g in AssetDatabase.FindAssets("TestDialogue t:DialogueData"))
        {
            var found = AssetDatabase.LoadAssetAtPath<DialogueData>(AssetDatabase.GUIDToAssetPath(g));
            if (found != null) return found;
        }

        var data = ScriptableObject.CreateInstance<DialogueData>();
        data.dialogueName = "TestDialogue";

        var hello = new DialogueNode();
        hello.nodeID = "hello";
        hello.speakerName = "Куб";
        hello.dialogueText = "Привет! Я тестовый куб. Нажми пробел или кликни, потом выбери ответ.";
        hello.textSpeed = 0.03f;
        hello.nextNodeID = "question";

        var question = new DialogueNode();
        question.nodeID = "question";
        question.speakerName = "Куб";
        question.dialogueText = "Ну что, работает цепочка?";
        question.textSpeed = 0.03f;

        var good = new DialogueChoice();
        good.choiceText = "Работает";
        good.nextNodeID = "bye";

        var byeChoice = new DialogueChoice();
        byeChoice.choiceText = "Пока";
        byeChoice.endDialogue = true;

        question.choices.Add(good);
        question.choices.Add(byeChoice);

        var bye = new DialogueNode();
        bye.nodeID = "bye";
        bye.speakerName = "Куб";
        bye.dialogueText = "Отлично! Теперь выдели свои диалоги в Project и создай куб заново.";
        bye.textSpeed = 0.03f;

        data.nodes.Add(hello);
        data.nodes.Add(question);
        data.nodes.Add(bye);

        string folder = "Assets/All Project/Dialog";
        if (!AssetDatabase.IsValidFolder(folder)) folder = "Assets";
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/TestDialogue.asset");
        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        return data;
    }

    [MenuItem(MenuRoot + "Переимпортировать диалоги (лечит «0 узлов»)", false, 23)]
    public static void ReimportDialogues()
    {
        if (!RequireEditMode("Переимпортировать диалоги")) return;
        // Лечит рассинхрон Library: файл цел (узлы на месте), а рантайм видит
        // пустой список (как было с D7_Eda). Форсированный переимпорт всех
        // ассетов папки Dialog + условий.
        string[] guids = AssetDatabase.FindAssets("", new[] { "Assets/All Project/Dialog" });
        int n = 0;
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path) || path.EndsWith(".meta")) continue;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            n++;
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Диалоги",
            $"Переимпортировано файлов: {n}.\nТеперь жми Play — «0 узлов» должно уйти. " +
            $"Если нет — жми «Проверить связки» и смотри консоль.", "Ок");
    }

    [MenuItem(MenuRoot + "Сбросить ВСЁ (новая игра)", false, 20)]
    public static void ResetAllSaves()
    {
        // Полный вайп для честного ретеста с нуля: диалоги (прогресс, done,
        // одноразовые кнопки, история), квесты, флаги/доверие, инвентарь,
        // счётчики событий. Иначе Play продолжает с прошлого разговора.
        // Работает и в Play: тогда после вайпа сцена перезагружается.
        if (Application.isPlaying)
        {
            WipeAllSavesRuntime();
            QuestSystem.ResetAll(true);
            GameState.ResetAll(true);
            InventorySystem invLive =
                Object.FindObjectOfType<InventorySystem>();
            if (invLive != null)
            {
                invLive.Clear();
                invLive.DeleteSave();
                PlayerPrefs.Save();
            }
            Debug.Log("[Новая игра] Сейвы стёрты в Play — перезагружаю сцену.",
                Object.FindObjectOfType<DialogueManager>(true));
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            return;
        }

        WipeAllSavesRuntime();
        QuestSystem.ResetAll(true);
        GameState.ResetAll(true);

        InventorySystem inv = Object.FindObjectOfType<InventorySystem>(true);
        if (inv != null)
        {
            inv.Clear();
            inv.DeleteSave();
            PlayerPrefs.Save();
        }

        int hooks = 0;
        foreach (var h in Object.FindObjectsOfType<DialogueRaidHook>(true))
        {
            if (h == null) continue;
            h.ResetCounter();
            hooks++;
        }

        // Оживляем цепочки: выключенные по «всё пройдено» снова включатся.
        foreach (var s in Object.FindObjectsOfType<NpcDialogueSequence>(true))
        {
            s.enabled = true;
            EditorUtility.SetDirty(s);
        }
        DirtyScene();
        Debug.Log($"[Новая игра] Всё стёрто, хуков={hooks}, " +
                  $"инвентарь={(inv != null ? "очищен" : "не найден")}. Сохрани сцену и жми Play — начнётся с D1.");
        EditorUtility.DisplayDialog("Новая игра",
            $"Всё стёрто, хуков={hooks}.\n" +
            "Квесты, флаги, доверие, инвентарь, кнопки, история — с нуля.\n" +
            "Сохрани сцену (Ctrl+S) и жми Play.", "Ок");
    }

    /// <summary>
    /// Стереть сейвы диалогов (прогресс, done, одноразовые кнопки, история).
    /// Безопасно и в Play (только PlayerPrefs), и в редакторе.
    /// </summary>
    static int WipeAllSavesRuntime()
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
            PlayerPrefs.DeleteKey("flame_dlg_used_" + key);
            PlayerPrefs.DeleteKey("flame_hist_" + key);
            n++;
        }
        PlayerPrefs.Save();
        return n;
    }

    /// <summary>Пометить сцену грязной (в Play запрещено — молча пропускаем).</summary>
    static void DirtyScene()
    {
        if (Application.isPlaying) return;
        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    /// <summary>Редакторные меню-конструкторы в Play не работают — только вне.</summary>
    static bool RequireEditMode(string what)
    {
        if (!Application.isPlaying) return true;
        EditorUtility.DisplayDialog("Только вне Play",
            $"«{what}» меняет сцену — останови Play и повтори.", "Ок");
        return false;
    }

    [MenuItem(MenuRoot + "Сбросить сохранения диалогов", false, 24)]
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
            PlayerPrefs.DeleteKey("flame_dlg_used_" + key);
            PlayerPrefs.DeleteKey("flame_hist_" + key);
            n++;
        }
        PlayerPrefs.Save();
        // Оживляем цепочки: выключенные по «всё пройдено» снова включатся.
        foreach (var s in Object.FindObjectsOfType<NpcDialogueSequence>(true))
        {
            s.enabled = true;
            EditorUtility.SetDirty(s);
        }
        DirtyScene();
        Debug.Log($"[Диалоги] Сейвы стёрты ({n} диалогов: прогресс, done, кнопки, история). Цепочки включены. Сохрани сцену и жми Play.");
    }

    [MenuItem(MenuRoot + "Сбросить квесты", false, 25)]
    public static void ResetQuests()
    {
        QuestSystem.ResetAll(true);
        Debug.Log("[Квесты] Сброшены (PlayerPrefs flame_q_states стёрт).");
    }
}
