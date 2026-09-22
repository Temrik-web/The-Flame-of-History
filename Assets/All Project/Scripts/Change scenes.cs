using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class NewBehaviourScript : MonoBehaviour
{
    private const string MusicVolumeKey = "music_volume_v1";

    [Header("Музыка меню")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume = 0.8f;
    [SerializeField] private Slider musicVolumeSlider;
    [SerializeField] private GameObject settingsPanel;

    private AudioSource musicSource;

    private void Awake()
    {
        settingsPanel = settingsPanel != null ? settingsPanel : FindSceneObject("SettingsPanel");
        SetupMusic();
        SetupMusicSlider();
    }

    public void LoadSceneByIndex(int sceneIndex)
    {
        SaveMusicVolume();
        SceneManager.LoadScene(sceneIndex);
    }
    
    public void QuitGame()
    {
        SaveMusicVolume();
        Application.Quit();
    }

    public void SetMusicVolume(float value)
    {
        value = Mathf.Clamp01(value);
        if (musicSource != null)
            musicSource.volume = value;

        PlayerPrefs.SetFloat(MusicVolumeKey, value);
        PlayerPrefs.Save();
    }

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

    private void SetupMusicSlider()
    {
        if (musicVolumeSlider == null && settingsPanel != null)
            musicVolumeSlider = settingsPanel.GetComponentInChildren<Slider>(true);
        if (musicVolumeSlider == null && settingsPanel != null)
            musicVolumeSlider = CreateMusicSlider(settingsPanel.transform);
        if (musicVolumeSlider == null) return;

        float value = PlayerPrefs.GetFloat(MusicVolumeKey, Mathf.Clamp01(defaultMusicVolume));
        musicVolumeSlider.minValue = 0f;
        musicVolumeSlider.maxValue = 1f;
        musicVolumeSlider.wholeNumbers = false;
        musicVolumeSlider.SetValueWithoutNotify(value);
        musicVolumeSlider.onValueChanged.RemoveListener(SetMusicVolume);
        musicVolumeSlider.onValueChanged.AddListener(SetMusicVolume);
    }

    private Slider CreateMusicSlider(Transform parent)
    {
        GameObject root = new GameObject("MusicVolumeSlider", typeof(RectTransform), typeof(Image), typeof(Slider));
        root.transform.SetParent(parent, false);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(0f, 20f);
        rootRect.sizeDelta = new Vector2(420f, 24f);

        Image background = root.GetComponent<Image>();
        background.color = new Color(0.12f, 0.12f, 0.12f, 0.9f);
        background.raycastTarget = true;
        Sprite uiSprite = CreateUiSprite();
        background.sprite = uiSprite;
        background.type = Image.Type.Sliced;

        Slider slider = root.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.transition = Selectable.Transition.ColorTint;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = 1f;

        RectTransform fillArea = CreateUiChild("Fill Area", root.transform);
        fillArea.anchorMin = Vector2.zero;
        fillArea.anchorMax = Vector2.one;
        fillArea.offsetMin = new Vector2(4f, 4f);
        fillArea.offsetMax = new Vector2(-4f, -4f);

        RectTransform fill = CreateUiChild("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = Vector2.zero;
        fill.offsetMax = Vector2.zero;
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        fillImage.sprite = uiSprite;
        fillImage.type = Image.Type.Sliced;
        slider.fillRect = fill;

        RectTransform handle = CreateUiChild("Handle", root.transform);
        handle.anchorMin = new Vector2(0f, 0.5f);
        handle.anchorMax = new Vector2(0f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(28f, 34f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = Color.white;
        handleImage.sprite = uiSprite;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;

        GameObject labelObject = new GameObject("MusicVolumeLabel", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(parent, false);
        RectTransform labelRect = (RectTransform)labelObject.transform;
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(0f, 58f);
        labelRect.sizeDelta = new Vector2(420f, 32f);
        Text label = labelObject.GetComponent<Text>();
        label.text = "Громкость музыки";
        label.alignment = TextAnchor.MiddleCenter;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 22;
        label.color = Color.white;
        label.raycastTarget = false;

        return slider;
    }

    private static RectTransform CreateUiChild(string name, Transform parent)
    {
        GameObject child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        return (RectTransform)child.transform;
    }

    private static Sprite CreateUiSprite()
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.name = "MusicVolumeUiTexture";
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f), 1f);
        sprite.name = "MusicVolumeUiSprite";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private void SaveMusicVolume()
    {
        float value = musicSource != null
            ? musicSource.volume
            : PlayerPrefs.GetFloat(MusicVolumeKey, Mathf.Clamp01(defaultMusicVolume));
        PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
        PlayerPrefs.Save();
    }

    private static GameObject FindSceneObject(string objectName)
    {
        GameObject active = GameObject.Find(objectName);
        if (active != null) return active;

        foreach (Transform transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (transform.name == objectName && transform.gameObject.scene.IsValid())
                return transform.gameObject;
        }
        return null;
    }

    private void OnApplicationQuit()
    {
        SaveMusicVolume();
    }
}
