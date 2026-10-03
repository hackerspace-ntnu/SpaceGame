using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Where on a worn garment a carried item hangs: the belt's three, then the backpack's two. The
    /// order is the order a carrier tries them in. Which of them exist is up to the garments the
    /// wearer has on — see <see cref="GarmentMounts"/>.
    /// </summary>
    public enum BeltSlot
    {
        HipRight = 0,
        HipLeft = 1,
        Back = 2,
        PackLeft = 3,
        PackRight = 4
    }

    /// <summary>
    /// How an item hangs from a belt, when it is carried rather than held.
    ///
    /// <para>
    /// The hand's half of an item's pose is <see cref="ItemGrip"/>; this is the belt's half. An item
    /// with no <see cref="BeltMount"/> is never drawn on a belt, which is how a shovel or a cart
    /// stays out of one without anything having to say so.
    /// </para>
    /// <para>
    /// The pose is a child transform rather than a number pair so it scales and rotates with the
    /// model: the carrier seats the item so that <see cref="Hang"/> lands on the belt anchor with
    /// the same orientation. Author <see cref="Hang"/> with its origin at the loop, ring, thong or
    /// sheath mouth, +Y pointing up toward the belt, and +Z pointing out from the wearer's body.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BeltMount : MonoBehaviour
    {
        [Tooltip("Child transform that lands on the belt anchor. Origin: where the belt holds the " +
                 "item. +Y: up, toward the belt. +Z: out from the wearer's body.")]
        [SerializeField] private Transform hang;

        [Tooltip("Where this item prefers to hang. A carrier falls back to the other slots, in " +
                 "enum order, when the preferred one is taken.")]
        [SerializeField] private BeltSlot preferred = BeltSlot.HipRight;

        /// <summary>The transform that lands on the belt anchor. Never null — falls back to the root.</summary>
        public Transform Hang => hang != null ? hang : transform;

        public BeltSlot Preferred => preferred;

        private void OnValidate()
        {
            if (hang != null && !hang.IsChildOf(transform))
            {
                Debug.LogWarning($"BeltMount on '{name}': hang '{hang.name}' is not part of this prefab and " +
                                 "will not survive instantiation. Clearing it.", this);
                hang = null;
            }
        }
    }
}
