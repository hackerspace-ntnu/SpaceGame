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

        public int Order => order;
    }
}
