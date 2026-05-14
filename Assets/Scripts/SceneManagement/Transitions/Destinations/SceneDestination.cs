using System.Collections;
using UnityEngine;

/// <summary>
/// Base class for transition destinations. A destination knows how to take an
/// initiator (player or AI agent) and place it into a target scene at a target spot.
///
/// Add a new destination kind by subclassing this and implementing Apply(). The
/// SceneTransition orchestrator and all triggers stay untouched.
/// </summary>
public abstract class SceneDestination : ScriptableObject
{
    /// <summary>True if this destination can run right now (referenced assets present, manager available, ...).</summary>
    public abstract bool IsValid();

    /// <summary>
    /// Move the initiator to the destination. Coroutine completes when the initiator
    /// is fully placed and the world around it is ready.
    /// </summary>
    public abstract IEnumerator Apply(GameObject initiator);
}
