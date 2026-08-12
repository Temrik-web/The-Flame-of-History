using UnityEngine;
using System.Collections;

public class Wep : MonoBehaviour
{
    [Header("Характеристики оружия")]
    public int maxAmmo = 71;
    public int currentAmmo;
    public int spareMagazines = 2;
    public float fireRate = 0.066f;
    public float reloadTime = 4f;
    public float damage = 25f;

    [Header("Разброс")]
    public float baseSpread = 5f;                // для hip
    public float autoSpreadPerShot = 1f;
    public float maxSpread = 14f;
    public float spreadRecoverySpeed = 1.2f;
    private float currentSpread;

    [Header("Прицеливание")]
    public float normalFOV = 60f;
    public float aimFOV = 40f;
    public float aimSpreadMultiplier = 0.7f;     // в прицеле разброс меньше
    public float aimRecoilMultiplier = 1.3f;     // но отдача сильнее
    public Vector3 hipPosition = new Vector3(0.3f, -0.25f, 0.5f);
    public Vector3 aimPosition = new Vector3(-0.295f, -0.529f, 0.248f);
    public Vector3 hipRotation = new Vector3(0f, 0f, 0f);
    public Vector3 aimRotation = new Vector3(0.507f, -87.974f, 2.263f);
    public float aimTransitionSpeed = 9f;
    private bool isAiming = false;

    [Header("Отдача (плавная, без возврата во время зажима)")]
    public AnimationCurve verticalRecoilCurve = new AnimationCurve(
        new Keyframe(0, 1.8f), new Keyframe(2, 2.5f), new Keyframe(5, 3.5f), new Keyframe(10, 4.0f)
    );
    public AnimationCurve horizontalRecoilCurve = new AnimationCurve(
        new Keyframe(0, 0.3f), new Keyframe(2, 0.5f), new Keyframe(5, 0.7f), new Keyframe(10, 0.8f)
    );
    public float maxRecoilAngle = 30f;
    public float recoilRecoveryDelay = 0.08f;    // задержка перед началом возврата
    public float recoilRecoverySpeed = 2.5f;     // скорость возврата после зажима

    private int consecutiveShots = 0;
    private float lastShotTime;
    private float timeOfLastShot = -10f;
    private Quaternion recoilAddedRotation = Quaternion.identity; // накопленная отдача

    [Header("Шатание и тремор")]
    public float swayAmount = 0.03f;
    public float swaySmoothness = 3.5f;
    public float moveSwayMultiplier = 2.2f;
    public float idleSwaySpeed = 0.8f;
    public float idleSwayAmount = 0.008f;
    public float tremorAmount = 0.003f;
    public float tremorSpeed = 14f;
    private Vector3 swayPositionOffset = Vector3.zero;
    private Vector3 swayRotationOffset = Vector3.zero;
    private Vector3 tremorPosition = Vector3.zero;
    private Vector3 tremorRotation = Vector3.zero;
    private float idleSwayTimer = 0f;
    private float tremorTimer = 0f;

    // Толчок модели
    private Vector3 recoilKickPosition = Vector3.zero;
    private Vector3 recoilKickRotation = Vector3.zero;

    [Header("Режимы огня")]
    public FireMode currentFireMode = FireMode.Auto;

    [Header("Состояние")]
    private bool isReloading = false;
    public bool IsReloading => isReloading;

    [Header("Ссылки")]
    public Camera playerCamera;
    public Transform weaponModel;
    public Transform muzzlePoint;
    public ParticleSystem muzzleFlash;
    public GameObject[] bulletHolePrefabs;
    public LineRenderer bulletTrailPrefab;
    public bool preventOverlappingHoles = false;
    public float holeMinDistance = 0.05f;
    public LayerMask holeCheckMask = ~0;

    private Transform cameraHolder;
    private Transform recoilPivot;
    private Coroutine reloadRoutine;
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
        cameraHolder = playerCamera.transform.parent;
        if (cameraHolder == null)
        {
            Debug.LogError("[Gun] Камера должна быть внутри CameraHolder!");
            initFailed = true;
            enabled = false;
            return;
        }

        recoilPivot = cameraHolder.Find("RecoilPivot");
        if (recoilPivot == null)
        {
            GameObject pivotGO = new GameObject("RecoilPivot");
            pivotGO.transform.SetParent(cameraHolder, false);
            recoilPivot = pivotGO.transform;
        }
        playerCamera.transform.SetParent(recoilPivot, false);
        playerCamera.transform.localPosition = Vector3.zero;
        playerCamera.transform.localRotation = Quaternion.identity;

        if (muzzlePoint == null)
        {
            GameObject mp = new GameObject("MuzzlePoint");
            mp.transform.SetParent(weaponModel, false);
            mp.transform.localPosition = new Vector3(0, 0, 0.5f);
            muzzlePoint = mp.transform;
        }

        weaponModel.localPosition = hipPosition;
        weaponModel.localRotation = Quaternion.Euler(hipRotation);
    }

    void Update()
    {
        if (initFailed || playerCamera == null || recoilPivot == null || weaponModel == null) return;

        isAiming = Input.GetMouseButton(1);
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, isAiming ? aimFOV : normalFOV, Time.deltaTime * 10f);

        // Сброс счётчика очереди после паузы
        if (Time.time - timeOfLastShot > 0.25f)
            consecutiveShots = 0;

        UpdateTremor();
        UpdateSway();

        Vector3 targetPos = (isAiming ? aimPosition : hipPosition) + swayPositionOffset + tremorPosition + recoilKickPosition;
        Quaternion targetRot = Quaternion.Euler(isAiming ? aimRotation : hipRotation)
                               * Quaternion.Euler(swayRotationOffset + tremorRotation + recoilKickRotation);

        weaponModel.localPosition = Vector3.Lerp(weaponModel.localPosition, targetPos, Time.deltaTime * aimTransitionSpeed);
        weaponModel.localRotation = Quaternion.Slerp(weaponModel.localRotation, targetRot, Time.deltaTime * aimTransitionSpeed);

        // Затухание толчка модели
        recoilKickPosition = Vector3.Lerp(recoilKickPosition, Vector3.zero, Time.deltaTime * 12f);
        recoilKickRotation = Vector3.Lerp(recoilKickRotation, Vector3.zero, Time.deltaTime * 12f);

        // Стрельба
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

        // Восстановление разброса (всегда)
        if (Time.time - lastShotTime > fireRate * 2f)
            currentSpread = Mathf.MoveTowards(currentSpread, baseSpread, spreadRecoverySpeed * Time.deltaTime);
    }

    void LateUpdate()
    {
        if (recoilPivot == null) return;

        // Возврат отдачи только если прошло время после последнего выстрела
        if (Time.time - timeOfLastShot > recoilRecoveryDelay)
        {
            // Плавно возвращаем к исходному повороту
            recoilPivot.localRotation = Quaternion.Slerp(recoilPivot.localRotation, Quaternion.identity, Time.deltaTime * recoilRecoverySpeed);
        }
        // Во время зажима не делаем ничего — отдача не возвращается
    }

    void Shoot()
    {
        if (currentAmmo <= 0 || muzzlePoint == null || recoilPivot == null || playerCamera == null) return;

        currentAmmo--;
        timeOfLastShot = Time.time;
        lastShotTime = Time.time;
        consecutiveShots++;

        if (muzzleFlash != null) muzzleFlash.Play();

        // Значения из кривых
        float vertStrength = verticalRecoilCurve.Evaluate(consecutiveShots);
        float horizStrength = horizontalRecoilCurve.Evaluate(consecutiveShots);

        // Усиливаем отдачу в прицеле
        if (isAiming)
        {
            vertStrength *= aimRecoilMultiplier;
            horizStrength *= aimRecoilMultiplier;
        }

        float vert = Random.Range(vertStrength * 0.9f, vertStrength * 1.1f);
        float horiz = Random.Range(-horizStrength, horizStrength);

        // Добавляем отдачу к текущему повороту pivot
        Quaternion deltaRotation = Quaternion.Euler(-vert, horiz, 0);
        recoilPivot.localRotation *= deltaRotation;

        // Ограничиваем максимальный угол (чтобы не улетело за пределы)
        float angle = Quaternion.Angle(Quaternion.identity, recoilPivot.localRotation);
        if (angle > maxRecoilAngle)
        {
            // Приводим к максимальному углу, сохраняя направление
            recoilPivot.localRotation = Quaternion.Slerp(Quaternion.identity, recoilPivot.localRotation, maxRecoilAngle / angle);
        }

        // Толчок модели (визуальный эффект)
        float kickPosY = isAiming ? 0.025f : 0.02f;
        float kickPosZ = isAiming ? -0.04f : -0.03f;
        float kickRotX = isAiming ? -4f : -3f;
        recoilKickPosition += new Vector3(0f, Random.Range(kickPosY * 0.7f, kickPosY), Random.Range(kickPosZ * 0.7f, kickPosZ));
        recoilKickRotation += new Vector3(Random.Range(kickRotX * 0.8f, kickRotX), Random.Range(-0.8f, 0.8f), 0f);

        // Разброс
        float effectiveSpread = currentSpread * (isAiming ? aimSpreadMultiplier : 1f);
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
            amp *= 1.3f;

        tremorPosition.x = Mathf.Sin(tremorTimer * 1.7f) * amp;
        tremorPosition.y = Mathf.Cos(tremorTimer * 1.9f) * amp;
        tremorPosition.z = Mathf.Sin(tremorTimer * 1.3f) * amp * 0.5f;

        tremorRotation.x = Mathf.Cos(tremorTimer * 2.1f) * amp * 8f;
        tremorRotation.y = Mathf.Sin(tremorTimer * 1.5f) * amp * 6f;
        tremorRotation.z = 0f;
    }

    void UpdateSway()
    {
        float mouseX = Input.GetAxis("Mouse X") * swayAmount;
        float mouseY = Input.GetAxis("Mouse Y") * swayAmount;
        float moveX = Input.GetAxis("Horizontal") * swayAmount * moveSwayMultiplier;
        float moveY = Input.GetAxis("Vertical") * swayAmount * moveSwayMultiplier;

        Vector3 targetPos = new Vector3(-mouseX - moveX, -mouseY, moveY);
        Vector3 targetRot = new Vector3(mouseY, mouseX, moveX) * 10f;

        idleSwayTimer += Time.deltaTime * idleSwaySpeed;
        targetPos += new Vector3(Mathf.Sin(idleSwayTimer * 1.3f), Mathf.Cos(idleSwayTimer * 1.7f), 0) * idleSwayAmount;

        float aimFactor = isAiming ? 0.3f : 1f;
        swayPositionOffset = Vector3.Lerp(swayPositionOffset, targetPos * aimFactor, Time.deltaTime * swaySmoothness);
        swayRotationOffset = Vector3.Lerp(swayRotationOffset, targetRot * aimFactor, Time.deltaTime * swaySmoothness);
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