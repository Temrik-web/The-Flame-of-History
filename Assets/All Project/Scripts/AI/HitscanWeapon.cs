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
    [SerializeField, Min(1)] private int magazineCapacity = 32;
    [SerializeField, Min(0)] private int startingReserve = 128;
    [SerializeField, Min(0.05f)] private float reloadDuration = 2.4f;

    [Header("Fire")]
    [Tooltip("Выстрелов в минуту.")]
    [SerializeField, Min(1f)] private float roundsPerMinute = 500f;

    [Header("Audible noise")]
    [SerializeField, Min(0f)] private float shotNoiseRadius = 35f;

    [Header("Tracer")]
    [SerializeField] private bool showTracer = true;
    [SerializeField] private Color tracerColor = new(1f, 0.8f, 0.25f);
    [SerializeField, Min(0.005f)] private float tracerWidth = 0.025f;
    [SerializeField, Min(0.01f)] private float tracerLifetime = 0.06f;

    public int AmmunitionInMagazine { get; private set; }
    public int ReserveAmmunition => _reserve;
    public bool IsReloading { get; private set; }
    public bool HasAmmunition => AmmunitionInMagazine > 0 || _reserve > 0;
    public bool NeedsReload => AmmunitionInMagazine <= 0 && _reserve > 0;

    public bool HasMuzzle => muzzle != null;
    public bool HasAudioSource => audioSource != null;
    public Vector3 MuzzlePosition => muzzle != null ? muzzle.position : transform.position;
    public Vector3 MuzzleForward => muzzle != null ? muzzle.forward : transform.forward;
    public float Range => range;
    public float DamageScale { get; set; } = 1f;
    public event System.Action<Vector3> Fired;
    public event System.Action ReloadStarted;
    public event System.Action ReloadFinished;

    private int _reserve;
    private float _nextShotTime;
    private Coroutine _reloadRoutine;
    private GameObject _ownerRoot;
    private Team _ownerTeam = Team.Axis;
    private static Material _tracerMaterial;

    private float ShotInterval => 60f / roundsPerMinute;

    private void Awake()  => ResetAmmo();

    public void SetMuzzle(Transform muzzleTransform)
    {
        if (muzzleTransform != null) muzzle = muzzleTransform;
    }
    public void SetAudioSource(AudioSource source)
    {
        if (source != null) audioSource = source;
    }
    public void SetSounds(AudioClip shot, AudioClip reload)
    {
        if (shot != null) shotSound = shot;
        if (reload != null) reloadSound = reload;
    }

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
            var ownerHealth = owner.GetComponentInParent<CharacterHealth>();
            _ownerRoot = ownerHealth != null ? ownerHealth.gameObject : owner;
            if (ownerHealth != null) _ownerTeam = ownerHealth.Team;
        }
        else
        {
            _ownerRoot = null;
            _ownerTeam = Team.Axis;
        }

        AmmunitionInMagazine--;
        _nextShotTime = Time.time + ShotInterval;
        Vector3 origin = MuzzlePosition;
        Vector3 toTarget = targetPoint - origin;
        Vector3 direction = toTarget.sqrMagnitude > 0.0001f
            ? ApplySpread(toTarget.normalized)
            : ApplySpread(MuzzleForward);
        origin += direction * 0.35f;
        if (audioSource != null && shotSound != null)
            audioSource.PlayOneShot(shotSound);

        NoiseSystem.Emit(MuzzlePosition, shotNoiseRadius, owner, 1f);

        Vector3 endPoint = origin + direction * range;
        bool hitSomething = false;

        var hits = Physics.RaycastAll(origin, direction, range, hitMask, triggerInteraction);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (_ownerRoot != null && hit.collider.transform.IsChildOf(_ownerRoot.transform))
                continue;

            endPoint = hit.point;
            hitSomething = true;

            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null && damageable.IsAlive)
                damageable.TakeDamage(new DamageInfo(damage * Mathf.Max(0.01f, DamageScale), hit.point, direction, owner));
            else
                SpawnImpact(hit.point, hit.normal);

            break;
        }
        if (showTracer) SpawnTracer(origin, endPoint);
        ProjectilePass.Emit(new ProjectilePass.Shot(
            origin, endPoint, owner, _ownerTeam, hitSomething));
        Fired?.Invoke(endPoint);
        if (AmmunitionInMagazine == 0) BeginReload();

        return true;
    }

    private void SpawnImpact(Vector3 point, Vector3 normal)
    {
        if (impactEffect == null) return;
        GameObject fx = Instantiate(impactEffect, point, Quaternion.LookRotation(normal));
        Destroy(fx, impactEffectLifetime);
    }

    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        if (_tracerMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            _tracerMaterial = new Material(shader) { color = tracerColor, hideFlags = HideFlags.HideAndDontSave };
        }
        var go = new GameObject("Tracer");
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = tracerWidth;
        line.endWidth = tracerWidth;
        line.material = _tracerMaterial;
        Destroy(go, tracerLifetime);
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
        if (_reloadRoutine != null) StopCoroutine(_reloadRoutine);
        _reloadRoutine = null;
        IsReloading = false;
    }

    private IEnumerator ReloadRoutine()
    {
        IsReloading = true;
        ReloadStarted?.Invoke();

        if (audioSource != null && reloadSound != null)
            audioSource.PlayOneShot(reloadSound);

        yield return new WaitForSeconds(reloadDuration);

        int required = magazineCapacity - AmmunitionInMagazine;
        int loaded = Mathf.Min(required, _reserve);

        AmmunitionInMagazine += loaded;
        _reserve -= loaded;

        IsReloading = false;
        _reloadRoutine = null;
        ReloadFinished?.Invoke();
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
        Gizmos.color = Color.red;
        Gizmos.DrawRay(MuzzlePosition, MuzzleForward * range);
    }
#endif
}
}
