using UnityEngine;
using System.Collections;
using EasyPeasyFirstPersonController;

public class Wep : MonoBehaviour
{
    [Header("Характеристики оружия")]
    public int maxAmmo = 71;
    public int currentAmmo;
    public int spareMagazines = 2;
    public float fireRate = 0.066f;
    public float reloadTime = 4f;
    public float damage = 25f;

    [Header("Разброс (динамический)")]
    public float baseSpread = 10f;
    public float autoSpreadPerShot = 2.5f;
    public float maxSpread = 30f;
    public float spreadRecoverySpeed = 0.6f;
    private float currentSpread;

    [Header("Прицеливание")]
    public float normalFOV = 60f;
    public float aimFOV = 40f;
    public float aimSpreadMultiplier = 0.6f;
    public float aimRecoilMultiplier = 1.2f;
    public Vector3 hipPosition = new Vector3(0.1186829f, -0.7355555f, 0.154068f);
    public Vector3 aimPosition = new Vector3(-0.298996f, -0.526f, 0.052f);
    public Vector3 hipRotation = new Vector3(-3.157f, -80.27f, -0.661f);
    public Vector3 aimRotation = new Vector3(0f, -90f, -0.285f);
    public float aimTransitionSpeed = 10f;
    private bool isAiming = false;

    [Header("Отдача (передаётся в контроллер)")]
    public AnimationCurve verticalRecoilCurve = new AnimationCurve(
        new Keyframe(0, 1.2f), new Keyframe(2, 2.0f), new Keyframe(5, 3.5f), new Keyframe(10, 5.0f)
    );
    public AnimationCurve horizontalRecoilCurve = new AnimationCurve(
        new Keyframe(0, 0.3f), new Keyframe(2, 0.5f), new Keyframe(5, 0.8f), new Keyframe(10, 1.2f)
    );
    private int consecutiveShots = 0;
    private float lastShotTime;
    private float timeOfLastShot = -10f;

    [Header("Ссылки")]
    public Camera playerCamera;
    public Transform weaponModel;
    public Transform muzzlePoint;
    public ParticleSystem muzzleFlash;
    public GameObject[] bulletHolePrefabs;
    public LineRenderer bulletTrailPrefab;
    public GameObject crosshairObject;
    public bool preventOverlappingHoles = false;
    public float holeMinDistance = 0.05f;
    public LayerMask holeCheckMask = ~0;

    [Header("Состояния движения (авто из контроллера)")]
    public bool isCrouching = false;
    public bool isRunning = false;
    public bool isGrounded = true;

    [Header("Анимации оружия")]
    public float swayAmount = 0.08f;
    public float swaySmoothness = 5f;
    public float moveSwayMultiplier = 3f;
    public float idleSwaySpeed = 1.2f;
    public float idleSwayAmount = 0.02f;
    public float tremorAmount = 0.008f;
    public float tremorSpeed = 20f;
    public float bobAmplitudeWalk = 0.08f;
    public float bobAmplitudeRun = 0.15f;
    public float bobFrequencyWalk = 10f;
    public float bobFrequencyRun = 16f;
    public float bobSmoothTime = 0.08f;
    public float jumpBobOffsetY = -0.1f;
    public float jumpBobOffsetZ = 0.06f;
    public float kickbackPositionY = 0.06f;
    public float kickbackPositionZ = -0.08f;
    public float kickbackRotationX = -10f;
    public float kickbackRotationY = 1.5f;

    // Анимация приседания
    public float crouchPositionOffsetY = -0.06f;
    public float crouchPositionOffsetZ = 0.03f;
    public float crouchRotationOffsetX = 5f;

    private Vector3 swayPositionOffset = Vector3.zero;
    private Vector3 swayRotationOffset = Vector3.zero;
    private Vector3 tremorPosition = Vector3.zero;
    private Vector3 tremorRotation = Vector3.zero;
    private Vector3 bobPosition = Vector3.zero;
    private Vector3 bobRotation = Vector3.zero;
    private Vector3 bobVelocity = Vector3.zero;
    private Vector3 bobRotVelocity = Vector3.zero;
    private float idleSwayTimer = 0f;
    private float tremorTimer = 0f;
    private float bobTimer = 0f;

    private Vector3 recoilKickPosition = Vector3.zero;
    private Vector3 recoilKickRotation = Vector3.zero;

    [Header("Режимы огня")]
    public FireMode currentFireMode = FireMode.Auto;
    private bool isReloading = false;
    public bool IsReloading => isReloading;

    private FirstPersonController fpsController;
    private Coroutine reloadRoutine;   // <-- ДОБАВЛЕНО
    private bool initFailed = false;

    public enum FireMode { Semi, Auto }

    void Start()
    {
        currentFireMode = FireMode.Auto;
        currentAmmo = maxAmmo;
        currentSpread = baseSpread;
        lastShotTime = -fireRate;

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null) playerCamera = GetComponentInParent<Camera>();
            if (playerCamera == null) playerCamera = FindObjectOfType<Camera>();
        }
        if (playerCamera == null)
        {
            Debug.LogError("[Gun] Камера не найдена!");
            initFailed = true;
            enabled = false;
            return;
        }

        if (weaponModel == null) weaponModel = transform;

        if (muzzlePoint == null)
        {
            GameObject mp = new GameObject("MuzzlePoint");
            mp.transform.SetParent(weaponModel, false);
            mp.transform.localPosition = new Vector3(0, 0, 0.5f);
            muzzlePoint = mp.transform;
        }

        weaponModel.localPosition = hipPosition;
        weaponModel.localRotation = Quaternion.Euler(hipRotation);

        fpsController = FindObjectOfType<FirstPersonController>();
        if (fpsController == null)
            Debug.LogWarning("[Gun] FirstPersonController не найден. Отдача не будет работать.");
    }

    void Update()
    {
        if (initFailed || playerCamera == null || weaponModel == null) return;

        if (fpsController != null)
            SyncFromController();

        isAiming = Input.GetMouseButton(1);
        if (crosshairObject != null)
            crosshairObject.SetActive(!isAiming);

        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, isAiming ? aimFOV : normalFOV, Time.deltaTime * 10f);

        if (Time.time - timeOfLastShot > 0.25f)
            consecutiveShots = 0;

        UpdateTremor();
        UpdateSway();
        UpdateBob();

        Vector3 targetPos = (isAiming ? aimPosition : hipPosition) + swayPositionOffset + tremorPosition + bobPosition + recoilKickPosition;
        Quaternion targetRot = Quaternion.Euler(isAiming ? aimRotation : hipRotation)
                               * Quaternion.Euler(swayRotationOffset + tremorRotation + bobRotation + recoilKickRotation);

        weaponModel.localPosition = Vector3.Lerp(weaponModel.localPosition, targetPos, Time.deltaTime * aimTransitionSpeed);
        weaponModel.localRotation = Quaternion.Slerp(weaponModel.localRotation, targetRot, Time.deltaTime * aimTransitionSpeed);

        recoilKickPosition = Vector3.Lerp(recoilKickPosition, Vector3.zero, Time.deltaTime * 15f);
        recoilKickRotation = Vector3.Lerp(recoilKickRotation, Vector3.zero, Time.deltaTime * 15f);

        bool fireHeld = Input.GetMouseButton(0);
        bool fireDown = Input.GetMouseButtonDown(0);

        if (!isReloading && Time.time - lastShotTime >= fireRate)
        {
            if (currentFireMode == FireMode.Auto && fireHeld)
                Shoot();
            else if (currentFireMode == FireMode.Semi && fireDown)
                Shoot();
        }

        if (Input.GetKeyDown(KeyCode.R) && !isReloading && currentAmmo < maxAmmo && spareMagazines > 0)
            reloadRoutine = StartCoroutine(Reload());

        if (Input.GetKeyDown(KeyCode.V))
            currentFireMode = (currentFireMode == FireMode.Auto) ? FireMode.Semi : FireMode.Auto;

        if (Time.time - lastShotTime > fireRate * 2f)
            currentSpread = Mathf.MoveTowards(currentSpread, baseSpread, spreadRecoverySpeed * Time.deltaTime);
    }

    void LateUpdate()
    {
        // Ничего
    }

    void SyncFromController()
    {
        isGrounded = fpsController.isGrounded;
        isCrouching = fpsController.targetCameraY < fpsController.standingCameraHeight - 0.05f;
        isRunning = isGrounded && fpsController.characterController.velocity.magnitude > fpsController.walkSpeed + 0.5f;
    }

    void Shoot()
    {
        if (currentAmmo <= 0 || muzzlePoint == null || playerCamera == null) return;

        currentAmmo--;
        timeOfLastShot = Time.time;
        lastShotTime = Time.time;
        consecutiveShots++;

        if (muzzleFlash != null) muzzleFlash.Play();

        float recoilMultiplier = 1f;
        float spreadMultiplier = 1f;

        if (isCrouching) { recoilMultiplier *= 0.7f; spreadMultiplier *= 0.7f; }
        if (isRunning) { recoilMultiplier *= 1.5f; spreadMultiplier *= 1.6f; }
        if (!isGrounded) { recoilMultiplier *= 1.8f; spreadMultiplier *= 2.0f; }
        if (isAiming) { recoilMultiplier *= aimRecoilMultiplier; spreadMultiplier *= aimSpreadMultiplier; }

        float vertStrength = verticalRecoilCurve.Evaluate(consecutiveShots) * recoilMultiplier;
        float horizStrength = horizontalRecoilCurve.Evaluate(consecutiveShots) * recoilMultiplier;

        float vert = Random.Range(vertStrength * 0.9f, vertStrength * 1.1f);
        float horiz = Random.Range(-horizStrength, horizStrength);

        // Добавляем отдачу в контроллер
        if (fpsController != null)
            fpsController.AddRecoil(vert, horiz);

        // Толчок модели оружия
        float kickY = kickbackPositionY * recoilMultiplier;
        float kickZ = kickbackPositionZ * recoilMultiplier;
        float kickRotX = kickbackRotationX * recoilMultiplier;
        float kickRotY = Random.Range(-kickbackRotationY, kickbackRotationY) * recoilMultiplier;

        recoilKickPosition += new Vector3(0, Random.Range(kickY * 0.7f, kickY), Random.Range(kickZ * 0.7f, kickZ));
        recoilKickRotation += new Vector3(Random.Range(kickRotX * 0.8f, kickRotX), kickRotY, 0);

        // Разброс
        float effectiveSpread = currentSpread * spreadMultiplier;
        Vector3 direction = GetSpreadDirection(effectiveSpread);

        Debug.DrawRay(playerCamera.transform.position, direction * 100f, Color.red, 0.05f);

        if (Physics.Raycast(playerCamera.transform.position, direction, out RaycastHit hit, 500f))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
            if (enemy != null) enemy.TakeDamage(damage);

            if (bulletHolePrefabs != null && bulletHolePrefabs.Length > 0)
            {
                if (preventOverlappingHoles && !CanPlaceHole(hit.point)) return;

                Quaternion holeRot = Quaternion.FromToRotation(Vector3.up, hit.normal)
                                     * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                GameObject hole = Instantiate(bulletHolePrefabs[Random.Range(0, bulletHolePrefabs.Length)],
                                              hit.point + hit.normal * 0.02f, holeRot);
                hole.tag = "BulletHole";
                hole.transform.SetParent(hit.collider.transform);
            }

            if (bulletTrailPrefab != null)
            {
                LineRenderer trail = Instantiate(bulletTrailPrefab, muzzlePoint.position, Quaternion.identity);
                trail.SetPosition(0, muzzlePoint.position);
                trail.SetPosition(1, hit.point);
                Destroy(trail.gameObject, 0.05f);
            }
        }
        else if (bulletTrailPrefab != null)
        {
            LineRenderer trail = Instantiate(bulletTrailPrefab, muzzlePoint.position, Quaternion.identity);
            trail.SetPosition(0, muzzlePoint.position);
            trail.SetPosition(1, muzzlePoint.position + direction * 500f);
            Destroy(trail.gameObject, 0.05f);
        }

        if (currentFireMode == FireMode.Auto)
            currentSpread = Mathf.Min(currentSpread + autoSpreadPerShot, maxSpread);
        else
            currentSpread = baseSpread;
    }

    void UpdateTremor()
    {
        tremorTimer += Time.deltaTime * tremorSpeed;
        float amp = tremorAmount;
        if (currentFireMode == FireMode.Auto && Input.GetMouseButton(0) && !isReloading)
            amp *= 1.5f;

        tremorPosition.x = Mathf.Sin(tremorTimer * 1.7f) * amp;
        tremorPosition.y = Mathf.Cos(tremorTimer * 1.9f) * amp;
        tremorPosition.z = Mathf.Sin(tremorTimer * 1.3f) * amp * 0.5f;

        tremorRotation.x = Mathf.Cos(tremorTimer * 2.1f) * amp * 10f;
        tremorRotation.y = Mathf.Sin(tremorTimer * 1.5f) * amp * 8f;
        tremorRotation.z = 0f;
    }

    void UpdateSway()
    {
        float mouseX = Input.GetAxis("Mouse X") * swayAmount;
        float mouseY = Input.GetAxis("Mouse Y") * swayAmount;
        float moveX = Input.GetAxis("Horizontal") * swayAmount * moveSwayMultiplier;
        float moveY = Input.GetAxis("Vertical") * swayAmount * moveSwayMultiplier;

        Vector3 targetPos = new Vector3(-mouseX - moveX, -mouseY, moveY);
        Vector3 targetRot = new Vector3(mouseY, mouseX, moveX) * 15f;

        idleSwayTimer += Time.deltaTime * idleSwaySpeed;
        targetPos += new Vector3(Mathf.Sin(idleSwayTimer * 1.3f), Mathf.Cos(idleSwayTimer * 1.7f), 0) * idleSwayAmount;

        float aimFactor = isAiming ? 0.4f : 1f;
        swayPositionOffset = Vector3.Lerp(swayPositionOffset, targetPos * aimFactor, Time.deltaTime * swaySmoothness);
        swayRotationOffset = Vector3.Lerp(swayRotationOffset, targetRot * aimFactor, Time.deltaTime * swaySmoothness);
    }

    void UpdateBob()
    {
        float inputX = Input.GetAxis("Horizontal");
        float inputY = Input.GetAxis("Vertical");
        bool isMoving = (Mathf.Abs(inputX) > 0.1f || Mathf.Abs(inputY) > 0.1f);

        Vector3 targetBobPos = Vector3.zero;
        Vector3 targetBobRot = Vector3.zero;

        if (isMoving && isGrounded)
        {
            bobTimer += Time.deltaTime * (isRunning ? bobFrequencyRun : bobFrequencyWalk);
            float amp = isRunning ? bobAmplitudeRun : bobAmplitudeWalk;

            targetBobPos = new Vector3(
                Mathf.Sin(bobTimer * 2f) * amp * 0.7f,
                -Mathf.Abs(Mathf.Sin(bobTimer)) * amp,
                Mathf.Sin(bobTimer) * amp * 0.5f
            );
            targetBobRot = new Vector3(
                Mathf.Sin(bobTimer) * amp * 8f,
                0f,
                Mathf.Sin(bobTimer * 2f) * amp * 5f
            );
        }
        else if (!isGrounded)
        {
            targetBobPos = new Vector3(0f, jumpBobOffsetY, jumpBobOffsetZ);
            targetBobRot = new Vector3(-10f, 0f, 0f);
        }

        if (isCrouching)
        {
            targetBobPos += new Vector3(0f, crouchPositionOffsetY, crouchPositionOffsetZ);
            targetBobRot += new Vector3(crouchRotationOffsetX, 0f, 0f);
        }

        bobPosition = Vector3.SmoothDamp(bobPosition, targetBobPos, ref bobVelocity, bobSmoothTime);
        bobRotation = Vector3.SmoothDamp(bobRotation, targetBobRot, ref bobRotVelocity, bobSmoothTime);
    }

    bool CanPlaceHole(Vector3 point)
    {
        foreach (Collider col in Physics.OverlapSphere(point, holeMinDistance, holeCheckMask))
            if (col.CompareTag("BulletHole")) return false;
        return true;
    }

    Vector3 GetSpreadDirection(float spreadDegrees)
    {
        Vector3 baseDir = playerCamera.transform.forward;
        float half = spreadDegrees * Mathf.Deg2Rad / 2f;
        Vector2 circle = Random.insideUnitCircle * Mathf.Tan(half);
        return Quaternion.Euler(circle.y, circle.x, 0) * baseDir;
    }

    IEnumerator Reload()
    {
        isReloading = true;
        yield return new WaitForSeconds(reloadTime);
        spareMagazines--;
        currentAmmo = maxAmmo;
        currentSpread = baseSpread;
        isReloading = false;
        reloadRoutine = null;
    }

    void OnGUI()
    {
        if (initFailed) return;
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = Color.white }, fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(15, 15, 400, 30), $"Патроны: {currentAmmo}/{maxAmmo}" + (isReloading ? " (перезарядка...)" : ""));
        GUI.Label(new Rect(15, 45, 400, 30), "Режим: " + (currentFireMode == FireMode.Auto ? "Авто" : "Одиночный"));
        GUI.Label(new Rect(15, Screen.height - 40, 400, 30), "R - перезарядка | V - режим огня");
    }

    void OnDisable()
    {
        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }
        isReloading = false;
    }
}