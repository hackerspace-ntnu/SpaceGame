using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        if (busy) return false;
        if (initiator == null) return false;
        if (destination == null || !destination.IsValid()) return false;
        if (IsLockedOut(initiator)) return false;
        return true;
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
        if (!CanTrigger(initiator)) return null;
        busy = true;
        lastInitiator = initiator;
        // Run on TransitionRunner (DontDestroyOnLoad). The host GameObject may be
        // inside a scene that the destination unloads — if the coroutine ran on us,
        // it would die mid-transition and effects would never receive End().
        return TransitionRunner.Instance.Run(Run(initiator));
    }

    private IEnumerator Run(GameObject initiator)
    {
        var handles = new List<EffectHandle>();

        if (effects != null)
        {
            foreach (var e in effects)
            {
                if (e == null) continue;
                var handle = e.Begin(this);
                if (handle != null) handles.Add(handle);
            }
        }

        // Wait for any effect that wants to block the load (e.g. a walk-through
        // cutscene that must play before the teleport). Out-phases run in parallel —
        // we yield each one in turn, so total wait is the slowest.
        foreach (var h in handles) yield return h.AwaitOutPhase();

        yield return destination.Apply(initiator);

        // Arm the cross-transition lockout once the initiator has landed. Prevents an
        // exit volume from firing on the spawn frame, or any other immediate bounce.
        // Keyed on InstanceID; entry is harmlessly orphaned if the initiator is destroyed
        // before expiry (next Trigger that reads it sees Time >= until and clears it).
        if (initiator != null && postTransitionLockoutSeconds > 0f)
            s_lockoutUntil[initiator.GetInstanceID()] = Time.unscaledTime + postTransitionLockoutSeconds;

        foreach (var h in handles) h.End();
        foreach (var h in handles) yield return h.AwaitCompletion();

        // Clear busy on this component if it still exists. If our scene was unloaded
        // mid-transition the SceneTransition is gone — no busy flag to clear, no leak.
        if (this != null)
        {
            busy = false;
            lastInitiator = null;
        }
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
