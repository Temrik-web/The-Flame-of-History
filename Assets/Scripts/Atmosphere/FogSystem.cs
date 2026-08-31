using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Основной контроллер атмосферного тумана WWII.
    /// Отвечает за:
    ///   * игровое время суток и расписание появления тумана;
    ///   * общую плотность и цвет (серо-голубой ночью, желтоватый утром);
    ///   * ветер, который двигает массы тумана;
    ///   * уровень качества (LOD) и связь с зонами FogVolume / эмиттерами FogParticles.
    ///
    /// Все тяжёлые вычисления вынесены в шейдер. Скрипт обновляет
    /// глобальные параметры с низкой частотой (по умолчанию 10 раз в секунду),
    /// поэтому нагрузка на CPU практически нулевая.
    ///
    /// Размещение: один пустой GameObject "FogSystem" в корне сцены.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WWII/Atmosphere/Fog System")]
    public class FogSystem : MonoBehaviour
    {
        // =============================================================
        //  Время суток
        // =============================================================
        [Header("Время суток")]
        [Tooltip("Использовать внутренние часы. Выключить, если временем управляет другая система через SetTimeOfDay().")]
        [SerializeField] private bool useInternalClock = true;

        [Tooltip("Текущее игровое время в часах (0–24). 3.5 = 03:30.")]
        [SerializeField, Range(0f, 24f)] private float timeOfDay = 3f;

        [Tooltip("Сколько игровых минут проходит за одну реальную секунду. 1 = игровые сутки за 24 реальные минуты.")]
        [SerializeField, Range(0f, 60f)] private float gameMinutesPerRealSecond = 1f;

        // =============================================================
        //  Расписание тумана
        // =============================================================
        [Header("Расписание тумана (часы)")]
        [Tooltip("Час, когда туман начинает появляться (вечер/ночь).")]
        [SerializeField, Range(0f, 24f)] private float fogStartHour = 21f;

        [Tooltip("Начало пика плотности. По ТЗ — 3:00.")]
        [SerializeField, Range(0f, 24f)] private float peakStartHour = 3f;

        [Tooltip("Конец пика плотности. По ТЗ — 5:00.")]
        [SerializeField, Range(0f, 24f)] private float peakEndHour = 5f;

        [Tooltip("Час полного рассеивания после восхода солнца.")]
        [SerializeField, Range(0f, 24f)] private float fogEndHour = 7.5f;

        [Tooltip("Длительность нарастания/спада в игровых минутах (15–20 по ТЗ).")]
        [SerializeField, Range(1f, 120f)] private float rampMinutes = 18f;

        [Header("Плотность")]
        [Tooltip("Плотность в предрассветный пик (3:00–5:00).")]
        [SerializeField, Range(0f, 1f)] private float peakDensity = 1f;

        [Tooltip("Базовая плотность в остальные ночные часы (до пика).")]
        [SerializeField, Range(0f, 1f)] private float nightDensity = 0.6f;

        [Tooltip("Остаточная дымка днём. 0 — туман полностью исчезает.")]
        [SerializeField, Range(0f, 0.3f)] private float dayResidual = 0f;

        [Tooltip("Скорость сглаживания изменений плотности. Меньше — плавнее.")]
        [SerializeField, Range(0.05f, 5f)] private float densitySmoothing = 0.5f;

        [Header("Ручное управление")]
        [Tooltip("Игнорировать расписание и держать плотность вручную.")]
        [SerializeField] private bool manualOverride = false;

        [Tooltip("Плотность при ручном управлении.")]
        [SerializeField, Range(0f, 1f)] private float manualDensity = 0.8f;

        // =============================================================
        //  Цвет
        // =============================================================
        [Header("Цвет тумана")]
        [Tooltip("Серо-голубой ночной цвет (глухая ночь). Держим тёмным, " +
                 "чтобы дальние дома не «синели» — они просто тонут в темноте.")]
        [SerializeField] private Color nightTint = new Color(0.18f, 0.21f, 0.26f);

        [Tooltip("Цвет в предрассветные часы — самый холодный и мёртвый.")]
        [SerializeField] private Color preDawnTint = new Color(0.16f, 0.19f, 0.24f);

        [Tooltip("Желтоватый утренний цвет (после восхода).")]
        [SerializeField] private Color morningTint = new Color(0.78f, 0.72f, 0.55f);

        [Tooltip("Яркость тумана. Ниже 1 — более мрачно и «тихо».")]
        [SerializeField, Range(0.2f, 2f)] private float tintBrightness = 0.6f;

        [Header("Лунный подсвет")]
        [Tooltip("Цвет свечения тумана в лунном свете.")]
        [SerializeField] private Color moonColor = new Color(0.55f, 0.66f, 0.85f);

        [Tooltip("Сила лунного свечения. Работает только ночью.")]
        [SerializeField, Range(0f, 1f)] private float moonIntensity = 0.22f;

        [Tooltip("Направленный свет луны/солнца. Используется для определения ночи. Необязательно.")]
        [SerializeField] private Light directionalLight;

        // =============================================================
        //  Ветер
        // =============================================================
        [Header("Ветер (движение масс тумана)")]
        [Tooltip("Направление ветра в плоскости XZ. Нормализуется автоматически.")]
        [SerializeField] private Vector2 windDirection = new Vector2(0.35f, 1f);

        [Tooltip("Скорость ветра, м/с. WWII-туман должен ползти очень медленно.")]
        [SerializeField, Range(0f, 3f)] private float windSpeed = 0.35f;

        [Tooltip("Амплитуда медленного изменения направления ветра, градусы.")]
        [SerializeField, Range(0f, 90f)] private float windWander = 25f;

        [Tooltip("Как быстро гуляет направление ветра.")]
        [SerializeField, Range(0.01f, 1f)] private float windWanderSpeed = 0.05f;

        // =============================================================
        //  Качество / LOD
        // =============================================================
        [Header("Качество и производительность")]
        [Tooltip("Уровень качества тумана. Low — для слабых систем.")]
        [SerializeField] private FogQuality quality = FogQuality.Medium;

        [Tooltip("Автоматически понизить качество, если FPS ниже порога.")]
        [SerializeField] private bool autoQuality = true;

        [Tooltip("Порог FPS, ниже которого качество снижается.")]
        [SerializeField, Range(15f, 60f)] private float autoQualityFpsThreshold = 35f;

        [Tooltip("Интервал обновления глобальных параметров, сек. 0.1 = 10 раз в секунду.")]
        [SerializeField, Range(0.02f, 0.5f)] private float updateInterval = 0.1f;

        [Tooltip("Максимальное общее число частиц тумана на сцене. Распределяется между эмиттерами.")]
        [SerializeField, Range(64, 4000)] private int globalParticleBudget = 900;

        // =============================================================
        //  Встроенный туман сцены (дальняя дымка без пост-обработки)
        // =============================================================
        [Header("Дальняя дымка (RenderSettings.fog)")]
        [Tooltip("Управлять встроенным туманом Unity — даёт дешёвую глубину на больших дистанциях.")]
        [SerializeField] private bool controlSceneFog = true;

        [Tooltip("Дистанция полной непроглядности при максимальной плотности, м.")]
        [SerializeField, Range(10f, 400f)] private float sceneFogEndDistanceAtPeak = 55f;

        [Tooltip("Дистанция полной непроглядности при нулевой плотности, м.")]
        [SerializeField, Range(50f, 2000f)] private float sceneFogEndDistanceClear = 500f;

        [Tooltip("Минимальная дистанция ясной видимости — чтобы туман не мешал видеть врагов.")]
        [SerializeField, Range(2f, 60f)] private float minimumClearVisibility = 14f;

        [Header("Отладка")]
        [Tooltip("Рисовать состояние системы в консоль при изменении фазы.")]
        [SerializeField] private bool logPhaseChanges = false;

        // =============================================================
        //  Состояние
        // =============================================================
        private static FogSystem instance;

        private readonly List<FogVolume> volumes = new List<FogVolume>(32);
        private readonly List<FogParticles> emitters = new List<FogParticles>(32);

        private float currentDensity;
        private float targetDensity;
        private Color currentTint;
        private Vector2 windOffset;
        private float animationTime;
        private float fpsAccumulator = 60f;
        private FogQuality appliedQuality = (FogQuality)(-1);
        private bool wasFoggy;
        private bool moonOwnedByLightInteraction;
        private bool sunOwnedByDayNight;
        private bool externalSceneFogSuppressed;
        private float environmentDensityScale = 1f;
        private Coroutine tickRoutine;
        private FogVolumetricLayer volumetricLayer;

        /// <summary>Единственный экземпляр системы в сцене.</summary>
        public static FogSystem Instance => instance;

        /// <summary>Текущая итоговая плотность тумана 0..1.</summary>
        public float CurrentDensity => currentDensity;

        /// <summary>Текущий цвет тумана.</summary>
        public Color CurrentTint => currentTint;

        /// <summary>Текущее игровое время в часах 0..24.</summary>
        public float TimeOfDay => timeOfDay;

        /// <summary>Активный уровень качества.</summary>
        public FogQuality Quality => appliedQuality;

        /// <summary>Общий бюджет частиц на сцену.</summary>
        public int GlobalParticleBudget => globalParticleBudget;

        /// <summary>Текущее смещение ветра (для эмиттеров).</summary>
        public Vector2 WindOffset => windOffset;

        /// <summary>Мировое направление ветра с учётом «гуляния».</summary>
        public Vector3 WindVector { get; private set; }

        // =============================================================
        //  Жизненный цикл
        // =============================================================
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[FogSystem] В сцене уже есть FogSystem. Лишний компонент отключён.", this);
                enabled = false;
                return;
            }

            instance = this;

            // 3D-шум генерируется здесь: он нужен и объёмному слою, и частицам.
            FogGlobals.ApplyDefaults();
            currentTint = nightTint * tintBrightness;
            NormalizeSchedule();

            volumetricLayer = GetComponent<FogVolumetricLayer>();
        }

        private void OnEnable()
        {
            if (instance != this) return;

            // Один расчёт сразу, чтобы туман не «прыгал» в первый кадр.
            RecalculateImmediate();
            tickRoutine = StartCoroutine(TickLoop());
        }

        private void OnDisable()
        {
            if (tickRoutine != null)
            {
                StopCoroutine(tickRoutine);
                tickRoutine = null;
            }

            FogGlobals.SetDensity(0f);
            FogGlobals.ClearLights();

            if (controlSceneFog)
                RenderSettings.fog = false;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        /// <summary>
        /// Единственный цикл обновления системы. Работает на корутине с
        /// заданным интервалом вместо Update — экономит CPU.
        /// </summary>
        private IEnumerator TickLoop()
        {
            while (true)
            {
                float dt = Mathf.Max(updateInterval, Time.unscaledDeltaTime);

                AdvanceClock(dt);
                UpdateWind(dt);
                UpdateDensity(dt);
                UpdateTint();
                UpdateSceneFog();

                if (autoQuality)
                    UpdateAutoQuality();

                ApplyQualityKeywords();
                PushGlobals();
                NotifyEmitters();

                yield return new WaitForSeconds(updateInterval);
            }
        }

        // =============================================================
        //  Время
        // =============================================================
        /// <summary>Продвинуть внутренние часы.</summary>
        private void AdvanceClock(float deltaTime)
        {
            if (!useInternalClock || gameMinutesPerRealSecond <= 0f) return;

            timeOfDay += deltaTime * gameMinutesPerRealSecond / 60f;
            if (timeOfDay >= 24f) timeOfDay -= 24f;
            if (timeOfDay < 0f) timeOfDay += 24f;
        }

        // =============================================================
        //  Плотность
        // =============================================================
        /// <summary>Целевая плотность по расписанию для заданного часа.</summary>
        public float EvaluateScheduledDensity(float hour)
        {
            if (manualOverride)
                return manualDensity;

            float ramp = Mathf.Max(rampMinutes / 60f, 0.01f); // нарастание в часах

            // Ночной интервал может пересекать полночь, поэтому работаем
            // в «развёрнутых» координатах относительно fogStartHour.
            float t = Mathf.Repeat(hour - fogStartHour, 24f);
            float peakStart = Mathf.Repeat(peakStartHour - fogStartHour, 24f);
            float peakEnd = Mathf.Repeat(peakEndHour - fogStartHour, 24f);
            float end = Mathf.Repeat(fogEndHour - fogStartHour, 24f);

            // Защита от бессмысленного расписания.
            if (peakEnd < peakStart) peakEnd = peakStart;
            if (end < peakEnd) end = peakEnd + ramp;

            float density;

            if (t <= ramp)
            {
                // плавное появление вечером
                density = Mathf.Lerp(dayResidual, nightDensity, Mathf.SmoothStep(0f, 1f, t / ramp));
            }
            else if (t < peakStart)
            {
                // подъём к предрассветному пику
                float k = Mathf.InverseLerp(ramp, peakStart, t);
                density = Mathf.Lerp(nightDensity, peakDensity, Mathf.SmoothStep(0f, 1f, k));
            }
            else if (t <= peakEnd)
            {
                // плато пика с лёгким «дыханием»
                density = peakDensity;
            }
            else if (t < end)
            {
                // рассеивание после восхода
                float k = Mathf.InverseLerp(peakEnd, end, t);
                density = Mathf.Lerp(peakDensity, dayResidual, Mathf.SmoothStep(0f, 1f, k));
            }
            else
            {
                density = dayResidual;
            }

            return Mathf.Clamp01(density);
        }

        /// <summary>Сгладить переход к целевой плотности.</summary>
        private void UpdateDensity(float deltaTime)
        {
            targetDensity = EvaluateScheduledDensity(timeOfDay);

            // Медленное «дыхание» плотности — волны сгущения/разрежения во времени.
            float breath = 1f + (Mathf.PerlinNoise(animationTime * 0.07f, 3.1f) - 0.5f) * 0.18f;

            // Множитель освещённости от DayNightLighting: днём туман слабее,
            // чтобы яркое солнце не превращало его в непроглядную белую пелену.
            float goal = Mathf.Clamp01(targetDensity * breath * environmentDensityScale);
            currentDensity = Mathf.MoveTowards(currentDensity, goal, densitySmoothing * deltaTime);

            bool foggy = currentDensity > 0.02f;
            if (foggy != wasFoggy)
            {
                wasFoggy = foggy;
                if (logPhaseChanges)
                    Debug.Log($"[FogSystem] Туман {(foggy ? "появился" : "рассеялся")} в {FormatTime(timeOfDay)}");
            }
        }

        /// <summary>Мгновенно выставить плотность и цвет без сглаживания.</summary>
        private void RecalculateImmediate()
        {
            currentDensity = Mathf.Clamp01(EvaluateScheduledDensity(timeOfDay) * environmentDensityScale);
            UpdateTint();
            UpdateSceneFog();
            ApplyQualityKeywords();
            PushGlobals();
        }

        // =============================================================
        //  Цвет
        // =============================================================
        /// <summary>
        /// Цвет тумана по времени суток: серо-голубой ночью,
        /// самый холодный перед рассветом, желтоватый утром.
        /// </summary>
        private void UpdateTint()
        {
            Color target;

            if (timeOfDay >= peakStartHour && timeOfDay <= peakEndHour)
            {
                target = preDawnTint;
            }
            else if (timeOfDay > peakEndHour && timeOfDay <= fogEndHour)
            {
                // предрассветный холод плавно переходит в утреннюю желтизну
                float k = Mathf.InverseLerp(peakEndHour, fogEndHour, timeOfDay);
                target = Color.Lerp(preDawnTint, morningTint, Mathf.SmoothStep(0f, 1f, k));
            }
            else if (timeOfDay > fogEndHour && timeOfDay < 12f)
            {
                target = morningTint;
            }
            else
            {
                target = nightTint;
            }

            currentTint = target * tintBrightness;
            currentTint.a = 1f;
        }

        /// <summary>Ночь ли сейчас — по времени или по направленному свету.</summary>
        private bool IsNight()
        {
            if (directionalLight != null)
            {
                // солнце ниже горизонта => подсвечиваем луной
                return Vector3.Dot(-directionalLight.transform.forward, Vector3.up) < 0.05f;
            }

            return timeOfDay < 6f || timeOfDay > 20f;
        }

        // =============================================================
        //  Ветер
        // =============================================================
        /// <summary>Накопление смещения слоёв тумана и «гуляние» направления.</summary>
        private void UpdateWind(float deltaTime)
        {
            animationTime += deltaTime;

            Vector2 baseDir = windDirection.sqrMagnitude > 0.0001f
                ? windDirection.normalized
                : Vector2.up;

            // медленный поворот направления — туман не движется по линейке
            float wander = (Mathf.PerlinNoise(animationTime * windWanderSpeed, 7.7f) - 0.5f) * 2f * windWander;
            float rad = wander * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad);
            float sin = Mathf.Sin(rad);
            Vector2 dir = new Vector2(baseDir.x * cos - baseDir.y * sin, baseDir.x * sin + baseDir.y * cos);

            windOffset += dir * (windSpeed * deltaTime);
            WindVector = new Vector3(dir.x, 0f, dir.y) * windSpeed;

            // Offset намеренно не оборачивается: любой сброс дал бы заметный
            // «прыжок» всей текстуры тумана. При скорости ~0.35 м/с точности
            // float хватает на многие часы непрерывной игры.
        }

        // =============================================================
        //  Встроенный туман сцены
        // =============================================================
        /// <summary>
        /// Дальняя дымка через RenderSettings — почти бесплатная глубина.
        /// Ближняя граница держится не ближе minimumClearVisibility,
        /// чтобы туман не скрывал врагов на игровой дистанции.
        /// </summary>
        private void UpdateSceneFog()
        {
            if (!controlSceneFog) return;

            // Внутри помещения дальнюю дымку гасит DayNightLighting.
            // Флаг нужен, чтобы два скрипта не мигали, перетягивая RenderSettings.fog.
            if (externalSceneFogSuppressed)
            {
                RenderSettings.fog = false;
                return;
            }

            if (currentDensity <= 0.01f)
            {
                RenderSettings.fog = false;
                return;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = currentTint;

            float end = Mathf.Lerp(sceneFogEndDistanceClear, sceneFogEndDistanceAtPeak, currentDensity);
            float start = Mathf.Max(minimumClearVisibility, end * 0.25f);

            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = Mathf.Max(end, start + 5f);
        }

        // =============================================================
        //  Качество
        // =============================================================
        /// <summary>Мягкая оценка FPS и автоматическое снижение качества.</summary>
        private void UpdateAutoQuality()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt > 0.0001f)
            {
                float fps = 1f / dt;
                fpsAccumulator = Mathf.Lerp(fpsAccumulator, fps, 0.1f);
            }

            if (fpsAccumulator < autoQualityFpsThreshold && quality != FogQuality.Low)
            {
                quality = quality == FogQuality.High ? FogQuality.Medium : FogQuality.Low;
                if (logPhaseChanges)
                    Debug.Log($"[FogSystem] Качество тумана снижено до {quality} (FPS ~{fpsAccumulator:F0})");
            }
        }

        /// <summary>Включить/выключить ветви шейдера согласно уровню качества.</summary>
        private void ApplyQualityKeywords()
        {
            if (appliedQuality == quality) return;
            appliedQuality = quality;

            bool soft = quality >= FogQuality.Medium;
            bool lights = quality >= FogQuality.Medium;
            bool detail = quality >= FogQuality.High;

            SetKeyword(FogGlobals.KeywordSoftParticles, soft);
            SetKeyword(FogGlobals.KeywordLights, lights);
            SetKeyword(FogGlobals.KeywordDetail, detail);

            // Объёмный слой переключает число шагов рейтмарча —
            // это главный рычаг производительности всей системы.
            if (volumetricLayer == null)
                volumetricLayer = GetComponent<FogVolumetricLayer>();

            if (volumetricLayer != null)
                volumetricLayer.OnQualityChanged(quality);

            // Эмиттеры сами перестроят число частиц под новый уровень.
            for (int i = 0; i < emitters.Count; i++)
            {
                if (emitters[i] != null)
                    emitters[i].OnQualityChanged(quality);
            }
        }

        private static void SetKeyword(string keyword, bool enable)
        {
            if (enable) Shader.EnableKeyword(keyword);
            else Shader.DisableKeyword(keyword);
        }

        // =============================================================
        //  Передача в шейдер
        // =============================================================
        /// <summary>Отправить актуальные значения в глобальные свойства шейдера.</summary>
        private void PushGlobals()
        {
            FogGlobals.SetTint(currentTint);
            FogGlobals.SetDensity(currentDensity);
            FogGlobals.SetWind(windOffset, animationTime);

            // Лунный подсвет пишем только если им не управляет FogLightInteraction —
            // иначе два скрипта затирали бы одно и то же глобальное свойство.
            if (!moonOwnedByLightInteraction)
            {
                float moon = IsNight() ? moonIntensity : 0f;
                FogGlobals.SetMoon(moonColor, moon * currentDensity);
            }

            // Направленное рассеивание. Если в сцене есть DayNightLighting,
            // он передаёт солнце сам — с корректной интенсивностью по времени.
            if (!sunOwnedByDayNight)
            {
                if (directionalLight != null && directionalLight.isActiveAndEnabled)
                {
                    FogGlobals.SetSun(
                        directionalLight.color,
                        directionalLight.intensity * 0.35f,
                        directionalLight.transform.forward);
                }
                else
                {
                    FogGlobals.SetSun(Color.white, 0f, Vector3.down);
                }
            }
        }

        /// <summary>
        /// Сообщить системе, что лунным свечением управляет FogLightInteraction.
        /// Вызывается самим FogLightInteraction при включении.
        /// </summary>
        public void ClaimMoonControl(bool claimed)
        {
            moonOwnedByLightInteraction = claimed;
        }

        /// <summary>
        /// Сообщить, что направленным светом в тумане управляет DayNightLighting.
        /// Не даёт двум скриптам перетирать одно глобальное свойство.
        /// </summary>
        public void ClaimSunControl(bool claimed)
        {
            sunOwnedByDayNight = claimed;
        }

        /// <summary>
        /// Внешний множитель плотности от освещённости (DayNightLighting).
        /// Днём туман ослабляется, чтобы не мешать видимости, независимо
        /// от расписания. Ночью множитель равен 1.
        /// </summary>
        public void SetEnvironmentDensityScale(float scale)
        {
            environmentDensityScale = Mathf.Clamp01(scale);
        }

        /// <summary>
        /// Сообщить, что дальнюю дымку сейчас подавляет другая система
        /// (DayNightLighting внутри помещения).
        /// </summary>
        public void SuppressSceneFog(bool suppressed)
        {
            externalSceneFogSuppressed = suppressed;
        }

        /// <summary>Сообщить эмиттерам новую плотность.</summary>
        private void NotifyEmitters()
        {
            for (int i = emitters.Count - 1; i >= 0; i--)
            {
                if (emitters[i] == null)
                {
                    emitters.RemoveAt(i);
                    continue;
                }

                emitters[i].OnSystemTick(currentDensity, WindVector);
            }
        }

        // =============================================================
        //  Регистрация зон и эмиттеров
        // =============================================================
        /// <summary>Зарегистрировать зону тумана.</summary>
        public void RegisterVolume(FogVolume volume)
        {
            if (volume != null && !volumes.Contains(volume))
                volumes.Add(volume);
        }

        /// <summary>Снять регистрацию зоны.</summary>
        public void UnregisterVolume(FogVolume volume)
        {
            volumes.Remove(volume);
        }

        /// <summary>Зарегистрировать эмиттер частиц.</summary>
        public void RegisterEmitter(FogParticles emitter)
        {
            if (emitter != null && !emitters.Contains(emitter))
                emitters.Add(emitter);
        }

        /// <summary>Снять регистрацию эмиттера.</summary>
        public void UnregisterEmitter(FogParticles emitter)
        {
            emitters.Remove(emitter);
        }

        /// <summary>Число зарегистрированных эмиттеров (для распределения бюджета частиц).</summary>
        public int EmitterCount => emitters.Count;

        /// <summary>
        /// Собрать до maxCount ближайших интерьерных зон в пределах радиуса.
        /// Используется FogVolumetricLayer: шейдер держит ограниченный массив
        /// боксов, поэтому передаём только те дома, что рядом с камерой.
        /// Список не аллоцируется — переиспользуется вызывающей стороной.
        /// </summary>
        public void CollectNearestInteriors(Vector3 position, float radius, int maxCount, List<FogVolume> result)
        {
            CollectNearest(position, radius, maxCount, result, wantInteriors: true);
        }

        /// <summary>Собрать до maxCount ближайших зон сгущения (дворы, низины).</summary>
        public void CollectNearestZones(Vector3 position, float radius, int maxCount, List<FogVolume> result)
        {
            CollectNearest(position, radius, maxCount, result, wantInteriors: false);
        }

        /// <summary>
        /// Общая выборка ближайших объёмов. Сортировка вставками:
        /// список короткий (не больше 4–8 элементов), это дешевле LINQ
        /// и не создаёт мусора для GC.
        /// </summary>
        private void CollectNearest(Vector3 position, float radius, int maxCount, List<FogVolume> result, bool wantInteriors)
        {
            result.Clear();
            if (maxCount <= 0) return;

            float radiusSqr = radius * radius;

            for (int i = 0; i < volumes.Count; i++)
            {
                FogVolume volume = volumes[i];
                if (volume == null || !volume.isActiveAndEnabled) continue;
                if (volume.IsInteriorExclusion != wantInteriors) continue;

                // Зоны с нулевым множителем ничего не меняют — пропускаем.
                if (!wantInteriors && Mathf.Approximately(volume.DensityMultiplier, 1f)) continue;

                float distSqr = (volume.WorldCenter - position).sqrMagnitude;
                if (distSqr > radiusSqr) continue;

                // Вставка в отсортированную позицию.
                int insertAt = result.Count;
                for (int j = 0; j < result.Count; j++)
                {
                    if (distSqr < (result[j].WorldCenter - position).sqrMagnitude)
                    {
                        insertAt = j;
                        break;
                    }
                }

                if (insertAt >= maxCount) continue;

                result.Insert(insertAt, volume);

                if (result.Count > maxCount)
                    result.RemoveAt(result.Count - 1);
            }
        }

        /// <summary>
        /// Находится ли точка внутри зоны-исключения (интерьер).
        /// Используется эмиттерами: туман не должен заходить в дома.
        /// </summary>
        public bool IsInsideInterior(Vector3 worldPosition)
        {
            for (int i = 0; i < volumes.Count; i++)
            {
                FogVolume v = volumes[i];
                if (v == null || !v.IsInteriorExclusion) continue;
                if (v.ContainsPoint(worldPosition)) return true;
            }

            return false;
        }

        /// <summary>
        /// Локальный множитель плотности в точке: максимум из перекрывающих зон.
        /// Возвращает 0 внутри интерьеров.
        /// </summary>
        public float SampleLocalDensity(Vector3 worldPosition)
        {
            float result = 0f;

            for (int i = 0; i < volumes.Count; i++)
            {
                FogVolume v = volumes[i];
                if (v == null) continue;

                if (v.IsInteriorExclusion && v.ContainsPoint(worldPosition))
                    return 0f;

                if (!v.IsInteriorExclusion)
                    result = Mathf.Max(result, v.SampleDensity(worldPosition));
            }

            return result;
        }

        // =============================================================
        //  Публичное API
        // =============================================================
        /// <summary>Задать время суток извне (0–24). Отключает внутренние часы.</summary>
        public void SetTimeOfDay(float hours, bool disableInternalClock = true)
        {
            timeOfDay = Mathf.Repeat(hours, 24f);
            if (disableInternalClock) useInternalClock = false;
            RecalculateImmediate();
        }

        /// <summary>Мгновенно применить целевую плотность (без плавного перехода).</summary>
        public void SnapToScheduledDensity()
        {
            currentDensity = EvaluateScheduledDensity(timeOfDay);
            PushGlobals();
        }

        /// <summary>Включить ручной режим и задать плотность 0..1.</summary>
        public void SetManualDensity(float density)
        {
            manualOverride = true;
            manualDensity = Mathf.Clamp01(density);
        }

        /// <summary>Вернуться к расписанию.</summary>
        public void ClearManualOverride()
        {
            manualOverride = false;
        }

        /// <summary>Сменить уровень качества вручную (отключает авто-режим).</summary>
        public void SetQuality(FogQuality newQuality)
        {
            autoQuality = false;
            quality = newQuality;
            ApplyQualityKeywords();
        }

        /// <summary>Форматирование времени для логов и отладки: 03:30.</summary>
        public static string FormatTime(float hours)
        {
            int h = Mathf.FloorToInt(Mathf.Repeat(hours, 24f));
            int m = Mathf.FloorToInt(Mathf.Repeat(hours, 1f) * 60f);
            return $"{h:00}:{m:00}";
        }

        // =============================================================
        //  Редактор
        // =============================================================
        /// <summary>Привести расписание к осмысленным значениям.</summary>
        private void NormalizeSchedule()
        {
            if (peakEndHour < peakStartHour)
                peakEndHour = peakStartHour + 1f;

            if (sceneFogEndDistanceClear < sceneFogEndDistanceAtPeak)
                sceneFogEndDistanceClear = sceneFogEndDistanceAtPeak + 50f;
        }

        private void OnValidate()
        {
            NormalizeSchedule();

            if (Application.isPlaying && instance == this)
                RecalculateImmediate();
        }
    }
}
