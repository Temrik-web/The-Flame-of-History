using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace FlameOfHistory.AI
{
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(CharacterHealth))]
    [DisallowMultipleComponent]
    public sealed class EnemyAI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform eyePoint;
        [SerializeField] private HitscanWeapon weapon;
        [SerializeField] private PatrolRoute patrolRoute;
        [SerializeField] private Animator animator;

        [Header("Target")]
        [SerializeField] private Team enemyTeam = Team.Allies;
        [SerializeField] private LayerMask targetMask;
        [SerializeField] private LayerMask visibilityMask = ~0;

        [Header("Perception")]
        [SerializeField, Min(1f)] private float viewDistance = 45f;
        [SerializeField, Range(1f, 180f)] private float fieldOfView = 110f;
        [SerializeField, Min(1f)] private float verticalViewTolerance = 6f;
        [SerializeField, Min(0.02f)] private float perceptionInterval = 0.15f;
        [SerializeField, Min(0f)] private float targetMemoryDuration = 8f;

        [Header("Awareness")]
        [SerializeField, Min(0.05f)] private float timeToDetect = 0.6f;
        [SerializeField, Min(0.05f)] private float awarenessDecay = 0.35f;
        [SerializeField, Min(0f)] private float reactionTime = 0.35f;

        [Header("Patrol")]
        [SerializeField, Min(0f)] private float patrolSpeed = 2.2f;
        [SerializeField, Min(0f)] private float pointWaitDuration = 2f;
        [SerializeField, Min(0.1f)] private float pointReachRadius = 0.6f;
        [Tooltip("Если маршрут не задан — враг сам бродит по округе, а не стоит на месте.")]
        [SerializeField] private bool wanderWhenNoRoute = true;
        [SerializeField, Min(1f)] private float wanderRadius = 14f;

        [Header("Combat")]
        [SerializeField, Min(0f)] private float chaseSpeed = 4.2f;
        [SerializeField, Min(1f)] private float preferredCombatDistance = 20f;
        [SerializeField, Min(1f)] private float maximumCombatDistance = 38f;
        [SerializeField, Min(0f)] private float combatRepositionDistance = 5f;
        [SerializeField, Range(1, 20)] private int minimumBurstSize = 2;
        [SerializeField, Range(1, 20)] private int maximumBurstSize = 5;
        [SerializeField, Min(0f)] private float minimumBurstPause = 0.4f;
        [SerializeField, Min(0f)] private float maximumBurstPause = 1.2f;

        [Header("Accuracy (метры разброса по цели)")]
        [SerializeField, Min(0f)] private float baseSpread = 0.25f;
        [SerializeField, Min(0f)] private float spreadPerTenMeters = 0.35f;
        [SerializeField, Min(1f)] private float movingSpreadMultiplier = 2.2f;
        [SerializeField, Min(1f)] private float suppressedSpreadMultiplier = 3f;
        [SerializeField, Range(0f, 1f)] private float leadAccuracy = 0.6f;

        [Header("Retreat / Suppression")]
        [SerializeField, Range(0f, 1f)] private float retreatHealthThreshold = 0.25f;
        [SerializeField, Min(1f)] private float retreatDistance = 18f;
        [SerializeField, Min(0f)] private float retreatSpeed = 4.8f;
        [SerializeField, Min(0f)] private float retreatDuration = 6f;
        [SerializeField, Min(0.1f)] private float suppressionDecay = 0.6f;

        [Header("Navigation")]
        [SerializeField, Min(0.1f)] private float stoppingDistance = 1.2f;
        [SerializeField, Min(0.05f)] private float pathRefreshInterval = 0.25f;

        public EnemyState State { get; private set; }
        public Transform CurrentTarget => _target;
        public float Awareness => _awareness;
        public float Suppression => _suppression;

        private NavMeshAgent _agent;
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
        private Vector3 _suspicionPoint;
        private bool _hasSuspicion;

        private float _nextPerceptionTime;
        private float _nextPathRefreshTime;
        private float _patrolWaitUntil;
        private bool _waitingAtPoint;
        private float _retreatUntil;

        private int _patrolIndex;
        private Vector3 _homePosition;
        private int _shotsRemaining;
        private float _nextBurstTime;

        private readonly Collider[] _targetBuffer = new Collider[128];
        private readonly HashSet<CharacterHealth> _candidateSet = new();

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsAimingHash = Animator.StringToHash("IsAiming");
        private static readonly int DieHash = Animator.StringToHash("Die");

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<CharacterHealth>();
            _agent.stoppingDistance = stoppingDistance;
            _agent.updateRotation = true;
            if (eyePoint == null) eyePoint = transform;
        }

        private void OnEnable()
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
            NoiseSystem.NoiseCreated += OnNoiseCreated;
            State = EnemyState.Patrol;
        }

        private void Start()
        {
            _homePosition = transform.position;
            ChangeState(EnemyState.Patrol);
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

        /// <summary>Публичный вход подавления — вызывается SuppressionReceiver при близком пролёте.</summary>
        public void ApplySuppression(float amount)
        {
            if (_health == null || !_health.IsAlive) return;
            _suppression = Mathf.Clamp01(_suppression + amount);
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
                        _canFireAfter = Time.time + reactionTime;
                    }

                    ChangeState(visDistance <= maximumCombatDistance
                        ? EnemyState.Combat
                        : EnemyState.Chase);
                }
                else if (State == EnemyState.Patrol)
                {
                    _suspicionPoint = visible.position;
                    _hasSuspicion = true;
                    ChangeState(EnemyState.Alert);
                }
                return;
            }

            if (_target != null && Time.time - _lastTargetSeenTime > targetMemoryDuration)
            {
                ClearTarget();
                _firstSightAcquired = false;
            }

            if (_target == null && _hasSuspicion && _awareness > 0.15f &&
                State != EnemyState.Retreat && State != EnemyState.Search)
            {
                ChangeState(EnemyState.Alert);
            }
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
            Transform best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider col = _targetBuffer[i];
                if (col == null) continue;

                CharacterHealth cand = col.GetComponentInParent<CharacterHealth>();
                if (cand == null || !_candidateSet.Add(cand) ||
                    !cand.IsAlive || cand.Team != enemyTeam)
                    continue;

                Vector3 aimPoint = GetTargetAimPoint(cand.transform);
                Vector3 toTarget = aimPoint - eyePoint.position;
                float distance = toTarget.magnitude;
                if (distance <= 0.01f || distance > viewDistance) continue;

                if (Mathf.Abs(aimPoint.y - eyePoint.position.y) > verticalViewTolerance)
                    continue;

                Vector3 flatFwd = eyePoint.forward; flatFwd.y = 0f;
                Vector3 flatDir = toTarget; flatDir.y = 0f;
                if (flatDir.sqrMagnitude < 0.0001f) continue;

                float angle = Vector3.Angle(flatFwd, flatDir);
                if (angle > fieldOfView * 0.5f) continue;

                if (!HasLineOfSight(cand.transform, aimPoint)) continue;

                float score = distance + angle * 0.1f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = cand.transform;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private bool HasLineOfSight(Transform target, Vector3 targetPoint)
        {
            Vector3 origin = eyePoint.position;
            Vector3 dir = targetPoint - origin;
            float dist = dir.magnitude;

            if (!Physics.Raycast(origin, dir.normalized, out RaycastHit hit,
                    dist + 0.2f, visibilityMask, QueryTriggerInteraction.Ignore))
                return true;

            return hit.transform == target || hit.transform.IsChildOf(target);
        }

        private void UpdatePatrol()
        {
            if (patrolRoute == null || patrolRoute.Count == 0)
            {
                if (wanderWhenNoRoute) UpdateWander();
                else StopMoving();
                return;
            }

            Transform point = patrolRoute.GetPoint(_patrolIndex);
            if (point == null) return;

            _agent.speed = patrolSpeed;

            if (_waitingAtPoint)
            {
                if (Time.time >= _patrolWaitUntil)
                {
                    _waitingAtPoint = false;
                    AdvancePatrolIndex();
                    MoveTo(patrolRoute.GetPoint(_patrolIndex).position);
                }
                return;
            }

            if (!_agent.pathPending && !_agent.hasPath)
                MoveTo(point.position);

            float flatDist = Vector3.Distance(
                new Vector3(transform.position.x, 0f, transform.position.z),
                new Vector3(point.position.x, 0f, point.position.z));

            if (!_agent.pathPending && flatDist <= pointReachRadius)
            {
                _waitingAtPoint = true;
                _patrolWaitUntil = Time.time + pointWaitDuration;
                StopMoving();
            }
        }

        private void AdvancePatrolIndex()
        {
            _patrolIndex = (_patrolIndex + 1) % patrolRoute.Count;
        }

        private void UpdateWander()
        {
            _agent.speed = patrolSpeed;

            // Если агент достиг точки или остановлен – выбираем новую случайную точку
            if (!_agent.pathPending && (!_agent.hasPath || HasReachedDestination()))
            {
                Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
                randomDirection += _homePosition;

                if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
                {
                    // Игнорируем слишком близкие точки, чтобы избежать "дрожания"
                    if (Vector3.Distance(transform.position, hit.position) > pointReachRadius)
                    {
                        MoveTo(hit.position);
                    }
                }
            }
        }

        private void UpdateAlert()
        {
            if (!_hasSuspicion && _awareness <= 0.05f)
            {
                ChangeState(EnemyState.Patrol);
                return;
            }

            _agent.speed = chaseSpeed * 0.8f;
            RefreshDestination(_suspicionPoint);

            if (HasReachedDestination())
            {
                _hasSuspicion = false;
                _awareness = Mathf.Min(_awareness, 0.4f);
                StopMoving();
                ChangeState(EnemyState.Search);
            }
        }

        private void UpdateSearch()
        {
            _agent.speed = patrolSpeed;
            StopMoving();
            transform.Rotate(0f, 90f * Time.deltaTime, 0f);

            if (_awareness <= 0.02f)
                ChangeState(EnemyState.Patrol);
        }

        private void UpdateChase()
        {
            if (_target == null) { GoSearchLastKnown(); return; }
            if (_targetHealth != null && !_targetHealth.IsAlive) { LoseTarget(); return; }

            float distance = Vector3.Distance(transform.position, _target.position);

            if (CanSeeCurrentTarget() && distance <= maximumCombatDistance)
            {
                ChangeState(EnemyState.Combat);
                return;
            }

            _agent.speed = chaseSpeed;
            RefreshDestination(_lastKnownTargetPosition);
        }

        private void UpdateCombat()
        {
            if (_target == null) { GoSearchLastKnown(); return; }
            if (_targetHealth != null && !_targetHealth.IsAlive) { LoseTarget(); return; }

            float distance = Vector3.Distance(transform.position, _target.position);
            bool visible = CanSeeCurrentTarget();

            if (!visible || distance > maximumCombatDistance)
            {
                ChangeState(EnemyState.Chase);
                return;
            }

            FaceTarget(_target.position);

            bool moving;
            if (distance > preferredCombatDistance + combatRepositionDistance)
            {
                _agent.speed = chaseSpeed;
                RefreshDestination(_target.position);
                moving = true;
            }
            else if (distance < preferredCombatDistance - combatRepositionDistance ||
                     _suppression > 0.6f)
            {
                Vector3 away = (transform.position - _target.position).normalized;
                moving = TryMoveToNearbyNavMeshPoint(transform.position + away * combatRepositionDistance);
            }
            else
            {
                StopMoving();
                moving = false;
            }

            UpdateFiring(distance, moving);
        }

        private void UpdateFiring(float distance, bool selfMoving)
        {
            if (weapon == null || weapon.IsReloading || Time.time < _nextBurstTime)
                return;

            if (Time.time < _canFireAfter)
                return;

            if (weapon.AmmunitionInMagazine <= 0) { weapon.BeginReload(); return; }

            if (_shotsRemaining <= 0)
                _shotsRemaining = Random.Range(minimumBurstSize, maximumBurstSize + 1);

            Vector3 aimPoint = ComputeAimPoint(distance, selfMoving);

            if (weapon.TryFire(aimPoint, gameObject))
            {
                _shotsRemaining--;
                if (_shotsRemaining <= 0)
                    _nextBurstTime = Time.time + Random.Range(minimumBurstPause, maximumBurstPause);
            }
        }

        private Vector3 ComputeAimPoint(float distance, bool selfMoving)
        {
            Vector3 basePoint = GetTargetAimPoint(_target);

            float lead = leadAccuracy * 0.15f;
            basePoint += _targetVelocity * lead;

            float spread = baseSpread + spreadPerTenMeters * (distance / 10f);
            if (selfMoving) spread *= movingSpreadMultiplier;
            spread *= Mathf.Lerp(1f, suppressedSpreadMultiplier, _suppression);

            Vector2 circle = Random.insideUnitCircle * spread;
            Vector3 right = transform.right;
            Vector3 up = Vector3.up;

            return basePoint + right * circle.x + up * circle.y;
        }

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

            _agent.speed = retreatSpeed;

            if (HasReachedDestination())
                SelectCoverOrRetreat();

            if (_target != null && CanSeeCurrentTarget() && Time.time >= _canFireAfter)
            {
                FaceTarget(_target.position);
                if (weapon != null)
                {
                    Vector3 aim = ComputeAimPoint(
                        Vector3.Distance(transform.position, _target.position), true);
                    weapon.TryFire(aim, gameObject);
                }
            }
        }

        private void SelectCoverOrRetreat()
        {
            Vector3 threat = _target != null ? _target.position : _lastKnownTargetPosition;
            Vector3 away = transform.position - threat;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();

            Vector3 bestCover = Vector3.zero;
            bool coverFound = false;

            for (int i = 0; i < 10; i++)
            {
                Vector3 side = Vector3.Cross(Vector3.up, away) *
                               Random.Range(-retreatDistance * 0.6f, retreatDistance * 0.6f);
                Vector3 candidate = transform.position + away * retreatDistance + side;

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit navHit, 6f, NavMesh.AllAreas))
                    continue;

                Vector3 threatEye = threat + Vector3.up * 1.5f;
                Vector3 coverEye = navHit.position + Vector3.up * 1.5f;
                bool blocked = Physics.Linecast(threatEye, coverEye, visibilityMask,
                                                 QueryTriggerInteraction.Ignore);

                if (blocked)
                {
                    bestCover = navHit.position;
                    coverFound = true;
                    break;
                }

                if (!coverFound) { bestCover = navHit.position; coverFound = true; }
            }

            if (coverFound) MoveTo(bestCover);
            else StopMoving();
        }

        private void GoSearchLastKnown()
        {
            _agent.speed = chaseSpeed;
            RefreshDestination(_lastKnownTargetPosition);

            if (HasReachedDestination())
            {
                ClearTarget();
                _firstSightAcquired = false;
                _awareness = Mathf.Min(_awareness, 0.4f);
                ChangeState(EnemyState.Search);
            }
        }

        private void LoseTarget()
        {
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

            Vector3 point = GetTargetAimPoint(_target);
            Vector3 toTarget = point - eyePoint.position;
            if (toTarget.sqrMagnitude > viewDistance * viewDistance) return false;

            bool visible = HasLineOfSight(_target, point);
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

        private void RefreshDestination(Vector3 destination)
        {
            if (Time.time < _nextPathRefreshTime) return;
            _nextPathRefreshTime = Time.time + pathRefreshInterval;
            MoveTo(destination);
        }

        private void MoveTo(Vector3 destination)
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;
            _agent.isStopped = false;
            _agent.SetDestination(destination);
        }

        private bool TryMoveToNearbyNavMeshPoint(Vector3 position)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
                return false;
            MoveTo(hit.position);
            return true;
        }

        private void StopMoving()
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;
            _agent.isStopped = true;
            _agent.ResetPath();
        }

        private bool HasReachedDestination()
        {
            if (!_agent.enabled || !_agent.isOnNavMesh || _agent.pathPending)
                return false;

            if (_agent.remainingDistance > _agent.stoppingDistance + 0.15f)
                return false;

            return !_agent.hasPath || _agent.velocity.sqrMagnitude < 0.05f;
        }

        private void FaceTarget(Vector3 targetPosition)
        {
            Vector3 dir = targetPosition - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            Quaternion targetRot = Quaternion.LookRotation(dir);
            float turnSpeed = Mathf.Lerp(10f, 5f, _suppression);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, turnSpeed * Time.deltaTime);
        }

        private bool ShouldRetreat() =>
            _health.NormalizedHealth <= retreatHealthThreshold && _target != null;

        private void ChangeState(EnemyState newState)
        {
            if (State == EnemyState.Dead || State == newState) return;
            State = newState;

            bool aiming = newState is EnemyState.Combat or EnemyState.Retreat;
            if (animator != null) animator.SetBool(IsAimingHash, aiming);

            switch (newState)
            {
                case EnemyState.Patrol: _agent.speed = patrolSpeed; break;
                case EnemyState.Alert:
                case EnemyState.Chase: _agent.speed = chaseSpeed; break;
                case EnemyState.Retreat: _agent.speed = retreatSpeed; break;
            }
        }

        private void OnNoiseCreated(NoiseSystem.Noise noise)
        {
            if (!_health.IsAlive || State == EnemyState.Dead) return;

            float distance = Vector3.Distance(noise.Position, transform.position);
            if (distance > noise.Radius) return;

            if (noise.Source != null)
            {
                var srcHealth = noise.Source.GetComponentInParent<CharacterHealth>();
                if (srcHealth != null && srcHealth.Team == _health.Team) return;
            }

            float falloff = 1f - Mathf.Clamp01(distance / noise.Radius);
            float heard = falloff * noise.Intensity;
            if (heard < 0.1f) return;

            _awareness = Mathf.Min(0.9f, _awareness + heard * 0.6f);
            _suspicionPoint = noise.Position;
            _hasSuspicion = true;

            if (_target == null && _awareness > 0.15f &&
                State is EnemyState.Patrol or EnemyState.Search)
            {
                ChangeState(EnemyState.Alert);
            }
        }

        private void OnDamaged(DamageInfo damage)
        {
            _suppression = Mathf.Min(1f, _suppression + (damage.IsSuppression ? 0.5f : 0.35f));

            if (damage.Attacker != null)
            {
                var attackerHealth = damage.Attacker.GetComponentInParent<CharacterHealth>();
                if (attackerHealth != null && attackerHealth.Team == enemyTeam)
                {
                    SetTarget(attackerHealth.transform);
                    _lastKnownTargetPosition = attackerHealth.transform.position;
                    _lastTargetSeenTime = Time.time;
                    _awareness = 1f;
                    if (!_firstSightAcquired)
                    {
                        _firstSightAcquired = true;
                        _canFireAfter = Time.time + reactionTime * 0.5f;
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
            StopMoving();

            if (weapon != null) { weapon.CancelReload(); weapon.enabled = false; }

            if (animator != null)
            {
                animator.SetBool(IsAimingHash, false);
                animator.SetTrigger(DieHash);
            }

            _agent.enabled = false;

            foreach (Collider c in GetComponentsInChildren<Collider>())
                c.enabled = false;

            enabled = false;
        }

        private void UpdateAnimator()
        {
            if (animator == null) return;
            float speed = _agent.enabled ? _agent.velocity.magnitude : 0f;
            animator.SetFloat(SpeedHash, speed, 0.15f, Time.deltaTime);
        }

        private void OnDisable()
        {
            if (_health != null)
            {
                _health.Damaged -= OnDamaged;
                _health.Died -= OnDied;
            }
            NoiseSystem.NoiseCreated -= OnNoiseCreated;
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

            if (Application.isPlaying && _target != null)
            {
                Gizmos.color = Color.Lerp(Color.green, Color.red, _awareness);
                Gizmos.DrawLine(origin.position, _target.position);
            }
        }
#endif
    }
}