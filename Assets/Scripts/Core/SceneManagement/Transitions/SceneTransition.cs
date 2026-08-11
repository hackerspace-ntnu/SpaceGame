using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Gameplay;

namespace SpaceGame.Core
{
    /// <summary>
    /// Drop-in scene transition orchestrator.
    ///
    /// Place this on any GameObject that should send an initiator (player or AI agent) to
    /// another scene. It does no triggering of its own — pair it with a trigger component:
    ///
    ///   • InteractableTrigger — fires when the player interacts with this object.
    ///   • VolumeTrigger       — fires when a player or agent enters a trigger volume.
    ///   • From script         — call <see cref="Trigger"/> directly.
    ///
    /// What it does, in order:
    ///   1. Plays all assigned effects in parallel (fade, audio muffle, camera shake, ...).
    ///   2. Asks the destination to apply itself (additive load + place initiator at anchor).
    ///   3. Tells the effects the load is done; waits for their "in" phase to finish.
    ///
    /// Effects must use different <see cref="TransitionChannel"/>s — two effects on the same
    /// channel will fight each other. The inspector warns at edit-time if this happens.
    ///
    /// The transition is reentry-guarded by an internal busy flag, so multiple triggers
    /// firing on the same frame all call back into the same single transition safely.
    /// </summary>
    [AddComponentMenu("Scene Management/Scene Transition")]
    public class SceneTransition : MonoBehaviour, ITriggerable
    {
        [TextArea(6, 12)]
        [SerializeField] private string description =
            "Drop-in scene transition.\n" +
            "• Destination: which scene + spawn anchor (ScriptableObject).\n" +
            "• Effects: visual/audio effects that play during the load. Multiple allowed,\n" +
            "  but each must use a different TransitionChannel (Screen/Audio/Camera/Time).\n" +
            "• Pair with an InteractableTrigger or VolumeTrigger on the same GameObject,\n" +
            "  or call Trigger(initiator) from script, to fire the transition.\n" +
            "• Effects play during load. When the load finishes, the 'in' phase of each\n" +
            "  effect runs and the transition completes.\n" +
            "• Spacebar skips effects (skip is ignored until the load completes).";

        [Header("Configuration")]
        [SerializeField] private SceneDestination destination;
        [SerializeField] private SceneTransitionEffect[] effects;

        [Tooltip("After this transition fires for an initiator, that initiator cannot be moved by ANY " +
                 "SceneTransition for this many seconds. Prevents an exit volume from re-firing on the " +
                 "spawn frame inside the destination, or an entrance from re-firing as the player walks back out.")]
        [SerializeField] private float postTransitionLockoutSeconds = 1f;

        // Initiator → unscaled-time when the post-transition lockout ends.
        // Static so the gate is shared across every SceneTransition in every scene
        // (entrance and exit are different components on different GameObjects).
        private static readonly System.Collections.Generic.Dictionary<int, float> s_lockoutUntil = new();

        private bool busy;
        private GameObject lastInitiator;
        private float busySetAt;
        private const float BusyMaxSeconds = 20f;

        private void Update()
        {
            // Self-heal: if busy has been stuck longer than any plausible transition,
            // something didn't clean up (host destroyed, coroutine cancelled, scene
            // unload mid-flight). Force-clear so the volume can fire again next time.
            if (busy && Time.unscaledTime - busySetAt > BusyMaxSeconds)
            {
                Debug.LogWarning($"[SceneTransition] '{name}' (instId={GetInstanceID()}) busy stuck for {Time.unscaledTime - busySetAt:0.0}s — force-clearing. Original initiator={(lastInitiator != null ? lastInitiator.name : "<null>")}", this);
                busy = false;
                lastInitiator = null;
            }
        }

        public bool IsBusy => busy;
        public SceneDestination Destination => destination;

        /// <summary>
        /// The GameObject that fired the currently-running transition (the player or AI agent).
        /// Effects that need to know who's being transported read this on Begin(). Null between
        /// transitions.
        /// </summary>
        public GameObject LastInitiator => lastInitiator;

        public bool CanTrigger(GameObject initiator)
        {
            // Dedup throttles by (instId, reason) once per second so we see flips
            // (busy↔lockout) without flooding when nothing changes.
            if (busy)
            {
                LogDeniedThrottled("busy", $"lastInitiator={(lastInitiator != null ? lastInitiator.name : "<null>")}, frame={Time.frameCount}");
                return false;
            }
            if (initiator == null) return false;
            if (destination == null || !destination.IsValid())
            {
                LogDeniedThrottled("dest-invalid", $"destination={(destination == null ? "null" : "IsValid=false")}, InteriorManager.Instance={(InteriorManager.Instance == null ? "<null>" : "alive")}");
                return false;
            }
            if (IsLockedOut(initiator))
            {
                LogDeniedThrottled("locked-out", $"initiator={initiator.name}, remaining={s_lockoutUntil[initiator.GetInstanceID()] - Time.unscaledTime:0.00}s");
                return false;
            }
            return true;
        }

        private static readonly Dictionary<(int, string), float> s_lastDeniedLog = new();
        private void LogDeniedThrottled(string reason, string extra)
        {
            var key = (GetInstanceID(), reason);
            if (s_lastDeniedLog.TryGetValue(key, out var last) && Time.unscaledTime - last < 1f) return;
            s_lastDeniedLog[key] = Time.unscaledTime;
            Debug.Log($"[SceneTransition] '{name}' (instId={GetInstanceID()}) CanTrigger=false: {reason} ({extra})", this);
        }

        private static bool IsLockedOut(GameObject initiator)
        {
            int key = initiator.GetInstanceID();
            if (!s_lockoutUntil.TryGetValue(key, out var until)) return false;
            if (Time.unscaledTime >= until)
            {
                s_lockoutUntil.Remove(key);
                return false;
            }
            return true;
        }

        /// <summary>Fire the transition for the given initiator. Returns null if not eligible.</summary>
        public Coroutine Trigger(GameObject initiator)
        {
            if (!CanTrigger(initiator))
            {
                // Diagnostic so we can see WHY repeat triggers are being denied — the only
                // way for the trigger to silently no-op. Remove once round-trip is solid.
                string reason =
                    busy ? "busy" :
                    initiator == null ? "no-initiator" :
                    destination == null ? "no-destination" :
                    !destination.IsValid() ? "destination-invalid" :
                    IsLockedOut(initiator) ? "locked-out" :
                    "unknown";
                Debug.Log($"[SceneTransition] '{name}' Trigger denied ({reason}) for {(initiator != null ? initiator.name : "null")}", this);
                return null;
            }
            busy = true;
            busySetAt = Time.unscaledTime;
            lastInitiator = initiator;
            Debug.Log($"[SceneTransition] '{name}' (instId={GetInstanceID()}) Trigger firing for {initiator.name} [busy<-true, frame={Time.frameCount}]", this);
            // Run on TransitionRunner (DontDestroyOnLoad). The host GameObject may be
            // inside a scene that the destination unloads — if the coroutine ran on us,
            // it would die mid-transition and effects would never receive End().
            return TransitionRunner.Instance.Run(Run(initiator));
        }

        private IEnumerator Run(GameObject initiator)
        {
            string label = name;
            var handles = new List<EffectHandle>();
            int instId = GetInstanceID();

            if (effects != null)
            {
                foreach (var e in effects)
                {
                    if (e == null) continue;
                    var handle = e.Begin(this);
                    if (handle != null) handles.Add(handle);
                }
            }

            // Arm the cross-transition lockout BEFORE the destination runs. The destination
            // can teleport the initiator into another SceneTransition's volume (e.g. an exit
            // teleporting the player back into the entrance volume); without an early lockout,
            // that volume's OnTriggerEnter fires on the same frame and re-fires the entrance
            // before we've had a chance to clear our own busy. Refresh the lockout again
            // after Apply so the full duration always extends past landing.
            if (initiator != null && postTransitionLockoutSeconds > 0f)
                s_lockoutUntil[initiator.GetInstanceID()] = Time.unscaledTime + postTransitionLockoutSeconds;

            // Wait for any effect that wants to block the load (e.g. a walk-through
            // cutscene that must play before the teleport). Out-phases run in parallel —
            // we yield each one in turn, so total wait is the slowest.
            foreach (var h in handles) yield return h.AwaitOutPhase();

            Debug.Log($"[SceneTransition] '{label}' out-phase done; applying destination.");
            yield return destination.Apply(initiator);
            Debug.Log($"[SceneTransition] '{label}' destination applied; running in-phase.");

            if (initiator != null && postTransitionLockoutSeconds > 0f)
                s_lockoutUntil[initiator.GetInstanceID()] = Time.unscaledTime + postTransitionLockoutSeconds;

            // Clear busy as soon as the destination has landed — the remaining work
            // (in-phase fades) is cosmetic and must not gate the next trigger. If we
            // wait until AwaitCompletion and that hangs or throws, busy never clears
            // and the volume becomes permanently dead (the original bug).
            if (this != null)
            {
                busy = false;
                lastInitiator = null;
                Debug.Log($"[SceneTransition] '{label}' (instId={instId}) destination done; busy cleared [frame={Time.frameCount}].");
            }

            foreach (var h in handles) h.End();
            foreach (var h in handles) yield return h.AwaitCompletion();

            if (this == null)
                Debug.Log($"[SceneTransition] '{label}' in-phase complete but host destroyed.");
        }

    #if UNITY_EDITOR
        private void OnValidate()
        {
            if (effects == null) return;
            var seen = new HashSet<TransitionChannel>();
            foreach (var e in effects)
            {
                if (e == null) continue;
                if (e.Channel == TransitionChannel.Custom) continue;
                if (!seen.Add(e.Channel))
                {
                    Debug.LogWarning(
                        $"[SceneTransition] Two effects share channel '{e.Channel}' on '{name}'. " +
                        "They will collide — give one a different channel or remove the duplicate.",
                        this);
                }
            }
        }
    #endif
    }
}
