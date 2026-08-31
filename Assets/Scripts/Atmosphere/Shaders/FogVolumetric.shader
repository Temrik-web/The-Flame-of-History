// =====================================================================
//  WWII Atmosphere — объёмный туман (raymarched height fog)
//
//  ЗАЧЕМ ЭТОТ ШЕЙДЕР:
//  Билборд-частицы принципиально не могут выглядеть реалистично, когда
//  камера стоит ВНУТРИ тумана: игрок видит плоские овалы, потому что
//  билборд — это плоскость, а не объём. Здесь туман считается честным
//  интегралом плотности вдоль луча взгляда, поэтому «овалов» нет
//  физически: у тумана вообще нет формы, только плотность в точке.
//
//  Рендерится на кваде, прикреплённом к камере (FogVolumetricLayer.cs),
//  сразу после непрозрачной геометрии. Пост-обработка не используется.
// =====================================================================
Shader "WWII/Fog Volumetric"
{
    Properties
    {
        [Header(Plotnost i vysota)]
        _FogDensity      ("Плотность на уровне земли", Range(0, 1)) = 0.09
        _BaseHeight      ("Высота основания тумана, м", Float) = 0
        _FalloffHeight   ("Высота спада плотности, м", Range(0.5, 40)) = 4
        _MaxDistance     ("Максимальная дальность расчёта, м", Range(20, 500)) = 160

        [Header(Shum)]
        _NoiseScale      ("Масштаб крупных масс (1/м)", Range(0.002, 0.2)) = 0.022
        _NoiseDetailScale("Масштаб детализации (1/м)", Range(0.01, 0.6)) = 0.09
        _NoiseStrength   ("Сила неоднородности", Range(0, 1)) = 0.75
        _NoiseScroll     ("Скорость проплывания масс", Range(0, 2)) = 0.35
        _DetailStrength  ("Вклад мелкой детализации", Range(0, 1)) = 0.35

        [Header(Rasseivanie sveta)]
        _Anisotropy      ("Направленность рассеивания (g)", Range(0, 0.95)) = 0.6
        _LightScatter    ("Сила подсветки лампами", Range(0, 8)) = 2.5
        _SunScatter      ("Сила подсветки солнцем/луной", Range(0, 4)) = 1
        _AmbientScatter  ("Сила окружающего свечения", Range(0, 2)) = 0.5

        [Header(Kachestvo)]
        _StepJitter      ("Дизеринг шагов (убирает полосы)", Range(0, 1)) = 1
        _NearFade        ("Затухание вплотную к камере, м", Range(0, 5)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent-100"   // раньше частиц и прочей прозрачности
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "FogVolumetric"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplied alpha: цвет уже умножен на покрытие.
            // Единственный корректный блендинг для накопленного рассеивания.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always      // квад может пересекать геометрию — глубину читаем сами
            Cull Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex   FogVolVert
            #pragma fragment FogVolFrag
            #pragma target 3.5

            // Число шагов рейтмарча — главный рычаг производительности.
            #pragma multi_compile _FOG_STEPS_LOW _FOG_STEPS_MED _FOG_STEPS_HIGH
            #pragma multi_compile _ _FOG_DETAIL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FogCommon.hlsl"

            // Компромисс качество/скорость. Даже 12 шагов выглядят гладко
            // благодаря дизерингу и экспоненциальному распределению.
            #if defined(_FOG_STEPS_HIGH)
                #define FOG_STEPS 32
            #elif defined(_FOG_STEPS_MED)
                #define FOG_STEPS 20
            #else
                #define FOG_STEPS 12
            #endif

            // Лампы дороги: на каждом шаге — цикл по источникам.
            // На низком качестве считаем только фонарик.
            #if defined(_FOG_STEPS_LOW)
                #define FOG_STEP_LIGHTS 1
            #else
                #define FOG_STEP_LIGHTS FOG_MAX_LIGHTS
            #endif

            CBUFFER_START(UnityPerMaterial)
                float _FogDensity;
                float _BaseHeight;
                float _FalloffHeight;
                float _MaxDistance;
                float _NoiseScale;
                float _NoiseDetailScale;
                float _NoiseStrength;
                float _NoiseScroll;
                float _DetailStrength;
                float _Anisotropy;
                float _LightScatter;
                float _SunScatter;
                float _AmbientScatter;
                float _StepJitter;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings FogVolVert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            // -----------------------------------------------------------------
            //  Плотность тумана в точке.
            //  Экспоненциальный профиль по высоте + 3D-шум + зоны + интерьеры.
            //  Ту же логику использует FogSoftParticle — слои совпадают.
            // -----------------------------------------------------------------
            float FogDensityAt(float3 positionWS)
            {
                // --- высотный профиль: туман лежит на земле и редеет вверх ---
                float h = (positionWS.y - _BaseHeight) / max(_FalloffHeight, 0.01);
                float height = exp(-max(h, 0.0));

                // Ниже основания плотность не растёт бесконечно — упирается в максимум.
                height = min(height, 1.0);
                if (height < 0.002) return 0.0;

                // --- неоднородность: зоны сгущения и разрежения ---
                float2 noise = FogSampleNoise3D(positionWS, _NoiseScale, _NoiseScroll);
                float n = noise.r;

                #ifdef _FOG_DETAIL
                    // Вторая октава: рваные края масс тумана.
                    float2 detail = FogSampleNoise3D(positionWS, _NoiseDetailScale, _NoiseScroll * 2.3);
                    n = saturate(n - (1.0 - detail.g) * _DetailStrength * 0.5);
                #endif

                // Контраст шума: получаем ясные «окна» и густые пласты,
                // а не однородную серую пелену.
                n = saturate(n * 1.6 - 0.25);
                float density = lerp(1.0, n, _NoiseStrength);

                density *= height;
                density *= FogZoneMultiplier(positionWS);
                density *= FogInteriorMask(positionWS);

                return density * _FogDensity * _FogDensityGlobal;
            }

            float4 FogVolFrag(Varyings input) : SV_Target
            {
                // Камера внутри помещения — объёмный туман не рисуем.
                if (_FogInteriorFade < 0.002 || _FogDensityGlobal < 0.002)
                    return float4(0, 0, 0, 0);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);

                // --- луч взгляда ---
                float3 cameraPos = _WorldSpaceCameraPos;
                float3 rayDir = normalize(input.positionWS - cameraPos);

                // --- где луч упирается в геометрию ---
                float rawDepth = SampleSceneDepth(screenUV);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // eyeDepth измерен вдоль оси камеры; переводим в длину вдоль луча.
                // GetViewForwardDir() смотрит от камеры вперёд.
                float cosAngle = max(dot(rayDir, GetViewForwardDir()), 1e-3);
                float sceneDistance = eyeDepth / cosAngle;

                // Небо (depth == far) не ограничивает луч.
                float far = _ProjectionParams.z;
                bool isSky = eyeDepth >= far * 0.999;
                float rayEnd = min(isSky ? _MaxDistance : sceneDistance, _MaxDistance);
                float rayStart = _NearFade;

                if (rayEnd <= rayStart)
                    return float4(0, 0, 0, 0);

                // --- подготовка марша ---
                float totalLength = rayEnd - rayStart;
                float stepSize = totalLength / FOG_STEPS;

                // Дизеринг стартовой позиции. Без него на градиенте плотности
                // видны концентрические кольца — артефакт фиксированных шагов.
                float jitter = FogDither(input.positionCS.xy) * _StepJitter;
                float t = rayStart + stepSize * jitter;

                // Направленный и окружающий свет одинаковы во всём объёме,
                // поэтому считаются один раз на пиксель, а не на каждом шаге.
                float3 sunLight = FogDirectionalScattering(rayDir, _Anisotropy) * _SunScatter;
                float3 ambientLight = FogAmbientScattering(rayDir) * _AmbientScatter;
                float3 uniformLight = sunLight + ambientLight;

                float3 scattering = 0;
                float transmittance = 1.0;

                [loop]
                for (int i = 0; i < FOG_STEPS; i++)
                {
                    float3 samplePos = cameraPos + rayDir * t;

                    float density = FogDensityAt(samplePos);

                    if (density > 0.0005)
                    {
                        // Закон Бугера–Ламберта: сколько света погасил этот отрезок.
                        float extinction = density * stepSize;
                        float sampleTransmittance = exp(-extinction);

                        float3 light = uniformLight;
                        light += FogLightScattering(samplePos, rayDir, _Anisotropy, FOG_STEP_LIGHTS) * _LightScatter;

                        // Энергосохраняющее накопление: интеграл рассеивания
                        // на отрезке, а не грубое domножение на длину шага.
                        // Убирает зависимость яркости от числа шагов.
                        float3 stepScattering = light * (1.0 - sampleTransmittance);
                        scattering += stepScattering * transmittance;

                        transmittance *= sampleTransmittance;

                        // Дальше туман уже ничего не покажет — выходим.
                        if (transmittance < 0.01) break;
                    }

                    t += stepSize;
                }

                float alpha = saturate(1.0 - transmittance) * _FogInteriorFade;
                if (alpha < 0.002) return float4(0, 0, 0, 0);

                // Цвет тумана как среды: альбедо × накопленное освещение.
                float3 color = scattering * _FogTintGlobal.rgb;

                // Premultiplied alpha — scattering уже учитывает покрытие.
                return float4(color * _FogInteriorFade, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
