using UnityEngine;

/// <summary>
/// Тестовый куб: подходишь, видишь подсказку, жмёшь E — включаются 3 немца.
/// Немцев НЕ спавним из префаба, а просто включаем уже лежащие на сцене объекты
/// (== Enemy Template (клонируй меня) == и его клоны). Перетащи их в массив в инспекторе
/// или оставь пустым — скрипт сам найдёт выключенные объекты с именем "Enemy Template".
/// </summary>
public class EnemySpawnCube : MonoBehaviour
{
    [Header("Немцы (выключенные объекты на сцене)")]
    [Tooltip("Перетащи сюда 3 шаблона: == Enemy Template (клонируй меня) ==, (1), (2). Пусто — найдутся сами по имени.")]
    public GameObject[] enemiesToEnable = new GameObject[3];

    [Header("Взаимодействие")]
    public float interactDistance = 3.5f;
    public KeyCode interactKey = KeyCode.E;
    public string hintMessage = "Нажмите E — выпустить немцев (тест)";
    public string doneMessage = "Немцы выпущены!";
    [Tooltip("Сработать только один раз. Выключи, если хочешь включать/выключать повторно.")]
    public bool spawnOnce = true;
    public float doneMessageTime = 3f;

    private GameObject cachedPlayer;
    private bool playerInRange;
    private bool hasSpawned;
    private float doneUntil;
    private float nextPlayerWarnTime;

    private void Start()
    {
        // Автопоиск, если в инспекторе ничего не назначили (удобно для теста).
        if (!HasAssignedEnemies())
            AutoFindEnemies();

        if (!HasAssignedEnemies())
            Debug.LogWarning($"[EnemySpawnCube] {name}: немцы не назначены и не найдены по имени " +
                             "\"Enemy Template\". Перетащи 3 шаблона в массив enemiesToEnable.", this);
        else
            Debug.Log($"[EnemySpawnCube] {name}: готово, немцев в списке: {CountAssigned()} " +
                      "(выключенные включатся по E).", this);
    }

    private void Update()
    {
        if (cachedPlayer == null)
        {
            try { cachedPlayer = GameObject.FindGameObjectWithTag("Player"); }
            catch { cachedPlayer = null; }
            if (cachedPlayer == null)
            {
                if (Time.time >= nextPlayerWarnTime)
                {
                    nextPlayerWarnTime = Time.time + 5f;
                    Debug.LogWarning($"[EnemySpawnCube] {name}: игрок с тегом «Player» не найден — E не сработает.", this);
                }
                playerInRange = false;
                return;
            }
        }

        playerInRange = Vector3.Distance(transform.position, cachedPlayer.transform.position) <= interactDistance;

        if (!playerInRange)
            return;

        // Во время диалога клавиша E принадлежит диалогу.
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;

        if (spawnOnce && hasSpawned)
            return;

        if (Input.GetKeyDown(interactKey))
            EnableEnemies();
    }

    /// <summary>Включить всех назначенных немцев. Вызывается по E, можно дёрнуть и из UnityEvent/кнопки.</summary>
    public void EnableEnemies()
    {
        int enabled = 0;
        foreach (GameObject go in enemiesToEnable)
        {
            if (go == null) continue;
            if (!go.activeSelf)
            {
                go.SetActive(true);
                enabled++;
            }
        }

        hasSpawned = true;
        doneUntil = Time.time + doneMessageTime;
        Debug.Log($"[EnemySpawnCube] {name}: включено немцев: {enabled} из {CountAssigned()}.", this);
    }

    private bool HasAssignedEnemies()
    {
        if (enemiesToEnable == null) return false;
        foreach (GameObject go in enemiesToEnable)
            if (go != null) return true;
        return false;
    }

    private int CountAssigned()
    {
        if (enemiesToEnable == null) return 0;
        int n = 0;
        foreach (GameObject go in enemiesToEnable)
            if (go != null) n++;
        return n;
    }

    private void AutoFindEnemies()
    {
        // Ищем ВСЕ объекты сцены, включая выключенные, с нужным именем.
        // Берём ровно 3: сначала точное "== Enemy Template (клонируй меня) ==",
        // потом "(1)", "(2)" — как просил автор теста.
        Transform[] all = FindObjectsOfType<Transform>(true);
        System.Collections.Generic.List<GameObject> found =
            new System.Collections.Generic.List<GameObject>();

        string[] wanted =
        {
            "== Enemy Template (клонируй меня) ==",
            "== Enemy Template (клонируй меня) == (1)",
            "== Enemy Template (клонируй меня) == (2)",
        };

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

        // Запасной вариант: любые 3 объекта с подстрокой "Enemy Template".
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

        enemiesToEnable = found.ToArray();
    }

    private void OnGUI()
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 20;
        style.alignment = TextAnchor.MiddleCenter;

        if (playerInRange && (!spawnOnce || !hasSpawned))
        {
            GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 50, 500, 30), hintMessage, style);
        }
        else if (hasSpawned && Time.time < doneUntil)
        {
            GUI.Label(new Rect(Screen.width / 2 - 250, Screen.height / 2 + 50, 500, 30), doneMessage, style);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, interactDistance);

        if (enemiesToEnable == null) return;
        Gizmos.color = Color.yellow;
        foreach (GameObject go in enemiesToEnable)
        {
            if (go != null)
                Gizmos.DrawLine(transform.position, go.transform.position);
        }
    }
}
