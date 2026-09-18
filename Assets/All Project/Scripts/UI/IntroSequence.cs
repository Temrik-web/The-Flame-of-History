using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Интро-последовательность при запуске игры:
/// Видео -> Phon + PhotoSlot_1 + текст -> Phon + PhotoSlot_2 -> Главное меню.
/// Фон Phon при переходе между фото НЕ трогаем.
///
/// Пропуск — УДЕРЖАНИЕМ Esc:
/// - держишь Esc — белая полоска заполняется;
/// - отпустил раньше — полоска возвращается назад;
/// - додержал до конца — скип текущего этапа.
///
/// Поддерживаются два варианта имён объектов (старые и новые):
///   Видео:      player / VideoPlayer / VideoDisplay / VideoImage
///   Фон:        PhonePanel / Phon
///   Фото 1:     PhotoSlot_1 / Phtoto1 / Photo1
///   Фото 2:     PhotoSlot_2 / Photo2
///   Текст:      TextContainer / Phontexta
/// Если ссылки не назначены — ищутся по именам. SkipHint/SkipBar
/// создаются автоматически, если их нет в сцене.
/// </summary>
[DisallowMultipleComponent]
public class IntroSequence : MonoBehaviour
{
    private enum Stage { Video, Photo1, Photo2, Done }

    [Header("Видео")]
    [Tooltip("Основной VideoPlayer. Если пусто — выбирается автоматически (у кого есть клип).")]
    public VideoPlayer videoPlayer;
    [Tooltip("Объект с RawImage для видео (VideoDisplay/VideoImage). Выключается при переходе к фото.")]
    public GameObject videoRoot;
    [Tooltip("Выключить остальные VideoPlayer в сцене чтобы не было двойного звука/мерцания.")]
    public bool disableExtraVideoPlayers = true;
    [Tooltip("Задержка после конца видео перед фото (чтобы не резало последний кадр).")]
    public float videoEndDelay = 0.3f;
    [Tooltip("Если видео не заиграло за это время — идём к фото, не висим на чёрном экране.")]
    public float videoTimeout = 15f;

    [Header("Фото 1 (фон + картинка + текст)")]
    [Tooltip("PhonePanel/Phon — общий фон. НЕ выключается на фото 2.")]
    public GameObject phonRoot;
    [Tooltip("PhotoSlot_1 — картинка фото 1. Выключается при переходе к фото 2.")]
    public GameObject photo1Root;
    [Tooltip("TextContainer — фон текста. Включается вместе с фоном.")]
    public GameObject textBackground;
    [Tooltip("Сколько секунд висит фото 1.")]
    public float photo1Duration = 4f;

    [Header("Фото 2")]
    [Tooltip("PhotoSlot_2 — включается после выключения фото 1.")]
    public GameObject photo2Root;
    [Tooltip("Сколько секунд висит фото 2.")]
    public float photo2Duration = 4f;

    [Header("Появление")]
    [Tooltip("Длительность плавного появления фото/текста.")]
    public float photoFadeDuration = 0.8f;

    [Header("Пропуск (удерживай Esc)")]
    [Tooltip("Сколько секунд держать Esc чтобы скипнуть текущий этап.")]
    public float holdToSkipDuration = 1.2f;
    [Tooltip("Во сколько раз быстрее полоска пустеет после отпускания.")]
    public float drainMultiplier = 1.5f;
    [Tooltip("Белая полоска (Image, Filled/Horizontal). Создастся сама, если пусто.")]
    public Image skipProgressBar;
    [Tooltip("Подсказка 'Удерживайте ESC'. Создастся сама, если пусто.")]
    public GameObject skipHint;
    [Tooltip("Если true — додержанный Esc из любого этапа ведёт сразу в меню.")]
    public bool skipDirectToMenu = false;

    [Header("Переход в меню")]
    [Tooltip("Точное имя сцены главного меню.")]
    public string mainMenuSceneName = "Main menu";

    [Header("Отладка")]
    [Tooltip("Писать этапы и прогресс скипа в Console — помогает проверить Esc.")]
    public bool debugLogs = true;

    private Stage currentStage = Stage.Video;
    private float stageTimer;
    private float skipProgress; // 0..1
    private int lastSkipQuarter = -1;
    private bool isTransitioning;
    private Coroutine videoEndCoroutine;

    private readonly List<VideoPlayer> allPlayers = new List<VideoPlayer>();
    private RectTransform canvasRect;
    private bool canvasWarned;
    private CanvasGroup photo1Group;
    private CanvasGroup photo2Group;
    private CanvasGroup textBgGroup;
    private RawImage videoImage;

    // ============================================================

    void Awake()
    {
        AutoFindReferences();
        // Сначала чиним отображение и состояния — они критичны.
        // Автосоздание Skip UI последним: оно не должно ронять остальное.
        RepairDisplayIssues();
        CacheGroups();
        EnsureSkipUI();

        // Стартовое состояние: фото выключены, видео включено
        SetActiveSafe(phonRoot, false);
        SetActiveSafe(photo1Root, false);
        SetActiveSafe(photo2Root, false);
        SetActiveSafe(textBackground, false);
        SetActiveSafe(videoRoot, true);

        // Полоска обязана быть Filled/Horizontal, иначе fillAmount игнорируется
        // и она выглядит статичной. Выставляем принудительно — инспектор мог сбить.
        if (skipProgressBar != null)
        {
            skipProgressBar.type = Image.Type.Filled;
            skipProgressBar.fillMethod = Image.FillMethod.Horizontal;
            skipProgressBar.fillOrigin = (int)Image.OriginHorizontal.Left;
            skipProgressBar.fillAmount = 0f;
        }

        Log("[Intro] Старт. Этап: Видео.");
    }

    void Start()
    {
        currentStage = Stage.Video;
        stageTimer = 0f;

        CollectVideoPlayers();

        if (videoPlayer != null && HasContent(videoPlayer))
        {
            SetupPrimaryPlayer();
            videoPlayer.Prepare();
            StartCoroutine(VideoTimeoutWatchdog());
        }
        else
        {
            Log("[Intro] У VideoPlayer нет клипа — сразу к фото.");
            GoToPhoto1();
        }
    }

    void OnDestroy()
    {
        foreach (VideoPlayer p in allPlayers)
        {
            if (p == null) continue;
            p.loopPointReached -= OnVideoFinished;
            p.prepareCompleted -= OnVideoPrepared;
            p.errorReceived -= OnVideoError;
        }
    }

    // ---------- Видео ----------

    static bool HasContent(VideoPlayer p)
    {
        return p != null && (p.clip != null || !string.IsNullOrEmpty(p.url));
    }

    /// <summary>Выбирает главный плеер и глушит остальные (двойной звук/мерцание).</summary>
    void CollectVideoPlayers()
    {
        allPlayers.Clear();
        allPlayers.AddRange(FindObjectsOfType<VideoPlayer>(true));

        // Кандидат по умолчанию
        if (videoPlayer == null || !HasContent(videoPlayer))
        {
            VideoPlayer best = null;
            if (videoPlayer != null && HasContent(videoPlayer))
                best = videoPlayer;
            else
                best = FindPlayerByNames("player", "VideoPlayer", "VideoDisplay", "VideoImage");

            if (!HasContent(best))
            {
                foreach (VideoPlayer p in allPlayers)
                {
                    if (HasContent(p)) { best = p; break; }
                }
            }
            if (best == null && allPlayers.Count > 0)
                best = allPlayers[0];

            if (best != null && best != videoPlayer)
            {
                Log($"[Intro] Главный VideoPlayer: '{best.name}' (был '{(videoPlayer != null ? videoPlayer.name : "none")}').");
                videoPlayer = best;
            }
        }

        // Глушим дубли чтобы не дрались за звук и RenderTexture
        if (disableExtraVideoPlayers)
        {
            foreach (VideoPlayer p in allPlayers)
            {
                if (p == null || p == videoPlayer) continue;
                if (p.isPlaying) p.Stop();
                p.playOnAwake = false;
                p.enabled = false;
                Log($"[Intro] Лишний VideoPlayer '{p.name}' выключен.");
            }
        }
    }

    void SetupPrimaryPlayer()
    {
        // Если PlayOnAwake уже запустил его до Start — останавливаем и берём управление
        if (videoPlayer.isPlaying) videoPlayer.Stop();

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = false;
        videoPlayer.skipOnDrop = true;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;

        AudioSource src = videoPlayer.GetComponent<AudioSource>();
        if (src == null)
            src = videoPlayer.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, src);

        // RawImage для картинки: ищем у себя, иначе по именам
        if (videoImage == null)
            videoImage = videoPlayer.GetComponent<RawImage>();
        if (videoImage == null)
        {
            GameObject disp = FindByNames("VideoDisplay", "VideoImage");
            if (disp != null) videoImage = disp.GetComponent<RawImage>();
        }
        // Подхватываем RenderTexture плеера если у RawImage пусто
        if (videoImage != null && videoImage.texture == null && videoPlayer.targetTexture != null)
            videoImage.texture = videoPlayer.targetTexture;

        videoPlayer.prepareCompleted += OnVideoPrepared;
        videoPlayer.loopPointReached += OnVideoFinished;
        videoPlayer.errorReceived += OnVideoError;
    }

    void OnVideoPrepared(VideoPlayer vp)
    {
        if (currentStage != Stage.Video || isTransitioning) return;
        Log("[Intro] Видео готово — играем.");
        vp.Play();
        if (videoImage != null)
        {
            Color c = videoImage.color;
            c.a = 1f;
            videoImage.color = c;
        }
    }

    void OnVideoFinished(VideoPlayer vp)
    {
        if (currentStage != Stage.Video || isTransitioning) return;
        Log("[Intro] Видео закончилось.");
        if (videoEndCoroutine == null)
            videoEndCoroutine = StartCoroutine(DelayedGoToPhoto1());
    }

    void OnVideoError(VideoPlayer vp, string message)
    {
        Log($"[Intro] Ошибка видео: {message}. Переходим к фото.");
        GoToPhoto1();
    }

    IEnumerator DelayedGoToPhoto1()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, videoEndDelay));
        videoEndCoroutine = null;
        GoToPhoto1();
    }

    IEnumerator VideoTimeoutWatchdog()
    {
        float t = 0f;
        while (t < videoTimeout && currentStage == Stage.Video && !isTransitioning)
        {
            if (videoPlayer != null && videoPlayer.isPlaying)
                yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        if (currentStage == Stage.Video && !isTransitioning &&
            (videoPlayer == null || !videoPlayer.isPlaying))
        {
            Log("[Intro] Видео не заиграло за таймаут — переход к фото.");
            GoToPhoto1();
        }
    }

    // ---------- Этапы ----------

    void Update()
    {
        if (isTransitioning) return;

        UpdateSkipHold();

        switch (currentStage)
        {
            case Stage.Video:
                UpdateVideoStage();
                break;
            case Stage.Photo1:
                UpdatePhotoStage(photo1Duration, GoToPhoto2);
                break;
            case Stage.Photo2:
                UpdatePhotoStage(photo2Duration, GoToMainMenu);
                break;
        }
    }

    void UpdateVideoStage()
    {
        if (videoPlayer != null && videoPlayer.clip != null && videoPlayer.isPlaying)
        {
            double len = videoPlayer.length;
            if (len > 0.05 && videoPlayer.time >= len - 0.1)
            {
                Log("[Intro] Конец видео по времени.");
                GoToPhoto1();
            }
        }
    }

    void UpdatePhotoStage(float duration, System.Action onDone)
    {
        stageTimer += Time.unscaledDeltaTime;
        float fade = photoFadeDuration > 0f
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(stageTimer / photoFadeDuration))
            : 1f;

        if (currentStage == Stage.Photo1)
        {
            SetAlpha(photo1Group, fade);
            SetAlpha(textBgGroup, fade);
        }
        else
        {
            SetAlpha(photo2Group, fade);
            SetAlpha(textBgGroup, fade);
        }

        if (stageTimer >= duration)
            onDone?.Invoke();
    }

    /// <summary>Видео -> Фото 1: гасим видео, включаем фон + фото 1 + текст.</summary>
    public void GoToPhoto1()
    {
        if (isTransitioning || currentStage == Stage.Photo1) return;
        StopVideoAndCleanup();
        ResetSkip();

        currentStage = Stage.Photo1;
        stageTimer = 0f;

        SetActiveSafe(videoRoot, false);
        SetActiveSafe(phonRoot, true);
        SetActiveSafe(photo1Root, true);
        SetActiveSafe(textBackground, true);

        SetAlpha(photo1Group, 0f);
        SetAlpha(textBgGroup, 0f);
        Log("[Intro] Этап: Фото 1.");
    }

    /// <summary>Фото 1 -> Фото 2: выключаем ТОЛЬКО фото 1, фон НЕ трогаем.</summary>
    public void GoToPhoto2()
    {
        if (isTransitioning || currentStage == Stage.Photo2) return;
        StopVideoAndCleanup();
        ResetSkip();

        SetActiveSafe(photo1Root, false);
        SetActiveSafe(photo2Root, true);

        currentStage = Stage.Photo2;
        stageTimer = 0f;

        SetAlpha(photo2Group, 0f);
        Log("[Intro] Этап: Фото 2.");
        if (photo2Root == null) GoToMainMenu();
    }

    public void GoToMainMenu()
    {
        if (isTransitioning) return;
        isTransitioning = true;
        StopVideoAndCleanup();
        ResetSkip();
        currentStage = Stage.Done;
        Log("[Intro] Этап: Главное меню.");
        SceneManager.LoadScene(mainMenuSceneName);
    }

    void StopVideoAndCleanup()
    {
        StopAllCoroutines();
        videoEndCoroutine = null;
        foreach (VideoPlayer p in allPlayers)
        {
            if (p != null && p.isPlaying) p.Stop();
        }
    }

    // ---------- Удержание Esc ----------

    void UpdateSkipHold()
    {
        // Страховка: масштаб Canvas иногда обнуляют в редакторе —
        // без этого не видно ни видео, ни полоску.
        if (canvasRect != null && canvasRect.localScale.magnitude < 0.01f)
        {
            canvasRect.localScale = Vector3.one;
            if (!canvasWarned)
            {
                canvasWarned = true;
                Log("[Intro] Масштаб Canvas был 0 — починен в рантайме. Не масштабируй Canvas в редакторе!");
            }
        }

        bool holding = Input.GetKey(KeyCode.Escape);

        if (holding)
        {
            skipProgress += Time.unscaledDeltaTime / Mathf.Max(0.01f, holdToSkipDuration);
            skipProgress = Mathf.Min(1f, skipProgress);

            int quarter = Mathf.FloorToInt(skipProgress * 4f);
            if (quarter != lastSkipQuarter)
            {
                lastSkipQuarter = quarter;
                Log($"[Intro] Esc держим: {Mathf.RoundToInt(skipProgress * 100f)}%.");
            }

            if (skipProgress >= 1f)
                OnSkipHeld();
        }
        else if (skipProgress > 0f)
        {
            // Отпустил раньше — полоска возвращается назад
            float drain = Time.unscaledDeltaTime / Mathf.Max(0.01f, holdToSkipDuration) * drainMultiplier;
            skipProgress = Mathf.Max(0f, skipProgress - drain);
            if (skipProgress == 0f) lastSkipQuarter = -1;
        }

        if (skipProgressBar != null)
            skipProgressBar.fillAmount = skipProgress;

        // Подсказка видна только на этапе видео — дальше только фото и меню. ЗАЧЕМ это сделано?????????????????????
        
        /* if (skipHint != null)
            skipHint.SetActive(currentStage == Stage.Video && !isTransitioning); */
    }

    void OnSkipHeld()
    {
        Log("[Intro] Esc додержан — скип этапа.");
        ResetSkip();
        if (skipDirectToMenu)
        {
            GoToMainMenu();
            return;
        }
        switch (currentStage)
        {
            case Stage.Video: GoToPhoto1(); break;
            case Stage.Photo1: GoToPhoto2(); break;
            case Stage.Photo2: GoToMainMenu(); break;
        }
    }

    void ResetSkip()
    {
        skipProgress = 0f;
        lastSkipQuarter = -1;
        if (skipProgressBar != null)
            skipProgressBar.fillAmount = 0f;
    }

    // ---------- Поиск ссылок / саморемонт ----------

    void AutoFindReferences()
    {
        if (videoPlayer == null)
            videoPlayer = FindPlayerByNames("player", "VideoPlayer", "VideoDisplay", "VideoImage");
        if (videoPlayer == null)
        {
            VideoPlayer[] all = FindObjectsOfType<VideoPlayer>(true);
            if (all.Length > 0) videoPlayer = all[0];
        }

        if (videoImage == null)
        {
            GameObject disp = FindByNames("VideoDisplay", "VideoImage");
            if (disp != null) videoImage = disp.GetComponent<RawImage>();
        }
        if (videoImage == null && videoPlayer != null)
            videoImage = videoPlayer.GetComponent<RawImage>();

        if (videoRoot == null)
            videoRoot = videoImage != null ? videoImage.gameObject : FindByNames("VideoPanel", "VideoDisplay", "VideoImage");
        if (phonRoot == null)
            phonRoot = FindByNames("PhonePanel", "Phon");
        if (photo1Root == null)
            photo1Root = FindByNames("PhotoSlot_1", "Phtoto1", "Photo1");
        if (photo2Root == null)
            photo2Root = FindByNames("PhotoSlot_2", "Photo2");
        if (textBackground == null)
            textBackground = FindByNames("TextContainer", "Phontexta");

        if (skipHint == null)
            skipHint = FindByName("SkipHint");
        if (skipProgressBar == null)
        {
            GameObject bar = FindByName("SkipBar");
            if (bar != null) skipProgressBar = bar.GetComponent<Image>();
        }
    }

    /// <summary>Ищет VideoPlayer сначала внутри объектов с такими именами, потом anywhere.</summary>
    static VideoPlayer FindPlayerByNames(params string[] names)
    {
        foreach (string n in names)
        {
            GameObject go = FindByName(n);
            if (go == null) continue;
            VideoPlayer p = go.GetComponent<VideoPlayer>();
            if (p != null) return p;
            p = go.GetComponentInChildren<VideoPlayer>(true);
            if (p != null) return p;
        }
        return null;
    }

    static GameObject FindByNames(params string[] names)
    {
        foreach (string n in names)
        {
            GameObject go = FindByName(n);
            if (go != null) return go;
        }
        return null;
    }

    static GameObject FindByName(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go != null) return go;
        // Поиск и по неактивным объектам
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        foreach (Transform t in all)
        {
            if (t.name == name && t.gameObject.scene.IsValid())
                return t.gameObject;
        }
        return null;
    }

    /// <summary>Создаёт SkipHint + SkipBar внизу экрана, если их нет в сцене.</summary>
    void EnsureSkipUI()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null) return;

        if (skipHint == null)
        {
            GameObject hint = new GameObject("SkipHint");
            hint.transform.SetParent(canvas.transform, false);
            RectTransform rt = hint.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 34f);
            rt.sizeDelta = new Vector2(500f, 30f);
            Text txt = hint.AddComponent<Text>();
            txt.text = "Удерживайте ESC для пропуска";
            txt.alignment = TextAnchor.MiddleCenter;
            txt.font = GetBuiltinFont();
            txt.fontSize = 22;
            txt.color = new Color(1f, 1f, 1f, 0.85f);
            txt.raycastTarget = false;
            skipHint = hint;
        }

        if (skipProgressBar == null)
        {
            GameObject bg = new GameObject("SkipBarBG");
            bg.transform.SetParent(canvas.transform, false);
            RectTransform bgRt = bg.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0.5f, 0f);
            bgRt.anchorMax = new Vector2(0.5f, 0f);
            bgRt.pivot = new Vector2(0.5f, 0f);
            bgRt.anchoredPosition = new Vector2(0f, 14f);
            bgRt.sizeDelta = new Vector2(320f, 12f);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(1f, 1f, 1f, 0.25f);
            bgImg.raycastTarget = false;

            GameObject bar = new GameObject("SkipBar");
            bar.transform.SetParent(bg.transform, false);
            RectTransform barRt = bar.AddComponent<RectTransform>();
            barRt.anchorMin = Vector2.zero;
            barRt.anchorMax = Vector2.one;
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;
            Image barImg = bar.AddComponent<Image>();
            barImg.color = Color.white;
            barImg.type = Image.Type.Filled;
            barImg.fillMethod = Image.FillMethod.Horizontal;
            barImg.fillOrigin = (int)Image.OriginHorizontal.Left;
            barImg.fillAmount = 0f;
            barImg.raycastTarget = false;
            skipProgressBar = barImg;
        }
    }

    /// <summary>Чинит типовые баги сцены: нулевой масштаб Canvas, прозрачное видео.</summary>
    void RepairDisplayIssues()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas != null)
        {
            if (!canvas.enabled) canvas.enabled = true;
            RectTransform crt = canvas.GetComponent<RectTransform>();
            canvasRect = crt;
            if (crt != null && crt.localScale.magnitude < 0.01f)
            {
                crt.localScale = Vector3.one;
                Log("[Intro] Починен масштаб Canvas (был 0 — ничего не было бы видно).");
            }
        }

        if (videoImage != null)
        {
            Color c = videoImage.color;
            if (c.a < 0.01f)
            {
                c.a = 1f;
                videoImage.color = c;
            }
            videoImage.raycastTarget = false;
        }
    }

    void CacheGroups()
    {
        photo1Group = GetOrAddGroup(photo1Root);
        photo2Group = GetOrAddGroup(photo2Root);
        textBgGroup = textBackground != null ? GetOrAddGroup(textBackground) : null;
    }

    static CanvasGroup GetOrAddGroup(GameObject go)
    {
        if (go == null) return null;
        // Phon (общий фон) не трогаем — fade ему не нужен, он просто видимый.
        if (go.name == "Phon" || go.name == "PhonePanel") return go.GetComponent<CanvasGroup>();
        CanvasGroup g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        return g;
    }

    static void SetAlpha(CanvasGroup g, float a)
    {
        if (g != null) g.alpha = a;
    }

    static void SetActiveSafe(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active)
            go.SetActive(active);
    }

    /// <summary>
    /// Встроенный шрифт для legacy Text. Arial.ttf выпилили из Unity —
    /// остался LegacyRuntime.ttf. Без try/catch исключение роняло весь Awake.
    /// </summary>
    static Font GetBuiltinFont()
    {
        try
        {
            Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) return f;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Intro] Не нашёлся встроенный шрифт: {e.Message}");
        }
        return null;
    }

    void Log(string msg)
    {
        if (debugLogs) Debug.Log(msg, this);
    }
}
