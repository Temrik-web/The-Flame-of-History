using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Unity считает пустые слоты TreePrototype ошибками при записи player data.
/// Удаляет только потерянные ссылки и экземпляры, которые на них указывали;
/// индексы остальных деревьев перенумеровывает без изменения их позиций.
/// </summary>
public sealed class TerrainBuildRepair : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report) => RepairAll();

    [InitializeOnLoadMethod]
    private static void RepairAfterScriptReload()
    {
        // delayCall ждёт завершения импорта: TerrainData уже доступны,
        // а SaveAssets не запускается посреди сборки скриптов.
        EditorApplication.delayCall += RepairAll;
    }

    [MenuItem("Tools/Сборка/Диагностика Terrain")]
    public static void Diagnose()
    {
        var report = new StringBuilder();
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            var instances = data.treeInstances;
            report.AppendLine(path + " | trees=" + instances.Length);
            var prototypes = data.treePrototypes;
            for (int i = 0; i < prototypes.Length; i++)
            {
                var prefab = prototypes[i].prefab;
                report.AppendLine($"  [{i}] {(prefab != null ? AssetDatabase.GetAssetPath(prefab) : "MISSING")} | instances={instances.Count(t => t.prototypeIndex == i)} | meshRenderers={(prefab != null ? prefab.GetComponentsInChildren<MeshRenderer>(true).Length : 0)}");
            }
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/terrain-diagnostics.txt", report.ToString());
        Debug.Log("Диагностика Terrain: Logs/terrain-diagnostics.txt");
    }

    [MenuItem("Tools/Сборка/Исправить пустые деревья Terrain")]
    public static void RepairAll()
    {
        int repairedAssets = 0;
        int removedPrototypes = 0;
        int removedInstances = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null) continue;

            TreePrototype[] oldPrototypes = data.treePrototypes;
            int[] remap = new int[oldPrototypes.Length];
            int nextIndex = 0;
            for (int i = 0; i < oldPrototypes.Length; i++)
                remap[i] = oldPrototypes[i] != null && oldPrototypes[i].prefab != null
                    ? nextIndex++ : -1;

            if (nextIndex == oldPrototypes.Length) continue;

            TreeInstance[] oldInstances = data.treeInstances;
            var newInstances = oldInstances
                .Where(tree => tree.prototypeIndex >= 0 &&
                               tree.prototypeIndex < remap.Length &&
                               remap[tree.prototypeIndex] >= 0)
                .Select(tree =>
                {
                    tree.prototypeIndex = remap[tree.prototypeIndex];
                    return tree;
                })
                .ToArray();

            var newPrototypes = oldPrototypes
                .Where(prototype => prototype != null && prototype.prefab != null)
                .ToArray();

            Undo.RegisterCompleteObjectUndo(data, "Исправить пустые деревья Terrain");
            data.treeInstances = newInstances;
            data.treePrototypes = newPrototypes;
            data.RefreshPrototypes();
            EditorUtility.SetDirty(data);

            repairedAssets++;
            removedPrototypes += oldPrototypes.Length - newPrototypes.Length;
            removedInstances += oldInstances.Length - newInstances.Length;
            Debug.Log($"[TerrainBuildRepair] {path}: удалено пустых прототипов " +
                      $"{oldPrototypes.Length - newPrototypes.Length}, экземпляров " +
                      $"{oldInstances.Length - newInstances.Length}.", data);
        }

        if (repairedAssets > 0) AssetDatabase.SaveAssets();
        ValidateNoMissingPrototypes();
        Debug.Log($"[TerrainBuildRepair] Готово: TerrainData {repairedAssets}, " +
                  $"прототипов {removedPrototypes}, экземпляров {removedInstances}.");
    }

    private static void ValidateNoMissingPrototypes()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainData", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null) continue;

            TreePrototype[] prototypes = data.treePrototypes;
            for (int i = 0; i < prototypes.Length; i++)
            {
                if (prototypes[i] == null || prototypes[i].prefab == null)
                    throw new BuildFailedException(
                        $"TerrainData '{path}' всё ещё содержит пустой TreePrototype [{i}].");
            }

            foreach (TreeInstance tree in data.treeInstances)
            {
                if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length)
                    throw new BuildFailedException(
                        $"TerrainData '{path}' содержит TreeInstance с неверным " +
                        $"prototypeIndex={tree.prototypeIndex}.");
            }
        }
    }
}
