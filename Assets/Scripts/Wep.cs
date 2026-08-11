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
    public float baseSpread = 2f;
    public float autoSpreadPerShot = 0.3f;
    public float maxSpread = 8f;
    public float spreadRecoverySpeed = 2f;   // скорость возврата разброса к базовому
    private float currentSpread;

    [Header("Прицеливание")]
    public float normalFOV = 60f;
    public float aimFOV = 40f;
    public float aimSpreadMultiplier = 0.5f;
    public Vector3 hipPosition = new Vector3(0.3000031f, -0.25f, 0.5f);
    public Vector3 aimPosition = new Vector3(0f, -0.2f, 0.35f);
    public float aimPositionSpeed = 10f;
    private bool isAiming = false;

    [Header("Отдача")]
    public float recoilVerticalMin = 0.2f;
    public float recoilVerticalMax = 0.5f;
    public float recoilHorizontal = 0.1f;
    public float recoilRecoverySpeed = 8f;
    public float maxRecoilAngle = 15f;

    [Header("Состояние")]
    public bool isAuto = true;
    private bool isReloading = false;
    private float lastShotTime;

    [Header("Ссылки")]
    public Camera playerCamera;
    public Transform muzzlePoint;
    public ParticleSystem muzzleFlash;
    public GameObject bulletHolePrefab;
    public LineRenderer bulletTrailPrefab;

    private Transform cameraHolder;
    private Transform recoilPivot;
    private Coroutine reloadRoutine;

    void Start()
    {
        currentAmmo = maxAmmo;
        currentSpread = baseSpread;
        lastShotTime = -fireRate;               // позволит выстрелить сразу

        if (playerCamera == null)
            playerCamera = Camera.main;

        if (playerCamera == null)
        {
            Debug.LogError("PlayerCamera not assigned and no MainCamera found! Gun disabled.");
            enabled = false;
            return;
        }

        cameraHolder = playerCamera.transform.parent;
        if (cameraHolder == null)
        {
            Debug.LogError("CameraHolder (parent of camera) not found! Gun disabled.");
            enabled = false;
            return;
        }

        // Создаём или находим RecoilPivot
        recoilPivot = cameraHolder.Find("RecoilPivot");
        if (recoilPivot == null)
        {
            GameObject pivotGO = new GameObject("RecoilPivot");
            pivotGO.transform.SetParent(cameraHolder, false);
            pivotGO.transform.localPosition = Vector3.zero;
            pivotGO.transform.localRotation = Quaternion.identity;
            recoilPivot = pivotGO.transform;
        }

        // Переносим камеру внутрь RecoilPivot, только если она ещё не там
        if (playerCamera.transform.parent != recoilPivot)
        {
            playerCamera.transform.SetParent(recoilPivot, false);
            playerCamera.transform.localPosition = Vector3.zero;
            playerCamera.transform.localRotation = Quaternion.identity;
        }

        if (muzzlePoint == null)
        {
            GameObject mp = new GameObject("MuzzlePoint");
            mp.transform.SetParent(transform, false);
            mp.transform.localPosition = new Vector3(0, 0, 0.5f);
            muzzlePoint = mp.transform;
        }

        transform.localPosition = hipPosition;
    }

    void Update()
    {
        // Проверка жизненно важных ссылок
        if (playerCamera == null || recoilPivot == null)
            return;

        // Прицеливание
        isAiming = Input.GetMouseButton(1);
        playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, isAiming ? aimFOV : normalFOV, Time.deltaTime * 10f);
        Vector3 targetPos = isAiming ? aimPosition : hipPosition;
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * aimPositionSpeed);

        // Стрельба
        if (isAuto)
        {
            if (Input.GetMouseButton(0) && Time.time - lastShotTime >= fireRate && !isReloading)
                Shoot();
        }
        else
        {
            if (Input.GetMouseButtonDown(0) && Time.time - lastShotTime >= fireRate && !isReloading)
                Shoot();
        }

        // Перезарядка
        if (Input.GetKeyDown(KeyCode.R) && !isReloading && currentAmmo < maxAmmo && spareMagazines > 0)
            reloadRoutine = StartCoroutine(Reload());

        // Переключение режима огня
        if (Input.GetKeyDown(KeyCode.Alpha1)) isAuto = false;
        if (Input.GetKeyDown(KeyCode.Alpha2)) isAuto = true;

        // Плавное уменьшение разброса, когда не стреляем
        if (Time.time - lastShotTime > fireRate * 2f)   // небольшая пауза перед началом восстановления
        {
            currentSpread = Mathf.MoveTowards(currentSpread, baseSpread, spreadRecoverySpeed * Time.deltaTime);
        }
    }

    void LateUpdate()
    {
        // Плавный возврат отдачи
        if (recoilPivot != null)
        {
            recoilPivot.localRotation = Quaternion.Slerp(recoilPivot.localRotation, Quaternion.identity, recoilRecoverySpeed * Time.deltaTime);
        }
    }

    void Shoot()
    {
        if (currentAmmo <= 0 || playerCamera == null || muzzlePoint == null || recoilPivot == null)
            return;

        currentAmmo--;
        lastShotTime = Time.time;

        // Вспышка
        if (muzzleFlash != null) muzzleFlash.Play();

        // Отдача – добавляем поворот к RecoilPivot
        float vert = Random.Range(recoilVerticalMin, recoilVerticalMax);
        float horiz = Random.Range(-recoilHorizontal, recoilHorizontal);
        Vector3 recoilEuler = new Vector3(-vert, horiz, 0f); // отрицательный X = подброс вверх

        Quaternion newRotation = recoilPivot.localRotation * Quaternion.Euler(recoilEuler);
        float angle = Quaternion.Angle(Quaternion.identity, newRotation);
        if (angle <= maxRecoilAngle)
        {
            recoilPivot.localRotation = newRotation;
        }
        else
        {
            recoilPivot.localRotation = Quaternion.Slerp(Quaternion.identity, newRotation, maxRecoilAngle / angle);
        }

        // Разброс с учётом прицеливания
        float effectiveSpread = currentSpread;
        if (isAiming) effectiveSpread *= aimSpreadMultiplier;
        Vector3 direction = GetSpreadDirection(effectiveSpread);

        // Луч
        RaycastHit hit;
        if (Physics.Raycast(playerCamera.transform.position, direction, out hit, 500f))
        {
            Enemy enemy = hit.collider.GetComponent<Enemy>();
            if (enemy != null) enemy.TakeDamage(damage);

            if (bulletHolePrefab != null)
            {
                Quaternion holeRot = Quaternion.FromToRotation(Vector3.up, hit.normal);
                GameObject hole = Instantiate(bulletHolePrefab, hit.point + hit.normal * 0.02f, holeRot);
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
        else
        {
            if (bulletTrailPrefab != null)
            {
                LineRenderer trail = Instantiate(bulletTrailPrefab, muzzlePoint.position, Quaternion.identity);
                trail.SetPosition(0, muzzlePoint.position);
                trail.SetPosition(1, muzzlePoint.position + direction * 500f);
                Destroy(trail.gameObject, 0.05f);
            }
        }

        // Увеличиваем разброс после выстрела
        if (isAuto)
            currentSpread = Mathf.Min(currentSpread + autoSpreadPerShot, maxSpread);
        else
            currentSpread = baseSpread;   // одиночный выстрел сбрасывает разброс
    }

    Vector3 GetSpreadDirection(float spreadDegrees)
    {
        if (playerCamera == null) return Vector3.forward;
        Vector3 baseDir = playerCamera.transform.forward;
        float halfSpread = spreadDegrees / 2f;
        Vector2 randomCircle = Random.insideUnitCircle * Mathf.Tan(halfSpread * Mathf.Deg2Rad);
        Quaternion rot = Quaternion.Euler(randomCircle.y, randomCircle.x, 0);
        return rot * baseDir;
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

    void OnDisable()
    {
        // Останавливаем перезарядку, если объект выключается
        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
            isReloading = false;
        }
    }
}