using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FlameOfHistory.AI
{
    /// <summary>
    /// Проигрыватель анимаций врага БЕЗ AnimatorController.
    /// Перетащи AnimationClip'ы в инспектор — скрипт сам склеит их через PlayableGraph:
    /// idle/walk/run/sprint смешиваются по скорости, shoot/reload/hit/death — поверх одним слоем.
    /// Управляется из EnemyAI (SetLocomotion / PlayShoot / PlayReload / PlayHit / PlayDeath).
    /// Все клипы необязательны: чего нет — то молча пропускается, ошибок в консоли не будет.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAnimator : MonoBehaviour
    {
        [Header("Ссылки")]
        [Tooltip("Animator модели врага. Пусто — найдётся сам (оружие исключается из поиска). " +
                 "ВАЖНО: поле Controller у этого Animator должно быть пустым (None) — " +
                 "клипы задаются ниже в Локомоции. Контроллер + граф вместе крутят " +
                 "одну и ту же анимацию дважды (рывки и перезапуск перехода).")]
        [SerializeField] private Animator animator;
        [Tooltip("Выключить Root Motion: движением владеет EnemyMotor/NavMeshAgent, " +
                 "иначе модель и навмеш тянут тело в разные стороны.")]
        [SerializeField] private bool disableRootMotion = true;
        [Tooltip("Писать предупреждения (нет Animator, нет клипов и т.п.).")]
        [SerializeField] private bool verbose = true;

        [Header("Локомоция — перетащи сюда свои клипы")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip runClip;
        [SerializeField] private AnimationClip sprintClip;

        [Header("На какой скорости какой клип (м/с)")]
        [Tooltip("Якорь ходьбы. Держи равным Patrol Speed из EnemyAI.")]
        [SerializeField, Min(0.1f)] private float walkAnchorSpeed = 1.8f;
        [Tooltip("Якорь бега. Держи равным Chase Speed из EnemyAI.")]
        [SerializeField, Min(0.1f)] private float runAnchorSpeed = 3.8f;
        [SerializeField, Min(0.1f)] private float sprintAnchorSpeed = 5f;
        [Tooltip("Ниже этой скорости считаемся стоящими.")]
        [SerializeField, Min(0f)] private float idleMaxSpeed = 0.25f;
        [Tooltip("Как быстро веса догоняют цель. Больше — резче переходы.")]
        [SerializeField, Min(1f)] private float blendResponsiveness = 8f;

        [Header("Экшен-клипы (необязательно — можно оставить пустыми)")]
        [SerializeField] private AnimationClip shootClip;
        [SerializeField] private AnimationClip reloadClip;
        [SerializeField] private AnimationClip hitClip;
        [SerializeField] private AnimationClip deathClip;
        [Tooltip("Скорость появления/затухания экшен-слоя.")]
        [SerializeField, Min(1f)] private float actionFadeSpeed = 8f;
        [Tooltip("Не чаще этого проигрывать анимацию выстрела (выстрелов в секунду больше).")]
        [SerializeField, Min(0f)] private float shootAnimCooldown = 0.28f;
        [Tooltip("Не чаще этого проигрывать анимацию попадания.")]
        [SerializeField, Min(0f)] private float hitAnimCooldown = 0.45f;
        [Tooltip("Если Death-клипа нет — завалить тело на бок, иначе труп останется стоять.")]
        [SerializeField] private bool tipOverOnDeathWithoutClip = true;

        public Animator Animator => animator;
        public bool IsDead => _dead;

        public bool HasLocomotion =>
            idleClip != null || walkClip != null || runClip != null || sprintClip != null;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _locoMixer;
        private AnimationLayerMixerPlayable _layerMixer;
        private readonly AnimationClipPlayable[] _locoPlayables = new AnimationClipPlayable[4];
        private AnimationClipPlayable _actionPlayable;
        private readonly float[] _weights = new float[4];
        private readonly float[] _targets = new float[4];

        private float _speed;
        private bool _aiming;
        private bool _dead;
        private bool _graphReady;

        private float _actionTime;
        private float _actionLength;
        private float _actionWeight;
        private float _actionTargetWeight;
        private bool _actionHoldAtEnd;

        private float _nextShootTime;
        private float _nextHitTime;
        private bool _tippedOver;
        private bool _warnedNoAnimator;
        private bool _warnedNoClips;
        private bool _warnedController;
        private bool _warnedNoDeathClip;
        private bool _warnedNoGraph;

        private void Awake()
        {
            ResolveAnimator();
            WarnIfControllerConflicts();
            if (animator != null && disableRootMotion)
                animator.applyRootMotion = false;
            if (runAnchorSpeed <= walkAnchorSpeed) runAnchorSpeed = walkAnchorSpeed + 0.5f;
            if (sprintAnchorSpeed <= runAnchorSpeed) sprintAnchorSpeed = runAnchorSpeed + 0.5f;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (animator == null)
            {
                ResolveAnimator();
                if (animator == null) return;
                if (disableRootMotion) animator.applyRootMotion = false;
            }
            WarnIfControllerConflicts();
            if (HasLocomotion || deathClip != null)
                BuildGraph();
        }

        private void OnDisable()
        {
            DestroyGraph();
        }

        private void OnDestroy()
        {
            DestroyGraph();
        }

        /// <summary>Пересобрать граф (например, если клипы поменяли в рантайме).</summary>
        public void Rebuild()
        {
            if (!Application.isPlaying) return;
            DestroyGraph();
            if (animator != null && (HasLocomotion || deathClip != null))
                BuildGraph();
        }

        /// <summary>Вызывается из EnemyAI каждый кадр: текущая скорость м/с.</summary>
        public void SetLocomotion(float speed)
        {
            _speed = Mathf.Max(0f, speed);
        }

        /// <summary>Вызывается из EnemyAI при смене состояния (пока только запоминается).</summary>
        public void SetAiming(bool aiming)
        {
            _aiming = aiming;
        }

        public void PlayShoot()
        {
            if (_dead || shootClip == null) return;
            if (Time.time < _nextShootTime) return;
            _nextShootTime = Time.time + Mathf.Max(0.05f, shootAnimCooldown);
            StartAction(shootClip, holdAtEnd: false);
        }

        public void PlayReload()
        {
            if (_dead || reloadClip == null) return;
            StartAction(reloadClip, holdAtEnd: false);
        }

        public void PlayHit()
        {
            if (_dead || hitClip == null) return;
            if (Time.time < _nextHitTime) return;
            _nextHitTime = Time.time + Mathf.Max(0.05f, hitAnimCooldown);
            StartAction(hitClip, holdAtEnd: false);
        }

        public void PlayDeath()
        {
            if (_dead) return;
            _dead = true;
            if (deathClip != null)
            {
                StartAction(deathClip, holdAtEnd: true);
                return;
            }
            if (tipOverOnDeathWithoutClip && !_tippedOver)
            {
                _tippedOver = true;
                transform.Rotate(Vector3.right, -85f, Space.Self);
            }
            if (verbose && !_warnedNoDeathClip)
            {
                _warnedNoDeathClip = true;
                Debug.Log($"[EnemyAnimator] {name}: Death-клип не задан — " +
                    "тело завалено на бок заглушкой. Подставь deathClip для нормальной смерти.", this);
            }
        }

        private void LateUpdate()
        {
            if (!_graphReady || !_graph.IsValid()) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            ComputeTargets(_speed, _targets);

            bool standingWithoutIdle = _speed <= idleMaxSpeed && idleClip == null;
            int slowest = SlowestAvailableIndex();

            float k = Mathf.Min(1f, blendResponsiveness * dt);
            for (int i = 0; i < 4; i++)
            {
                _weights[i] += (_targets[i] - _weights[i]) * k;
                if (Mathf.Abs(_targets[i] - _weights[i]) < 0.001f) _weights[i] = _targets[i];
                _locoMixer.SetInputWeight(i, _dead ? 0f : _weights[i]);

                var p = _locoPlayables[i];
                if (p.IsValid())
                    p.SetSpeed(_dead || (standingWithoutIdle && i == slowest) ? 0f : 1f);
            }

            UpdateAction(dt);
        }

        private void UpdateAction(float dt)
        {
            if (!_actionPlayable.IsValid())
            {
                _layerMixer.SetInputWeight(1, 0f);
                _actionWeight = 0f;
                return;
            }

            _actionTime += dt;
            if (_actionTime >= _actionLength)
            {
                if (_actionHoldAtEnd)
                {
                    _actionPlayable.SetTime(_actionLength);
                    _actionPlayable.Pause();
                    _actionWeight = 1f;
                    _layerMixer.SetInputWeight(0, 0f);
                    _layerMixer.SetInputWeight(1, 1f);
                    return;
                }
                _actionTargetWeight = 0f;
                if (_actionWeight <= 0.01f)
                {
                    _graph.Disconnect(_layerMixer, 1);
                    _actionPlayable.Destroy();
                    _actionPlayable = new AnimationClipPlayable();
                    _layerMixer.SetInputWeight(1, 0f);
                    _layerMixer.SetInputWeight(0, 1f);
                    return;
                }
            }

            _actionWeight = Mathf.MoveTowards(
                _actionWeight, _actionTargetWeight, actionFadeSpeed * dt);
            _layerMixer.SetInputWeight(1, _actionWeight);
            _layerMixer.SetInputWeight(0, _dead ? 0f : 1f - _actionWeight);
        }

        private void StartAction(AnimationClip clip, bool holdAtEnd)
        {
            if (clip == null) return;
            if (!_graphReady || !_graph.IsValid())
            {
                if (verbose && !_warnedNoGraph)
                {
                    _warnedNoGraph = true;
                    Debug.LogWarning($"[EnemyAnimator] {name}: нет графа (нет Animator/клипов) — " +
                        $"экшен «{clip.name}» пропущен.", this);
                }
                return;
            }

            if (_actionPlayable.IsValid())
            {
                _graph.Disconnect(_layerMixer, 1);
                _actionPlayable.Destroy();
                _actionPlayable = new AnimationClipPlayable();
            }

            _actionPlayable = AnimationClipPlayable.Create(_graph, clip);
            _actionPlayable.SetTime(0);
            _actionPlayable.Play();
            _graph.Connect(_actionPlayable, 0, _layerMixer, 1);

            _actionTime = 0f;
            _actionLength = Mathf.Max(0.05f, (float)clip.length);
            _actionHoldAtEnd = holdAtEnd;
            _actionTargetWeight = 1f;
            _layerMixer.SetInputWeight(1, _actionWeight);
        }

        private int SlowestAvailableIndex()
        {
            if (walkClip != null) return 1;
            if (runClip != null) return 2;
            if (sprintClip != null) return 3;
            if (idleClip != null) return 0;
            return -1;
        }

        private void ComputeTargets(float speed, float[] outW)
        {
            outW[0] = outW[1] = outW[2] = outW[3] = 0f;

            float[] anchors = { 0f, walkAnchorSpeed, runAnchorSpeed, sprintAnchorSpeed };
            AnimationClip[] clips = { idleClip, walkClip, runClip, sprintClip };

            int first = -1, last = -1;
            for (int i = 0; i < 4; i++)
                if (clips[i] != null) { if (first < 0) first = i; last = i; }
            if (first < 0) return;

            if (speed <= idleMaxSpeed)
            {
                outW[idleClip != null ? 0 : first] = 1f;
                return;
            }

            if (speed <= anchors[first]) { outW[first] = 1f; return; }
            if (speed >= anchors[last]) { outW[last] = 1f; return; }

            int lo = first;
            for (int i = first; i < last; i++)
            {
                if (clips[i] == null) continue;
                int next = -1;
                for (int j = i + 1; j <= last; j++)
                    if (clips[j] != null) { next = j; break; }
                if (next < 0) break;
                if (speed >= anchors[i] && speed <= anchors[next]) { lo = i; break; }
            }

            int hi = -1;
            for (int j = lo + 1; j <= last; j++)
                if (clips[j] != null) { hi = j; break; }
            if (hi < 0) { outW[lo] = 1f; return; }

            float span = Mathf.Max(0.001f, anchors[hi] - anchors[lo]);
            float t = Mathf.Clamp01((speed - anchors[lo]) / span);
            outW[lo] = 1f - t;
            outW[hi] = t;
        }

        private void BuildGraph()
        {
            DestroyGraph();
            if (animator == null) return;

            _graph = PlayableGraph.Create("EnemyAnimator_" + name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            _locoMixer = AnimationMixerPlayable.Create(_graph, 4);
            AnimationClip[] clips = { idleClip, walkClip, runClip, sprintClip };
            for (int i = 0; i < 4; i++)
            {
                if (clips[i] != null)
                {
                    var p = AnimationClipPlayable.Create(_graph, clips[i]);
                    p.Play();
                    _locoPlayables[i] = p;
                    _graph.Connect(p, 0, _locoMixer, i);
                    _locoMixer.SetInputWeight(i, 0f);
                }
                else
                {
                    _locoPlayables[i] = new AnimationClipPlayable();
                    _locoMixer.SetInputWeight(i, 0f);
                }
            }

            _layerMixer = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_locoMixer, 0, _layerMixer, 0);
            _layerMixer.SetInputWeight(0, 1f);
            _layerMixer.SetInputWeight(1, 0f);

            var output = AnimationPlayableOutput.Create(_graph, "EnemyOutput", animator);
            output.SetSourcePlayable(_layerMixer);

            ComputeTargets(0f, _targets);
            for (int i = 0; i < 4; i++) _weights[i] = _targets[i];

            _graph.Play();
            _graphReady = true;
        }

        private void DestroyGraph()
        {
            if (_graph.IsValid()) _graph.Destroy();
            _graphReady = false;
            _actionPlayable = new AnimationClipPlayable();
            _actionWeight = 0f;
            _actionTargetWeight = 0f;
        }

        /// <summary>
        /// БАГ: AnimatorController в поле Controller и PlayableGraph крутят кости
        /// одновременно — переход дёргается и проигрывается дважды. Клипы должны
        /// жить только в полях ниже (Локомоция/Экшен), Controller — пустой (None).
        /// </summary>
        private void WarnIfControllerConflicts()
        {
            if (_warnedController || animator == null) return;
            if (animator.runtimeAnimatorController == null) return;
            _warnedController = true;
            Debug.LogWarning($"[EnemyAnimator] {name}: у Animator задан Controller " +
                $"«{animator.runtimeAnimatorController.name}» — он и граф EnemyAnimator " +
                "проигрывают анимацию одновременно (двойной переход с рывком). " +
                "Убери Controller (поставь None), клипы уже заданы в полях ниже.", this);
        }

        private void ResolveAnimator()
        {
            if (animator != null) return;
            animator = GetComponent<Animator>();
            if (animator != null) return;

            var weapon = GetComponentInChildren<HitscanWeapon>(true);
            Transform weaponRoot = weapon != null ? weapon.transform : null;

            foreach (Animator cand in GetComponentsInChildren<Animator>(true))
            {
                if (cand == null) continue;
                if (weaponRoot != null &&
                    (cand.transform == weaponRoot || cand.transform.IsChildOf(weaponRoot)))
                    continue;
                animator = cand;
                return;
            }
        }

        private void OnValidate()
        {
            if (runAnchorSpeed <= walkAnchorSpeed) runAnchorSpeed = walkAnchorSpeed + 0.5f;
            if (sprintAnchorSpeed <= runAnchorSpeed) sprintAnchorSpeed = runAnchorSpeed + 0.5f;
        }
    }
}
