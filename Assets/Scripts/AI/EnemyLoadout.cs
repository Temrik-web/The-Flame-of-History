using UnityEngine;

namespace FlameOfHistory.AI
{
    /// <summary>
    /// Оружие в руках врага.
    ///
    /// Что решает: раньше HitscanWeapon приходилось вешать вручную на дочерний
    /// объект и вручную выставлять muzzle/audio. Теперь достаточно указать префаб
    /// оружия — компонент сам поставит его в руку (кость Right Hand у Animator
    /// или заданный сокет), найдёт дуло, подцепит звук и отдаст ссылку EnemyAI.
    ///
    /// Поддерживает:
    ///   • префаб оружия либо уже вручную поставленное оружие-ребёнок;
    ///   • автопоиск кости руки у humanoid-аниматора;
    ///   • автопоиск дула по имени (Muzzle / MuzzlePoint / FirePoint / Barrel);
    ///   • выброс оружия из рук при смерти с физикой;
    ///   • смену оружия в рантайме через EquipWeapon().
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyLoadout : MonoBehaviour
    {
        [Header("Что дать в руки")]
        [Tooltip("Префаб оружия. На нём (или его детях) желательно иметь HitscanWeapon — " +
                 "если его нет, компонент добавит его сам.")]
        [SerializeField] private GameObject weaponPrefab;
        [Tooltip("Уже поставленное вручную оружие. Если задано — префаб игнорируется.")]
        [SerializeField] private HitscanWeapon existingWeapon;

        [Header("Куда крепить")]
        [Tooltip("Сокет в руке. Пусто — попробуем найти кость правой руки у Animator, " +
                 "иначе создадим сокет автоматически.")]
        [SerializeField] private Transform handSocket;
        [Tooltip("Искать кость руки у humanoid-аниматора, если сокет не задан.")]
        [SerializeField] private bool useAnimatorHandBone = true;
        [Tooltip("Использовать левую руку вместо правой.")]
        [SerializeField] private bool leftHanded = false;
        [Tooltip("Позиция сокета, если кость не найдена (локально от врага).")]
        [SerializeField] private Vector3 fallbackSocketPosition = new(0.22f, 1.35f, 0.28f);

        [Header("Подгонка оружия в руке")]
        [SerializeField] private Vector3 weaponLocalPosition = Vector3.zero;
        [SerializeField] private Vector3 weaponLocalEuler = Vector3.zero;
        [SerializeField] private Vector3 weaponLocalScale = Vector3.one;

        [Header("Дуло")]
        [Tooltip("Имена, по которым ищется дуло среди детей оружия.")]
        [SerializeField]
        private string[] muzzleNameCandidates = { "Muzzle", "MuzzlePoint", "FirePoint", "Barrel", "Fire" };
        [Tooltip("Если дуло не найдено — создать его в конце модели оружия.")]
        [SerializeField] private bool createMuzzleIfMissing = true;

        [Header("Слои и коллайдеры оружия")]
        [Tooltip("Перевести оружие на слой врага — чтобы его собственные коллайдеры " +
                 "не мешали рейкастам зрения и стрельбы.")]
        [SerializeField] private bool matchOwnerLayer = true;
        [Tooltip("Отключить коллайдеры оружия в руках (иначе они бьются о навмеш и стены).")]
        [SerializeField] private bool disableWeaponColliders = true;

        [Header("Выброс при смерти")]
        [SerializeField] private bool dropWeaponOnDeath = true;
        [Tooltip("Сила подброса при выбросе.")]
        [SerializeField, Min(0f)] private float dropImpulse = 1.6f;
        [Tooltip("Через сколько секунд убрать выброшенное оружие. 0 — не убирать.")]
        [SerializeField, Min(0f)] private float dropLifetime = 30f;
        [Tooltip("Масса выброшенного оружия.")]
        [SerializeField, Min(0.1f)] private float dropMass = 3.5f;

        [Tooltip("Пауза перед включением гравитации. В момент смерти оружие ещё стоит " +
                 "внутри коллайдера тела и пола — мгновенная физика выталкивает его " +
                 "под землю. Пауза даёт кадры на выход из чужой геометрии.")]
        [SerializeField, Min(0f)] private float dropGravityDelay = 0.5f;

        [Tooltip("Слои пола, по которым выброшенное оружие проверяет, не провалилось ли оно.")]
        [SerializeField] private LayerMask dropGroundMask = ~0;

        [Tooltip("Объект, который выпадает при смерти (например, DropMp40 с правильным " +
                 "коллайдером). Если задан — роняется он, а не оружие из рук.")]
        [SerializeField] private GameObject dropObject;

        [Header("Коллайдер выпавшего оружия")]
        [Tooltip("Центр BoxCollider у выпавшего оружия (в локальных координатах объекта).")]
        [SerializeField] private Vector3 droppedColliderCenter = new(-2.402877f, 1.935332f, 0.03047342f);
        [Tooltip("Размер BoxCollider у выпавшего оружия (в локальных координатах объекта).")]
        [SerializeField] private Vector3 droppedColliderSize = new(12.5f, 4.2f, 0.7f);

        [Header("Звук экипировки")]
        [SerializeField] private AudioClip equipSound;

        /// <summary>Текущее оружие в руках (может быть null).</summary>
        public HitscanWeapon Weapon { get; private set; }

        /// <summary>Сокет, в котором сидит оружие.</summary>
        public Transform Socket => handSocket;

        private EnemyVoice _voice;
        private GameObject _spawnedWeaponRoot;
        private bool _weaponDropped;

        private void Awake()
        {
            _voice = GetComponent<EnemyVoice>();
            ResolveSocket();
            EquipInitialWeapon();
        }

        // =====================================================================
        // Сокет
        // =====================================================================

        private void ResolveSocket()
        {
            if (handSocket != null) return;

            if (useAnimatorHandBone)
            {
                var animator = GetComponentInChildren<Animator>();
                if (animator != null && animator.isHuman)
                {
                    Transform bone = animator.GetBoneTransform(
                        leftHanded ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);

                    if (bone != null)
                    {
                        // Отдельный дочерний сокет: подгонять оружие смещением
                        // на самой кости нельзя — её крутит анимация.
                        handSocket = CreateSocket(bone, "WeaponSocket", Vector3.zero);
                        return;
                    }
                }
            }

            Vector3 position = fallbackSocketPosition;
            if (leftHanded) position.x = -Mathf.Abs(position.x);

            handSocket = CreateSocket(transform, "WeaponSocket", position);
        }

        private static Transform CreateSocket(Transform parent, string socketName, Vector3 localPosition)
        {
            Transform existing = parent.Find(socketName);
            if (existing != null) return existing;

            var socket = new GameObject(socketName).transform;
            socket.SetParent(parent, false);
            socket.localPosition = localPosition;
            socket.localRotation = Quaternion.identity;
            return socket;
        }

        // =====================================================================
        // Экипировка
        // =====================================================================

        private void EquipInitialWeapon()
        {
            if (existingWeapon != null)
            {
                AdoptWeapon(existingWeapon, reparent: true, playSound: false);
                return;
            }

            // Оружие могло быть уже вручную положено в иерархию врага.
            var alreadyPresent = GetComponentInChildren<HitscanWeapon>(true);
            if (alreadyPresent != null)
            {
                AdoptWeapon(alreadyPresent, reparent: false, playSound: false);
                return;
            }

            if (weaponPrefab != null)
                EquipWeapon(weaponPrefab, playSound: false);
        }

        /// <summary>
        /// Выдать врагу оружие из префаба. Старое, если было, уничтожается.
        /// Работает и в рантайме — например, при смене вооружения отряда.
        /// </summary>
        public HitscanWeapon EquipWeapon(GameObject prefab, bool playSound = true)
        {
            if (prefab == null) return null;

            ResolveSocket();
            ClearCurrentWeapon();

            GameObject instance = Instantiate(prefab, handSocket);
            instance.name = prefab.name;
            _spawnedWeaponRoot = instance;

            HitscanWeapon weapon = instance.GetComponent<HitscanWeapon>() ??
                                   instance.GetComponentInChildren<HitscanWeapon>(true);

            if (weapon == null)
            {
                // Модель без логики — навешиваем стрельбу сами.
                weapon = instance.AddComponent<HitscanWeapon>();
                Debug.Log($"[EnemyLoadout] {name}: на префабе «{prefab.name}» не было HitscanWeapon — " +
                          "компонент добавлен автоматически. Настрой урон и темп стрельбы в инспекторе " +
                          "префаба, чтобы значения не сбрасывались.", this);
            }

            AdoptWeapon(weapon, reparent: false, playSound);
            return weapon;
        }

        /// <summary>Взять под управление уже существующий HitscanWeapon.</summary>
        public void AdoptWeapon(HitscanWeapon weapon, bool reparent, bool playSound)
        {
            if (weapon == null) return;

            ResolveSocket();

            Transform weaponRoot = weapon.transform;

            if (reparent && weaponRoot.parent != handSocket)
            {
                weaponRoot.SetParent(handSocket, false);
                _spawnedWeaponRoot = weaponRoot.gameObject;
            }

            // Подгонка в руке применяется только если оружие реально сидит в сокете.
            if (weaponRoot.parent == handSocket)
            {
                weaponRoot.localPosition = weaponLocalPosition;
                weaponRoot.localRotation = Quaternion.Euler(weaponLocalEuler);
                weaponRoot.localScale = weaponLocalScale;
            }

            weapon.gameObject.SetActive(true);
            weapon.enabled = true;

            PrepareMuzzle(weapon);
            PrepareAudio(weapon);
            PrepareCollidersAndLayer(weapon);

            weapon.ResetAmmo();
            Weapon = weapon;

            // Сообщаем ИИ, чем он теперь вооружён.
            var ai = GetComponent<EnemyAI>();
            if (ai != null) ai.SetWeapon(weapon);

            if (playSound && equipSound != null)
            {
                if (_voice != null) _voice.PlayBodyOneShot(equipSound, 0.8f);
                else AudioSource.PlayClipAtPoint(equipSound, weaponRoot.position, 0.8f);
            }
        }

        private void ClearCurrentWeapon()
        {
            if (_spawnedWeaponRoot != null)
            {
                Destroy(_spawnedWeaponRoot);
                _spawnedWeaponRoot = null;
            }

            Weapon = null;
        }

        // =====================================================================
        // Подготовка оружия
        // =====================================================================

        private void PrepareMuzzle(HitscanWeapon weapon)
        {
            if (weapon.HasMuzzle) return;

            Transform muzzle = FindMuzzle(weapon.transform);

            if (muzzle == null && createMuzzleIfMissing)
            {
                // Ставим дуло в передний край меша оружия — так трассы и вспышки
                // выходят из ствола, а не из центра модели.
                float forwardExtent = 0.5f;
                var renderers = weapon.GetComponentsInChildren<Renderer>();

                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);

                    Vector3 localCenter = weapon.transform.InverseTransformPoint(bounds.center);
                    Vector3 localExtents = weapon.transform.InverseTransformVector(bounds.extents);
                    forwardExtent = localCenter.z + Mathf.Abs(localExtents.z);
                }

                muzzle = CreateSocket(weapon.transform, "Muzzle",
                    new Vector3(0f, 0f, Mathf.Max(0.15f, forwardExtent)));
            }

            if (muzzle != null) weapon.SetMuzzle(muzzle);
        }

        private Transform FindMuzzle(Transform weaponRoot)
        {
            if (muzzleNameCandidates == null) return null;

            foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                foreach (string candidate in muzzleNameCandidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    if (child.name.IndexOf(candidate, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return child;
                }
            }

            return null;
        }

        private void PrepareAudio(HitscanWeapon weapon)
        {
            if (weapon.HasAudioSource) return;

            AudioSource source = weapon.GetComponent<AudioSource>();
            if (source == null) source = weapon.gameObject.AddComponent<AudioSource>();

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 5f;
            source.maxDistance = 90f;

            weapon.SetAudioSource(source);
        }

        private void PrepareCollidersAndLayer(HitscanWeapon weapon)
        {
            if (matchOwnerLayer)
            {
                foreach (Transform child in weapon.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = gameObject.layer;
            }

            if (!disableWeaponColliders) return;

            foreach (Collider collider in weapon.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        // =====================================================================
        // Смерть
        // =====================================================================

        /// <summary>
        /// Вызывается EnemyAI при смерти: оружие выпадает из рук и падает на землю.
        ///
        /// Гравитация включается не сразу, а через dropGravityDelay: в кадре смерти
        /// оружие ещё сидит внутри коллайдера тела, и включённая физика выталкивает
        /// его сквозь пол. Задержку оружие проводит kinematic, зависнув в воздухе.
        /// </summary>
        public void HandleOwnerDeath()
        {
            HitscanWeapon weapon = Weapon;
            if (weapon == null) return;

            weapon.CancelReload();
            weapon.enabled = false;

            if (!dropWeaponOnDeath) return;

            Transform weaponRoot = weapon.transform;

            if (dropObject != null)
            {
                SpawnDropObject(weaponRoot);
                _spawnedWeaponRoot = null;
                Weapon = null;
                _weaponDropped = true;
                return;
            }

            weaponRoot.SetParent(null, true);

            // Коллайдеры в руках были выключены (disableWeaponColliders) —
            // без них оружие пролетело бы сквозь пол независимо от гравитации
            bool hasCollider = false;
            foreach (Collider collider in weaponRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = true;
                collider.isTrigger = false;   // триггер физику не останавливает
                hasCollider = true;
            }

            if (!hasCollider) hasCollider = AddFallbackCollider(weaponRoot.gameObject);

            var body = weaponRoot.GetComponent<Rigidbody>();
            if (body == null) body = weaponRoot.gameObject.AddComponent<Rigidbody>();

            body.mass = dropMass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // Слой врага исключён из рейкастов зрения, но оружие на нём
            // может не сталкиваться с полом. Возвращаем на Default.
            if (matchOwnerLayer)
                foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = 0;

            if (!hasCollider)
            {
                Debug.LogWarning($"[EnemyLoadout] {name}: у выброшенного оружия нет ни одного " +
                                 "коллайдера — оно провалится под пол. Добавь коллайдер на префаб оружия.",
                                 this);
            }

            var dropper = weaponRoot.gameObject.AddComponent<DroppedWeapon>();
            dropper.Initialize(
                gravityDelay: dropGravityDelay,
                launchVelocity: (transform.forward * 0.4f + Vector3.up) * dropImpulse,
                spin: Random.insideUnitSphere * 3f,
                groundMask: dropGroundMask,
                lifetime: dropLifetime);

            _spawnedWeaponRoot = null;
            Weapon = null;
            _weaponDropped = true;
        }

        /// <summary>
        /// Страховка: если врага уничтожили без вызова HandleOwnerDeath
        /// (Destroy по таймеру трупа, выгрузка сцены), оружие всё равно выпадет
        /// вместо того, чтобы исчезнуть вместе с телом.
        ///
        /// Важно: перевешивать оружие из иерархии здесь нельзя — Unity уже
        /// помечает детей на уничтожение вместе с родителем, и SetParent(null)
        /// не спасёт объект. Поэтому создаётся новый экземпляр, а не переносится
        /// существующий.
        /// </summary>
        private void OnDestroy()
        {
            if (_weaponDropped || !dropWeaponOnDeath) return;
            if (Weapon == null) return;

            // Смена сцены и выход из игры: ронять нечего, всё и так удаляется
            if (!Application.isPlaying) return;

            _weaponDropped = true;

            if (dropObject == null)
            {
                Debug.LogWarning($"[EnemyLoadout] {name} уничтожен без HandleOwnerDeath, " +
                                 "а Drop Object не задан — оружие пропало вместе с телом. " +
                                 "Задай Drop Object, чтобы выпадение работало и в этом случае.", this);
                return;
            }

            SpawnDropObject(Weapon.transform);
            Weapon = null;
            _spawnedWeaponRoot = null;
        }

        /// <summary>
        /// Выбросить оружие без задержки. Используется при уничтожении врага:
        /// корутин и Update у уже мёртвого объекта не будет, поэтому физика
        /// включается сразу, здесь и сейчас.
        /// </summary>
        public void DropWeaponImmediate()
        {
            HitscanWeapon weapon = Weapon;
            if (weapon == null) return;

            _weaponDropped = true;

            Transform weaponRoot = weapon.transform;
            weapon.enabled = false;

            if (dropObject != null)
            {
                SpawnDropObject(weaponRoot);
                _spawnedWeaponRoot = null;
                Weapon = null;
                return;
            }

            weaponRoot.SetParent(null, true);

            foreach (Collider collider in weaponRoot.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = true;
                collider.isTrigger = false;
            }

            foreach (Transform child in weaponRoot.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 0;

            var body = weaponRoot.GetComponent<Rigidbody>();
            if (body == null) body = weaponRoot.gameObject.AddComponent<Rigidbody>();

            body.isKinematic = false;
            body.useGravity = true;
            body.mass = dropMass;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            body.AddForce((transform.forward * 0.4f + Vector3.up) * dropImpulse, ForceMode.Impulse);
            body.AddTorque(Random.insideUnitSphere * dropImpulse, ForceMode.Impulse);

            // Даже в аварийном пути кладём оружие на бок и страхуем от провала
            var dropper = weaponRoot.gameObject.AddComponent<DroppedWeapon>();
            dropper.Initialize(
                gravityDelay: 0f,
                launchVelocity: Vector3.zero,
                spin: Vector3.zero,
                groundMask: dropGroundMask,
                lifetime: dropLifetime);

            _spawnedWeaponRoot = null;
            Weapon = null;
        }

        /// <summary>
        /// Выбросить заданный dropObject (например, DropMp40) вместо оружия из рук.
        /// У объекта уже есть свой коллайдер и настройки DroppedWeapon, поэтому здесь
        /// его только позиционируем, поднимаем в воздух и даём лёгкий подброс.
        /// </summary>
        private void SpawnDropObject(Transform weaponRoot)
        {
            // Позицию берём до выключения: у неактивного объекта transform
            // читается нормально, но так надёжнее и понятнее.
            Vector3 spawnPosition = weaponRoot.position;
            Quaternion spawnRotation = weaponRoot.rotation;

            // Оружие в руках больше не нужно — прячем его, чтобы не осталось дубликатов.
            weaponRoot.gameObject.SetActive(false);

            GameObject instance = Instantiate(dropObject, spawnPosition, spawnRotation);
            instance.name = dropObject.name;
            instance.transform.SetParent(null);
            instance.SetActive(true);

            // Переводим объект на слой землю/Default, чтобы физика срабатывала корректно.
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 0;

            var body = instance.GetComponent<Rigidbody>();
            if (body == null) body = instance.AddComponent<Rigidbody>();

            body.mass = dropMass;
            body.isKinematic = true;          // DroppedWeapon сам включит гравитацию
            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            // Убеждаемся, что все коллайдеры включены — DroppedWeapon их временно глушит сам.
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
                c.enabled = true;

            // Гарантируем правильный коллайдер у выпавшего оружия (после включения всех).
            ApplyDropCollider(instance);

            var dropper = instance.GetComponent<DroppedWeapon>();
            if (dropper == null) dropper = instance.AddComponent<DroppedWeapon>();

            dropper.Initialize(
                gravityDelay: dropGravityDelay,
                launchVelocity: (transform.forward * 0.4f + Vector3.up) * dropImpulse,
                spin: Random.insideUnitSphere * 3f,
                groundMask: dropGroundMask,
                lifetime: dropLifetime);
        }

        /// <summary>
        /// Настраивает BoxCollider выпавшего оружия на заданные центр и размер.
        /// Все остальные коллайдеры (на корне и детях) отключаем, чтобы физическая
        /// форма была ровно одна — заданный бокс.
        /// </summary>
        private void ApplyDropCollider(GameObject instance)
        {
            BoxCollider box = instance.GetComponent<BoxCollider>();
            if (box == null) box = instance.AddComponent<BoxCollider>();

            box.center = droppedColliderCenter;
            box.size = droppedColliderSize;
            box.isTrigger = false;

            // Отключаем все прочие коллайдеры — оставляем только наш бокс.
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true))
            {
                if (c == box) continue;
                c.enabled = false;
            }
        }

        /// <summary>
        /// Коллайдер по габаритам модели, если на префабе его не было.
        /// Без коллайдера гравитация просто уронит оружие в бездну.
        /// </summary>
        private static bool AddFallbackCollider(GameObject target)
        {
            Bounds bounds = default;
            bool hasBounds = false;

            foreach (Renderer r in target.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else bounds.Encapsulate(r.bounds);
            }

            if (!hasBounds) return false;

            var box = target.AddComponent<BoxCollider>();
            box.center = target.transform.InverseTransformPoint(bounds.center);
            box.size = target.transform.InverseTransformVector(bounds.size);

            // Отрицательный размер после инверсии масштаба ломает коллайдер
            box.size = new Vector3(
                Mathf.Max(0.02f, Mathf.Abs(box.size.x)),
                Mathf.Max(0.02f, Mathf.Abs(box.size.y)),
                Mathf.Max(0.02f, Mathf.Abs(box.size.z)));

            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform socket = handSocket;
            Vector3 position = socket != null
                ? socket.position
                : transform.TransformPoint(fallbackSocketPosition);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(position, 0.06f);

            if (Weapon != null && Weapon.HasMuzzle)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(position, Weapon.MuzzlePosition);
            }
        }
#endif
    }
}
