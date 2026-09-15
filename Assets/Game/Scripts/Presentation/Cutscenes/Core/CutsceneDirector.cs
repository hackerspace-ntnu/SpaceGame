using System;
using System.Collections;
using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Diagnostics;

namespace SpaceGame.Presentation
{
    // Local-per-client cutscene playback. Locks the subject (player or AI agent), runs a
    // Cutscene's coroutine, restores on end (even if Play throws). One cutscene at a time;
    // concurrent Play() rejects.
    public class CutsceneDirector : MonoBehaviour
    {
        public static CutsceneDirector Instance { get; private set; }

        public bool IsPlaying { get; private set; }

        public event Action<Cutscene> OnCutsceneStarted;
        public event Action<Cutscene> OnCutsceneEnded;

        // What the running cutscene took, held on the director rather than in the routine's
        // locals so EndCutscene can give it back from outside the routine — which is the only
        // place left to give it back from once the routine has died.
        private Cutscene playing;
        private PlayerController lockedPlayer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Play a cutscene with the local player as the subject (legacy convenience).</summary>
        public bool Play(Cutscene cutscene) => Play(cutscene, subject: null);

        /// <summary>
        /// Play a cutscene with an explicit subject. The subject is whichever entity the
        /// cutscene is "about" — usually the player walking through a door, but it could be
        /// an AI agent in scripted sequences. If null, falls back to the local PlayerController.
        /// </summary>
        public bool Play(Cutscene cutscene, GameObject subject)
        {
            if (cutscene == null)
            {
                Debug.LogWarning("[CutsceneDirector] Play called with null cutscene.");
                return false;
            }
            if (IsPlaying)
            {
                Debug.LogWarning($"[CutsceneDirector] Rejecting '{cutscene.name}' — another cutscene is already playing.");
                return false;
            }

            // Guarded with EndCutscene as the teardown, and that is load-bearing rather than
            // tidy: a cutscene that dies mid-play leaves IsPlaying true forever, which refuses
            // every later cutscene, keeps the player in cutscene mode with no controls, and
            // leaves the bars across the screen.
            StartCoroutine(Fault.Coroutine(
                this, "CutsceneDirector.RunCutscene", RunCutscene(cutscene, subject), EndCutscene));
            return true;
        }

        private IEnumerator RunCutscene(Cutscene cutscene, GameObject subject)
        {
            IsPlaying = true;

            PlayerController player = ResolvePlayer(subject);
            if (player == null)
            {
                Debug.LogError("[CutsceneDirector] No PlayerController for cutscene subject; aborting cutscene.");
                EndCutscene();
                yield break;
            }

            Camera cam = player.PlayerCamera;
            var ctx = new CutsceneContext(player, cam, subject != null ? subject : player.gameObject);

            lockedPlayer = player;
            playing = cutscene;

            player.EnterCutsceneMode();
            LetterboxOverlay.Instance.ShowBarsAsync(0.4f);
            OnCutsceneStarted?.Invoke(cutscene);

            // try/finally around a coroutine: we can't yield inside a try-with-finally that
            // catches, but we can wrap the iteration manually so restore always runs.
            IEnumerator inner = cutscene.Play(ctx);
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    break;
                }
                yield return current;
            }

            EndCutscene();
        }

        /// <summary>
        /// Gives back everything a cutscene took: the player's controls, the bars and the flag.
        ///
        /// <para>
        /// The tail of <see cref="RunCutscene"/> and its teardown, so the two can never disagree.
        /// Idempotent and safe before the cutscene got as far as taking the screen — the abort
        /// path reaches it too, and reaches it with nothing to hand back but the flag.
        /// </para>
        /// </summary>
        private void EndCutscene()
        {
            if (!IsPlaying) return;
            IsPlaying = false;

            if (lockedPlayer != null) lockedPlayer.ExitCutsceneMode();
            lockedPlayer = null;

            // Null when the cutscene never started: no bars were shown and OnCutsceneStarted never
            // fired, so announcing an end nobody was told about would be a lie to every listener.
            if (playing == null) return;

            Cutscene finished = playing;
            playing = null;

            LetterboxOverlay.Instance.HideBarsAsync(0.4f);
            OnCutsceneEnded?.Invoke(finished);
        }

        /// <summary>
        /// The player a cutscene about <paramref name="subject"/> locks: the one the subject sits
        /// under, else the one this machine drives.
        ///
        /// <para>
        /// Never a scene search. Every peer holds a PlayerController per player in the session, and
        /// FindFirstObjectByType answered with whichever spawned first — in a six-player arrival
        /// that locked, blacked out and rigged a crewmate's (inactive) camera on most machines,
        /// so the local player kept their HUD and got no shake, no blur and no seated look, while
        /// the one machine whose own body happened to come first got all of it.
        /// </para>
        /// </summary>
        public static PlayerController ResolvePlayer(GameObject subject)
        {
            if (subject != null)
            {
                var p = subject.GetComponentInParent<PlayerController>();
                if (p != null) return p;
            }
            return GameplayMenuScope.FindLocalPlayer();
        }
    }
}
