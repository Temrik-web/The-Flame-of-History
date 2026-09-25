using UnityEngine;
using UnityEngine.SceneManagement;

public class ButtonAsync : MonoBehaviour
{
    // В Unity в это поле мы введем индекс (номер) сцены загрузки
    [SerializeField] private int loadingSceneIndex = 2; 
    
    // В это поле мы введем индекс сцены самой игры, куда хотим попасть
    [SerializeField] private int gameplaySceneIndex = 3;

    // Этот метод мы привяжем к кнопке «Играть»
    public void PlayGame()
    {
        // 1. Записываем в наш загрузчик, какую сцену ему нужно будет включить ПОСЛЕ загрузки
        AsyncLoader.SceneToLoad = gameplaySceneIndex;

        // 2. Открываем саму сцену загрузки
        SceneManager.LoadScene(loadingSceneIndex);
    }
}