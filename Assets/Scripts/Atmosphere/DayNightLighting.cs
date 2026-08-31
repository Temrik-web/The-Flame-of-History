using System.Collections;
using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Освещение суточного цикла: солнце, луна, ambient и небо.
    ///
    /// ЗАЧЕМ:
    /// Туман сам по себе не делает ночь. Раньше система только красила туман,
    /// а сцена оставалась дневной — из-за этого днём туман выглядел плотной
    /// белой пеленой при ярком солнце, а ночью не было темноты.
    /// Этот компонент двигает направленный свет по небу, гасит его на закате,
    /// поднимает луну и снижает ambient — сцена реально становится ночной.
    ///
    /// Время берётся из FogSystem, поэтому свет и туман всегда синхронны.
    /// Пост-обработка не используется: только Light, RenderSettings и ambient.
    ///
    /// Размещение: на том же объекте, что FogSystem.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WWII/Atmosphere/Day Night Lighting")]
    public class DayNightLighting : MonoBehaviour
    {
        // =============================================================
        //  Источники света
        // =============================================================
        [Header("Источники света")]
        [Tooltip("Основной направленный свет — солнце. Обязателен.")]
        [SerializeField] private Light sunLight;

        [Tooltip("Второй направленный свет — луна. Необязателен: если пусто, ночью используется солнце с лунными настройками.")]
        [SerializeField] private Light moonLight;

        [Tooltip("Вращать источники света по времени суток. Выключить, если светом управляет другая система.")]
        [SerializeField] private bool driveRotation = true;

        [Tooltip("Азимут восхода, градусы. Задаёт, с какой стороны встаёт солнце.")]
        [SerializeField, Range(0f, 360f)] private float sunAzimuth = 130f;

        // =============================================================
        //  Расписание суток
        // =============================================================
        [Header("Расписание суток (часы)")]
        [Tooltip("Час восхода солнца.")]
        [SerializeField, Range(0f, 24f)] private float sunriseHour = 6f;

        [Tooltip("Час заката.")]
        [SerializeField, Range(0f, 24f)] private float sunsetHour = 20f;

        [Tooltip("Длительность сумерек в часах. Плавный переход день↔ночь.")]
        [SerializeField, Range(0.1f, 4f)] private float twilightDuration = 1.2f;

        // =============================================================
        //  Солнце
        // =============================================================
        [Header("Солнце")]
        [Tooltip("Интенсивность солнца в полдень.")]
        [SerializeField, Range(0f, 5f)] private float sunPeakIntensity = 1.25f;

        [Tooltip("Цвет солнца в полдень.")]
        [SerializeField] private Color sunNoonColor = new Color(1f, 0.97f, 0.91f);

        [Tooltip("Цвет солнца на восходе и закате — тёплый, низкий свет.")]
        [SerializeField] private Color sunHorizonColor = new Color(1f, 0.68f, 0.42f);

        // =============================================================
        //  Луна
        // =============================================================
        [Header("Луна")]
        [Tooltip("Интенсивность луны в зенит. WWII-ночь должна быть тёмной: 0.05–0.15.")]
        [SerializeField, Range(0f, 1f)] private float moonPeakIntensity = 0.09f;

        [Tooltip("Цвет лунного света — холодный синеватый.")]
        [SerializeField] private Color moonColor = new Color(0.55f, 0.66f, 0.9f);

        [Tooltip("Отбрасывать тени от луны. Выключено — заметно дешевле.")]
        [SerializeField] private bool moonCastsShadows = false;

        // =============================================================
        //  Окружающее освещение
        // =============================================================
        [Header("Окружающее освещение (ambient)")]
        [Tooltip("Управлять ambient-светом сцены. Именно он делает ночь тёмной, а не только цвет тумана.")]
        [SerializeField] private bool controlAmbient = true;

        [Tooltip("Ambient в полдень.")]
        [SerializeField] private Color dayAmbient = new Color(0.42f, 0.45f, 0.5f);

        [Tooltip("Ambient в сумерках.")]
        [SerializeField] private Color twilightAmbient = new Color(0.2f, 0.2f, 0.26f);

        [Tooltip("Ambient глухой ночью. Держите очень низким — иначе ночи не будет.")]
        [SerializeField] private Color nightAmbient = new Color(0.045f, 0.055f, 0.085f);

        [Tooltip("Общий множитель ambient. Снизить, если ночь всё ещё светлая.")]
        [SerializeField, Range(0f, 2f)] private float ambientMultiplier = 1f;

        // =============================================================
        //  Небо
        // =============================================================
        [Header("Небо")]
        [Tooltip("Затемнять skybox ночью через экспозицию материала. Требует, чтобы материал неба имел свойство _Exposure.")]
        [SerializeField] private bool controlSkyboxExposure = true;

        [Tooltip("Экспозиция неба днём.")]
        [SerializeField, Range(0f, 4f)] private float dayExposure = 1.1f;

        [Tooltip("Экспозиция неба ночью.")]
        [SerializeField, Range(0f, 2f)] private float nightExposure = 0.16f;

        // =============================================================
        //  Дневная видимость
        // =============================================================
        [Header("Дневная видимость")]
        [Tooltip("Гасить туман днём дополнительно к расписанию FogSystem. Решает проблему 'днём плохо видно'.")]
        [SerializeField] private bool clearFogInDaylight = true;

        [Tooltip("Во сколько раз ослабить туман в полдень. 0 — полностью убрать.")]
        [SerializeField, Range(0f, 1f)] private float daylightFogScale = 0.12f;

        // =============================================================
        //  Производительность
        // =============================================================
        [Header("Производительность")]
        [Tooltip("Интервал обновления освещения, сек. Свет меняется медленно, часто пересчитывать не нужно.")]
        [SerializeField, Range(0.05f, 1f)] private float updateInterval = 0.15f;

        [Tooltip("Скорость сглаживания переходов освещения. Меньше — плавнее.")]
        [SerializeField, Range(0.2f, 10f)] private float smoothing = 2.5f;

        [Header("Отладка")]
        [Tooltip("Показывать фазу суток в консоли при смене.")]
        [SerializeField] private bool logPhaseChanges = false;

        // =============================================================
        //  Состояние
        // =============================================================
        private static DayNightLighting instance;

        private float sunFactor;      // 0..1 — насколько солнце над горизонтом
        private float nightFactor;    // 0..1 — насколько сейчас ночь
        private float smoothedSun;
        private float smoothedNight;
        private Material skyboxMaterial;
        private int exposurePropertyId;
        private bool skyboxSupportsExposure;
        private bool wasNight;
        private Coroutine tickRoutine;

        /// <summary>Единственный экземпляр в сцене.</summary>
        public static DayNightLighting Instance => instance;

        /// <summary>0 — солнце под горизонтом, 1 — солнце в зените.</summary>
        public float SunFactor => smoothedSun;

        /// <summary>0 — день, 1 — глухая ночь. Используется InteriorDarkness.</summary>
        public float NightFactor => smoothedNight;

        /// <summary>Ночь ли сейчас (порог 0.5).</summary>
        public bool IsNight => smoothedNight > 0.5f;

        /// <summary>
        /// Дополнительный множитель плотности тумана от освещённости.
        /// Днём туман ослабляется, чтобы не мешать видимости.
        /// </summary>
        public float FogDensityScale { get; private set; } = 1f;

        // =============================================================
        //  Жизненный цикл
        // =============================================================
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[DayNightLighting] В сцене уже есть DayNightLighting. Лишний компонент отключён.", this);
                enabled = false;
                return;
            }

            instance = this;

            ResolveSkybox();
            ConfigureLights();
        }

        private void OnEnable()
        {
            if (instance != this) return;

            // Забираем управление направленным светом в тумане у FogSystem,
            // чтобы два скрипта не перетирали одно глобальное свойство.
            if (FogSystem.Instance != null)
                FogSystem.Instance.ClaimSunControl(true);

            // Первый расчёт без сглаживания: сцена сразу в правильном состоянии.
            Evaluate();
            smoothedSun = sunFactor;
            smoothedNight = nightFactor;
            Apply();

            tickRoutine = StartCoroutine(TickLoop());
        }

        private void Start()
        {
            // FogSystem мог инициализироваться позже.
            if (FogSystem.Instance != null)
                FogSystem.Instance.ClaimSunControl(true);
        }

        private void OnDisable()
        {
            if (tickRoutine != null)
            {
                StopCoroutine(tickRoutine);
                tickRoutine = null;
            }

            if (FogSystem.Instance != null)
                FogSystem.Instance.ClaimSunControl(false);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Единственный цикл. Освещение меняется медленно, поэтому
        /// обновление раз в 0.15 с визуально неотличимо от покадрового.
        /// </summary>
        private IEnumerator TickLoop()
        {
            while (true)
            {
                Evaluate();

                float step = smoothing * updateInterval;
                smoothedSun = Mathf.MoveTowards(smoothedSun, sunFactor, step);
                smoothedNight = Mathf.MoveTowards(smoothedNight, nightFactor, step);

                Apply();

                yield return new WaitForSeconds(updateInterval);
            }
        }

        // =============================================================
        //  Расчёт фазы суток
        // =============================================================
        /// <summary>
        /// Вычислить положение солнца и степень ночи по времени из FogSystem.
        /// </summary>
        private void Evaluate()
        {
            float hour = FogSystem.Instance != null ? FogSystem.Instance.TimeOfDay : 12f;

            // Полная длительность светового дня.
            float dayLength = Mathf.Repeat(sunsetHour - sunriseHour, 24f);
            if (dayLength < 0.1f) dayLength = 12f;

            // Позиция внутри светового дня: 0 — восход, 1 — закат.
            float sinceSunrise = Mathf.Repeat(hour - sunriseHour, 24f);
            bool isDaytime = sinceSunrise <= dayLength;

            if (isDaytime)
            {
                float dayProgress = sinceSunrise / dayLength;

                // Синус даёт естественную высоту солнца: максимум в середине дня.
                sunFactor = Mathf.Sin(dayProgress * Mathf.PI);

                // Ночь угасает в течение сумерек после восхода.
                float twilightFraction = Mathf.Clamp01(twilightDuration / dayLength);
                float fromEdge = Mathf.Min(dayProgress, 1f - dayProgress);
                nightFactor = 1f - Mathf.Clamp01(fromEdge / Mathf.Max(twilightFraction, 0.001f));
            }
            else
            {
                sunFactor = 0f;

                // Ночью degree растёт к середине ночи и спадает к восходу,
                // но не опускается ниже 0.85 — ночь остаётся тёмной.
                float nightLength = 24f - dayLength;
                float sinceSunset = Mathf.Repeat(hour - sunsetHour, 24f);
                float nightProgress = Mathf.Clamp01(sinceSunset / Mathf.Max(nightLength, 0.1f));

                float twilightFraction = Mathf.Clamp01(twilightDuration / Mathf.Max(nightLength, 0.1f));
                float fromEdge = Mathf.Min(nightProgress, 1f - nightProgress);
                nightFactor = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(fromEdge / Mathf.Max(twilightFraction, 0.001f)));
                nightFactor = Mathf.Max(nightFactor, 0.4f);
            }

            sunFactor = Mathf.Clamp01(sunFactor);
            nightFactor = Mathf.Clamp01(nightFactor);

            if (logPhaseChanges)
            {
                bool night = nightFactor > 0.5f;
                if (night != wasNight)
                {
                    wasNight = night;
                    Debug.Log($"[DayNightLighting] {(night ? "Наступила ночь" : "Наступил день")} " +
                              $"в {FogSystem.FormatTime(hour)}");
                }
            }
        }

        // =============================================================
        //  Применение
        // =============================================================
        /// <summary>Применить рассчитанное состояние к свету, ambient и небу.</summary>
        private void Apply()
        {
            ApplySun();
            ApplyMoon();
            ApplyAmbient();
            ApplySkybox();
            ApplyFogScale();
            PushFogSun();
        }

        /// <summary>Настроить солнце: угол, цвет, интенсивность.</summary>
        private void ApplySun()
        {
            if (sunLight == null) return;

            if (driveRotation)
            {
                // Угол над горизонтом: отрицательный ночью, +78° в полдень.
                float elevation = Mathf.Lerp(-12f, 78f, smoothedSun);
                sunLight.transform.rotation = Quaternion.Euler(elevation, sunAzimuth, 0f);
            }

            // Цвет: у горизонта тёплый, в зените нейтральный.
            float horizonWeight = 1f - Mathf.Clamp01(smoothedSun * 2.2f);
            sunLight.color = Color.Lerp(sunNoonColor, sunHorizonColor, horizonWeight);

            // Интенсивность падает быстрее, чем высота: сумерки короткие.
            float intensity = sunPeakIntensity * Mathf.Pow(smoothedSun, 0.7f);
            sunLight.intensity = intensity;

            // Полностью гасим источник ночью — не считаем лишний свет и тени.
            bool sunVisible = intensity > 0.01f;
            if (sunLight.enabled != sunVisible)
                sunLight.enabled = sunVisible;

            // Тени только когда солнце достаточно высоко: у горизонта они
            // растянуты, дороги и выглядят плохо.
            sunLight.shadows = smoothedSun > 0.08f ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>Настроить луну. Если отдельного источника нет — ничего не делаем.</summary>
        private void ApplyMoon()
        {
            if (moonLight == null) return;

            if (driveRotation)
            {
                // Луна противоположна солнцу: её высота растёт с ночью.
                float elevation = Mathf.Lerp(-10f, 62f, smoothedNight);
                moonLight.transform.rotation = Quaternion.Euler(elevation, sunAzimuth + 180f, 0f);
            }

            moonLight.color = moonColor;
            moonLight.intensity = moonPeakIntensity * smoothedNight;

            bool moonVisible = moonLight.intensity > 0.005f;
            if (moonLight.enabled != moonVisible)
                moonLight.enabled = moonVisible;

            moonLight.shadows = moonCastsShadows && moonVisible ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>
        /// Ambient — главный инструмент темноты. Без его снижения ночь
        /// остаётся светлой независимо от направленного света.
        /// </summary>
        private void ApplyAmbient()
        {
            if (!controlAmbient) return;

            Color ambient;

            if (smoothedNight <= 0.5f)
            {
                // День → сумерки
                float k = smoothedNight * 2f;
                ambient = Color.Lerp(dayAmbient, twilightAmbient, k);
            }
            else
            {
                // Сумерки → глухая ночь
                float k = (smoothedNight - 0.5f) * 2f;
                ambient = Color.Lerp(twilightAmbient, nightAmbient, k);
            }

            ambient *= ambientMultiplier;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;

            // Отражения тоже гасим, иначе металл и стёкла светятся ночью.
            RenderSettings.reflectionIntensity = Mathf.Lerp(1f, 0.15f, smoothedNight);
        }

        /// <summary>Затемнить небо ночью через экспозицию skybox-материала.</summary>
        private void ApplySkybox()
        {
            if (!controlSkyboxExposure || !skyboxSupportsExposure || skyboxMaterial == null) return;

            float exposure = Mathf.Lerp(dayExposure, nightExposure, smoothedNight);
            skyboxMaterial.SetFloat(exposurePropertyId, exposure);
        }

        /// <summary>
        /// Ослабить туман днём. Даже при плотном тумане по расписанию
        /// солнечный день должен оставаться проглядываемым.
        /// </summary>
        private void ApplyFogScale()
        {
            if (!clearFogInDaylight)
            {
                FogDensityScale = 1f;
                return;
            }

            // Днём (night≈0) масштаб = daylightFogScale, ночью = 1.
            FogDensityScale = Mathf.Lerp(daylightFogScale, 1f, smoothedNight);

            if (FogSystem.Instance != null)
                FogSystem.Instance.SetEnvironmentDensityScale(FogDensityScale);
        }

        /// <summary>
        /// Передать в шейдеры тумана актуальный направленный свет.
        /// Ночью это луна, днём — солнце: туман рассеивает то, что светит.
        /// </summary>
        private void PushFogSun()
        {
            Light active = null;
            float weight = 1f;

            if (moonLight != null && smoothedNight > 0.5f && moonLight.intensity > 0.001f)
            {
                active = moonLight;
                // Лунное рассеивание усиливаем: физическая интенсивность луны
                // крошечная, но визуально туман должен серебриться. Однако не
                // слишком сильно — раньше множитель 6 делал весь ночной туман
                // ярко-голубым, и дома вдали «светились» в темноте.
                weight = 2f;
            }
            else if (sunLight != null && sunLight.intensity > 0.001f)
            {
                active = sunLight;
                weight = 0.4f;
            }

            if (active == null)
            {
                FogGlobals.SetSun(Color.white, 0f, Vector3.down);
                return;
            }

            FogGlobals.SetSun(active.color, active.intensity * weight, active.transform.forward);
        }

        // =============================================================
        //  Инициализация
        // =============================================================
        /// <summary>Привести источники света к ожидаемому типу и настройкам.</summary>
        private void ConfigureLights()
        {
            if (sunLight == null)
            {
                Debug.LogWarning("[DayNightLighting] Солнце не назначено. " +
                                 "Укажите направленный свет в поле Sun Light.", this);
            }
            else
            {
                sunLight.type = LightType.Directional;
            }

            if (moonLight != null)
            {
                moonLight.type = LightType.Directional;
                moonLight.shadows = moonCastsShadows ? LightShadows.Soft : LightShadows.None;
            }
        }

        /// <summary>
        /// Найти материал неба и проверить, есть ли у него свойство _Exposure.
        /// Материал клонируется, чтобы не изменять ассет на диске.
        /// </summary>
        private void ResolveSkybox()
        {
            if (!controlSkyboxExposure) return;

            Material source = RenderSettings.skybox;
            if (source == null)
            {
                skyboxSupportsExposure = false;
                return;
            }

            exposurePropertyId = Shader.PropertyToID("_Exposure");
            skyboxSupportsExposure = source.HasProperty(exposurePropertyId);

            if (!skyboxSupportsExposure)
            {
                Debug.Log("[DayNightLighting] У материала неба нет свойства _Exposure — " +
                          "затемнение skybox отключено. Ночь всё равно будет тёмной за счёт ambient.");
                return;
            }

            // Клон, чтобы правки экспозиции не сохранялись в ассет.
            skyboxMaterial = new Material(source) { name = source.name + " (DayNight)" };
            RenderSettings.skybox = skyboxMaterial;
        }

        // =============================================================
        //  Публичное API
        // =============================================================
        /// <summary>Мгновенно пересчитать и применить освещение (без сглаживания).</summary>
        public void SnapToCurrentTime()
        {
            Evaluate();
            smoothedSun = sunFactor;
            smoothedNight = nightFactor;
            Apply();
        }

        /// <summary>Задать множитель ambient в рантайме (например, для катсцены).</summary>
        public void SetAmbientMultiplier(float multiplier)
        {
            ambientMultiplier = Mathf.Clamp(multiplier, 0f, 2f);
        }

        // =============================================================
        //  Редактор
        // =============================================================
        private void OnValidate()
        {
            if (Mathf.Approximately(sunriseHour, sunsetHour))
                sunsetHour = sunriseHour + 12f;

            if (Application.isPlaying && instance == this)
                SnapToCurrentTime();
        }
    }
}
