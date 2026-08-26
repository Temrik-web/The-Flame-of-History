using UnityEngine;

namespace FlameOfHistory.AI
{
/// <summary>
/// Реагирует на пролетающие рядом пули.
/// На игроке — звук свиста пули у виска (whizz).
/// На враге — рост подавления: он хуже целится и жмётся в укрытие.
/// Тряски камеры здесь нет намеренно — только звук.
/// </summary>
[DisallowMultipleComponent]
public sealed class SuppressionReceiver : MonoBehaviour
{
    [Header("Кого считать «рядом»")]
    [Tooltip("Радиус, в котором пролёт пули считается близким.")]
    [SerializeField, Min(0.1f)] private float nearMissRadius = 2.5f;

    [Header("Игрок: свист пули")]
    [Tooltip("Включи, если этот компонент висит на игроке или его камере.")]
    [SerializeField] private bool isPlayer = false;
    [SerializeField] private AudioSource whizzSource;
    [SerializeField] private AudioClip[] whizzClips;
    [SerializeField, Min(0f)] private float whizzCooldown = 0.08f;

    [Header("Враг: подавление")]
    [SerializeField] private EnemyAI enemyAI;

    private CharacterHealth _health;
    private float _nextWhizzTime;

    private void Awake()
    {
        _health = GetComponentInParent<CharacterHealth>();
        if (enemyAI == null) enemyAI = GetComponentInParent<EnemyAI>();
    }

    private void OnEnable()  => ProjectilePass.ShotFired += OnShot;
    private void OnDisable() => ProjectilePass.ShotFired -= OnShot;

    private void OnShot(ProjectilePass.Shot shot)
    {
        if (_health == null || !_health.IsAlive) return;

        // Не реагируем на дружественный огонь.
        if (shot.ShooterTeam == _health.Team) return;

        // Не реагируем на собственные выстрелы.
        if (shot.Shooter != null &&
            shot.Shooter.transform.root == transform.root) return;

        float distance = shot.DistanceToPoint(transform.position);
        if (distance > nearMissRadius) return;

        // 1 у самого уха → 0 на краю радиуса.
        float closeness = 1f - Mathf.Clamp01(distance / nearMissRadius);

        if (isPlayer)
            PlayerFeedback(closeness);
        else
            EnemyFeedback(closeness);
    }

    private void PlayerFeedback(float closeness)
    {
        if (whizzSource == null || whizzClips == null || whizzClips.Length == 0) return;
        if (Time.time < _nextWhizzTime) return;

        _nextWhizzTime = Time.time + whizzCooldown;

        AudioClip clip = whizzClips[Random.Range(0, whizzClips.Length)];
        if (clip == null) return;

        whizzSource.pitch = Random.Range(0.92f, 1.08f);
        whizzSource.PlayOneShot(clip, Mathf.Lerp(0.5f, 1f, closeness));
    }

    private void EnemyFeedback(float closeness)
    {
        if (enemyAI != null)
            enemyAI.ApplySuppression(Mathf.Lerp(0.2f, 0.6f, closeness));
    }
}
}
