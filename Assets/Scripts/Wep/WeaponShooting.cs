using UnityEngine;
using System.Collections;

public class WeaponShooting : MonoBehaviour
{
    [Header("Настройки оружия")]
    public Transform muzzlePoint;     // перетащи сюда MuzzlePoint
    public Camera playerCamera;       // перетащи сюда Camera (можно оставить пустым, если скрипт на самой камере)
    public float damage = 25f;
    public float range = 100f;
    public float fireRate = 10f;      // выстрелов в секунду
    public int magazineSize = 35;
    public float reloadTime = 2f;

    [Header("Эффекты (можно оставить пустым пока)")]
    public ParticleSystem muzzleFlash;
    public GameObject impactEffectPrefab;
    public AudioSource audioSource;
    public AudioClip shootSound;
    public AudioClip reloadSound;

    private int currentAmmo;
    private bool isReloading = false;
    private float nextTimeToFire = 0f;

    void Awake()
    {
        // Если скрипт висит прямо на камере и поле не заполнено - берём себя
        if (playerCamera == null)
        {
            playerCamera = GetComponent<Camera>();
        }

        if (playerCamera == null)
        {
            Debug.LogError("[WeaponShooting] Player Camera не назначена в инспекторе! Стрельба работать не будет.");
        }

        if (muzzlePoint == null)
        {
            Debug.LogWarning("[WeaponShooting] Muzzle Point не назначена. Используется позиция камеры как точка выстрела.");
        }
    }

    void Start()
    {
        currentAmmo = magazineSize;
        Debug.Log("[WeaponShooting] Старт. Патронов в магазине: " + currentAmmo);
    }

    void Update()
    {
        if (playerCamera == null) return; // без камеры стрелять некуда

        // Открытый инвентарь или диалог: ввод игнорируем
        if (PlayerInputLock.WeaponsLocked) return;
        if (DialogueManager.Instance != null && DialogueManager.Instance.isDialogueActive) return;

        if (isReloading) return;

        if (currentAmmo <= 0)
        {
            Debug.Log("[WeaponShooting] Патроны кончились, автоперезарядка");
            StartCoroutine(Reload());
            return;
        }

        // ЛКМ (по умолчанию Fire1) ИЛИ прямая проверка мыши как запасной вариант
        bool firePressed = false;
        try
        {
            firePressed = Input.GetButton("Fire1");
        }
        catch (System.InvalidOperationException)
        {
            // Fire1 недоступен, если активна только новая Input System
            firePressed = Input.GetMouseButton(0);
        }

        if (firePressed && Time.time >= nextTimeToFire)
        {
            nextTimeToFire = Time.time + 1f / fireRate;
            Shoot();
        }

        if (Input.GetKeyDown(KeyCode.R) && currentAmmo < magazineSize)
        {
            StartCoroutine(Reload());
        }
    }

    void Shoot()
    {
        currentAmmo--;
        Debug.Log("[WeaponShooting] Выстрел! Осталось патронов: " + currentAmmo);

        if (muzzleFlash != null) muzzleFlash.Play();
        if (audioSource != null && shootSound != null) audioSource.PlayOneShot(shootSound);

        Vector3 rayOrigin = playerCamera.transform.position;
        Vector3 rayDirection = playerCamera.transform.forward;

        Debug.DrawRay(rayOrigin, rayDirection * range, Color.red, 1f); // видно в окне Scene

        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, range))
        {
            Debug.Log("[WeaponShooting] Попадание: " + hit.collider.name);

            if (impactEffectPrefab != null)
            {
                GameObject impact = Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
                Destroy(impact, 2f);
            }

            ApplyDamage(hit, rayDirection);
        }
        else
        {
            Debug.Log("[WeaponShooting] Промах (луч ни во что не попал)");
        }
    }

    /// <summary>
    /// Нанести урон цели. Поддерживаются оба интерфейса урона проекта:
    /// боевой FlameOfHistory.AI.IDamageable (враги на CharacterHealth) и
    /// простой глобальный IDamageable (его реализуют Enemy и PlayerHealth).
    /// GetComponentInParent, а не GetComponent: коллайдер попадания обычно
    /// висит на дочерней кости, а здоровье — на корне персонажа.
    /// </summary>
    void ApplyDamage(RaycastHit hit, Vector3 direction)
    {
        Collider col = hit.collider;
        if (col == null) return;

        var aiTarget = col.GetComponentInParent<FlameOfHistory.AI.IDamageable>();
        if (aiTarget != null)
        {
            if (!aiTarget.IsAlive) return;

            aiTarget.TakeDamage(new FlameOfHistory.AI.DamageInfo(
                damage, hit.point, direction, gameObject));

            Debug.Log($"[WeaponShooting] {col.name} получил {damage} урона.");
            return;
        }

        var simpleTarget = col.GetComponentInParent<IDamageable>();
        if (simpleTarget != null)
        {
            simpleTarget.TakeDamage(damage, transform.position);
            Debug.Log($"[WeaponShooting] {col.name} получил {damage} урона.");
        }
    }

    IEnumerator Reload()
    {
        isReloading = true;
        Debug.Log("[WeaponShooting] Перезарядка...");

        if (audioSource != null && reloadSound != null) audioSource.PlayOneShot(reloadSound);

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;
        Debug.Log("[WeaponShooting] Перезарядка завершена. Патронов: " + currentAmmo);
    }
}