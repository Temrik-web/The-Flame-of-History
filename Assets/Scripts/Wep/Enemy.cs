using UnityEngine;
using System;

/// <summary>
/// Здоровье и смерть врага.
/// Обновлено: добавлены события OnDeath / OnDamaged и реализация IDamageable,
/// чтобы EnemyAI (и оружие игрока) могли получать урон/смерть единым способом.
/// Старый вызов TakeDamage(float) по-прежнему работает — обратная совместимость сохранена.
/// </summary>
public class Enemy : MonoBehaviour, IDamageable
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Смерть")]
    public GameObject deathEffectPrefab;   // частицы крови/взрыва (необязательно)
    public AudioClip deathSound;           // звук смерти (необязательно)
    public bool destroyOnDeath = false;    // false = труп остаётся лежать на сцене (рекомендуется для шутера)

    private AudioSource audioSource;

    // События — на них подписывается EnemyAI, не создавая жёсткой зависимости
    public event Action OnDeath;
    public event Action<float, Vector3> OnDamaged; // (урон, позиция атакующего)

    public bool IsDead => currentHealth <= 0f;
    public float CurrentHealth => currentHealth;
    public float HealthPercent => maxHealth > 0f ? currentHealth / maxHealth : 0f;

    void Start()
    {
        currentHealth = maxHealth;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && deathSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; // 3D звук
        }
    }

    // Старый метод — оставлен для совместимости с уже написанным оружием игрока
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position);
    }

    // Новый метод — с позицией атакующего. Это то, что нужно ИИ,
    // чтобы понимать, откуда стреляют, даже если он не видит игрока.
    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        if (currentHealth <= 0) return; // уже мёртв

        currentHealth -= damage;
        OnDamaged?.Invoke(damage, attackerPosition);
        Debug.Log($"{gameObject.name} получил {damage} урона. Осталось: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} погиб.");
        OnDeath?.Invoke();

        if (deathEffectPrefab != null)
        {
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
        }

        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        if (destroyOnDeath)
        {
            if (deathSound != null && audioSource != null)
            {
                Destroy(gameObject, deathSound.length);
            }
            else
            {
                Destroy(gameObject);
            }
        }
        else
        {
            // Труп остаётся видимым на месте — отключаем только коллайдер,
            // чтобы по нему больше нельзя было стрелять и он не мешал навигации.
            // Анимация падения (Death trigger в EnemyAI) сама укладывает тело на землю.
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }
    }
}