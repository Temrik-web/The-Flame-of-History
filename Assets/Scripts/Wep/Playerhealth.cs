using UnityEngine;
using System;

/// <summary>
/// Здоровье игрока.
///
/// На игроке одновременно висят два health-скрипта: этот и боевой
/// FlameOfHistory.AI.CharacterHealth. Оружие, ножи и гранаты ищут интерфейс
/// FlameOfHistory.AI.IDamageable первым, поэтому весь их урон уходил
/// в CharacterHealth, а старый EnemyAI (Enemyai.cs) бил через глобальный
/// IDamageable — то есть сюда. Получались два независимых пула HP: граната
/// «убивала» игрока в CharacterHealth, но событие смерти здесь не стреляло,
/// а аптечки лечили это, всегда полное здоровье.
///
/// Теперь CharacterHealth — единственный источник правды, если он есть
/// на объекте: урон и лечение переадресуются туда, а обратно приходят
/// через события Damaged / Died.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    private float currentHealth;

    public event Action OnDeath;
    public event Action<float> OnDamaged;

    public bool IsDead => combatHealth != null ? !combatHealth.IsAlive : currentHealth <= 0f;

    public float HealthPercent => combatHealth != null
        ? combatHealth.NormalizedHealth
        : (maxHealth > 0f ? currentHealth / maxHealth : 0f);

    private FlameOfHistory.AI.CharacterHealth combatHealth;

    void Awake()
    {
        combatHealth = GetComponent<FlameOfHistory.AI.CharacterHealth>();

        // CurrentHealth здесь не читаем: порядок Awake у компонентов одного
        // объекта не определён, и CharacterHealth мог ещё не сбросить здоровье
        if (combatHealth != null) maxHealth = combatHealth.MaximumHealth;

        currentHealth = maxHealth;
    }

    void OnEnable()
    {
        if (combatHealth == null) return;

        combatHealth.Damaged += HandleCombatDamage;
        combatHealth.Died += HandleCombatDeath;
    }

    void OnDisable()
    {
        if (combatHealth == null) return;

        combatHealth.Damaged -= HandleCombatDamage;
        combatHealth.Died -= HandleCombatDeath;
    }

    void HandleCombatDamage(FlameOfHistory.AI.DamageInfo damage)
    {
        currentHealth = combatHealth.CurrentHealth;
        OnDamaged?.Invoke(damage.Amount);
        Debug.Log($"Игрок получил {damage.Amount:0.#} урона. Осталось: {currentHealth:0.#}");
    }

    void HandleCombatDeath(FlameOfHistory.AI.DamageInfo damage)
    {
        currentHealth = 0f;
        OnDeath?.Invoke();
        Debug.Log("Игрок погиб.");
        // Здесь: экран смерти, рестарт уровня, переход в визуальную новеллу и т.д.
    }

    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        if (combatHealth != null)
        {
            Vector3 direction = (transform.position - attackerPosition).normalized;
            combatHealth.TakeDamage(new FlameOfHistory.AI.DamageInfo(
                damage, transform.position, direction, null));
            return;   // остальное придёт через HandleCombatDamage
        }

        if (currentHealth <= 0f) return;

        currentHealth -= damage;
        OnDamaged?.Invoke(damage);
        Debug.Log($"Игрок получил {damage} урона. Осталось: {currentHealth}");

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            OnDeath?.Invoke();
            Debug.Log("Игрок погиб.");
        }
    }

    public void Heal(float amount)
    {
        if (combatHealth != null)
        {
            combatHealth.RestoreHealth(amount);
            currentHealth = combatHealth.CurrentHealth;
            return;
        }

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }
}
