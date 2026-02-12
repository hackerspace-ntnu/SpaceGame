using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;
    
    public AudioMixer mixer;
    
    [Header("Volume Settings")]
    [SerializeField, Range(0f, 1f)]
    private float musicVolume = 0.5f;

    
    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            musicVolume = Mathf.Clamp01(value);
            SetMusicVolume();
        }
    }

    [SerializeField, Range(0f, 1f)]
    private float sfxVolume = 0.5f;

    public float SFXVolume
    {
        get => sfxVolume;
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            SetSFXVolume();
        }
    }
    
    [SerializeField, Range(0f, 1f)]
    private float uiVolume = 0.5f;

    public float UIVolume
    {
        get => uiVolume;
        set
        {
            uiVolume = Mathf.Clamp01(value);
            SetUIVolume();
        }
    }
    
    [Header("Audio Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;
    public AudioSource uiSource;

    [Header("Audio Clips")]
    public AudioClip bgm;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        SetMusicVolume();
        SetSFXVolume();
        PlayMusic(bgm);
    }

    public void PlayMusic(AudioClip clip)
    {
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    public void StopMusic()
    {
        musicSource.Stop();
    }

    public void PlaySFX(AudioClip clip)
    {
        sfxSource.PlayOneShot(clip);
    }
    
    public void PlayUI(AudioClip clip)
    {
        uiSource.PlayOneShot(clip);
    }
    
    private void SetMusicVolume()
    {
        float volume = musicVolume <= 0.0001f ? -80f : Mathf.Log10(musicVolume) * 20f;
        mixer.SetFloat("BGMVol", volume);
    }

    private void SetSFXVolume()
    {
        float volume = sfxVolume <= 0.0001f ? -80f : Mathf.Log10(sfxVolume) * 20f;
        mixer.SetFloat("SFXVol", volume);
    }

    private void SetUIVolume()
    {
        float volume = uiVolume <= 0.0001f ? -80f : Mathf.Log10(uiVolume) * 20f;
        mixer.SetFloat("UIVol", volume);
    }
    
    private void OnValidate()
    {
        if (!Application.isPlaying) return;

        SetMusicVolume();
        SetSFXVolume();
        SetUIVolume();
    }
}