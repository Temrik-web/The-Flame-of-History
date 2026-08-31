using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Уровень качества (LOD) системы тумана.
    /// Определяет число шагов рейтмарча и то, какие ветви шейдеров активны.
    /// </summary>
    public enum FogQuality
    {
        /// <summary>Слабое железо: 12 шагов марча, только фонарик, без детализации.</summary>
        Low = 0,

        /// <summary>Средние настройки: 20 шагов, все лампы, мягкие частицы.</summary>
        Medium = 1,

        /// <summary>Полное качество: 32 шага, вторая октава шума.</summary>
        High = 2
    }

    /// <summary>
    /// Тип зоны тумана. Влияет на высоту, плотность и поведение частиц.
    /// </summary>
    public enum FogZoneType
    {
        /// <summary>Открытая площадь, поле — туман поднимается выше.</summary>
        OpenGround = 0,

        /// <summary>Улица между домами — вытянутые медленные пласты.</summary>
        Street = 1,

        /// <summary>Двор, замкнутое пространство — туман скапливается, плотнее.</summary>
        Courtyard = 2,

        /// <summary>Низина, воронка, канал — самый густой стелющийся туман.</summary>
        Lowland = 3,

        /// <summary>Интерьер: туман сюда не заходит (зона-исключение).</summary>
        Interior = 4
    }

    /// <summary>
    /// Единая точка доступа к глобальным параметрам шейдеров тумана.
    /// Кэширует ID свойств, чтобы не искать их по строке каждый кадр.
    ///
    /// Оба слоя тумана (объёмный и частицы) читают эти же значения,
    /// поэтому выглядят как одна среда, а не как два независимых эффекта.
    /// </summary>
    public static class FogGlobals
    {
        /// <summary>Максимум источников света, которые шейдеры учитывают одновременно.</summary>
        public const int MaxLights = 8;

        /// <summary>Максимум интерьерных боксов, вычитаемых из тумана.</summary>
        public const int MaxInteriors = 4;

        /// <summary>Максимум локальных зон сгущения, передаваемых в шейдер.</summary>
        public const int MaxZones = 4;

        /// <summary>Имя шейдера объёмного тумана.</summary>
        public const string VolumetricShaderName = "WWII/Fog Volumetric";

        /// <summary>Имя шейдера частиц тумана.</summary>
        public const string ShaderName = "WWII/Fog Soft Particle";

        // --- ключевые слова качества ---
        public const string KeywordSoftParticles = "_FOG_SOFT_PARTICLES";
        public const string KeywordLights = "_FOG_LIGHTS";
        public const string KeywordDetail = "_FOG_DETAIL";
        public const string KeywordStepsLow = "_FOG_STEPS_LOW";
        public const string KeywordStepsMed = "_FOG_STEPS_MED";
        public const string KeywordStepsHigh = "_FOG_STEPS_HIGH";

        // --- глобальные свойства среды ---
        public static readonly int TintId = Shader.PropertyToID("_FogTintGlobal");
        public static readonly int DensityId = Shader.PropertyToID("_FogDensityGlobal");
        public static readonly int WindId = Shader.PropertyToID("_FogWindGlobal");
        public static readonly int MoonId = Shader.PropertyToID("_FogMoonGlobal");
        public static readonly int SunId = Shader.PropertyToID("_FogSunGlobal");
        public static readonly int SunDirId = Shader.PropertyToID("_FogSunDirGlobal");
        public static readonly int InteriorFadeId = Shader.PropertyToID("_FogInteriorFade");
        public static readonly int Noise3DId = Shader.PropertyToID("_FogNoise3D");

        // --- источники света ---
        public static readonly int LightCountId = Shader.PropertyToID("_FogLightCount");
        public static readonly int LightPositionsId = Shader.PropertyToID("_FogLightPositions");
        public static readonly int LightColorsId = Shader.PropertyToID("_FogLightColors");
        public static readonly int LightDirectionsId = Shader.PropertyToID("_FogLightDirections");

        // --- интерьеры и зоны ---
        public static readonly int InteriorCountId = Shader.PropertyToID("_FogInteriorCount");
        public static readonly int InteriorMatricesId = Shader.PropertyToID("_FogInteriorMatrices");
        public static readonly int ZoneCountId = Shader.PropertyToID("_FogZoneCount");
        public static readonly int ZoneMatricesId = Shader.PropertyToID("_FogZoneMatrices");
        public static readonly int ZoneParamsId = Shader.PropertyToID("_FogZoneParams");

        // --- свойства материала объёмного тумана ---
        public static readonly int VolumeDensityId = Shader.PropertyToID("_FogDensity");
        public static readonly int BaseHeightId = Shader.PropertyToID("_BaseHeight");
        public static readonly int FalloffHeightId = Shader.PropertyToID("_FalloffHeight");
        public static readonly int MaxDistanceId = Shader.PropertyToID("_MaxDistance");
        public static readonly int AnisotropyId = Shader.PropertyToID("_Anisotropy");
        public static readonly int SunScatterId = Shader.PropertyToID("_SunScatter");
        public static readonly int AmbientScatterId = Shader.PropertyToID("_AmbientScatter");

        // --- свойства материала частиц ---
        public static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        public static readonly int NoiseTexId = Shader.PropertyToID("_NoiseTex");
        public static readonly int MaterialDensityId = Shader.PropertyToID("_Density");
        public static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");
        public static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
        public static readonly int SoftFadeId = Shader.PropertyToID("_SoftFade");
        public static readonly int NearFadeId = Shader.PropertyToID("_NearFade");
        public static readonly int LightScatterId = Shader.PropertyToID("_LightScatter");
        public static readonly int MoonGlowId = Shader.PropertyToID("_MoonGlow");

        // Переиспользуемые буферы: SetGlobalVectorArray требует массив
        // фиксированной длины, а аллокации каждый кадр недопустимы.
        private static readonly Vector4[] emptyLights = new Vector4[MaxLights];
        private static readonly Matrix4x4[] emptyMatrices = new Matrix4x4[MaxInteriors];

        /// <summary>Цвет (альбедо) тумана для всех слоёв сцены.</summary>
        public static void SetTint(Color tint)
        {
            Shader.SetGlobalVector(TintId, tint);
        }

        /// <summary>Общая плотность 0..1. Один множитель на всю систему.</summary>
        public static void SetDensity(float density)
        {
            Shader.SetGlobalFloat(DensityId, Mathf.Clamp01(density));
        }

        /// <summary>Накопленное смещение масс тумана (ветер) и время анимации.</summary>
        public static void SetWind(Vector2 offset, float time)
        {
            Shader.SetGlobalVector(WindId, new Vector4(offset.x, offset.y, time, 0f));
        }

        /// <summary>Цвет и сила лунного подсвета.</summary>
        public static void SetMoon(Color color, float intensity)
        {
            Shader.SetGlobalVector(MoonId, new Vector4(color.r, color.g, color.b, Mathf.Max(0f, intensity)));
        }

        /// <summary>
        /// Основной направленный свет (солнце или луна), рассеивающийся в тумане.
        /// direction — куда летит свет (light.transform.forward).
        /// </summary>
        public static void SetSun(Color color, float intensity, Vector3 direction)
        {
            Shader.SetGlobalVector(SunId, new Vector4(color.r, color.g, color.b, Mathf.Max(0f, intensity)));

            Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.down;
            Shader.SetGlobalVector(SunDirId, new Vector4(dir.x, dir.y, dir.z, 0f));
        }

        /// <summary>
        /// Насколько объёмный туман виден: 1 — камера снаружи, 0 — внутри помещения.
        /// Плавно интерполируется, чтобы на входе в дом не было щелчка.
        /// </summary>
        public static void SetInteriorFade(float fade)
        {
            Shader.SetGlobalFloat(InteriorFadeId, Mathf.Clamp01(fade));
        }

        /// <summary>Установить 3D-текстуру шума для всех слоёв тумана.</summary>
        public static void SetNoise3D(Texture texture)
        {
            if (texture != null)
                Shader.SetGlobalTexture(Noise3DId, texture);
        }

        /// <summary>Передать в шейдеры список ламп, подсвечивающих туман.</summary>
        public static void SetLights(int count, Vector4[] positions, Vector4[] colors, Vector4[] directions)
        {
            Shader.SetGlobalInt(LightCountId, Mathf.Clamp(count, 0, MaxLights));
            Shader.SetGlobalVectorArray(LightPositionsId, positions);
            Shader.SetGlobalVectorArray(LightColorsId, colors);
            Shader.SetGlobalVectorArray(LightDirectionsId, directions);
        }

        /// <summary>Сбросить подсветку (например, при выгрузке сцены).</summary>
        public static void ClearLights()
        {
            Shader.SetGlobalInt(LightCountId, 0);
        }

        /// <summary>
        /// Передать боксы помещений. Матрицы должны переводить мировые
        /// координаты в локальный единичный куб (±0.5).
        /// </summary>
        public static void SetInteriors(int count, Matrix4x4[] matrices)
        {
            Shader.SetGlobalInt(InteriorCountId, Mathf.Clamp(count, 0, MaxInteriors));
            if (count > 0)
                Shader.SetGlobalMatrixArray(InteriorMatricesId, matrices);
        }

        /// <summary>
        /// Передать локальные зоны сгущения.
        /// params: x — множитель плотности, y — мягкость края (0..0.5).
        /// </summary>
        public static void SetZones(int count, Matrix4x4[] matrices, Vector4[] parameters)
        {
            Shader.SetGlobalInt(ZoneCountId, Mathf.Clamp(count, 0, MaxZones));
            if (count > 0)
            {
                Shader.SetGlobalMatrixArray(ZoneMatricesId, matrices);
                Shader.SetGlobalVectorArray(ZoneParamsId, parameters);
            }
        }

        /// <summary>Значения по умолчанию — чтобы туман не был чёрным до первого кадра.</summary>
        public static void ApplyDefaults()
        {
            SetTint(new Color(0.55f, 0.6f, 0.68f));
            SetDensity(0f);
            SetWind(Vector2.zero, 0f);
            SetMoon(new Color(0.6f, 0.7f, 0.9f), 0f);
            SetSun(Color.white, 0f, Vector3.down);
            SetInteriorFade(1f);
            SetNoise3D(FogNoise.GetNoise3D());

            ClearLights();
            Shader.SetGlobalVectorArray(LightPositionsId, emptyLights);
            Shader.SetGlobalVectorArray(LightColorsId, emptyLights);
            Shader.SetGlobalVectorArray(LightDirectionsId, emptyLights);

            SetInteriors(0, emptyMatrices);
            SetZones(0, emptyMatrices, emptyLights);
        }
    }
}
