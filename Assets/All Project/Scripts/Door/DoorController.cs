using UnityEngine;
using System.Collections;

public class DoorController : MonoBehaviour
{
    [Header("Настройки двери")]
    public float openAngle = 90f;        // Угол открытия (знак определяет сторону)
    public float openSpeed = 2f;         // Скорость открывания
    public float closeSpeed = 2f;        // Скорость закрывания

    [Header("Взаимодействие")]
    public float interactDistance = 3f;       // Дистанция, с которой можно открыть дверь
    public LayerMask interactMask = 1 << 8;    // Слой, на котором находится триггер двери (по умолчанию слой 8)
    public bool showHint = true;               // Показывать подсказку "Нажмите E"

    [Header("Блокировка")]
    public bool isLocked = false;

    [Header("Звуки")]
    public AudioClip openSound;
    public AudioClip closeSound;
    public AudioClip lockedSound;
    public AudioSource audioSource;

    [Header("Физический коллайдер проёма")]
    public Collider physicalCollider;    // Коллайдер, блокирующий проход (не триггер)

    private Quaternion closedRotation;
    private Quaternion targetRotation;
    private bool isOpen = false;
    private bool isAnimating = false;
    private bool isLookingAtDoor = false;

    void Start()
    {
        closedRotation = transform.rotation;
        targetRotation = closedRotation;

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Если физический коллайдер не назначен, попытаемся найти его среди дочерних (не триггер)
        if (physicalCollider == null)
        {
            Collider[] cols = GetComponentsInChildren<Collider>();
            foreach (Collider col in cols)
            {
                if (!col.isTrigger)
                {
                    physicalCollider = col;
                    break;
                }
            }
        }

        // Устанавливаем начальное состояние физического коллайдера
        if (physicalCollider != null)
            physicalCollider.enabled = !isOpen;
    }

    void Update()
    {
        // Проверяем, смотрит ли игрок на дверь (рейкаст)
        isLookingAtDoor = false;
        if (Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, interactDistance, interactMask))
            {
                // Попадание в триггер двери? (коллайдер принадлежит этой двери)
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform == transform)
                {
                    isLookingAtDoor = true;
                }
            }
        }

        // Плавное вращение, только если не идёт анимация
        if (!isAnimating)
        {
            float speed = isOpen ? openSpeed : closeSpeed;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * speed);
            if (Quaternion.Angle(transform.rotation, targetRotation) < 0.01f)
                transform.rotation = targetRotation;
        }

        // Обработка нажатия E
        if (isLookingAtDoor && Input.GetKeyDown(KeyCode.E))
        {
            if (isAnimating) return;

            if (isLocked)
            {
                PlaySound(lockedSound);
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

    private IEnumerator MoveDoor(bool opening)
    {
        isAnimating = true;
        isOpen = opening; // временно для расчёта targetRotation
        SetTargetRotation();

        PlaySound(opening ? openSound : closeSound);

        // Отключаем физический коллайдер при открытии, включаем при закрытии
        if (physicalCollider != null)
            physicalCollider.enabled = !opening;

        Quaternion startRot = transform.rotation;
        Quaternion endRot = targetRotation;
        float speed = opening ? openSpeed : closeSpeed;
        float distance = Quaternion.Angle(startRot, endRot);

        if (distance < 0.01f)
        {
            isAnimating = false;
            isOpen = opening;
            yield break;
        }

        float progress = 0f;
        while (progress < 1f)
        {
            progress += Time.deltaTime * speed / distance;
            transform.rotation = Quaternion.Slerp(startRot, endRot, Mathf.Clamp01(progress));
            yield return null;
        }

        transform.rotation = endRot;
        isOpen = opening;
        isAnimating = false;
    }

    private void SetTargetRotation()
    {
        targetRotation = isOpen ? closedRotation * Quaternion.AngleAxis(openAngle, Vector3.up) : closedRotation;
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
    }

    // Подсказка на экране
    void OnGUI()
    {
        if (showHint && isLookingAtDoor && !isAnimating)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 20;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(Screen.width / 2 - 100, Screen.height / 2 + 50, 200, 30), "Нажмите E", style);
        }
    }

    // Визуализация в редакторе
    void OnDrawGizmosSelected()
    {
        if (Camera.main != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(Camera.main.transform.position, Camera.main.transform.forward * interactDistance);
        }
        if (physicalCollider != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(physicalCollider.bounds.center, physicalCollider.bounds.size);
        }
    }
}