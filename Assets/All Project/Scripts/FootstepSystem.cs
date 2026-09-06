using UnityEngine;
using System.Collections.Generic;

public class FootstepSystem : MonoBehaviour
{
    [Header("Audio Source")]
    [SerializeField] private AudioSource audioSource;

    [Header("Настройки шагов")]
    [SerializeField] private LayerMask groundMask = ~0;          // Слой земли
    [SerializeField] private float rayDistance = 1.5f;           // Длина луча вниз
    [SerializeField] private float walkInterval = 0.45f;         // Интервал при ходьбе
    [SerializeField] private float sprintInterval = 0.3f;        // Интервал при беге
    [SerializeField] private float crouchInterval = 0.6f;        // Интервал при приседе

    [Header("Громкость и тон")]
    [SerializeField] private float baseVolume = 0.7f;
    [SerializeField] private float sprintVolumeMultiplier = 1.2f;
    [SerializeField] private float crouchVolumeMultiplier = 0.6f;
    [SerializeField] private float basePitch = 1f;
    [SerializeField] private float sprintPitchMultiplier = 1.1f;
    [SerializeField] private float crouchPitchMultiplier = 0.9f;
    [SerializeField] private float randomPitchRange = 0.1f;     // случайное изменение тона

    [Header("Звук по умолчанию (если тег не найден)")]
    [SerializeField] private AudioClip defaultSound;             // <-- теперь отдельно

    [Header("Соответствие тегов и звуков")]
    [SerializeField] private SurfaceSound[] surfaceSounds;      // Настройка в инспекторе

    [System.Serializable]
    public class SurfaceSound
    {
        public string tag;              // Тег объекта (например, "Grass")
        public AudioClip[] clips;       // Массив клипов (выбирается случайный)
    }

    // Словарь для быстрого доступа
    private Dictionary<string, AudioClip[]> soundMap = new Dictionary<string, AudioClip[]>();
    private CharacterController controller;
    private float stepTimer;

    // Переменные для определения состояния (замени на свои, если нужно)
    private bool isGrounded => controller != null && controller.isGrounded;
    private bool isSprinting => Input.GetKey(KeyCode.LeftShift); // или своя переменная
    private bool isCrouching => Input.GetKey(KeyCode.C);         // или своя

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // Если AudioSource не назначен – создаём
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.loop = false;

        // Заполняем словарь из массива
        soundMap.Clear();
        foreach (var item in surfaceSounds)
        {
            if (!string.IsNullOrEmpty(item.tag) && item.clips != null && item.clips.Length > 0)
                soundMap[item.tag] = item.clips;
        }
    }

    private void Update()
    {
        if (!isGrounded)
        {
            stepTimer = 0f;
            return;
        }

        // Получаем горизонтальную скорость (без вертикали)
        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0;
        float speed = horizontalVelocity.magnitude;

        if (speed > 0.1f)
        {
            // Выбираем интервал в зависимости от состояния
            float interval = walkInterval;
            if (isSprinting) interval = sprintInterval;
            else if (isCrouching) interval = crouchInterval;

            stepTimer += Time.deltaTime;
            if (stepTimer >= interval)
            {
                PlayFootstep();
                stepTimer = 0f;
            }
        }
        else
        {
            stepTimer = 0f;
        }
    }

    private void PlayFootstep()
    {
        RaycastHit hit;
        if (!Physics.Raycast(transform.position, Vector3.down, out hit, rayDistance, groundMask))
            return;

        // Определяем тег поверхности
        string tag = hit.collider.tag;

        // Ищем в словаре
        AudioClip[] clips = null;
        if (!soundMap.TryGetValue(tag, out clips) || clips == null || clips.Length == 0)
        {
            // Если не нашли – используем дефолтный звук
            if (defaultSound != null)
            {
                PlayClip(defaultSound);
                return;
            }
            else
                return; // совсем нет звуков
        }

        // Выбираем случайный клип из массива
        AudioClip selectedClip = clips[Random.Range(0, clips.Length)];
        if (selectedClip == null) return;

        PlayClip(selectedClip);
    }

    // Отдельный метод для воспроизведения с настройками громкости и тона
    private void PlayClip(AudioClip clip)
    {
        float volume = baseVolume;
        float pitch = basePitch;

        if (isSprinting)
        {
            volume *= sprintVolumeMultiplier;
            pitch *= sprintPitchMultiplier;
        }
        else if (isCrouching)
        {
            volume *= crouchVolumeMultiplier;
            pitch *= crouchPitchMultiplier;
        }

        // Добавляем случайность
        pitch += Random.Range(-randomPitchRange, randomPitchRange);
        pitch = Mathf.Clamp(pitch, 0.7f, 1.3f);

        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip, volume);
    }

    // Визуализация луча в редакторе (для отладки)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, Vector3.down * rayDistance);
    }
}