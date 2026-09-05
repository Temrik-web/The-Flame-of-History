using System;
using UnityEngine;
using FlameOfHistory.AI;

/// <summary>
/// Совместимый API для инвентаря и UI. Всё здоровье хранит CharacterHealth.
/// Этот компонент не является второй целью для оружия и не хранит отдельные HP.
/// </summary>
[RequireComponent(typeof(CharacterHealth))]
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour
{
    private CharacterHealth combatHealth;
    private CharacterHealth Health => combatHealth != null
        ? combatHealth : combatHealth = GetComponent<CharacterHealth>();

    public float maxHealth => Health.MaximumHealth;
    public bool IsDead => !Health.IsAlive;
    public float HealthPercent => Health.NormalizedHealth;
    public event Action OnDeath;
    public event Action<float> OnDamaged;

    private void OnEnable()
    {
        Health.Damaged += HandleDamage;
        Health.Died += HandleDeath;
    }

    private void OnDisable()
    {
        if (combatHealth == null) return;
        combatHealth.Damaged -= HandleDamage;
        combatHealth.Died -= HandleDeath;
    }

    private void HandleDamage(DamageInfo damage) => OnDamaged?.Invoke(damage.Amount);
    private void HandleDeath(DamageInfo damage) => OnDeath?.Invoke();

    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        Health.TakeDamage(new DamageInfo(damage, transform.position,
            (transform.position - attackerPosition).normalized, null));
    }

    public void Heal(float amount) => Health.RestoreHealth(amount);
}
