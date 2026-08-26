using UnityEngine;
using System.Collections;

/// <summary>
/// Простая тряска камеры. Используется системой подавления (SuppressionReceiver)
/// для эффекта близких пролётов пуль.
/// </summary>
public class CameraShake : MonoBehaviour
{
    [Header("Настройки тряски")]
    [SerializeField] private float defaultDuration = 0.15f;
    [SerializeField] private float defaultMagnitude = 0.1f;
    [SerializeField] private float dampingSpeed = 2f;

    private Vector3 _originalLocalPosition;
    private Coroutine _shakeRoutine;

    private void Awake()
    {
        _originalLocalPosition = transform.localPosition;
    }

    /// <summary>Запустить тряску с указанными параметрами.</summary>
    public void Shake(float duration, float magnitude)
    {
        if (duration <= 0f || magnitude <= 0f) return;

        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);

        _shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    /// <summary>Метод, вызываемый SuppressionReceiver для тряски.</summary>
    public void TriggerShake(float duration, float magnitude)
    {
        Shake(duration > 0f ? duration : defaultDuration,
              magnitude > 0f ? magnitude : defaultMagnitude);
    }

    private IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;
            float z = Random.Range(-1f, 1f) * magnitude * 0.3f;

            transform.localPosition = _originalLocalPosition + new Vector3(x, y, z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Плавное возвращение в исходную позицию
        while (Vector3.Distance(transform.localPosition, _originalLocalPosition) > 0.001f)
        {
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                _originalLocalPosition,
                dampingSpeed * Time.deltaTime);

            yield return null;
        }

        transform.localPosition = _originalLocalPosition;
    }
}