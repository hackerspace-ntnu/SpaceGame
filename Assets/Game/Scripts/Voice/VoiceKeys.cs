// The two voice keys — push-to-talk and mute — as InputActions of their own.
//
// Not on the player's input asset, deliberately. PlayerInputManager lives on the player prefab, and
// in the LOBBY there is no player at all; in the world it is switched off whenever a menu takes
// control. Voice has to answer in both places, so these are built in code — the third route
// CoreServices.md gives for "a button that must stay live while the component is disabled" — and
// rebuilt whenever the player rebinds them.
//
// They are not gameplay hotkeys, so they do not gate on GameplayMenuScope.AcceptsGameplayInput:
// holding push-to-talk with the pause menu open should still talk. The one thing they stand down
// for is typing (TextEntry.IsTyping), and a rebind in progress (Suspended), where the key being
// pressed is the new binding, not a command.
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Core;

namespace SpaceGame.Voice
{
    /// <summary>Reads the voice keys once a frame and keeps them matched to the player's bindings.</summary>
    public sealed class VoiceKeys : IDisposable
    {
        private readonly PushToTalkLatch latch = new PushToTalkLatch();

        private InputAction talk;
        private InputAction mute;

        // Null until the first Tick, so the first Tick always builds.
        private string talkPath;
        private string mutePath;

        /// <summary>
        /// Set while a settings row is capturing a new key, so the press that becomes the binding
        /// does not also mute the player or open their microphone.
        /// </summary>
        public static bool Suspended { get; set; }

        /// <summary>Whether the push-to-talk channel is open — held, in its release tail, or latched.</summary>
        public bool TalkOpen { get; private set; }

        public void Tick(float deltaTime)
        {
            Rebuild(ref talk, ref talkPath, GameSettings.VoicePushToTalkBinding, "VoicePushToTalk");
            Rebuild(ref mute, ref mutePath, GameSettings.VoiceMuteBinding, "VoiceMute");

            bool listening = !Suspended && !TextEntry.IsTyping;

            if (listening && mute != null && mute.WasPressedThisFrame())
                GameSettings.VoiceSelfMuted = !GameSettings.VoiceSelfMuted;

            bool held = listening && talk != null && talk.IsPressed();
            bool pressed = listening && talk != null && talk.WasPressedThisFrame();

            TalkOpen = latch.Step(GameSettings.VoicePushToTalkToggle, pressed, held, deltaTime);
        }

        /// <summary>Closes the channel — a session ended, and nobody should rejoin mid-sentence.</summary>
        public void ResetLatch()
        {
            latch.Reset();
            TalkOpen = false;
        }

        public void Dispose()
        {
            Release(ref talk);
            Release(ref mute);
            talkPath = null;
            mutePath = null;
        }

        private static void Rebuild(ref InputAction action, ref string current, string wanted, string name)
        {
            wanted ??= string.Empty;
            if (current != null && wanted == current) return;

            Release(ref action);
            current = wanted;

            // An empty binding is a real choice — the mute key is unbound by default.
            if (wanted.Length == 0) return;

            try
            {
                action = new InputAction(name, InputActionType.Button, wanted);
                action.Enable();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Voice] Could not bind {name} to \"{wanted}\": {e.Message}");
                Release(ref action);
            }
        }

        private static void Release(ref InputAction action)
        {
            if (action == null) return;

            action.Disable();
            action.Dispose();
            action = null;
        }
    }
}
