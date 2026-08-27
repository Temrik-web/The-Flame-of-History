using UnityEngine;

namespace FlameOfHistory.AI
{
    /// <summary>
    /// Оружие, выпавшее из рук убитого врага.
    /// Гарантированная укладка на левый/правый бок плашмя.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroppedWeapon : MonoBehaviour
    {
        [Header("Падение")]
        [SerializeField, Min(0f)] private float gravityDelay = 0.5f;
        [SerializeField, Min(0f)] private float gravityDelayRandom = 0.2f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0f)] private float watchDuration = 6f;
        [SerializeField, Min(0.05f)] private float fallThroughTolerance = 0.3f;
        [SerializeField, Min(1f)] private float groundSearchHeight = 30f;

        [Header("Физика")]
        [SerializeField] private PhysicMaterial physicMaterial;
        [SerializeField] private bool autoCenterOfMass = true;
        [SerializeField] private Vector3 centerOfMassOffset = Vector3.zero;
        [SerializeField, Range(0f, 1f)] private float velocityRandomness = 0.3f;
        [SerializeField, Range(0f, 1f)] private float spinRandomness = 0.3f;

        [Header("Укладка на бок")]
        [SerializeField] private bool layFlatOnGround = true;
        [SerializeField, Min(10f)] private float layFlatSpeed = 180f;
        [SerializeField, Min(0f)] private float layFlatSpeedThreshold = 0.8f;
        [SerializeField, Min(0f)] private float layFlatForceDelay = 2f;
        [SerializeField, Min(0.1f)] private float layFlatLiftHeight = 0.3f;
        [SerializeField, Min(0.1f)] private float layFlatLowerSpeed = 0.5f;
        [SerializeField] private bool alignToSurface = true;
        [SerializeField, Range(0f, 30f)] private float maxSurfaceAlignAngle = 15f;

        [Header("Заморозка")]
        [SerializeField] private bool freezeWhenSettled = true;
        [SerializeField, Min(0f)] private float freezeAfterCalm = 0.5f;
        [SerializeField, Min(0f)] private float calmSpeedThreshold = 0.2f;
        [SerializeField, Min(0f)] private float calmAngularThreshold = 0.2f;
        [SerializeField, Min(1f)] private float forceFreezeAfter = 10f;

        [Header("Жизнь")]
        [SerializeField, Min(0f)] private float lifetime = 30f;

        private Rigidbody body;
        private Vector3 initialVelocity;
        private Vector3 initialSpin;
        private float actualDelay;
        private float delayTimer;
        private float watchTimer;
        private bool gravityEnabled;
        private bool hasTouchedGround;
        private float calmTimer;
        private float aliveSinceGravity;
        private bool isFrozen;
        private Collider[] ownColliders;
        private Vector3[] groundCheckPoints;
        private int rescueAttempts;

        // Принудительная укладка
        private bool isLayingFlat;
        private int layFlatPhase; // 1 - подъём, 2 - поворот, 3 - опускание
        private Vector3 startLayPosition;
        private Quaternion startLayRotation;
        private Quaternion targetLayRotation;
        private float layFlatTimer;
        private bool targetSideChosen;
        private bool layRightSide;
        private Vector3 barrelAxisWorld;   // длинная горизонтальная ось
        private Vector3 thicknessAxisWorld; // короткая ось, становится вертикальной

        public void Initialize(float gravityDelay, Vector3 launchVelocity, Vector3 spin,
                               LayerMask groundMask, float lifetime)
        {
            this.gravityDelay = Mathf.Max(0f, gravityDelay);
            this.groundMask = groundMask;
            this.lifetime = Mathf.Max(0f, lifetime);

            initialVelocity = launchVelocity;
            initialSpin = spin;
            actualDelay = this.gravityDelay + Random.Range(0f, gravityDelayRandom);

            PrepareBody();
            CacheGroundPoints();

            if (this.lifetime > 0f) Destroy(gameObject, this.lifetime);
        }

        private void Awake() => PrepareBody();

        private void PrepareBody()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();

            body.isKinematic = true;
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // Гарантируем наличие коллайдера: если нет ни одного, создаём BoxCollider
            ownColliders = GetComponentsInChildren<Collider>();
            if (ownColliders.Length == 0)
            {
                BoxCollider box = gameObject.AddComponent<BoxCollider>();
                // Определяем размер по рендереру или задаём приблизительный (1м длина, 0.2м ширина, 0.3м высота)
                Renderer rend = GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    box.size = rend.bounds.size;
                    box.center = rend.bounds.center - transform.position;
                }
                else
                {
                    box.size = new Vector3(1.0f, 0.2f, 0.3f); // длина, толщина, высота
                }
                ownColliders = new Collider[] { box };
            }

            if (physicMaterial != null)
            {
                foreach (var col in ownColliders) col.material = physicMaterial;
            }

            if (!autoCenterOfMass)
            {
                body.centerOfMass += centerOfMassOffset;
            }

            SetCollidersEnabled(false);
        }

        private void SetCollidersEnabled(bool enabled)
        {
            if (ownColliders == null) ownColliders = GetComponentsInChildren<Collider>();
            foreach (var col in ownColliders) col.enabled = enabled;
        }

        private void CacheGroundPoints()
        {
            Bounds bounds = new Bounds(transform.position, Vector3.zero);
            foreach (var col in ownColliders)
            {
                bounds.Encapsulate(col.bounds);
            }

            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            var points = new System.Collections.Generic.List<Vector3>
            {
                transform.InverseTransformPoint(center),
                transform.InverseTransformPoint(center + Vector3.up * extents.y * 0.5f),
                transform.InverseTransformPoint(center - Vector3.up * extents.y * 0.5f),
                transform.InverseTransformPoint(center + Vector3.forward * extents.z * 0.5f),
                transform.InverseTransformPoint(center - Vector3.forward * extents.z * 0.5f),
                transform.InverseTransformPoint(center + Vector3.right * extents.x * 0.5f),
                transform.InverseTransformPoint(center - Vector3.right * extents.x * 0.5f)
            };
            groundCheckPoints = points.ToArray();
        }

        private void Update()
        {
            if (isFrozen) return;

            if (!gravityEnabled)
            {
                delayTimer += Time.deltaTime;
                if (delayTimer >= actualDelay)
                {
                    EnableGravity();
                }
                return;
            }

            aliveSinceGravity += Time.deltaTime;

            if (watchTimer < watchDuration)
            {
                watchTimer += Time.deltaTime;
                GuardAgainstFallThrough();
            }

            if (layFlatOnGround && !isLayingFlat)
            {
                UpdateLayFlatStart();
            }

            if (isLayingFlat)
            {
                UpdateLayFlatProcess();
            }

            UpdateFreeze();
        }

        private void EnableGravity()
        {
            gravityEnabled = true;
            ResolveInitialOverlap();
            SetCollidersEnabled(true);

            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Vector3 vel = initialVelocity + Random.insideUnitSphere * velocityRandomness;
            Vector3 spin = initialSpin + Random.insideUnitSphere * spinRandomness;
            body.AddForce(vel * body.mass, ForceMode.Impulse);
            body.AddTorque(spin * body.mass * 0.5f, ForceMode.Impulse);
        }

        private void ResolveInitialOverlap()
        {
            if (ownColliders == null) ownColliders = GetComponentsInChildren<Collider>();
            if (ownColliders.Length == 0) return;

            Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);
            foreach (var col in ownColliders)
            {
                combinedBounds.Encapsulate(col.bounds);
            }

            int maxIterations = 20;
            float stepUp = 0.1f;

            for (int i = 0; i < maxIterations; i++)
            {
                Collider[] overlaps = Physics.OverlapBox(combinedBounds.center, combinedBounds.extents,
                                                         transform.rotation, groundMask, QueryTriggerInteraction.Ignore);
                bool hasOverlap = false;
                foreach (var other in overlaps)
                {
                    if (other.transform != transform && !other.transform.IsChildOf(transform))
                    {
                        hasOverlap = true;
                        break;
                    }
                }

                if (!hasOverlap) break;

                transform.position += Vector3.up * stepUp;
                foreach (var col in ownColliders)
                {
                    combinedBounds.Encapsulate(col.bounds);
                }
            }

            if (Physics.OverlapBox(combinedBounds.center, combinedBounds.extents,
                                   transform.rotation, groundMask, QueryTriggerInteraction.Ignore).Length > 0)
            {
                Vector3 randomDir = Random.onUnitSphere;
                randomDir.y = Mathf.Abs(randomDir.y);
                transform.position += randomDir * 0.3f;
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!gravityEnabled) return;
            hasTouchedGround = true;
        }

        private bool IsGrounded()
        {
            if (groundCheckPoints == null) CacheGroundPoints();
            foreach (var localPoint in groundCheckPoints)
            {
                Vector3 worldPoint = transform.TransformPoint(localPoint);
                if (Physics.Raycast(worldPoint + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit,
                                    0.25f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsNearGround(out Vector3 groundPoint, out Vector3 normal)
        {
            groundPoint = transform.position;
            normal = Vector3.up;
            float minDist = float.MaxValue;
            bool found = false;
            foreach (var localPoint in groundCheckPoints)
            {
                Vector3 worldPoint = transform.TransformPoint(localPoint);
                if (Physics.Raycast(worldPoint + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit,
                                    0.5f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    if (hit.distance < minDist)
                    {
                        minDist = hit.distance;
                        groundPoint = hit.point;
                        normal = hit.normal;
                        found = true;
                    }
                }
            }
            return found;
        }

        private void UpdateLayFlatStart()
        {
            if (body == null || body.isKinematic) return;
            if (!hasTouchedGround && !IsGrounded()) return;

            float angleFromUp = Vector3.Angle(transform.up, Vector3.up);
            bool isLaying = angleFromUp > 45f;

            bool slowEnough = body.velocity.magnitude <= layFlatSpeedThreshold &&
                              body.angularVelocity.magnitude <= calmAngularThreshold;

            if (!slowEnough)
            {
                layFlatTimer = 0f;
                return;
            }

            // Если уже лежит на боку естественно, ждём заморозки
            if (isLaying && IsGrounded())
            {
                return;
            }

            layFlatTimer += Time.deltaTime;

            if (!isLaying || layFlatTimer > layFlatForceDelay)
            {
                StartLayFlat();
            }
        }

        private void StartLayFlat()
        {
            if (isLayingFlat) return;
            isLayingFlat = true;
            layFlatPhase = 1;
            startLayPosition = transform.position;
            startLayRotation = transform.rotation;

            ComputeLayOrientation();

            body.isKinematic = true;
            body.useGravity = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Определяет целевую ориентацию, при которой оружие ложится плашмя на самую
        /// большую грань (на бок), а не стоит на самом тонком ребре (дуле/магазине).
        /// Габариты считаются в локальной системе координат оружия, поэтому результат
        /// не зависит от того, под каким углом оружие упало на землю.
        /// </summary>
        private void ComputeLayOrientation()
        {
            if (!GetLocalBounds(out Vector3 size, out Vector3 center))
                return;

            // Локальные единичные оси оружия — ортонормированная тройка transform.
            Vector3[] localAxes = { transform.right, transform.up, transform.forward };

            // Толщина = ось с наименьшим размером -> станет вертикалью, т.е. оружие
            // ляжет на свою самую большую грань, а не на дуло.
            int thicknessIdx = 0;
            for (int i = 1; i < 3; i++)
                if (size[i] < size[thicknessIdx]) thicknessIdx = i;

            // Ствол = ось с наибольшим размером -> останется горизонтальной.
            int barrelIdx = 0;
            for (int i = 1; i < 3; i++)
                if (size[i] > size[barrelIdx]) barrelIdx = i;

            // Куб или слишком «круглая» форма — точную сторону не определить,
            // оставляем текущую ориентацию.
            if (thicknessIdx == barrelIdx) return;

            Vector3 localBarrel = localAxes[barrelIdx];

            // Случайный бок: на какую из двух больших граней уложить.
            if (!targetSideChosen)
            {
                layRightSide = Random.value < 0.5f;
                targetSideChosen = true;
            }
            Vector3 worldUp = layRightSide ? Vector3.up : Vector3.down;

            // Направление ствола в мире, спроецированное на горизонталь.
            Vector3 worldForward = transform.TransformDirection(localBarrel);
            worldForward.y = 0f;
            if (worldForward.sqrMagnitude < 0.01f)
                worldForward = Vector3.forward;
            worldForward.Normalize();

            targetLayRotation = Quaternion.LookRotation(worldForward, worldUp);
        }

        /// <summary>
        /// Габариты коллайдеров оружия в его локальном пространстве. Мировой AABB
        /// (col.bounds) зависит от поворота оружия и может неправильно указать на
        /// «тонкую» ось — локальные размеры такого недостатка лишены.
        /// </summary>
        private bool GetLocalBounds(out Vector3 size, out Vector3 center)
        {
            if (ownColliders == null) ownColliders = GetComponentsInChildren<Collider>();
            if (ownColliders == null || ownColliders.Length == 0)
            {
                size = Vector3.one;
                center = Vector3.zero;
                return false;
            }

            bool first = true;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            foreach (var col in ownColliders)
            {
                if (col == null || !col.enabled) continue;
                Vector3 bmin = col.bounds.min;
                Vector3 bmax = col.bounds.max;
                Vector3[] corners =
                {
                    new Vector3(bmin.x, bmin.y, bmin.z),
                    new Vector3(bmin.x, bmin.y, bmax.z),
                    new Vector3(bmin.x, bmax.y, bmin.z),
                    new Vector3(bmin.x, bmax.y, bmax.z),
                    new Vector3(bmax.x, bmin.y, bmin.z),
                    new Vector3(bmax.x, bmin.y, bmax.z),
                    new Vector3(bmax.x, bmax.y, bmin.z),
                    new Vector3(bmax.x, bmax.y, bmax.z)
                };

                foreach (var corner in corners)
                {
                    Vector3 local = transform.InverseTransformPoint(corner);
                    if (first)
                    {
                        min = local;
                        max = local;
                        first = false;
                    }
                    else
                    {
                        min = Vector3.Min(min, local);
                        max = Vector3.Max(max, local);
                    }
                }
            }

            if (first)
            {
                size = Vector3.one;
                center = Vector3.zero;
                return false;
            }

            size = max - min;
            center = (min + max) * 0.5f;
            for (int i = 0; i < 3; i++)
                size[i] = Mathf.Max(0.02f, size[i]);

            return true;
        }

        private void UpdateLayFlatProcess()
        {
            switch (layFlatPhase)
            {
                case 1: // Подъём
                    {
                        Vector3 targetPos = startLayPosition + Vector3.up * layFlatLiftHeight;
                        transform.position = Vector3.MoveTowards(transform.position, targetPos, layFlatLowerSpeed * 2f * Time.deltaTime);
                        if (Vector3.Distance(transform.position, targetPos) < 0.01f)
                        {
                            layFlatPhase = 2;
                        }
                        break;
                    }
                case 2: // Поворот
                    {
                        transform.rotation = Quaternion.RotateTowards(startLayRotation, targetLayRotation, layFlatSpeed * Time.deltaTime);
                        if (Quaternion.Angle(transform.rotation, targetLayRotation) < 1f)
                        {
                            transform.rotation = targetLayRotation;
                            layFlatPhase = 3;
                        }
                        break;
                    }
                case 3: // Опускание до земли
                    {
                        Vector3 groundPoint;
                        Vector3 normal;
                        if (IsNearGround(out groundPoint, out normal))
                        {
                            if (alignToSurface)
                            {
                                Quaternion surfaceAlignRot = Quaternion.FromToRotation(Vector3.up, normal);
                                Quaternion currentRot = transform.rotation;
                                Quaternion targetAlign = surfaceAlignRot * currentRot;
                                Quaternion limited = Quaternion.RotateTowards(currentRot, targetAlign, maxSurfaceAlignAngle);
                                transform.rotation = Quaternion.Slerp(transform.rotation, limited, 0.5f);
                            }

                            float targetY = groundPoint.y + 0.01f;
                            Vector3 targetPos = new Vector3(transform.position.x, targetY, transform.position.z);
                            transform.position = Vector3.MoveTowards(transform.position, targetPos, layFlatLowerSpeed * Time.deltaTime);

                            if (Mathf.Abs(transform.position.y - targetY) < 0.01f || IsGrounded())
                            {
                                FinishLayFlat();
                            }
                        }
                        else
                        {
                            FinishLayFlat();
                        }
                        break;
                    }
            }
        }

        private void FinishLayFlat()
        {
            layFlatPhase = 4;
            isLayingFlat = false;
            body.isKinematic = false;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            isFrozen = true;
            body.Sleep();
        }

        private void UpdateFreeze()
        {
            if (!freezeWhenSettled || body == null || body.isKinematic) return;
            if (isLayingFlat) return;

            bool calmLinear = body.velocity.magnitude <= calmSpeedThreshold;
            bool calmAngular = body.angularVelocity.magnitude <= calmAngularThreshold;
            bool grounded = IsGrounded();

            if (!grounded && calmLinear && calmAngular)
            {
                Vector3 groundPoint;
                Vector3 normal;
                if (IsNearGround(out groundPoint, out normal))
                {
                    Vector3 targetPos = new Vector3(transform.position.x, groundPoint.y + 0.02f, transform.position.z);
                    transform.position = Vector3.MoveTowards(transform.position, targetPos, 0.05f);
                    grounded = IsGrounded();
                }
            }

            bool isLaying = Vector3.Angle(transform.up, Vector3.up) > 45f;
            bool settled = grounded && calmLinear && calmAngular && isLaying;
            calmTimer = settled ? calmTimer + Time.deltaTime : 0f;

            bool timedOut = aliveSinceGravity >= forceFreezeAfter;

            if (!settled && !timedOut) return;

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.Sleep();
            isFrozen = true;
        }

        private void GuardAgainstFallThrough()
        {
            if (body == null || body.isKinematic) return;

            float avgGroundY = GetAverageGroundY();

            if (transform.position.y < avgGroundY - fallThroughTolerance)
            {
                rescueAttempts++;
                Vector3 targetPos = new Vector3(transform.position.x,
                                                avgGroundY + 0.15f,
                                                transform.position.z);
                transform.position = Vector3.MoveTowards(transform.position, targetPos, 0.5f);
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                if (rescueAttempts >= 3)
                {
                    hasTouchedGround = true;
                    body.isKinematic = true;
                    body.useGravity = false;
                    isFrozen = true;
                }
                return;
            }

            if (!hasTouchedGround)
            {
                bool stuck = body.velocity.magnitude < 0.05f && body.angularVelocity.magnitude < 0.05f;
                bool highAbove = transform.position.y > avgGroundY + 0.4f;
                if (stuck && highAbove && aliveSinceGravity > 0.4f)
                {
                    body.WakeUp();
                    Vector3 randomForce = Vector3.down * 3f + Random.insideUnitSphere * 0.8f;
                    body.AddForce(randomForce * body.mass, ForceMode.Impulse);
                }
            }
        }

        private float GetAverageGroundY()
        {
            float sum = 0f;
            int count = 0;
            foreach (var localPoint in groundCheckPoints)
            {
                Vector3 worldPoint = transform.TransformPoint(localPoint);
                if (Physics.Raycast(worldPoint + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit,
                                    groundSearchHeight, groundMask, QueryTriggerInteraction.Ignore))
                {
                    sum += hit.point.y;
                    count++;
                }
            }
            return count > 0 ? sum / count : transform.position.y;
        }
    }
}