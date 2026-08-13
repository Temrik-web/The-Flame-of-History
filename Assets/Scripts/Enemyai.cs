using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// Реалистичный ИИ врага для 3D-шутера.
///
/// Конечный автомат: Patrol -> Investigate -> Combat -> Dead.
///
/// Ключевые фишки для реализма:
/// - Зрение с углом обзора + рейкаст на препятствия (не видит сквозь стены,
///   не видит боковым зрением то, что позади).
/// - "Подозрение" копится постепенно, а не мгновенный 100% детект —
///   враг сначала настораживается, идёт проверить, и только потом атакует.
/// - Слух: реагирует на шум (выстрелы игрока) даже не видя его.
/// - Получив урон "из ниоткуда" — идёт в сторону, откуда стреляли.
/// - Задержка реакции (reactionDelay) перед первым выстрелом — не читерский
///   мгновенный хедшот в момент обнаружения.
/// - Упреждение цели по её скорости (простое предсказание) + разброс точности.
/// - При потере цели не телепортируется в "неведение", а идёт искать в районе
///   последней известной позиции некоторое время, потом возвращается патрулировать.
/// - Оповещает союзников поблизости, когда обнаружил игрока.
///
/// Требует: NavMeshAgent на объекте, запечённый NavMesh на сцене,
/// компонент Enemy (здоровье) на этом же объекте.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Patrol, Investigate, Combat, Cover, Dead }
    private enum CoverPhase { MovingToCover, Hiding, MovingToPeek, Peeking }

    [Header("Ссылки")]
    public Transform player;              // если не задано — найдётся по тегу "Player"
    public Enemy health;                  // если не задано — возьмётся с этого объекта
    public Animator animator;             // если не задано — возьмётся из детей
    public Transform eyes;                // точка глаз для рейкастов (иначе transform + вверх)
    public Transform gunMuzzle;           // дуло оружия (иначе позиция тела)
    public GameObject muzzleFlashPrefab;

    [Header("Скорость")]
    public float walkSpeed = 2f;
    public float runSpeed = 4.5f;
    public float turnSpeed = 8f;

    [Header("Патрулирование")]
    public Transform[] patrolPoints;
    public float patrolWaitTime = 2f;
    public bool patrolLoop = true;
    public bool patrolRandom = false;

    [Header("Зрение")]
    public float viewRadius = 15f;
    [Range(0, 360)] public float viewAngle = 110f;
    public float closeRangeRadius = 3f;   // в упор видит всегда, независимо от угла
    public LayerMask targetMask;          // слой игрока
    public LayerMask obstacleMask;        // слои, которые загораживают обзор (стены и т.п.)

    [Header("Слух")]
    public float hearingRadius = 10f;

    [Header("Подозрение / реакция")]
    public float suspicionBuildRate = 1f;     // ед/сек, пока видит цель
    public float suspicionDecayRate = 0.5f;   // ед/сек, пока не видит
    public float suspicionThreshold = 3f;     // при достижении — бой
    public float reactionDelay = 0.4f;        // задержка перед первым выстрелом в бою

    [Header("Бой")]
    public float attackRange = 20f;
    public float preferredCombatDistance = 10f;
    public float minCombatDistance = 5f;
    public float fireRate = 1.5f;             // выстрелов в секунду
    public float damagePerShot = 10f;
    [Range(0f, 1f)] public float accuracy = 0.85f; // 1 = без разброса
    public LayerMask hittableMask;            // во что может попасть пуля (игрок + окружение)
    public bool predictMovement = true;
    public float assumedBulletSpeed = 120f;
    public float loseSightGrace = 2.5f;       // сколько секунд помнит цель, не видя её, прежде чем начать поиск

    [Header("Поиск при потере цели")]
    public float searchDuration = 6f;
    public float searchRadius = 6f;

    [Header("Укрытие (при малом HP)")]
    public bool useCover = true;
    public Transform[] coverPoints;          // ручные точки укрытия (рекомендуется расставить у ящиков/стен)
    [Range(0f, 1f)] public float lowHealthCoverThreshold = 0.35f; // ниже этого % HP — прячется
    public float coverSearchRadius = 12f;    // если ручных точек нет — искать автоматически в этом радиусе
    public float hideDuration = 2.5f;        // сколько сидит за укрытием, не высовываясь
    public float peekDuration = 1.5f;        // сколько стреляет, высунувшись
    public float peekDistance = 2f;          // на сколько высовывается из-за укрытия

    [Header("Агрессия при попаданиях по игроку")]
    public int hitsToBecomeAggressive = 2;   // после стольких попаданий подряд — прёт вперёд
    public float aggressiveDistanceReduction = 4f; // насколько сокращает дистанцию боя

    [Header("Отладка стрельбы")]
    public bool debugLogging = true;         // логировать попадания/промахи в консоль

    [Header("Оповещение союзников")]
    public float alertRadius = 20f;
    public LayerMask enemyMask;

    [Header("Звуки (крики/команды)")]
    public AudioClip[] spottedSounds;     // "Обнаружил цель!"
    public AudioClip[] alertSounds;       // "Что это было?"
    public AudioClip[] loseTargetSounds;  // "Показалось..."
    public AudioClip[] shootSounds;

    [Header("Отладка")]
    public bool drawGizmos = true;

    // ---- внутреннее состояние ----
    private State currentState = State.Patrol;
    private NavMeshAgent agent;
    private AudioSource audioSource;
    private Rigidbody playerRb;
    private CharacterController playerCC;

    private int currentPatrolIndex = 0;
    private float waitTimer;
    private bool waitingAtPoint;

    private float suspicion;
    private Vector3 lastKnownPosition;
    private float perceptionTimer;
    private const float perceptionInterval = 0.15f;

    private float investigateTimer;
    private float nextSearchPointTimer;

    private float fireCooldown;
    private bool hasFiredReactionShot;
    private float reactionTimer;
    private float loseSightTimer;

    private Vector3 coverDestination;
    private CoverPhase coverPhase;
    private float hideTimer;
    private float peekTimer;

    private int consecutiveHits;
    private bool isAggressive;

    public bool IsAggressive => isAggressive;
    public int ConsecutiveHits => consecutiveHits;

    public State CurrentState => currentState;
    public float SuspicionLevel => suspicion;

    // Защита от ошибок "can only be called on an active agent that has been
    // placed on a NavMesh" — возникают, если NavMesh не запечён или враг
    // временно не стоит на нём (например, при спавне до бейка).
    bool AgentReady() => agent != null && agent.enabled && agent.isOnNavMesh;

    void SafeSetDestination(Vector3 pos)
    {
        if (AgentReady()) agent.SetDestination(pos);
    }

    // ВАЖНО: не используем "animator?.SetBool(...)" — оператор ?. в C# не учитывает
    // переопределённый Unity оператор ==, поэтому если поле animator оставлено
    // пустым в инспекторе, ?. всё равно попытается вызвать метод и упадёт с
    // UnassignedReferenceException. Явная проверка "animator != null" работает верно.
    void AnimSetBool(string name, bool value)
    {
        if (animator != null) animator.SetBool(name, value);
    }

    void AnimSetTrigger(string name)
    {
        if (animator != null) animator.SetTrigger(name);
    }

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;

        if (health == null) health = GetComponent<Enemy>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    void Start()
    {
        if (player == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null) player = playerObj.transform;
        }

        if (player != null)
        {
            playerRb = player.GetComponent<Rigidbody>();
            playerCC = player.GetComponent<CharacterController>();
        }

        if (health != null)
        {
            health.OnDeath += HandleDeath;
            health.OnDamaged += HandleDamaged;
        }

        agent.speed = walkSpeed;
        loseSightTimer = loseSightGrace;

        if (patrolPoints != null && patrolPoints.Length > 0)
        {
            SafeSetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= HandleDeath;
            health.OnDamaged -= HandleDamaged;
        }
    }

    void Update()
    {
        if (currentState == State.Dead) return;

        switch (currentState)
        {
            case State.Patrol: PatrolState(); break;
            case State.Investigate: InvestigateState(); break;
            case State.Combat: CombatState(); break;
            case State.Cover: CoverState(); break;
        }
    }

    // ==================== ПАТРУЛИРОВАНИЕ ====================

    void PatrolState()
    {
        AnimSetBool("IsRunning", false);
        AnimSetBool("InCombat", false);

        UpdatePerception();
        if (currentState != State.Patrol) return; // ушли в Investigate/Combat

        if (patrolPoints == null || patrolPoints.Length == 0) return;

        if (!AgentReady()) return; // NavMesh ещё не готов — ждём молча, без ошибок

        if (!waitingAtPoint)
        {
            agent.speed = walkSpeed;
            if (!agent.pathPending && agent.remainingDistance < 0.3f)
            {
                waitingAtPoint = true;
                waitTimer = patrolWaitTime;
                AnimSetTrigger("LookAround");
            }
        }
        else
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                waitingAtPoint = false;
                AdvancePatrolIndex();
                SafeSetDestination(patrolPoints[currentPatrolIndex].position);
            }
        }
    }

    void AdvancePatrolIndex()
    {
        if (patrolPoints.Length == 0) return;

        if (patrolRandom)
        {
            if (patrolPoints.Length > 1)
            {
                int newIndex;
                do { newIndex = Random.Range(0, patrolPoints.Length); }
                while (newIndex == currentPatrolIndex);
                currentPatrolIndex = newIndex;
            }
        }
        else
        {
            if (currentPatrolIndex >= patrolPoints.Length - 1)
            {
                if (patrolLoop) currentPatrolIndex = 0;
                // иначе остаётся на последней точке
            }
            else currentPatrolIndex++;
        }
    }

    // ==================== РАССЛЕДОВАНИЕ / ПОИСК ====================

    void EnterInvestigate(Vector3 pos)
    {
        if (currentState == State.Combat) return; // бой важнее
        currentState = State.Investigate;
        lastKnownPosition = pos;
        investigateTimer = searchDuration;
        nextSearchPointTimer = Time.time + 999f;

        if (AgentReady())
        {
            agent.updateRotation = true;
            agent.isStopped = false;
            agent.speed = runSpeed;
            agent.SetDestination(pos);
        }
        PlayRandomSound(alertSounds);
        AnimSetBool("InCombat", false);
    }

    void InvestigateState()
    {
        UpdatePerception();
        if (currentState != State.Investigate) return; // ушли в Combat
        if (!AgentReady()) return;

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            investigateTimer -= Time.deltaTime;
            if (investigateTimer <= 0f)
            {
                ReturnToPatrol();
                return;
            }

            if (Time.time >= nextSearchPointTimer)
            {
                nextSearchPointTimer = Time.time + 1.5f;
                Vector3 randomPoint = lastKnownPosition + Random.insideUnitSphere * searchRadius;
                if (NavMesh.SamplePosition(randomPoint, out NavMeshHit navHit, searchRadius, NavMesh.AllAreas))
                {
                    SafeSetDestination(navHit.position);
                }
            }
        }

        AnimSetBool("IsRunning", agent.velocity.magnitude > 0.3f);
    }

    void ReturnToPatrol()
    {
        currentState = State.Patrol;
        suspicion = 0f;
        consecutiveHits = 0;
        isAggressive = false;
        PlayRandomSound(loseTargetSounds);
        AnimSetBool("InCombat", false);

        if (AgentReady())
        {
            agent.updateRotation = true;
            agent.isStopped = false;
            agent.speed = walkSpeed;
            if (patrolPoints != null && patrolPoints.Length > 0)
                agent.SetDestination(patrolPoints[currentPatrolIndex].position);
        }
    }

    // ==================== БОЙ ====================

    void EnterCombat()
    {
        if (currentState == State.Combat)
        {
            lastKnownPosition = player.position;
            return;
        }

        currentState = State.Combat;
        if (AgentReady())
        {
            agent.updateRotation = false; // разворотом к цели управляем сами (FaceTarget)
            agent.isStopped = false;
            agent.speed = runSpeed;
        }
        hasFiredReactionShot = false;
        reactionTimer = 0f;
        loseSightTimer = loseSightGrace;
        lastKnownPosition = player.position;
        consecutiveHits = 0;
        isAggressive = false;

        PlayRandomSound(spottedSounds);
        AlertAllies();
        AnimSetBool("InCombat", true);
    }

    void CombatState()
    {
        if (player == null) { LoseTargetFromCombat(); return; }

        // Мало HP — сначала пытаемся спрятаться, не геройствуем
        if (useCover && health != null && health.HealthPercent <= lowHealthCoverThreshold)
        {
            EnterCover();
            if (currentState == State.Cover) return;
        }

        bool canSee = CanSeeTarget();
        float distance = Vector3.Distance(transform.position, player.position);
        bool agentReady = AgentReady();
        float targetDistance = EffectiveCombatDistance();

        if (canSee)
        {
            lastKnownPosition = player.position;
            loseSightTimer = loseSightGrace;

            if (agentReady)
            {
                agent.speed = runSpeed;
                if (distance > targetDistance + 2f)
                {
                    agent.isStopped = false;
                    agent.SetDestination(player.position);
                }
                else if (distance < minCombatDistance)
                {
                    Vector3 away = (transform.position - player.position).normalized;
                    agent.isStopped = false;
                    agent.SetDestination(transform.position + away * 4f);
                }
                else
                {
                    agent.isStopped = true;
                }
            }

            FaceTarget(); // разворот не зависит от NavMesh — работает всегда
            TryShoot();
        }
        else
        {
            loseSightTimer -= Time.deltaTime;
            if (loseSightTimer <= 0f)
            {
                LoseTargetFromCombat();
                return;
            }
            if (agentReady)
            {
                agent.isStopped = false;
                agent.SetDestination(lastKnownPosition);
            }
        }

        if (agentReady)
            AnimSetBool("IsRunning", agent.velocity.magnitude > 0.3f);
    }

    // Насколько близко враг хочет держаться игрока прямо сейчас.
    // Обычно — preferredCombatDistance. Если недавно попал пару раз подряд —
    // подходит ближе (уверен в себе). Если в укрытии — это не используется.
    float EffectiveCombatDistance()
    {
        if (isAggressive)
            return Mathf.Max(minCombatDistance, preferredCombatDistance - aggressiveDistanceReduction);
        return preferredCombatDistance;
    }

    void LoseTargetFromCombat()
    {
        currentState = State.Investigate;
        investigateTimer = searchDuration;
        nextSearchPointTimer = Time.time + 999f;
        consecutiveHits = 0;
        isAggressive = false;
        if (AgentReady())
        {
            agent.updateRotation = true;
            agent.SetDestination(lastKnownPosition);
        }
        PlayRandomSound(loseTargetSounds);
        AnimSetBool("InCombat", false);
    }

    // ==================== УКРЫТИЕ (при малом HP) ====================

    void EnterCover()
    {
        if (!useCover || currentState == State.Cover) return;

        Vector3 destination;
        Transform manual = FindNearestManualCover();
        if (manual != null)
        {
            destination = manual.position;
        }
        else if (TryFindAutoCoverPoint(out Vector3 auto))
        {
            destination = auto;
        }
        else
        {
            return; // укрытия не нашлось — остаёмся в обычном бою на общих правах
        }

        currentState = State.Cover;
        coverDestination = destination;
        coverPhase = CoverPhase.MovingToCover;
        isAggressive = false; // раненый и осторожный — не время переть вперёд

        if (AgentReady())
        {
            agent.updateRotation = true;
            agent.isStopped = false;
            agent.speed = runSpeed;
            agent.SetDestination(destination);
        }
        AnimSetBool("InCombat", true);
    }

    void CoverState()
    {
        if (player == null) { ReturnToPatrol(); return; }

        // Если здоровье снова в порядке (аптечка и т.п.) — возвращаемся в обычный бой
        if (health != null && health.HealthPercent > lowHealthCoverThreshold * 1.5f)
        {
            currentState = State.Combat;
            return;
        }

        bool agentReady = AgentReady();

        switch (coverPhase)
        {
            case CoverPhase.MovingToCover:
                if (agentReady && !agent.pathPending && agent.remainingDistance < 0.4f)
                {
                    coverPhase = CoverPhase.Hiding;
                    hideTimer = hideDuration;
                    agent.isStopped = true;
                }
                break;

            case CoverPhase.Hiding:
                hideTimer -= Time.deltaTime;
                if (hideTimer <= 0f)
                {
                    Vector3 peekPos = ComputePeekPosition();
                    coverPhase = CoverPhase.MovingToPeek;
                    if (agentReady)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(peekPos);
                    }
                }
                break;

            case CoverPhase.MovingToPeek:
                if (agentReady && !agent.pathPending && agent.remainingDistance < 0.4f)
                {
                    coverPhase = CoverPhase.Peeking;
                    peekTimer = peekDuration;
                    agent.isStopped = true;
                }
                // пока идёт к точке "высовывания" — уже можно стрелять, если видит
                if (CanSeeTarget())
                {
                    FaceTarget();
                    TryShoot();
                }
                break;

            case CoverPhase.Peeking:
                peekTimer -= Time.deltaTime;
                if (CanSeeTarget())
                {
                    FaceTarget();
                    TryShoot();
                }
                if (peekTimer <= 0f)
                {
                    coverPhase = CoverPhase.MovingToCover;
                    if (agentReady)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(coverDestination);
                    }
                }
                break;
        }

        if (agentReady)
            AnimSetBool("IsRunning", agent.velocity.magnitude > 0.3f);
    }

    // Точка, куда враг "высовывается" из укрытия в сторону игрока, чтобы выстрелить
    Vector3 ComputePeekPosition()
    {
        Vector3 dirToPlayer = player.position - coverDestination;
        dirToPlayer.y = 0f;
        if (dirToPlayer.sqrMagnitude < 0.01f) return coverDestination;
        dirToPlayer.Normalize();

        Vector3 candidate = coverDestination + dirToPlayer * peekDistance;
        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, peekDistance + 1f, NavMesh.AllAreas))
            return hit.position;
        return coverDestination;
    }

    // Ближайшая точка из вручную расставленных в инспекторе (надёжнее автопоиска)
    Transform FindNearestManualCover()
    {
        if (coverPoints == null || coverPoints.Length == 0) return null;

        Transform best = null;
        float bestDist = float.MaxValue;
        foreach (var cp in coverPoints)
        {
            if (cp == null) continue;
            float d = Vector3.Distance(transform.position, cp.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = cp;
            }
        }
        return best;
    }

    // Автоматический поиск точки, где обзор игрока перекрыт препятствием.
    // Работает, только если coverPoints не заданы вручную. Менее надёжен,
    // чем ручные точки — для продакшена лучше расставить coverPoints самому.
    bool TryFindAutoCoverPoint(out Vector3 result)
    {
        result = transform.position;
        if (player == null) return false;

        bool found = false;
        float bestScore = float.MinValue;
        const int samples = 10;

        for (int i = 0; i < samples; i++)
        {
            float angle = i * (360f / samples);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            Vector3 candidate = transform.position + dir * coverSearchRadius;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 3f, NavMesh.AllAreas)) continue;

            Vector3 eyeAtCandidate = navHit.position + Vector3.up * 1.6f;
            Vector3 toPlayer = (player.position + Vector3.up) - eyeAtCandidate;

            // Годная точка укрытия — та, где обзор на игрока перекрыт препятствием
            bool blocked = Physics.Raycast(eyeAtCandidate, toPlayer.normalized, toPlayer.magnitude, obstacleMask);
            if (!blocked) continue;

            float distToPlayer = Vector3.Distance(navHit.position, player.position);
            if (distToPlayer > bestScore) // предпочитаем более дальние из подходящих
            {
                bestScore = distToPlayer;
                result = navHit.position;
                found = true;
            }
        }

        return found;
    }

    void FaceTarget()
    {
        if (player == null) return;
        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);
    }

    void TryShoot()
    {
        if (!hasFiredReactionShot)
        {
            reactionTimer += Time.deltaTime;
            if (reactionTimer < reactionDelay) return;
            hasFiredReactionShot = true;
        }

        fireCooldown -= Time.deltaTime;
        if (fireCooldown > 0f) return;
        if (Vector3.Distance(transform.position, player.position) > attackRange) return;

        Shoot();
        fireCooldown = 1f / Mathf.Max(0.01f, fireRate);
    }

    void Shoot()
    {
        AnimSetTrigger("Shoot");
        PlayRandomSound(shootSounds);

        Vector3 origin = gunMuzzle != null ? gunMuzzle.position : transform.position + Vector3.up * 1.5f;
        Vector3 targetPoint = PredictTargetPosition();
        Vector3 dir = ApplyAccuracySpread((targetPoint - origin).normalized);

        if (muzzleFlashPrefab != null && gunMuzzle != null)
        {
            Instantiate(muzzleFlashPrefab, gunMuzzle.position, gunMuzzle.rotation);
        }

        if (debugLogging) Debug.DrawRay(origin, dir * attackRange, Color.red, 1f);

        // QueryTriggerInteraction.Collide — важно, если коллайдер игрока стоит как Trigger
        // (частый случай, если движение сделано на CharacterController + отдельный trigger-коллайдер).
        if (Physics.Raycast(origin, dir, out RaycastHit hit, attackRange, hittableMask, QueryTriggerInteraction.Collide))
        {
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damagePerShot, transform.position);

                bool hitPlayer = player != null && (hit.transform == player || hit.transform.IsChildOf(player));
                if (hitPlayer)
                {
                    consecutiveHits++;
                    if (consecutiveHits >= hitsToBecomeAggressive)
                        isAggressive = true;
                }

                if (debugLogging)
                    Debug.Log($"{name}: попал в {hit.collider.name}, нанёс {damagePerShot} урона.");
            }
            else if (debugLogging)
            {
                Debug.LogWarning($"{name}: попал в \"{hit.collider.name}\", но ни на нём, ни на его родителях " +
                    $"нет компонента с интерфейсом IDamageable — урон НЕ нанесён. " +
                    $"Проверь, что на игроке есть скрипт, реализующий IDamageable (например, PlayerHealth.cs).");
            }
        }
        else if (debugLogging)
        {
            Debug.Log($"{name}: выстрел мимо — рейкаст ничего не задел в hittableMask. " +
                $"Проверь, что слой игрока (Layer) включён в поле Hittable Mask в инспекторе.");
        }
    }

    Vector3 PredictTargetPosition()
    {
        Vector3 basePos = player.position + Vector3.up * 1f;
        if (!predictMovement) return basePos;

        Vector3 velocity = Vector3.zero;
        if (playerRb != null) velocity = playerRb.velocity;
        else if (playerCC != null) velocity = playerCC.velocity;

        if (velocity.sqrMagnitude < 0.01f) return basePos;

        float distance = Vector3.Distance(transform.position, basePos);
        float travelTime = distance / Mathf.Max(1f, assumedBulletSpeed);
        return basePos + velocity * travelTime;
    }

    Vector3 ApplyAccuracySpread(Vector3 dir)
    {
        float maxSpreadAngle = Mathf.Lerp(8f, 0f, accuracy); // accuracy=1 -> без разброса
        Vector3 randomOffset = Random.insideUnitSphere * Mathf.Tan(maxSpreadAngle * Mathf.Deg2Rad);
        return (dir + randomOffset).normalized;
    }

    // ==================== ВОСПРИЯТИЕ ====================

    void UpdatePerception()
    {
        if (player == null) return;

        perceptionTimer -= Time.deltaTime;
        if (perceptionTimer > 0f) return;
        perceptionTimer = perceptionInterval;

        bool seen = CanSeeTarget();
        if (seen)
        {
            lastKnownPosition = player.position;
            float distance = Vector3.Distance(transform.position, player.position);
            float rate = distance < closeRangeRadius ? suspicionBuildRate * 3f : suspicionBuildRate;
            suspicion += rate * perceptionInterval;

            if (suspicion >= suspicionThreshold)
            {
                if (currentState != State.Combat) EnterCombat();
            }
            else if (currentState == State.Patrol && suspicion > suspicionThreshold * 0.4f)
            {
                EnterInvestigate(player.position);
            }
        }
        else
        {
            suspicion = Mathf.Max(0f, suspicion - suspicionDecayRate * perceptionInterval);
        }
    }

    bool CanSeeTarget()
    {
        if (player == null) return false;

        Vector3 origin = eyes != null ? eyes.position : transform.position + Vector3.up * 1.6f;
        Vector3 targetPos = player.position + Vector3.up * 1f;
        Vector3 toTarget = targetPos - origin;
        float distance = toTarget.magnitude;

        if (distance > viewRadius) return false;

        if (distance > closeRangeRadius)
        {
            float angle = Vector3.Angle(transform.forward, toTarget.normalized);
            if (angle > viewAngle * 0.5f) return false;
        }

        // Если на пути есть препятствие раньше, чем цель — обзор загорожен
        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, distance, obstacleMask | targetMask))
        {
            bool hitIsTarget = ((1 << hit.collider.gameObject.layer) & targetMask) != 0;
            if (!hitIsTarget) return false;
        }

        return true;
    }

    /// <summary>
    /// Вызывается извне (например, оружием игрока) при выстреле рядом,
    /// чтобы враг мог среагировать на звук, даже не видя стрелка.
    /// </summary>
    public void HearNoise(Vector3 position, float loudness = 1f)
    {
        if (currentState == State.Dead || currentState == State.Combat) return;
        float distance = Vector3.Distance(transform.position, position);
        if (distance <= hearingRadius * Mathf.Max(0.1f, loudness))
        {
            EnterInvestigate(position);
        }
    }

    /// <summary>Статический хелпер: оповестить всех врагов о шуме в радиусе (напр. выстрел игрока).</summary>
    public static void NotifyNoise(Vector3 position, float radius, float loudness = 1f)
    {
        Collider[] cols = Physics.OverlapSphere(position, radius);
        foreach (var c in cols)
        {
            EnemyAI ai = c.GetComponent<EnemyAI>();
            ai?.HearNoise(position, loudness);
        }
    }

    // ==================== ОПОВЕЩЕНИЕ СОЮЗНИКОВ ====================

    void AlertAllies()
    {
        Collider[] cols = Physics.OverlapSphere(transform.position, alertRadius, enemyMask);
        foreach (var c in cols)
        {
            if (c.gameObject == gameObject) continue;
            EnemyAI ally = c.GetComponent<EnemyAI>();
            ally?.AlertToPosition(lastKnownPosition);
        }
    }

    public void AlertToPosition(Vector3 pos)
    {
        if (currentState == State.Dead || currentState == State.Combat) return;
        EnterInvestigate(pos);
    }

    // ==================== СМЕРТЬ / УРОН ====================

    void HandleDamaged(float damage, Vector3 attackerPosition)
    {
        if (currentState == State.Dead) return;

        suspicion = suspicionThreshold; // получил урон — точно знает, что на него напали
        if (CanSeeTarget())
            EnterCombat();
        else
            EnterInvestigate(attackerPosition);
    }

    void HandleDeath()
    {
        currentState = State.Dead;
        if (AgentReady())
        {
            agent.isStopped = true;
        }
        if (agent != null) agent.enabled = false;

        if (animator != null)
        {
            // Ожидается, что в Animator Controller есть параметр-триггер "Death"
            // и состояние с анимацией падения. Она сама уложит тело на землю.
            animator.SetTrigger("Death");
        }
        else
        {
            // Аниматора нет (или не настроен) — заваливаем тело простым поворотом,
            // чтобы враг не стоял истуканом после смерти.
            StartCoroutine(FallOverFallback());
        }

        enabled = false; // выключаем Update (корутина выше при этом всё равно доработает)
    }

    IEnumerator FallOverFallback()
    {
        float duration = 0.6f;
        float elapsed = 0f;
        Quaternion startRot = transform.rotation;
        Quaternion endRot = startRot * Quaternion.Euler(90f, Random.Range(-30f, 30f), 0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(startRot, endRot, elapsed / duration);
            yield return null;
        }
        transform.rotation = endRot;
    }

    // ==================== ОТЛАДКА ====================

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, hearingRadius);

        Vector3 forward = transform.forward;
        Quaternion leftRot = Quaternion.AngleAxis(-viewAngle * 0.5f, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(viewAngle * 0.5f, Vector3.up);
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, leftRot * forward * viewRadius);
        Gizmos.DrawRay(transform.position, rightRot * forward * viewRadius);

        if (currentState == State.Cover)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(coverDestination, 0.5f);
            Gizmos.DrawLine(transform.position, coverDestination);
        }
    }

    void PlayRandomSound(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0 || audioSource == null) return;
        audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)]);
    }
}