using UnityEngine;
using UnityEngine.AI;

namespace FlameOfHistory.AI
{
    /// <summary>
    /// Слой передвижения врага по земле.
    ///
    /// Зачем нужен: EnemyAI раньше ходил только через NavMeshAgent, и если NavMesh
    /// не запечён (или враг заспавнен рядом, но не на нём) — все вызовы SetDestination
    /// молча игнорировались, и враг стоял на месте.
    ///
    /// Теперь есть два режима:
    ///   NavMesh  — обычная навигация агентом (предпочтительно, умеет обходить углы);
    ///   Fallback — прямое движение по земле: гравитация, прижатие к поверхности,
    ///              объезд препятствий «усами» и отказ от шага в пропасть.
    ///
    /// Режим выбирается автоматически и переключается на ходу: как только под врагом
    /// появляется NavMesh, мотор возвращается к агенту.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public sealed class EnemyMotor : MonoBehaviour
    {
        public enum MotorMode
        {
            /// <summary>Движение через NavMeshAgent.</summary>
            NavMesh,

            /// <summary>Движение вручную по коллайдерам земли.</summary>
            Fallback
        }

        [Header("Общее")]
        [Tooltip("Радиус, в котором цель считается достигнутой.")]
        [SerializeField, Min(0.05f)] private float arriveRadius = 1.2f;
        [Tooltip("Скорость разворота корпуса, град/сек.")]
        [SerializeField, Min(1f)] private float turnSpeed = 540f;

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
        [SerializeField, Min(0f)] private float gravity = 22f;
        [Tooltip("Отступ центра капсулы от земли. Обычно половина высоты врага.")]
        [SerializeField, Min(0f)] private float groundOffset = 0f;

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

        private float _nextNavMeshCheckTime;
        private bool _warnedAboutFallback;

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

        /// <summary>
        /// Насколько центр объекта должен стоять выше земли. Без этого в режиме
        /// Fallback враг утапливается в пол по пояс: transform.position у капсулы —
        /// это её центр, а не ступни.
        /// </summary>
        private float MeasureGroundOffset()
        {
            if (_controller != null)
                return _controller.height * 0.5f - _controller.center.y + _controller.skinWidth;

            // Берём самый крупный коллайдер тела и считаем расстояние от центра
            // объекта до его нижней точки.
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
        }

        // =====================================================================
        // Публичный API — им пользуется EnemyAI
        // =====================================================================

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

        /// <summary>Остановиться и забыть цель.</summary>
        public void Stop()
        {
            _blockedCompletely = false;

            // Состояние Search дёргает Stop() каждый кадр — без этой проверки
            // ResetPath пересчитывал бы путь агента впустую 60 раз в секунду.
            bool agentBusy = AgentUsable && (_agent.hasPath || _agent.pathPending || !_agent.isStopped);
            if (!HasDestination && !agentBusy) return;

            HasDestination = false;

            if (AgentUsable)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        /// <summary>Достигнута ли текущая цель.</summary>
        public bool HasArrived()
        {
            if (!HasDestination) return true;

            // Агент строит путь асинхронно: сразу после SetDestination у него ещё
            // нет ни пути, ни скорости. Без этой отсрочки состояние ИИ решило бы,
            // что уже пришло, и враг не сделал бы ни шага.
            if (Time.time - _destinationSetTime < 0.15f) return false;

            if (Mode == MotorMode.NavMesh && AgentUsable)
            {
                if (_agent.pathPending) return false;

                // Путь не построился (цель за пропастью, вне навмеша) — считаем
                // это прибытием, иначе состояние ИИ зависнет в ожидании навсегда.
                if (_agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;
                if (!_agent.hasPath) return true;

                if (_agent.remainingDistance > Mathf.Max(_agent.stoppingDistance, arriveRadius) + 0.15f)
                    return false;

                return _agent.velocity.sqrMagnitude < 0.05f;
            }

            // В fallback-режиме «застрял намертво» тоже считается прибытием.
            if (_blockedCompletely) return true;

            return FlatDistance(transform.position, _destination) <= arriveRadius;
        }

        /// <summary>
        /// Найти проходимую точку рядом с желаемой.
        /// В режиме NavMesh — через NavMesh.SamplePosition, иначе — прижатием к земле.
        /// </summary>
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

            // Fallback: ищем землю от уровня ног врага, а не от произвольной высоты
            // желаемой точки — иначе на неровном рельефе луч уходит в воздух.
            Vector3 probe = new(desired.x, transform.position.y - groundOffset + stepHeight + 1f, desired.z);

            if (TryFindGround(probe, out Vector3 grounded))
            {
                // Возвращаем точку на уровне центра тела, чтобы MoveTo и HasArrived
                // работали в одних и тех же координатах.
                result = grounded + Vector3.up * groundOffset;
                return true;
            }

            result = desired;
            return false;
        }

        /// <summary>Плавно повернуть корпус в сторону точки (только по горизонтали).</summary>
        public void FaceTowards(Vector3 worldPoint, float turnSpeedMultiplier = 1f)
        {
            Vector3 direction = worldPoint - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, target,
                turnSpeed * Mathf.Max(0.05f, turnSpeedMultiplier) * Time.deltaTime);
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

        // =====================================================================
        // Основной цикл
        // =====================================================================

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

        // =====================================================================
        // Выбор режима
        // =====================================================================

        private void EvaluateMode(bool initial)
        {
            if (_agent == null) return;

            if (_agent.enabled && _agent.isOnNavMesh)
            {
                if (Mode != MotorMode.NavMesh)
                {
                    Mode = MotorMode.NavMesh;
                    _verticalVelocity = 0f;
                    if (HasDestination) MoveTo(_destination);
                }
                return;
            }

            // Агент выключен или свалился с навмеша — пробуем вернуть.
            if (TryReturnToNavMesh())
            {
                Mode = MotorMode.NavMesh;
                if (HasDestination) MoveTo(_destination);
                return;
            }

            if (Mode != MotorMode.Fallback)
            {
                Mode = MotorMode.Fallback;

                // Агент в этом режиме только мешает: он продолжит писать
                // ошибки и держать transform. Отключаем, но не удаляем.
                if (_agent.enabled) _agent.enabled = false;

                if (allowFallbackMovement && !_warnedAboutFallback)
                {
                    _warnedAboutFallback = true;
                    Debug.Log($"[EnemyMotor] {name}: NavMesh недоступен — " +
                              "враг ходит в режиме Fallback (по коллайдерам земли). " +
                              "Запеки NavMesh, чтобы включилась полноценная навигация.", this);
                }
                else if (!allowFallbackMovement && !_warnedAboutFallback)
                {
                    _warnedAboutFallback = true;
                    Debug.LogWarning($"[EnemyMotor] {name}: NavMesh не найден, а Fallback выключен — " +
                                     "враг не будет двигаться.", this);
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

            bool wasEnabled = _agent.enabled;
            if (!wasEnabled) _agent.enabled = true;

            // Warp корректно ставит агента на навмеш без «телепорта сквозь стены».
            if (!_agent.Warp(hit.position))
            {
                if (!wasEnabled) _agent.enabled = false;
                return false;
            }

            _agent.speed = _desiredSpeed;
            _verticalVelocity = 0f;
            return _agent.isOnNavMesh;
        }

        // =====================================================================
        // Fallback: ручная ходьба по земле
        // =====================================================================

        private void UpdateFallbackMovement(float dt)
        {
            Vector3 position = transform.position;

            // transform.position — это центр тела, а не ступни. Все проверки земли
            // и препятствий считаем от уровня ног, иначе враг «висит» и никогда
            // не считается стоящим на земле.
            float footY = position.y - groundOffset;

            // --- горизонталь ---
            Vector3 horizontal = Vector3.zero;

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
                    Vector3 desiredDirection = toTarget / distance;

                    if (TryResolveDirection(position, footY, desiredDirection, out Vector3 clearDirection))
                    {
                        _blockedCompletely = false;
                        float step = Mathf.Min(_desiredSpeed, distance / Mathf.Max(dt, 0.0001f));
                        horizontal = clearDirection * step;

                        // Разворот в сторону фактического движения. В бою поворотом
                        // управляет ИИ (FaceTowards), поэтому тут не мешаем.
                        if (_autoRotation)
                        {
                            Quaternion target = Quaternion.LookRotation(clearDirection, Vector3.up);
                            transform.rotation = Quaternion.RotateTowards(
                                transform.rotation, target, turnSpeed * dt);
                        }
                    }
                    else
                    {
                        // Обошли всё — упёрлись. Сообщаем ИИ через HasArrived.
                        _blockedCompletely = true;
                    }
                }
            }

            // --- вертикаль: прижатие к земле или падение ---
            Vector3 probeStart = new(position.x, footY + stepHeight + 0.1f, position.z);
            bool groundFound = TryFindGround(probeStart, out Vector3 groundPoint);

            _isGrounded = groundFound && footY - groundPoint.y <= stepHeight + 0.05f;

            Vector3 motion = horizontal * dt;

            if (_isGrounded)
            {
                _verticalVelocity = 0f;

                // Плавно подтягиваем тело к поверхности — так враг взбирается
                // по склонам и ступенькам без рывков.
                float targetY = groundPoint.y + groundOffset;
                motion.y = Mathf.Lerp(position.y, targetY, 1f - Mathf.Exp(-12f * dt)) - position.y;
            }
            else
            {
                _verticalVelocity -= gravity * dt;
                motion.y = _verticalVelocity * dt;

                // Не проваливаемся ниже найденной земли за один кадр.
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
                _controller.Move(motion);
            else
                transform.position = position + motion;
        }

        /// <summary>
        /// Подобрать направление, свободное от препятствий. Сначала пробуем прямо,
        /// затем — заданные углы объезда влево/вправо.
        /// </summary>
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

            result = desired;
            return false;
        }

        private bool IsDirectionWalkable(Vector3 position, float footY, Vector3 direction)
        {
            // Проверяем на высоте чуть выше ступеньки: то, что ниже, враг переступит.
            Vector3 origin = new(position.x, footY + stepHeight + bodyRadius, position.z);

            var hits = Physics.SphereCastAll(origin, bodyRadius, direction,
                lookAheadDistance, obstacleMask, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                // Собственные коллайдеры игнорируем.
                if (hit.collider.transform.root == transform.root) continue;

                // Низкое препятствие в пределах ступеньки — переступим.
                if (hit.collider.bounds.max.y - footY <= stepHeight) continue;

                return false;
            }

            if (!avoidLedges) return true;

            // Есть ли земля там, куда шагаем.
            Vector3 ahead = position + direction * lookAheadDistance;
            Vector3 aheadProbe = new(ahead.x, footY + stepHeight + 0.5f, ahead.z);

            if (!TryFindGround(aheadProbe, out Vector3 aheadGround))
                return false;

            // Слишком крутой спуск или подъём — не идём.
            return Mathf.Abs(aheadGround.y - footY) <= Mathf.Max(stepHeight, 1f);
        }

        /// <summary>Найти точку земли под указанной позицией.</summary>
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

            // Луч мог уйти в свой же коллайдер — пробуем полный список.
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
