using UnityEngine;
using UnityEngine.UI;

/// <summary>Устанавливает начальное положение слайдера из сохранённой громкости.</summary>
[RequireComponent(typeof(Slider))]
public class MenuMusicSlider : MonoBehaviour
{
    [SerializeField] private NewBehaviourScript menuMusic;

    // Синхронизирует слайдер без вызова события изменения громкости.
    private void Start()
    {
        if (menuMusic != null)
            GetComponent<Slider>().SetValueWithoutNotify(menuMusic.CurrentMusicVolume);
    }
}
