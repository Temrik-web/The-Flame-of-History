using UnityEngine;

public class TimeController : MonoBehaviour
{
    // Скорость времени при зажатой кнопке
    [SerializeField] private float boostedTimeScale = 3f; 
    
    // Стандартная скорость времени (обычно 1)
    private float normalTimeScale = 1f;

    void Update()
    {
        // Если кнопка W зажата
        if (Input.GetKey(KeyCode.W))
        {
            Time.timeScale = boostedTimeScale;
            // Корректируем fixedDeltaTime, чтобы физика работала плавно при ускорении
            Time.fixedDeltaTime = 0.02f * Time.timeScale;
        }
        else
        {
            Time.timeScale = normalTimeScale;
            Time.fixedDeltaTime = 0.02f * normalTimeScale;
        }
    }

    // Сбрасываем скорость времени в нормальное состояние, если объект будет уничтожен
    void OnDisable()
    {
        Time.timeScale = normalTimeScale;
    }
}