// =====================================================================
//  WWII Atmosphere — общая шейдерная библиотека тумана
//  Подключается и объёмным слоем (FogVolumetric), и частицами
//  (FogSoftParticle), чтобы оба слоя использовали ОДНУ И ТУ ЖЕ
//  функцию плотности, один шум и один расчёт света. Иначе слои
//  визуально расходятся и туман выглядит нереалистично.
// =====================================================================
#ifndef WWII_FOG_COMMON_INCLUDED
#define WWII_FOG_COMMON_INCLUDED

// Лимиты. Держим маленькими: каждый элемент считается на КАЖДОМ шаге
// рейтмарча, поэтому это самый дорогой параметр всей системы.
#define FOG_MAX_LIGHTS    8
#define FOG_MAX_INTERIORS 4
#define FOG_MAX_ZONES     4

#define FOG_PI 3.14159265359

// ---------------------------------------------------------------------
//  Процедурный 3D-шум. Генерируется в FogNoise.cs, ставится глобально.
//  R — крупная октава, G — мелкая. Оба слоя тумана берут его отсюда.
// ---------------------------------------------------------------------
TEXTURE3D(_FogNoise3D);
SAMPLER(sampler_FogNoise3D);

// ---------------------------------------------------------------------
//  Глобальные параметры (FogSystem / FogLightInteraction / FogVolumetricLayer)
// ---------------------------------------------------------------------
float4 _FogTintGlobal;        // rgb — цвет (альбедо) тумана
float  _FogDensityGlobal;     // 0..1 — общая плотность по расписанию
float4 _FogWindGlobal;        // xy — накопленное смещение, z — время анимации
float4 _FogMoonGlobal;        // rgb — цвет луны, w — интенсивность
float4 _FogSunGlobal;         // rgb — цвет солнца/луны, w — интенсивность
float4 _FogSunDirGlobal;      // xyz — направление, КУДА летит свет; w — не используется
float  _FogInteriorFade;      // 1 — камера снаружи, 0 — камера внутри помещения

// Источники света, подсвечивающие туман (фонарик всегда первый).
int    _FogLightCount;
float4 _FogLightPositions[FOG_MAX_LIGHTS];   // xyz — позиция, w — радиус
float4 _FogLightColors[FOG_MAX_LIGHTS];      // rgb — цвет, w — интенсивность
float4 _FogLightDirections[FOG_MAX_LIGHTS];  // xyz — направление, w — cos внешнего угла (-1 = точечный)

// Интерьеры: туман не заходит внутрь домов.
// Матрица переводит мир в локальный единичный куб (±0.5).
int      _FogInteriorCount;
float4x4 _FogInteriorMatrices[FOG_MAX_INTERIORS];

// Локальные зоны сгущения (дворы, низины). Тот же приём с матрицей.
int      _FogZoneCount;
float4x4 _FogZoneMatrices[FOG_MAX_ZONES];
float4   _FogZoneParams[FOG_MAX_ZONES];      // x — множитель плотности, y — мягкость края

// ---------------------------------------------------------------------
//  Утилиты
// ---------------------------------------------------------------------

/// Дешёвый хеш по экранным координатам. Нужен для дизеринга шагов
/// рейтмарча: без него видны концентрические «кольца» полос.
/// Намеренно СТАТИЧНЫЙ во времени — временной джиттер без TAA мерцает.
float FogDither(float2 pixelCoord)
{
    // Interleaved gradient noise: стабильный, хорошо распределённый паттерн.
    float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
    return frac(magic.z * frac(dot(pixelCoord, magic.xy)));
}

/// Фазовая функция Хеньи–Гринштейна: описывает, куда рассеивается свет
/// в капельной среде. g > 0 — прямое рассеивание: туман ярко светится,
/// когда смотришь В СТОРОНУ источника. Именно это даёт живой луч фонарика
/// и утреннее сияние против солнца.
float FogPhaseHG(float cosTheta, float g)
{
    float g2 = g * g;
    float denom = 1.0 + g2 - 2.0 * g * cosTheta;
    return (1.0 - g2) / (4.0 * FOG_PI * pow(max(denom, 1e-4), 1.5));
}

/// Нормированная фаза: 1.0 при изотропном рассеивании, удобно домножать.
float FogPhase(float cosTheta, float g)
{
    return FogPhaseHG(cosTheta, g) * 4.0 * FOG_PI;
}

/// Насколько точка внутри бокса, заданного матрицей мир→единичный куб.
/// 1 — глубоко внутри, 0 — снаружи, между — мягкий край (никаких резких границ).
float FogBoxMask(float4x4 worldToLocal, float3 positionWS, float feather)
{
    float3 local = mul(worldToLocal, float4(positionWS, 1.0)).xyz;
    float3 a = abs(local);

    float f = max(feather, 1e-3);
    float3 e = saturate((0.5 - a) / f);

    // smoothstep по каждой оси — мягче, чем линейное затухание
    e = e * e * (3.0 - 2.0 * e);
    return e.x * e.y * e.z;
}

/// Множитель плотности от интерьеров: 0 внутри домов, 1 снаружи.
float FogInteriorMask(float3 positionWS)
{
    float mask = 1.0;

    [loop]
    for (int i = 0; i < FOG_MAX_INTERIORS; i++)
    {
        if (i >= _FogInteriorCount) break;
        mask *= 1.0 - FogBoxMask(_FogInteriorMatrices[i], positionWS, 0.12);
    }

    return mask;
}

/// Множитель плотности от локальных зон: дворы и низины гуще, чем улица.
float FogZoneMultiplier(float3 positionWS)
{
    float result = 1.0;

    [loop]
    for (int i = 0; i < FOG_MAX_ZONES; i++)
    {
        if (i >= _FogZoneCount) break;

        float inside = FogBoxMask(_FogZoneMatrices[i], positionWS, _FogZoneParams[i].y);
        result *= lerp(1.0, _FogZoneParams[i].x, inside);
    }

    return result;
}

/// Один фетч 3D-шума. Две октавы лежат в каналах R и G,
/// поэтому крупная структура и детализация стоят одну выборку.
float2 FogSampleNoise3D(float3 positionWS, float scale, float scrollAmount)
{
    float3 uvw = positionWS * scale;

    // Массы тумана ползут по ветру; по вертикали — очень медленный подъём.
    uvw.xz += _FogWindGlobal.xy * scrollAmount;
    uvw.y += _FogWindGlobal.z * scrollAmount * 0.05;

    return SAMPLE_TEXTURE3D_LOD(_FogNoise3D, sampler_FogNoise3D, uvw, 0).rg;
}

/// Свет от ламп в точке. Для рейтмарча вызывается на каждом шаге,
/// поэтому лимит источников намеренно низкий.
/// rayDirWS — направление луча взгляда (от камеры к точке).
float3 FogLightScattering(float3 positionWS, float3 rayDirWS, float anisotropy, int maxLights)
{
    float3 result = 0;

    [loop]
    for (int i = 0; i < FOG_MAX_LIGHTS; i++)
    {
        if (i >= _FogLightCount || i >= maxLights) break;

        float3 lightPos = _FogLightPositions[i].xyz;
        float lightRange = max(_FogLightPositions[i].w, 0.001);

        float3 delta = lightPos - positionWS;
        float dist = length(delta);
        if (dist > lightRange) continue;

        float3 toLight = delta / max(dist, 1e-4);

        // Затухание: физичное 1/d² сглаженно обрезаем на границе радиуса,
        // иначе на краю светового шара видна ступенька. Коэффициент 0.06
        // мягче, чем классический 0.25: луч фонарика дотягивается далеко,
        // а не гаснет почти у ног.
        float normalized = dist / lightRange;
        float atten = saturate(1.0 - normalized * normalized);
        atten = atten * atten / (1.0 + dist * dist * 0.06);

        // Конус прожектора. w = -1 помечает точечный источник.
        float cosOuter = _FogLightDirections[i].w;
        float coneCos = dot(-toLight, _FogLightDirections[i].xyz);
        float cosInner = lerp(cosOuter, 1.0, 0.4);
        float spot = smoothstep(cosOuter, cosInner, coneCos);
        float isSpot = step(-0.995, cosOuter);
        float cone = lerp(1.0, spot, isSpot);
        if (cone <= 0.001) continue;

        // Смотрим вдоль луча в сторону лампы — туман светится ярче.
        float phase = FogPhase(dot(rayDirWS, toLight), anisotropy);

        result += _FogLightColors[i].rgb * _FogLightColors[i].w * atten * cone * phase;
    }

    return result;
}

/// Рассеивание от солнца/луны. Направленный свет одинаков во всём объёме,
/// поэтому считается ОДИН раз на пиксель, а не на каждом шаге.
float3 FogDirectionalScattering(float3 rayDirWS, float anisotropy)
{
    float3 toLight = -_FogSunDirGlobal.xyz;
    float phase = FogPhase(dot(rayDirWS, toLight), anisotropy);
    return _FogSunGlobal.rgb * _FogSunGlobal.w * phase;
}

/// Окружающее освещение тумана. Вверх — светлее (небо), вниз — темнее (земля).
/// Дешёвый, но убедительный признак объёма.
float3 FogAmbientScattering(float3 rayDirWS)
{
    float up = saturate(rayDirWS.y * 0.5 + 0.5);
    float3 tint = _FogTintGlobal.rgb;

    float3 ambient = lerp(tint * 0.55, tint * 1.15, up);
    ambient += _FogMoonGlobal.rgb * _FogMoonGlobal.w;

    return ambient;
}

#endif // WWII_FOG_COMMON_INCLUDED
