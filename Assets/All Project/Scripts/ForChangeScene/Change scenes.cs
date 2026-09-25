using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Управляет переходами из главного меню и воспроизведением его музыки.
/// Элементы интерфейса настраиваются в сцене.
/// </summary>
public class NewBehaviourScript : MonoBehaviour
{
    // Ключ сохранённой громкости и имя игровой сцены.
    private const string MusicVolumeKey = "music_volume_v1";
    private const string GameSceneName = "Game";

    [SerializeField] private AudioClip menuMusic;
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume = 0.8f;

    private AudioSource musicSource;

    /// <summary>Текущая громкость музыки меню.</summary>
    public float CurrentMusicVolume => musicSource != null
        ? musicSource.volume
        : PlayerPrefs.GetFloat(MusicVolumeKey, Mathf.Clamp01(defaultMusicVolume));

    // Подготавливает источник звука и запускает музыку при открытии меню.
    private void Awake()
    {
        SetupMusic();
    }

    /// <summary>Сбрасывает прежнюю игру, сохраняя громкость, и открывает новую.</summary>
    public void StartNewGame()
    {
        float volume = PlayerPrefs.GetFloat(MusicVolumeKey, Mathf.Clamp01(defaultMusicVolume));
        PlayerPrefs.DeleteAll();
        PlayerPrefs.SetFloat(MusicVolumeKey, volume);
        PlayerPrefs.Save();

        GameState.ResetAll(false);
        QuestSystem.ResetAll(false);
        PlayerInputLock.Clear();
        Time.timeScale = 1f;
        SaveMusicVolume();
        SceneManager.LoadScene(GameSceneName);
    }

    /// <summary>Открывает сцену по её индексу в настройках сборки.</summary>
    public void LoadSceneByIndex(int sceneIndex)
    {
        SaveMusicVolume();
        SceneManager.LoadScene(sceneIndex);
    }

    /// <summary>Сохраняет громкость и закрывает приложение.</summary>
    public void QuitGame()
    {
        SaveMusicVolume();
        Application.Quit();
    }

    /// <summary>Меняет громкость музыки меню и запоминает её для следующих запусков.</summary>
    public void SetMusicVolume(float value)
    {
        value = Mathf.Clamp01(value);
        if (musicSource != null)
            musicSource.volume = value;

        PlayerPrefs.SetFloat(MusicVolumeKey, value);
        PlayerPrefs.Save();
    }

    // Использует AudioSource этого объекта и применяет сохранённую громкость.
    private void SetupMusic()
    {
        musicSource = GetComponent<AudioSource>();
        if (musicSource == null)
            musicSource = gameObject.AddComponent<AudioSource>();

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.clip = menuMusic;
        musicSource.volume = PlayerPrefs.GetFloat(MusicVolumeKey, Mathf.Clamp01(defaultMusicVolume));

        if (menuMusic != null && !musicSource.isPlaying)
            musicSource.Play();
    }

    // Запоминает фактическую громкость перед переходом или выходом.
    private void SaveMusicVolume()
    {
        PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(CurrentMusicVolume));
        PlayerPrefs.Save();
    }

    // Сохраняет громкость при закрытии приложения извне меню.
    private void OnApplicationQuit()
    {
        SaveMusicVolume();
    }
}
