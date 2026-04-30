using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marker added at runtime to any GameObject that has at least one leash attached to it.
/// Lets <see cref="LeashArtifact"/> answer "is this thing already leashed?" with a single
/// GetComponent call, and ensures all leashes referencing this object are cleaned up if
/// the object is destroyed.
///
/// Multiple leashes per object are allowed; the same pair of objects can have multiple
/// leashes between them.
/// </summary>
public class LeashAttachable : MonoBehaviour
{
    public List<Leash> leashes = new List<Leash>();

    public bool HasLeashes => leashes.Count > 0;

    public static LeashAttachable GetOrAdd(GameObject go)
    {
        var existing = go.GetComponent<LeashAttachable>();
        return existing != null ? existing : go.AddComponent<LeashAttachable>();
    }

    public void AddLeash(Leash l)
    {
        if (l != null && !leashes.Contains(l)) leashes.Add(l);
    }

    public void RemoveLeash(Leash l)
    {
        leashes.Remove(l);
        // Intentionally do NOT auto-destroy when empty. Destroy() is deferred to end of
        // frame but the C# reference becomes Unity-null immediately; if a new leash
        // were attached to the same object on the same frame, GetOrAdd would AddComponent
        // a second marker. Keeping the empty marker around costs nothing and avoids that
        // race entirely.
    }

    private void OnDestroy()
    {
        // We deliberately do NOT auto-dispose leashes here. If the object holding the
        // leashes is destroyed, each leash will see a null endpoint in FixedUpdate and
        // stop applying physics — but the leash GameObjects survive, so the user can
        // see them in the Hierarchy and we can diagnose what destroyed the endpoint.
        Debug.Log($"[LeashAttachable] OnDestroy on '{name}' with {leashes.Count} leash(es). NOT auto-disposing.");
        leashes.Clear();
    }
}
