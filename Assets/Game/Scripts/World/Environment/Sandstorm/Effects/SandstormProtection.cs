// A piece of gear that keeps the sand out.
//
// This is the whole authoring story for storm equipment: put it on the item's prefab — the one
// InventoryItem.itemPrefab spawns when the item is equipped — set one number, done. No item type
// to subclass, no registry to add to, and no code at all for a new piece of kit.
using UnityEngine;

namespace SpaceGame.World.Weather
{
    public class SandstormProtection : MonoBehaviour, ISandProtection
    {
        [Tooltip("How much of the storm this keeps out. 1 makes the wearer immune; 0.9 leaves a " +
                 "trickle of damage so a long crossing is still a gamble.")]
        [SerializeField, Range(0f, 1f)] private float protection = 0.9f;

        [Tooltip("Turn off to disable the protection without removing the component — for gear " +
                 "that runs out of filters, or that only works while powered.")]
        [SerializeField] private bool active = true;

        public float SandProtection => active ? protection : 0f;

        /// <summary>Lets a gadget switch its own protection off when it runs dry.</summary>
        public void SetActive(bool value) => active = value;
    }
}
