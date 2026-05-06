using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop-in scene transition orchestrator.
///
/// Place this on any GameObject that should send an initiator (player or AI agent) to
/// another scene. It does no triggering of its own — pair it with a trigger component:
///
///   • InteractableTransitionTrigger — fires when the player interacts with this object.
///   • VolumeTransitionTrigger        — fires when a player or agent enters a trigger volume.
///   • From script                    — call <see cref="Trigger"/> directly.
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
public class SceneTransition : MonoBehaviour
{
    [TextArea(6, 12)]
    [SerializeField] private string description =
        "Drop-in scene transition.\n" +
        "• Destination: which scene + spawn anchor (ScriptableObject).\n" +
        "• Effects: visual/audio effects that play during the load. Multiple allowed,\n" +
        "  but each must use a different TransitionChannel (Screen/Audio/Camera/Time).\n" +
        "• Add an InteractableTransitionTrigger or VolumeTransitionTrigger on the same\n" +
        "  GameObject, or call Trigger(initiator) from script, to fire the transition.\n" +
        "• Effects play during load. When the load finishes, the 'in' phase of each\n" +
        "  effect runs and the transition completes.\n" +
        "• Spacebar skips effects (skip is ignored until the load completes).";

    [Header("Configuration")]
    [SerializeField] private SceneDestination destination;
    [SerializeField] private SceneTransitionEffect[] effects;

    private bool busy;

    public bool IsBusy => busy;
    public SceneDestination Destination => destination;

    public bool CanTrigger(GameObject initiator)
    {
        if (busy) return false;
        if (initiator == null) return false;
        if (destination == null || !destination.IsValid()) return false;
        return true;
    }

    /// <summary>Fire the transition for the given initiator. Returns null if not eligible.</summary>
    public Coroutine Trigger(GameObject initiator)
    {
        if (!CanTrigger(initiator)) return null;
        busy = true;
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

        foreach (var h in handles) h.End();
        foreach (var h in handles) yield return h.AwaitCompletion();

        // Clear busy on this component if it still exists. If our scene was unloaded
        // mid-transition the SceneTransition is gone — no busy flag to clear, no leak.
        if (this != null) busy = false;
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
