using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Рабочая система звуков шагов от расстояния (не от таймера).
/// Вешается на игрока (там же, где CharacterController).
///
/// Как работает:
///  - копит пройденную дистанцию, при наборе длины шага (stride) играет звук;
///  - поверхность определяется лучом вниз: сначала по тегу, потом по имени
///    PhysicMaterial, иначе — клипы по умолчанию;
///  - громкость/тон зависят от состояния: бег / шаг / присесть;
///  - есть звук приземления после падения и публичные методы для Animation Events.
///
/// Зависимостей на контроллер нет: скорость считается по реальному движению
/// (позиция за кадр), земля — по CharacterController + лучу. Поэтому работает
/// и с EasyPeasyFirstPersonController, и с любым другим движением.
/// Уважает паузу (timeScale = 0) и PlayerInputLock.MovementLocked (инвентарь/диалог).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class SimpleFootstep : MonoBehaviour
{
    [System.Serializable]
    public class SurfaceSounds
    {
        [Tooltip("Тег объекта пола (например Grass, Wood, Stone). Сравнение без учёта регистра.")]
        public string tag = "";
        [Tooltip("Клипы для этой поверхности. Выбирается случайный, без повтора подряд.")]
        public AudioClip[] clips = new AudioClip[0];
    }

    [Header("Откуда слушать землю")]
    [Tooltip("Точка старта луча (обычно ступни/groundCheck). Пусто — центр transform + 0.5м вверх.")]
    public Transform footOrigin;
    [Tooltip("По каким слоям искать землю. Убери слои игрока, оружия и подборов.")]
    public LayerMask groundMask = ~0;
    [Tooltip("Длина луча вниз от точки старта.")]
    [Min(0.2f)] public float rayDistance = 2.0f;
    [Tooltip("Смещение старта луча вверх, чтобы не стартовать внутри пола.")]
    [Min(0f)] public float rayStartHeight = 0.5f;

    [Header("Длина шага (метры дистанции, не секунды)")]
    [Tooltip("Сколько метров пройти для одного шага при ходьбе.")]
    [Min(0.5f)] public float strideWalk = 2.2f;
    [Tooltip("То же при беге (спринт).")]
    [Min(0.5f)] public float strideSprint = 2.8f;
    [Tooltip("То же при приседе.")]
    [Min(0.5f)] public float strideCrouch = 1.6f;
    [Tooltip("Ниже этой скорости шаги не играются (дрейф/толчки).")]
    [Min(0f)] public float minMoveSpeed = 0.6f;
    [Tooltip("Максимальная скорость, выше которой дистанция режется (защита от телепортов).")]
    [Min(1f)] public float maxTrackedSpeed = 12f;

    [Header("Бег / присесть (определение состояния)")]
    [Tooltip("Клавиши спринта.")]
    public KeyCode sprintKey = KeyCode.LeftShift;
    [Tooltip("Клавиши приседа (любая из них).")]
    public KeyCode[] crouchKeys = { KeyCode.C, KeyCode.LeftControl };

    [Header("Громкость и тон")]
    [Range(0f, 1f)] public float walkVolume = 0.7f;
    [Range(0f, 2f)] public float sprintVolumeMult = 1.15f;
    [Range(0f, 2f)] public float crouchVolumeMult = 0.55f;
    [Range(0f, 2f)] public float landVolumeMult = 1.0f;
    [Min(0.1f)] public float basePitch = 1f;
    [Tooltip("Случайный разброс тона каждого шага (+/-).")]
    [Range(0f, 0.3f)] public float pitchRandom = 0.07f;
    [Tooltip("Тон бега / приседа относительно базы.")]
    [Range(0.5f, 1.5f)] public float sprintPitchMult = 1.06f;
    [Range(0.5f, 1.5f)] public float crouchPitchMult = 0.92f;

    [Header("Поверхности")]
    [Tooltip("Настройка тег -> звуки. Пустой тег игнорируется.")]
    public SurfaceSounds[] surfaces = new SurfaceSounds[0];
    [Tooltip("Пробовать имя PhysicMaterial пола, если тег не найден в списке.")]
    public bool usePhysicMaterialName = true;
    [Tooltip("Звуки по умолчанию (любая неизвестная поверхность).")]
    public AudioClip[] defaultClips = new AudioClip[0];

    [Header("Прыжок / приземление")]
    [Tooltip("Звук приземления после падения (пусто — взять шаг той же поверхности, но громче).")]
    public AudioClip[] landClips = new AudioClip[0];
    [Tooltip("Минимальная скорость падения для звука приземления.")]
    [Min(0f)] public float landMinFallSpeed = 4f;
    [Tooltip("Тихий порог: падение слабее — мягкое приземление (тише).")]
    [Min(0f)] public float landSoftThreshold = 7f;

    [Header("Звук")]
    [Tooltip("3D-звук шагов (1 = полностью объёмный). 0 — плоский 2D.")]
    [Range(0f, 1f)] public float spatialBlend = 1f;
    [Tooltip("Свой AudioSource. Пусто — возьмётся/создастся на этом объекте.")]
    public AudioSource audioSource;

    // ---------- внутреннее ----------
    private readonly Dictionary<string, AudioClip[]> tagLookup =
        new Dictionary<string, AudioClip[]>(System.StringComparer.OrdinalIgnoreCase);
    private CharacterController characterController;
    private Vector3 lastPosition;
    private float distanceAcc;
    private float verticalSpeed;
    private bool wasGrounded = true;
    private int lastClipIndex = -1;
    private AudioClip[] lastPool;
    private bool initialized;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = spatialBlend;

        RebuildLookup();
    }

    void OnEnable()
    {
        lastPosition = transform.position;
        distanceAcc = 0f;
        verticalSpeed = 0f;
        wasGrounded = true;
        initialized = true;
    }

    void OnValidate()
    {
        strideWalk = Mathf.Max(0.5f, strideWalk);
        strideSprint = Mathf.Max(0.5f, strideSprint);
        strideCrouch = Mathf.Max(0.5f, strideCrouch);
        rayDistance = Mathf.Max(0.2f, rayDistance);
        if (audioSource != null)
            audioSource.spatialBlend = spatialBlend;
    }

    /// <summary>Пересобрать словарь тег -> клипы (после правок в инспекторе).</summary>
    public void RebuildLookup()
    {
        tagLookup.Clear();
        if (surfaces == null) return;
        foreach (SurfaceSounds s in surfaces)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.tag)) continue;
            if (s.clips == null || s.clips.Length == 0) continue;
            string key = s.tag.Trim();
            if (tagLookup.ContainsKey(key))
            {
                Debug.LogWarning($"[Footsteps] Дубликат поверхности '{key}' — взята первая запись.", this);
                continue;
            }
            tagLookup[key] = s.clips;
        }
        lastPool = null;
        lastClipIndex = -1;
    }

    void Update()
    {
        if (!initialized) return;
        // Пауза (инвентарь на timeScale=0, меню): шагов нет, накопление не растёт.
        if (Time.deltaTime <= 0f || Mathf.Approximately(Time.timeScale, 0f))
        {
            lastPosition = transform.position;
            return;
        }
        // Инвентарь/диалог/катсцена держат MovementLocked — не топаем на месте.
        if (PlayerInputLock.MovementLocked)
        {
            distanceAcc = 0f;
            lastPosition = transform.position;
            wasGrounded = IsGrounded();
            verticalSpeed = 0f;
            return;
        }

        Vector3 pos = transform.position;
        float dt = Time.deltaTime;

        // Вертикальная скорость для приземления (по реальному движению, а не по velocity,
        // которое у CharacterController обнуляется при приземлении в том же кадре).
        float fallSpeedNow = -(pos.y - lastPosition.y) / Mathf.Max(dt, 0.0001f);

        bool grounded = IsGrounded();

        // Переход воздух -> земля: приземление.
        if (grounded && !wasGrounded)
        {
            // fallSpeedNow > 0 означает падение вниз.
            if (fallSpeedNow >= landMinFallSpeed)
                PlayLand(fallSpeedNow);
            distanceAcc = 0f;
            verticalSpeed = 0f;
        }
        wasGrounded = grounded;

        Vector3 delta = pos - lastPosition;
        lastPosition = pos;
        delta.y = 0f;
        float planarDist = delta.magnitude;
        // Режем выбросы (телепорт/смена сцены), иначе сразу пачка шагов.
        float maxStep = maxTrackedSpeed * dt;
        if (planarDist > maxStep) planarDist = maxStep;

        if (!grounded)
        {
            // В воздухе шаги не копим, но помним скорость падения.
            verticalSpeed = -fallSpeedNow;
            distanceAcc = 0f;
            return;
        }

        float speed = planarDist / Mathf.Max(dt, 0.0001f);
        // Учитываем и velocity контроллера: если объект едет на платформе,
        // позиция меняется, а персонаж стоит — топать не надо.
        if (IsStandingStillByInput() && speed < minMoveSpeed + 1.2f)
        {
            distanceAcc = Mathf.Max(0f, distanceAcc - planarDist * 2f);
            return;
        }
        if (speed < minMoveSpeed)
        {
            distanceAcc = 0f;
            return;
        }

        distanceAcc += planarDist;
        float stride = CurrentStride();
        if (distanceAcc >= stride)
        {
            distanceAcc = 0f;
            PlayStep();
        }
    }

    bool IsStandingStillByInput()
    {
#if ENABLE_INPUT_SYSTEM
        // При новом Input System старый Input может быть выключен —
        // проверяем через velocity контроллера как запасной вариант.
        if (characterController != null)
        {
            Vector3 v = characterController.velocity;
            v.y = 0f;
            if (v.magnitude >= minMoveSpeed) return false;
        }
        try
        {
            if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f) return false;
            if (Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f) return false;
        }
        catch { return false; }
        return true;
#else
        try
        {
            if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f) return false;
            if (Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f) return false;
        }
        catch { /* осей нет в проекте — считаем что движется, решает скорость */ return false; }
        return true;
#endif
    }

    bool IsGrounded()
    {
        if (characterController != null && characterController.isGrounded)
            return true;
        // Запасной вариант: короткий луч вниз (работает и без CharacterController).
        Vector3 origin = FootRayOrigin();
        return Physics.Raycast(origin, Vector3.down, rayDistance,
            groundMask, QueryTriggerInteraction.Ignore);
    }

    Vector3 FootRayOrigin()
    {
        if (footOrigin != null) return footOrigin.position + Vector3.up * 0.05f;
        return transform.position + Vector3.up * rayStartHeight;
    }

    bool IsSprinting()
    {
        try { return Input.GetKey(sprintKey); }
        catch { return false; }
    }

    bool IsCrouching()
    {
        if (crouchKeys == null) return false;
        try
        {
            foreach (KeyCode k in crouchKeys)
                if (Input.GetKey(k)) return true;
        }
        catch { return false; }
        return false;
    }

    float CurrentStride()
    {
        if (IsCrouching()) return strideCrouch;
        if (IsSprinting()) return strideSprint;
        return strideWalk;
    }

    // =====================================================================
    // Воспроизведение
    // =====================================================================

    /// <summary>Обычный шаг (можно вызывать из Animation Event).</summary>
    public void PlayStep() => PlayFootstepSound(isLanding: false, fallSpeed: 0f);

    /// <summary>Приземление (можно вызывать из Animation Event).</summary>
    /// <param name="fallSpeed">Скорость падения для громкости. &lt;=0 — средняя.</param>
    public void PlayLand(float fallSpeed = -1f) => PlayFootstepSound(isLanding: true, fallSpeed: fallSpeed);

    void PlayFootstepSound(bool isLanding, float fallSpeed)
    {
        if (audioSource == null) return;

        AudioClip[] pool = ResolveSurfacePool(out string surfaceName);
        if (isLanding && landClips != null && landClips.Length > 0)
            pool = landClips;
        if (pool == null || pool.Length == 0)
        {
            if (!isLanding)
                Debug.LogWarning($"[Footsteps] Нет звуков для поверхности '{surfaceName}' и нет defaultClips.", this);
            return;
        }

        AudioClip clip = PickNoRepeat(pool);
        if (clip == null) return;

        float volume = walkVolume;
        float pitch = basePitch;
        if (IsCrouching())
        {
            volume *= crouchVolumeMult;
            pitch *= crouchPitchMult;
        }
        else if (IsSprinting())
        {
            volume *= sprintVolumeMult;
            pitch *= sprintPitchMult;
        }
        if (isLanding)
        {
            volume *= landVolumeMult;
            // Сильное падение — громче и ниже, слабое — тише.
            if (fallSpeed > 0f)
            {
                float t = Mathf.InverseLerp(landMinFallSpeed, landSoftThreshold + 6f, fallSpeed);
                volume *= Mathf.Lerp(0.6f, 1.2f, t);
                pitch *= Mathf.Lerp(1.02f, 0.9f, t);
            }
        }

        pitch += Random.Range(-pitchRandom, pitchRandom);
        pitch = Mathf.Clamp(pitch, 0.5f, 1.6f);
        volume = Mathf.Clamp01(volume);

        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }

    AudioClip PickNoRepeat(AudioClip[] pool)
    {
        if (pool == null || pool.Length == 0) return null;
        if (pool.Length == 1)
        {
            lastPool = pool;
            lastClipIndex = 0;
            return pool[0];
        }
        int idx;
        if (pool == lastPool && lastClipIndex >= 0)
        {
            // Не повторяем тот же клип подряд.
            idx = Random.Range(0, pool.Length - 1);
            if (idx >= lastClipIndex) idx++;
        }
        else
        {
            idx = Random.Range(0, pool.Length);
        }
        lastPool = pool;
        lastClipIndex = idx;
        return pool[idx];
    }

    AudioClip[] ResolveSurfacePool(out string surfaceName)
    {
        surfaceName = "default";
        RaycastHit hit;
        Vector3 origin = FootRayOrigin();
        if (!Physics.Raycast(origin, Vector3.down, out hit, rayDistance, groundMask,
                QueryTriggerInteraction.Ignore))
            return defaultClips;

        string tag = hit.collider != null ? hit.collider.tag : "";
        if (!string.IsNullOrEmpty(tag))
        {
            surfaceName = tag;
            AudioClip[] byTag;
            if (tagLookup.TryGetValue(tag, out byTag) && byTag != null && byTag.Length > 0)
                return byTag;
        }

        if (usePhysicMaterialName && hit.collider != null)
        {
            PhysicMaterial mat = hit.collider.sharedMaterial;
            if (mat != null && !string.IsNullOrEmpty(mat.name))
            {
                surfaceName = mat.name;
                AudioClip[] byMat;
                if (tagLookup.TryGetValue(mat.name, out byMat) && byMat != null && byMat.Length > 0)
                    return byMat;
                // Unity добавляет " (Instance)" к именам в рантайме — пробуем без суффикса.
                string clean = mat.name.Replace(" (Instance)", "").Trim();
                if (clean != mat.name && tagLookup.TryGetValue(clean, out byMat) && byMat != null && byMat.Length > 0)
                {
                    surfaceName = clean;
                    return byMat;
                }
            }
        }
        return defaultClips;
    }

    /// <summary>Проверка из инспектора: проиграть шаг прямо в Play Mode.</summary>
    [ContextMenu("Test: Play Step")]
    void TestStep()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Footsteps] Test доступен только в Play Mode.");
            return;
        }
        RebuildLookup();
        PlayStep();
    }

    void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? FootRayOrigin()
            : (footOrigin != null ? footOrigin.position : transform.position + Vector3.up * rayStartHeight);
        Gizmos.color = Color.green;
        Gizmos.DrawRay(origin, Vector3.down * rayDistance);
    }
}
