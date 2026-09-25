using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Обслуживает кнопку «Продолжить» на Canvas главного меню.</summary>




[RequireComponent(typeof(Button))]
public class ContinueGameButton : MonoBehaviour
{

    // Показывает кнопку доступной, только если есть сохранённая игра.
    private void Awake()
    {
        GetComponent<Button>().interactable = PlayerPrefs.HasKey(InventorySystem.DefaultSaveKey);
        
    }

    /// <summary>Открывает сохранённую игру по нажатию кнопки.</summary>
    public void ContinueGame()
    {
        if (!PlayerPrefs.HasKey(InventorySystem.DefaultSaveKey))
            return; // ЗАЩИТА ОТ БАРАНОВ!

        Time.timeScale = 1f;
        
        SceneManager.LoadScene(2);
    }
}
