using System;
using UnityEngine;

namespace FlameOfHistory.AI
{
[DisallowMultipleComponent]
public sealed class CharacterHealth : MonoBehaviour, IDamageable
{
    [SerializeField] private Team team = Team.Axis;
    [SerializeField, Min(1f)] private float maximumHealth = 100f;

    public event Action<DamageInfo> Damaged;
    public event Action<DamageInfo> Died;

    public Team Team => team;
    public float MaximumHealth => maximumHealth;
    public float CurrentHealth { get; private set; }
    public float NormalizedHealth => CurrentHealth / maximumHealth;
    public bool IsAlive { get; private set; }
    public float LastDamageTime { get; private set; } = float.NegativeInfinity;

    private void Awake()  => ResetHealth();

    // Реестр всех персонажей для EnemyAI: OverlapSphere не видит цели вообще
    // без Collider или с одними триггерами, поэтому такая цель без реестра
    // была бы невидима для врагов. Регистрация идёт здесь, а не в EnemyAI,
    // чтобы работала для любого объекта.
    private void OnEnable() => EnemyAI.RegisterCharacter(this);
    private void OnDisable() => EnemyAI.UnregisterCharacter(this);

    public void ResetHealth()
    {
        CurrentHealth = maximumHealth;
        IsAlive = true;
        LastDamageTime = float.NegativeInfinity;
    }

    public void TakeDamage(DamageInfo damage)
    {
        if (!IsAlive || damage.Amount <= 0f)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage.Amount);
        LastDamageTime = Time.time;
        // Событие урона уже видит смерть: вложенный урон или лечение
        // из обработчика не могут повторно убить или оживить цель.
        bool died = CurrentHealth <= 0f;
        IsAlive = !died;
        Damaged?.Invoke(damage);
        if (died) Died?.Invoke(damage);
    }

    public void RestoreHealth(float amount)
    {
        if (!IsAlive || amount <= 0f)
            return;

        CurrentHealth = Mathf.Min(maximumHealth, CurrentHealth + amount);
    }
}
}
