using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Объём тумана — прямоугольная зона, задающая локальную плотность,
    /// высоту и характер тумана: двор, улица, низина, открытое место.
    /// Также может работать как зона-исключение для интерьеров: внутри дома
    /// тумана нет.
    ///
    /// Плотность внутри объёма неравномерна: она затухает к краям и по высоте,
    /// а процедурный шум создаёт зоны сгущения и разрежения. За счёт этого
    /// туман «обтекает» дома и скапливается в низинах.
    ///
    /// Размещение: пустой GameObject внутри двора/улицы, масштаб задаётся
    /// полями size, а не Transform.localScale (чтобы не искажать частицы).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WWII/Atmosphere/Fog Volume")]
    public class FogVolume : MonoBehaviour
    {
        // =============================================================
        //  Тип и геометрия
        // =============================================================
        [Header("Тип зоны")]
        [Tooltip("Характер зоны. Interior — зона-исключение, туман внутрь не заходит.")]
        [SerializeField] private FogZoneType zoneType = FogZoneType.Courtyard;

        [Tooltip("Применить типовые настройки высоты/плотности при смене типа зоны.")]
        [SerializeField] private bool applyPresetOnValidate = true;

        [Header("Геометрия зоны (локальные координаты)")]
        [Tooltip("Размеры зоны по XYZ в метрах. Y — полная высота объёма.")]
        [SerializeField] private Vector3 size = new Vector3(20f, 8f, 20f);

        [Tooltip("Смещение центра зоны относительно объекта.")]
        [SerializeField] private Vector3 centerOffset = new Vector3(0f, 4f, 0f);

        // =============================================================
        //  Плотность
        // =============================================================
        [Header("Плотность")]
        [Tooltip("Множитель плотности зоны. >1 — туман здесь гуще, чем в среднем по сцене.")]
        [SerializeField, Range(0f, 2f)] private float densityMultiplier = 1f;

        [Tooltip("Ширина мягкого края зоны в метрах. Никаких резких границ.")]
        [SerializeField, Range(0.5f, 20f)] private float edgeFalloff = 4f;

        [Tooltip("Минимальная плотность в зоне при полном тумане. Не даёт зоне полностью опустеть.")]
        [SerializeField, Range(0f, 1f)] private float minimumDensity = 0.05f;

        // =============================================================
        //  Высотный профиль
        // =============================================================
        [Header("Высотный профиль")]
        [Tooltip("Высота слоя максимальной плотности над низом зоны, м. Стелющийся туман — 0.5–1.5.")]
        [SerializeField, Range(0f, 15f)] private float coreHeight = 1.2f;

        [Tooltip("На какой высоте туман сходит на нет, м. Открытые места — выше.")]
        [SerializeField, Range(0.5f, 40f)] private float fadeHeight = 6f;

        [Tooltip("Резкость затухания по высоте. 1 — линейно, >1 — туман сильнее прижат к земле.")]
        [SerializeField, Range(0.5f, 4f)] private float heightFalloffPower = 2f;

        // =============================================================
        //  Волны плотности
        // =============================================================
        [Header("Волны плотности (живой туман)")]
        [Tooltip("Сила процедурных зон сгущения и разрежения внутри объёма.")]
        [SerializeField, Range(0f, 1f)] private float wavesStrength = 0.55f;

        [Tooltip("Размер пятен сгущения в метрах. Больше — крупнее пятна.")]
        [SerializeField, Range(2f, 60f)] private float wavesScale = 18f;

        [Tooltip("Скорость перемещения зон сгущения. Очень медленно для WWII-настроения.")]
        [SerializeField, Range(0f, 0.5f)] private float wavesSpeed = 0.03f;

        // =============================================================
        //  Привязка к рельефу и укрытиям
        // =============================================================
        [Header("Привязка к геометрии")]
        [Tooltip("Прижимать нижнюю границу тумана к земле по рейкасту вниз.")]
        [SerializeField] private bool snapToGround = true;

        [Tooltip("Слои, считающиеся землёй/полом.")]
        [SerializeField] private LayerMask groundLayers = ~0;

        [Tooltip("С какой высоты над центром зоны стрелять рейкастом вниз, м.")]
        [SerializeField, Range(1f, 60f)] private float groundProbeHeight = 25f;

        [Header("Интерьер")]
        [Tooltip("Зона-исключение: туман внутрь не попадает. Ставить на объём каждого дома.")]
        [SerializeField] private bool interiorExclusion = false;

        [Tooltip("Запас за границей интерьера, м. Гасит туман чуть раньше стен.")]
        [SerializeField, Range(0f, 3f)] private float interiorPadding = 0.4f;

        // =============================================================
        //  Отладка
        // =============================================================
        [Header("Отладка")]
        [Tooltip("Показывать габариты зоны в редакторе.")]
        [SerializeField] private bool drawGizmos = true;

        [Tooltip("Цвет гизмо для обычных зон.")]
        [SerializeField] private Color gizmoColor = new Color(0.5f, 0.7f, 1f, 0.25f);

        // =============================================================
        //  Состояние
        // =============================================================
        private float groundLevel;
        private bool groundResolved;
        private float noiseSeedX;
        private float noiseSeedZ;

        /// <summary>Является ли зона исключением для интерьера.</summary>
        public bool IsInteriorExclusion => interiorExclusion || zoneType == FogZoneType.Interior;

        /// <summary>Тип зоны.</summary>
        public FogZoneType ZoneType => zoneType;

        /// <summary>Множитель плотности зоны.</summary>
        public float DensityMultiplier => densityMultiplier;

        /// <summary>Мировой центр объёма.</summary>
        public Vector3 WorldCenter => transform.TransformPoint(centerOffset);

        /// <summary>Размеры объёма.</summary>
        public Vector3 Size => size;

        /// <summary>Высота слоя максимальной плотности.</summary>
        public float CoreHeight => coreHeight;

        /// <summary>Высота полного затухания.</summary>
        public float FadeHeight => fadeHeight;

        /// <summary>
        /// Мягкость края зоны в единицах локального куба (0..0.5).
        /// Шейдер работает в нормированных координатах, поэтому метры
        /// переводятся в долю от размера зоны.
        /// </summary>
        public float ShaderEdgeFeather
        {
            get
            {
                float minExtent = Mathf.Min(size.x, size.z);
                if (minExtent < 0.01f) return 0.2f;
                return Mathf.Clamp(edgeFalloff / minExtent, 0.02f, 0.49f);
            }
        }

        /// <summary>
        /// Матрица перевода мировых координат в локальный единичный куб (±0.5).
        /// Шейдеру достаточно одного mul, чтобы понять, внутри ли точка объёма.
        /// </summary>
        public Matrix4x4 GetWorldToUnitCube()
        {
            Vector3 extents = size;

            if (IsInteriorExclusion)
                extents += Vector3.one * (interiorPadding * 2f);

            // Защита от деления на ноль в масштабе.
            extents = new Vector3(
                Mathf.Max(extents.x, 0.01f),
                Mathf.Max(extents.y, 0.01f),
                Mathf.Max(extents.z, 0.01f));

            // Локальный→мировой для бокса: сдвиг в центр, поворот объекта, масштаб в размер.
            Matrix4x4 boxToWorld = Matrix4x4.TRS(
                transform.TransformPoint(centerOffset),
                transform.rotation,
                extents);

            return boxToWorld.inverse;
        }

        /// <summary>Уровень земли (низ тумана) в мировых координатах.</summary>
        public float GroundLevel
        {
            get
            {
                if (!groundResolved) ResolveGroundLevel();
                return groundLevel;
            }
        }

        // =============================================================
        //  Жизненный цикл
        // =============================================================
        private void Awake()
        {
            noiseSeedX = Random.value * 100f;
            noiseSeedZ = Random.value * 100f;
            ResolveGroundLevel();
        }

        private void OnEnable()
        {
            if (FogSystem.Instance != null)
                FogSystem.Instance.RegisterVolume(this);
        }

        private void Start()
        {
            // Система могла инициализироваться позже этого объекта.
            if (FogSystem.Instance != null)
                FogSystem.Instance.RegisterVolume(this);
        }

        private void OnDisable()
        {
            if (FogSystem.Instance != null)
                FogSystem.Instance.UnregisterVolume(this);
        }

        // =============================================================
        //  Геометрия
        // =============================================================
        /// <summary>Определить уровень земли рейкастом вниз из центра зоны.</summary>
        private void ResolveGroundLevel()
        {
            Vector3 center = WorldCenter;
            groundLevel = center.y - size.y * 0.5f;

            if (snapToGround)
            {
                Vector3 origin = new Vector3(center.x, center.y + groundProbeHeight, center.z);
                float distance = groundProbeHeight + size.y;

                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance, groundLayers, QueryTriggerInteraction.Ignore))
                    groundLevel = hit.point.y;
            }

            groundResolved = true;
        }

        /// <summary>Пересчитать уровень земли (вызвать, если геометрия сцены изменилась).</summary>
        public void RefreshGroundLevel()
        {
            groundResolved = false;
            ResolveGroundLevel();
        }

        /// <summary>Находится ли мировая точка внутри объёма (с учётом padding для интерьеров).</summary>
        public bool ContainsPoint(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - centerOffset;
            Vector3 half = size * 0.5f;

            if (IsInteriorExclusion)
                half += Vector3.one * interiorPadding;

            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        /// <summary>
        /// Плотность тумана в мировой точке: 0 снаружи, до densityMultiplier внутри.
        /// Учитывает мягкие края, высотный профиль и процедурные волны.
        /// Для интерьерных зон всегда 0 — туман внутрь домов не заходит.
        /// </summary>
        public float SampleDensity(Vector3 worldPosition)
        {
            if (IsInteriorExclusion)
                return 0f;

            Vector3 local = transform.InverseTransformPoint(worldPosition) - centerOffset;
            Vector3 half = size * 0.5f;

            // --- горизонтальное затухание к краям ---
            float fx = HorizontalFalloff(local.x, half.x);
            float fz = HorizontalFalloff(local.z, half.z);
            if (fx <= 0f || fz <= 0f) return 0f;

            // --- высотный профиль относительно земли ---
            float heightAboveGround = worldPosition.y - GroundLevel;
            float fy = HeightFalloff(heightAboveGround);
            if (fy <= 0f) return 0f;

            // --- волны сгущения/разрежения ---
            float waves = 1f;
            if (wavesStrength > 0f)
            {
                float drift = Time.time * wavesSpeed;
                float scale = 1f / Mathf.Max(wavesScale, 0.01f);
                float n = Mathf.PerlinNoise(
                    worldPosition.x * scale + noiseSeedX + drift,
                    worldPosition.z * scale + noiseSeedZ - drift * 0.6f);

                waves = Mathf.Lerp(1f, n * 1.8f, wavesStrength);
            }

            float density = densityMultiplier * fx * fz * fy * waves;
            return Mathf.Clamp(density, minimumDensity * densityMultiplier * fy, 2f);
        }

        /// <summary>Мягкое затухание вдоль одной горизонтальной оси.</summary>
        private float HorizontalFalloff(float localCoord, float halfExtent)
        {
            float distanceToEdge = halfExtent - Mathf.Abs(localCoord);
            if (distanceToEdge <= 0f) return 0f;

            float falloff = Mathf.Min(edgeFalloff, halfExtent);
            if (falloff <= 0.001f) return 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceToEdge / falloff));
        }

        /// <summary>
        /// Профиль плотности по высоте: плато до coreHeight,
        /// затем плавное затухание до fadeHeight.
        /// </summary>
        private float HeightFalloff(float heightAboveGround)
        {
            if (heightAboveGround < -1f) return 0f;              // под землёй
            if (heightAboveGround <= coreHeight) return 1f;       // густой стелющийся слой
            if (heightAboveGround >= fadeHeight) return 0f;       // выше тумана нет

            float k = Mathf.InverseLerp(fadeHeight, coreHeight, heightAboveGround);
            return Mathf.Pow(Mathf.Clamp01(k), heightFalloffPower);
        }

        /// <summary>
        /// Случайная точка внутри объёма, пригодная для спавна частицы.
        /// Возвращает false, если точка попала в интерьер или в разреженную область.
        /// </summary>
        public bool TryGetSpawnPoint(out Vector3 point, out float densityAtPoint)
        {
            Vector3 half = size * 0.5f;

            // Позиция в плане — равномерно по площади зоны.
            Vector3 local = new Vector3(
                Random.Range(-half.x, half.x),
                0f,
                Random.Range(-half.z, half.z));

            point = transform.TransformPoint(local + centerOffset);

            // Высота — со смещением к земле: чем выше, тем реже частицы.
            // Pow(random, power) сгущает выборку у нуля, повторяя высотный профиль.
            float bias = Mathf.Pow(Random.value, heightFalloffPower);
            point.y = GroundLevel + fadeHeight * bias;

            densityAtPoint = SampleDensity(point);

            if (densityAtPoint <= 0.01f)
                return false;

            FogSystem system = FogSystem.Instance;
            if (system != null && system.IsInsideInterior(point))
                return false;

            return true;
        }

        // =============================================================
        //  Пресеты
        // =============================================================
        /// <summary>Типовые настройки для каждого вида зоны.</summary>
        public void ApplyPreset(FogZoneType type)
        {
            zoneType = type;

            switch (type)
            {
                case FogZoneType.OpenGround:
                    // открытое место: туман поднимается выше, но реже
                    densityMultiplier = 0.75f;
                    coreHeight = 1.5f;
                    fadeHeight = 9f;
                    heightFalloffPower = 1.4f;
                    wavesStrength = 0.6f;
                    wavesScale = 26f;
                    edgeFalloff = 6f;
                    interiorExclusion = false;
                    break;

                case FogZoneType.Street:
                    // улица между домами: вытянутые медленные пласты
                    densityMultiplier = 1f;
                    coreHeight = 1.2f;
                    fadeHeight = 5f;
                    heightFalloffPower = 2f;
                    wavesStrength = 0.5f;
                    wavesScale = 20f;
                    edgeFalloff = 3f;
                    interiorExclusion = false;
                    break;

                case FogZoneType.Courtyard:
                    // двор: туман скапливается, плотнее и ниже
                    densityMultiplier = 1.25f;
                    coreHeight = 1f;
                    fadeHeight = 4.5f;
                    heightFalloffPower = 2.2f;
                    wavesStrength = 0.45f;
                    wavesScale = 14f;
                    edgeFalloff = 2.5f;
                    interiorExclusion = false;
                    break;

                case FogZoneType.Lowland:
                    // низина/воронка: самый густой стелющийся туман
                    densityMultiplier = 1.6f;
                    coreHeight = 0.7f;
                    fadeHeight = 3f;
                    heightFalloffPower = 3f;
                    wavesStrength = 0.35f;
                    wavesScale = 10f;
                    edgeFalloff = 2f;
                    interiorExclusion = false;
                    break;

                case FogZoneType.Interior:
                    // помещение: тумана нет
                    densityMultiplier = 0f;
                    interiorExclusion = true;
                    break;
            }
        }

        /// <summary>Задать плотность зоны в рантайме.</summary>
        public void SetDensityMultiplier(float value)
        {
            densityMultiplier = Mathf.Clamp(value, 0f, 2f);
        }

        // =============================================================
        //  Редактор
        // =============================================================
        private void OnValidate()
        {
            size = new Vector3(
                Mathf.Max(0.5f, size.x),
                Mathf.Max(0.5f, size.y),
                Mathf.Max(0.5f, size.z));

            if (fadeHeight < coreHeight)
                fadeHeight = coreHeight + 0.5f;

            if (applyPresetOnValidate && zoneType == FogZoneType.Interior)
                interiorExclusion = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            // В редакторе объект можно двигать — пересчитываем землю каждый раз.
            if (!Application.isPlaying)
                groundResolved = false;

            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Color c = IsInteriorExclusion ? new Color(1f, 0.4f, 0.3f, 0.25f) : gizmoColor;

            Gizmos.color = c;
            Gizmos.DrawCube(centerOffset, size);

            Gizmos.color = new Color(c.r, c.g, c.b, 1f);
            Gizmos.DrawWireCube(centerOffset, size);

            Gizmos.matrix = prev;

            if (!IsInteriorExclusion)
            {
                // визуализация слоя густого тумана у земли
                float ground = GroundLevel;
                Vector3 center = WorldCenter;

                Gizmos.color = new Color(0.9f, 0.95f, 1f, 0.8f);
                Gizmos.DrawWireCube(
                    new Vector3(center.x, ground + coreHeight * 0.5f, center.z),
                    new Vector3(size.x, Mathf.Max(coreHeight, 0.05f), size.z));
            }
        }
    }
}
