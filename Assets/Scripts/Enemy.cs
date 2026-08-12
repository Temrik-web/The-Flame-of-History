using UnityEngine;

public class Enemy : MonoBehaviour
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    private float currentHealth;

    [Header("Смерть")]
    public GameObject deathEffectPrefab;   // частицы крови/взрыва (необязательно)
    public AudioClip deathSound;           // звук смерти (необязательно)
    public bool destroyOnDeath = true;     // уничтожать объект при смерти

    private AudioSource audioSource;

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

    // Метод, который вызывает оружие
    public void TakeDamage(float damage)
    {
        if (currentHealth <= 0) return; // уже мёртв

        currentHealth -= damage;
        Debug.Log($"{gameObject.name} получил {damage} урона. Осталось: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log($"{gameObject.name} погиб.");

        // Спавн эффекта смерти
        if (deathEffectPrefab != null)
        {
            Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
        }

        // Звук смерти
        if (deathSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(deathSound);
        }

        // Уничтожение или отключение
        if (destroyOnDeath)
        {
            // Если есть звук, подождём его проигрывания
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
            // Можно отключить коллайдер и визуально скрыть
            Collider col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            foreach (Renderer r in renderers) r.enabled = false;
        }
    }
}