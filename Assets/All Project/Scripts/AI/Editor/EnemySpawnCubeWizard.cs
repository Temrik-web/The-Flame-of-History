using UnityEditor;
using UnityEngine;

/// <summary>Тестовый куб: по E включает 3 немцев. Tools -> Тест -> Создать куб спавна немцев.</summary>
public static class EnemySpawnCubeWizard
{
    [MenuItem("Tools/Тест/Создать куб спавна немцев", false, 0)]
    public static void CreateSpawnCube()
    {
        GameObject player = FindPlayer();

        Vector3 pos;
        if (player != null)
        {
            Vector3 fwd = player.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            pos = player.transform.position + fwd * 3f + Vector3.up * 0.5f;
        }
        else
        {
            pos = new Vector3(0f, 0.5f, 0f);
            Debug.LogWarning("[EnemySpawnCube] Игрок не найден — куб создан в (0, 0.5, 0).");
        }

        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "== TEST: Куб спавна немцев (E) ==";
        cube.transform.position = pos;

        EnemySpawnCube spawner = Undo.AddComponent<EnemySpawnCube>(cube);
        spawner.interactDistance = 3.5f;
        spawner.hintMessage = "Нажмите E — выпустить немцев (тест)";

        Renderer rend = cube.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0.15f, 0.15f, 1f);
            rend.sharedMaterial = mat;
        }

        spawner.enemiesToEnable = FindThreeTemplates();

        Undo.RegisterCreatedObjectUndo(cube, "Create enemy spawn cube");
        Selection.activeGameObject = cube;
        EditorGUIUtility.PingObject(cube);
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();

        int n = CountAssigned(spawner.enemiesToEnable);
        if (n == 0)
            EditorUtility.DisplayDialog("Куб спавна немцев",
                "Куб создан, но шаблоны \"Enemy Template\" на сцене не найдены.\n\n" +
                "Открой сцену Game и перетащи 3 выключенных немца в поле Enemies To Enable.",
                "Ок");
        else
            Debug.Log($"[EnemySpawnCube] Куб создан перед игроком, привязано немцев: {n}/3. " +
                      "Жми Play, подходи и жми E.", cube);
    }

    private static GameObject[] FindThreeTemplates()
    {
        string[] wanted =
        {
            "== Enemy Template (клонируй меня) ==",
            "== Enemy Template (клонируй меня) == (1)",
            "== Enemy Template (клонируй меня) == (2)",
        };

        Transform[] all = Object.FindObjectsOfType<Transform>(true);
        System.Collections.Generic.List<GameObject> found =
            new System.Collections.Generic.List<GameObject>();

        foreach (string w in wanted)
        {
            foreach (Transform t in all)
            {
                if (t.name == w)
                {
                    found.Add(t.gameObject);
                    break;
                }
            }
            if (found.Count >= 3) break;
        }

        if (found.Count < 3)
        {
            foreach (Transform t in all)
            {
                if (t.name.Contains("Enemy Template") && !found.Contains(t.gameObject))
                {
                    found.Add(t.gameObject);
                    if (found.Count >= 3) break;
                }
            }
        }

        return found.ToArray();
    }

    private static int CountAssigned(GameObject[] arr)
    {
        if (arr == null) return 0;
        int n = 0;
        foreach (GameObject go in arr)
            if (go != null) n++;
        return n;
    }

    private static GameObject FindPlayer()
    {
        GameObject tagged = null;
        try { tagged = GameObject.FindGameObjectWithTag("Player"); }
        catch { /* тег может быть не определён */ }
        if (tagged != null) return tagged;

        var cc = Object.FindObjectOfType<CharacterController>();
        if (cc != null) return cc.gameObject;
        return null;
    }
}
