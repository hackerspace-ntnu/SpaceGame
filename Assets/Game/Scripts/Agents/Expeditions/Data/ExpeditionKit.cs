// What one member of a band carries out of the gate. Kits are not stock: there is no economy, a kit is
// simply the gear a role takes on a goal. Members draw their kit at the muster, so the hand-off to a
// stand-in shows no change of gear.
using System;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Agents.Expeditions
{
    [CreateAssetMenu(menuName = "SpaceGame/Expeditions/Kit", fileName = "Kit")]
    public sealed class ExpeditionKit : ScriptableObject
    {
        [Tooltip("Carried in hand on the road. Required: every member of a band is armed (the validator fails a kit without one).")]
        public InventoryItem weapon;

        [Tooltip("The member's working item (spyglass, hammer), drawn at a work stage with the weapon put away. Empty = none.")]
        public InventoryItem tool;

        [Tooltip("Worn on the belt or pack, in order. Items without a BeltMount are not drawn.")]
        public InventoryItem[] beltItems = Array.Empty<InventoryItem>();
    }
}
