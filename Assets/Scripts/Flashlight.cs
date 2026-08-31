using System.Collections;
using UnityEngine;

/// <summary>
/// Фонарик игрока в стиле WWII.
/// Один Spot Light на камере: тёплый свет, плавное включение,
/// лёгкое покачивание при ходьбе и редкое едва заметное мерцание.
/// Вешается на игрока (или на камеру), свет назначается через инспектор.
/// </summary>
[DisallowMultipleComponent]
public class Flashlight : MonoBehaviour
{
    [Header("Инвентарь")]
    [Tooltip("Есть ли фонарик у игрока. Если включён Auto Detect From Inventory, " +
             "значение выставляется автоматически по наличию предмета в сумке.")]
    [SerializeField] private bool hasFlashlight = false;

    [Tooltip("Брать наличие фонарика из InventorySystem по Item Id. " +
             "Выключи, если хочешь управлять флагом вручную из своего кода.")]
    [SerializeField] private bool autoDetectFromInventory = true;

    [Tooltip("Item Id ассета ItemData фонарика. Должен совпадать с полем Item Id " +
             "(или с именем ассета, если Item Id пустой).")]
    [SerializeField] private string flashlightItemId = "flashlight";

    [Tooltip("Клавиша включения/выключения.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F;

    [Tooltip("Не реагировать на клавишу, пока открыт инвентарь или идёт диалог.")]
    [SerializeField] private bool blockWhileUIOpen = true;

    [Header("Источник света")]
    [Tooltip("Spot Light — дочерний объект камеры. Если пусто, будет найден в дочерних объектах.")]
    [SerializeField] private Light spotLight;

    [Tooltip("Трансформ, по смещению которого считается покачивание (обычно сам игрок).")]
    [SerializeField] private Transform movementSource;

    [Header("Параметры луча")]
    [Tooltip("Рабочая интенсивность включённого фонарика. " +
             "Высокая из-за ACES-тонмаппинга, который сжимает яркость.")]
    [SerializeField] private float baseIntensity = 40f;
    [Tooltip("Полный угол конуса (внешний край), градусы.")]
    [SerializeField, Range(20f, 80f)] private float spotAngle = 55f;
    [Tooltip("Дальность луча, метры.")]
    [SerializeField] private float range = 45f;
    [Tooltip("Тёплый цвет лампы накаливания.")]
    [SerializeField] private Color lightColor = new Color(1f, 0.87f, 0.7f, 1f);
    [Tooltip("Цветовая температура (2700–3000K — тёплая лампа).")]
    [SerializeField, Range(1500f, 6500f)] private float colorTemperature = 2850f;

    [Header("Очень мягкие тени")]
    [Tooltip("Включать мягкие тени у фонарика.")]
    [SerializeField] private bool castShadows = true;
    [Tooltip("Сила тени: чем ниже, тем тени мягче и светлее " +
             "(0 = вообще без заметных теней).")]
    [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.35f;

    [Header("Реалистичный луч")]
    [Tooltip("Внутренний конус (горячее пятно). 0 = весь конус одинаковый, " +
             "1 = узкий центральный hotspot с плавным затуханием к краю.")]
    [SerializeField, Range(0f, 1f)] private float innerConeBlend = 0.55f;
    [Tooltip("Мягкость внешнего края конуса. 0 = жёсткая граница, " +
             "1 = полностью размытый край.")]
    [SerializeField, Range(0f, 1f)] private float outerSoftness = 0.7f;
    [Tooltip("Разрешение procedural-куки (256 — достаточно, 512 — детальнее).")]
    [SerializeField, Range(64, 512)] private int cookieResolution = 256;

    [Header("Ближняя засветка")]
    [Tooltip("Ниже этой дистанции яркость плавно гасится, чтобы в упор не слепило.")]
    [SerializeField] private float closeRangeStart = 2.5f;
    [Tooltip("Дистанция минимальной яркости (вплотную к предмету).")]
    [SerializeField] private float closeRangeEnd = 0.6f;
    [Tooltip("Во сколько раз гасится яркость при выставлении вплотную (0.015 = 1.5% от рабочей). " +
             "Физически поднесённый к поверхности свет слепит сильнее из-за обратных квадратов, " +
             "поэтому вплотную яркость нужно гасить почти до нуля: база 40 × 0.015 = 0.6 — " +
             "ниже порога блума (1.0), стену не слепит.")]
    [SerializeField, Range(0f, 1f)] private float closeRangeMinFactor = 0.015f;
    [Tooltip("Слои, которые считаются препятствием для расчёта засветки. " +
             "Слой предметов в руках исключается автоматически.")]
    [SerializeField] private LayerMask closeRangeMask = ~0;

    [Header("Плавность включения")]
    [Tooltip("Время выхода на полную яркость, сек.")]
    [SerializeField] private float fadeInTime = 0.35f;
    [Tooltip("Время затухания, сек.")]
    [SerializeField] private float fadeOutTime = 0.2f;

    [Header("Покачивание луча")]
    [Tooltip("Амплитуда постоянного дрожания в покое, градусы.")]
    [SerializeField] private float idleSwayAngle = 0.25f;
    [Tooltip("Дополнительная амплитуда при движении, градусы.")]
    [SerializeField] private float moveSwayAngle = 1.6f;
    [Tooltip("Скорость шума покачивания.")]
    [SerializeField] private float swaySpeed = 1.4f;
    [Tooltip("Сглаживание возврата луча.")]
    [SerializeField] private float swaySmooth = 8f;

    [Header("Мерцание («уставший» свет)")]
    [Tooltip("Включить редкое мерцание.")]
    [SerializeField] private bool useFlicker = true;
    [Tooltip("Минимальная пауза между мерцаниями, сек.")]
    [SerializeField] private float flickerIntervalMin = 9f;
    [Tooltip("Максимальная пауза между мерцаниями, сек.")]
    [SerializeField] private float flickerIntervalMax = 22f;
    [Tooltip("Насколько глубоко просаживается яркость (0.15 = -15%).")]
    [SerializeField, Range(0f, 0.6f)] private float flickerDepth = 0.18f;
    [Tooltip("Амплитуда медленной неравномерности яркости.")]
    [SerializeField, Range(0f, 0.3f)] private float unevenAmount = 0.08f;

    [Header("Пыль в луче (необязательно)")]
    [Tooltip("Партикл-система пыли, включается вместе со светом.")]
    [SerializeField] private ParticleSystem dustParticles;

    [Header("Звук (файлы можно назначить позже)")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Клик при включении.")]
    [SerializeField] private AudioClip clickOnClip;
    [Tooltip("Клик при выключении.")]
    [SerializeField] private AudioClip clickOffClip;
    [Tooltip("Тихий гул работающего фонарика (луп).")]
    [SerializeField] private AudioClip humLoopClip;
    [SerializeField, Range(0f, 1f)] private float clickVolume = 0.8f;
    [SerializeField, Range(0f, 1f)] private float humVolume = 0.15f;

    // --- внутреннее состояние ---
    private bool isOn;                  // логическое состояние выключателя
    private float fadeFactor;           // 0..1 — плавное включение
    private float flickerFactor = 1f;   // множитель мерцания
    private float closeRangeFactor = 1f;// снижение яркости вплотную к предмету
    private Quaternion baseRotation;    // исходный поворот света
    private Vector3 lastPosition;       // для оценки скорости движения
    private float noiseSeed;

    // Слои, которые не должны подсвечиваться фонариком (предметы в руках).
    // Выставляются в Awake отдельно от маски близкой засветки.
    private int heldObjectsLayer = -1;

    // Процедурная кука: одна текстура на все экземпляры фонарика.
    private static Texture2D sharedCookie;

    private Coroutine fadeRoutine;
    private Coroutine flickerRoutine;
    private Coroutine swayRoutine;

    /// <summary>Горит ли фонарик сейчас.</summary>
    public bool IsOn => isOn;

    private void Awake()
    {
        if (spotLight == null)
            spotLight = FindOrCreateLight();

        if (movementSource == null)
            movementSource = transform;

        heldObjectsLayer = LayerMask.NameToLayer("HeldObjects");

        noiseSeed = Random.value * 100f;
        lastPosition = movementSource.position;

        SetupLight();
        SetupLightCullingMask();
        ApplyIntensity();
    }

    /// <summary>
    /// Не подсвечивать фонариком предметы в руках (оружие, нож, граната) —
    /// иначе модель в камере заливается ослепляющим светом. Для этого настраиваем
    /// маску рендера света (cullingMask): выкидываем слой HeldObjects.
    /// </summary>
    private void SetupLightCullingMask()
    {
        if (spotLight == null || heldObjectsLayer < 0) return;

        int mask = spotLight.cullingMask;
        mask &= ~(1 << heldObjectsLayer);
        spotLight.cullingMask = mask;
    }

    /// <summary>
    /// Ищем Spot Light среди дочерних объектов, а если его нет — создаём сами
    /// отдельным объектом под камерой игрока. Иначе забытая ссылка в инспекторе
    /// выглядит как «фонарик не работает», хотя скрипт исправен.
    ///
    /// Свет создаётся как самостоятельный объект-держатель «FlashlightRoot»
    /// (а не размазанным компонентом камеры), чтобы его можно было позиционировать,
    /// отдельно анимировать неоновой кочергой/фонарём и навесить свои меши и звук.
    /// </summary>
    private Light FindOrCreateLight()
    {
        foreach (Light l in GetComponentsInChildren<Light>(true))
            if (l.type == LightType.Spot) return l;

        Camera cam = GetComponentInChildren<Camera>(true);
        if (cam == null) cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[Flashlight] Нет камеры — некуда крепить свет. " +
                           "Назначь Spot Light вручную.");
            return null;
        }

        // Единый корневой держатель фонарика. Если он уже присутствует в сцене
        // (например, вручную собран), переиспользуем его, а не плодим дубликаты.
        Transform existing = cam.transform.Find("FlashlightRoot");
        if (existing != null)
        {
            Light found = existing.GetComponent<Light>();
            if (found != null) return found;
        }

        var go = new GameObject("FlashlightRoot");
        go.transform.SetParent(cam.transform, false);
        // Ставим источник света перед камерой, как если бы игрок держал фонарик
        // в вытянутой руке: чуть ниже и правее центра кадра.
        go.transform.localPosition = new Vector3(0.22f, -0.18f, 0.4f);
        Light created = go.AddComponent<Light>();
        Debug.Log("[Flashlight] Spot Light не был назначен — создан отдельный объект " +
                  $"«{go.name}» под камерой {cam.name}.");
        return created;
    }

    /// <summary>Первичная настройка Spot Light из параметров инспектора.</summary>
    private void SetupLight()
    {
        if (spotLight == null)
        {
            Debug.LogWarning("[Flashlight] Не назначен Spot Light.");
            return;
        }

        spotLight.type = LightType.Spot;
        spotLight.spotAngle = spotAngle;
        spotLight.innerSpotAngle = spotAngle * Mathf.Lerp(1f, innerConeBlend, innerConeBlend);
        spotLight.range = range;
        spotLight.color = lightColor;
        spotLight.useColorTemperature = true;
        spotLight.colorTemperature = colorTemperature;

        // Очень мягкие тени: мягкий режим + низкая сила + повышенный normal bias,
        // чтобы границы теней были размытыми и не давали резких артефактов.
        if (castShadows)
        {
            spotLight.shadows = LightShadows.Soft;
            spotLight.shadowStrength = shadowStrength;
            spotLight.shadowBias = 0.1f;
            spotLight.shadowNormalBias = 0.9f;
        }
        else
        {
            spotLight.shadows = LightShadows.None;
        }

        spotLight.intensity = 0f;
        spotLight.enabled = false;

        // Предметы в руках не должны считаться препятствием для засветки:
        // оружие всегда перед камерой и без исключения гасило бы свет при прицеливании.
        if (heldObjectsLayer >= 0)
            closeRangeMask &= ~(1 << heldObjectsLayer);

        ApplyCookie();
        baseRotation = spotLight.transform.localRotation;
    }

    /// <summary>
    /// Процедурная текстура куки: центральное яркое пятно (hotspot) с плавным
    /// затуханием к краю и лёгкими «voluçãoными» дефектами отражателя.
    /// Одна текстура на все фонарики — не дублируем в памяти.
    /// </summary>
    private void ApplyCookie()
    {
        if (spotLight == null) return;

        int res = Mathf.Clamp(cookieResolution, 64, 512);
        if (sharedCookie == null || sharedCookie.width != res)
            sharedCookie = GenerateCookie(res);

        spotLight.cookie = sharedCookie;
        spotLight.cookieSize = 1f;
    }

    /// <summary>
    /// Генерирует текстуру куки. Радиальный градиент с hotspot-ядром,
    /// лёгкой неравномерностью (имитация дефектов рефлектора)
    /// и мягким внешним краем.
    /// </summary>
    private static Texture2D GenerateCookie(int resolution)
    {
        Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "FlashlightCookie"
        };

        float center = (resolution - 1) * 0.5f;
        float maxRadius = center;

        Color[] pixels = new Color[resolution * resolution];

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = (x - center) / maxRadius;
                float dy = (y - center) / maxRadius;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Внутренний hotspot: крутой обрыв ближе к центру
                float hotspot = Mathf.Clamp01(1f - dist * 1.8f);
                hotspot = hotspot * hotspot; // квадратичный — реалистичнее

                // Основной градиент конуса
                float beam = Mathf.Clamp01(1f - dist);
                beam = Mathf.Pow(beam, 1.5f);

                // Сумма: hotspot доминирует в центре, beam — по краям
                float value = Mathf.Max(hotspot * 0.95f, beam);

                // Лёгкая азимутальная неравномерность (дефекты отражателя)
                float angle = Mathf.Atan2(dy, dx);
                float ring = Mathf.Sin(angle * 3f + dist * 5f) * 0.04f
                           + Mathf.Sin(angle * 7f - dist * 3f) * 0.02f;
                value = Mathf.Clamp01(value + ring * (1f - dist));

                // Мягкий обрез на краю
                float edgeFade = Mathf.Clamp01((1f - dist) * 4f);
                value *= edgeFade;

                // Тёплый оттенок (R чуть больше GB — имитация лампы накаливания)
                float r = value;
                float g = value * 0.95f;
                float b = value * 0.85f;

                pixels[y * resolution + x] = new Color(r, g, b, 1f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);
        return tex;
    }

    // Единственный Update — опрос клавиши и синхронизация с инвентарём.
    private void Update()
    {
        if (autoDetectFromInventory)
            SyncWithInventory();

        if (blockWhileUIOpen && IsUIBlocking()) return;

        if (!Input.GetKeyDown(toggleKey)) return;

        if (!hasFlashlight)
        {
            // Подсказка вместо тишины: чаще всего «F не работает» — это
            // просто отсутствие предмета в сумке.
            Debug.Log($"[Flashlight] Фонарика нет в инвентаре (ищу Item Id \"{flashlightItemId}\").");
            return;
        }

        Toggle();
    }

    /// <summary>
    /// Наличие фонарика берём напрямую из инвентаря: предмет в сумке = фонарик есть.
    /// Так не нужно дописывать вызовы в InventorySystem.
    /// </summary>
    private void SyncWithInventory()
    {
        InventorySystem inv = InventorySystem.Instance;
        if (inv == null) return;

        bool has = inv.CountItemById(flashlightItemId) > 0;
        if (has != hasFlashlight)
            SetHasFlashlight(has);
    }

    /// <summary>Открыт инвентарь или активен диалог — клавиша игнорируется.</summary>
    private bool IsUIBlocking()
    {
        if (InventorySystem.Instance != null && InventorySystem.Instance.IsOpen)
            return true;

        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive)
            return true;

        return false;
    }

    // ---------- публичное API ----------

    /// <summary>Вызывается инвентарём при экипировке/снятии фонарика.</summary>
    public void SetHasFlashlight(bool value)
    {
        hasFlashlight = value;
        if (!hasFlashlight && isOn)
            TurnOff();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Заполнение ссылок из редакторного визарда. В рантайме не используется.
    /// </summary>
    public void ConfigureFromEditor(Light light, Transform movement, AudioSource audio, string itemId)
    {
        spotLight = light;
        movementSource = movement;
        audioSource = audio;
        flashlightItemId = itemId;
        autoDetectFromInventory = true;
    }
#endif

    /// <summary>Переключить состояние.</summary>
    public void Toggle()
    {
        if (isOn) TurnOff();
        else TurnOn();
    }

    public void TurnOn()
    {
        if (!hasFlashlight || isOn || spotLight == null) return;

        isOn = true;
        spotLight.enabled = true;

        PlayClick(true);
        StartHum();

        if (dustParticles != null) dustParticles.Play();

        RestartFade(1f, fadeInTime);

        if (swayRoutine == null) swayRoutine = StartCoroutine(SwayLoop());
        if (useFlicker && flickerRoutine == null) flickerRoutine = StartCoroutine(FlickerLoop());
    }

    public void TurnOff()
    {
        if (!isOn) return;

        isOn = false;

        PlayClick(false);
        StopHum();

        if (dustParticles != null) dustParticles.Stop();

        RestartFade(0f, fadeOutTime);

        if (flickerRoutine != null)
        {
            StopCoroutine(flickerRoutine);
            flickerRoutine = null;
        }
        flickerFactor = 1f;
    }

    // ---------- свет ----------

    private void RestartFade(float target, float duration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeTo(target, duration));
    }

    /// <summary>Плавный переход яркости к целевому значению.</summary>
    private IEnumerator FadeTo(float target, float duration)
    {
        float start = fadeFactor;

        if (duration <= 0f)
        {
            fadeFactor = target;
        }
        else
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                // сглаживание: лампа разогревается неравномерно
                fadeFactor = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
                ApplyIntensity();
                yield return null;
            }
            fadeFactor = target;
        }

        ApplyIntensity();

        // полностью гасим источник, чтобы не считать лишний свет
        if (Mathf.Approximately(fadeFactor, 0f) && spotLight != null)
            spotLight.enabled = false;

        fadeRoutine = null;
    }

    /// <summary>Итоговая яркость = база * плавность * мерцание * медленная неравномерность * близость.</summary>
    private void ApplyIntensity()
    {
        if (spotLight == null) return;

        float uneven = 1f;
        if (unevenAmount > 0f)
        {
            // медленный перлин-шум: свет «дышит», выглядит уставшим
            float n = Mathf.PerlinNoise(noiseSeed, Time.time * 0.35f);
            uneven = 1f + (n - 0.5f) * 2f * unevenAmount;
        }

        UpdateCloseRange();

        spotLight.intensity = baseIntensity * fadeFactor * flickerFactor * uneven * closeRangeFactor;
    }

    /// <summary>
    /// Реалистичное поведение вплотную: когда фонарик подносят почти к самой
    /// поверхности, ровное пятно света размывается и глаза не ослепляет так,
    /// как жёсткий близкий свет в игре.
    /// Используем SphereCast с радиусом для надёжного обнаружения близких поверхностей
    /// и SmoothStep для естественного затухания.
    /// </summary>
    private void UpdateCloseRange()
    {
        if (!isOn || spotLight == null)
        {
            closeRangeFactor = 1f;
            return;
        }

        Transform t = spotLight.transform;
        Vector3 origin = t.position;
        Vector3 dir = t.forward;

        // Небольшой радиус сферы, чтобы не промахиваться мимо близких объектов
        float sphereRadius = 0.15f;

        if (Physics.SphereCast(origin, sphereRadius, dir, out RaycastHit hit,
                               closeRangeStart, closeRangeMask, QueryTriggerInteraction.Ignore))
        {
            float d = hit.distance;
            float span = Mathf.Max(closeRangeStart - closeRangeEnd, 0.01f);
            float fade = Mathf.Clamp01((d - closeRangeEnd) / span);

            // SmoothStep делает переход более естественным и быстрым у краёв
            fade = Mathf.SmoothStep(0f, 1f, fade);
            closeRangeFactor = Mathf.Lerp(closeRangeMinFactor, 1f, fade);

            // Мы намеренно не трогаем range, чтобы не ломать затухание света
        }
        else
        {
            closeRangeFactor = 1f;
        }
    }

    /// <summary>Дрожание луча: шум + усиление при движении. Работает только пока фонарик включён.</summary>
    private IEnumerator SwayLoop()
    {
        while (isOn || fadeFactor > 0.001f)
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // оценка скорости по смещению трансформа
            Vector3 pos = movementSource.position;
            float speed = (pos - lastPosition).magnitude / dt;
            lastPosition = pos;

            float moveWeight = Mathf.Clamp01(speed / 4f); // 4 м/с ≈ бег
            float amplitude = idleSwayAngle + moveSwayAngle * moveWeight;

            float time = Time.time * swaySpeed;
            float x = (Mathf.PerlinNoise(noiseSeed, time) - 0.5f) * 2f * amplitude;
            float y = (Mathf.PerlinNoise(time, noiseSeed + 13.7f) - 0.5f) * 2f * amplitude;

            Quaternion target = baseRotation * Quaternion.Euler(x, y, 0f);
            spotLight.transform.localRotation = Quaternion.Slerp(
                spotLight.transform.localRotation, target, dt * swaySmooth);

            ApplyIntensity();
            yield return null;
        }

        // возврат в исходное положение
        spotLight.transform.localRotation = baseRotation;
        swayRoutine = null;
    }

    /// <summary>Редкое едва заметное мерцание — контакт в старом фонарике.</summary>
    private IEnumerator FlickerLoop()
    {
        while (isOn)
        {
            yield return new WaitForSeconds(Random.Range(flickerIntervalMin, flickerIntervalMax));

            int blinks = Random.Range(1, 4);
            for (int i = 0; i < blinks && isOn; i++)
            {
                float depth = flickerDepth * Random.Range(0.5f, 1f);
                float down = Random.Range(0.03f, 0.07f);
                float up = Random.Range(0.05f, 0.12f);

                yield return LerpFlicker(1f, 1f - depth, down);
                yield return LerpFlicker(1f - depth, 1f, up);

                if (blinks > 1)
                    yield return new WaitForSeconds(Random.Range(0.04f, 0.15f));
            }

            flickerFactor = 1f;
            ApplyIntensity();
        }

        flickerRoutine = null;
    }

    private IEnumerator LerpFlicker(float from, float to, float duration)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / Mathf.Max(duration, 0.0001f);
            flickerFactor = Mathf.Lerp(from, to, t);
            ApplyIntensity();
            yield return null;
        }
        flickerFactor = to;
    }

    // ---------- звук ----------

    /// <summary>Клик выключателя.</summary>
    private void PlayClick(bool on)
    {
        if (audioSource == null) return;

        AudioClip clip = on ? clickOnClip : clickOffClip;
        if (clip != null)
            audioSource.PlayOneShot(clip, clickVolume);
    }

    /// <summary>Запуск тихого гула на время работы.</summary>
    private void StartHum()
    {
        if (audioSource == null || humLoopClip == null) return;

        audioSource.clip = humLoopClip;
        audioSource.loop = true;
        audioSource.volume = humVolume;
        audioSource.Play();
    }

    private void StopHum()
    {
        if (audioSource == null || humLoopClip == null) return;
        audioSource.loop = false;
        audioSource.Stop();
    }

    // Живая правка параметров в инспекторе во время игры.
    private void OnValidate()
    {
        if (flickerIntervalMax < flickerIntervalMin)
            flickerIntervalMax = flickerIntervalMin;

        if (Application.isPlaying && spotLight != null)
        {
            spotLight.spotAngle = spotAngle;
            spotLight.innerSpotAngle = spotAngle * Mathf.Lerp(1f, innerConeBlend, innerConeBlend);
            spotLight.range = range;
            spotLight.color = lightColor;
            spotLight.colorTemperature = colorTemperature;
            ApplyCookie();
        }
    }
}