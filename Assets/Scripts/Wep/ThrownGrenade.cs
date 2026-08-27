using System.Collections.Generic;
using UnityEngine;
using FlameOfHistory.AI;

/// <summary>
/// Граната, уже выпущенная из руки: летит, тикает запалом, взрывается.
///
/// Урон раздаётся всем в радиусе с проверкой линии видимости, чтобы
/// стена принимала осколки на себя. Урон падает от центра к краю радиуса:
/// у самой границы он почти нулевой, рядом с центром — полный.
///
/// Вешается на объект в полёте. Обычно создаётся GrenadeItem, а не вручную.
/// </summary>
[DisallowMultipleComponent]
public class ThrownGrenade : MonoBehaviour
{
    [Header("Запал")]
    [Tooltip("Через сколько секунд после броска рванёт.")]
    [Min(0.1f)] public float fuseTime = 3.8f;

    [Tooltip("Взрываться при первом же ударе о поверхность (ударный взрыватель).")]
    public bool explodeOnImpact = false;

    [Tooltip("Минимальная скорость удара для срабатывания ударного взрывателя.")]
    public float impactSpeedThreshold = 4f;

    [Header("Физика полёта")]
    [Tooltip("Максимальная угловая скорость в полёте, рад/с. " +
             "Ограничение убирает «бешеное» кручение после удара о землю.")]
    [Min(0f)] public float maxAngularVelocity = 12f;

    [Tooltip("Гасить вращение и скорость после первого удара: " +
             "граната отскакивает, а не улетает волчком.")]
    public bool dampenOnImpact = true;

    [Tooltip("Сколько скорости остаётся после удара. 0.35 — заметно тормозит.")]
    [Range(0f, 1f)] public float impactVelocityDamping = 0.35f;

    [Tooltip("Через сколько секунд после удара граната замирает на месте " +
             "(isKinematic). 0 — не замирать, катиться до взрыва.")]
    [Min(0f)] public float settleDelay = 0.3f;

    [Tooltip("Замирать только если граната уже почти не двигается. " +
             "Снимай галочку, если хочешь жёсткую остановку строго по таймеру.")]
    public bool settleOnlyWhenSlow = true;

    [Tooltip("Скорость, ниже которой граната считается остановившейся, м/с.")]
    [Min(0f)] public float settleSpeedThreshold = 0.55f;

    [Header("Защита от провала под землю")]
    [Tooltip("Проверять пол под гранатой при старте и поднимать её на поверхность.")]
    public bool snapToGroundOnStart = true;

    [Tooltip("Слои пола для проверки.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Как глубоко вниз искать пол от точки появления, метры.")]
    [Min(0.1f)] public float groundSearchDistance = 3f;

    [Tooltip("Зазор над полом, чтобы модель не касалась текстуры, метры.")]
    [Min(0f)] public float groundClearance = 0.02f;

    [Tooltip("Следить за гранатой в полёте и выталкивать её наверх, если утонула. " +
             "Лечит случай, когда граната легла в пол уже после отскока.")]
    public bool keepAboveGroundAlways = true;

    [Header("Урон")]
    public float damage = 110f;
    public float damageRadius = 6f;

    [Tooltip("Доля урона на самой границе радиуса. 0 — на краю урона нет вовсе.")]
    [Range(0f, 1f)] public float edgeDamageFactor = 0.15f;

    [Tooltip("Слои, по которым проверяется, не закрыта ли цель стеной.")]
    public LayerMask coverMask = ~0;

    [Tooltip("Слои, среди которых ищутся цели.")]
    public LayerMask targetMask = ~0;

    [Header("Физика взрыва")]
    public float explosionForce = 520f;
    public float explosionRadius = 6f;
    public float explosionUpwardModifier = 0.4f;

    [Header("Эффекты")]
    public GameObject explosionEffectPrefab;
    public float effectLifetime = 4f;
    public AudioClip explosionSound;
    [Range(0f, 1f)] public float explosionVolume = 1f;
    public AudioClip bounceSound;
    [Range(0f, 1f)] public float bounceVolume = 0.5f;

    [Header("Тряска камеры")]
    public float shakeIntensity = 0.55f;
    public float shakeDuration = 0.45f;
    [Tooltip("Дальше этого расстояния взрыв камеру уже не трясёт.")]
    public float shakeRange = 18f;

    [Header("Шум для ИИ")]
    [Tooltip("Радиус, в котором враги услышат взрыв.")]
    public float noiseRadius = 45f;

    [Header("Отладка")]
    public bool logDamage = false;

    /// <summary>Кто бросил. Нужно, чтобы граната не «попадала» в саму себя и в руку.</summary>
    public GameObject Thrower { get; private set; }

    private float fuseTimer;
    private bool hasExploded;
    private bool bounceSoundPlayed;

    private Rigidbody body;
    private bool hasLanded;
    private float settleTimer;
    private bool isSettled;

    // =====================================================================
    /// <summary>
    /// Запустить гранату: направление, сила, вращение и владелец.
    /// Вызывается сразу после Instantiate.
    /// </summary>
    public void Launch(Vector3 velocity, Vector3 angularVelocity, GameObject thrower, float fuse = -1f)
    {
        Thrower = thrower;
        if (fuse > 0f) fuseTime = fuse;
        fuseTimer = fuseTime;

        Rigidbody rb = EnsureBody();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Без этого предела Unity разрешает 7 рад/с по умолчанию, но при ударе
        // о землю мелкий коллайдер получает огромный импульс вращения —
        // отсюда и «бешеное» кручение гранаты на земле
        if (maxAngularVelocity > 0f) rb.maxAngularVelocity = maxAngularVelocity;

        rb.velocity = velocity;
        rb.angularVelocity = ClampAngular(angularVelocity);

        hasLanded = false;
        isSettled = false;
        settleTimer = 0f;

        IgnoreThrowerCollisions();
    }

    void Awake()
    {
        if (fuseTimer <= 0f) fuseTimer = fuseTime;
        EnsureBody();
    }

    void Start()
    {
        if (snapToGroundOnStart) SnapAboveGround();
    }

    /// <summary>
    /// Поднять гранату на поверхность, если она утонула в полу.
    ///
    /// Луч пускается сверху вниз через саму гранату: если начинать из её центра,
    /// уже провалившийся объект не увидит пол над собой. Смещение считается от
    /// нижней точки коллайдера, а не от радиуса: у меша и повёрнутой капсулы
    /// радиус не совпадает с реальным низом, поэтому часть модели уходила
    /// в текстуру.
    /// </summary>
    void SnapAboveGround()
    {
        Collider col = FindOwnCollider();

        // Старт заведомо выше гранаты: иначе луч начнётся внутри пола
        float lift = Mathf.Max(0.5f, GetColliderHeight(col));
        Vector3 origin = transform.position + Vector3.up * lift;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, lift + groundSearchDistance,
                                               groundMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;   // свой коллайдер
            if (Thrower != null && hit.collider.transform.IsChildOf(Thrower.transform.root)) continue;

            PlaceAboveSurface(hit.point.y, col);
            return;
        }
    }

    /// <summary>
    /// Поставить гранату так, чтобы низ её коллайдера стоял на поверхности.
    ///
    /// Считаем через bounds.min: это фактический низ с учётом масштаба и
    /// поворота. Разница между центром и низом добавляется к высоте пола —
    /// так модель садится ровно на поверхность, а не половиной в неё.
    /// </summary>
    void PlaceAboveSurface(float surfaceY, Collider col)
    {
        float bottomOffset = col != null
            ? transform.position.y - col.bounds.min.y
            : 0.08f;

        float targetY = surfaceY + bottomOffset + groundClearance;
        if (transform.position.y >= targetY) return;   // уже стоит выше

        transform.position = new Vector3(transform.position.x, targetY, transform.position.z);

        if (body != null && !body.isKinematic)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>Первый рабочий коллайдер гранаты.</summary>
    Collider FindOwnCollider()
    {
        foreach (Collider c in GetComponentsInChildren<Collider>())
            if (c != null && c.enabled && !c.isTrigger) return c;

        return GetComponentInChildren<Collider>();
    }

    /// <summary>Высота коллайдера — нужна как безопасный подъём для луча.</summary>
    float GetColliderHeight(Collider col) =>
        col != null ? col.bounds.size.y : 0.16f;

    void Update()
    {
        if (hasExploded) return;

        UpdateSettling();

        fuseTimer -= Time.deltaTime;
        if (fuseTimer <= 0f) Explode();
    }

    /// <summary>
    /// Дожать гранату до полной остановки после приземления.
    /// Kinematic вместо Sleep: спящий Rigidbody просыпается от любого касания
    /// и граната опять начинает крутиться.
    /// </summary>
    void UpdateSettling()
    {
        if (!hasLanded || isSettled || settleDelay <= 0f || body == null) return;

        settleTimer += Time.deltaTime;
        if (settleTimer < settleDelay) return;

        if (settleOnlyWhenSlow && body.velocity.magnitude > settleSpeedThreshold)
            return;   // ещё катится — не примораживаем в воздухе или на склоне

        // Перед заморозкой выставляем ровно на поверхность: иначе граната
        // застынет наполовину в текстуре и так и останется
        if (keepAboveGroundAlways) SnapAboveGround();

        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
        isSettled = true;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (hasExploded) return;

        float impactSpeed = collision.relativeVelocity.magnitude;

        if (!bounceSoundPlayed && bounceSound != null && impactSpeed > 1.5f)
        {
            bounceSoundPlayed = true;
            AudioSource.PlayClipAtPoint(bounceSound, transform.position, bounceVolume);
        }

        if (explodeOnImpact && impactSpeed >= impactSpeedThreshold)
        {
            Explode();
            return;
        }

        // Первый контакт с землёй запускает отсчёт до полной остановки
        if (!hasLanded)
        {
            hasLanded = true;
            settleTimer = 0f;
        }

        // Быстрый удар вплотную к полу может протолкнуть мелкий коллайдер
        // сквозь него до следующего кадра физики — выправляем сразу
        if (keepAboveGroundAlways) SnapAboveGround();

        if (dampenOnImpact && body != null && !body.isKinematic)
        {
            body.velocity *= impactVelocityDamping;
            body.angularVelocity = ClampAngular(body.angularVelocity * impactVelocityDamping);
        }
    }

    Rigidbody EnsureBody()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();

        if (maxAngularVelocity > 0f) body.maxAngularVelocity = maxAngularVelocity;
        return body;
    }

    Vector3 ClampAngular(Vector3 angular)
    {
        if (maxAngularVelocity <= 0f) return angular;
        return Vector3.ClampMagnitude(angular, maxAngularVelocity);
    }

    // =====================================================================
    /// <summary>Взорваться немедленно.</summary>
    public void Explode()
    {
        if (hasExploded) return;
        hasExploded = true;

        Vector3 center = transform.position;

        SpawnEffects(center);
        ApplyDamage(center);
        ApplyPhysics(center);
        ShakeCamera(center);

        if (noiseRadius > 0f)
            NoiseSystem.Emit(center, noiseRadius, Thrower, 1f);

        Destroy(gameObject);
    }

    // =====================================================================
    void SpawnEffects(Vector3 center)
    {
        if (explosionEffectPrefab != null)
        {
            GameObject fx = Instantiate(explosionEffectPrefab, center, Quaternion.identity);
            Destroy(fx, effectLifetime);
        }

        if (explosionSound != null)
            AudioSource.PlayClipAtPoint(explosionSound, center, explosionVolume);
    }

    /// <summary>
    /// Раздать урон всем в радиусе взрыва.
    ///
    /// Один объект получает урон один раз, даже если у него несколько
    /// коллайдеров: без этого враг с коллайдерами на конечностях получал бы
    /// урон кратно их числу. При этом из всех коллайдеров одной цели берётся
    /// самый выгодный — ближайший к взрыву и не закрытый стеной. Иначе взрыв
    /// у ног не убивал бы врага, потому что первой в списке OverlapSphere
    /// оказалась, например, спрятанная за укрытием рука.
    /// </summary>
    void ApplyDamage(Vector3 center)
    {
        Collider[] overlapped = Physics.OverlapSphere(center, damageRadius, targetMask,
                                                     QueryTriggerInteraction.Collide);

        // root -> (лучший коллайдер, лучший урон)
        var best = new Dictionary<GameObject, KeyValuePair<Collider, float>>();

        foreach (Collider col in overlapped)
        {
            if (col == null) continue;

            GameObject root = col.transform.root.gameObject;

            // Ближайшая точка коллайдера, а не его центр: у крупной капсулы
            // центр может лежать далеко, и урон занижался бы вдвое
            Vector3 targetPoint = col.ClosestPoint(center);
            if (IsBehindCover(center, targetPoint, col)) continue;

            float dealt = DamageAtDistance(Vector3.Distance(center, targetPoint));
            if (dealt <= 0.01f) continue;

            if (best.TryGetValue(root, out var current) && current.Value >= dealt) continue;

            best[root] = new KeyValuePair<Collider, float>(col, dealt);
        }

        foreach (var pair in best)
        {
            Collider col = pair.Value.Key;
            float dealt = pair.Value.Value;
            Vector3 targetPoint = col.ClosestPoint(center);

            if (DealDamage(col, dealt, center, targetPoint) && logDamage)
                Debug.Log($"[Grenade] {pair.Key.name} получил {dealt:0.#} урона.");
        }
    }

    /// <summary>
    /// Нанести урон через любой из двух интерфейсов урона, которые есть в проекте:
    /// боевой FlameOfHistory.AI.IDamageable (враги на CharacterHealth) и
    /// простой глобальный IDamageable (его реализуют Enemy и PlayerHealth).
    /// </summary>
    bool DealDamage(Collider col, float amount, Vector3 center, Vector3 targetPoint)
    {
        Vector3 direction = (targetPoint - center).normalized;

        var aiTarget = col.GetComponentInParent<FlameOfHistory.AI.IDamageable>();
        if (aiTarget != null)
        {
            if (!aiTarget.IsAlive) return false;
            aiTarget.TakeDamage(new DamageInfo(amount, targetPoint, direction, Thrower));
            return true;
        }

        var simpleTarget = col.GetComponentInParent<global::IDamageable>();
        if (simpleTarget != null)
        {
            simpleTarget.TakeDamage(amount, center);
            return true;
        }

        return false;
    }

    /// <summary>Урон по расстоянию: линейное падение к краю радиуса.</summary>
    float DamageAtDistance(float distance)
    {
        if (damageRadius <= 0.01f) return damage;

        float t = Mathf.Clamp01(distance / damageRadius);
        return damage * Mathf.Lerp(1f, edgeDamageFactor, t);
    }

    /// <summary>Закрыта ли цель стеной от точки взрыва.</summary>
    bool IsBehindCover(Vector3 center, Vector3 targetPoint, Collider target)
    {
        Vector3 dir = targetPoint - center;
        float distance = dir.magnitude;
        if (distance < 0.05f) return false;

        RaycastHit[] hits = Physics.RaycastAll(center, dir.normalized, distance, coverMask,
                                               QueryTriggerInteraction.Ignore);

        // Сортировка обязательна: RaycastAll возвращает попадания в произвольном
        // порядке, и без неё далёкая стена «закрывала» бы цель, стоящую ближе
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Transform targetRoot = target.transform.root;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider == target) return false;

            // Любая часть той же цели (голова, руки) — это уже сама цель
            if (hit.collider.transform.IsChildOf(targetRoot)) return false;

            // Сама граната и рука бросавшего преградой не считаются
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (Thrower != null && hit.collider.transform.IsChildOf(Thrower.transform.root)) continue;

            return true;
        }

        return false;
    }

    void ApplyPhysics(Vector3 center)
    {
        if (explosionForce <= 0f) return;

        foreach (Collider col in Physics.OverlapSphere(center, explosionRadius))
        {
            Rigidbody rb = col.attachedRigidbody;
            if (rb == null || rb.isKinematic) continue;

            rb.AddExplosionForce(explosionForce, center, explosionRadius,
                                 explosionUpwardModifier, ForceMode.Impulse);
        }
    }

    void ShakeCamera(Vector3 center)
    {
        if (shakeIntensity <= 0f) return;

        var fps = FindObjectOfType<EasyPeasyFirstPersonController.FirstPersonController>();
        if (fps == null) return;

        float distance = Vector3.Distance(center, fps.transform.position);
        if (distance > shakeRange) return;

        float falloff = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, shakeRange));
        Vector3 direction = (fps.transform.position - center).normalized;

        fps.TriggerCameraShake(shakeIntensity * falloff, shakeDuration, direction);
    }

    /// <summary>
    /// Отключить столкновения с бросающим: без этого граната застревает
    /// в капсуле игрока и падает под ноги вместо полёта.
    /// </summary>
    void IgnoreThrowerCollisions()
    {
        if (Thrower == null) return;

        Collider[] own = GetComponentsInChildren<Collider>();
        Collider[] throwerColliders = Thrower.GetComponentsInChildren<Collider>();

        foreach (Collider a in own)
        {
            if (a == null) continue;
            foreach (Collider b in throwerColliders)
            {
                if (b == null || b == a) continue;
                Physics.IgnoreCollision(a, b, true);
            }
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.15f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, damageRadius);
    }
#endif
}
