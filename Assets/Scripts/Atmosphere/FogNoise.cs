using UnityEngine;

namespace WWII.Atmosphere
{
    /// <summary>
    /// Процедурная генерация текстур для тумана.
    /// Никаких внешних ассетов: всё создаётся в рантайме один раз и кэшируется.
    ///
    /// Главный ресурс — бесшовная 3D-текстура шума <see cref="GetNoise3D"/>.
    /// Именно она делает туман неоднородным в объёме: без неё любой туман
    /// выглядит однородной пеленой. 2D-текстуры нужны только частицам.
    /// </summary>
    public static class FogNoise
    {
        private static Texture3D cachedNoise3D;
        private static Texture2D cachedNoise;
        private static Texture2D cachedPuff;

        // =============================================================
        //  3D шум — основа объёмного тумана
        // =============================================================
        /// <summary>
        /// Бесшовная 3D-текстура шума.
        /// Канал R — крупные массы (Worley/клеточный, даёт «клубы»),
        /// канал G — фрактальный value-шум для мелкой рваности.
        ///
        /// Worley в R выбран намеренно: обычный Perlin даёт «мыльную» ровную
        /// пелену, а клеточный шум создаёт плотные ядра с прогалинами между
        /// ними — именно так выглядит реальный туман.
        ///
        /// Разрешение 32³ хватает: детализация берётся из второй октавы
        /// и высокой частоты тайлинга. 32³ RGBA32 = 128 КБ.
        /// </summary>
        public static Texture3D GetNoise3D(int resolution = 32, int seed = 1337)
        {
            if (cachedNoise3D != null && cachedNoise3D.width == resolution)
                return cachedNoise3D;

            if (cachedNoise3D != null)
                Object.Destroy(cachedNoise3D);

            Texture3D tex = new Texture3D(resolution, resolution, resolution, TextureFormat.RGBA32, true)
            {
                name = "FogNoise3D_Procedural",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0
            };

            // --- точки для клеточного шума (Worley) ---
            // Разбиваем куб на сетку и кладём по точке в каждую ячейку.
            // Тайлинг обеспечивается взятием индексов ячеек по модулю.
            const int cellsPerAxis = 4;
            Vector3[] featurePoints = new Vector3[cellsPerAxis * cellsPerAxis * cellsPerAxis];

            Random.State prevState = Random.state;
            Random.InitState(seed);

            for (int i = 0; i < featurePoints.Length; i++)
            {
                featurePoints[i] = new Vector3(Random.value, Random.value, Random.value);
            }

            Vector3 valueOffset = new Vector3(Random.value * 64f, Random.value * 64f, Random.value * 64f);
            Random.state = prevState;

            Color32[] pixels = new Color32[resolution * resolution * resolution];
            float inv = 1f / resolution;

            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    int rowStart = z * resolution * resolution + y * resolution;

                    for (int x = 0; x < resolution; x++)
                    {
                        Vector3 p = new Vector3(x * inv, y * inv, z * inv);

                        // Инвертированный Worley: 1 в ядрах клубов, 0 в прогалинах.
                        float worley = 1f - TileableWorley(p, featurePoints, cellsPerAxis);
                        worley = Mathf.SmoothStep(0.05f, 0.75f, worley);

                        // Фрактальный value-шум: мелкая рваность краёв.
                        float fbm = TileableValueFbm(p, 4, 3, valueOffset);
                        fbm = Mathf.SmoothStep(0.2f, 0.85f, fbm);

                        // Смешиваем: крупные клубы, слегка изъеденные детализацией.
                        float coarse = Mathf.Clamp01(worley * 0.75f + fbm * 0.25f);

                        pixels[rowStart + x] = new Color32(
                            (byte)(coarse * 255f),
                            (byte)(fbm * 255f),
                            0, 255);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, false);

            cachedNoise3D = tex;
            return cachedNoise3D;
        }

        /// <summary>
        /// Бесшовный клеточный шум (Worley F1): расстояние до ближайшей
        /// характеристической точки. Индексы ячеек берутся по модулю,
        /// поэтому текстура тайлится без швов.
        /// </summary>
        private static float TileableWorley(Vector3 p, Vector3[] points, int cells)
        {
            Vector3 scaled = p * cells;
            int cx = Mathf.FloorToInt(scaled.x);
            int cy = Mathf.FloorToInt(scaled.y);
            int cz = Mathf.FloorToInt(scaled.z);

            float minDistSqr = float.MaxValue;

            // Проверяем 27 соседних ячеек — ближайшая точка может быть в любой.
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx;
                        int ny = cy + dy;
                        int nz = cz + dz;

                        // Оборачиваем индекс — это и даёт бесшовность.
                        int wx = ((nx % cells) + cells) % cells;
                        int wy = ((ny % cells) + cells) % cells;
                        int wz = ((nz % cells) + cells) % cells;

                        Vector3 point = points[(wz * cells + wy) * cells + wx];

                        // Позиция точки в непрерывных координатах соседней ячейки.
                        Vector3 worldPoint = new Vector3(nx + point.x, ny + point.y, nz + point.z);

                        float distSqr = (scaled - worldPoint).sqrMagnitude;
                        if (distSqr < minDistSqr) minDistSqr = distSqr;
                    }
                }
            }

            // Нормируем: типичное расстояние до ближайшей точки ~0.5 ячейки.
            return Mathf.Clamp01(Mathf.Sqrt(minDistSqr));
        }

        /// <summary>Фрактальный бесшовный value-шум в 3D.</summary>
        private static float TileableValueFbm(Vector3 p, int frequency, int octaves, Vector3 offset)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            int freq = frequency;

            for (int o = 0; o < octaves; o++)
            {
                sum += TileableValueNoise(p, freq, offset) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                freq *= 2;
            }

            return total > 0f ? sum / total : 0f;
        }

        /// <summary>
        /// Одна октава бесшовного value-шума: решётка случайных значений
        /// с плавной интерполяцией. Индексы решётки берутся по модулю частоты.
        /// </summary>
        private static float TileableValueNoise(Vector3 p, int freq, Vector3 offset)
        {
            Vector3 scaled = new Vector3(p.x * freq, p.y * freq, p.z * freq);

            int x0 = Mathf.FloorToInt(scaled.x);
            int y0 = Mathf.FloorToInt(scaled.y);
            int z0 = Mathf.FloorToInt(scaled.z);

            float fx = scaled.x - x0;
            float fy = scaled.y - y0;
            float fz = scaled.z - z0;

            // Quintic-сглаживание: непрерывная вторая производная,
            // не видно решётки на градиентах.
            fx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            fy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            fz = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);

            float c000 = LatticeValue(x0, y0, z0, freq, offset);
            float c100 = LatticeValue(x0 + 1, y0, z0, freq, offset);
            float c010 = LatticeValue(x0, y0 + 1, z0, freq, offset);
            float c110 = LatticeValue(x0 + 1, y0 + 1, z0, freq, offset);
            float c001 = LatticeValue(x0, y0, z0 + 1, freq, offset);
            float c101 = LatticeValue(x0 + 1, y0, z0 + 1, freq, offset);
            float c011 = LatticeValue(x0, y0 + 1, z0 + 1, freq, offset);
            float c111 = LatticeValue(x0 + 1, y0 + 1, z0 + 1, freq, offset);

            float x00 = Mathf.Lerp(c000, c100, fx);
            float x10 = Mathf.Lerp(c010, c110, fx);
            float x01 = Mathf.Lerp(c001, c101, fx);
            float x11 = Mathf.Lerp(c011, c111, fx);

            float y0v = Mathf.Lerp(x00, x10, fy);
            float y1v = Mathf.Lerp(x01, x11, fy);

            return Mathf.Lerp(y0v, y1v, fz);
        }

        /// <summary>Детерминированное значение в узле решётки (с оборачиванием).</summary>
        private static float LatticeValue(int x, int y, int z, int freq, Vector3 offset)
        {
            int wx = ((x % freq) + freq) % freq;
            int wy = ((y % freq) + freq) % freq;
            int wz = ((z % freq) + freq) % freq;

            // Целочисленный хеш — быстрее и стабильнее, чем Mathf.PerlinNoise.
            // unchecked: переполнение int здесь ожидаемо и является частью хеша.
            unchecked
            {
                int h = wx * 374761393 + wy * 668265263 + wz * 1274126177
                        + (int)offset.x * 971 + (int)offset.y * 1289 + (int)offset.z * 1613;

                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;

                return (h & 0x7FFFFFFF) / 2147483647f;
            }
        }

        // =============================================================
        //  2D текстуры для частиц
        // =============================================================
        /// <summary>
        /// Текстура шума плотности для частиц.
        /// Канал R — крупные массы, канал G — мелкая детализация.
        /// </summary>
        public static Texture2D GetNoiseTexture(int resolution = 256, int seed = 1337)
        {
            if (cachedNoise != null && cachedNoise.width == resolution)
                return cachedNoise;

            if (cachedNoise != null)
                Object.Destroy(cachedNoise);

            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, true)
            {
                name = "FogNoise_Procedural",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[resolution * resolution];
            float inv = 1f / resolution;

            Random.State prevState = Random.state;
            Random.InitState(seed);
            Vector2 offsetA = new Vector2(Random.value * 100f, Random.value * 100f);
            Vector2 offsetB = new Vector2(Random.value * 100f, Random.value * 100f);
            Random.state = prevState;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x * inv;
                    float v = y * inv;

                    float coarse = TileableFbm(u, v, 4f, 3, offsetA);
                    float detail = TileableFbm(u, v, 11f, 2, offsetB);

                    // Контраст: убираем «серую кашу», получаем зоны сгущения.
                    coarse = Mathf.SmoothStep(0.15f, 0.9f, coarse);
                    detail = Mathf.SmoothStep(0.25f, 0.85f, detail);

                    pixels[y * resolution + x] = new Color32(
                        (byte)(Mathf.Clamp01(coarse) * 255f),
                        (byte)(Mathf.Clamp01(detail) * 255f),
                        0, 255);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, false);

            cachedNoise = tex;
            return cachedNoise;
        }

        /// <summary>
        /// Спрайт клубка тумана для частиц: мягкое пятно с рваными краями.
        ///
        /// Форма намеренно сильно неровная и вытянутая по-разному в разных
        /// направлениях: идеальный радиальный градиент — главная причина,
        /// по которой частицы читаются как «овалы».
        /// </summary>
        public static Texture2D GetPuffTexture(int resolution = 128, int seed = 4242)
        {
            if (cachedPuff != null && cachedPuff.width == resolution)
                return cachedPuff;

            if (cachedPuff != null)
                Object.Destroy(cachedPuff);

            Texture2D tex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true, true)
            {
                name = "FogPuff_Procedural",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color32[] pixels = new Color32[resolution * resolution];
            float inv = 1f / resolution;

            Random.State prevState = Random.state;
            Random.InitState(seed);
            Vector2 offset = new Vector2(Random.value * 100f, Random.value * 100f);
            Random.state = prevState;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float u = x * inv;
                    float v = y * inv;

                    float dx = u - 0.5f;
                    float dy = v - 0.5f;

                    float dist = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float angle = Mathf.Atan2(dy, dx);

                    // Радиус зависит от угла: контур получается рваный,
                    // а не эллиптический. Три гармоники дают неповторяющуюся форму.
                    float wobble =
                        Mathf.Sin(angle * 3f + offset.x) * 0.16f +
                        Mathf.Sin(angle * 5f - offset.y) * 0.10f +
                        Mathf.Sin(angle * 9f + offset.x * 2f) * 0.06f;

                    float radius = 1f + wobble;
                    float normalized = Mathf.Clamp01(dist / Mathf.Max(radius, 0.2f));

                    // Плавное затухание к рваной границе.
                    float falloff = 1f - normalized;
                    falloff = falloff * falloff * (3f - 2f * falloff);

                    // Внутренняя структура: клубок неоднороден и внутри.
                    float structure = TileableFbm(u, v, 6f, 3, offset);
                    float alpha = falloff * Mathf.Lerp(0.35f, 1f, structure);

                    // Гарантированно нулевая альфа у самого края билборда,
                    // иначе видно квадратную границу спрайта.
                    alpha *= Mathf.SmoothStep(1f, 0.7f, dist);

                    pixels[y * resolution + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, false);

            cachedPuff = tex;
            return cachedPuff;
        }

        /// <summary>Бесшовный 2D fBm на основе Mathf.PerlinNoise.</summary>
        private static float TileableFbm(float u, float v, float frequency, int octaves, Vector2 offset)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float totalAmplitude = 0f;
            float freq = frequency;

            for (int o = 0; o < octaves; o++)
            {
                sum += TileablePerlin(u, v, freq, offset) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= 0.5f;
                freq *= 2f;
            }

            return totalAmplitude > 0f ? sum / totalAmplitude : 0f;
        }

        /// <summary>Одна октава бесшовного 2D Perlin-шума.</summary>
        private static float TileablePerlin(float u, float v, float freq, Vector2 offset)
        {
            float x = u * freq + offset.x;
            float y = v * freq + offset.y;

            float n00 = Mathf.PerlinNoise(x, y);
            float n10 = Mathf.PerlinNoise(x - freq, y);
            float n01 = Mathf.PerlinNoise(x, y - freq);
            float n11 = Mathf.PerlinNoise(x - freq, y - freq);

            float top = Mathf.Lerp(n00, n10, u);
            float bottom = Mathf.Lerp(n01, n11, u);
            return Mathf.Lerp(top, bottom, v);
        }

        /// <summary>Освободить кэшированные текстуры (например, при смене уровня).</summary>
        public static void ClearCache()
        {
            if (cachedNoise3D != null)
            {
                Object.Destroy(cachedNoise3D);
                cachedNoise3D = null;
            }

            if (cachedNoise != null)
            {
                Object.Destroy(cachedNoise);
                cachedNoise = null;
            }

            if (cachedPuff != null)
            {
                Object.Destroy(cachedPuff);
                cachedPuff = null;
            }
        }
    }
}
