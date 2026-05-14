using System.Collections;
using UnityEngine;

/// <summary>
/// What part of the experience an effect controls. Two effects on the same channel
/// will fight each other — SceneTransition warns at edit-time when this happens.
/// Pick Custom to opt out of the collision check.
/// </summary>
public enum TransitionChannel
{
    Screen,
    Audio,
    Camera,
    Time,
    Custom,
}

/// <summary>
/// Base class for transition effects. An effect runs in two phases:
///   • Begin() — start the "out" phase (fade to black, muffle audio, ...).
///   • The orchestrator then runs the destination load.
///   • handle.End() — start the "in" phase (fade back, unmuffle, ...).
///   • handle.AwaitCompletion() — yields until the "in" phase is done.
///
/// Add a new effect by subclassing this and returning an EffectHandle from Begin().
/// The effect SHOULD run on a host that survives scene unloads (e.g. LetterboxOverlay,
/// which is DontDestroyOnLoad), because the GameObject hosting the SceneTransition
/// may itself be unloaded mid-transition.
/// </summary>
public abstract class SceneTransitionEffect : ScriptableObject
{
    public abstract TransitionChannel Channel { get; }

    /// <summary>
    /// Start the "out" phase. Returns a handle the orchestrator drives.
    /// <paramref name="host"/> is the SceneTransition firing this effect — effects that
    /// need per-instance scene refs (a sibling Cutscene MonoBehaviour, an animator on
    /// the door, …) read them off <c>host.gameObject</c>. Effects that don't need it
    /// can ignore the parameter.
    /// </summary>
    public abstract EffectHandle Begin(SceneTransition host);
}

/// <summary>
/// Per-run handle for a single effect instance. The orchestrator calls End() when the
/// destination load finishes, then awaits AwaitCompletion() to know the in-phase is done.
/// </summary>
public abstract class EffectHandle
{
    /// <summary>
    /// Yield until the "out" phase is finished. The orchestrator awaits this on every
    /// handle in parallel before running the destination, so an effect that wants to
    /// block the load (a walk-through cutscene that should play before the teleport)
    /// just yields here until it's done. Default is "out phase finishes immediately"
    /// — a fade can ignore this and fade-out alongside the load.
    /// </summary>
    public virtual IEnumerator AwaitOutPhase() { yield break; }

    /// <summary>Signal the load is done — start the "in" phase.</summary>
    public abstract void End();

    /// <summary>Yield until the "in" phase has fully finished.</summary>
    public abstract IEnumerator AwaitCompletion();
}
