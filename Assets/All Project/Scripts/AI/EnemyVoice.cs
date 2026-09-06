using UnityEngine;

namespace FlameOfHistory.AI
{
    /// <summary>
    /// Голос и звуки врага: крики при обнаружении, боль, смерть, шаги, перезарядка.
    ///
    /// Все клипы — массивы: из массива берётся случайный, поэтому отряд не звучит
    /// как один и тот же семпл. Питч слегка рандомизируется.
    ///
    /// Реплики (voice) и звуки тела (шаги) идут через разные AudioSource, чтобы
    /// крик не обрывался шагом. Если источники не заданы — создаются автоматически
    /// в Awake, так что компонент работает «из коробки»: достаточно закинуть клипы.
    ///
    /// Крики дополнительно эмитят шум в NoiseSystem — союзники их слышат.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyVoice : MonoBehaviour
    {
        [Header("Источники звука")]
        [Tooltip("Источник для реплик (крики, боль, смерть). Пусто — создастся сам.")]
        [SerializeField] private AudioSource voiceSource;
        [Tooltip("Источник для звуков тела (шаги, экипировка). Пусто — создастся сам.")]
        [SerializeField] private AudioSource bodySource;

        [Header("Реплики: обнаружение и бой")]
        [Tooltip("«Вижу его!», «Контакт!» — при переходе в бой.")]
        [SerializeField] private AudioClip[] spottedLines;
        [Tooltip("«Что за шум?» — при переходе в настороженность.")]
        [SerializeField] private AudioClip[] alertLines;
        [Tooltip("«Куда он делся?» — при потере цели и начале поиска.")]
        [SerializeField] private AudioClip[] lostTargetLines;
        [Tooltip("Выкрики во время стрельбы — необязательны.")]
        [SerializeField] private AudioClip[] combatChatterLines;
        [Tooltip("«Перезаряжаюсь!»")]
        [SerializeField] private AudioClip[] reloadLines;
        [Tooltip("«Прикройте!» — при отходе и сильном подавлении.")]
        [SerializeField] private AudioClip[] retreatLines;
        [Tooltip("Крик под плотным огнём (подавление).")]
        [SerializeField] private AudioClip[] suppressedLines;

        [Header("Реплики: урон и смерть")]
        [SerializeField] private AudioClip[] painLines;
        [SerializeField] private AudioClip[] deathLines;

        [Header("Шаги")]
        [SerializeField] private AudioClip[] footstepClips;
        [Tooltip("Шагов в секунду при скорости 1 м/с. Итоговый темп зависит от скорости.")]
        [SerializeField, Min(0.05f)] private float footstepsPerMeter = 0.55f;
        [Tooltip("Ниже этой скорости шаги не проигрываются.")]
        [SerializeField, Min(0f)] private float footstepMinimumSpeed = 0.35f;
        [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.7f;

        [Header("Громкость реплик")]
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1f;
        [SerializeField] private Vector2 pitchRange = new(0.94f, 1.06f);

        [Header("Пауза между репликами")]
        [Tooltip("Минимальная пауза между любыми репликами, сек.")]
        [SerializeField, Min(0f)] private float voiceCooldown = 0.6f;
        [Tooltip("Отдельная пауза для боевых выкриков, сек.")]
        [SerializeField, Min(0f)] private float chatterCooldown = 6f;
        [Tooltip("Пауза между вскриками от боли, сек.")]
        [SerializeField, Min(0f)] private float painCooldown = 0.9f;

        [Header("Шум для союзников")]
        [Tooltip("Радиус шума от крика — по нему союзники подтягиваются.")]
        [SerializeField, Min(0f)] private float shoutNoiseRadius = 22f;
        [Tooltip("Эмитить шум при криках обнаружения и боли.")]
        [SerializeField] private bool shoutsMakeNoise = true;

        [Header("3D-звук")]
        [SerializeField, Min(0f)] private float minimumDistance = 3f;
        [SerializeField, Min(1f)] private float maximumDistance = 45f;

        private float _nextVoiceTime;
        private float _nextChatterTime;
        private float _nextPainTime;
        private float _footstepAccumulator;

        private void Awake()
        {
            voiceSource = EnsureSource(voiceSource, "VoiceSource");
            bodySource = EnsureSource(bodySource, "BodySource");
        }

        private AudioSource EnsureSource(AudioSource existing, string childName)
        {
            if (existing != null)
            {
                ConfigureSource(existing);
                return existing;
            }

            // Отдельный дочерний объект — чтобы не конфликтовать с AudioSource оружия.
            Transform child = transform.Find(childName);
            GameObject host;

            if (child != null) host = child.gameObject;
            else
            {
                host = new GameObject(childName);
                host.transform.SetParent(transform, false);
                host.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            }

            AudioSource source = host.GetComponent<AudioSource>();
            if (source == null) source = host.AddComponent<AudioSource>();

            ConfigureSource(source);
            return source;
        }

        private void ConfigureSource(AudioSource source)
        {
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minimumDistance;
            source.maxDistance = maximumDistance;
        }

        // =====================================================================
        // Реплики — вызываются из EnemyAI
        // =====================================================================

        /// <summary>«Вижу цель!» — при входе в бой.</summary>
        public void PlaySpotted()
        {
            if (PlayVoice(spottedLines, 1f) && shoutsMakeNoise)
                EmitShoutNoise(1f);
        }

        /// <summary>«Что это было?» — при переходе в настороженность.</summary>
        public void PlayAlert() => PlayVoice(alertLines, 0.9f);

        /// <summary>«Потерял его» — при потере цели.</summary>
        public void PlayLostTarget() => PlayVoice(lostTargetLines, 0.85f);

        /// <summary>Боевой выкрик — со своим длинным кулдауном.</summary>
        public void PlayCombatChatter()
        {
            if (Time.time < _nextChatterTime) return;
            if (!PlayVoice(combatChatterLines, 0.85f)) return;

            _nextChatterTime = Time.time + chatterCooldown;
        }

        /// <summary>«Перезаряжаюсь!»</summary>
        public void PlayReload() => PlayVoice(reloadLines, 0.9f);

        /// <summary>«Отходим!» / «Прикройте!»</summary>
        public void PlayRetreat()
        {
            if (PlayVoice(retreatLines, 1f) && shoutsMakeNoise)
                EmitShoutNoise(0.8f);
        }

        /// <summary>Крик под плотным огнём.</summary>
        public void PlaySuppressed() => PlayVoice(suppressedLines, 0.95f);

        /// <summary>Вскрик от полученного урона.</summary>
        public void PlayPain()
        {
            if (Time.time < _nextPainTime) return;
            _nextPainTime = Time.time + painCooldown;

            if (PlayVoice(painLines, 1f, ignoreCooldown: true) && shoutsMakeNoise)
                EmitShoutNoise(0.7f);
        }

        /// <summary>
        /// Предсмертный крик. Проигрывается «отвязанно» от объекта, потому что
        /// сам враг обычно тут же выключается/удаляется и оборвал бы звук.
        /// </summary>
        public void PlayDeath()
        {
            AudioClip clip = PickClip(deathLines);
            if (clip == null) return;

            PlayDetached(clip, voiceVolume);

            if (shoutsMakeNoise) EmitShoutNoise(0.9f);
        }

        // =====================================================================
        // Шаги
        // =====================================================================

        /// <summary>
        /// Обновление шагов. Вызывать каждый кадр, передавая текущую скорость
        /// врага в м/с и признак касания земли.
        /// </summary>
        public void UpdateFootsteps(float speed, bool grounded)
        {
            if (footstepClips == null || footstepClips.Length == 0) return;

            if (!grounded || speed < footstepMinimumSpeed)
            {
                _footstepAccumulator = 0f;
                return;
            }

            _footstepAccumulator += speed * footstepsPerMeter * Time.deltaTime;

            if (_footstepAccumulator < 1f) return;
            _footstepAccumulator -= 1f;

            AudioClip clip = PickClip(footstepClips);
            if (clip == null || bodySource == null) return;

            bodySource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            bodySource.PlayOneShot(clip, footstepVolume);
        }

        /// <summary>Разовый звук тела (экипировка, падение и т.п.).</summary>
        public void PlayBodyOneShot(AudioClip clip, float volume = 1f)
        {
            if (clip == null || bodySource == null) return;

            bodySource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            bodySource.PlayOneShot(clip, volume);
        }

        // =====================================================================
        // Внутреннее
        // =====================================================================

        private bool PlayVoice(AudioClip[] clips, float volumeScale, bool ignoreCooldown = false)
        {
            if (voiceSource == null) return false;
            if (!ignoreCooldown && Time.time < _nextVoiceTime) return false;

            AudioClip clip = PickClip(clips);
            if (clip == null) return false;

            _nextVoiceTime = Time.time + Mathf.Max(voiceCooldown, clip.length * 0.5f);

            voiceSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
            voiceSource.PlayOneShot(clip, voiceVolume * volumeScale);
            return true;
        }

        private static AudioClip PickClip(AudioClip[] clips)
        {
            if (clips == null || clips.Length == 0) return null;

            // Пропускаем пустые слоты в массиве — частая ситуация в инспекторе.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                AudioClip candidate = clips[Random.Range(0, clips.Length)];
                if (candidate != null) return candidate;
            }

            foreach (AudioClip candidate in clips)
                if (candidate != null) return candidate;

            return null;
        }

        /// <summary>Звук, который доигрывает, даже если враг уже выключен.</summary>
        private void PlayDetached(AudioClip clip, float volume)
        {
            var host = new GameObject($"Voice_{clip.name}");
            host.transform.position = voiceSource != null
                ? voiceSource.transform.position
                : transform.position + Vector3.up * 1.5f;

            AudioSource source = host.AddComponent<AudioSource>();
            ConfigureSource(source);
            source.clip = clip;
            source.volume = volume;
            source.pitch = Random.Range(pitchRange.x, pitchRange.y);
            source.Play();

            Destroy(host, clip.length / Mathf.Max(0.1f, source.pitch) + 0.2f);
        }

        private void EmitShoutNoise(float intensity)
        {
            if (shoutNoiseRadius <= 0f) return;
            NoiseSystem.Emit(transform.position, shoutNoiseRadius, gameObject, intensity);
        }
    }
}
