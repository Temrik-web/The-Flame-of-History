using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FlameOfHistory.AI;

/// <summary>
/// Холодное оружие в руках: нож, штык, лопата.
///
/// Управление:
///   ЛКМ — тычок: короткий, быстрый, узкая зона попадания, бьёт одну цель;
///   ПКМ — размах: медленный, сильный, широкая дуга, задевает несколько целей;
///   F (настраивается) — удар в спину: полный урон, если цель не смотрит на игрока.
///
/// Тычок проверяется сферой по траектории (SphereCast) в момент контакта.
/// Размах проверяется веером лучей по дуге: одиночный луч пропускал бы цели
/// сбоку, а именно по ним и должен попадать размах.
///
/// Прицела нет: холодным оружием не целятся, перекрестие только мешает.
///
/// Вешается на модель ножа рядом с EquippableWeapon.
/// </summary>
[DisallowMultipleComponent]
public class MeleeItem : HeldItem
{
    // =====================================================================
    [Header("Урон")]
    [Tooltip("Тычок (ЛКМ).")]
    public float lightDamage = 40f;
    [Tooltip("Размах (ПКМ).")]
    public float heavyDamage = 85f;

    [Tooltip("Множитель урона при ударе со спины. Урон = heavyDamage * этот множитель.")]
    public float backstabMultiplier = 4f;

    [Tooltip("Считать удар со спины, если угол между взглядом цели и направлением " +
             "на игрока больше этого значения (градусы).")]
    [Range(0f, 180f)] public float backstabAngle = 100f;

    [Header("Досягаемость")]
    [Tooltip("Дальность тычка (ЛКМ).")]
    public float range = 2.1f;
    [Tooltip("Радиус проверки попадания тычком. Больше — легче попасть по краю силуэта.")]
    public float hitRadius = 0.28f;
    public LayerMask hitMask = ~0;

    [Header("Досягаемость размаха (ПКМ)")]
    [Tooltip("Дальность размаха. Обычно чуть короче тычка: рука идёт по дуге.")]
    public float swingRange = 1.9f;
    [Tooltip("Ширина дуги размаха в градусах — сколько градусов заметает клинок.")]
    [Range(10f, 180f)] public float swingArcAngle = 100f;
    [Tooltip("Сколько лучей проверяет дугу. Больше — точнее, но дороже.")]
    [Range(3, 15)] public int swingRayCount = 7;
    [Tooltip("Радиус каждого луча дуги.")]
    public float swingRayRadius = 0.22f;

    [Tooltip("Сколько целей может задеть один тычок. 1 — только первая.")]
    [Min(1)] public int maxTargetsPerSwing = 1;

    [Tooltip("Сколько целей может задеть один размах. Смысл размаха — бить нескольких.")]
    [Min(1)] public int maxTargetsPerHeavySwing = 3;

    [Header("Тайминги: тычок (ЛКМ)")]
    [Tooltip("Замах до момента контакта.")]
    public float lightWindup = 0.09f;
    [Tooltip("Проводка после контакта.")]
    public float lightRecovery = 0.16f;
    [Tooltip("Пауза до следующего удара.")]
    public float lightCooldown = 0.12f;

    [Header("Тайминги: размах (ПКМ)")]
    public float heavyWindup = 0.28f;
    public float heavyRecovery = 0.3f;
    public float heavyCooldown = 0.35f;

    [Header("Клавиши")]
    [Tooltip("Удар со спины. None — механика выключена, тихий убой идёт обычным ударом.")]
    public KeyCode backstabKey = KeyCode.F;

    [Header("Поза тычка (ЛКМ)")]
    [Tooltip("Замах: нож уходит назад к плечу.")]
    public Vector3 lightWindupPosition = new Vector3(0.34f, -0.3f, 0.3f);
    public Vector3 lightWindupRotation = new Vector3(-10f, 26f, 10f);
    [Tooltip("Контакт: нож выброшен вперёд по прямой.")]
    public Vector3 lightStrikePosition = new Vector3(0.06f, -0.2f, 0.9f);
    public Vector3 lightStrikeRotation = new Vector3(4f, -8f, -4f);

    [Header("Поза размаха (ПКМ)")]
    [Tooltip("Замах: рука уходит далеко вправо и вверх.")]
    public Vector3 heavyWindupPosition = new Vector3(0.62f, 0.02f, 0.1f);
    public Vector3 heavyWindupRotation = new Vector3(-40f, 62f, 34f);
    [Tooltip("Контакт: клинок прошёл дугу и оказался слева.")]
    public Vector3 heavyStrikePosition = new Vector3(-0.3f, -0.26f, 0.66f);
    public Vector3 heavyStrikeRotation = new Vector3(24f, -58f, -34f);

    [Header("Отдача руки")]
    public Vector3 hitKickPosition = new Vector3(-0.04f, 0.02f, -0.09f);
    public Vector3 hitKickRotation = new Vector3(-7f, 3f, 0f);
    public Vector3 missKickPosition = new Vector3(-0.015f, 0.01f, -0.03f);
    public Vector3 missKickRotation = new Vector3(-3f, 1.5f, 0f);

    [Header("Эффекты попадания")]
    [Tooltip("Эффект удара по живому.")]
    public GameObject fleshImpactPrefab;
    [Tooltip("Эффект удара по стене/земле.")]
    public GameObject surfaceImpactPrefab;
    public float impactLifetime = 3f;

    [Header("Шум для ИИ")]
    [Tooltip("Радиус слышимости удара. Нож тихий: враги рядом всё же реагируют.")]
    public float swingNoiseRadius = 3f;
    public float hitNoiseRadius = 6f;

    [Header("Звуки")]
    public AudioClip swingSound;
    public AudioClip hitFleshSound;
    public AudioClip hitSurfaceSound;
    [Range(0f, 1f)] public float soundVolume = 0.9f;

    [Header("Отладка")]
    public bool logActions = true;
    public bool drawHitGizmo = false;

    // =====================================================================
    private Coroutine strikeRoutine;
    private bool isStriking;
    private float nextStrikeTime;

    private Vector3 lastHitOrigin;
    private Vector3 lastHitDirection;

    /// <summary>Идёт ли удар прямо сейчас.</summary>
    public bool IsStriking => isStriking;

    /// <summary>Ножу прицел не нужен: перекрестие только мешает в ближнем бою.</summary>
    public override bool HidesCrosshair => true;

    // =====================================================================
    protected override void Awake()
    {
        base.Awake();

        // Ножом не целятся: ПКМ занят размахом, а перекрестие только мешает
        useRightMouseAsAim = false;
        manageCrosshair = false;
        HideCrosshair();

        DisableHeldColliders();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        isStriking = false;
        nextStrikeTime = 0f;

        // OnEnable базового класса мог включить перекрестие обратно
        HideCrosshair();
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (strikeRoutine != null)
        {
            StopCoroutine(strikeRoutine);
            strikeRoutine = null;
        }

        isStriking = false;
        ClearPoseOverride();

        RestoreCrosshair();
    }

    /// <summary>
    /// Спрятать перекрестие ППШ, пока в руках нож.
    /// Перекрестие принадлежит Wep, и никто, кроме нас, его сейчас не трогает:
    /// Wep выключен вместе со своей моделью.
    /// </summary>
    void HideCrosshair()
    {
        GameObject cross = ResolveCrosshair();
        if (cross != null && cross.activeSelf) cross.SetActive(false);
    }

    /// <summary>Вернуть перекрестие при смене ножа на огнестрел.</summary>
    void RestoreCrosshair()
    {
        // При выходе из Play и выгрузке сцены объект уже мог быть уничтожен
        if (!Application.isPlaying) return;

        GameObject cross = ResolveCrosshair();
        if (cross != null && !cross.activeSelf) cross.SetActive(true);
    }

    GameObject ResolveCrosshair()
    {
        if (crosshairObject != null) return crosshairObject;

        foreach (Wep w in FindObjectsOfType<Wep>(true))
        {
            if (w != null && w.crosshairObject != null)
            {
                crosshairObject = w.crosshairObject;
                break;
            }
        }

        return crosshairObject;
    }

    // =====================================================================
    protected override void HandleInput()
    {
        if (isStriking || Time.time < nextStrikeTime) return;

        if (backstabKey != KeyCode.None && Input.GetKeyDown(backstabKey))
        {
            Strike(heavy: true, forceBackstabAttempt: true);
            return;
        }

        // ЛКМ — тычок, ПКМ — размах. Раздельно, без модификаторов:
        // держать ПКМ и жать ЛКМ в ближнем бою неудобно.
        if (Input.GetMouseButtonDown(0))
        {
            Strike(heavy: false);
            return;
        }

        if (Input.GetMouseButtonDown(1))
            Strike(heavy: true);
    }

    /// <summary>Ударить. heavy — размах (ПКМ), иначе тычок (ЛКМ).</summary>
    public void Strike(bool heavy, bool forceBackstabAttempt = false)
    {
        if (isStriking || Time.time < nextStrikeTime) return;
        strikeRoutine = StartCoroutine(StrikeSequence(heavy, forceBackstabAttempt));
    }

    /// <summary>
    /// Поставить модель точно в позу «в руках» без сглаживания.
    /// Нужно перед началом удара: анимация интерполирует от текущей позиции,
    /// и если модель ещё не доехала до hipPosition, траектория удара искажается.
    /// </summary>
    void SnapToHipPose()
    {
        Transform model = itemModel != null ? itemModel : transform;
        model.localPosition = hipPosition;
        model.localRotation = Quaternion.Euler(hipRotation);

        SetPoseOverride(hipPosition, hipRotation, 1f);
    }

    IEnumerator StrikeSequence(bool heavy, bool forceBackstabAttempt)
    {
        isStriking = true;

        float windup = heavy ? heavyWindup : lightWindup;
        float recovery = heavy ? heavyRecovery : lightRecovery;
        float cooldown = heavy ? heavyCooldown : lightCooldown;

        Vector3 windupPos = heavy ? heavyWindupPosition : lightWindupPosition;
        Vector3 windupRot = heavy ? heavyWindupRotation : lightWindupRotation;
        Vector3 strikePos = heavy ? heavyStrikePosition : lightStrikePosition;
        Vector3 strikeRot = heavy ? heavyStrikeRotation : lightStrikeRotation;

        // Первый удар после экипировки: модель ещё едет к позе «в руках»,
        // и анимация тычка стартовала с произвольной точки — из-за этого он
        // выглядел широким замахом. Ставим базовую позу мгновенно.
        SnapToHipPose();

        PlaySound(swingSound, soundVolume);
        if (swingNoiseRadius > 0f)
            NoiseSystem.Emit(transform.position, swingNoiseRadius, gameObject, 0.3f);

        // --- замах ---
        float t = 0f;
        while (t < windup)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, windup));
            SetPoseOverride(
                Vector3.Lerp(hipPosition, windupPos, k),
                Vector3.Lerp(hipRotation, windupRot, k),
                k);
            yield return null;
        }

        // --- контакт ---
        SetPoseOverride(strikePos, strikeRot, 1f);
        int hits = ResolveHits(heavy, forceBackstabAttempt);

        if (hits > 0) AddKick(hitKickPosition, hitKickRotation);
        else AddKick(missKickPosition, missKickRotation);

        // --- проводка ---
        t = 0f;
        while (t < recovery)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, recovery));
            SetPoseOverride(
                Vector3.Lerp(strikePos, hipPosition, k),
                Vector3.Lerp(strikeRot, hipRotation, k),
                1f - k);
            yield return null;
        }

        ClearPoseOverride();
        nextStrikeTime = Time.time + cooldown;
        isStriking = false;
        strikeRoutine = null;
    }

    // =====================================================================
    /// <summary>Найти цели и нанести урон. Возвращает число задетых объектов.</summary>
    int ResolveHits(bool heavy, bool forceBackstabAttempt)
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 origin = cam.position;
        Vector3 direction = cam.forward;

        lastHitOrigin = origin;
        lastHitDirection = direction;

        List<RaycastHit> hits = heavy
            ? GatherArcHits(cam, origin)
            : GatherThrustHits(origin, direction);

        var touchedRoots = new HashSet<GameObject>();
        GameObject self = Controller != null ? Controller.gameObject : gameObject;
        int limit = heavy ? maxTargetsPerHeavySwing : maxTargetsPerSwing;
        int applied = 0;
        bool blockedBySurface = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;

            GameObject root = hit.collider.transform.root.gameObject;
            if (root == self) continue;                  // сам себя не режем
            if (root == transform.root.gameObject) continue;
            if (touchedRoots.Contains(root)) continue;

            touchedRoots.Add(root);

            float damage = heavy ? heavyDamage : lightDamage;
            bool backstab = IsBackstab(hit.collider);
            if (backstab) damage = heavyDamage * backstabMultiplier;

            bool damagedLiving = DealDamage(hit.collider, damage, hit.point, direction);

            SpawnImpact(damagedLiving, hit.point, hit.normal);
            PlaySound(damagedLiving ? hitFleshSound : hitSurfaceSound, soundVolume);

            if (hitNoiseRadius > 0f)
                NoiseSystem.Emit(hit.point, hitNoiseRadius, self, damagedLiving ? 0.7f : 0.4f);

            if (logActions)
            {
                string kind = heavy ? "Размах" : "Тычок";
                string what = damagedLiving ? $"{root.name} на {damage:0.#} урона" : root.name;
                Debug.Log($"[Melee] {kind}: {what}{(backstab ? " (в спину)" : "")}");
            }

            applied++;

            if (!damagedLiving)
            {
                // Тычок гасится стеной: клинок идёт по прямой и упирается.
                // Размах — нет: он заметает дугу и может достать цель мимо угла.
                if (!heavy) { blockedBySurface = true; break; }
                continue;
            }

            if (applied >= limit) break;
        }

        if (applied == 0 && logActions && forceBackstabAttempt)
            Debug.Log("[Melee] Удар в спину: цели рядом нет.");

        if (blockedBySurface && logActions)
            Debug.Log("[Melee] Тычок упёрся в поверхность.");

        return applied;
    }

    /// <summary>Тычок: одна сфера строго вперёд, ближние цели первыми.</summary>
    List<RaycastHit> GatherThrustHits(Vector3 origin, Vector3 direction)
    {
        RaycastHit[] hits = Physics.SphereCastAll(origin, hitRadius, direction, range, hitMask,
                                                 QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        return new List<RaycastHit>(hits);
    }

    /// <summary>
    /// Размах: веер лучей по дуге справа налево — так же, как идёт рука.
    ///
    /// Один луч вперёд для размаха не годится: цель, стоящая сбоку, попадала бы
    /// в анимацию удара, но не в проверку. Лучи идут в порядке прохождения
    /// клинка, поэтому первым задевается тот, кто оказался в начале дуги.
    /// </summary>
    List<RaycastHit> GatherArcHits(Transform cam, Vector3 origin)
    {
        var result = new List<RaycastHit>();
        var seenColliders = new HashSet<Collider>();

        int rays = Mathf.Max(3, swingRayCount);
        float half = swingArcAngle * 0.5f;

        for (int i = 0; i < rays; i++)
        {
            // От +half (справа) к -half (слева): направление движения руки
            float t = rays == 1 ? 0.5f : i / (float)(rays - 1);
            float angle = Mathf.Lerp(half, -half, t);

            Vector3 dir = Quaternion.AngleAxis(angle, cam.up) * cam.forward;

            RaycastHit[] hits = Physics.SphereCastAll(origin, swingRayRadius, dir, swingRange,
                                                     hitMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (!seenColliders.Add(hit.collider)) continue;
                result.Add(hit);
            }
        }

        return result;
    }

    /// <summary>
    /// Нанести урон обоими поддерживаемыми интерфейсами.
    /// Возвращает true, если цель живая (значит, эффект — кровь, а не пыль).
    /// </summary>
    bool DealDamage(Collider col, float amount, Vector3 point, Vector3 direction)
    {
        GameObject attacker = Controller != null ? Controller.gameObject : gameObject;

        var aiTarget = col.GetComponentInParent<FlameOfHistory.AI.IDamageable>();
        if (aiTarget != null)
        {
            if (!aiTarget.IsAlive) return false;
            aiTarget.TakeDamage(new DamageInfo(amount, point, direction, attacker));
            return true;
        }

        var simpleTarget = col.GetComponentInParent<global::IDamageable>();
        if (simpleTarget != null)
        {
            simpleTarget.TakeDamage(amount, attacker.transform.position);
            return true;
        }

        return false;
    }

    /// <summary>Стоит ли игрок за спиной цели.</summary>
    bool IsBackstab(Collider col)
    {
        if (backstabMultiplier <= 1f) return false;

        Transform target = col.transform.root;
        Vector3 toPlayer = transform.position - target.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f) return false;

        Vector3 targetForward = target.forward;
        targetForward.y = 0f;

        return Vector3.Angle(targetForward, toPlayer.normalized) > backstabAngle;
    }

    void SpawnImpact(bool living, Vector3 point, Vector3 normal)
    {
        GameObject prefab = living ? fleshImpactPrefab : surfaceImpactPrefab;
        if (prefab == null) return;

        GameObject fx = Instantiate(prefab, point, Quaternion.LookRotation(normal));
        Destroy(fx, impactLifetime);
    }

    /// <summary>
    /// Коллайдеры клинка в руках выключаем: удар считается рейкастом от камеры,
    /// а физический коллайдер у лица только толкал бы игрока и цеплял стены.
    /// </summary>
    void DisableHeldColliders()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c != null) c.enabled = false;

        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!drawHitGizmo) return;

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 origin = Application.isPlaying ? lastHitOrigin : cam.position;
        Vector3 dir = Application.isPlaying ? lastHitDirection : cam.forward;

        // Тычок — прямая линия со сферой на конце
        Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(origin + dir * range, hitRadius);
        Gizmos.DrawLine(origin, origin + dir * range);

        // Размах — веер по дуге
        Gizmos.color = new Color(0.95f, 0.75f, 0.25f, 0.5f);
        int rays = Mathf.Max(3, swingRayCount);
        float half = swingArcAngle * 0.5f;

        for (int i = 0; i < rays; i++)
        {
            float t = rays == 1 ? 0.5f : i / (float)(rays - 1);
            float angle = Mathf.Lerp(half, -half, t);
            Vector3 arcDir = Quaternion.AngleAxis(angle, cam.up) * dir;

            Gizmos.DrawLine(origin, origin + arcDir * swingRange);
            Gizmos.DrawWireSphere(origin + arcDir * swingRange, swingRayRadius);
        }
    }

    [ContextMenu("Запомнить текущую позу как замах тычка (ЛКМ)")]
    void CaptureLightWindup()
    {
        lightWindupPosition = transform.localPosition;
        lightWindupRotation = transform.localEulerAngles;
    }

    [ContextMenu("Запомнить текущую позу как контакт тычка (ЛКМ)")]
    void CaptureLightStrike()
    {
        lightStrikePosition = transform.localPosition;
        lightStrikeRotation = transform.localEulerAngles;
    }

    [ContextMenu("Запомнить текущую позу как замах размаха (ПКМ)")]
    void CaptureHeavyWindup()
    {
        heavyWindupPosition = transform.localPosition;
        heavyWindupRotation = transform.localEulerAngles;
    }

    [ContextMenu("Запомнить текущую позу как контакт размаха (ПКМ)")]
    void CaptureHeavyStrike()
    {
        heavyStrikePosition = transform.localPosition;
        heavyStrikeRotation = transform.localEulerAngles;
    }
#endif
}
