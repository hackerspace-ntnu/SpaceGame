using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Marks a transform on a ship as somewhere a player starts the match.
    ///
    /// <para>
    /// A component rather than a naming convention on child transforms. Names are a known silent
    /// failure in this project — several systems store them as strings and break quietly when
    /// somebody renames the thing — and here the failure would be invisible in the worst way: a
    /// renamed anchor stops being found, the spawner falls back to its stand-in ring, and players
    /// appear in roughly the right place with a clean console. A missing component is something you
    /// can see in the Inspector.
    /// </para>
    ///
    /// <para>
    /// Nothing carries a seat's identity beyond its order, because nothing needs it yet. Players
    /// are placed AT these poses, not parented to the ship and not mounted into it. When real seats
    /// arrive — with a pose to hold, a camera and a way out — this is where they hang off.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ShipSeat : MonoBehaviour
    {
        [Tooltip("Seats are filled lowest first. Ties keep their order in the hierarchy, so leaving " +
                 "these all at zero fills them top to bottom.")]
        [SerializeField] private int order;

        [Tooltip("Where the occupant of this seat stands up: on the deck, clear of the chair, " +
                 "facing the way they should walk off. Leave it empty and the ship's mount " +
                 "dismount point is used instead.")]
        [SerializeField] private Transform dismountPoint;

        public int Order => order;

        /// <summary>
        /// Where this seat's occupant stands up, or null when the ship has not authored one.
        ///
        /// <para>
        /// A marker rather than an offset, and per seat rather than per ship, because neither of the
        /// obvious shortcuts survives contact with a real hull. An offset from the seat is measured
        /// in the chair's local space and this ship's four chairs face two different ways; one point
        /// for the whole ship stands the whole crew up inside each other. The marker is the answer,
        /// the same way the seat marker itself is.
        /// </para>
        /// <para>
        /// Note it is a place to put the player's ORIGIN, which sits exactly 1 m above the soles —
        /// a marker on the deck buries the feet a metre through it.
        /// </para>
        /// </summary>
        public Transform DismountPoint => dismountPoint;
    }
}
