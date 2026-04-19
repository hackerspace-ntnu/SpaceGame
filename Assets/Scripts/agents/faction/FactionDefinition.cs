// ScriptableObject representing a single faction (e.g. Robots, BountyHunters, Player, Wildlife).
// Create via Assets > Create > Factions > Faction Definition.
using UnityEngine;

[CreateAssetMenu(menuName = "Factions/Faction Definition")]
public class FactionDefinition : ScriptableObject
{
    [Tooltip("Display name for this faction.")]
    public string factionName = "Unnamed Faction";

    [Tooltip("Colour used in debug gizmos and editor tools.")]
    public Color debugColor = Color.white;
}
