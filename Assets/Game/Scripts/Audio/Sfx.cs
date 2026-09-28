using System;
using System.Collections.Generic;
using FMOD.Studio;
using FMODUnity;
using Mono.Cecil.Cil;
using UnityEngine;

namespace SpaceGame.Audio
{
    public static class Sfx
    {

        private static HashSet<EventReference> Babying = new HashSet<EventReference>();
        
        //Plays oneshot, no transform. for menus, HUD, UI
        public static void Play(EventReference eventReference)
        {
            RuntimeManager.PlayOneShot(eventReference);    
        }
        
        //plays oneshot with pos
        public static void Play(EventReference eventReference, Vector3 position)
        {
            PlayInternal(eventReference, position, null);    
        }

        //plays oneshot at transform pos. no ratelimiting bullshit
        public static void Play(EventReference eventReference, Transform source)
        {
            PlayInternal(eventReference, source.position, null);    
        }

        public static void PlayAttached(EventReference eventReference, GameObject attachTo)
        {
            PlayInternal(eventReference, Vector3.zero, attachTo);
        }            
        //Play oneshot with a parameter set
        public static void PlayWithParameter(EventReference eventReference, string parameterName, float parameter)
        {
            PlayInternal(eventReference, Vector3.zero, null, parameterName, parameter);
        }
        public static void PlayWithParameter(EventReference eventReference, Vector3 position, string parameterName, float parameter)
        {
            PlayInternal(eventReference, position, null, parameterName, parameter);
        }
        public static void PlayWithParameter(EventReference eventReference, GameObject attachTo, string parameterName, float parameter)
        {
            PlayInternal(eventReference, Vector3.zero, attachTo, parameterName, parameter);
        }
        

        private static void PlayInternal(EventReference eventRef, Vector3 position, GameObject attachTo, string? paramName= null, float? parameter = null)
        {
            if (eventRef.IsNull && Babying.Add(eventRef))
            {
                Debug.LogWarning($"FUCK YOU BITCH {eventRef.ToString()} IS NOTHING, QUIET, NADA");
                return;
            }
            
            try
            {
                // PlayOneShot cannot take a volume, so anything trimmed has to go the long way round.
                if (!parameter.HasValue)
                {
                    if (attachTo == null)
                    {
                        RuntimeManager.PlayOneShot(eventRef, position);
                    }
                    else
                    {
                        RuntimeManager.PlayOneShotAttached(eventRef, attachTo);
                    }
                }
                else
                {
                    EventInstance instance = RuntimeManager.CreateInstance(eventRef);
                    instance.setParameterByName(paramName, parameter.Value); 
                    if (attachTo != null)
                    {
                        RuntimeManager.AttachInstanceToGameObject(instance, attachTo);
                    }
                    else
                    {
                        instance.set3DAttributes(RuntimeUtils.To3DAttributes(position));
                    }

                    instance.start();
                    // Released immediately: FMOD keeps it alive until it finishes, then reclaims it.
                    // Skipping this is the classic way to leak every one-shot the game ever plays.

                    // uhhh... yeah whatever
                    instance.release();
                }
            }
            catch (EventNotFoundException)
            {
                if (Babying.Add(eventRef))
                {
                    Debug.LogWarning($"{eventRef} is not in any loaded bank. " +
                                     "Check the bank list and the event path.");
                }
            }
            catch (Exception e)
            {
                // catch everythin else. Blablabla
                if (Babying.Add(eventRef))
                {
                    Debug.LogWarning($"[Audio] {eventRef} could not be played ({e.GetType().Name}: " +
                                     $"{e.Message}). Audio is unavailable; gameplay continues.");
                }
            }
        }
    
        public class Looper
        {
            private EventInstance instance;
            private bool daShitWorks => instance.isValid();
            private bool started => instance.isValid() &&
                                    instance.getPlaybackState(out PLAYBACK_STATE x) == FMOD.RESULT.OK &&
                                    x == PLAYBACK_STATE.PLAYING; 
        
            private void InitSound(EventReference daLoopinSoundEfffect)
            {
                try
                {
                    instance = RuntimeManager.CreateInstance(daLoopinSoundEfffect);
                }
                catch (EventNotFoundException)
                {
                    Debug.LogWarning($"[Audio] Looping event '{daLoopinSoundEfffect}' is not in any loaded bank.");
                    return;
                }
            }
            public void PlayUI(EventReference daLoopinSoundEfffect)
            {
                InitSound(daLoopinSoundEfffect);
                instance.start();
            }

            public void PlayAndAttach(EventReference daLoopinSoundEfffect, GameObject attachTo)
            {
                if (!daShitWorks) return;
                if (started) return;
                InitSound(daLoopinSoundEfffect);
                if (attachTo != null) RuntimeManager.AttachInstanceToGameObject(instance, attachTo);
                instance.start();
            }
            public void PlayAndPosition(EventReference daLoopinSoundEfffect, Vector3 pos)
            {
                if (!daShitWorks) return;
                if (started) return;
                InitSound(daLoopinSoundEfffect);
                instance.set3DAttributes(RuntimeUtils.To3DAttributes(pos));
                instance.start();
            }

            public void Stop(bool allowFadeOut = true)
            {
                if (!daShitWorks) return;
                RuntimeManager.DetachInstanceFromGameObject(instance);
                instance.stop(allowFadeOut ? FMOD.Studio.STOP_MODE.ALLOWFADEOUT : FMOD.Studio.STOP_MODE.IMMEDIATE);
                instance.release();
                instance.clearHandle();                
            }
        
            public void SetParameter(string name, float value)
            {
                if (!started || string.IsNullOrEmpty(name)) return;
                instance.setParameterByName(name, value);
            }
        }
    }
}
