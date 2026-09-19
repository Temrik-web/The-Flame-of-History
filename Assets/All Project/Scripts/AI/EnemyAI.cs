using System.Collections.Generic;
using UnityEngine;

namespace FlameOfHistory.AI
{
    /// <summary>
    /// Боевой ИИ врага: восприятие, состояния, стрельба.
    ///
    /// Передвижением сам не занимается — всё через <see cref="EnemyMotor"/>,
    /// поэтому враг ходит по земле и с запечённым NavMesh, и без него.
    /// Звуки и крики — через <see cref="EnemyVoice"/>.
    /// Оружие в руках — через <see cref="EnemyLoadout"/> (или ссылкой вручную).
    /// </summary>
    [RequireComponent(typeof(EnemyMotor))]
    [RequireComponent(typeof(CharacterHealth))]
    [DisallowMultipleComponent]
    public sealed class EnemyAI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform eyePoint;
        [SerializeField] private HitscanWeapon weapon;
        [SerializeField] private PatrolRoute patrolRoute;
        [SerializeField] private Animator animator;
        [Tooltip("Голос врага. Пусто — будет найден на этом объекте автоматически.")]
        [SerializeField] private EnemyVoice voice;
        [Tooltip("Выдача оружия. Пусто — будет найдена на этом объекте автоматически.")]
        [SerializeField] private EnemyLoadout loadout;

        [Header("Target")]
        [SerializeField] private Team enemyTeam = Team.Axis;
        [SerializeField] private LayerMask targetMask;
        [SerializeField] private LayerMask visibilityMask = ~0;

        [Header("Perception")]
        [SerializeField, Min(1f)] private float viewDistance = 45f;
        [SerializeField, Range(1f, 180f)] private float fieldOfView = 110f;
        [SerializeField, Min(1f)] private float verticalViewTolerance = 6f;
        [SerializeField, Min(0.02f)] private float perceptionInterval = 0.15f;
        [SerializeField, Min(0f)] private float targetMemoryDuration = 8f;

        [Header("Awareness")]
        [SerializeField, Min(0.05f)] private float timeToDetect = 0.9f;
        [SerializeField, Min(0.05f)] private float awarenessDecay = 0.35f;
        [SerializeField, Min(0f)] private float reactionTime = 0.55f;

        [Header("Patrol")]
        [SerializeField, Min(0f)] private float patrolSpeed = 1.8f;
        [SerializeField, Min(0f)] private float pointWaitDuration = 2f;
        [SerializeField, Min(0.1f)] private float pointReachRadius = 0.6f;
        [Tooltip("Если маршрут не задан — враг сам бродит по округе, а не стоит на месте.")]
        [SerializeField] private bool wanderWhenNoRoute = true;
        [SerializeField, Min(1f)] private float wanderRadius = 14f;
        [Tooltip("Сколько ждать на месте, если новую точку для прогулки найти не удалось.")]
        [SerializeField, Min(0.1f)] private float wanderRetryDelay = 1.5f;

        [Header("Combat")]
        [SerializeField, Min(0f)] private float chaseSpeed = 3.8f;
        [SerializeField, Min(1f)] private float preferredCombatDistance = 20f;
        [SerializeField, Min(1f)] private float maximumCombatDistance = 38f;
        [SerializeField, Min(0f)] private float combatRepositionDistance = 5f;
        [Tooltip("Скорость отхода/стрейфа в бою с оружием, м/с. Человек с винтовкой " +
                 "пятится медленно, а не спринтует спиной вперёд.")]
        [SerializeField, Min(0.1f)] private float repositionSpeed = 1.4f;
        [Tooltip("Скорость сближения в бою под огнём, м/с. Бежит только в погоне " +
                 "вслепую (Chase) или драпая без выстрелов (Retreat).")]
        [SerializeField, Min(0.1f)] private float combatAdvanceSpeed = 1.9f;

        [Header("Fight pacing (темп схватки, как в топ-играх)")]
        [Tooltip("Первые секунды схватки враг бьёт кучнее (был наготове): " +
                 "первая кровь приходит быстро. Длительность, сек.")]
        [SerializeField, Min(0f)] private float openingDuration = 4f;
        [Tooltip("Множитель разброса в открытии схватки.")]
        [SerializeField, Min(0.1f)] private float openingSpread = 0.55f;
        [Tooltip("Множитель разброса в середине (1–2 попадания): свист мимо, передышка игроку.")]
        [SerializeField, Min(0.1f)] private float midFightSpread = 1.25f;
        [Tooltip("Множитель пауз между очередями в середине схватки.")]
        [SerializeField, Min(0.5f)] private float midFightPauseMult = 1.4f;
        [Tooltip("Множитель разброса в концовке (3+ попадания): пристрелялся, давит.")]
        [SerializeField, Min(0.1f)] private float closingSpread = 0.85f;
        [Tooltip("Множители урона по номеру попадания (0,1,2,3,4+). Сумма × базовый урон " +
                 "≈ 100: игрок умирает примерно за 5 попаданий, первое — царапина.")]
        [SerializeField] private float[] pacingDamageStages = { 0.45f, 0.65f, 0.8f, 1f, 1.2f };
        [Tooltip("Живой разброс урона стадии ±: не выглядит скриптом.")]
        [SerializeField, Range(0f, 0.5f)] private float pacingDamageJitter = 0.15f;
        [Tooltip("Сколько держать бой при кратковременной помехе (рывки луча " +
                 "из-за углов/косяков), прежде чем сорваться в погоню. Сек.")]
        [SerializeField, Min(0f)] private float combatLoseSightGrace = 0.4f;

        [Header("Humanity (человечность)")]
        [Tooltip("Скорость доводки корпуса в бою, град/сек. Человек доворачивается " +
                 "медленно — по стрейфящейся цели мажет. Было 540 (робот).")]
        [SerializeField, Min(20f)] private float combatTurnSpeed = 115f;
        [Tooltip("Разброс времени реакции: при каждом захвате цели реакция " +
                 "умножается на случайное 0.8–1.5. Кто-то шустрый, кто-то тормоз.")]
        [SerializeField] private bool jitterReaction = true;
        [Tooltip("Увод ствола вверх к концу очереди (отдача): первые пули точнее. Метры.")]
        [SerializeField, Min(0f)] private float burstClimb = 0.35f;
        [Tooltip("Пауза-осмотр на точках прогулки: стоит и смотрит, а не марширует. Мин. сек.")]
        [SerializeField, Min(0f)] private float wanderWaitMin = 0.5f;
        [Tooltip("Пауза-осмотр на точках прогулки. Макс. сек.")]
        [SerializeField, Min(0f)] private float wanderWaitMax = 2f;
        [Tooltip("Разброс темпа прогулки ±: каждый маршрут идёт чуть иначе.")]
        [SerializeField, Range(0f, 0.4f)] private float wanderSpeedVariation = 0.1f;
        [Tooltip("Амплитуда осматривающих качаний в поиске, град.")]
        [SerializeField, Min(10f)] private float searchSweepAmplitude = 70f;
        [Tooltip("Скорость этих качаний, град/сек.")]
        [SerializeField, Min(10f)] private float searchSweepSpeed = 95f;
        [SerializeField, Range(1, 20)] private int minimumBurstSize = 2;
        [SerializeField, Range(1, 20)] private int maximumBurstSize = 5;
        [SerializeField, Min(0f)] private float minimumBurstPause = 0.5f;
        [SerializeField, Min(0f)] private float maximumBurstPause = 1.6f;

        [Header("Accuracy (метры разброса по цели)")]
        [SerializeField, Min(0f)] private float baseSpread = 0.35f;
        [SerializeField, Min(0f)] private float spreadPerTenMeters = 0.45f;
        [SerializeField, Min(1f)] private float movingSpreadMultiplier = 2.2f;
        [SerializeField, Min(1f)] private float suppressedSpreadMultiplier = 3f;
        [SerializeField, Range(0f, 1f)] private float leadAccuracy = 0.6f;

        [Header("Retreat / Suppression")]
        [SerializeField, Range(0f, 1f)] private float retreatHealthThreshold = 0.25f;
        [SerializeField, Min(1f)] private float retreatDistance = 18f;
        [SerializeField, Min(0f)] private float retreatSpeed = 4.2f;
        [SerializeField, Min(0f)] private float retreatDuration = 6f;
        [SerializeField, Min(0.1f)] private float suppressionDecay = 0.6f;

        [Header("Navigation")]
        [SerializeField, Min(0.05f)] private float pathRefreshInterval = 0.25f;

        [Header("Death")]
        [Tooltip("Через сколько секунд убрать труп. 0 — оставить навсегда.")]
        [SerializeField, Min(0f)] private float corpseLifetime = 0f;

        [Header("Debug")]
        [Tooltip("Писать в консоль смену состояний, захват и потерю цели. " +
                 "Включай только у одного тестового врага — иначе будет спам.")]
        [SerializeField] private bool debugLogging;

        public EnemyState State { get; private set; }
        public Transform CurrentTarget => _target;
        public float Awareness => _awareness;
        public float Suppression => _suppression;

        /// <summary>Текущее оружие врага (может быть null — тогда он просто преследует).</summary>
        public HitscanWeapon Weapon => weapon;

        private EnemyMotor _motor;
        private CharacterHealth _health;

        private Transform _target;
        private CharacterHealth _targetHealth;
        private Vector3 _lastKnownTargetPosition;
        private Vector3 _targetVelocity;
        private Vector3 _prevTargetPosition;
        private float _lastTargetSeenTime = float.NegativeInfinity;
        private bool _firstSightAcquired;
        private float _canFireAfter;

        private float _awareness;
        private float _suppression;
        private float _lastSuppressionShout;
        private Vector3 _suspicionPoint;
        private bool _hasSuspicion;

        private float _nextPerceptionTime;
        private float _nextPathRefreshTime;
        private float _patrolWaitUntil;
        private bool _waitingAtPoint;
        private float _retreatUntil;
        private float _nextWanderAttemptTime;

        private int _patrolIndex;
        private Vector3 _homePosition;
        private int _shotsRemaining;
        private float _nextBurstTime;

        // Точка отхода в бою в упор: держим её, пока не дошли, а не выбираем
        // заново каждый кадр (иначе цель всё время за спиной — враг пятится
        // в стену и дёргается на месте).
        private Vector3 _repositionPoint;
        private bool _hasRepositionPoint;
        private float _nextRepositionPickTime;

        // Бой и диагностика.
        private float _lastVisibleTime;
        private float _nextDiagTime;
        private string _lastLosBlocker;
        private string _lastRejectNote;
        private string _lastNoiseInfo;

        // Точка проверки: куда смотрим в первую очередь в Search (последний
        // шум или мелькнувшая цель). Сначала всматриваемся в неё, потом обзор.
        private Vector3 _searchCenter;
        private float _searchFaceUntil;
        private float _searchBaseYaw;
        private bool _searchBaseCaptured;

        // Человечность огня и походки.
        private int _burstShotIndex;
        private float _wanderSpeedFactor = 1f;
        private float _chaseStuckTime = -1f;

        // Темп схватки с текущей целью.
        private Transform _engageTarget;
        private float _engageStartTime;
        private float _engageEndTime = float.NegativeInfinity;
        private int _engageHits;
        private float _lastTargetHp;
        private int _diagConsidered;
        private int _diagRejectedTeam;
        private int _diagRejectedDead;
        private int _diagRejectedLayer;
        private int _diagRejectedDist;
        private int _diagRejectedFov;
        private int _diagRejectedLos;

        private readonly Collider[] _targetBuffer = new Collider[128];
        private readonly HashSet<CharacterHealth> _candidateSet = new();
        private readonly RaycastHit[] _losBuffer = new RaycastHit[16];
        private readonly Collider[] _eyeBuffer = new Collider[4];

        // Реестр всех персонажей сцены (ведут его сами CharacterHealth).
        // Страховка поверх OverlapSphere: тот не видит цели вообще без Collider
        // или с одними триггерами (QueryTriggerInteraction.Ignore) — такой
        // персонаж иначе был бы невидим для врагов в принципе.
        private static readonly HashSet<CharacterHealth> s_characters = new();

        /// <summary>Зарегистрировать персонажа как видимую цель (зовёт CharacterHealth).</summary>
        public static void RegisterCharacter(CharacterHealth character)
        {
            if (character != null) s_characters.Add(character);
        }

        /// <summary>Убрать персонажа из видимых целей (зовёт CharacterHealth).</summary>
        public static void UnregisterCharacter(CharacterHealth character)
        {
            if (character != null) s_characters.Remove(character);
        }

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
        private static readonly int ShootHash = Animator.StringToHash("Shoot");
        private static readonly int ReloadHash = Animator.StringToHash("Reload");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DieHash = Animator.StringToHash("Die");

        private void Awake()
        {
            // Уже расставленные по сценам враги были сериализованы без EnemyMotor,
            // а RequireComponent задним числом его не добавляет. Поэтому чиним здесь,
            // иначе старые сцены падали бы с NullReferenceException.
            _motor = GetComponent<EnemyMotor>();
            if (_motor == null)
            {
                if (GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                    gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();

                _motor = gameObject.AddComponent<EnemyMotor>();
            }

            _health = GetComponent<CharacterHealth>();

            if (eyePoint == null) eyePoint = transform;
            if (voice == null) voice = GetComponent<EnemyVoice>();
            if (loadout == null) loadout = GetComponent<EnemyLoadout>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            // Если targetMask пуст (0) — враг слепой. Включаем все слои,
            // чтобы он хотя бы что-то видел. В идеале настрой в инспекторе.
            if (targetMask == 0)
            {
                targetMask = ~0;
                Debug.LogWarning($"[EnemyAI] {name}: targetMask был пуст (0) — " +
                    "автоматически включены все слои. Настрой targetMask в инспекторе " +
                    "для корректной работы.", this);
            }

            // Старая сериализованная сцена могла сохранить enemyTeam = Allies
            // (было до исправления бага). Игрок = Allies, враг должен быть = Axis.
            if (enemyTeam == Team.Allies)
            {
                enemyTeam = Team.Axis;
                Debug.LogWarning($"[EnemyAI] {name}: enemyTeam был Allies (старое значение " +
                    "из сцены) — автоматически исправлен на Axis.", this);
            }

            // Если оружие не проставлено вручную — берём то, что уже висит в иерархии.
            // EnemyLoadout при спавне оружия всё равно перезапишет ссылку через SetWeapon.
            if (weapon == null) weapon = GetComponentInChildren<HitscanWeapon>(true);
        }

        private void OnEnable()
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            NoiseSystem.NoiseCreated += OnNoiseCreated;
            SubscribeWeapon(weapon);
            State = EnemyState.Patrol;
        }

        private void Start()
        {
            _homePosition = transform.position;
            _searchCenter = _homePosition;

            // ChangeState(Patrol) здесь ничего бы не сделал: State уже Patrol после
            // OnEnable, и метод вышел бы по проверке «состояние не менялось».
            // Поэтому стартовые настройки мотора выставляем напрямую.
            _motor.SetSpeed(patrolSpeed);
            _motor.SetAutoRotation(true);
        }

        /// <summary>
        /// Настройка из редакторского мастера. Без SerializedObject —
        /// прямые присваивания надёжнее и не падают на опечатках в именах полей.
        /// </summary>
        public void ConfigureForWizard(
            Transform eye,
            HitscanWeapon weaponRef,
            Team targetTeam,
            LayerMask targets,
            LayerMask visibility,
            Animator animatorRef = null,
            PatrolRoute route = null)
        {
            eyePoint = eye;
            weapon = weaponRef;
            enemyTeam = targetTeam;
            targetMask = targets;
            visibilityMask = visibility;
            if (animatorRef != null) animator = animatorRef;
            if (route != null) patrolRoute = route;
        }

        /// <summary>Задать маршрут патрулирования (используется мастером на экземплярах сцены).</summary>
        public void SetPatrolRoute(PatrolRoute route) => patrolRoute = route;

        /// <summary>
        /// Сменить оружие в рантайме. Вызывается EnemyLoadout после выдачи оружия
        /// в руку, но можно дёргать и вручную — например, при подборе трофея.
        /// </summary>
        public void SetWeapon(HitscanWeapon newWeapon)
        {
            if (weapon == newWeapon) return;

            UnsubscribeWeapon(weapon);
            weapon = newWeapon;
            SubscribeWeapon(weapon);

            _shotsRemaining = 0;
            _nextBurstTime = 0f;
        }

        /// <summary>Публичный вход подавления — вызывается SuppressionReceiver при близком пролёте.</summary>
        public void ApplySuppression(float amount)
        {
            if (_health == null || !_health.IsAlive) return;
            _suppression = Mathf.Clamp01(_suppression + amount);

            if (_suppression > 0.7f && Time.time - _lastSuppressionShout > 5f)
            {
                _lastSuppressionShout = Time.time;
                if (voice != null) voice.PlaySuppressed();
            }
        }

        private void SubscribeWeapon(HitscanWeapon target)
        {
            if (target == null) return;

            // Порядок Awake между EnemyLoadout и EnemyAI не гарантирован, поэтому
            // сначала снимаем подписку — иначе можно подписаться дважды и получить
            // двойные триггеры анимации выстрела.
            target.Fired -= OnWeaponFired;
            target.ReloadStarted -= OnWeaponReloadStarted;

            target.Fired += OnWeaponFired;
            target.ReloadStarted += OnWeaponReloadStarted;
        }

        private void UnsubscribeWeapon(HitscanWeapon target)
        {
            if (target == null) return;
            target.Fired -= OnWeaponFired;
            target.ReloadStarted -= OnWeaponReloadStarted;
        }

        private void OnWeaponFired(Vector3 impactPoint)
        {
            if (animator != null) animator.SetTrigger(ShootHash);
            if (voice != null) voice.PlayCombatChatter();
        }

        private void OnWeaponReloadStarted()
        {
            if (animator != null) animator.SetTrigger(ReloadHash);
            if (voice != null) voice.PlayReload();
        }

        private void Update()
        {
            if (!_health.IsAlive) return;

            float dt = Time.deltaTime;
            _suppression = Mathf.Max(0f, _suppression - suppressionDecay * dt);

            if (Time.time >= _nextPerceptionTime)
            {
                _nextPerceptionTime = Time.time + perceptionInterval + Random.Range(0f, 0.04f);
                UpdatePerception();
            }

            if (_awareness < 1f || _target == null)
                _awareness = Mathf.Max(0f, _awareness - awarenessDecay * dt);

            if (ShouldRetreat() && State != EnemyState.Retreat && State != EnemyState.Dead)
                BeginRetreat();

            switch (State)
            {
                case EnemyState.Patrol: UpdatePatrol(); break;
                case EnemyState.Alert: UpdateAlert(); break;
                case EnemyState.Search: UpdateSearch(); break;
                case EnemyState.Chase: UpdateChase(); break;
                case EnemyState.Combat: UpdateCombat(); break;
                case EnemyState.Retreat: UpdateRetreat(); break;
            }

            UpdateAnimator();
            UpdateVoiceFootsteps();

            // Тикающий статус для диагностики: раз в 1.5 сек одной строкой видно
            // состояние, цель, причину слепоты и почему не стреляет.
            if (debugLogging && Time.time >= _nextDiagTime)
            {
                _nextDiagTime = Time.time + 1.5f;
                LogDiag();
            }
        }

        private void UpdatePerception()
        {
            Transform visible = FindBestVisibleTarget(out float visDistance);

            if (visible != null)
            {
                float distanceFactor = Mathf.Clamp01(1f - visDistance / viewDistance);
                float gain = (0.4f + distanceFactor) / Mathf.Max(0.05f, timeToDetect);
                _awareness = Mathf.Min(1f, _awareness + gain * perceptionInterval);

                SetTarget(visible);
                TrackTargetKinematics(visible.position);
                _lastKnownTargetPosition = visible.position;
                _lastTargetSeenTime = Time.time;

                if (_awareness >= 1f)
                {
                    _hasSuspicion = false;

                    if (!_firstSightAcquired)
                    {
                        _firstSightAcquired = true;
                        // Живой разброс реакции: каждый захват — чуть иначе.
                        _canFireAfter = Time.time + reactionTime *
                            (jitterReaction ? Random.Range(0.8f, 1.5f) : 1f);
                        if (debugLogging)
                            Debug.Log($"[EnemyAI] {name}: заметил цель «{visible.name}» " +
                                $"(дистанция {visDistance:F1} м)", this);
                    }

                    ChangeState(visDistance <= maximumCombatDistance
                        ? EnemyState.Combat
                        : EnemyState.Chase);
                }
                else if (State == EnemyState.Patrol)
                {
                    _suspicionPoint = visible.position;
                    _searchCenter = visible.position;
                    _hasSuspicion = true;
                    ChangeState(EnemyState.Alert);
                }
                return;
            }

            if (_target != null && Time.time - _lastTargetSeenTime > targetMemoryDuration)
            {
                if (debugLogging)
                    Debug.Log($"[EnemyAI] {name}: потерял цель «{_target.name}» — " +
                        $"не видел {targetMemoryDuration:F0} сек", this);
                ClearTarget();
                _firstSightAcquired = false;
            }

            if (_target == null && _hasSuspicion && _awareness > 0.15f &&
                State != EnemyState.Retreat && State != EnemyState.Search)
            {
                ChangeState(EnemyState.Alert);
            }
        }

        /// <summary>
        /// Тикающий статус (раз в 1.5 сек при debugLogging): одной строкой видно
        /// состояние, цель, причину слепоты и почему не стреляет.
        /// </summary>
        private void LogDiag()
        {
            string targetInfo = "—";
            float targetDist = 0f;
            if (_target != null)
            {
                targetDist = Vector3.Distance(transform.position, _target.position);
                bool vis = IsTargetVisibleNow(out string blocker);
                targetInfo = $"{_target.name} {targetDist:F1}м видно={(vis ? "да" : "нет")}" +
                    (vis ? "" : $" ({blocker})");
            }

            string weaponInfo;
            if (weapon == null)
            {
                weaponInfo = "НЕТ ОРУЖИЯ (не выдано — проверь EnemyLoadout/Weapon Prefab)";
            }
            else
            {
                string gate;
                if (weapon.IsReloading) gate = "перезарядка";
                else if (Time.time < _canFireAfter) gate = $"реакция ещё {(_canFireAfter - Time.time):F1}с";
                else if (Time.time < _nextBurstTime) gate = $"пауза между очередями ещё {(_nextBurstTime - Time.time):F1}с";
                else if (weapon.AmmunitionInMagazine <= 0)
                    gate = weapon.ReserveAmmunition > 0 ? "магазин пуст (перезаряжается)" : "НЕТ ПАТРОНОВ ВООБЩЕ";
                else if (_target != null && targetDist > weapon.Range)
                    gate = $"далеко: {targetDist:F0}м > дальности {weapon.Range:F0}м";
                else if (State != EnemyState.Combat && State != EnemyState.Retreat)
                    gate = $"не в бою ({State})";
                else gate = "ДОЛЖЕН СТРЕЛЯТЬ";
                weaponInfo = $"{weapon.name} {weapon.AmmunitionInMagazine}/{weapon.ReserveAmmunition} → {gate}";
            }

            Debug.Log($"[EnemyAI] {name}: {State} aware={_awareness:F2} | цель: {targetInfo} | " +
                $"{weaponInfo} | мотор: {(_motor != null ? _motor.Mode.ToString() : "—")} | " +
                $"схватка: {_engageHits} попаданий | " +
                $"канд.: {_diagConsidered} " +
                $"(ком×{_diagRejectedTeam} мертв×{_diagRejectedDead} слой×{_diagRejectedLayer} " +
                $"дист×{_diagRejectedDist} fov×{_diagRejectedFov} los×{_diagRejectedLos})" +
                (_lastRejectNote != null ? $" | {_lastRejectNote}" : "") +
                (_lastNoiseInfo != null ? $" | последний {_lastNoiseInfo}" : ""), this);
        }

        /// <summary>Чистая проверка видимости текущей цели (без побочек, для статуса).</summary>
        private bool IsTargetVisibleNow(out string blocker)
        {
            blocker = "";
            if (_target == null) return false;

            Vector3 chest = GetTargetAimPoint(_target);
            if ((chest - eyePoint.position).sqrMagnitude > viewDistance * viewDistance)
            {
                blocker = "дальше viewDistance";
                return false;
            }

            if (HasLineOfSight(_target, chest)) return true;
            if (HasLineOfSight(_target, chest + Vector3.up * 0.55f)) return true;

            blocker = _lastLosBlocker != null ? $"мешает {_lastLosBlocker}" : "нет луча";
            return false;
        }

        private void TrackTargetKinematics(Vector3 pos)
        {
            if (_prevTargetPosition != Vector3.zero)
            {
                Vector3 delta = (pos - _prevTargetPosition) / Mathf.Max(0.0001f, perceptionInterval);
                _targetVelocity = Vector3.Lerp(_targetVelocity, delta, 0.5f);
            }
            _prevTargetPosition = pos;
        }

        private Transform FindBestVisibleTarget(out float bestDistance)
        {
            bestDistance = float.MaxValue;

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, viewDistance, _targetBuffer,
                targetMask, QueryTriggerInteraction.Ignore);

            _candidateSet.Clear();
            _lastRejectNote = null;
            _diagConsidered = 0;
            _diagRejectedTeam = 0;
            _diagRejectedDead = 0;
            _diagRejectedLayer = 0;
            _diagRejectedDist = 0;
            _diagRejectedFov = 0;
            _diagRejectedLos = 0;
            Transform best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider col = _targetBuffer[i];
                if (col == null) continue;

                CharacterHealth cand = col.GetComponentInParent<CharacterHealth>();
                if (cand == null) continue;
                EvaluateCandidate(cand, ref best, ref bestScore, ref bestDistance);
            }

            // Добираем тех, кого OverlapSphere пропустил: цели вообще без
            // Collider или с одними триггерами, коллайдер на границе сферы.
            // Проверки дистанции, FOV и прямой видимости внутри
            // EvaluateCandidate — те же самые.
            foreach (CharacterHealth cand in s_characters)
            {
                if (cand == null) continue;
                EvaluateCandidate(cand, ref best, ref bestScore, ref bestDistance);
            }

            return best;
        }

        /// <summary>
        /// Общая проверка одного кандидата: команда, слой, дистанция, FOV,
        /// прямая видимость. Повторы отсекает _candidateSet, поэтому одного
        /// персонажа с несколькими коллайдерами считаем один раз.
        /// </summary>
        private void EvaluateCandidate(CharacterHealth cand, ref Transform best,
            ref float bestScore, ref float bestDistance)
        {
            if (!_candidateSet.Add(cand)) return;
            _diagConsidered++;
            // Труп — не цель: мёртвого не видим в принципе (и не берём в прицел).
            if (!cand.IsAlive) { _diagRejectedDead++; return; }
            if (cand.Team == enemyTeam) { _diagRejectedTeam++; return; }
            if (cand.gameObject == gameObject) return;

            // Для пути через OverlapSphere маска уже применена самим запросом,
            // для пути через реестр проверяем слой цели явно.
            if ((targetMask.value & (1 << cand.gameObject.layer)) == 0)
            {
                _diagRejectedLayer++;
                // Частый случай слепоты — не пишем каждый тик, видно в статусе.
                _lastRejectNote = $"слой {LayerMask.LayerToName(cand.gameObject.layer)} не в TargetMask";
                return;
            }

            Vector3 aimPoint = GetTargetAimPoint(cand.transform);
            Vector3 toTarget = aimPoint - eyePoint.position;
            float distance = toTarget.magnitude;
            if (distance <= 0.01f || distance > viewDistance) { _diagRejectedDist++; return; }

            if (Mathf.Abs(aimPoint.y - eyePoint.position.y) > verticalViewTolerance)
            {
                _diagRejectedDist++;
                return;
            }

            Vector3 flatFwd = eyePoint.forward; flatFwd.y = 0f;
            Vector3 flatDir = toTarget; flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.0001f) { _diagRejectedFov++; return; }

            float angle = Vector3.Angle(flatFwd, flatDir);
            if (angle > fieldOfView * 0.5f) { _diagRejectedFov++; return; }

            if (!HasLineOfSight(cand.transform, aimPoint)) { _diagRejectedLos++; return; }

            float score = distance + angle * 0.1f;
            if (score < bestScore)
            {
                bestScore = score;
                best = cand.transform;
                bestDistance = distance;
            }
        }

        private bool HasLineOfSight(Transform target, Vector3 targetPoint)
        {
            Vector3 origin = eyePoint.position;
            Vector3 dir = targetPoint - origin;
            float dist = dir.magnitude;
            if (dist <= 0.001f) return true;
            dir /= dist;

            // NonAlloc + пропуск попаданий в собственное тело: глаз стоит
            // внутри капсулы врага, и одиночный Raycast со старой маской ~0
            // бил в себя же — враг был слепым всегда и везде.
            int count = Physics.RaycastNonAlloc(origin, dir, _losBuffer,
                dist + 0.2f, visibilityMask, QueryTriggerInteraction.Ignore);

            // Враг прижался спиной к стене — глаз оказался внутри её коллайдера.
            // Именно эта стена обзор не закрывает (иначе прижатый враг слепнет
            // навсегда, теряет цель и уходит). Остальные стены — закрывают:
            // пропускаем только коллайдер, в котором сидит глаз, и только
            // вплотную (дальше 0.5 м стена уже считается честно).
            int eyeCount = Physics.OverlapSphereNonAlloc(origin, 0.25f, _eyeBuffer,
                visibilityMask, QueryTriggerInteraction.Ignore);

            // Сортировка вставками — буфер маленький, аллокаций нет.
            for (int i = 1; i < count; i++)
            {
                RaycastHit key = _losBuffer[i];
                int j = i - 1;
                while (j >= 0 && _losBuffer[j].distance > key.distance)
                {
                    _losBuffer[j + 1] = _losBuffer[j];
                    j--;
                }
                _losBuffer[j + 1] = key;
            }

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _losBuffer[i];
                Transform hitTransform = hit.transform;
                if (hitTransform == null) continue;

                // Своё тело, оружие и глаз обзор не закрывают.
                if (hitTransform.root == transform.root) continue;

                // Стена, в которой сидит глаз, — тоже не закрывает (см. выше).
                if (hit.distance < 0.5f && IsEnclosingEyeCollider(hit.collider, eyeCount))
                    continue;

                // Первое НЕ своё попадание решает: цель — видно, стена — нет.
                if (hitTransform == target || hitTransform.IsChildOf(target))
                {
                    _lastLosBlocker = null;
                    return true;
                }

                _lastLosBlocker = hitTransform.name;
                return false;
            }

            _lastLosBlocker = null;
            return true;
        }

        private bool IsEnclosingEyeCollider(Collider collider, int eyeCount)
        {
            if (collider == null) return false;
            for (int i = 0; i < eyeCount; i++)
                if (_eyeBuffer[i] == collider) return true;
            return false;
        }

        // =====================================================================
        // Патрулирование и прогулка
        // =====================================================================

        private void UpdatePatrol()
        {
            if (patrolRoute == null || patrolRoute.Count == 0)
            {
                if (wanderWhenNoRoute) UpdateWander();
                else _motor.Stop();
                return;
            }

            Transform point = patrolRoute.GetPoint(_patrolIndex);
            if (point == null) return;

            _motor.SetSpeed(patrolSpeed);

            if (_waitingAtPoint)
            {
                if (Time.time >= _patrolWaitUntil)
                {
                    _waitingAtPoint = false;
                    AdvancePatrolIndex();

                    Transform next = patrolRoute.GetPoint(_patrolIndex);
                    if (next != null) MoveTo(next.position);
                }
                return;
            }

            if (!_motor.HasDestination) MoveTo(point.position);

            float flatDist = FlatDistance(transform.position, point.position);

            if (flatDist <= pointReachRadius || _motor.HasArrived())
            {
                _waitingAtPoint = true;
                _patrolWaitUntil = Time.time + pointWaitDuration;
                _motor.Stop();
            }
        }

        private void AdvancePatrolIndex()
        {
            if (patrolRoute == null || patrolRoute.Count == 0) return;

            _patrolIndex = patrolRoute.NextIndex(_patrolIndex);
        }

        private void UpdateWander()
        {
            _motor.SetSpeed(patrolSpeed * _wanderSpeedFactor);

            // Дошли — стоим и осматриваемся, а не разворачиваемся на месте.
            if (_waitingAtPoint)
            {
                if (Time.time < _patrolWaitUntil) return;
                _waitingAtPoint = false;
            }

            if (_motor.HasDestination)
            {
                if (!_motor.HasArrived()) return;

                // Пришли: пауза-осмотр случайной длины.
                _motor.Stop();
                _waitingAtPoint = true;
                _patrolWaitUntil = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
                return;
            }

            if (Time.time < _nextWanderAttemptTime) return;

            _nextWanderAttemptTime = Time.time + wanderRetryDelay;

            // Несколько попыток: одиночная выборка часто попадает в стену или в себя.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle * wanderRadius;
                Vector3 candidate = _homePosition + new Vector3(circle.x, 0f, circle.y);

                if (!_motor.SampleReachablePoint(candidate, wanderRadius, out Vector3 point))
                    continue;

                if (FlatDistance(transform.position, point) <= pointReachRadius * 2f)
                    continue;

                // Каждый маршрут — своим темпом: не маршируют строем.
                _wanderSpeedFactor = 1f + Random.Range(-wanderSpeedVariation, wanderSpeedVariation);
                _motor.SetSpeed(patrolSpeed * _wanderSpeedFactor);
                MoveTo(point);
                return;
            }

            // Никуда не вышло — постоять и осмотреться, а не дёргаться.
            _waitingAtPoint = true;
            _patrolWaitUntil = Time.time + Random.Range(wanderWaitMin, wanderWaitMax);
        }

        // =====================================================================
        // Настороженность и поиск
        // =====================================================================

        private void UpdateAlert()
        {
            if (!_hasSuspicion && _awareness <= 0.05f)
            {
                ChangeState(EnemyState.Patrol);
                return;
            }

            _motor.SetSpeed(chaseSpeed * 0.8f);
            RefreshDestination(_suspicionPoint);

            if (_motor.HasArrived())
            {
                _hasSuspicion = false;
                _awareness = Mathf.Min(_awareness, 0.4f);
                _motor.Stop();
                _searchCenter = _suspicionPoint;
                ChangeState(EnemyState.Search);
            }
        }

        private void UpdateSearch()
        {
            _motor.SetSpeed(patrolSpeed);
            _motor.Stop();

            // Сначала всматриваемся в точку проверки (где шумели/мелькали),
            // потом осматриваемся вокруг. Иначе враг крутится, не глядя туда,
            // откуда был звук, и «не замечает» стоящего там игрока.
            if (Time.time < _searchFaceUntil)
            {
                _motor.FaceTowardsAtSpeed(_searchCenter, combatTurnSpeed * 1.5f);
            }
            else
            {
                // Живое сканирование: качания взглядом вокруг исходного
                // направления с паузами по краям, а не вентилятор 90°/с.
                if (!_searchBaseCaptured)
                {
                    _searchBaseCaptured = true;
                    _searchBaseYaw = transform.eulerAngles.y;
                }

                float sweepTime = Time.time - _searchFaceUntil;
                float targetYaw = _searchBaseYaw +
                    Mathf.Sin(sweepTime * 0.7f) * searchSweepAmplitude;
                Quaternion targetRot = Quaternion.Euler(0f, targetYaw, 0f);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRot, searchSweepSpeed * Time.deltaTime);
            }

            if (_awareness <= 0.02f)
                ChangeState(EnemyState.Patrol);
        }

        // =====================================================================
        // Преследование и бой
        // =====================================================================

        private void UpdateChase()
        {
            if (_target == null) { GoSearchLastKnown(); return; }
            if (_targetHealth != null && !_targetHealth.IsAlive) { LoseTarget("мертва"); return; }

            float distance = Vector3.Distance(transform.position, _target.position);

            if (CanSeeCurrentTarget() && distance <= maximumCombatDistance)
            {
                ChangeState(EnemyState.Combat);
                return;
            }

            _motor.SetSpeed(chaseSpeed);
            RefreshDestination(_lastKnownTargetPosition);

            // Уткнулся (стена, забор) и топчется дольше 2.5 сек — хватит давить,
            // идём проверять последнюю точку ногами и дальше по поиску.
            if (_motor.HasArrived())
            {
                if (_chaseStuckTime < 0f) _chaseStuckTime = Time.time;
                if (Time.time - _chaseStuckTime > 2.5f)
                {
                    _chaseStuckTime = -1f;
                    if (debugLogging)
                        Debug.Log($"[EnemyAI] {name}: погоня упёрлась — перехожу к проверке.", this);
                    GoSearchLastKnown();
                }
            }
            else _chaseStuckTime = -1f;
        }

        private void UpdateCombat()
        {
            if (_target == null) { GoSearchLastKnown(); return; }
            if (_targetHealth != null && !_targetHealth.IsAlive) { LoseTarget("мертва"); return; }

            // Оружие могли выдать позже (EnemyLoadout в другом порядке Awake)
            // или снять сценовым оверрайдом — подбираем сами, иначе нечем стрелять.
            if (weapon == null)
            {
                weapon = GetComponentInChildren<HitscanWeapon>(true);
                if (weapon != null)
                {
                    SubscribeWeapon(weapon);
                    if (debugLogging)
                        Debug.Log($"[EnemyAI] {name}: подобрал оружие «{weapon.name}» уже в бою.", this);
                }
            }

            float distance = Vector3.Distance(transform.position, _target.position);
            bool visible = CanSeeCurrentTarget();
            if (visible) _lastVisibleTime = Time.time;

            // Кратковременная помеха (угол, косяк) — держим бой и целемся
            // в последнюю точку, а не срываемся в погоню на каждый рывок луча.
            bool holdingThroughFlicker = !visible &&
                Time.time - _lastVisibleTime <= combatLoseSightGrace;

            if ((!visible && !holdingThroughFlicker) || distance > maximumCombatDistance)
            {
                ChangeState(EnemyState.Chase);
                return;
            }

            // Доводка медленная, человеческая: по стрейфящейся цели мажет.
            _motor.FaceTowardsAtSpeed(holdingThroughFlicker ? _lastKnownTargetPosition : _target.position,
                combatTurnSpeed);

            // Вслепую не стреляем и не перебегаем — стоим и ждём полсекунды.
            if (holdingThroughFlicker)
            {
                _motor.Stop();
                return;
            }

            // Считаем свои попадания по просадке HP цели: по ним идёт темп схватки.
            if (_targetHealth != null)
            {
                if (_targetHealth.CurrentHealth < _lastTargetHp - 0.01f)
                {
                    _engageHits++;
                    if (debugLogging)
                        Debug.Log($"[EnemyAI] {name}: попадание {_engageHits} по «{_target.name}» " +
                            $"(осталось {_targetHealth.CurrentHealth:F0} HP)", this);
                }
                _lastTargetHp = _targetHealth.CurrentHealth;
            }

            bool moving;
            float selfSpeed = _motor.CurrentSpeed;
            if (distance > preferredCombatDistance + combatRepositionDistance)
            {
                // Сближение под огнём — шагом, с оружием наизготовку.
                _motor.SetSpeed(combatAdvanceSpeed);
                RefreshDestination(_target.position);
                moving = true;
            }
            else if (distance < preferredCombatDistance - combatRepositionDistance ||
                     _suppression > 0.6f)
            {
                moving = UpdateCombatReposition();
            }
            else
            {
                _motor.Stop();
                moving = false;
            }

            UpdateFiring(distance, moving ? selfSpeed : 0f);
        }

        /// <summary>
        /// Отход в бою в упор или под подавлением. Точку выбираем редко и идём
        /// к ней до конца; если места нет (комната, угол) — стоим и стреляем,
        /// а не давим спиной в стену.
        /// </summary>
        private bool UpdateCombatReposition()
        {
            if (!_hasRepositionPoint || _motor.HasArrived() ||
                Time.time >= _nextRepositionPickTime)
            {
                if (TryPickRepositionPoint(out Vector3 point))
                {
                    _repositionPoint = point;
                    _hasRepositionPoint = true;
                    _nextRepositionPickTime = Time.time + 2f;
                    // Отход с оружием — медленный, по-человечески.
                    _motor.SetSpeed(repositionSpeed);
                    MoveTo(point);
                    return true;
                }

                _hasRepositionPoint = false;
                _motor.Stop();
                return false;
            }

            if (!_motor.HasDestination)
                MoveTo(_repositionPoint);

            return true;
        }

        /// <summary>
        /// Куда отойти: сначала вбок (не пятиться в стену за спиной), потом назад.
        /// Точки внутри стен отсекает сам мотор (SampleReachablePoint).
        /// </summary>
        private bool TryPickRepositionPoint(out Vector3 result)
        {
            Vector3 away = transform.position - _target.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away.Normalize();

            Vector3[] directions =
            {
                Quaternion.Euler(0f, 70f, 0f) * away,
                Quaternion.Euler(0f, -70f, 0f) * away,
                Quaternion.Euler(0f, 120f, 0f) * away,
                Quaternion.Euler(0f, -120f, 0f) * away,
                away,
            };

            foreach (Vector3 dir in directions)
            {
                Vector3 desired = transform.position + dir * combatRepositionDistance;
                if (!_motor.SampleReachablePoint(desired, 4f, out Vector3 point))
                    continue;
                if (FlatDistance(transform.position, point) < 1f)
                    continue;

                result = point;
                return true;
            }

            result = transform.position;
            return false;
        }

        private void UpdateFiring(float distance, float selfSpeed)
        {
            if (weapon == null || weapon.IsReloading || Time.time < _nextBurstTime)
                return;

            if (Time.time < _canFireAfter)
                return;

            // Дальше дистанции ствола стрелять бессмысленно — пуля не доедет.
            if (distance > weapon.Range) return;

            if (weapon.AmmunitionInMagazine <= 0) { weapon.BeginReload(); return; }

            if (_shotsRemaining <= 0)
            {
                _shotsRemaining = Random.Range(minimumBurstSize, maximumBurstSize + 1);
                _burstShotIndex = 0;
            }

            Vector3 aimPoint = ComputeAimPoint(distance, selfSpeed);

            // Урон стадии схватки с живым разбросом: 5 попаданий ≈ смерть.
            weapon.DamageScale = EngageDamageScale() *
                Random.Range(1f - pacingDamageJitter, 1f + pacingDamageJitter);

            if (weapon.TryFire(aimPoint, gameObject))
            {
                _shotsRemaining--;
                _burstShotIndex++;
                if (_shotsRemaining <= 0)
                    _nextBurstTime = Time.time + Random.Range(minimumBurstPause, maximumBurstPause) *
                        ((_engageHits >= 1 && _engageHits <= 2) ? midFightPauseMult : 1f);
            }
        }

        /// <summary>Множитель урона по номеру попадания (стадии pacingDamageStages).</summary>
        private float EngageDamageScale()
        {
            if (pacingDamageStages == null || pacingDamageStages.Length == 0) return 1f;
            int index = Mathf.Min(_engageHits, pacingDamageStages.Length - 1);
            return Mathf.Max(0.05f, pacingDamageStages[index]);
        }

        /// <summary>
        /// Множитель разброса по фазе схватки: открытие кучное, середина с передышкой,
        /// концовка давит. Выглядит как пристрелка, а не скрипт.
        /// </summary>
        private float EngageSpreadScale()
        {
            if (_engageHits <= 0 && Time.time - _engageStartTime < openingDuration)
                return openingSpread;
            if (_engageHits <= 2)
                return midFightSpread;
            return closingSpread;
        }

        private Vector3 ComputeAimPoint(float distance, float selfSpeed)
        {
            Vector3 basePoint = GetTargetAimPoint(_target);

            float lead = leadAccuracy * 0.15f;
            basePoint += _targetVelocity * lead;

            // Отдача: ствол уводит вверх к концу очереди — первые пули точнее.
            basePoint += Vector3.up * (burstClimb * Mathf.Min(1f, _burstShotIndex / 4f));

            float spread = baseSpread + spreadPerTenMeters * (distance / 10f);
            // На ходу мажет сильнее, чем стоя: штраф пропорционален скорости,
            // а не вкл/выкл — на медленном шаге почти не мажет, на бегу сильно.
            spread *= Mathf.Lerp(1f, movingSpreadMultiplier,
                Mathf.Clamp01(selfSpeed / Mathf.Max(0.1f, chaseSpeed)));
            spread *= Mathf.Lerp(1f, suppressedSpreadMultiplier, _suppression);
            spread *= EngageSpreadScale();

            Vector2 circle = Random.insideUnitCircle * spread;
            Vector3 right = transform.right;
            Vector3 up = Vector3.up;

            return basePoint + right * circle.x + up * circle.y;
        }

        // =====================================================================
        // Отход
        // =====================================================================

        private void BeginRetreat()
        {
            _retreatUntil = Time.time + retreatDuration;
            ChangeState(EnemyState.Retreat);
            SelectCoverOrRetreat();
        }

        private void UpdateRetreat()
        {
            if (Time.time >= _retreatUntil)
            {
                ChangeState(_target != null && CanSeeCurrentTarget()
                    ? EnemyState.Combat : EnemyState.Search);
                return;
            }

            // Драп на полной скорости — только пока не стреляем.
            // Как только ведёт ответный огонь — переходит на медленный отход
            // с оружием: бежать спринт и попадать одновременно человек не может.
            bool firingOnRetreat = _target != null && CanSeeCurrentTarget() &&
                Time.time >= _canFireAfter && weapon != null &&
                Vector3.Distance(transform.position, _target.position) <= weapon.Range;

            _motor.SetSpeed(firingOnRetreat ? repositionSpeed : retreatSpeed);

            if (_motor.HasArrived())
                SelectCoverOrRetreat();

            if (firingOnRetreat)
            {
                _motor.FaceTowardsAtSpeed(_target.position, combatTurnSpeed);

                float distance = Vector3.Distance(transform.position, _target.position);
                weapon.TryFire(ComputeAimPoint(distance, _motor.CurrentSpeed), gameObject);
            }
        }

        private void SelectCoverOrRetreat()
        {
            Vector3 threat = _target != null ? _target.position : _lastKnownTargetPosition;
            Vector3 away = transform.position - threat;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.y = 0f;
            away.Normalize();

            Vector3 bestCover = Vector3.zero;
            bool coverFound = false;

            for (int i = 0; i < 10; i++)
            {
                Vector3 side = Vector3.Cross(Vector3.up, away) *
                               Random.Range(-retreatDistance * 0.6f, retreatDistance * 0.6f);
                Vector3 candidate = transform.position + away * retreatDistance + side;

                if (!_motor.SampleReachablePoint(candidate, 6f, out Vector3 point))
                    continue;

                Vector3 threatEye = threat + Vector3.up * 1.5f;
                Vector3 coverEye = point + Vector3.up * 1.5f;
                bool blocked = Physics.Linecast(threatEye, coverEye, visibilityMask,
                                                 QueryTriggerInteraction.Ignore);

                if (blocked)
                {
                    bestCover = point;
                    coverFound = true;
                    break;
                }

                if (!coverFound) { bestCover = point; coverFound = true; }
            }

            if (coverFound) MoveTo(bestCover);
            else _motor.Stop();
        }

        private void GoSearchLastKnown()
        {
            _motor.SetSpeed(chaseSpeed);
            RefreshDestination(_lastKnownTargetPosition);

            if (_motor.HasArrived())
            {
                ClearTarget();
                _firstSightAcquired = false;
                _awareness = Mathf.Min(_awareness, 0.4f);
                _searchCenter = _lastKnownTargetPosition;
                ChangeState(EnemyState.Search);
            }
        }

        private void LoseTarget(string reason = "потеряна")
        {
            if (debugLogging && _target != null)
                Debug.Log($"[EnemyAI] {name}: цель «{_target.name}» {reason} — стою down, возврат в патруль.", this);
            ClearTarget();
            _firstSightAcquired = false;
            ChangeState(EnemyState.Patrol);
        }

        private void SetTarget(Transform target)
        {
            if (_target != target)
            {
                _prevTargetPosition = target.position;
                _targetVelocity = Vector3.zero;
            }
            _target = target;
            _targetHealth = target.GetComponentInParent<CharacterHealth>();
        }

        private void ClearTarget()
        {
            _target = null;
            _targetHealth = null;
            _targetVelocity = Vector3.zero;
            _prevTargetPosition = Vector3.zero;
        }

        private bool CanSeeCurrentTarget()
        {
            if (_target == null) return false;

            Vector3 chest = GetTargetAimPoint(_target);
            Vector3 toTarget = chest - eyePoint.position;
            if (toTarget.sqrMagnitude > viewDistance * viewDistance) return false;

            // Грудь за низким укрытием, а голова торчит — цель всё равно видна.
            // Вторая точка убирает дёрганье «вижу/не вижу» на каждом кадре.
            bool visible = HasLineOfSight(_target, chest);
            if (!visible)
                visible = HasLineOfSight(_target, chest + Vector3.up * 0.55f);

            if (visible)
            {
                _lastKnownTargetPosition = _target.position;
                _lastTargetSeenTime = Time.time;
                TrackTargetKinematics(_target.position);
            }
            return visible;
        }

        private Vector3 GetTargetAimPoint(Transform target)
        {
            if (target == null) return transform.position;

            var controller = target.GetComponentInParent<CharacterController>();
            if (controller != null) return controller.bounds.center;

            var col = target.GetComponentInChildren<Collider>();
            return col != null ? col.bounds.center : target.position + Vector3.up * 1.4f;
        }

        // =====================================================================
        // Обёртки над мотором
        // =====================================================================

        private void RefreshDestination(Vector3 destination)
        {
            // Если цели ещё нет — ставим её немедленно, не дожидаясь интервала.
            // Иначе состояние сразу видит HasArrived() == true (цели-то нет)
            // и проскакивает дальше, так и не начав движение.
            if (!_motor.HasDestination)
            {
                _nextPathRefreshTime = Time.time + pathRefreshInterval;
                MoveTo(destination);
                return;
            }

            if (Time.time < _nextPathRefreshTime) return;
            _nextPathRefreshTime = Time.time + pathRefreshInterval;
            MoveTo(destination);
        }

        private void MoveTo(Vector3 destination) => _motor.MoveTo(destination);

        private bool TryMoveToNearbyPoint(Vector3 position)
        {
            if (!_motor.SampleReachablePoint(position, 6f, out Vector3 point))
                return false;

            MoveTo(point);
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private bool ShouldRetreat() =>
            _health.NormalizedHealth <= retreatHealthThreshold && _target != null;

        // =====================================================================
        // Состояния
        // =====================================================================

        private void ChangeState(EnemyState newState)
        {
            if (State == EnemyState.Dead || State == newState) return;

            EnemyState previous = State;
            State = newState;

            // Вышли из боя — старая точка отхода недействительна.
            if (newState != EnemyState.Combat)
                _hasRepositionPoint = false;

            // Заход в бой: новая схватка, если цель другая или пауза была долгой.
            // Иначе продолжаем считать попадания (вернулся через секунду — та же драка).
            if (newState == EnemyState.Combat && previous != EnemyState.Combat)
            {
                if (_target == null || _target != _engageTarget ||
                    Time.time - _engageEndTime > 10f)
                {
                    _engageTarget = _target;
                    _engageHits = 0;
                    _engageStartTime = Time.time;
                    _lastTargetHp = _targetHealth != null ? _targetHealth.CurrentHealth : 0f;
                }
            }
            if (previous == EnemyState.Combat && newState != EnemyState.Combat)
                _engageEndTime = Time.time;

            if (debugLogging)
                Debug.Log($"[EnemyAI] {name}: {previous} → {newState} " +
                    $"(awareness={_awareness:F2}, target={(_target != null ? _target.name : "—")})", this);

            bool aiming = newState is EnemyState.Combat or EnemyState.Retreat;
            if (animator != null) animator.SetBool(IsAimingHash, aiming);

            // Разворотом в бою управляем сами, вне боя — пусть его ведёт мотор
            // по направлению движения.
            _motor.SetAutoRotation(!aiming);

            switch (newState)
            {
                case EnemyState.Patrol:
                    _motor.SetSpeed(patrolSpeed);
                    _waitingAtPoint = false;
                    _nextWanderAttemptTime = 0f;
                    if (previous is EnemyState.Search or EnemyState.Chase or EnemyState.Combat)
                        if (voice != null) voice.PlayLostTarget();
                    break;

                case EnemyState.Alert:
                    _motor.SetSpeed(chaseSpeed * 0.8f);
                    if (previous == EnemyState.Patrol && voice != null) voice.PlayAlert();
                    break;

                case EnemyState.Search:
                    _motor.SetSpeed(patrolSpeed);
                    // При входе в поиск всматриваемся в точку проверки подольше.
                    _searchFaceUntil = Time.time + 1.2f;
                    _searchBaseCaptured = false;
                    break;

                case EnemyState.Chase:
                    _motor.SetSpeed(chaseSpeed);
                    _chaseStuckTime = -1f;
                    if (previous is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                        if (voice != null) voice.PlaySpotted();
                    break;

                case EnemyState.Combat:
                    _lastVisibleTime = Time.time;
                    if (previous is EnemyState.Patrol or EnemyState.Alert or EnemyState.Search)
                        if (voice != null) voice.PlaySpotted();
                    break;

                case EnemyState.Retreat:
                    _motor.SetSpeed(retreatSpeed);
                    if (voice != null) voice.PlayRetreat();
                    break;
            }
        }

        // =====================================================================
        // Реакции
        // =====================================================================

        private void OnNoiseCreated(NoiseSystem.Noise noise)
        {
            if (!_health.IsAlive || State == EnemyState.Dead) return;

            float distance = Vector3.Distance(noise.Position, transform.position);
            if (distance > noise.Radius) return;

            if (noise.Source != null)
            {
                // Свои крики и шаги игнорируем полностью.
                if (noise.Source.GetComponentInParent<CharacterHealth>() == _health) return;

                var srcHealth = noise.Source.GetComponentInParent<CharacterHealth>();
                if (srcHealth != null && srcHealth.Team == _health.Team) return;
            }

            float falloff = 1f - Mathf.Clamp01(distance / noise.Radius);
            float heard = falloff * noise.Intensity;
            if (heard < 0.1f) return;

            _awareness = Mathf.Min(0.9f, _awareness + heard * 0.6f);
            _suspicionPoint = noise.Position;
            _searchCenter = noise.Position;
            _hasSuspicion = true;
            _lastNoiseInfo = $"шум {distance:F0}м (громк. {heard:P0})";

            // Громкий близкий звук — мягко глянем в его сторону, остальное
            // доделает Alert движением. Без резкого щелчка корпусом.
            if (falloff > 0.5f)
                _motor.FaceTowardsAtSpeed(noise.Position, 90f);

            if (debugLogging && _target == null)
                Debug.Log($"[EnemyAI] {name}: услышал {_lastNoiseInfo} (aware={_awareness:F2})", this);

            if (_target == null && _awareness > 0.15f &&
                State is EnemyState.Patrol or EnemyState.Search)
            {
                ChangeState(EnemyState.Alert);
            }
        }

        private void OnDamaged(DamageInfo damage)
        {
            _suppression = Mathf.Min(1f, _suppression + (damage.IsSuppression ? 0.5f : 0.35f));

            if (!damage.IsSuppression)
            {
                if (voice != null) voice.PlayPain();
                if (animator != null) animator.SetTrigger(HitHash);
            }

            if (damage.Attacker != null)
            {
                var attackerHealth = damage.Attacker.GetComponentInParent<CharacterHealth>();
                // Труп в цель не берём: иначе попадания от уже мёртвого игрока
                // дёргали бы ИИ между прицелом и патрулём каждый выстрел.
                if (attackerHealth != null && attackerHealth.IsAlive && attackerHealth.Team != enemyTeam)
                {
                    SetTarget(attackerHealth.transform);
                    _lastKnownTargetPosition = attackerHealth.transform.position;
                    _lastTargetSeenTime = Time.time;
                    _awareness = 1f;
                    if (!_firstSightAcquired)
                    {
                        _firstSightAcquired = true;
                        _canFireAfter = Time.time + reactionTime * 0.5f *
                            (jitterReaction ? Random.Range(0.8f, 1.5f) : 1f);
                    }
                }
            }

            if (ShouldRetreat()) BeginRetreat();
            else if (_target != null &&
                     State is not EnemyState.Combat and not EnemyState.Chase)
                ChangeState(EnemyState.Chase);
        }

        private void OnDied(DamageInfo damage)
        {
            State = EnemyState.Dead;

            if (voice != null) voice.PlayDeath();

            // Оружие выпадает из рук (если есть EnemyLoadout), иначе просто глохнет.
            if (loadout != null) loadout.HandleOwnerDeath();
            else if (weapon != null)
            {
                weapon.CancelReload();
                weapon.enabled = false;
            }

            UnsubscribeWeapon(weapon);

            if (animator != null)
            {
                animator.SetBool(IsAimingHash, false);
                animator.SetBool(IsMovingHash, false);
                animator.SetFloat(SpeedHash, 0f);
                animator.SetTrigger(DieHash);
            }

            _motor.Disable();

            foreach (Collider c in GetComponentsInChildren<Collider>())
                c.enabled = false;

            if (corpseLifetime > 0f) Destroy(gameObject, corpseLifetime);

            enabled = false;
        }

        // =====================================================================
        // Анимация и звук движения
        // =====================================================================

        private void UpdateAnimator()
        {
            if (animator == null) return;

            float speed = _motor.CurrentSpeed;
            animator.SetFloat(SpeedHash, speed, 0.15f, Time.deltaTime);
            animator.SetBool(IsMovingHash, speed > 0.2f);
        }

        private void UpdateVoiceFootsteps()
        {
            if (voice == null) return;

            Vector3 velocity = _motor.Velocity;
            velocity.y = 0f;
            voice.UpdateFootsteps(velocity.magnitude, _motor.IsGrounded);
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }
            NoiseSystem.NoiseCreated -= OnNoiseCreated;
            UnsubscribeWeapon(weapon);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform origin = eyePoint != null ? eyePoint : transform;

            Gizmos.color = new Color(1f, 0.85f, 0f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, viewDistance);

            Vector3 fwd = origin.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 left = Quaternion.Euler(0f, -fieldOfView * 0.5f, 0f) * fwd;
            Vector3 right = Quaternion.Euler(0f, fieldOfView * 0.5f, 0f) * fwd;

            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(origin.position, left * viewDistance);
            Gizmos.DrawRay(origin.position, right * viewDistance);

            if (wanderWhenNoRoute && (patrolRoute == null || patrolRoute.Count == 0))
            {
                Vector3 center = Application.isPlaying ? _homePosition : transform.position;
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
                Gizmos.DrawWireSphere(center, wanderRadius);
            }

            if (Application.isPlaying)
            {
                if (_target != null)
                {
                    Gizmos.color = Color.Lerp(Color.green, Color.red, _awareness);
                    Gizmos.DrawLine(origin.position, _target.position);
                    Gizmos.DrawWireSphere(_target.position, 0.3f);
                }
                else
                {
                    // Нет цели — показываем направление обзора
                    Gizmos.color = _awareness > 0.15f ? new Color(1f, 0.5f, 0f) : Color.gray;
                    Gizmos.DrawRay(origin.position, origin.forward * 3f);
                }
            }

            // Показываем targetMask в инспекторе
            if (targetMask == 0)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(transform.position, 0.5f);
            }
        }
#endif
    }
}
