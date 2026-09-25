using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro; 

public class AsyncLoader : MonoBehaviour
{
    [Header("Настройки UI")]
    [SerializeField] private TextMeshProUGUI progressText; 

    
    public static int SceneToLoad = 3; // Нужно, чтобы фоновый загрузчик подгружал сцену 3, то есть игру и мог выводить проценты

    void Start()
    {
        // Как только загрузочная сцена открылась, сразу запускаем фоновую загрузку
        StartCoroutine(LoadLevelAsync());
    }

    public IEnumerator LoadLevelAsync()
    {
        // Запускаем асинхронную загрузку сцены в фоновом потоке
        AsyncOperation operation = SceneManager.LoadSceneAsync(SceneToLoad);

       
        while (!operation.isDone)
        {
            // Unity возвращает прогресс от 0.0 до 0.9. Последние 0.1 - это финальная активация сцены
         
            float progress = Mathf.Clamp01(operation.progress / 0.9f);

            // Обновляем проценты текстом
            if (progressText != null)
            {
                progressText.text = $"Загрузка мира: {Mathf.RoundToInt(progress * 100)}%";
            }

            // Ждем следующего кадра перед продолжением цикла
            yield return null;
        }
    }
}