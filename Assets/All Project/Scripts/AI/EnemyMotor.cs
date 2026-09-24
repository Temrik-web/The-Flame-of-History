using UnityEngine;
using UnityEngine.AI;

namespace FlameOfHistory.AI
{
    /// <summary>Ходьба врага: NavMesh, а без него — ручной Fallback по коллайдерам. Режим выбирается сам.</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class EnemyMotor : MonoBehaviour
    {
        public enum MotorMode { NavMesh, Fallback }

        [Header("Общее")]
        [Tooltip("Радиус, в котором цель считается достигнутой.")]
        [SerializeField, Min(0.05f)] private float arriveRadius = 1.2f;
        [Tooltip("Скорость разворота корпуса, град/сек. Это максимум: доводка " +
                 "замедляется к концу поворота, как у человека.")]
        [SerializeField, Min(1f)] private float turnSpeed = 180f;

        [Header("Режим без NavMesh")]
        [Tooltip("Разрешить ходьбу без запечённого NavMesh. Выключи, если ходьба " +
                 "должна быть строго по навмешу.")]
        [SerializeField] private bool allowFallbackMovement = true;
        [Tooltip("Как часто проверять, не появился ли NavMesh под ногами, сек.")]
        [SerializeField, Min(0.1f)] private float navMeshRecheckInterval = 1f;
        [Tooltip("Радиус поиска NavMesh вокруг врага при попытке вернуться на него.")]
        [SerializeField, Min(0.5f)] private float navMeshSnapRadius = 3f;

        [Header("Земля и гравитация (режим Fallback)")]
        [Tooltip("Слои, которые считаются землёй.")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("Максимальная высота ступеньки, на которую враг может забраться.")]
        [SerializeField, Min(0f)] private float stepHeight = 0.45f;
        [Tooltip("Насколько далеко вниз искать землю, прежде чем считать, что враг падает.")]
        [SerializeField, Min(0.1f)] private float groundProbeDistance = 2.5f;
        [Tooltip("Максимальный уклон поверхности, по которой можно идти, град.")]
        [SerializeField, Range(1f, 89f)] private float maximumSlope = 50f;
        [Tooltip("Максимальный перепад высоты на шаг вперёд (1.4 м), м. Неровный " +
                 "рельеф: холмы и овраги требуют 1.5–2.5, иначе враг отказывается " +
                 "идти по склону и топчется.")]
        [SerializeField, Min(0.2f)] private float maximumStepDown = 1.5f;
        [SerializeField, Min(0f)] private float gravity = 22f;
        [Tooltip("Отступ центра капсулы от земли. Обычно половина высоты врага.")]
        [SerializeField, Min(0f)] private float groundOffset = 0f;

        [Header("Humanity (плавность без NavMesh)")]
        [Tooltip("Разгон в режиме Fallback, м/с². Без него враг стартует и " +
                 "меняет направление мгновенно, как робот.")]
        [SerializeField, Min(0.5f)] private float fallbackAcceleration = 6f;
        [Tooltip("Торможение в режиме Fallback, м/с².")]
        [SerializeField, Min(0.5f)] private float fallbackDeceleration = 12f;
        [Tooltip("Скорость смены направления в режиме Fallback, град/сек. " +
                 "На резких поворотах враг ещё и притормаживает.")]
        [SerializeField, Min(30f)] private float fallbackTurnRate = 270f;

        [Header("Объезд препятствий (режим Fallback)")]
        [Tooltip("Слои, которые считаются препятствиями на пути.")]
        [SerializeField] private LayerMask obstacleMask = ~0;
        [Tooltip("Радиус тела для проверки препятствий.")]
        [SerializeField, Min(0.05f)] private float bodyRadius = 0.4f;
        [Tooltip("На какое расстояние вперёд смотреть в поисках препятствия.")]
        [SerializeField, Min(0.2f)] private float lookAheadDistance = 1.4f;
        [Tooltip("Углы объезда, которые перебираются по очереди (град).")]
        [SerializeField] private float[] avoidanceAngles = { 30f, -30f, 60f, -60f, 90f, -90f };
        [Tooltip("Не шагать туда, где под ногами нет земли (обрывы, ямы).")]
        [SerializeField] private bool avoidLedges = true;

        /// <summary>Текущий режим передвижения.</summary>
        public MotorMode Mode { get; private set; } = MotorMode.NavMesh;

        /// <summary>Есть ли активный пункт назначения.</summary>
        public bool HasDestination { get; private set; }

        /// <summary>Текущая цель движения (валидна при HasDestination).</summary>
        public Vector3 Destination => _destination;

        /// <summary>Фактическая скорость перемещения в м/с (без вертикали).</summary>
        public float CurrentSpeed => _measuredVelocity.magnitude;

        /// <summary>Фактическая скорость с вертикалью — для анимаций.</summary>
        public Vector3 Velocity => _measuredVelocity;

        /// <summary>Стоит ли враг на земле (в режиме NavMesh всегда true).</summary>
        public bool IsGrounded => Mode == MotorMode.NavMesh || _isGrounded;

        /// <summary>Готов ли мотор двигать врага хоть каким-то способом.</summary>
        public bool CanMove =>
            (Mode == MotorMode.NavMesh && AgentUsable) ||
            (Mode == MotorMode.Fallback && allowFallbackMovement);

        private NavMeshAgent _agent;
        private CharacterController _controller;

        private Vector3 _destination;
        private float _destinationSetTime = float.NegativeInfinity;
        private float _desiredSpeed = 2f;
        private float _verticalVelocity;
        private bool _isGrounded = true;
        private bool _blockedCompletely;
        private bool _autoRotation = true;

        private Vector3 _previousPosition;
        private Vector3 _measuredVelocity;
        private float _fallbackSpeed;
        private Vector3 _fallbackMoveDir;
        // Топчется с активной целью — считаем упёршимся, HasArrived вернёт true.
        private Vector3 _lastProgressPos;
        private float _lastProgressTime;
        private float _nextNavMeshCheckTime;
        private static bool s_fallbackNoticeShown;
        private static bool s_fallbackDisabledNoticeShown;
        private readonly Collider[] _obstacleCheckBuffer = new Collider[8];
        // Задние углы — последний шанс выйти из тупика разворотом.
        private static readonly float[] RearAvoidanceAngles = { 135f, -135f, 180f };

        private bool AgentUsable => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _controller = GetComponent<CharacterController>();
            _previousPosition = transform.position;

            if (groundOffset <= 0f) groundOffset = MeasureGroundOffset();

            _agent.stoppingDistance = Mathf.Min(_agent.stoppingDistance, arriveRadius);
            _autoRotation = _agent.updateRotation;
        }

        /// <summary>Высота центра над землёй: у капсулы position — центр, а не ступни.</summary>
        private float MeasureGroundOffset()
        {
            if (_controller != null)
                return _controller.height * 0.5f - _controller.center.y + _controller.skinWidth;
            float best = 0f;

            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger) continue;

                float offset = transform.position.y - collider.bounds.min.y;
                if (offset > best) best = offset;
            }

            return best > 0.01f ? best : 1f; // 1f — половина стандартной капсулы
        }

        private void Start() => EvaluateMode(true);

        private void OnEnable()
        {
            _previousPosition = transform.position;
            _measuredVelocity = Vector3.zero;
            _lastProgressPos = transform.position;
            _lastProgressTime = Time.time;
        }

        // Публичный API для EnemyAI.
        /// <summary>Задать скорость передвижения.</summary>
        public void SetSpeed(float speed)
        {
            _desiredSpeed = Mathf.Max(0f, speed);
            if (AgentUsable) _agent.speed = _desiredSpeed;
        }

        /// <summary>Идти к точке. Возвращает false, если путь построить не удалось.</summary>
        public bool MoveTo(Vector3 target)
        {
            _destination = target;
            HasDestination = true;
            _blockedCompletely = false;
            _destinationSetTime = Time.time;

            if (Mode == MotorMode.NavMesh)
            {
                if (!AgentUsable && !TryReturnToNavMesh())
                {
                    EvaluateMode(false);
                    return Mode == MotorMode.Fallback;
                }

                _agent.isStopped = false;
                _agent.speed = _desiredSpeed;
                return _agent.SetDestination(target);
            }

            return allowFallbackMovement;
        }

        public void Stop()
        {
            _blockedCompletely = false;
            // Search дёргает Stop каждый кадр — без проверки ResetPath молотил бы впустую.
            bool agentBusy = AgentUsable && (_agent.hasPath || _agent.pathPending || !_agent.isStopped);
            if (!HasDestination && !agentBusy) return;

            HasDestination = false;

            if (AgentUsable)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        public bool HasArrived()
        {
            if (!HasDestination) return true;
            // Путь строится асинхронно ~0.15с: без паузы ИИ решил бы, что уже пришёл.
            if (Time.time - _destinationSetTime < 0.15f) return false;
            if (Mode == MotorMode.NavMesh && AgentUsable)
            {
                if (_agent.pathPending) return false;
                // Непостроимый путь считаем прибытием, иначе ИИ зависнет навсегда.
                if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;
                if (!_agent.hasPath) return true;

                if (_agent.remainingDistance > Mathf.Max(_agent.stoppingDistance, arriveRadius) + 0.15f)
                    return false;

                return _agent.velocity.sqrMagnitude < 0.05f;
            }

            if (_blockedCompletely) return true;
            return FlatDistance(transform.position, _destination) <= arriveRadius;
        }
        public bool SampleReachablePoint(Vector3 desired, float searchRadius, out Vector3 result)
        {
            if (Mode == MotorMode.NavMesh && AgentUsable)
            {
                if (NavMesh.SamplePosition(desired, out NavMeshHit navHit, searchRadius, NavMesh.AllAreas))
                {
                    result = navHit.position;
                    return true;
                }

                result = desired;
                return false;
            }

            // Луч от уровня ног, иначе на рельефе уйдёт в воздух.
            Vector3 probe = new(desired.x, transform.position.y - groundOffset + stepHeight + 1f, desired.z);
            if (TryFindGround(probe, out Vector3 grounded))
            {
                result = grounded + Vector3.up * groundOffset;
                // Точку внутри стены не выдаём — CharacterController на тонких стенах проталкивает насквозь.
                if (IsPointInsideObstacle(result)) { result = desired; return false; }

                return true;
            }

            result = desired;
            return false;
        }

        private bool IsPointInsideObstacle(Vector3 point)
        {
            int count = Physics.OverlapSphereNonAlloc(point, bodyRadius * 0.9f,
                _obstacleCheckBuffer, obstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = _obstacleCheckBuffer[i];
                if (collider == null || collider.isTrigger) continue;
                if (collider.transform.root == transform.root) continue;
                // Ниже ступеньки — переступим, это не стена.
                float footY = point.y - groundOffset;
                if (collider.bounds.max.y - footY <= stepHeight) continue;

                return true;
            }

            return false;
        }

        public void FaceTowards(Vector3 worldPoint, float turnSpeedMultiplier = 1f)
        {
            FaceTowardsAtSpeed(worldPoint, turnSpeed * Mathf.Max(0.05f, turnSpeedMultiplier));
        }
        /// <summary>Доводка с затуханием к концу — без роботизированного щелчка.</summary>
        public void FaceTowardsAtSpeed(Vector3 worldPoint, float degreesPerSecond)
        {
            Vector3 direction = worldPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target,
                EaseTurnSpeed(transform.rotation, target, degreesPerSecond) * Time.deltaTime);
        }

        /// <summary>
        /// Затухание доводки: чем меньше осталось довернуть, тем медленнее.
        /// Убирает роботизированный «щелчок» в конце поворота.
        /// </summary>
        public static float EaseTurnSpeed(Quaternion current, Quaternion target, float maxSpeed)
        {
            float angle = Quaternion.Angle(current, target);
            return Mathf.Max(1f, maxSpeed) * Mathf.Clamp01(angle / 40f + 0.12f);
        }

        /// <summary>Кто управляет поворотом: агент или сам ИИ.</summary>
        public void SetAutoRotation(bool enabled)
        {
            _autoRotation = enabled;
            if (_agent != null) _agent.updateRotation = enabled;
        }

        /// <summary>Полностью отключить мотор (смерть врага).</summary>
        public void Disable()
        {
            Stop();
            HasDestination = false;
            _measuredVelocity = Vector3.zero;

            if (_agent != null) _agent.enabled = false;
            enabled = false;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (Time.time >= _nextNavMeshCheckTime)
            {
                _nextNavMeshCheckTime = Time.time + navMeshRecheckInterval;
                EvaluateMode(false);
            }

            if (Mode == MotorMode.Fallback && allowFallbackMovement)
                UpdateFallbackMovement(dt);

            Vector3 position = transform.position;
            _measuredVelocity = (position - _previousPosition) / dt;
            _previousPosition = position;
        }

        private void EvaluateMode(bool initial)
        {
            if (_agent == null) return;

            if (_agent.enabled && _agent.isOnNavMesh)
            {
                if (Mode != MotorMode.NavMesh)
                {
                    Mode = MotorMode.NavMesh;
                    _verticalVelocity = 0f;

                    // NavMeshAgent сам двигает трансформ — CharacterController мешает.
                    if (_controller != null && _controller.enabled)
                        _controller.enabled = false;

                    if (HasDestination) MoveTo(_destination);
                }
                return;
            }

            // Агент выключен или свалился с навмеша — пробуем вернуть.
            if (TryReturnToNavMesh())
            {
                Mode = MotorMode.NavMesh;
                if (_controller != null && _controller.enabled)
                    _controller.enabled = false;
                if (HasDestination) MoveTo(_destination);
                return;
            }

            if (Mode != MotorMode.Fallback)
            {
                Mode = MotorMode.Fallback;
                if (_agent.enabled) _agent.enabled = false;

                // Включаем CharacterController для fallback-движения с физикой.
                if (_controller != null && !_controller.enabled)
                    _controller.enabled = true;

                if (allowFallbackMovement && !s_fallbackNoticeShown)
                {
                    s_fallbackNoticeShown = true;
                    Debug.Log("[EnemyMotor]: NavMesh недоступен — враги ходят в режиме " +
                              "Fallback (по коллайдерам земли). Запеки NavMesh, чтобы " +
                              "включилась полноценная навигация.");
                }
                else if (!allowFallbackMovement && !s_fallbackDisabledNoticeShown)
                {
                    s_fallbackDisabledNoticeShown = true;
                    Debug.LogWarning("[EnemyMotor]: NavMesh не найден, а Fallback выключен — " +
                                     "враги не будут двигаться.");
                }
            }

            if (initial) _previousPosition = transform.position;
        }

        private bool TryReturnToNavMesh()
        {
            if (_agent == null) return false;

            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit,
                    navMeshSnapRadius, NavMesh.AllAreas))
                return false;

            // Warp без проверки тащит сквозь стену, если навмеш за ней.
            Vector3 from = transform.position + Vector3.up * 1f;
            Vector3 to = hit.position + Vector3.up * 1f;
            if (Physics.Linecast(from, to, obstacleMask, QueryTriggerInteraction.Ignore))
                return false;

            bool wasEnabled = _agent.enabled;
            if (!wasEnabled) _agent.enabled = true;
            if (!_agent.Warp(hit.position))
            {
                if (!wasEnabled) _agent.enabled = false;
                return false;
            }

            _agent.speed = _desiredSpeed;
            _verticalVelocity = 0f;
            return _agent.isOnNavMesh;
        }

        private void UpdateFallbackMovement(float dt)
        {
            Vector3 position = transform.position;
            // position — центр тела, землю считаем от ног, иначе «висит» в воздухе.
            float footY = position.y - groundOffset;
            Vector3 horizontal = Vector3.zero;
            float desiredSpeed = 0f;
            Vector3 desiredDirection = Vector3.zero;
            bool hasDirection = false;

            if (HasDestination && _desiredSpeed > 0.01f)
            {
                Vector3 toTarget = _destination - position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;

                if (distance <= arriveRadius)
                {
                    HasDestination = false;
                }
                else
                {
                    Vector3 straight = toTarget / distance;

                    if (TryResolveDirection(position, footY, straight, out Vector3 clearDirection))
                    {
                        _blockedCompletely = false;
                        desiredSpeed = Mathf.Min(_desiredSpeed, distance / Mathf.Max(dt, 0.0001f));
                        desiredDirection = clearDirection;
                        hasDirection = true;

                        // В бою разворотом рулит ИИ, тут не мешаем.
                        if (_autoRotation)
                        {
                            Quaternion target = Quaternion.LookRotation(clearDirection, Vector3.up);
                            transform.rotation = Quaternion.RotateTowards(
                                transform.rotation, target,
                                EaseTurnSpeed(transform.rotation, target, turnSpeed) * dt);
                        }
                    }
                    else
                    {
                        _blockedCompletely = true;
                    }
                }
            }

            if (hasDirection)
            {
                if (_fallbackMoveDir.sqrMagnitude < 0.001f)
                    _fallbackMoveDir = desiredDirection;
                else
                    _fallbackMoveDir = Vector3.RotateTowards(_fallbackMoveDir, desiredDirection,
                        fallbackTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;

                float alignment = Vector3.Dot(_fallbackMoveDir, desiredDirection);
                float cappedSpeed = alignment < 0.5f ? desiredSpeed * 0.55f : desiredSpeed;
                _fallbackSpeed = Mathf.MoveTowards(_fallbackSpeed, cappedSpeed,
                    fallbackAcceleration * dt);

                horizontal = _fallbackMoveDir * _fallbackSpeed;
            }
            else
            {
                _fallbackSpeed = Mathf.MoveTowards(_fallbackSpeed, 0f,
                    fallbackDeceleration * dt);
                horizontal = _fallbackMoveDir * _fallbackSpeed;
            }

            // Буксует на месте дольше секунды — считаем упёршимся, ИИ выберет новую точку.
            if (HasDestination && _desiredSpeed > 0.01f && !_blockedCompletely)
            {
                if (Time.time - _lastProgressTime >= 1f)
                {
                    if (FlatDistance(position, _lastProgressPos) < 0.25f)
                        _blockedCompletely = true;
                    _lastProgressPos = position;
                    _lastProgressTime = Time.time;
                }
            }
            else
            {
                _lastProgressPos = position;
                _lastProgressTime = Time.time;
            }

            // --- вертикаль: прижатие к земле или падение ---
            Vector3 probeStart = new(position.x, footY + stepHeight + 0.1f, position.z);
            bool groundFound = TryFindGround(probeStart, out Vector3 groundPoint);

            _isGrounded = groundFound && footY - groundPoint.y <= stepHeight + 0.05f;

            Vector3 motion = horizontal * dt;

            if (_isGrounded)
            {
                _verticalVelocity = 0f;
                float targetY = groundPoint.y + groundOffset;
                motion.y = Mathf.Lerp(position.y, targetY, 1f - Mathf.Exp(-12f * dt)) - position.y;
            }
            else
            {
                _verticalVelocity -= gravity * dt;
                motion.y = _verticalVelocity * dt;
                if (groundFound)
                {
                    float floor = groundPoint.y + groundOffset;
                    if (position.y + motion.y < floor)
                    {
                        motion.y = floor - position.y;
                        _verticalVelocity = 0f;
                    }
                }
            }

            if (motion.sqrMagnitude < 1e-10f) return;

            if (_controller != null && _controller.enabled)
            {
                _controller.Move(motion);
            }
            else
            {
                Vector3 desiredPos = position + motion;
                float radius = bodyRadius;
                Vector3 direction = motion;
                float distance = direction.magnitude;

                if (distance > 0.001f &&
                    Physics.SphereCast(position + Vector3.up * groundOffset,
                        radius, direction.normalized, out RaycastHit hit,
                        distance + radius, obstacleMask,
                        QueryTriggerInteraction.Ignore))
                {
                    float safeDistance = Mathf.Max(0f, hit.distance - radius);
                    desiredPos = position + direction.normalized * safeDistance;
                    _blockedCompletely = true;
                }

                desiredPos.y = position.y + motion.y;
                transform.position = desiredPos;
            }
        }

        private bool TryResolveDirection(Vector3 position, float footY, Vector3 desired, out Vector3 result)
        {
            if (IsDirectionWalkable(position, footY, desired))
            {
                result = desired;
                return true;
            }

            if (avoidanceAngles != null)
            {
                foreach (float angle in avoidanceAngles)
                {
                    Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * desired;
                    if (!IsDirectionWalkable(position, footY, candidate)) continue;

                    result = candidate;
                    return true;
                }
            }

            // Разворот — последний шанс выйти из тупика.
            foreach (float angle in RearAvoidanceAngles)
            {
                Vector3 candidate = Quaternion.Euler(0f, angle, 0f) * desired;
                if (!IsDirectionWalkable(position, footY, candidate)) continue;

                result = candidate;
                return true;
            }

            result = desired;
            return false;
        }

        private bool IsDirectionWalkable(Vector3 position, float footY, Vector3 direction)
        {
            Vector3 origin = new(position.x, footY + stepHeight + bodyRadius, position.z);

            var hits = Physics.SphereCastAll(origin, bodyRadius, direction,
                lookAheadDistance, obstacleMask, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.root == transform.root) continue;
                if (hit.collider.bounds.max.y - footY <= stepHeight) continue;
                return false;
            }
            if (!avoidLedges) return true;
            Vector3 ahead = position + direction * lookAheadDistance;
            Vector3 aheadProbe = new(ahead.x, footY + stepHeight + 0.5f, ahead.z);

            if (!TryFindGround(aheadProbe, out Vector3 aheadGround)) return false;
            return Mathf.Abs(aheadGround.y - footY) <= Mathf.Max(stepHeight, maximumStepDown);
        }
        private bool TryFindGround(Vector3 from, out Vector3 point)
        {
            float distance = groundProbeDistance + stepHeight + 1f;

            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, distance,
                    groundMask, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.root != transform.root &&
                Vector3.Angle(hit.normal, Vector3.up) <= maximumSlope)
            {
                point = hit.point;
                return true;
            }

            // Луч мог задеть себя — добираем полным списком.
            var hits = Physics.RaycastAll(from, Vector3.down, distance, groundMask,
                QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var candidate in hits)
            {
                if (candidate.collider.transform.root == transform.root) continue;
                if (Vector3.Angle(candidate.normal, Vector3.up) > maximumSlope) continue;

                point = candidate.point;
                return true;
            }

            point = from;
            return false;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!HasDestination) return;

            Gizmos.color = Mode == MotorMode.NavMesh ? Color.green : new Color(1f, 0.5f, 0f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, _destination);
            Gizmos.DrawWireSphere(_destination, arriveRadius);
        }
#endif
    }
}
