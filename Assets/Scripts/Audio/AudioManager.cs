using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.Audio;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;
    
    [Header("Volume Settings")]
    [SerializeField, Range(0f, 1f)] private float musicVolume =  1f;
    [SerializeField, Range(0f, 1f)] private float sfxVolume =    1f;
    [SerializeField, Range(0f, 1f)] private float reverbVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float uiVolume =     1f;

    public float SfxVolume { get => sfxVolume; set => sfxVolume = value; }
    public float MusicVolume { get => musicVolume; set => musicVolume = value; }
    public float ReverbVolume { get => reverbVolume; set => reverbVolume = value; }
    public float UIVolume { get => uiVolume; set => uiVolume = value; }
    

    [Header("Audio Busses")]
    private Bus music;
    private Bus sfx;
    private Bus ui;
    private Bus reverb;


    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);

        Instance = this;

        music = RuntimeManager.GetBus("bus:/Music");
        sfx = RuntimeManager.GetBus("bus:/SFX");
        ui = RuntimeManager.GetBus("bus:/UI");
        reverb = RuntimeManager.GetBus("bus:/Reverb");

        return;
    }

    public void PlayTestMusic()
    {
        RuntimeManager.PlayOneShot("event:/Music/TestSong");
    } //Todo: Make more flexible
    
    public void PlayEvent(EventReference myevent) {
        RuntimeManager.PlayOneShot(myevent);
    }

    public void PlayEvent(EventReference myevent, Vector3 position) {
        RuntimeManager.PlayOneShot(myevent, position);
    }

    public void PlayAndAttachEvent(EventReference myevent, GameObject gameObject, Rigidbody rigidbody)
    {
        FMOD.Studio.EventInstance aSoundInstance = RuntimeManager.CreateInstance(myevent);
        aSoundInstance.start();        
        RuntimeManager.AttachInstanceToGameObject(aSoundInstance, gameObject, rigidbody);
    }

    public void PlaySFX(string sound)
    {
        RuntimeManager.PlayOneShot(sound);
    } //Todo: Find easy way to hear and assign sound effects:
    
    public void PlaySFX3d(string sound, Vector3 worldPos)
    {
        RuntimeManager.PlayOneShot(sound, worldPos);
    }
    
    private void OnValidate()
    {
        if (!Application.isPlaying) return; //Todo: Når spiller endrer på volume sliders, oppdater disse;
        
        music.setVolume(musicVolume);
        sfx.setVolume(sfxVolume);
        reverb.setVolume(reverbVolume);
        ui.setVolume(uiVolume);
    }
}
