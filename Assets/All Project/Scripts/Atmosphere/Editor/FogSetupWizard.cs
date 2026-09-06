using UnityEditor;
using UnityEngine;

namespace WWII.Atmosphere.EditorTools
{
    /// <summary>
    /// Мастер настройки системы тумана.
    /// Меню: Tools -> Атмосфера -> Настроить туман на сцене.
    ///
    /// Что делает:
    /// 1) создаёт объект "FogSystem" с FogSystem и FogLightInteraction;
    /// 2) создаёт материал тумана Assets/GameData/Materials/FogParticle.mat;
    /// 3) находит фонарик игрока и луну, подставляет их в FogLightInteraction;
    /// 4) по кнопке создаёт зоны тумана нужного типа в позиции камеры сцены;
    /// 5) по кнопке помечает выделенные дома как интерьерные зоны-исключения.
    ///
    /// Существующие скрипты не изменяются.
    /// </summary>
    public static class FogSetupWizard
    {
        private const string GameDataFolder = "Assets/GameData";
        private const string MaterialsFolder = "Assets/GameData/Materials";
        private const string MaterialPath = "Assets/GameData/Materials/FogParticle.mat";

        // =============================================================
        //  Основная настройка
        // =============================================================
        [MenuItem("Tools/Атмосфера/Настроить туман на сцене", false, 0)]
        public static void SetupFog()
        {
            Material material = CreateOrGetMaterial();
            if (material == null) return;

            GameObject systemObject = GameObject.Find("FogSystem");
            if (systemObject == null)
            {
                systemObject = new GameObject("FogSystem");
                Undo.RegisterCreatedObjectUndo(systemObject, "Создание FogSystem");
            }

            FogSystem system = systemObject.GetComponent<FogSystem>();
            if (system == null)
                system = Undo.AddComponent<FogSystem>(systemObject);

            FogLightInteraction lightInteraction = systemObject.GetComponent<FogLightInteraction>();
            if (lightInteraction == null)
                lightInteraction = Undo.AddComponent<FogLightInteraction>(systemObject);

            AssignLights(system, lightInteraction, material);
            VerifyDepthTexture();
            SetupDayNight(systemObject);

            EditorUtility.SetDirty(systemObject);
            Selection.activeGameObject = systemObject;

            Debug.Log("[FogSetupWizard] Система тумана настроена. " +
                      "Добавьте зоны через Tools -> Атмосфера -> Создать зону тумана.");
        }

        // =============================================================
        //  Освещение по времени суток
        // =============================================================
        [MenuItem("Tools/Атмосфера/Настроить освещение день-ночь", false, 1)]
        public static void SetupDayNightLighting()
        {
            GameObject systemObject = GameObject.Find("FogSystem");
            if (systemObject == null)
            {
                systemObject = new GameObject("FogSystem");
                Undo.RegisterCreatedObjectUndo(systemObject, "Создание FogSystem");
            }

            DayNightLighting lighting = SetupDayNight(systemObject);
            if (lighting == null) return;

            Selection.activeGameObject = systemObject;
            EditorUtility.DisplayDialog(
                "Освещение настроено",
                "Компонент Day Night Lighting добавлен.\n\n" +
                "Время суток задаётся через:\n" +
                "  Tools -> Атмосфера -> Пресет времени\n\n" +
                "Пресет двигает и туман, и освещение сразу.\n\n" +
                "Затемнить помещение:\n" +
                "  выделить дом -> Tools -> Атмосфера ->\n" +
                "  Пометить выделенное как тёмное помещение\n\n" +
                "Не забудьте сохранить сцену (Ctrl+S).",
                "Ок");
        }

        /// <summary>Добавить и настроить DayNightLighting, подставив свет и камеру.</summary>
        private static DayNightLighting SetupDayNight(GameObject systemObject)
        {
            DayNightLighting lighting = systemObject.GetComponent<DayNightLighting>();
            if (lighting == null)
                lighting = Undo.AddComponent<DayNightLighting>(systemObject);

            Light sun = FindDirectionalLight();
            Material skybox = RenderSettings.skybox;

            SerializedObject serialized = new SerializedObject(lighting);

            if (sun != null)
                serialized.FindProperty("sunLight").objectReferenceValue = sun;

            if (skybox != null)
                serialized.FindProperty("daySkybox").objectReferenceValue = skybox;

            if (Camera.main != null)
                serialized.FindProperty("viewer").objectReferenceValue = Camera.main.transform;

            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(lighting);

            if (sun == null)
                Debug.LogWarning("[FogSetupWizard] Directional Light не найден. " +
                                 "Назначьте его вручную в поле Sun Light компонента Day Night Lighting.");
            else
                Debug.Log($"[FogSetupWizard] Освещение день-ночь настроено, солнце: {sun.name}.");

            return lighting;
        }

        /// <summary>Найти направленный свет сцены.</summary>
        private static Light FindDirectionalLight()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;

#if UNITY_2023_1_OR_NEWER
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            Light[] lights = Object.FindObjectsOfType<Light>();
#endif

            for (int i = 0; i < lights.Length; i++)
                if (lights[i].type == LightType.Directional) return lights[i];

            return null;
        }

        /// <summary>
        /// Мягкие пересечения тумана со стенами работают через текстуру глубины.
        /// Проверяем, включена ли она в активном URP-ассете, и предлагаем включить.
        /// Правка идёт через SerializedObject, чтобы не зависеть от типов URP.
        /// </summary>
        private static void VerifyDepthTexture()
        {
            ScriptableObject pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as ScriptableObject;
            if (pipeline == null)
            {
                Debug.LogWarning("[FogSetupWizard] Активный Render Pipeline не найден. " +
                                 "Мягкие пересечения тумана требуют URP с включённой Depth Texture.");
                return;
            }

            SerializedObject serialized = new SerializedObject(pipeline);
            SerializedProperty depthProperty = serialized.FindProperty("m_SupportsCameraDepthTexture");

            if (depthProperty == null)
            {
                Debug.LogWarning($"[FogSetupWizard] Не удалось проверить Depth Texture в '{pipeline.name}'. " +
                                 "Включите её вручную, иначе туман будет резко обрезаться о стены.");
                return;
            }

            if (depthProperty.boolValue) return;

            bool enable = EditorUtility.DisplayDialog(
                "Туман: нужна текстура глубины",
                $"В ассете '{pipeline.name}' отключена Depth Texture.\n\n" +
                "Без неё туман будет резко обрезаться о стены домов вместо мягкого пересечения.\n\n" +
                "Включить Depth Texture?",
                "Включить", "Оставить как есть");

            if (!enable) return;

            depthProperty.boolValue = true;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            Debug.Log($"[FogSetupWizard] Depth Texture включена в '{pipeline.name}'.");
        }

        /// <summary>Найти фонарик, луну и камеру, подставить в компоненты.</summary>
        private static void AssignLights(FogSystem system, FogLightInteraction lightInteraction, Material material)
        {
#if UNITY_2023_1_OR_NEWER
            Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            Light[] lights = Object.FindObjectsOfType<Light>();
#endif

            Light flashlight = null;
            Light moon = null;

            for (int i = 0; i < lights.Length; i++)
            {
                Light light = lights[i];

                if (light.type == LightType.Directional && moon == null)
                    moon = light;

                if (light.type != LightType.Spot) continue;

                // Фонарик ищем по имени объекта или по наличию компонента со свойством IsOn.
                string lowerName = light.gameObject.name.ToLowerInvariant();
                bool nameMatch = lowerName.Contains("flash") || lowerName.Contains("фонар") || lowerName.Contains("torch");

                bool scriptMatch = false;
                MonoBehaviour[] parents = light.GetComponentsInParent<MonoBehaviour>(true);
                for (int p = 0; p < parents.Length; p++)
                {
                    if (parents[p] != null && parents[p].GetType().Name == "Flashlight")
                    {
                        scriptMatch = true;
                        break;
                    }
                }

                if (nameMatch || scriptMatch)
                    flashlight = light;
            }

            SerializedObject lightSerialized = new SerializedObject(lightInteraction);
            lightSerialized.FindProperty("flashlight").objectReferenceValue = flashlight;
            lightSerialized.FindProperty("moonLight").objectReferenceValue = moon;
            lightSerialized.FindProperty("fogMaterial").objectReferenceValue = material;

            if (Camera.main != null)
                lightSerialized.FindProperty("viewer").objectReferenceValue = Camera.main.transform;

            lightSerialized.ApplyModifiedProperties();

            SerializedObject systemSerialized = new SerializedObject(system);
            systemSerialized.FindProperty("directionalLight").objectReferenceValue = moon;
            systemSerialized.ApplyModifiedProperties();

            if (flashlight == null)
                Debug.LogWarning("[FogSetupWizard] Фонарик не найден автоматически. " +
                                 "Назначьте Spot Light вручную в поле Flashlight компонента FogLightInteraction.");
        }

        // =============================================================
        //  Материал
        // =============================================================
        /// <summary>Создать или получить материал тумана.</summary>
        private static Material CreateOrGetMaterial()
        {
            Shader shader = Shader.Find(FogGlobals.ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[FogSetupWizard] Шейдер '{FogGlobals.ShaderName}' не найден. " +
                               "Убедитесь, что FogSoftParticle.shader импортирован и проект использует URP.");
                return null;
            }

            EnsureFolders();

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "FogParticle" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            // Базовые значения под ночной WWII-туман.
            material.SetFloat(FogGlobals.MaterialDensityId, 1f);
            material.SetFloat(FogGlobals.EdgeSoftnessId, 0.55f);
            material.SetFloat(FogGlobals.NoiseStrengthId, 0.75f);
            material.SetFloat(FogGlobals.SoftFadeId, 2.5f);
            material.SetFloat(FogGlobals.NearFadeId, 1f);
            material.SetFloat(FogGlobals.LightScatterId, 2.2f);
            material.SetFloat(FogGlobals.MoonGlowId, 0.4f);

            material.renderQueue = 3000;

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            return material;
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(GameDataFolder))
                AssetDatabase.CreateFolder("Assets", "GameData");

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
                AssetDatabase.CreateFolder(GameDataFolder, "Materials");
        }

        // =============================================================
        //  Создание зон
        // =============================================================
        [MenuItem("Tools/Атмосфера/Создать зону тумана/Двор", false, 20)]
        public static void CreateCourtyard() => CreateZone(FogZoneType.Courtyard, "FogZone_Двор", new Vector3(18f, 6f, 18f), 60);

        [MenuItem("Tools/Атмосфера/Создать зону тумана/Улица", false, 21)]
        public static void CreateStreet() => CreateZone(FogZoneType.Street, "FogZone_Улица", new Vector3(12f, 6f, 45f), 80);

        [MenuItem("Tools/Атмосфера/Создать зону тумана/Низина", false, 22)]
        public static void CreateLowland() => CreateZone(FogZoneType.Lowland, "FogZone_Низина", new Vector3(14f, 4f, 14f), 50);

        [MenuItem("Tools/Атмосфера/Создать зону тумана/Открытое место", false, 23)]
        public static void CreateOpenGround() => CreateZone(FogZoneType.OpenGround, "FogZone_Открытое", new Vector3(40f, 12f, 40f), 90);

        /// <summary>Создать зону тумана заданного типа перед камерой редактора.</summary>
        private static void CreateZone(FogZoneType type, string objectName, Vector3 size, int particleCount)
        {
            Material material = CreateOrGetMaterial();
            if (material == null) return;

            GameObject zone = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(zone, "Создание зоны тумана");

            // Ставим зону туда, куда смотрит камера сцены.
            if (SceneView.lastActiveSceneView != null)
            {
                Transform camera = SceneView.lastActiveSceneView.camera.transform;
                zone.transform.position = camera.position + camera.forward * 15f;
            }

            FogVolume volume = Undo.AddComponent<FogVolume>(zone);
            volume.ApplyPreset(type);

            SerializedObject volumeSerialized = new SerializedObject(volume);
            volumeSerialized.FindProperty("size").vector3Value = size;
            volumeSerialized.FindProperty("centerOffset").vector3Value = new Vector3(0f, size.y * 0.5f, 0f);
            volumeSerialized.ApplyModifiedProperties();

            FogParticles particles = Undo.AddComponent<FogParticles>(zone);
            SerializedObject particlesSerialized = new SerializedObject(particles);
            particlesSerialized.FindProperty("fogMaterial").objectReferenceValue = material;
            particlesSerialized.FindProperty("targetParticleCount").intValue = particleCount;

            // Открытые места: клубки крупнее и выше.
            if (type == FogZoneType.OpenGround)
            {
                particlesSerialized.FindProperty("minPuffSize").floatValue = 12f;
                particlesSerialized.FindProperty("maxPuffSize").floatValue = 28f;
            }
            else if (type == FogZoneType.Lowland)
            {
                particlesSerialized.FindProperty("minPuffSize").floatValue = 5f;
                particlesSerialized.FindProperty("maxPuffSize").floatValue = 12f;
                particlesSerialized.FindProperty("horizontalStretch").floatValue = 3f;
            }

            particlesSerialized.ApplyModifiedProperties();

            Selection.activeGameObject = zone;
            Debug.Log($"[FogSetupWizard] Создана зона '{objectName}'. Подгоните размеры в поле Size компонента FogVolume.");
        }

        // =============================================================
        //  Интерьеры
        // =============================================================
        [MenuItem("Tools/Атмосфера/Пометить выделенное как интерьер", false, 40)]
        public static void MarkSelectionAsInterior()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection.Length == 0)
            {
                Debug.LogWarning("[FogSetupWizard] Ничего не выделено. Выделите объекты домов в иерархии.");
                return;
            }

            int created = 0;

            foreach (GameObject target in selection)
            {
                // Оцениваем габариты дома по рендерам, чтобы автоматически задать размер зоны.
                Bounds bounds = CalculateBounds(target);

                GameObject zone = new GameObject($"FogInterior_{target.name}");
                Undo.RegisterCreatedObjectUndo(zone, "Создание интерьерной зоны");

                zone.transform.SetParent(target.transform, false);
                zone.transform.position = bounds.center;

                FogVolume volume = Undo.AddComponent<FogVolume>(zone);
                volume.ApplyPreset(FogZoneType.Interior);

                SerializedObject serialized = new SerializedObject(volume);
                // Небольшой отступ внутрь, чтобы туман гасился по внутреннему объёму, а не по фасаду.
                serialized.FindProperty("size").vector3Value = bounds.size * 0.92f;
                serialized.FindProperty("centerOffset").vector3Value = Vector3.zero;
                serialized.FindProperty("snapToGround").boolValue = false;
                serialized.ApplyModifiedProperties();

                created++;
            }

            Debug.Log($"[FogSetupWizard] Создано интерьерных зон-исключений: {created}. " +
                      "При необходимости уменьшите Size, чтобы зона совпадала с внутренним объёмом дома.");
        }

        /// <summary>Габариты объекта по всем дочерним рендерам.</summary>
        private static Bounds CalculateBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(target.transform.position, new Vector3(8f, 4f, 8f));

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        // =============================================================
        //  Тёмные помещения
        // =============================================================
        [MenuItem("Tools/Атмосфера/Пометить выделенное как тёмное помещение", false, 41)]
        public static void MarkSelectionAsDarkInterior()
        {
            GameObject[] selection = Selection.gameObjects;
            if (selection.Length == 0)
            {
                Debug.LogWarning("[FogSetupWizard] Ничего не выделено. Выделите объекты домов в иерархии.");
                return;
            }

            // Без DayNightLighting зоны темноты ничего не сделают: гасит ambient именно он.
            if (Object.FindObjectOfType<DayNightLighting>() == null)
            {
                bool setup = EditorUtility.DisplayDialog(
                    "Нужен контроллер освещения",
                    "Зоны темноты гасит компонент Day Night Lighting, а его нет в сцене.\n\n" +
                    "Настроить освещение день-ночь сейчас?",
                    "Настроить", "Отмена");

                if (!setup) return;

                GameObject systemObject = GameObject.Find("FogSystem");
                if (systemObject == null)
                {
                    systemObject = new GameObject("FogSystem");
                    Undo.RegisterCreatedObjectUndo(systemObject, "Создание FogSystem");
                }

                SetupDayNight(systemObject);
            }

            int created = 0;

            foreach (GameObject target in selection)
            {
                Bounds bounds = CalculateBounds(target);

                Transform existing = target.transform.Find($"DarkInterior_{target.name}");
                GameObject zone;

                if (existing != null)
                {
                    zone = existing.gameObject;
                }
                else
                {
                    zone = new GameObject($"DarkInterior_{target.name}");
                    Undo.RegisterCreatedObjectUndo(zone, "Создание зоны темноты");
                    zone.transform.SetParent(target.transform, false);
                }

                zone.transform.position = bounds.center;

                InteriorDarkness darkness = zone.GetComponent<InteriorDarkness>();
                if (darkness == null) darkness = Undo.AddComponent<InteriorDarkness>(zone);

                // Чуть внутрь фасада, чтобы затемнение начиналось за дверью, а не на улице.
                darkness.Configure(bounds.size * 0.85f, Vector3.zero, 0.12f);

                EditorUtility.SetDirty(darkness);
                created++;
            }

            Debug.Log($"[FogSetupWizard] Создано зон темноты: {created}. " +
                      "Подгоните Size, чтобы зона совпадала с внутренним объёмом помещения.");
        }

        [MenuItem("Tools/Атмосфера/Создать зону темноты перед камерой", false, 42)]
        public static void CreateDarknessZone()
        {
            GameObject zone = new GameObject("DarkInterior");
            Undo.RegisterCreatedObjectUndo(zone, "Создание зоны темноты");

            if (SceneView.lastActiveSceneView != null)
            {
                Transform camera = SceneView.lastActiveSceneView.camera.transform;
                zone.transform.position = camera.position + camera.forward * 8f;
            }

            InteriorDarkness darkness = Undo.AddComponent<InteriorDarkness>(zone);
            darkness.Configure(new Vector3(8f, 4f, 8f), Vector3.zero, 0.12f);

            Selection.activeGameObject = zone;
            Debug.Log("[FogSetupWizard] Зона темноты создана. Задайте Size под габариты помещения.");
        }

        // =============================================================
        //  Пресеты времени
        // =============================================================
        [MenuItem("Tools/Атмосфера/Пресет времени/Глухая ночь (01:00)", false, 60)]
        public static void PresetNight() => SetTime(1f);

        [MenuItem("Tools/Атмосфера/Пресет времени/Пик тумана (04:00)", false, 61)]
        public static void PresetPeak() => SetTime(4f);

        [MenuItem("Tools/Атмосфера/Пресет времени/Рассвет (06:30)", false, 62)]
        public static void PresetDawn() => SetTime(6.5f);

        [MenuItem("Tools/Атмосфера/Пресет времени/Сумерки (20:00)", false, 63)]
        public static void PresetDusk() => SetTime(20f);

        [MenuItem("Tools/Атмосфера/Пресет времени/День (13:00)", false, 64)]
        public static void PresetDay() => SetTime(13f);

        /// <summary>
        /// Выставить время суток. Двигает и туман, и освещение —
        /// иначе густой ночной туман оказывался под ярким днём.
        /// </summary>
        private static void SetTime(float hour)
        {
#if UNITY_2023_1_OR_NEWER
            FogSystem system = Object.FindFirstObjectByType<FogSystem>();
            DayNightLighting lighting = Object.FindFirstObjectByType<DayNightLighting>();
#else
            FogSystem system = Object.FindObjectOfType<FogSystem>();
            DayNightLighting lighting = Object.FindObjectOfType<DayNightLighting>();
#endif

            if (system == null && lighting == null)
            {
                Debug.LogWarning("[FogSetupWizard] Ни FogSystem, ни DayNightLighting не найдены в сцене. " +
                                 "Запустите «Настроить освещение день-ночь».");
                return;
            }

            if (system != null)
            {
                SerializedObject serialized = new SerializedObject(system);
                serialized.FindProperty("timeOfDay").floatValue = hour;
                serialized.ApplyModifiedProperties();

                if (Application.isPlaying)
                    system.SetTimeOfDay(hour, false);
            }

            if (lighting != null)
            {
                SerializedObject serialized = new SerializedObject(lighting);
                serialized.FindProperty("timeOfDay").floatValue = hour;
                serialized.ApplyModifiedProperties();

                // Помечаем компонент как изменённый
                EditorUtility.SetDirty(lighting);

                // Пытаемся найти и вызвать метод для немедленного применения изменений
                // Используем рефлексию для поиска метода, если он существует
                var applyMethod = lighting.GetType().GetMethod("ApplyNow",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                if (applyMethod != null)
                {
                    applyMethod.Invoke(lighting, null);
                }
                else
                {
                    // Если метода нет, пробуем найти другие возможные методы
                    var updateMethod = lighting.GetType().GetMethod("UpdateLighting",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);

                    if (updateMethod != null)
                    {
                        updateMethod.Invoke(lighting, null);
                    }
                    else
                    {
                        // Если нет специальных методов, просто принудительно обновляем сцену
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    }
                }
            }
            else
            {
                Debug.LogWarning("[FogSetupWizard] DayNightLighting нет в сцене — " +
                                 "изменилась только плотность тумана, освещение осталось прежним. " +
                                 "Запустите «Настроить освещение день-ночь».");
            }

            Debug.Log($"[FogSetupWizard] Время суток: {FogSystem.FormatTime(hour)}");
        }
    }
}