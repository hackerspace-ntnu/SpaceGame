using System.Collections;
using UnityEngine;

/// <summary>
/// Destination that teleports the initiator to an <see cref="InteriorAnchor"/> with the
/// given id, found in any currently-loaded scene. No additive scene load — same-world
/// movement only. Useful for "portal between two rooms in the same level" or "teleport
/// to ship bridge."
/// </summary>
[CreateAssetMenu(fileName = "Destination_Anchor_", menuName = "Scene Management/Destinations/Same-Scene Anchor")]
public class SameSceneAnchorDestination : SceneDestination
{
    [Tooltip("ID of the InteriorAnchor to teleport the initiator to.")]
    [SerializeField] private string anchorId;

    public string AnchorId => anchorId;

    public override bool IsValid() => !string.IsNullOrEmpty(anchorId);

    public override IEnumerator Apply(GameObject initiator)
    {
        if (!IsValid() || initiator == null) yield break;

        var anchor = InteriorAnchor.FindAnywhere(anchorId);
        if (anchor == null)
        {
            Debug.LogWarning($"[SameSceneAnchorDestination] No InteriorAnchor '{anchorId}' " +
                             "found in any loaded scene — nothing to teleport to.");
            yield break;
        }
        anchor.TeleportPlayer(initiator);
    }
}
