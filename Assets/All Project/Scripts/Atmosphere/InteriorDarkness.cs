using System.Collections.Generic;
using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Зона темноты в помещении.
    ///
    /// Внутри дома, подвала или блиндажа окружающий свет должен падать
    /// сильнее, чем на улице: снаружи хотя бы луна и небо, внутри —
    /// ничего. Компонент задаёт прямоугольную зону, при входе в которую
    /// ambient плавно гасится до заданного уровня. Именно так получается
    /// эффект, когда без фонарика в помещении не видно ничего.
    ///
    /// Само гашение выполняет DayNightLighting: единственный писатель
    /// в RenderSettings, чтобы зоны не перетирали друг друга. Этот
    /// компонент только сообщает, насколько темно должно быть в точке.
    ///
    /// Размещение: пустой GameObject внутри помещения. Размер задаётся
    /// полем Size, а не масштабом Transform.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("WWII/Atmosphere/Interior Darkness")]
    public class InteriorDarkness : MonoBehaviour
    {
        // =============================================================
        //  Геометрия
        // =============================================================
        [Header("Геометрия зоны (локальные координаты)")]
        [Tooltip("Размеры зоны по XYZ в метрах.")]
        [SerializeField] private Vector3 size = new Vector3(8f, 4f, 8f);

        [Tooltip("Смещение центра зоны относительно объекта.")]
        [SerializeField] private Vector3 centerOffset = Vector3.zero;

        [Tooltip("Ширина мягкой границы, м. Свет гаснет постепенно при входе в дверь.")]
        [SerializeField, Range(0f, 6f)] private float edgeFalloff = 1.5f;

        // =============================================================
        //  Затемнение
        // =============================================================
        [Header("Затемнение")]
        [Tooltip("Во сколько раз ослабить окружающий свет в центре зоны. " +
                 "0.1 — почти полная тьма, 0.5 — сумрак.")]
        [SerializeField, Range(0f, 1f)] private float ambientMultiplier = 0.12f;

        [Tooltip("Цвет полумрака внутри. Пусто-чёрный выглядит мёртво, " +
                 "лёгкий холодный оттенок читается лучше.")]
        [SerializeField] private Color interiorTint = new Color(0.06f, 0.07f, 0.09f);

        [Tooltip("Насколько сильно цвет зоны подменяет уличный. 1 — полностью.")]
        [SerializeField, Range(0f, 1f)] private float tintBlend = 0.85f;

        [Tooltip("Работает и днём. Выключить, если помещение должно темнеть только ночью.")]
        [SerializeField] private bool activeInDaytime = true;

        [Header("Дальняя дымка внутри")]
        [Tooltip("Гасить встроенный туман сцены внутри помещения — " +
                 "иначе далёкие стены выглядят как серая пелена.")]
        [SerializeField] private bool suppressSceneFog = true;

        // =============================================================
        //  Отладка
        // =============================================================
        [Header("Отладка")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color gizmoColor = new Color(0.15f, 0.1f, 0.35f, 0.3f);

        // =============================================================
        //  Реестр зон
        // =============================================================
        private static readonly List<InteriorDarkness> zones = new List<InteriorDarkness>(16);

        /// <summary>Все активные зоны темноты на сцене.</summary>
        public static IReadOnlyList<InteriorDarkness> Zones => zones;

        /// <summary>Подавляется ли дальняя дымка в этой зоне.</summary>
        public bool SuppressSceneFog => suppressSceneFog;

        private void OnEnable()
        {
            if (!zones.Contains(this)) zones.Add(this);
        }

        private void OnDisable()
        {
            zones.Remove(this);
        }

        // =============================================================
        //  Расчёт
        // =============================================================
        /// <summary>
        /// Вес зоны в мировой точке: 0 снаружи, 1 в глубине помещения.
        /// Мягкая граница даёт плавное затемнение в проёме двери.
        /// </summary>
        public float Evaluate(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition) - centerOffset;
            Vector3 half = size * 0.5f;

            float wx = AxisWeight(local.x, half.x);
            if (wx <= 0f) return 0f;

            float wy = AxisWeight(local.y, half.y);
            if (wy <= 0f) return 0f;

            float wz = AxisWeight(local.z, half.z);
            if (wz <= 0f) return 0f;

            return wx * wy * wz;
        }

        /// <summary>Вес вдоль одной оси с мягким краем.</summary>
        private float AxisWeight(float localCoord, float halfExtent)
        {
            float distanceToEdge = halfExtent - Mathf.Abs(localCoord);
            if (distanceToEdge <= 0f) return 0f;

            float falloff = Mathf.Min(edgeFalloff, halfExtent);
            if (falloff <= 0.001f) return 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distanceToEdge / falloff));
        }

        /// <summary>Множитель яркости этой зоны с учётом веса точки.</summary>
        private float BrightnessAt(float weight)
        {
            return Mathf.Lerp(1f, ambientMultiplier, weight);
        }

        /// <summary>
        /// Суммарное затемнение в точке по всем зонам: берём самую тёмную.
        /// Возвращает false, если точка снаружи всех зон.
        /// </summary>
        public static bool SampleDarkness(Vector3 worldPosition, bool isNight,
                                          out float brightnessMultiplier,
                                          out Color tint, out float tintWeight,
                                          out bool suppressFog)
        {
            brightnessMultiplier = 1f;
            tint = Color.black;
            tintWeight = 0f;
            suppressFog = false;

            bool any = false;

            for (int i = zones.Count - 1; i >= 0; i--)
            {
                InteriorDarkness zone = zones[i];

                if (zone == null)
                {
                    zones.RemoveAt(i);
                    continue;
                }

                if (!zone.activeInDaytime && !isNight) continue;

                float weight = zone.Evaluate(worldPosition);
                if (weight <= 0.001f) continue;

                float brightness = zone.BrightnessAt(weight);

                // Самая тёмная зона задаёт результат — вложенные объёмы
                // (дом внутри двора) работают предсказуемо.
                if (brightness < brightnessMultiplier)
                {
                    brightnessMultiplier = brightness;
                    tint = zone.interiorTint;
                    tintWeight = zone.tintBlend * weight;
                    suppressFog = zone.suppressSceneFog;
                }

                any = true;
            }

            return any;
        }

        /// <summary>Находится ли точка хоть в одной зоне темноты.</summary>
        public static bool IsInsideAny(Vector3 worldPosition)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                if (zones[i] != null && zones[i].Evaluate(worldPosition) > 0.001f)
                    return true;
            }

            return false;
        }

        // =============================================================
        //  Настройка из кода / редактора
        // =============================================================
        /// <summary>Задать габариты зоны (используется визардом).</summary>
        public void Configure(Vector3 zoneSize, Vector3 offset, float darkness)
        {
            size = zoneSize;
            centerOffset = offset;
            ambientMultiplier = Mathf.Clamp01(darkness);
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
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.color = gizmoColor;
            Gizmos.DrawCube(centerOffset, size);

            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
            Gizmos.DrawWireCube(centerOffset, size);

            Gizmos.matrix = prev;
        }
    }
}
