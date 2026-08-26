using System.Collections;
using UnityEngine;

namespace FlameOfHistory.AI
{
[DisallowMultipleComponent]
public sealed class HitscanWeapon : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip shotSound;
    [SerializeField] private AudioClip reloadSound;

    [Header("Impact")]
    [Tooltip("Префаб эффекта попадания (пыль/искры). Необязателен.")]
    [SerializeField] private GameObject impactEffect;
    [SerializeField, Min(0f)] private float impactEffectLifetime = 3f;

    [Header("Ballistics")]
    [SerializeField, Min(1f)] private float damage = 25f;
    [SerializeField, Min(1f)] private float range = 150f;
    [SerializeField, Min(0f)] private float spreadAngle = 1.25f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private QueryTriggerInteraction triggerInteraction =
        QueryTriggerInteraction.Ignore;

    [Header("Magazine")]
    [SerializeField, Min(1)] private int magazineCapacity = 30;
    [SerializeField, Min(0)] private int startingReserve = 120;
    [SerializeField, Min(0.05f)] private float reloadDuration = 2.4f;

    [Header("Fire")]
    [Tooltip("Выстрелов в минуту.")]
    [SerializeField, Min(1f)] private float roundsPerMinute = 500f;

    [Header("Audible noise")]
    [SerializeField, Min(0f)] private float shotNoiseRadius = 35f;

    public int AmmunitionInMagazine { get; private set; }
    public int ReserveAmmunition => _reserve;
    public bool IsReloading { get; private set; }
    public bool HasAmmunition => AmmunitionInMagazine > 0 || _reserve > 0;
    public bool NeedsReload => AmmunitionInMagazine <= 0 && _reserve > 0;

    private int _reserve;
    private float _nextShotTime;
    private Coroutine _reloadRoutine;
    private GameObject _ownerRoot;
    private Team _ownerTeam = Team.Axis;

    private float ShotInterval => 60f / roundsPerMinute;

    private void Awake()  => ResetAmmo();
    private void OnEnable() { if (AmmunitionInMagazine <= 0 && _reserve <= 0) ResetAmmo(); }

    public void ResetAmmo()
    {
        AmmunitionInMagazine = magazineCapacity;
        _reserve = startingReserve;
        IsReloading = false;
    }

    public bool TryFire(Vector3 targetPoint, GameObject owner)
    {
        if (IsReloading || Time.time < _nextShotTime)
            return false;

        if (AmmunitionInMagazine <= 0)
        {
            BeginReload();
            return false;
        }

        if (owner != null)
        {
            _ownerRoot = owner.transform.root.gameObject;
            var ownerHealth = owner.GetComponentInParent<CharacterHealth>();
            if (ownerHealth != null) _ownerTeam = ownerHealth.Team;
        }
        else _ownerRoot = null;

        AmmunitionInMagazine--;
        _nextShotTime = Time.time + ShotInterval;

        Vector3 origin = muzzle.position;
        Vector3 direction = ApplySpread((targetPoint - origin).normalized);
        origin += direction * 0.35f;

        muzzleFlash?.Play();

        if (audioSource != null && shotSound != null)
            audioSource.PlayOneShot(shotSound);

        NoiseSystem.Emit(muzzle.position, shotNoiseRadius, owner, 1f);

        Vector3 endPoint = origin + direction * range;
        bool hitSomething = false;

        var hits = Physics.RaycastAll(origin, direction, range, hitMask, triggerInteraction);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (_ownerRoot != null && hit.collider.transform.root.gameObject == _ownerRoot)
                continue;

            endPoint = hit.point;
            hitSomething = true;

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
                damageable.TakeDamage(new DamageInfo(damage, hit.point, direction, owner));
            else
                SpawnImpact(hit.point, hit.normal);

            break;
        }

        // Оповещаем о пролёте пули (для whizz игроку и подавления врагам).
        ProjectilePass.Emit(new ProjectilePass.Shot(
            origin, endPoint, owner, _ownerTeam, hitSomething));

        if (AmmunitionInMagazine == 0)
            BeginReload();

        return true;
    }

    private void SpawnImpact(Vector3 point, Vector3 normal)
    {
        if (impactEffect == null) return;
        GameObject fx = Instantiate(impactEffect, point, Quaternion.LookRotation(normal));
        Destroy(fx, impactEffectLifetime);
    }

    public bool BeginReload()
    {
        if (IsReloading || AmmunitionInMagazine >= magazineCapacity || _reserve <= 0)
            return false;

        _reloadRoutine = StartCoroutine(ReloadRoutine());
        return true;
    }

    public void CancelReload()
    {
        if (_reloadRoutine != null)
            StopCoroutine(_reloadRoutine);

        _reloadRoutine = null;
        IsReloading = false;
    }

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;

        if (audioSource != null && reloadSound != null)
            audioSource.PlayOneShot(reloadSound);

        yield return new WaitForSeconds(reloadDuration);

        int required = magazineCapacity - AmmunitionInMagazine;
        int loaded = Mathf.Min(required, _reserve);

        AmmunitionInMagazine += loaded;
        _reserve -= loaded;

        IsReloading = false;
        _reloadRoutine = null;
    }

    private Vector3 ApplySpread(Vector3 direction)
    {
        if (spreadAngle <= 0f)
            return direction;

        Quaternion lookRotation = Quaternion.LookRotation(direction);
        float yaw = Random.Range(-spreadAngle, spreadAngle);
        float pitch = Random.Range(-spreadAngle, spreadAngle);

        return lookRotation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
    }

    private void OnDisable() => CancelReload();

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (muzzle == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawRay(muzzle.position, muzzle.forward * range);
    }
#endif
}
}
