using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Взаимодействие тумана со светом.
    /// Собирает ближайшие к игроку источники света (в первую очередь фонарик),
    /// сортирует их по значимости и передаёт в шейдер тумана как массив.
    /// Шейдер рисует объёмное рассеивание: в луче фонарика туман светится,
    /// вокруг ламп появляется мягкое гало.
    ///
    /// Работа с существующим скриптом Flashlight выполняется через рефлексию
    /// свойства IsOn — существующие скрипты не изменяются. Если фонарик
    /// назначен как Light напрямую, рефлексия не нужна.
    ///
    /// Размещение: один компонент на объекте FogSystem.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WWII/Atmosphere/Fog Light Interaction")]
    public class FogLightInteraction : MonoBehaviour
    {
        // =============================================================
        //  Источники света
        // =============================================================
        [Header("Фонарик игрока")]
        [Tooltip("Spot Light фонарика. Получает приоритет над всеми остальными источниками.")]
        [SerializeField] private Light flashlight;

        [Tooltip("Множитель силы подсветки тумана фонариком.")]
        [SerializeField, Range(0f, 6f)] private float flashlightScatter = 2.4f;

        [Tooltip("Насколько узким выглядит светящийся луч. Больше — тоньше и заметнее.")]
        [SerializeField, Range(0f, 8f)] private float beamFocus = 3f;

        [Tooltip("Множитель радиуса действия фонарика в тумане. >1 — луч дотягивается дальше.")]
        [SerializeField, Range(0.2f, 2f)] private float flashlightRangeScale = 1.4f;

        [Header("Прочие источники света")]
        [Tooltip("Автоматически искать в сцене все Point/Spot источники (костры, лампы, фары).")]
        [SerializeField] private bool autoCollectSceneLights = true;

        [Tooltip("Дополнительные источники, добавляемые вручную.")]
        [SerializeField] private List<Light> manualLights = new List<Light>();

        [Tooltip("Слои источников света, которые учитываются при авто-сборе.")]
        [SerializeField] private LayerMask lightLayers = ~0;

        [Tooltip("Максимальная дистанция от игрока, на которой лампа влияет на туман, м.")]
        [SerializeField, Range(5f, 200f)] private float maxLightDistance = 60f;

        [Tooltip("Общий множитель подсветки от прочих ламп.")]
        [SerializeField, Range(0f, 4f)] private float ambientLightScatter = 1.1f;

        // =============================================================
        //  Луна
        // =============================================================
        [Header("Лунный свет")]
        [Tooltip("Направленный свет луны. Даёт лёгкое общее свечение тумана ночью.")]
        [SerializeField] private Light moonLight;

        [Tooltip("Сила лунного свечения тумана.")]
        [SerializeField, Range(0f, 1f)] private float moonScatter = 0.15f;

        // =============================================================
        //  Рассеивание
        // =============================================================
        [Header("Рассеивание света в тумане")]
        [Tooltip("Как сильно плотность тумана усиливает свечение. Густой туман светится ярче.")]
        [SerializeField, Range(0f, 2f)] private float densityBoost = 0.8f;

        [Tooltip("Ограничение суммарной яркости подсветки, чтобы туман не выбеливался.")]
        [SerializeField, Range(0.5f, 8f)] private float scatterClamp = 3.5f;

        [Tooltip("Сглаживание изменений яркости подсветки. Меньше — плавнее.")]
        [SerializeField, Range(1f, 30f)] private float scatterSmoothing = 8f;

        [Tooltip("Материал тумана (тот же, что в FogParticles). Нужен для управления фокусом луча. Если пусто — параметры материала не меняются.")]
        [SerializeField] private Material fogMaterial;

        // =============================================================
        //  Производительность
        // =============================================================
        [Header("Производительность")]
        [Tooltip("Интервал обновления списка ламп, сек.")]
        [SerializeField, Range(0.03f, 0.5f)] private float updateInterval = 0.06f;

        [Tooltip("Интервал повторного поиска источников света в сцене, сек. 0 — искать только при старте.")]
        [SerializeField, Range(0f, 30f)] private float rescanInterval = 5f;

        [Tooltip("Трансформ игрока/камеры для отбора ближайших ламп. Если пусто — Camera.main.")]
        [SerializeField] private Transform viewer;

        [Header("Отладка")]
        [Tooltip("Выводить в консоль число активных ламп при изменении.")]
        [SerializeField] private bool logLightCount = false;

        // =============================================================
        //  Состояние
        // =============================================================
        private readonly Vector4[] positions = new Vector4[FogGlobals.MaxLights];
        private readonly Vector4[] colors = new Vector4[FogGlobals.MaxLights];
        private readonly Vector4[] directions = new Vector4[FogGlobals.MaxLights];

        private readonly List<Light> candidates = new List<Light>(32);
        private readonly List<Light> sceneLights = new List<Light>(32);

        private float flashlightWeight;
        private int lastCount = -1;
        private float rescanTimer;
        private Coroutine updateRoutine;

        // Кэш для чтения IsOn у существующего скрипта Flashlight через рефлексию.
        private Component flashlightComponent;
        private System.Reflection.PropertyInfo isOnProperty;
        private bool flashlightProbeDone;

        // =============================================================
        //  Жизненный цикл
        // =============================================================
        private void Awake()
        {
            if (viewer == null && Camera.main != null)
                viewer = Camera.main.transform;
        }

        private void Start()
        {
            // FogSystem мог инициализироваться позже этого компонента.
            if (moonLight != null && FogSystem.Instance != null)
                FogSystem.Instance.ClaimMoonControl(true);
        }

        private void OnEnable()
        {
            RescanSceneLights();

            // Забираем управление лунным свечением у FogSystem, если задан moonLight.
            if (moonLight != null && FogSystem.Instance != null)
                FogSystem.Instance.ClaimMoonControl(true);

            updateRoutine = StartCoroutine(UpdateLoop());
        }

        private void OnDisable()
        {
            if (updateRoutine != null)
            {
                StopCoroutine(updateRoutine);
                updateRoutine = null;
            }

            if (FogSystem.Instance != null)
                FogSystem.Instance.ClaimMoonControl(false);

            FogGlobals.ClearLights();
        }

        /// <summary>
        /// Основной цикл. Работает на корутине с шагом updateInterval:
        /// фонарик успевает за движением игрока, но CPU почти не грузится.
        /// </summary>
        private IEnumerator UpdateLoop()
        {
            while (true)
            {
                if (viewer == null && Camera.main != null)
                    viewer = Camera.main.transform;

                if (rescanInterval > 0.01f)
                {
                    rescanTimer += updateInterval;
                    if (rescanTimer >= rescanInterval)
                    {
                        rescanTimer = 0f;
                        RescanSceneLights();
                    }
                }

                int count = CollectLights();
                FogGlobals.SetLights(count, positions, colors, directions);
                UpdateMoon();
                UpdateMaterialScatter();

                if (logLightCount && count != lastCount)
                {
                    lastCount = count;
                    Debug.Log($"[FogLightInteraction] Ламп подсвечивают туман: {count}");
                }

                yield return new WaitForSeconds(updateInterval);
            }
        }

        // =============================================================
        //  Сбор источников света
        // =============================================================
        /// <summary>Найти в сцене все точечные и конусные источники.</summary>
        public void RescanSceneLights()
        {
            sceneLights.Clear();

            if (!autoCollectSceneLights) return;

#if UNITY_2023_1_OR_NEWER
            Light[] all = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            Light[] all = Object.FindObjectsOfType<Light>();
#endif

            for (int i = 0; i < all.Length; i++)
            {
                Light light = all[i];
                if (light == null) continue;
                if (light.type != LightType.Point && light.type != LightType.Spot) continue;
                if ((lightLayers.value & (1 << light.gameObject.layer)) == 0) continue;

                sceneLights.Add(light);
            }
        }

        /// <summary>
        /// Отобрать до FogGlobals.MaxLights самых значимых источников
        /// и заполнить массивы для шейдера.
        /// </summary>
        private int CollectLights()
        {
            candidates.Clear();

            Vector3 viewPos = viewer != null ? viewer.position : transform.position;

            // Фонарик всегда первым — он важнее любой лампы в сцене.
            bool flashlightActive = IsFlashlightActive();
            if (flashlightActive)
                candidates.Add(flashlight);

            AppendCandidates(manualLights, viewPos);
            AppendCandidates(sceneLights, viewPos);

            // Сортировка по значимости: интенсивность / квадрат расстояния.
            // Фонарик исключён из сортировки, он уже на первом месте.
            int startIndex = flashlightActive ? 1 : 0;
            SortByImportance(startIndex, viewPos);

            int count = Mathf.Min(candidates.Count, FogGlobals.MaxLights);
            float density = FogSystem.Instance != null ? FogSystem.Instance.CurrentDensity : 1f;
            float boost = 1f + density * densityBoost;

            for (int i = 0; i < count; i++)
            {
                Light light = candidates[i];
                Transform t = light.transform;

                bool isFlashlight = flashlightActive && i == 0;
                float scatter = isFlashlight ? flashlightScatter : ambientLightScatter;
                float rangeScale = isFlashlight ? flashlightRangeScale : 1f;

                float intensity = Mathf.Min(light.intensity * scatter * boost, scatterClamp);

                positions[i] = new Vector4(t.position.x, t.position.y, t.position.z,
                    Mathf.Max(light.range * rangeScale, 0.1f));

                Color c = light.color;
                colors[i] = new Vector4(c.r, c.g, c.b, intensity);

                if (light.type == LightType.Spot)
                {
                    Vector3 forward = t.forward;
                    // cos половины внешнего угла — граница конуса в шейдере
                    float cosOuter = Mathf.Cos(light.spotAngle * 0.5f * Mathf.Deg2Rad);
                    directions[i] = new Vector4(forward.x, forward.y, forward.z, cosOuter);
                }
                else
                {
                    // w = -1 помечает точечный источник (конус не применяется)
                    directions[i] = new Vector4(0f, -1f, 0f, -1f);
                }
            }

            // Обнуляем хвост массива, чтобы старые данные не «светили».
            for (int i = count; i < FogGlobals.MaxLights; i++)
            {
                positions[i] = Vector4.zero;
                colors[i] = Vector4.zero;
                directions[i] = new Vector4(0f, -1f, 0f, -1f);
            }

            return count;
        }

        /// <summary>Добавить подходящие лампы из списка в кандидаты.</summary>
        private void AppendCandidates(List<Light> source, Vector3 viewPos)
        {
            float maxDistSqr = maxLightDistance * maxLightDistance;

            for (int i = 0; i < source.Count; i++)
            {
                Light light = source[i];
                if (light == null || !light.isActiveAndEnabled) continue;
                if (light.intensity <= 0.01f) continue;
                if (light == flashlight) continue;                 // уже добавлен
                if (candidates.Contains(light)) continue;

                if ((light.transform.position - viewPos).sqrMagnitude > maxDistSqr) continue;

                candidates.Add(light);
            }
        }

        /// <summary>
        /// Простая сортировка вставками по значимости.
        /// Список короткий (обычно < 20), поэтому это дешевле, чем LINQ с аллокациями.
        /// </summary>
        private void SortByImportance(int startIndex, Vector3 viewPos)
        {
            for (int i = startIndex + 1; i < candidates.Count; i++)
            {
                Light current = candidates[i];
                float currentScore = Importance(current, viewPos);
                int j = i - 1;

                while (j >= startIndex && Importance(candidates[j], viewPos) < currentScore)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }

                candidates[j + 1] = current;
            }
        }

        /// <summary>Значимость лампы для тумана: ярче и ближе — важнее.</summary>
        private static float Importance(Light light, Vector3 viewPos)
        {
            float distSqr = Mathf.Max((light.transform.position - viewPos).sqrMagnitude, 0.01f);
            return light.intensity * light.range / distSqr;
        }

        // =============================================================
        //  Фонарик
        // =============================================================
        /// <summary>
        /// Горит ли фонарик. Сначала проверяется сам Light,
        /// затем — свойство IsOn существующего скрипта Flashlight (через рефлексию,
        /// чтобы не изменять и не жёстко связывать существующий код).
        /// </summary>
        private bool IsFlashlightActive()
        {
            if (flashlight == null) return false;
            if (!flashlight.isActiveAndEnabled) return false;
            if (flashlight.intensity <= 0.01f) return false;

            ProbeFlashlightScript();

            if (isOnProperty != null && flashlightComponent != null)
            {
                object value = isOnProperty.GetValue(flashlightComponent, null);
                if (value is bool isOn) return isOn;
            }

            return true;
        }

        /// <summary>Однократный поиск компонента со свойством IsOn выше по иерархии.</summary>
        private void ProbeFlashlightScript()
        {
            if (flashlightProbeDone) return;
            flashlightProbeDone = true;

            if (flashlight == null) return;

            MonoBehaviour[] behaviours = flashlight.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;

                System.Reflection.PropertyInfo property = behaviour.GetType().GetProperty(
                    "IsOn",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (property != null && property.PropertyType == typeof(bool))
                {
                    flashlightComponent = behaviour;
                    isOnProperty = property;
                    return;
                }
            }
        }

        // =============================================================
        //  Луна и материалы
        // =============================================================
        /// <summary>Обновить лунный подсвет тумана.</summary>
        private void UpdateMoon()
        {
            if (moonLight == null) return;

            // Свет ниже горизонта — луны не видно.
            float elevation = Vector3.Dot(-moonLight.transform.forward, Vector3.up);
            float visibility = Mathf.Clamp01(elevation * 2f);

            float density = FogSystem.Instance != null ? FogSystem.Instance.CurrentDensity : 1f;
            float intensity = moonScatter * visibility * moonLight.intensity * density;

            FogGlobals.SetMoon(moonLight.color, intensity);
        }

        /// <summary>Плавно подтянуть вес подсветки и применить фокус луча к материалу.</summary>
        private void UpdateMaterialScatter()
        {
            float target = IsFlashlightActive() ? 1f : 0f;
            flashlightWeight = Mathf.MoveTowards(flashlightWeight, target, scatterSmoothing * updateInterval);

            if (fogMaterial == null) return;

            // Луч заметнее, когда фонарик включён: плавно поднимаем направленность.
            fogMaterial.SetFloat(FogGlobals.LightScatterId, Mathf.Lerp(0.6f, 1f, flashlightWeight) * 2f);
            fogMaterial.SetFloat(anisoId, beamFocus * Mathf.Lerp(0.4f, 1f, flashlightWeight));
        }

        private static readonly int anisoId = Shader.PropertyToID("_LightAniso");

        // =============================================================
        //  Публичное API
        // =============================================================
        /// <summary>Назначить фонарик в рантайме (например, после подбора из инвентаря).</summary>
        public void SetFlashlight(Light light)
        {
            flashlight = light;
            flashlightComponent = null;
            isOnProperty = null;
            flashlightProbeDone = false;
        }

        /// <summary>Добавить источник света вручную.</summary>
        public void AddLight(Light light)
        {
            if (light != null && !manualLights.Contains(light))
                manualLights.Add(light);
        }

        /// <summary>Убрать источник света из ручного списка.</summary>
        public void RemoveLight(Light light)
        {
            manualLights.Remove(light);
        }

        // =============================================================
        //  Редактор
        // =============================================================
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                flashlightProbeDone = false;
                flashlightComponent = null;
                isOnProperty = null;
            }
        }
    }
}
