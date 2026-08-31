using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Процедурный эмиттер частиц тумана для одной зоны.
    /// Работает в паре с <see cref="FogVolume"/>: заранее (в Start) находит
    /// пригодные точки спавна внутри объёма, отсеивая интерьеры и точки под
    /// геометрией, а затем один раз выпускает частицы через ParticleSystem.Emit.
    ///
    /// Ключевые решения для производительности:
    ///   * точки спавна считаются один раз при инициализации (бейк), не в Update;
    ///   * частицы живут очень долго и почти не пересоздаются;
    ///   * движение слоёв делает шейдер, а не CPU;
    ///   * LOD: число частиц и размер зависят от расстояния до камеры и уровня качества;
    ///   * обновление раз в 0.2–0.5 с через корутину, без Update.
    ///
    /// Размещение: на том же GameObject, что и FogVolume.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FogVolume))]
    [AddComponentMenu("WWII/Atmosphere/Fog Particles")]
    public class FogParticles : MonoBehaviour
    {
        // =============================================================
        //  Материал и рендер
        // =============================================================
        [Header("Материал")]
        [Tooltip("Материал на шейдере WWII/Fog Soft Particle. Если пусто — создаётся автоматически.")]
        [SerializeField] private Material fogMaterial;

        [Tooltip("Разрешение процедурного спрайта пласта тумана.")]
        [SerializeField, Range(32, 256)] private int puffResolution = 128;

        [Tooltip("Сортировочный слой рендера частиц. Отрицательный — рисовать раньше остальной прозрачной геометрии.")]
        [SerializeField] private int sortingOrder = -10;

        // =============================================================
        //  Количество частиц
        // =============================================================
        [Header("Количество частиц")]
        [Tooltip("Желаемое число пластов в зоне при полном качестве. Держите небольшим: основной объём рисует FogVolumetricLayer.")]
        [SerializeField, Range(4, 400)] private int targetParticleCount = 28;

        [Tooltip("Доля частиц от целевого числа на среднем качестве.")]
        [SerializeField, Range(0.1f, 1f)] private float mediumQualityRatio = 0.65f;

        [Tooltip("Доля частиц от целевого числа на низком качестве.")]
        [SerializeField, Range(0.05f, 1f)] private float lowQualityRatio = 0.35f;

        [Tooltip("Сколько попыток на одну частицу делать при поиске точки спавна.")]
        [SerializeField, Range(1, 12)] private int spawnAttemptsPerParticle = 6;

        [Header("Обтекание домов")]
        [Tooltip("Не спавнить клубки внутри коллайдеров зданий — туман обтекает дома, а не сидит в стенах.")]
        [SerializeField] private bool avoidGeometry = true;

        [Tooltip("Слои зданий и препятствий для проверки при бейке.")]
        [SerializeField] private LayerMask geometryLayers = ~0;

        [Tooltip("Радиус проверки свободного места вокруг точки спавна, м.")]
        [SerializeField, Range(0.2f, 8f)] private float clearanceRadius = 1.5f;

        // =============================================================
        //  Внешний вид частиц
        // =============================================================
        [Header("Размер пластов")]
        [Tooltip("Минимальный диаметр пласта тумана, м.")]
        [SerializeField, Range(1f, 40f)] private float minPuffSize = 10f;

        [Tooltip("Максимальный диаметр пласта тумана, м.")]
        [SerializeField, Range(1f, 80f)] private float maxPuffSize = 26f;

        [Header("Прозрачность")]
        [Tooltip("Минимальная непрозрачность отдельного пласта.")]
        [SerializeField, Range(0.02f, 1f)] private float minAlpha = 0.12f;

        [Tooltip("Максимальная непрозрачность отдельного пласта. Держите низкой: основной объём даёт FogVolumetricLayer.")]
        [SerializeField, Range(0.02f, 1f)] private float maxAlpha = 0.3f;

        [Tooltip("Мягкость пересечения со стенами домов, м. Больше — мягче.")]
        [SerializeField, Range(0.2f, 10f)] private float softIntersection = 3f;

        [Tooltip("Дистанция, ближе которой туман гасится, чтобы не залеплять экран, м.")]
        [SerializeField, Range(0f, 6f)] private float nearCameraFade = 1f;

        [Tooltip("Радиус гашения пластов вокруг камеры, м. Главная защита от видимых 'овалов' — стоя в тумане, вы не видите форму частиц.")]
        [SerializeField, Range(1f, 40f)] private float insideFadeDistance = 14f;

        [Tooltip("Гашение при взгляде сверху на пласт. 0 — не гасить (видна плоскость), 0.6 — норма.")]
        [SerializeField, Range(0f, 1f)] private float grazingFade = 0.6f;

        // =============================================================
        //  Движение
        // =============================================================
        [Header("Движение и ветер")]
        [Tooltip("Реагировать на ветер из FogSystem.")]
        [SerializeField] private bool useWind = true;

        [Tooltip("Насколько сильно ветер сносит частицы. 1 = скорость ветра из FogSystem.")]
        [SerializeField, Range(0f, 2f)] private float windInfluence = 0.5f;

        [Tooltip("Скорость медленного вертикального дыхания частиц, м/с.")]
        [SerializeField, Range(0f, 0.5f)] private float verticalDrift = 0.05f;

        [Tooltip("Сила турбулентности (Noise-модуль). Очень слабая для WWII-настроения.")]
        [SerializeField, Range(0f, 1f)] private float turbulence = 0.18f;

        [Tooltip("Частота турбулентности. Низкая — крупные плавные завихрения.")]
        [SerializeField, Range(0.01f, 1f)] private float turbulenceFrequency = 0.06f;

        [Tooltip("Скорость вращения пластов, град/с. Малые значения — туман не 'крутится'.")]
        [SerializeField, Range(0f, 15f)] private float rotationSpeed = 1.5f;

        [Tooltip("Время жизни частицы, сек. Долгая жизнь = меньше пересозданий = меньше нагрузки.")]
        [SerializeField, Range(10f, 600f)] private float particleLifetime = 180f;

        // =============================================================
        //  LOD
        // =============================================================
        [Header("LOD (оптимизация по дистанции)")]
        [Tooltip("Дистанция полного качества, м. Ближе — все частицы.")]
        [SerializeField, Range(5f, 120f)] private float lodNearDistance = 35f;

        [Tooltip("Дистанция, дальше которой остаётся минимум частиц, м.")]
        [SerializeField, Range(20f, 400f)] private float lodFarDistance = 120f;

        [Tooltip("Доля частиц на максимальной дистанции LOD.")]
        [SerializeField, Range(0.05f, 1f)] private float lodFarRatio = 0.25f;

        [Tooltip("Полностью отключать зону дальше этой дистанции, м. 0 — не отключать.")]
        [SerializeField, Range(0f, 600f)] private float cullDistance = 220f;

        [Tooltip("Интервал проверки LOD, сек. Реже — дешевле.")]
        [SerializeField, Range(0.1f, 2f)] private float lodCheckInterval = 0.35f;

        [Tooltip("Камера наблюдателя. Если пусто — берётся Camera.main.")]
        [SerializeField] private Transform viewer;

        // =============================================================
        //  Состояние
        // =============================================================
        private FogVolume volume;
        private ParticleSystem particles;
        private ParticleSystemRenderer particleRenderer;

        private readonly List<Vector3> bakedPoints = new List<Vector3>(128);
        private readonly List<float> bakedDensities = new List<float>(128);

        private int activeCount;
        private int desiredCount;
        private float systemDensity;
        private Vector3 windVector;
        private FogQuality quality = FogQuality.Medium;
        private bool visible = true;
        private Coroutine lodRoutine;
        private ParticleSystem.Particle[] buffer;

        /// <summary>Сколько частиц реально выпущено сейчас.</summary>
        public int ActiveParticleCount => activeCount;

        /// <summary>Последняя полученная от FogSystem общая плотность (для отладки и внешних систем).</summary>
        public float SystemDensity => systemDensity;

        // =============================================================
        //  Инициализация
        // =============================================================
        private void Awake()
        {
            volume = GetComponent<FogVolume>();
            EnsureParticleSystem();
        }

        private void OnEnable()
        {
            if (FogSystem.Instance != null)
            {
                FogSystem.Instance.RegisterEmitter(this);
                quality = FogSystem.Instance.Quality;
            }
        }

        private void Start()
        {
            FogSystem system = FogSystem.Instance;
            if (system != null)
            {
                system.RegisterEmitter(this);
                quality = system.Quality;
            }
            else
            {
                Debug.LogWarning("[FogParticles] В сцене нет FogSystem — туман не будет управляться расписанием.", this);
            }

            // Бейк откладываем на один кадр: к этому моменту все FogVolume
            // (включая интерьерные зоны-исключения) уже зарегистрированы в системе.
            StartCoroutine(DeferredInitialize());
        }

        /// <summary>Отложенная инициализация — гарантирует корректный учёт интерьеров.</summary>
        private IEnumerator DeferredInitialize()
        {
            yield return null;

            if (viewer == null && Camera.main != null)
                viewer = Camera.main.transform;

            BakeSpawnPoints();
            RebuildParticles();

            lodRoutine = StartCoroutine(LodLoop());
        }

        private void OnDisable()
        {
            if (lodRoutine != null)
            {
                StopCoroutine(lodRoutine);
                lodRoutine = null;
            }

            if (FogSystem.Instance != null)
                FogSystem.Instance.UnregisterEmitter(this);

            if (particles != null)
                particles.Clear();
        }

        /// <summary>Создать и настроить ParticleSystem, если его ещё нет.</summary>
        private void EnsureParticleSystem()
        {
            particles = GetComponent<ParticleSystem>();
            if (particles == null)
                particles = gameObject.AddComponent<ParticleSystem>();

            particleRenderer = GetComponent<ParticleSystemRenderer>();

            ConfigureParticleSystem();
            ConfigureRenderer();
        }

        /// <summary>
        /// Настройка модулей ParticleSystem. Эмиссия отключена:
        /// частицы выпускаются вручную через Emit в заранее вычисленных точках.
        /// </summary>
        private void ConfigureParticleSystem()
        {
            ParticleSystem.MainModule main = particles.main;

            // loop = true при выключенной эмиссии: система остаётся "playing",
            // поэтому вручную выпущенные частицы продолжают симулироваться,
            // но сама она ничего не спавнит.
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = particleLifetime;
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.startSize = (minPuffSize + maxPuffSize) * 0.5f;
            main.startRotation3D = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // Shape: масштаб трансформа влияет только на позиции спавна,
            // размер пластов остаётся в метрах, как задано в инспекторе.
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = Mathf.Max(targetParticleCount, 8);
            main.gravityModifier = 0f;

            // Pause: за экраном симуляция останавливается и не догоняет время —
            // самый дешёвый вариант для слабых систем.
            main.cullingMode = ParticleSystemCullingMode.Pause;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = false;

            // Медленное восходящее дыхание + снос ветром.
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-verticalDrift, verticalDrift);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            // Плавное появление и исчезновение пласта — никаких резких границ во времени.
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(BuildLifetimeGradient());

            // Пласт медленно разрастается — ощущение живой массы.
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, BuildSizeCurve());

            ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
            rotation.enabled = rotationSpeed > 0.01f;
            rotation.separateAxes = false;
            rotation.z = new ParticleSystem.MinMaxCurve(-rotationSpeed * Mathf.Deg2Rad, rotationSpeed * Mathf.Deg2Rad);

            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = turbulence > 0.001f;
            noise.strength = turbulence;
            noise.frequency = turbulenceFrequency;
            noise.scrollSpeed = 0.05f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low; // дешёвая ветвь
            noise.octaveCount = 1;

            // Ничего лишнего — все прочие модули выключены.
            // Модули ParticleSystem — структуры, возвращаемые по значению:
            // писать в particles.trails.enabled напрямую нельзя (CS1612),
            // нужна локальная переменная.
            ParticleSystem.TrailModule trails = particles.trails;
            trails.enabled = false;

            ParticleSystem.LightsModule lights = particles.lights;
            lights.enabled = false;

            ParticleSystem.TriggerModule trigger = particles.trigger;
            trigger.enabled = false;

            ParticleSystem.CollisionModule collision = particles.collision;
            collision.enabled = false;

            ParticleSystem.SubEmittersModule subEmitters = particles.subEmitters;
            subEmitters.enabled = false;

            ParticleSystem.TextureSheetAnimationModule sheet = particles.textureSheetAnimation;
            sheet.enabled = false;

            ParticleSystem.ForceOverLifetimeModule force = particles.forceOverLifetime;
            force.enabled = false;

            ParticleSystem.ExternalForcesModule external = particles.externalForces;
            external.enabled = false;
        }

        /// <summary>Градиент альфы за время жизни: плавный вход и выход.</summary>
        private static Gradient BuildLifetimeGradient()
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        /// <summary>Кривая размера: пласт медленно расширяется.</summary>
        private static AnimationCurve BuildSizeCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.75f),
                new Keyframe(0.5f, 1f),
                new Keyframe(1f, 1.25f));
        }

        /// <summary>
        /// Настройка рендера. Ключевое решение: HorizontalBillboard вместо
        /// обычного билборда. Плоскость, всегда повёрнутая к камере, и есть
        /// причина видимых «овалов»: игрок буквально смотрит на диск.
        /// Горизонтальные пласты читаются как слои тумана над землёй.
        /// </summary>
        private void ConfigureRenderer()
        {
            if (particleRenderer == null) return;

            particleRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            particleRenderer.sortMode = ParticleSystemSortMode.Distance;
            particleRenderer.sortingOrder = sortingOrder;

            // Туман не должен ни отбрасывать, ни принимать тени и не пишет в глубину.
            particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            particleRenderer.receiveShadows = false;
            particleRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            particleRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            particleRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            particleRenderer.allowRoll = false;

            particleRenderer.sharedMaterial = GetOrCreateMaterial();
        }

        /// <summary>
        /// Получить материал тумана. Если не назначен — собрать процедурно:
        /// шейдер + сгенерированный спрайт пласта.
        /// 3D-шум ставится глобально в FogGlobals, здесь не нужен.
        /// </summary>
        private Material GetOrCreateMaterial()
        {
            if (fogMaterial == null)
            {
                Shader shader = Shader.Find(FogGlobals.ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[FogParticles] Шейдер '{FogGlobals.ShaderName}' не найден. " +
                                   "Проверьте, что FogSoftParticle.shader импортирован.", this);
                    return null;
                }

                fogMaterial = new Material(shader)
                {
                    name = "FogParticle_Runtime",
                    hideFlags = HideFlags.DontSave
                };
            }

            fogMaterial.SetTexture(FogGlobals.MainTexId, FogNoise.GetPuffTexture(puffResolution));
            fogMaterial.SetFloat(FogGlobals.SoftFadeId, softIntersection);
            fogMaterial.SetFloat(FogGlobals.NearFadeId, nearCameraFade);
            fogMaterial.SetFloat(insideFadeId, insideFadeDistance);
            fogMaterial.SetFloat(grazingFadeId, grazingFade);

            return fogMaterial;
        }

        private static readonly int insideFadeId = Shader.PropertyToID("_InsideFade");
        private static readonly int grazingFadeId = Shader.PropertyToID("_GrazingFade");

        // =============================================================
        //  Бейк точек спавна
        // =============================================================
        /// <summary>
        /// Заранее найти точки спавна внутри объёма. Выполняется один раз:
        /// в рантайме поиск точек больше не нужен, поэтому CPU-нагрузки нет.
        /// </summary>
        public void BakeSpawnPoints()
        {
            bakedPoints.Clear();
            bakedDensities.Clear();

            if (volume == null) volume = GetComponent<FogVolume>();
            if (volume == null || volume.IsInteriorExclusion) return;

            volume.RefreshGroundLevel();

            int budget = ResolveBudget();
            int attempts = budget * spawnAttemptsPerParticle;

            for (int i = 0; i < attempts && bakedPoints.Count < budget; i++)
            {
                if (!volume.TryGetSpawnPoint(out Vector3 point, out float density))
                    continue;

                // Клубок не должен оказаться внутри стены дома.
                if (avoidGeometry && IsInsideGeometry(point))
                    continue;

                bakedPoints.Add(point);
                bakedDensities.Add(density);
            }

            if (bakedPoints.Count == 0)
                Debug.LogWarning($"[FogParticles] Не найдено ни одной точки спавна в зоне '{name}'. " +
                                 "Проверьте размеры FogVolume и слои земли.", this);
        }

        /// <summary>
        /// Проверка занятости точки геометрией. Выполняется только при бейке,
        /// поэтому на рантайм-производительность не влияет.
        /// </summary>
        private bool IsInsideGeometry(Vector3 point)
        {
            return Physics.CheckSphere(point, clearanceRadius, geometryLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Сколько частиц отвести этой зоне с учётом глобального бюджета,
        /// уровня качества и LOD-дистанции.
        /// </summary>
        private int ResolveBudget()
        {
            float qualityRatio = quality switch
            {
                FogQuality.Low => lowQualityRatio,
                FogQuality.Medium => mediumQualityRatio,
                _ => 1f
            };

            int budget = Mathf.RoundToInt(targetParticleCount * qualityRatio);

            // Не превышаем общий бюджет сцены, поделённый между эмиттерами.
            FogSystem system = FogSystem.Instance;
            if (system != null && system.EmitterCount > 0)
            {
                int share = Mathf.Max(4, system.GlobalParticleBudget / system.EmitterCount);
                budget = Mathf.Min(budget, share);
            }

            return Mathf.Max(2, budget);
        }

        // =============================================================
        //  Выпуск частиц
        // =============================================================
        /// <summary>
        /// Полностью пересобрать набор частиц по забейканным точкам.
        /// Вызывается при инициализации и при смене LOD/качества.
        /// </summary>
        private void RebuildParticles()
        {
            if (particles == null || bakedPoints.Count == 0) return;

            ParticleSystem.MainModule main = particles.main;
            main.maxParticles = Mathf.Max(bakedPoints.Count, 8);

            particles.Clear();

            desiredCount = Mathf.Clamp(
                Mathf.RoundToInt(bakedPoints.Count * CurrentLodRatio()),
                0, bakedPoints.Count);

            for (int i = 0; i < desiredCount; i++)
                EmitAt(i);

            activeCount = desiredCount;

            // Симуляция стоит на месте: частицы живут долго и двигаются
            // скоростью/шумом, поэтому Play нужен один раз.
            if (!particles.isPlaying)
                particles.Play();
        }

        /// <summary>Выпустить одну частицу в забейканной точке с индексом i.</summary>
        private void EmitAt(int i)
        {
            Vector3 position = bakedPoints[i];
            float localDensity = bakedDensities[i];

            // EmitParams.position задаётся в пространстве симуляции.
            // Точки забейканы в мировых координатах, поэтому для локальной
            // симуляции переводим их обратно в локальные.
            if (particles.main.simulationSpace != ParticleSystemSimulationSpace.World)
                position = transform.InverseTransformPoint(position);

            // Размер: чем плотнее точка, тем крупнее пласт.
            float size = Mathf.Lerp(minPuffSize, maxPuffSize, Mathf.Clamp01(localDensity * Random.Range(0.6f, 1.1f)));

            // Альфа отдельного пласта держится низкой: основную плотность
            // даёт объёмный слой, частицы только добавляют структуру.
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, Mathf.Clamp01(localDensity)) * Random.Range(0.75f, 1f);

            ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
            {
                position = position,
                startLifetime = particleLifetime * Random.Range(0.7f, 1.3f),
                startSize = size,
                startColor = new Color(1f, 1f, 1f, alpha),
                rotation = Random.Range(0f, 360f),
                velocity = Vector3.zero,
                applyShapeToPosition = false
            };

            particles.Emit(emitParams, 1);
        }

        // =============================================================
        //  LOD
        // =============================================================
        /// <summary>Доля частиц по текущей дистанции до наблюдателя.</summary>
        private float CurrentLodRatio()
        {
            if (viewer == null) return 1f;

            float distance = Vector3.Distance(viewer.position, volume != null ? volume.WorldCenter : transform.position);

            if (distance <= lodNearDistance) return 1f;
            if (distance >= lodFarDistance) return lodFarRatio;

            float k = Mathf.InverseLerp(lodNearDistance, lodFarDistance, distance);
            return Mathf.Lerp(1f, lodFarRatio, k);
        }

        /// <summary>
        /// Периодическая проверка LOD и отсечения по дистанции.
        /// Пересборка частиц происходит только при заметном изменении.
        /// </summary>
        private IEnumerator LodLoop()
        {
            // Небольшая случайная задержка, чтобы все зоны не пересчитывались в один кадр.
            yield return new WaitForSeconds(Random.Range(0f, lodCheckInterval));

            while (true)
            {
                if (viewer == null && Camera.main != null)
                    viewer = Camera.main.transform;

                UpdateCulling();

                if (visible)
                {
                    int target = Mathf.Clamp(
                        Mathf.RoundToInt(bakedPoints.Count * CurrentLodRatio()),
                        0, bakedPoints.Count);

                    // Порог 15% — избегаем дёргания числа частиц на каждой проверке.
                    if (Mathf.Abs(target - activeCount) > Mathf.Max(2, bakedPoints.Count / 7))
                        ApplyParticleCount(target);
                    else
                        RefillExpired(target);
                }

                yield return new WaitForSeconds(lodCheckInterval);
            }
        }

        /// <summary>
        /// Дозаполнить набор вместо умерших частиц.
        /// Эмиссия ParticleSystem отключена, поэтому долгоживущие клубки
        /// нужно возобновлять вручную — это происходит редко и стоит копейки.
        /// </summary>
        private void RefillExpired(int target)
        {
            if (particles == null || bakedPoints.Count == 0) return;

            int alive = particles.particleCount;
            int missing = Mathf.Min(target, bakedPoints.Count) - alive;
            if (missing <= 0) return;

            // Возобновляем с конца списка точек, чтобы не создавать сгустки в одном месте.
            for (int i = 0; i < missing; i++)
            {
                int index = (alive + i) % bakedPoints.Count;
                EmitAt(index);
            }

            activeCount = Mathf.Min(target, bakedPoints.Count);
        }

        /// <summary>Полное отключение зоны, если игрок далеко.</summary>
        private void UpdateCulling()
        {
            if (cullDistance <= 0.1f || viewer == null)
            {
                SetVisible(true);
                return;
            }

            float distance = Vector3.Distance(viewer.position, volume != null ? volume.WorldCenter : transform.position);
            SetVisible(distance <= cullDistance);
        }

        private void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;

            if (particleRenderer != null)
                particleRenderer.enabled = value;

            if (!value && particles != null)
            {
                particles.Clear();
                activeCount = 0;
            }
            else if (value)
            {
                RebuildParticles();
            }
        }

        /// <summary>
        /// Довыпустить или погасить частицы до целевого числа
        /// без полной пересборки — дешевле, чем Clear + Emit всего набора.
        /// </summary>
        private void ApplyParticleCount(int target)
        {
            if (particles == null || bakedPoints.Count == 0) return;

            target = Mathf.Clamp(target, 0, bakedPoints.Count);

            if (target > activeCount)
            {
                for (int i = activeCount; i < target; i++)
                    EmitAt(i);
            }
            else if (target < activeCount)
            {
                // Гасим лишние частицы, обнуляя их остаточное время жизни.
                EnsureBuffer();
                int alive = particles.GetParticles(buffer);
                int toKill = alive - target;

                for (int i = alive - 1; i >= 0 && toKill > 0; i--, toKill--)
                    buffer[i].remainingLifetime = 0f;

                particles.SetParticles(buffer, alive);
            }

            activeCount = target;
        }

        private void EnsureBuffer()
        {
            int needed = Mathf.Max(particles.main.maxParticles, 8);
            if (buffer == null || buffer.Length < needed)
                buffer = new ParticleSystem.Particle[needed];
        }

        // =============================================================
        //  Обратные вызовы от FogSystem
        // =============================================================
        /// <summary>
        /// Вызывается FogSystem с низкой частотой. Обновляет снос ветром.
        /// Плотность в шейдер передаёт сама система, поэтому здесь только ветер.
        /// </summary>
        public void OnSystemTick(float density, Vector3 wind)
        {
            systemDensity = density;

            if (!useWind || particles == null) return;

            Vector3 target = wind * windInfluence;
            if ((target - windVector).sqrMagnitude < 0.0001f) return;

            windVector = target;

            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.x = new ParticleSystem.MinMaxCurve(windVector.x * 0.7f, windVector.x * 1.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(windVector.z * 0.7f, windVector.z * 1.3f);
        }

        /// <summary>Реакция на смену уровня качества: перебейкать и пересобрать.</summary>
        public void OnQualityChanged(FogQuality newQuality)
        {
            if (quality == newQuality) return;
            quality = newQuality;

            if (!isActiveAndEnabled) return;

            BakeSpawnPoints();
            RebuildParticles();
        }

        /// <summary>Полная перегенерация зоны (например, после изменения геометрии сцены).</summary>
        public void Regenerate()
        {
            BakeSpawnPoints();
            RebuildParticles();
        }

        // =============================================================
        //  Редактор
        // =============================================================
        private void OnValidate()
        {
            if (maxPuffSize < minPuffSize)
                maxPuffSize = minPuffSize;

            if (maxAlpha < minAlpha)
                maxAlpha = minAlpha;

            if (lodFarDistance < lodNearDistance + 5f)
                lodFarDistance = lodNearDistance + 5f;

            if (Application.isPlaying && particles != null)
            {
                ConfigureParticleSystem();
                ConfigureRenderer();
            }
        }
    }
}
