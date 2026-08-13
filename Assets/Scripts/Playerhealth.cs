using UnityEngine;
using System;

/// <summary>
/// Пример health-скрипта игрока. Если у тебя уже есть свой — просто добавь
/// ": IDamageable" к его классу и реализуй метод TakeDamage(float, Vector3),
/// этот файл можно не использовать. Нужен только для того, чтобы враг
/// физически мог наносить урон игроку через рейкаст.
/// </summary>
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    private float currentHealth;

    public event Action OnDeath;
    public event Action<float> OnDamaged;

    public bool IsDead => currentHealth <= 0f;
    public float HealthPercent => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        if (currentHealth <= 0f) return;

        currentHealth -= damage;
        OnDamaged?.Invoke(damage);
        Debug.Log($"Игрок получил {damage} урона. Осталось: {currentHealth}");

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            OnDeath?.Invoke();
            Debug.Log("Игрок погиб.");
            // Здесь: экран смерти, рестарт уровня, переход в визуальную новеллу и т.д.
        }
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }
}