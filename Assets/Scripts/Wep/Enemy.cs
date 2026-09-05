using UnityEngine;
using System;
using FlameOfHistory.AI;

/// <summary>
/// Эффекты смерти и совместимые события. HP принадлежат только CharacterHealth.
/// </summary>
[RequireComponent(typeof(CharacterHealth))]
[DisallowMultipleComponent]
public class Enemy : MonoBehaviour
{
    private CharacterHealth combatHealth;
    private CharacterHealth Health => combatHealth != null
        ? combatHealth : combatHealth = GetComponent<CharacterHealth>();
    public float maxHealth => Health.MaximumHealth;

    [Header("Смерть")]
    public GameObject deathEffectPrefab;   // частицы крови/взрыва (необязательно)
    public AudioClip deathSound;           // звук смерти (необязательно)
    public bool destroyOnDeath = false;    // false = труп остаётся лежать на сцене (рекомендуется для шутера)

    private AudioSource audioSource;

    // События — на них подписывается EnemyAI, не создавая жёсткой зависимости
    public event Action OnDeath;
    public event Action<float, Vector3> OnDamaged; // (урон, позиция атакующего)

    public bool IsDead => !Health.IsAlive;
    public float CurrentHealth => Health.CurrentHealth;
    public float HealthPercent => Health.NormalizedHealth;

    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && deathSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 1f; // 3D звук
        }
    }

    void OnEnable()
    {
        Health.Damaged += HandleDamage;
        Health.Died += HandleDeath;
    }

    void OnDisable()
    {
        if (combatHealth == null) return;
        combatHealth.Damaged -= HandleDamage;
        combatHealth.Died -= HandleDeath;
    }

    void HandleDamage(DamageInfo damage) => OnDamaged?.Invoke(damage.Amount,
        damage.Attacker != null ? damage.Attacker.transform.position : damage.Point - damage.Direction);
    void HandleDeath(DamageInfo damage) => Die();

    // Старый метод — оставлен для совместимости с уже написанным оружием игрока
    public void TakeDamage(float damage)
    {
        TakeDamage(damage, transform.position);
    }

    // Новый метод — с позицией атакующего. Это то, что нужно ИИ,
    // чтобы понимать, откуда стреляют, даже если он не видит игрока.
    public void TakeDamage(float damage, Vector3 attackerPosition)
    {
        Health.TakeDamage(new DamageInfo(damage, transform.position,
            (transform.position - attackerPosition).normalized, null));
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
