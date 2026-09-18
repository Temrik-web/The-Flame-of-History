using System.Collections;
using UnityEngine;
using TMPro;   

public class TypewriterUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textComponent;
    [SerializeField] private float timeBetweenCharacters = 0.05f; // Скорость печати
    [SerializeField] private float delayBeforeHide = 2.0f; // Пауза перед очисткой/следующим текстом

    // Тестовый массив строк для проверки в инспекторе Unity
    [SerializeField] private string[] testTexts = new string[] { "Первый текст.", "Второй текст." };

    private Coroutine typewriterCoroutine;

    // Метод принимает массив строк (можно передать 2 или сколько угодно текстов)
    public void StartTypewriter(string[] textsToPrint)
    {
        if (typewriterCoroutine != null)
        {
            StopCoroutine(typewriterCoroutine);
        }

        typewriterCoroutine = StartCoroutine(TypeTextRoutine(textsToPrint));
    }

    private IEnumerator TypeTextRoutine(string[] textsToPrint)
    {
        // Перебираем каждый текст в массиве по очереди
        foreach (string textToPrint in textsToPrint)
        {
            textComponent.text = textToPrint;
            textComponent.maxVisibleCharacters = 0; // Скрываем буквы

            // Ждем кадр для корректного рассчета тегов TextMeshPro
            yield return null; 

            int totalVisibleCharacters = textToPrint.Length;
            int counter = 0;

            // Печатаем текущий текст
            while (counter <= totalVisibleCharacters)
            {
                textComponent.maxVisibleCharacters = counter;
                counter++;

                yield return new WaitForSeconds(timeBetweenCharacters);
            }

            // Ждем заданное время, пока игрок читает текущий текст
            yield return new WaitForSeconds(delayBeforeHide);
        }

        // После показа всех текстов полностью очищаем поле
        textComponent.text = ""; 
        textComponent.maxVisibleCharacters = 0;
    }

    private void Start()
    {
        // Запуск теста при старте игры
        if (testTexts != null && testTexts.Length > 0)
        {
            StartTypewriter(testTexts);
        }
    }
}
