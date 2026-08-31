using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Слой объёмного тумана — главный визуальный компонент системы.
    ///
    /// ПОЧЕМУ ОН НУЖЕН:
    /// Билборд-частицы физически не могут выглядеть правдоподобно, когда
    /// камера стоит внутри тумана — игрок видит плоские овалы, потому что
    /// билборд это плоскость. Здесь туман считается интегралом плотности
    /// вдоль луча взгляда (raymarching), у него нет формы вообще, только
    /// плотность в точке. Овалов не возникает физически.
    ///
    /// Как работает: на камеру вешается небольшой квад чуть дальше
    /// near-плоскости. Он закрывает весь кадр, а шейдер для каждого пикселя
    /// марширует луч от камеры до геометрии сцены. Пост-обработка
    /// не используется, только обычный прозрачный рендер.
    ///
    /// Размещение: компонент на объекте FogSystem. Квад создаётся сам.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WWII/Atmosphere/Fog Volumetric Layer")]
    public class FogVolumetricLayer : MonoBehaviour
    {
        // =============================================================
        //  Материал
        // =============================================================
        [Header("Материал")]
        [Tooltip("Материал на шейдере WWII/Fog Volumetric. Если пусто — создаётся автоматически.")]
        [SerializeField] private Material volumetricMaterial;

        [Tooltip("Камера, к которой крепится слой. Если пусто — Camera.main.")]
        [SerializeField] private Camera targetCamera;

        // =============================================================
        //  Плотность и высота
        // =============================================================
        [Header("Плотность и высота")]
        [Tooltip("Плотность тумана у земли при полном ночном тумане. 0.06–0.12 — реалистичный диапазон.")]
        [SerializeField, Range(0.01f, 0.4f)] private float groundDensity = 0.085f;

        [Tooltip("Уровень земли, от которого начинается туман, м. Обычно Y пола сцены.")]
        [SerializeField] private float baseHeight = 0f;

        [Tooltip("Характерная высота спада плотности, м. 3–6 — стелющийся туман, 10+ — высокая пелена.")]
        [SerializeField, Range(1f, 30f)] private float falloffHeight = 4.5f;

        [Tooltip("Дальность расчёта тумана, м. Дальше работает дешёвая линейная дымка из FogSystem.")]
        [SerializeField, Range(30f, 400f)] private float maxDistance = 150f;

        // =============================================================
        //  Неоднородность
        // =============================================================
        [Header("Неоднородность (живой туман)")]
        [Tooltip("Размер крупных масс тумана. Меньше значение — крупнее клубы. 0.02 ≈ клубы по 50 м.")]
        [SerializeField, Range(0.005f, 0.1f)] private float noiseScale = 0.022f;

        [Tooltip("Размер мелкой детализации. Рваные края масс.")]
        [SerializeField, Range(0.02f, 0.4f)] private float detailScale = 0.09f;

        [Tooltip("Сила неоднородности. 0 — однородная пелена (плохо), 0.7–0.85 — реалистично.")]
        [SerializeField, Range(0f, 1f)] private float noiseStrength = 0.78f;

        [Tooltip("Вклад мелкой детализации в рваность краёв.")]
        [SerializeField, Range(0f, 1f)] private float detailStrength = 0.35f;

        [Tooltip("Скорость проплывания масс тумана относительно ветра.")]
        [SerializeField, Range(0f, 2f)] private float noiseScroll = 0.35f;

        // =============================================================
        //  Рассеивание света
        // =============================================================
        [Header("Рассеивание света")]
        [Tooltip("Направленность рассеивания (g в фазе Хеньи–Гринштейна). 0.5–0.7 — водяная дымка: ярко светится против света.")]
        [SerializeField, Range(0f, 0.95f)] private float anisotropy = 0.62f;

        [Tooltip("Сила подсветки лампами и фонариком.")]
        [SerializeField, Range(0f, 8f)] private float lightScatter = 2.5f;

        [Tooltip("Сила подсветки солнцем/луной. Даёт объём и сияние против света.")]
        [SerializeField, Range(0f, 4f)] private float sunScatter = 1.1f;

        [Tooltip("Окружающее свечение тумана от неба. Задаёт общую светлоту.")]
        [SerializeField, Range(0f, 2f)] private float ambientScatter = 0.5f;

        // =============================================================
        //  Интерьеры
        // =============================================================
        [Header("Интерьеры")]
        [Tooltip("Вычитать объёмы помещений из тумана. Внутри домов туман не рисуется.")]
        [SerializeField] private bool excludeInteriors = true;

        [Tooltip("Скорость затухания слоя при входе в помещение. Больше — резче.")]
        [SerializeField, Range(0.5f, 10f)] private float interiorFadeSpeed = 3f;

        [Tooltip("Интервал обновления списка интерьеров и зон, сек.")]
        [SerializeField, Range(0.05f, 1f)] private float volumeUpdateInterval = 0.2f;

        // =============================================================
        //  Качество
        // =============================================================
        [Header("Качество")]
        [Tooltip("Дизеринг шагов рейтмарча. 1 — обязательно: без него видны кольца и полосы.")]
        [SerializeField, Range(0f, 1f)] private float stepJitter = 1f;

        [Tooltip("Затухание вплотную к камере, м. Не даёт тумана прямо в объективе.")]
        [SerializeField, Range(0f, 3f)] private float nearFade = 0.4f;

        [Tooltip("Порядок сортировки квада. Ниже — рисуется раньше частиц.")]
        [SerializeField] private int sortingOrder = -100;

        // =============================================================
        //  Состояние
        // =============================================================
        private Transform quadTransform;
        private MeshRenderer quadRenderer;
        private Mesh quadMesh;

        private readonly List<FogVolume> interiorCache = new List<FogVolume>(8);
        private readonly List<FogVolume> zoneCache = new List<FogVolume>(8);

        private readonly Matrix4x4[] interiorMatrices = new Matrix4x4[FogGlobals.MaxInteriors];
        private readonly Matrix4x4[] zoneMatrices = new Matrix4x4[FogGlobals.MaxZones];
        private readonly Vector4[] zoneParams = new Vector4[FogGlobals.MaxZones];

        private float interiorFade = 1f;
        private FogQuality appliedQuality = (FogQuality)(-1);
        private Coroutine volumeRoutine;

        /// <summary>Находится ли камера внутри помещения (0 — внутри, 1 — снаружи).</summary>
        public float InteriorFade => interiorFade;

        /// <summary>Материал слоя — для внешней подстройки.</summary>
        public Material Material => volumetricMaterial;

        // =============================================================
        //  Жизненный цикл
        // =============================================================
        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

            FogGlobals.SetNoise3D(FogNoise.GetNoise3D());
            BuildQuad();
            ApplyMaterialSettings();
        }

        private void OnEnable()
        {
            if (quadRenderer != null)
                quadRenderer.enabled = true;

            volumeRoutine = StartCoroutine(VolumeUpdateLoop());
        }

        private void OnDisable()
        {
            if (volumeRoutine != null)
            {
                StopCoroutine(volumeRoutine);
                volumeRoutine = null;
            }

            if (quadRenderer != null)
                quadRenderer.enabled = false;

            FogGlobals.SetInteriorFade(1f);
        }

        private void OnDestroy()
        {
            if (quadMesh != null) Destroy(quadMesh);
            if (quadTransform != null) Destroy(quadTransform.gameObject);
        }

        // =============================================================
        //  Квад на камере
        // =============================================================
        /// <summary>
        /// Создать квад-носитель шейдера. Он крепится к камере и всегда
        /// закрывает кадр целиком: размер считается из FOV и near-плоскости.
        /// </summary>
        private void BuildQuad()
        {
            if (targetCamera == null)
            {
                Debug.LogError("[FogVolumetricLayer] Камера не найдена. Назначьте Target Camera вручную.", this);
                return;
            }

            GameObject quad = new GameObject("FogVolumetricQuad")
            {
                hideFlags = HideFlags.DontSave
            };

            quadTransform = quad.transform;
            quadTransform.SetParent(targetCamera.transform, false);

            quadMesh = new Mesh { name = "FogQuad" };
            quadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f)
            };
            quadMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quadMesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };

            // Огромные границы: квад никогда не должен отсекаться фрустумом.
            quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

            MeshFilter filter = quad.AddComponent<MeshFilter>();
            filter.sharedMesh = quadMesh;

            quadRenderer = quad.AddComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = GetOrCreateMaterial();
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            quadRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            quadRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            quadRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            quadRenderer.allowOcclusionWhenDynamic = false;

            PositionQuad();
        }

        /// <summary>
        /// Разместить и растянуть квад так, чтобы он покрывал весь кадр
        /// чуть дальше near-плоскости.
        /// </summary>
        private void PositionQuad()
        {
            if (quadTransform == null || targetCamera == null) return;

            float distance = targetCamera.nearClipPlane * 1.5f;
            float height = 2f * distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float width = height * targetCamera.aspect;

            // Запас 15%: страхует от щелей по краям при смене аспекта.
            quadTransform.localPosition = new Vector3(0f, 0f, distance);
            quadTransform.localRotation = Quaternion.identity;
            quadTransform.localScale = new Vector3(width * 1.15f, height * 1.15f, 1f);
        }

        // =============================================================
        //  Материал
        // =============================================================
        /// <summary>Получить или создать материал объёмного тумана.</summary>
        private Material GetOrCreateMaterial()
        {
            if (volumetricMaterial != null) return volumetricMaterial;

            Shader shader = Shader.Find(FogGlobals.VolumetricShaderName);
            if (shader == null)
            {
                Debug.LogError($"[FogVolumetricLayer] Шейдер '{FogGlobals.VolumetricShaderName}' не найден. " +
                               "Проверьте, что FogVolumetric.shader импортирован.", this);
                return null;
            }

            volumetricMaterial = new Material(shader)
            {
                name = "FogVolumetric_Runtime",
                hideFlags = HideFlags.DontSave
            };

            return volumetricMaterial;
        }

        /// <summary>Перенести настройки инспектора в материал.</summary>
        public void ApplyMaterialSettings()
        {
            Material material = GetOrCreateMaterial();
            if (material == null) return;

            material.SetFloat(FogGlobals.VolumeDensityId, groundDensity);
            material.SetFloat(FogGlobals.BaseHeightId, baseHeight);
            material.SetFloat(FogGlobals.FalloffHeightId, falloffHeight);
            material.SetFloat(FogGlobals.MaxDistanceId, maxDistance);

            material.SetFloat(Shader.PropertyToID("_NoiseScale"), noiseScale);
            material.SetFloat(Shader.PropertyToID("_NoiseDetailScale"), detailScale);
            material.SetFloat(FogGlobals.NoiseStrengthId, noiseStrength);
            material.SetFloat(Shader.PropertyToID("_DetailStrength"), detailStrength);
            material.SetFloat(Shader.PropertyToID("_NoiseScroll"), noiseScroll);

            material.SetFloat(FogGlobals.AnisotropyId, anisotropy);
            material.SetFloat(FogGlobals.LightScatterId, lightScatter);
            material.SetFloat(FogGlobals.SunScatterId, sunScatter);
            material.SetFloat(FogGlobals.AmbientScatterId, ambientScatter);

            material.SetFloat(Shader.PropertyToID("_StepJitter"), stepJitter);
            material.SetFloat(FogGlobals.NearFadeId, nearFade);

            if (quadRenderer != null)
                quadRenderer.sortingOrder = sortingOrder;
        }

        /// <summary>Переключить число шагов рейтмарча под уровень качества.</summary>
        public void OnQualityChanged(FogQuality quality)
        {
            if (appliedQuality == quality) return;
            appliedQuality = quality;

            Material material = GetOrCreateMaterial();
            if (material == null) return;

            material.DisableKeyword(FogGlobals.KeywordStepsLow);
            material.DisableKeyword(FogGlobals.KeywordStepsMed);
            material.DisableKeyword(FogGlobals.KeywordStepsHigh);

            switch (quality)
            {
                case FogQuality.Low:
                    material.EnableKeyword(FogGlobals.KeywordStepsLow);
                    material.DisableKeyword(FogGlobals.KeywordDetail);
                    break;

                case FogQuality.Medium:
                    material.EnableKeyword(FogGlobals.KeywordStepsMed);
                    material.EnableKeyword(FogGlobals.KeywordDetail);
                    break;

                default:
                    material.EnableKeyword(FogGlobals.KeywordStepsHigh);
                    material.EnableKeyword(FogGlobals.KeywordDetail);
                    break;
            }
        }

        // =============================================================
        //  Интерьеры и зоны
        // =============================================================
        /// <summary>
        /// Периодически собирает ближайшие интерьерные боксы и зоны сгущения
        /// и передаёт их в шейдер как матрицы. Работает редко (5 раз в секунду),
        /// поэтому на CPU практически не влияет.
        /// </summary>
        private IEnumerator VolumeUpdateLoop()
        {
            while (true)
            {
                if (targetCamera == null)
                {
                    targetCamera = Camera.main;
                    if (targetCamera != null && quadTransform == null)
                        BuildQuad();
                }

                UpdateInteriorFade();
                PushInteriors();
                PushZones();

                yield return new WaitForSeconds(volumeUpdateInterval);
            }
        }

        /// <summary>
        /// Плавно гасит объёмный слой, когда камера входит в помещение.
        /// Внутри дома объёмный туман не нужен: там работает InteriorDarkness.
        /// </summary>
        private void UpdateInteriorFade()
        {
            FogSystem system = FogSystem.Instance;
            Vector3 cameraPos = targetCamera != null ? targetCamera.transform.position : transform.position;

            bool inside = system != null && system.IsInsideInterior(cameraPos);
            float target = inside ? 0f : 1f;

            interiorFade = Mathf.MoveTowards(interiorFade, target, interiorFadeSpeed * volumeUpdateInterval);
            FogGlobals.SetInteriorFade(interiorFade);
        }

        /// <summary>
        /// Передать в шейдер до MaxInteriors ближайших помещений.
        /// Матрица переводит мир в локальный единичный куб — шейдеру
        /// достаточно одного mul, чтобы понять, внутри ли точка.
        /// </summary>
        private void PushInteriors()
        {
            if (!excludeInteriors)
            {
                FogGlobals.SetInteriors(0, interiorMatrices);
                return;
            }

            FogSystem system = FogSystem.Instance;
            if (system == null)
            {
                FogGlobals.SetInteriors(0, interiorMatrices);
                return;
            }

            Vector3 cameraPos = targetCamera != null ? targetCamera.transform.position : transform.position;

            interiorCache.Clear();
            system.CollectNearestInteriors(cameraPos, maxDistance, FogGlobals.MaxInteriors, interiorCache);

            int count = Mathf.Min(interiorCache.Count, FogGlobals.MaxInteriors);
            for (int i = 0; i < count; i++)
                interiorMatrices[i] = interiorCache[i].GetWorldToUnitCube();

            for (int i = count; i < FogGlobals.MaxInteriors; i++)
                interiorMatrices[i] = Matrix4x4.identity;

            FogGlobals.SetInteriors(count, interiorMatrices);
        }

        /// <summary>Передать в шейдер до MaxZones ближайших зон сгущения.</summary>
        private void PushZones()
        {
            FogSystem system = FogSystem.Instance;
            if (system == null)
            {
                FogGlobals.SetZones(0, zoneMatrices, zoneParams);
                return;
            }

            Vector3 cameraPos = targetCamera != null ? targetCamera.transform.position : transform.position;

            zoneCache.Clear();
            system.CollectNearestZones(cameraPos, maxDistance, FogGlobals.MaxZones, zoneCache);

            int count = Mathf.Min(zoneCache.Count, FogGlobals.MaxZones);
            for (int i = 0; i < count; i++)
            {
                FogVolume zone = zoneCache[i];
                zoneMatrices[i] = zone.GetWorldToUnitCube();
                zoneParams[i] = new Vector4(zone.DensityMultiplier, zone.ShaderEdgeFeather, 0f, 0f);
            }

            for (int i = count; i < FogGlobals.MaxZones; i++)
            {
                zoneMatrices[i] = Matrix4x4.identity;
                zoneParams[i] = new Vector4(1f, 0.2f, 0f, 0f);
            }

            FogGlobals.SetZones(count, zoneMatrices, zoneParams);
        }

        // =============================================================
        //  Публичное API
        // =============================================================
        /// <summary>Задать уровень земли в рантайме (например, при смене этажа).</summary>
        public void SetBaseHeight(float height)
        {
            baseHeight = height;
            ApplyMaterialSettings();
        }

        /// <summary>Задать плотность у земли в рантайме.</summary>
        public void SetGroundDensity(float density)
        {
            groundDensity = Mathf.Clamp(density, 0f, 0.4f);
            ApplyMaterialSettings();
        }

        /// <summary>Пересоздать квад (например, после смены камеры).</summary>
        public void RebuildForCamera(Camera camera)
        {
            targetCamera = camera;

            if (quadTransform != null)
            {
                Destroy(quadTransform.gameObject);
                quadTransform = null;
            }

            BuildQuad();
            ApplyMaterialSettings();
        }

        // =============================================================
        //  Редактор
        // =============================================================
        private void OnValidate()
        {
            if (!Application.isPlaying) return;

            ApplyMaterialSettings();
            PositionQuad();
        }
    }
}
