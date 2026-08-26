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
    private void OnEnable() => ResetHealth();

    public void ResetHealth()
    {
        CurrentHealth = maximumHealth;
        IsAlive = true;
    }

    public void TakeDamage(DamageInfo damage)
    {
        if (!IsAlive || damage.Amount <= 0f)
            return;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - damage.Amount);
        LastDamageTime = Time.time;
        Damaged?.Invoke(damage);

        if (CurrentHealth <= 0f)
        {
            IsAlive = false;
            Died?.Invoke(damage);
        }
    }

    public void RestoreHealth(float amount)
    {
        if (!IsAlive || amount <= 0f)
            return;

        CurrentHealth = Mathf.Min(maximumHealth, CurrentHealth + amount);
    }
}
}
