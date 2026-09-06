// =====================================================================
//  WWII Atmosphere — частицы тумана (детализирующий слой)
//
//  РОЛЬ В СИСТЕМЕ:
//  Основной объём делает FogVolumetric (рейтмарч). Частицы добавляют
//  крупные пласты у земли, которые дают паралакс при движении.
//
//  ЧТОБЫ НЕ БЫЛО «ОВАЛОВ»:
//   * частицы рендерятся ГОРИЗОНТАЛЬНЫМИ пластами (Horizontal Billboard),
//     а не плоскостями «лицом к камере» — плоскость, повёрнутая к игроку,
//     и есть источник овалов;
//   * альфа стремится к нулю у камеры: стоя в тумане, вы не видите форму;
//   * альфа падает, когда смотришь на пласт сверху (он выдаёт свою плоскость);
//   * форма размывается 3D-шумом В МИРОВЫХ координатах, поэтому граница
//     видимого тумана не совпадает с границей спрайта;
//   * тот же шум и тот же свет, что у объёмного слоя — слои не расходятся.
// =====================================================================
Shader "WWII/Fog Soft Particle"
{
    Properties
    {
        [Header(Forma)]
        _MainTex        ("Форма клубка (альфа)", 2D) = "white" {}
        _Density        ("Множитель плотности материала", Range(0, 2)) = 1
        _EdgeSoftness   ("Мягкость краёв", Range(0.01, 1)) = 0.6

        [Header(Shum)]
        _NoiseStrength  ("Сила разрушения формы шумом", Range(0, 1)) = 0.8
        _NoiseScale     ("Масштаб шума (1/м)", Range(0.005, 0.3)) = 0.05
        _ScrollSpeed    ("Скорость проплывания", Range(0, 2)) = 0.35

        [Header(Zatuhaniya)]
        _SoftFade       ("Мягкость пересечения со стенами, м", Range(0.05, 10)) = 3
        _NearFade       ("Начало затухания у камеры, м", Range(0, 10)) = 1
        _NearFadeRange  ("Длина затухания у камеры, м", Range(0.1, 20)) = 6
        _InsideFade     ("Радиус гашения «изнутри тумана», м", Range(0.5, 40)) = 12
        _GrazingFade    ("Гашение при взгляде сверху на пласт", Range(0, 1)) = 0.6

        [Header(Svet)]
        _Anisotropy     ("Направленность рассеивания", Range(0, 0.95)) = 0.55
        _LightScatter   ("Сила подсветки лампами", Range(0, 8)) = 2.2
        _SunScatter     ("Сила подсветки солнцем/луной", Range(0, 4)) = 1
        _AmbientScatter ("Окружающее свечение", Range(0, 2)) = 0.6
        _MoonGlow       ("Свечение в лунном свете", Range(0, 2)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType"     = "Plane"
        }

        Pass
        {
            Name "FogParticleForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha   // premultiplied alpha — мягкие края без ореолов
            ZWrite Off
            ZTest LEqual
            Cull Off
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex   FogVert
            #pragma fragment FogFrag
            #pragma target 3.0

            #pragma multi_compile _ _FOG_SOFT_PARTICLES
            #pragma multi_compile _ _FOG_LIGHTS
            #pragma multi_compile _ _FOG_DETAIL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "FogCommon.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float  _Density;
                float  _EdgeSoftness;
                float  _NoiseStrength;
                float  _NoiseScale;
                float  _ScrollSpeed;
                float  _SoftFade;
                float  _NearFade;
                float  _NearFadeRange;
                float  _InsideFade;
                float  _GrazingFade;
                float  _Anisotropy;
                float  _LightScatter;
                float  _SunScatter;
                float  _AmbientScatter;
                float  _MoonGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 color      : COLOR;
            };

            Varyings FogVert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = vpi.positionCS;
                output.positionWS = vpi.positionWS;
                output.screenPos  = vpi.positionNDC;
                output.uv         = TRANSFORM_TEX(input.uv, _MainTex);
                output.color      = input.color;
                return output;
            }

            float4 FogFrag(Varyings input) : SV_Target
            {
                // Внутри помещения частицы не рисуем.
                if (_FogInteriorFade < 0.002 || _FogDensityGlobal < 0.002)
                    discard;

                // --- форма клубка ---
                float4 shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float alpha = shape.a;

                // --- разрушение формы 3D-шумом в МИРОВЫХ координатах ---
                // Главный приём против «овалов»: видимая граница тумана
                // определяется шумом в пространстве, а не контуром спрайта.
                float2 noise = FogSampleNoise3D(input.positionWS, _NoiseScale, _ScrollSpeed);
                float n = noise.r;

                #ifdef _FOG_DETAIL
                    n = saturate(n * 0.7 + noise.g * 0.3);
                #endif

                n = saturate(n * 1.7 - 0.2);
                alpha *= lerp(1.0, n, _NoiseStrength);
                alpha = smoothstep(0.0, _EdgeSoftness, alpha);

                float eyeDepth = input.screenPos.w;

                // --- мягкое пересечение со стенами домов ---
                #ifdef _FOG_SOFT_PARTICLES
                    float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 0.0001);
                    float rawDepth = SampleSceneDepth(screenUV);
                    float sceneDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                    alpha *= saturate((sceneDepth - eyeDepth) / _SoftFade);
                #endif

                // --- затухание у камеры ---
                alpha *= saturate((eyeDepth - _NearFade) / _NearFadeRange);

                // --- гашение «изнутри тумана» ---
                // Когда игрок стоит в тумане, ближние пласты гасятся почти
                // полностью. Иначе именно они читаются как плоские пятна,
                // а объём в этой области уже отрисовал FogVolumetric.
                alpha *= smoothstep(0.0, _InsideFade, eyeDepth);

                // --- гашение при взгляде сверху ---
                // Пласты горизонтальные. Глядя на них сверху вниз, игрок
                // видит плоскость; на скользящих углах плоскость незаметна.
                // Поэтому гасим тем сильнее, чем вертикальнее взгляд.
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float verticality = abs(viewDirWS.y);
                alpha *= lerp(1.0, 1.0 - verticality * 0.9, _GrazingFade);

                // --- итоговое покрытие ---
                alpha *= input.color.a * _Density * _FogDensityGlobal * _FogInteriorFade;
                alpha *= FogInteriorMask(input.positionWS);
                alpha *= FogZoneMultiplier(input.positionWS);

                alpha = saturate(alpha);
                if (alpha <= 0.003) discard;

                // --- освещение: та же модель, что у объёмного слоя ---
                float3 rayDir = -viewDirWS;

                float3 light = FogAmbientScattering(rayDir) * _AmbientScatter;
                light += FogDirectionalScattering(rayDir, _Anisotropy) * _SunScatter;
                light += FogLightScattering(input.positionWS, rayDir, _Anisotropy, FOG_MAX_LIGHTS) * _LightScatter;
                light += _FogMoonGlobal.rgb * (_FogMoonGlobal.w * _MoonGlow);

                float3 color = light * _FogTintGlobal.rgb * input.color.rgb;

                // premultiplied alpha
                return float4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
