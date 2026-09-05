using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ProjectBuild
{
    [MenuItem("Tools/Сборка/Windows x64")]
    public static void Windows()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0 || scenes.Any(s => !File.Exists(s)))
            throw new BuildFailedException("В Build Settings отсутствуют доступные включённые сцены.");

        Directory.CreateDirectory("Builds/Windows");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "Builds/Windows/The Flame of History.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException($"Сборка завершилась с результатом {report.summary.result}; ошибок: {report.summary.totalErrors}.");
        Debug.Log($"Сборка готова: {report.summary.outputPath}");
    }
}
