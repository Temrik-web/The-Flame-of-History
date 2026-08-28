using System.Collections;
using UnityEngine;

/// <summary>
/// Граната в руках: замах, бросок, полёт, взрыв.
///
/// Управление:
///   ПКМ (удерживать) — поднять гранату, приготовиться к броску;
///   ЛКМ             — замах и бросок;
///   ЛКМ+ПКМ         — короткий подкат под ноги (слабый бросок), если включено;
///   X (настраивается) — выдернуть запал заранее, чтобы граната рванула в воздухе.
///
/// После броска граната списывается из инвентаря, а модель в руках прячется.
/// Если гранат больше нет — руки пустеют через WeaponSlotManager.
///
/// Вешается на модель гранаты рядом с EquippableWeapon.
/// </summary>
[DisallowMultipleComponent]
public class GrenadeItem : HeldItem
{
    // =====================================================================
    [Header("Снаряд")]
    [Tooltip("Префаб летящей гранаты. Пусто — будет создана копия модели из рук " +
             "с добавленным Rigidbody, коллайдером и ThrownGrenade.")]
    public GameObject grenadePrefab;

    [Tooltip("Откуда вылетает граната. Пусто — точка чуть впереди камеры.")]
    public Transform throwOrigin;

    [Tooltip("Сдвиг точки вылета относительно камеры, если Throw Origin не задан.")]
    public Vector3 throwOriginOffset = new Vector3(0.25f, -0.1f, 0.55f);

    [Header("Бросок")]
    [Tooltip("Сила обычного броска.")]
    public float throwForce = 15f;

    [Tooltip("Сила слабого броска (подкатить под ноги).")]
    public float weakThrowForce = 5.5f;

    [Tooltip("Разрешить слабый бросок по ЛКМ, пока держишь ПКМ.")]
    public bool allowWeakThrow = true;

    [Tooltip("Подъём броска над направлением взгляда, градусы. Дуга вместо прямой линии.")]
    public float throwUpwardAngle = 12f;

    [Tooltip("Добавлять скорость игрока к скорости гранаты — на бегу летит дальше.")]
    public bool inheritPlayerVelocity = true;

    [Tooltip("Начальное вращение гранаты в полёте.")]
    public Vector3 throwSpin = new Vector3(-7f, 1.5f, 0.5f);

    [Header("Запал")]
    [Tooltip("Время горения запала после броска.")]
    [Min(0.1f)] public float fuseTime = 3.8f;

    [Tooltip("Клавиша: выдернуть запал заранее (готовить в руке). " +
             "None — механика выключена. G не используем: он занят " +
             "выбрасыванием предметов из инвентаря (G + цифра).")]
    public KeyCode cookKey = KeyCode.X;

    [Tooltip("Взорвётся в руке, если передержать. Реалистично, но злобно.")]
    public bool explodeInHandsIfOvercooked = true;

    [Header("Тайминги анимации")]
    [Tooltip("Время замаха до момента вылета.")]
    public float windupTime = 0.32f;
    [Tooltip("Время проводки руки после вылета.")]
    public float followThroughTime = 0.22f;
    [Tooltip("Пауза перед появлением следующей гранаты в руке.")]
    public float rearmTime = 0.45f;

    [Header("Поза замаха")]
    public Vector3 windupPosition = new Vector3(0.42f, -0.12f, 0.18f);
    public Vector3 windupRotation = new Vector3(-38f, 22f, 12f);

    [Header("Поза броска")]
    public Vector3 releasePosition = new Vector3(0.1f, -0.05f, 0.72f);
    public Vector3 releaseRotation = new Vector3(22f, -14f, -8f);

    [Header("Поза выдернутого запала")]
    public Vector3 cookedPosition = new Vector3(0.2f, -0.26f, 0.42f);
    public Vector3 cookedRotation = new Vector3(-14f, 12f, 6f);

    [Header("Отдача руки")]
    public Vector3 throwKickPosition = new Vector3(-0.05f, 0.03f, -0.12f);
    public Vector3 throwKickRotation = new Vector3(-9f, 4f, 0f);

    [Header("Расход из инвентаря")]
    [Tooltip("Списывать одну гранату из инвентаря при броске.")]
    public bool consumeFromInventory = true;

    [Tooltip("Id оружия, по которому ищется предмет в инвентаре. " +
             "Пусто — берётся Weapon Id с EquippableWeapon на этом же объекте.")]
    public string inventoryWeaponId = "";

    [Header("Звуки")]
    public AudioClip pinPullSound;
    public AudioClip throwSound;
    public AudioClip rearmSound;
    [Range(0f, 1f)] public float soundVolume = 0.85f;

    [Header("Отладка")]
    public bool logActions = true;

    // =====================================================================
    private EquippableWeapon equippable;
    private Coroutine throwRoutine;

    private bool isThrowing;
    private bool pinPulled;
    private float cookTimer;

    /// <summary>Идёт ли бросок прямо сейчас.</summary>
    public bool IsThrowing => isThrowing;

    /// <summary>Выдернут ли запал (граната «горит» в руке).</summary>
    public bool IsPinPulled => pinPulled;

    /// <summary>Сколько осталось до взрыва, если запал выдернут.</summary>
    public float CookTimeLeft => pinPulled ? Mathf.Max(0f, fuseTime - cookTimer) : fuseTime;

    // =====================================================================
    protected override void Awake()
    {
        base.Awake();

        equippable = GetComponent<EquippableWeapon>();
        if (equippable == null) equippable = GetComponentInParent<EquippableWeapon>();

        if (string.IsNullOrEmpty(inventoryWeaponId) && equippable != null)
            inventoryWeaponId = equippable.weaponId;

        // Гранате в руках физика не нужна: позой управляет HeldItem.
        // Rigidbody на модели вырывал бы её из держателя и ронял на землю —
        // отсюда и брался эффект «граната появилась в мире».
        DisableHeldPhysics();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        isThrowing = false;
        pinPulled = false;
        cookTimer = 0f;
        SetModelVisible(true);
    }

    protected override void OnDisable()
    {
        base.OnDisable();

        if (throwRoutine != null)
        {
            StopCoroutine(throwRoutine);
            throwRoutine = null;
        }

        // Спрятать гранату с выдернутым запалом — не способ отменить запал.
        // Раньше это давало бесплатный сброс: выдернул кольцо, нажал 0, взял снова.
        if (pinPulled && !isThrowing) DropLiveGrenade();

        isThrowing = false;
        pinPulled = false;
        cookTimer = 0f;
        ClearPoseOverride();
    }

    /// <summary>
    /// Уронить «горящую» гранату под ноги. Вызывается, когда игрок убирает
    /// из рук гранату с выдернутым запалом.
    /// </summary>
    void DropLiveGrenade()
    {
        // Выгрузка сцены и выход из Play: создавать объекты нельзя
        if (!Application.isPlaying) return;
        if (!gameObject.scene.isLoaded) return;

        float remainingFuse = Mathf.Max(0.3f, fuseTime - cookTimer);

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        Vector3 dropPoint = cam.position + cam.forward * 0.4f - cam.up * 0.35f;

        GameObject instance = grenadePrefab != null
            ? Instantiate(grenadePrefab, dropPoint, Quaternion.identity)
            : BuildRuntimeGrenade(dropPoint, cam.forward);

        ThrownGrenade thrown = instance.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = instance.AddComponent<ThrownGrenade>();

        GameObject thrower = Controller != null ? Controller.gameObject : gameObject;
        thrown.Launch(cam.forward * 1.2f, Vector3.zero, thrower, remainingFuse);

        ConsumeGrenade();

        if (logActions)
            Debug.LogWarning($"[Grenade] Убрал гранату с выдернутым запалом — она упала под ноги " +
                             $"(до взрыва {remainingFuse:0.##} с).");
    }

    // =====================================================================
    protected override void HandleInput()
    {
        if (isThrowing) return;

        if (cookKey != KeyCode.None && !pinPulled && Input.GetKeyDown(cookKey))
            PullPin();

        if (Input.GetMouseButtonDown(0))
        {
            bool weak = allowWeakThrow && Input.GetMouseButton(1);
            Throw(weak ? weakThrowForce : throwForce);
        }
    }

    protected override void Update()
    {
        base.Update();

        if (!pinPulled || isThrowing) return;

        // Граната горит в руке даже во время диалога: запал не знает про UI
        cookTimer += Time.deltaTime;
        SetPoseOverride(cookedPosition, cookedRotation, 0.7f);

        if (cookTimer >= fuseTime && explodeInHandsIfOvercooked)
            ExplodeInHands();
    }

    // =====================================================================
    /// <summary>Выдернуть запал, не бросая гранату.</summary>
    public void PullPin()
    {
        if (pinPulled || isThrowing) return;

        pinPulled = true;
        cookTimer = 0f;
        PlaySound(pinPullSound, soundVolume);
        AddKick(new Vector3(-0.02f, 0f, -0.03f), new Vector3(-3f, 2f, 0f));

        if (logActions) Debug.Log("[Grenade] Запал выдернут.");
    }

    /// <summary>Бросить гранату с заданной силой.</summary>
    public void Throw(float force)
    {
        if (isThrowing) return;
        if (!HasGrenadeInInventory())
        {
            if (logActions) Debug.Log("[Grenade] Гранат больше нет.");
            return;
        }

        throwRoutine = StartCoroutine(ThrowSequence(force));
    }

    IEnumerator ThrowSequence(float force)
    {
        isThrowing = true;

        // Первый бросок после экипировки: модель ещё едет к позе «в руках»,
        // и замах стартовал бы с произвольной точки (тот же баг был у ножа)
        Transform model = itemModel != null ? itemModel : transform;
        model.localPosition = hipPosition;
        model.localRotation = Quaternion.Euler(hipRotation);
        SetPoseOverride(hipPosition, hipRotation, 1f);

        // --- замах ---
        float t = 0f;
        while (t < windupTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, windupTime));
            SetPoseOverride(
                Vector3.Lerp(hipPosition, windupPosition, k),
                Vector3.Lerp(hipRotation, windupRotation, k),
                k);
            yield return null;
        }

        if (!pinPulled) PullPin();

        // --- вылет ---
        SetPoseOverride(releasePosition, releaseRotation, 1f);
        SpawnGrenade(force);
        SetModelVisible(false);
        PlaySound(throwSound, soundVolume);
        AddKick(throwKickPosition, throwKickRotation);

        bool hasMore = ConsumeGrenade();

        // --- проводка ---
        t = 0f;
        while (t < followThroughTime)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.01f, followThroughTime));
            SetPoseOverride(
                Vector3.Lerp(releasePosition, hipPosition, k),
                Vector3.Lerp(releaseRotation, hipRotation, k),
                1f - k);
            yield return null;
        }

        ClearPoseOverride();
        pinPulled = false;
        cookTimer = 0f;

        if (!hasMore)
        {
            // Гранат не осталось: убираем из рук, иначе игрок «держит» пустоту
            isThrowing = false;
            throwRoutine = null;

            if (WeaponSlotManager.Instance != null) WeaponSlotManager.Instance.Holster();
            else SetModelVisible(false);

            if (logActions) Debug.Log("[Grenade] Последняя граната брошена, руки пустые.");
            yield break;
        }

        // --- достаём следующую ---
        if (rearmTime > 0f) yield return new WaitForSeconds(rearmTime);

        SetModelVisible(true);
        PlaySound(rearmSound, soundVolume);

        isThrowing = false;
        throwRoutine = null;
    }

    // =====================================================================
    /// <summary>Создать летящую гранату и запустить её.</summary>
    void SpawnGrenade(float force)
    {
        Vector3 origin = GetThrowOrigin();
        Vector3 direction = GetThrowDirection();

        GameObject instance = grenadePrefab != null
            ? Instantiate(grenadePrefab, origin, Quaternion.LookRotation(direction))
            : BuildRuntimeGrenade(origin, direction);

        ThrownGrenade thrown = instance.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = instance.AddComponent<ThrownGrenade>();

        Vector3 velocity = direction * force;
        if (inheritPlayerVelocity) velocity += PlayerVelocity;

        // Оставшееся время запала: подготовленная граната рванёт раньше
        float remainingFuse = pinPulled ? Mathf.Max(0.15f, fuseTime - cookTimer) : fuseTime;

        GameObject thrower = Controller != null ? Controller.gameObject : gameObject;
        thrown.Launch(velocity, throwSpin * Mathf.Deg2Rad * 30f, thrower, remainingFuse);

        if (logActions)
            Debug.Log($"[Grenade] Брошена: сила {force:0.#}, запал {remainingFuse:0.##} с.");
    }

    /// <summary>
    /// Копия модели из рук как снаряд. Нужна, чтобы граната летала даже без
    /// заранее собранного префаба: в сцене есть только модель в руках.
    /// </summary>
    GameObject BuildRuntimeGrenade(Vector3 origin, Vector3 direction)
    {
        Vector3 worldScale = transform.lossyScale;
        Quaternion rotation = Quaternion.LookRotation(direction);

        // Копия не должна цепляться к руке в своём Awake, иначе она сначала
        // прыгнет в держатель и полетит от него, а не от точки броска
        GameObject copy;
        HeldItem.SuppressAttachOnAwake = true;
        try
        {
            copy = Instantiate(gameObject, origin, rotation);
        }
        finally
        {
            HeldItem.SuppressAttachOnAwake = false;
        }

        copy.name = name + " (Thrown)";

        // Логика «в руках» на снаряде не нужна и мешает. Сначала гасим скрипты:
        // Destroy срабатывает только в конце кадра, а до него Update успел бы
        // вернуть копию в позу «в руках».
        foreach (HeldItem held in copy.GetComponentsInChildren<HeldItem>(true))
        {
            held.enabled = false;
            Destroy(held);
        }
        foreach (EquippableWeapon eq in copy.GetComponentsInChildren<EquippableWeapon>(true))
        {
            eq.enabled = false;
            Destroy(eq);
        }
        foreach (Pickup pickup in copy.GetComponentsInChildren<Pickup>(true))
        {
            pickup.enabled = false;
            Destroy(pickup);
        }
        foreach (AudioSource src in copy.GetComponentsInChildren<AudioSource>(true)) Destroy(src);

        copy.transform.SetParent(null, false);
        copy.transform.position = origin;
        copy.transform.rotation = rotation;
        copy.transform.localScale = worldScale;

        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        copy.SetActive(true);

        EnsureProjectileColliders(copy);

        return copy;
    }

    /// <summary>Снаряду нужен непустой не-триггерный коллайдер, иначе он провалится сквозь пол.</summary>
    void EnsureProjectileColliders(GameObject target)
    {
        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        bool hasSolid = false;

        foreach (Collider c in colliders)
        {
            if (c == null) continue;
            c.isTrigger = false;
            c.enabled = true;
            hasSolid = true;
        }

        if (hasSolid) return;

        // Радиус по габаритам модели: сфера точнее «магической» константы
        float radius = 0.08f;
        Renderer rend = target.GetComponentInChildren<Renderer>();
        if (rend != null) radius = Mathf.Max(0.03f, rend.bounds.extents.magnitude * 0.5f);

        SphereCollider sphere = target.AddComponent<SphereCollider>();
        sphere.radius = radius;
    }

    Vector3 GetThrowOrigin()
    {
        if (throwOrigin != null) return throwOrigin.position;

        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        return cam.position
               + cam.right * throwOriginOffset.x
               + cam.up * throwOriginOffset.y
               + cam.forward * throwOriginOffset.z;
    }

    Vector3 GetThrowDirection()
    {
        Transform cam = playerCamera != null ? playerCamera.transform : transform;
        return Quaternion.AngleAxis(-throwUpwardAngle, cam.right) * cam.forward;
    }

    // =====================================================================
    /// <summary>Есть ли ещё граната в сумке. Без инвентаря считаем, что да.</summary>
    bool HasGrenadeInInventory()
    {
        if (!consumeFromInventory) return true;

        InventorySystem inv = InventorySystem.Instance;
        if (inv == null || string.IsNullOrEmpty(inventoryWeaponId)) return true;

        return inv.CountWeaponItem(inventoryWeaponId) > 0;
    }

    /// <summary>Списать одну гранату. Возвращает true, если ещё осталось.</summary>
    bool ConsumeGrenade()
    {
        if (!consumeFromInventory) return true;

        InventorySystem inv = InventorySystem.Instance;
        if (inv == null || string.IsNullOrEmpty(inventoryWeaponId)) return true;

        ItemData item = inv.GetWeaponItem(inventoryWeaponId);
        if (item == null) return false;

        inv.RemoveItem(item, 1);
        return inv.CountWeaponItem(inventoryWeaponId) > 0;
    }

    // =====================================================================
    void ExplodeInHands()
    {
        if (logActions) Debug.LogWarning("[Grenade] Передержал — взрыв в руках.");

        Vector3 point = playerCamera != null ? playerCamera.transform.position : transform.position;

        GameObject instance = grenadePrefab != null
            ? Instantiate(grenadePrefab, point, Quaternion.identity)
            : BuildRuntimeGrenade(point, Vector3.forward);

        ThrownGrenade thrown = instance.GetComponent<ThrownGrenade>();
        if (thrown == null) thrown = instance.AddComponent<ThrownGrenade>();

        // Thrower не указываем: игрок должен получить урон от своей же ошибки
        thrown.Launch(Vector3.zero, Vector3.zero, null, 0.01f);
        thrown.Explode();

        pinPulled = false;
        cookTimer = 0f;
        ConsumeGrenade();

        if (WeaponSlotManager.Instance != null) WeaponSlotManager.Instance.Holster();
    }

    /// <summary>
    /// Убрать физику у модели в руках: Rigidbody вырвал бы её из держателя,
    /// а коллайдер толкал бы игрока.
    /// </summary>
    void DisableHeldPhysics()
    {
        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (c == null) continue;
            // Коллайдер оставляем на объекте: он нужен снаряду-копии
            c.enabled = false;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Запомнить текущую позу как замах")]
    void CaptureWindupPose()
    {
        windupPosition = transform.localPosition;
        windupRotation = transform.localEulerAngles;
    }

    [ContextMenu("Запомнить текущую позу как бросок")]
    void CaptureReleasePose()
    {
        releasePosition = transform.localPosition;
        releaseRotation = transform.localEulerAngles;
    }
#endif
}
