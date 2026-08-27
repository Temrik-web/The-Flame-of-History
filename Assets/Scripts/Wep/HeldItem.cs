using UnityEngine;
using EasyPeasyFirstPersonController;

/// <summary>
/// База для всего, что игрок держит в руках, кроме огнестрела: нож, граната,
/// фонарь, лопата и т.д. Делает ровно то, чем Wep занимается для ППШ, но
/// без стрельбы, магазинов и кинематографичной перезарядки:
///
///   - цепляет модель к держателю в руках (тому же, где висит ППШ);
///   - держит позу «в руках» / «поднято» (ПКМ);
///   - качает предмет при ходьбе, беге, прыжке, приседе и от движения мышью;
///   - даёт наследникам простой способ проиграть замах/удар/бросок
///     через SetPoseOverride, не переписывая всю математику заново.
///
/// Почему это отдельный класс, а не Wep: Wep жёстко завязан на патроны,
/// затвор, магазины и режимы огня. Ножу и гранате это не нужно, а нужное
/// (поза в руках) в Wep нельзя переиспользовать — оно там перемешано
/// с логикой стрельбы.
///
/// Вешается на модель предмета рядом с EquippableWeapon.
/// EquippableWeapon сам включит и выключит этот скрипт вместе с моделью.
/// </summary>
public abstract class HeldItem : MonoBehaviour
{
    // =====================================================================
    // Ссылки и держатель
    // =====================================================================
    [Header("Держатель")]
    [Tooltip("Родитель, к которому цепляется предмет. Пусто — найдётся сам: " +
             "сначала держатель ППШ (чтобы позы были в одной системе координат), " +
             "потом объект WeaponHolder, потом камера.")]
    public Transform holder;

    [Tooltip("Перецепить предмет к держателю при старте. Именно это лечит случай, " +
             "когда модель лежит где-то в мире, а не в руках.")]
    public bool autoAttachToHolder = true;

    [Tooltip("Имя объекта-держателя, который ищется, если ППШ в сцене нет.")]
    public string holderObjectName = "WeaponHolder";

    [Header("Ссылки")]
    [Tooltip("Пусто — Camera.main или первая камера в родителях.")]
    public Camera playerCamera;

    [Tooltip("Модель, которую двигает скрипт. Пусто — этот же объект.")]
    public Transform itemModel;

    public AudioSource audioSource;

    // =====================================================================
    // Поза
    // =====================================================================
    [Header("Поза в руках (локально относительно держателя)")]
    [Tooltip("Взять позу из Custom Pos / Custom Rot вместо Hip Position / Hip Rotation.")]
    public bool useCustomPose = false;

    [Tooltip("Кастомная локальная позиция в руках.")]
    public Vector3 customPos;

    [Tooltip("Кастомный локальный поворот в руках, градусы.")]
    public Vector3 customRot;

    public Vector3 hipPosition = new Vector3(0.28f, -0.32f, 0.55f);
    public Vector3 hipRotation = new Vector3(0f, -15f, 0f);

    [Tooltip("Поза по ПКМ: поднять предмет к лицу / приготовиться.")]
    public Vector3 aimPosition = new Vector3(0.12f, -0.2f, 0.45f);
    public Vector3 aimRotation = new Vector3(0f, -5f, 0f);

    [Tooltip("Использовать ПКМ как «поднять предмет». Наследник может отключить " +
             "и занять ПКМ своим действием (например, сильный удар ножом).")]
    public bool useRightMouseAsAim = true;

    public float aimTransitionSpeed = 7f;

    [Header("Скорость следования за целевой позой")]
    [Tooltip("Обычное состояние: чем больше, тем жёстче предмет держится в позе.")]
    public float followSpeed = 12f;
    [Tooltip("Во время замаха/удара/броска — резче, иначе анимация выглядит вязкой.")]
    public float animationFollowSpeed = 26f;
    [Tooltip("Насколько анимация подавляет покачивание (0 — не подавляет).")]
    [Range(0f, 1f)] public float animationMotionDamping = 0.85f;

    // =====================================================================
    // Покачивание
    // =====================================================================
    [Header("Покачивание от мыши")]
    public float swayAmount = 0.035f;
    public float swaySmoothness = 8f;
    public float swayInertiaSmoothness = 0.1f;
    public float swayMultiplier = 0.9f;

    [Header("Покачивание при ходьбе")]
    public float bobAmplitudeWalk = 0.035f;
    public float bobAmplitudeRun = 0.06f;
    public float bobFrequencyWalk = 1.8f;
    public float bobFrequencyRun = 2.3f;
    public float bobSmoothTime = 0.12f;
    public float bobVerticalMultiplier = 0.8f;
    public float bobHorizontalMultiplier = 0.6f;
    public float bobRotationMultiplier = 0.5f;

    [Header("Прыжок и присед")]
    public float jumpOffsetY = -0.06f;
    public float jumpOffsetZ = 0.04f;
    public float crouchOffsetY = -0.05f;
    public float crouchOffsetZ = 0.03f;
    public float crouchRotationX = 4f;

    [Header("Дыхание и холостое покачивание")]
    public float idleSwaySpeed = 1.2f;
    public float idleSwayAmount = 0.012f;
    public float breathAmplitude = 0.0035f;
    public float breathSpeed = 1f;

    [Header("Прицел")]
    [Tooltip("Показывать перекрестие, пока предмет в руках. Объект берётся у ППШ, " +
             "чтобы прицел не оставался спрятанным после смены оружия.")]
    public bool manageCrosshair = true;
    public GameObject crosshairObject;

    // =====================================================================
    // Состояние
    // =====================================================================
    protected bool isCrouching;
    protected bool isRunning;
    protected bool isGrounded = true;

    /// <summary>Поднят ли предмет (ПКМ).</summary>
    protected bool IsAiming { get; private set; }

    /// <summary>0 — в руках, 1 — поднято к лицу.</summary>
    protected float AimBlend { get; private set; }

    /// <summary>Ввод заблокирован (диалог, инвентарь, другое UI).</summary>
    protected bool InputBlocked { get; private set; }

    protected FirstPersonController Controller => fpsController;

    private FirstPersonController fpsController;
    private float aimProgress;

    private Vector3 swayPositionOffset;
    private Vector3 swayRotationOffset;
    private Vector2 mouseDelta;
    private Vector3 inertiaPosition;
    private Vector3 inertiaRotation;
    private Vector3 smoothMoveOffset;
    private Vector3 smoothMoveRotation;
    private Vector3 moveOffsetVelocity;
    private Vector3 moveRotVelocity;

    private Vector3 bobPosition;
    private Vector3 bobRotation;
    private Vector3 bobVelocity;
    private Vector3 bobRotVelocity;
    private float bobTime;
    private float idleSwayTimer;
    private float breathTimer;

    private Vector3 kickPosition;
    private Vector3 kickRotation;

    private bool poseOverrideActive;
    private Vector3 poseOverridePosition;
    private Vector3 poseOverrideRotation;
    private float poseOverrideWeight;

    private Renderer[] cachedRenderers;
    private bool initFailed;

    /// <summary>
    /// Пока true, новые HeldItem не цепляются к держателю в Awake.
    /// Нужно тому, кто копирует модель из рук как снаряд: копия иначе
    /// успела бы прыгнуть в руки игрока до того, как с неё снимут скрипты.
    /// Ставится на время одного Instantiate и сразу снимается.
    /// </summary>
    public static bool SuppressAttachOnAwake { get; set; }

    // =====================================================================
    // Жизненный цикл
    // =====================================================================
    protected virtual void Awake()
    {
        if (itemModel == null) itemModel = transform;

        ResolveCamera();
        if (playerCamera == null)
        {
            Debug.LogError($"[HeldItem] {name}: камера не найдена, предмет держать негде.");
            initFailed = true;
            enabled = false;
            return;
        }

        if (autoAttachToHolder && !SuppressAttachOnAwake) AttachToHolder();

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;   // предмет в руках — звук не пространственный
        }

        fpsController = FindObjectOfType<FirstPersonController>();
        cachedRenderers = GetComponentsInChildren<Renderer>(true);

        itemModel.localPosition = hipPosition;
        itemModel.localRotation = Quaternion.Euler(hipRotation);
    }

    protected virtual void Start()
    {
        StartCoroutine(ApplyCustomPose());
    }

    /// <summary>
    /// Поставить кастомную позу через кадр после старта.
    ///
    /// Кадр ожидания нужен, потому что в первом кадре держатель ещё двигает
    /// камеру (FirstPersonController выставляет высоту), а UpdatePose уже
    /// сглаживает предмет к старой позе — отсюда рывок при появлении в руках.
    /// Сначала сбрасываем трансформ в ноль: остатки позиции из сцены иначе
    /// складывались бы с кастомной позой.
    /// </summary>
    System.Collections.IEnumerator ApplyCustomPose()
    {
        if (!useCustomPose) yield break;

        // Кастомная поза становится основной, иначе UpdatePose в том же кадре
        // вернул бы предмет к hipPosition
        hipPosition = customPos;
        hipRotation = customRot;

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        yield return null;

        transform.localPosition = customPos;
        transform.localRotation = Quaternion.Euler(customRot);

        if (itemModel != null && itemModel != transform)
        {
            itemModel.localPosition = customPos;
            itemModel.localRotation = Quaternion.Euler(customRot);
        }
    }

    protected virtual void OnEnable()
    {
        // Предмет мог быть спрятан во время анимации — начинаем с чистой позы
        ClearPoseOverride();
        kickPosition = Vector3.zero;
        kickRotation = Vector3.zero;
        aimProgress = 0f;
        AimBlend = 0f;
        IsAiming = false;
        SetModelVisible(true);

        if (manageCrosshair) ApplyCrosshair(true);
    }

    protected virtual void OnDisable()
    {
        if (manageCrosshair) ApplyCrosshair(false);
    }

    protected virtual void Update()
    {
        if (initFailed || playerCamera == null || itemModel == null) return;

        InputBlocked = PlayerInputLock.WeaponsLocked
                       || (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive);

        SyncFromController();

        if (!InputBlocked)
        {
            if (useRightMouseAsAim) IsAiming = Input.GetMouseButton(1);
            HandleInput();
        }
        else
        {
            IsAiming = false;
        }

        UpdateAimBlend();
        UpdateSway();
        UpdateBob();
        UpdateBreathing();
        UpdatePose();

        if (manageCrosshair) ApplyCrosshair(true);
    }

    // =====================================================================
    // Наследникам
    // =====================================================================
    /// <summary>
    /// Обработка ввода предмета. Вызывается только когда ввод не заблокирован.
    /// Здесь наследник читает ЛКМ/ПКМ и запускает свои действия.
    /// </summary>
    protected abstract void HandleInput();

    /// <summary>
    /// Задать позу от анимации (замах, удар, бросок). Держится до ClearPoseOverride.
    /// weight — насколько анимация перебивает обычную позу.
    /// </summary>
    protected void SetPoseOverride(Vector3 position, Vector3 eulerRotation, float weight = 1f)
    {
        poseOverridePosition = position;
        poseOverrideRotation = eulerRotation;
        poseOverrideWeight = Mathf.Clamp01(weight);
        poseOverrideActive = poseOverrideWeight > 0.001f;
    }

    /// <summary>Вернуться к обычной позе в руках.</summary>
    protected void ClearPoseOverride()
    {
        poseOverrideActive = false;
        poseOverrideWeight = 0f;
    }

    /// <summary>Толчок предмета: отдача удара, вылет гранаты из руки.</summary>
    protected void AddKick(Vector3 positionKick, Vector3 rotationKick)
    {
        kickPosition += positionKick;
        kickRotation += rotationKick;
    }

    /// <summary>Спрятать/показать меши, не выключая сам объект и корутины.</summary>
    protected void SetModelVisible(bool visible)
    {
        if (cachedRenderers == null) return;
        foreach (Renderer r in cachedRenderers)
            if (r != null) r.enabled = visible;
    }

    /// <summary>Проиграть звук, если он задан.</summary>
    protected void PlaySound(AudioClip clip, float volume = 1f)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, volume);
    }

    /// <summary>Двигается ли игрок прямо сейчас.</summary>
    protected bool IsMoving()
    {
        if (fpsController != null && fpsController.characterController != null)
            return fpsController.characterController.velocity.magnitude > 0.5f;

        return Mathf.Abs(Input.GetAxis("Horizontal")) > 0.1f
               || Mathf.Abs(Input.GetAxis("Vertical")) > 0.1f;
    }

    /// <summary>Скорость игрока в мировых единицах (0, если контроллера нет).</summary>
    protected Vector3 PlayerVelocity =>
        fpsController != null && fpsController.characterController != null
            ? fpsController.characterController.velocity
            : Vector3.zero;

    // =====================================================================
    // Держатель
    // =====================================================================
    void ResolveCamera()
    {
        if (playerCamera != null) return;

        playerCamera = GetComponentInParent<Camera>();
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerCamera == null) playerCamera = FindObjectOfType<Camera>();
    }

    /// <summary>
    /// Перецепить предмет в руки. Без этого модель остаётся там, где её
    /// положили в сцене, — именно поэтому граната «появлялась в мире».
    /// </summary>
    public void AttachToHolder()
    {
        // Держатель ищется до Awake, если EquippableWeapon успел первым:
        // порядок Awake у компонентов одного объекта не определён
        if (itemModel == null) itemModel = transform;
        ResolveCamera();

        Transform target = holder != null ? holder : FindHolder();
        if (target == null)
        {
            Debug.LogWarning($"[HeldItem] {name}: держатель не найден — предмет останется на месте.");
            return;
        }

        holder = target;

        if (transform.parent != target)
        {
            // worldPositionStays: false — сохраняем локальный масштаб модели
            transform.SetParent(target, false);
            Debug.Log($"[HeldItem] {name} перецеплен в руки к «{target.name}».");
        }

        transform.localPosition = hipPosition;
        transform.localRotation = Quaternion.Euler(hipRotation);
    }

    Transform FindHolder()
    {
        // 1) Тот же родитель, что у ППШ: позы описаны в одной системе координат
        foreach (Wep w in FindObjectsOfType<Wep>(true))
        {
            if (w == null || w.transform.parent == null) continue;
            if (w.gameObject.scene.IsValid()) return w.transform.parent;
        }

        // 2) Явный держатель по имени
        if (!string.IsNullOrEmpty(holderObjectName))
        {
            GameObject named = GameObject.Find(holderObjectName);
            if (named != null) return named.transform;
        }

        // 3) Камера
        return playerCamera != null ? playerCamera.transform : null;
    }

    // =====================================================================
    // Поза и покачивание
    // =====================================================================
    void SyncFromController()
    {
        if (fpsController == null || fpsController.characterController == null) return;

        isGrounded = fpsController.isGrounded;
        isCrouching = fpsController.targetCameraY < fpsController.standingCameraHeight - 0.05f;
        isRunning = isGrounded &&
                    fpsController.characterController.velocity.magnitude > fpsController.walkSpeed + 0.5f;
    }

    void UpdateAimBlend()
    {
        float target = IsAiming ? 1f : 0f;
        aimProgress = Mathf.MoveTowards(aimProgress, target, Time.deltaTime * aimTransitionSpeed);
        AimBlend = Mathf.SmoothStep(0f, 1f, aimProgress);
    }

    void UpdateSway()
    {
        idleSwayTimer += Time.deltaTime * idleSwaySpeed;
        swayPositionOffset = new Vector3(
            Mathf.Sin(idleSwayTimer * 1.3f) * idleSwayAmount,
            Mathf.Cos(idleSwayTimer * 1.7f) * idleSwayAmount,
            Mathf.Sin(idleSwayTimer * 0.9f) * idleSwayAmount * 0.3f);
        swayRotationOffset = new Vector3(
            Mathf.Cos(idleSwayTimer * 1.5f) * idleSwayAmount * 4f,
            Mathf.Sin(idleSwayTimer * 1.1f) * idleSwayAmount * 3f,
            0f);

        float mouseX = Input.GetAxis("Mouse X") * swayAmount;
        float mouseY = Input.GetAxis("Mouse Y") * swayAmount;
        mouseDelta = Vector2.Lerp(mouseDelta, new Vector2(mouseX, mouseY), swayInertiaSmoothness);

        inertiaPosition = Vector3.Lerp(inertiaPosition, new Vector3(-mouseDelta.x, -mouseDelta.y, 0f),
                                       Time.deltaTime * swaySmoothness);
        inertiaRotation = Vector3.Lerp(inertiaRotation, new Vector3(mouseDelta.y, mouseDelta.x, 0f),
                                       Time.deltaTime * swaySmoothness);

        Vector3 targetMovePos = Vector3.zero;
        Vector3 targetMoveRot = Vector3.zero;

        if (fpsController != null && fpsController.characterController != null)
        {
            Vector3 localVel = fpsController.transform.InverseTransformDirection(
                fpsController.characterController.velocity);

            float walk = Mathf.Max(0.1f, fpsController.walkSpeed);
            float moveX = Mathf.Clamp(localVel.x / walk, -1f, 1f);
            float moveZ = Mathf.Clamp(localVel.z / walk, -1f, 1f);

            targetMovePos = new Vector3(
                -moveX * swayAmount * swayMultiplier,
                -moveZ * swayAmount * swayMultiplier * 0.5f,
                moveZ * swayAmount * swayMultiplier * 0.5f);
            targetMoveRot = new Vector3(
                moveZ * swayAmount * swayMultiplier * 5f,
                moveX * swayAmount * swayMultiplier * 5f,
                moveX * swayAmount * swayMultiplier * 3f);
        }

        smoothMoveOffset = Vector3.SmoothDamp(smoothMoveOffset, targetMovePos, ref moveOffsetVelocity, 0.1f);
        smoothMoveRotation = Vector3.SmoothDamp(smoothMoveRotation, targetMoveRot, ref moveRotVelocity, 0.1f);

        swayPositionOffset += inertiaPosition * swayMultiplier + smoothMoveOffset;
        swayRotationOffset += inertiaRotation * swayMultiplier * 15f + smoothMoveRotation;
    }

    void UpdateBob()
    {
        float speed = PlayerVelocity.magnitude;
        bool moving = speed > 0.5f;

        if (moving && isGrounded)
            bobTime += Time.deltaTime * (isRunning ? bobFrequencyRun : bobFrequencyWalk);

        Vector3 targetPos = Vector3.zero;
        Vector3 targetRot = Vector3.zero;

        if (moving && isGrounded)
        {
            float amp = isRunning ? bobAmplitudeRun : bobAmplitudeWalk;
            float t = bobTime * Mathf.PI * 2f;

            targetPos = new Vector3(
                Mathf.Sin(t * 2f) * amp * bobHorizontalMultiplier,
                Mathf.Sin(t) * amp * bobVerticalMultiplier,
                Mathf.Cos(t) * amp * 0.5f);
            targetRot = new Vector3(
                Mathf.Sin(t) * amp * 5f * bobRotationMultiplier,
                Mathf.Sin(t) * amp * 2f * bobRotationMultiplier,
                Mathf.Sin(t * 2f) * amp * 3f * bobRotationMultiplier);
        }
        else if (!isGrounded)
        {
            targetPos = new Vector3(0f, jumpOffsetY, jumpOffsetZ);
            targetRot = new Vector3(-4f, 0f, 0f);
        }

        if (isCrouching)
        {
            targetPos += new Vector3(0f, crouchOffsetY, crouchOffsetZ);
            targetRot += new Vector3(crouchRotationX, 0f, 0f);
        }

        bobPosition = Vector3.SmoothDamp(bobPosition, targetPos, ref bobVelocity, bobSmoothTime);
        bobRotation = Vector3.SmoothDamp(bobRotation, targetRot, ref bobRotVelocity, bobSmoothTime);
    }

    void UpdateBreathing()
    {
        breathTimer += Time.deltaTime * breathSpeed;
    }

    void UpdatePose()
    {
        Vector3 basePos = Vector3.Lerp(hipPosition, aimPosition, AimBlend);
        Vector3 baseRot = Vector3.Lerp(hipRotation, aimRotation, AimBlend);

        if (poseOverrideActive)
        {
            basePos = Vector3.Lerp(basePos, poseOverridePosition, poseOverrideWeight);
            baseRot = Vector3.Lerp(baseRot, poseOverrideRotation, poseOverrideWeight);
        }

        // Поднятый предмет качается меньше, во время анимации — почти не качается
        float motion = 1f - AimBlend * 0.7f;
        if (poseOverrideActive) motion *= 1f - poseOverrideWeight * animationMotionDamping;

        Vector3 offsetPos = (swayPositionOffset + bobPosition + kickPosition) * motion;
        Vector3 offsetRot = (swayRotationOffset + bobRotation + kickRotation) * motion;

        offsetPos += new Vector3(0f, breathAmplitude * Mathf.Sin(breathTimer), 0f) * motion;

        Vector3 targetPos = basePos + offsetPos;
        Quaternion targetRot = Quaternion.Euler(baseRot) * Quaternion.Euler(offsetRot);

        float speed = poseOverrideActive
            ? Mathf.Lerp(followSpeed, animationFollowSpeed, poseOverrideWeight)
            : followSpeed;

        float k = 1f - Mathf.Exp(-speed * Time.deltaTime);
        itemModel.localPosition = Vector3.Lerp(itemModel.localPosition, targetPos, k);
        itemModel.localRotation = Quaternion.Slerp(itemModel.localRotation, targetRot, k);

        float decay = 1f - Mathf.Exp(-22f * Time.deltaTime);
        kickPosition = Vector3.Lerp(kickPosition, Vector3.zero, decay);
        kickRotation = Vector3.Lerp(kickRotation, Vector3.zero, decay);
    }

    // =====================================================================
    // Прицел
    // =====================================================================
    void ApplyCrosshair(bool itemInHands)
    {
        if (crosshairObject == null)
        {
            // Перекрестие принадлежит ППШ. Берём его, иначе после смены оружия
            // прицел остался бы спрятанным: Wep выключен и никто его не включит.
            foreach (Wep w in FindObjectsOfType<Wep>(true))
            {
                if (w != null && w.crosshairObject != null) { crosshairObject = w.crosshairObject; break; }
            }
            if (crosshairObject == null) return;
        }

        bool show = itemInHands && !InputBlocked && !IsAiming;
        if (crosshairObject.activeSelf != show) crosshairObject.SetActive(show);
    }

    // =====================================================================
    // Подгонка позы из инспектора
    // =====================================================================
#if UNITY_EDITOR
    [ContextMenu("Запомнить текущую позу как «в руках»")]
    void CaptureHipPose()
    {
        hipPosition = transform.localPosition;
        hipRotation = transform.localEulerAngles;
        customPos = hipPosition;
        customRot = hipRotation;
        Debug.Log($"[HeldItem] {name}: поза «в руках» = {hipPosition} / {hipRotation}");
    }

    [ContextMenu("Запомнить текущую позу как «поднято» (ПКМ)")]
    void CaptureAimPose()
    {
        aimPosition = transform.localPosition;
        aimRotation = transform.localEulerAngles;
        Debug.Log($"[HeldItem] {name}: поза «поднято» = {aimPosition} / {aimRotation}");
    }

    [ContextMenu("Поставить модель в позу «в руках»")]
    void PreviewHipPose()
    {
        transform.localPosition = hipPosition;
        transform.localEulerAngles = hipRotation;
    }
#endif
}
