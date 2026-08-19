using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class DoorController : MonoBehaviour
{
    [Header("Основные настройки")]
    public float openAngle = 90f;
    public Vector3 rotationAxis = Vector3.up;

    [Tooltip("Скорость открывания в градусах в секунду. Рекомендуется 90–120.")]
    public float openSpeed = 120f;

    [Tooltip("Скорость закрывания в градусах в секунду. Если 0, используется скорость открывания.")]
    public float closeSpeed = 100f;

    [Tooltip("Кривая скорости: 0 = закрыто, 1 = открыто. По умолчанию EaseInOut.")]
    public AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Направление открывания")]
    public OpenDirection directionMode = OpenDirection.Fixed;
    public enum OpenDirection { Fixed, AwayFromPlayer }

    [Header("Блокировка")]
    public bool isLocked = false;

    [Header("Физическая дверь")]
    [Tooltip("Если включено, дверь всегда имеет коллайдер и толкает Rigidbody игрока. Если выключено, коллайдер отключается при открытии.")]
    public bool physicalDoor = true;

    [Header("Звуки")]
    public AudioClip openSound;
    public AudioClip closeSound;
    public AudioClip lockedSound;
    [Range(0f, 1f)]
    public float soundVolume = 1f;
    public AudioSource oneShotAudioSource;

    [Header("Коллайдер двери")]
    [Tooltip("Коллайдер, который взаимодействует с игроком.")]
    public Collider doorCollider;

    [Header("События")]
    public UnityEvent onDoorStartOpen;
    public UnityEvent onDoorStartClose;
    public UnityEvent onDoorHalfway;
    public UnityEvent onDoorOpened;
    public UnityEvent onDoorClosed;

    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private bool isOpen = false;
    private bool playerInTrigger = false;
    private bool isAnimating = false;
    private Coroutine soundRoutine;
    private bool hasInvokedHalfway = false;
    private Rigidbody rb;

    void Reset()
    {
        openAngle = 90f;
        rotationAxis = Vector3.up;
        openSpeed = 120f;
        closeSpeed = 100f;
        movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        directionMode = OpenDirection.Fixed;
        isLocked = false;
        physicalDoor = true;
        openSound = null;
        closeSound = null;
        lockedSound = null;
        soundVolume = 1f;
        oneShotAudioSource = null;
        doorCollider = null;
        onDoorStartOpen = new UnityEvent();
        onDoorStartClose = new UnityEvent();
        onDoorHalfway = new UnityEvent();
        onDoorOpened = new UnityEvent();
        onDoorClosed = new UnityEvent();
    }

    void Start()
    {
        closedRotation = transform.rotation;
        targetRotation = closedRotation;

        if (closeSpeed <= 0f)
            closeSpeed = openSpeed;

        // Настройка аудио
        if (oneShotAudioSource == null)
        {
            oneShotAudioSource = gameObject.AddComponent<AudioSource>();
        }
        oneShotAudioSource.playOnAwake = false;
        oneShotAudioSource.loop = false;
        oneShotAudioSource.volume = soundVolume;

        // Настройка физической двери
        if (physicalDoor)
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Коллайдер всегда включён
            if (doorCollider != null)
                doorCollider.enabled = true;
        }
        else
        {
            // Старое поведение: коллайдер включается/выключается в зависимости от состояния
            if (doorCollider != null)
                doorCollider.enabled = !isOpen;
        }
    }

    void Update()
    {
        if (playerInTrigger && Input.GetKeyDown(KeyCode.E))
        {
            if (isAnimating) return;

            if (isLocked)
            {
                PlayOneShot(lockedSound);
                return;
            }

            ToggleDoor();
        }
    }

    public void ToggleDoor()
    {
        if (isAnimating || isLocked) return;
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (isAnimating || isOpen || isLocked) return;
        StartCoroutine(MoveDoor(true));
    }

    public void Close()
    {
        if (isAnimating || !isOpen) return;
        StartCoroutine(MoveDoor(false));
    }

    public void SetLocked(bool locked)
    {
        isLocked = locked;
    }

    private IEnumerator MoveDoor(bool opening)
    {
        isAnimating = true;
        hasInvokedHalfway = false;

        SetTargetRotation(opening);
        Quaternion startRot = transform.rotation;
        Quaternion endRot = targetRotation;

        float distance = Quaternion.Angle(startRot, endRot);
        if (distance < 0.01f)
        {
            isOpen = opening;
            isAnimating = false;
            UpdateColliderState();
            yield break;
        }

        if (opening)
        {
            PlayOneShot(openSound);
            onDoorStartOpen.Invoke();
        }
        else
        {
            PlayOneShot(closeSound);
            onDoorStartClose.Invoke();
        }

        float speed = opening ? openSpeed : closeSpeed;
        float duration = distance / speed;
        float progress = 0f;

        while (progress < 1f)
        {
            progress += Time.deltaTime / duration;
            progress = Mathf.Clamp01(progress);

            float curveValue = movementCurve.Evaluate(progress);
            Quaternion desiredRot = Quaternion.Slerp(startRot, endRot, curveValue);
            SetDoorRotation(desiredRot);

            if (!hasInvokedHalfway && progress >= 0.5f)
            {
                hasInvokedHalfway = true;
                onDoorHalfway.Invoke();
            }

            yield return null;
        }

        // Финальная установка
        SetDoorRotation(endRot);

        isOpen = opening;
        isAnimating = false;
        UpdateColliderState();

        if (opening)
            onDoorOpened.Invoke();
        else
            onDoorClosed.Invoke();
    }

    /// <summary>
    /// Устанавливает вращение двери, используя физику при наличии Rigidbody.
    /// </summary>
    private void SetDoorRotation(Quaternion rot)
    {
        if (rb != null)
            rb.MoveRotation(rot);
        else
            transform.rotation = rot;
    }

    /// <summary>
    /// Обновляет состояние коллайдера в зависимости от режима physicalDoor.
    /// </summary>
    private void UpdateColliderState()
    {
        if (doorCollider == null) return;

        if (physicalDoor)
            doorCollider.enabled = true;   // всегда включён
        else
            doorCollider.enabled = !isOpen;
    }

    private void SetTargetRotation(bool opening)
    {
        if (opening)
        {
            if (directionMode == OpenDirection.AwayFromPlayer && playerInTrigger)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    float signedAngle = GetSignedAngleForPlayer(player.transform);
                    targetRotation = closedRotation * Quaternion.AngleAxis(signedAngle, rotationAxis);
                }
                else
                {
                    targetRotation = closedRotation * Quaternion.AngleAxis(openAngle, rotationAxis);
                }
            }
            else
            {
                targetRotation = closedRotation * Quaternion.AngleAxis(openAngle, rotationAxis);
            }
        }
        else
        {
            targetRotation = closedRotation;
        }
    }

    private float GetSignedAngleForPlayer(Transform player)
    {
        Vector3 toPlayer = player.position - transform.position;
        Vector3 doorForward = transform.forward;

        if (rotationAxis.normalized == Vector3.up)
        {
            toPlayer.y = 0f;
            doorForward.y = 0f;
        }

        float dot = Vector3.Dot(doorForward, toPlayer.normalized);
        return dot > 0 ? Mathf.Abs(openAngle) : -Mathf.Abs(openAngle);
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip == null || oneShotAudioSource == null) return;

        if (soundRoutine != null)
        {
            StopCoroutine(soundRoutine);
            soundRoutine = null;
        }

        oneShotAudioSource.Stop();
        oneShotAudioSource.volume = soundVolume;
        oneShotAudioSource.clip = clip;
        oneShotAudioSource.Play();
        soundRoutine = StartCoroutine(FadeOutOneShot(clip.length));
    }

    private IEnumerator FadeOutOneShot(float clipLength)
    {
        float fadeDuration = 0.3f;
        float fadeStart = Mathf.Max(0f, clipLength - fadeDuration);
        if (fadeStart > 0f)
            yield return new WaitForSeconds(fadeStart);

        float elapsed = 0f;
        float startVol = oneShotAudioSource.volume;
        while (elapsed < fadeDuration)
        {
            if (oneShotAudioSource == null) yield break;
            elapsed += Time.deltaTime;
            oneShotAudioSource.volume = Mathf.Lerp(startVol, 0f, elapsed / fadeDuration);
            yield return null;
        }

        if (oneShotAudioSource != null)
            oneShotAudioSource.Stop();

        soundRoutine = null;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInTrigger = true;
            if (isOpen && directionMode == OpenDirection.AwayFromPlayer && !isAnimating)
                SetTargetRotation(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInTrigger = false;
        }
    }

    void OnDisable()
    {
        if (soundRoutine != null) StopCoroutine(soundRoutine);
        if (oneShotAudioSource != null) oneShotAudioSource.Stop();
        isAnimating = false;
    }

    void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, rotationAxis * 1.5f);

        if (directionMode == OpenDirection.Fixed)
        {
            Gizmos.color = Color.green;
            Quaternion openRot = transform.rotation * Quaternion.AngleAxis(openAngle, rotationAxis);
            Vector3 direction = openRot * Vector3.forward;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.2f, direction * 1.5f);
        }
        else
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.2f, transform.forward * 1.5f);
            Gizmos.DrawRay(transform.position + Vector3.up * 0.2f, -transform.forward * 1.5f);
        }
    }
}