// Keeps a parked dune foiler at its berth until somebody is aboard.
//
// The craft ships with its sails SET, so it is ready to sail the moment you step onto the deck and
// you never have to hunt for the hoist station to make it go. The cost of that is a craft that sails
// itself away: with the rig drawing, it catches the wind on the frame the scene loads and leaves —
// measured once at 293 m from the player spawn within seconds, foiling, gangway stowed, unreachable.
//
// So the rig is left drawing and the HULL is what gets held. While nobody is standing on the deck the
// locomotion is told to hold station: sails trim and flog against the real wind, the craft looks
// alive at its mooring, and it makes no way. Step aboard and it sails.
//
// Lives outside the DuneFoil assembly because WalkerPlatformCarrier — the thing that knows who is on
// the deck — is in the default assembly, the same split DuneFoilRiggingStation uses.
using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Vehicles
{
    [DefaultExecutionOrder(90)]     // before DuneFoilLocomotion (100) reads the flag
    public class DuneFoilMooring : MonoBehaviour
    {
        [Tooltip("Off lets the craft sail with nobody aboard. Expect it to leave and not come back.")]
        [SerializeField] private bool holdUntilBoarded = true;

        [Tooltip("The craft. Found on this object when empty.")]
        [SerializeField] private DuneFoilLocomotion locomotion;

        [Tooltip("Who is on the deck. Found on this object when empty.")]
        [SerializeField] private WalkerPlatformCarrier carrier;

        [Tooltip("How long the craft keeps sailing after the last person seems to have left, " +
                 "seconds. The rider set is rebuilt every frame from an overlap query, and a " +
                 "player who jumps, walks over a hatch coaming or crosses the lip of the carry " +
                 "volume drops out of it for a frame or two. Without this grace period that " +
                 "reads as the craft stopping dead and starting again — a hard stutter under " +
                 "way, and a visible one, because the whole deck the player is standing on jerks.")]
        [SerializeField, Min(0f)] private float castOffGrace = 1.5f;

        private float lastSeenAboard = float.NegativeInfinity;

        /// <summary>True while the craft is being held at its berth.</summary>
        public bool IsMoored => locomotion != null && locomotion.HoldStation;

        private void Awake()
        {
            if (locomotion == null) locomotion = GetComponent<DuneFoilLocomotion>();
            if (carrier == null) carrier = GetComponent<WalkerPlatformCarrier>();

            if (locomotion == null)
                Debug.LogWarning($"[{nameof(DuneFoilMooring)}] no DuneFoilLocomotion on {name}; " +
                                 "nothing to moor.", this);
            else if (carrier == null)
                Debug.LogWarning($"[{nameof(DuneFoilMooring)}] no WalkerPlatformCarrier on {name}, " +
                                 "so nobody can ever be seen aboard and the craft would never sail. " +
                                 "Leaving it free.", this);
        }

        private void Update()
        {
            if (locomotion == null) return;

            // No carrier means no way to tell who is aboard. Free rather than stuck: a craft that
            // will not move is a worse failure than one that sails off, because it looks broken.
            if (carrier == null)
            {
                locomotion.HoldStation = false;
                return;
            }

            if (carrier.RiderCount > 0) lastSeenAboard = Time.time;

            bool crewed = Time.time - lastSeenAboard <= castOffGrace;
            locomotion.HoldStation = holdUntilBoarded && !crewed;
        }

        /// <summary>Wire it up. Used by the prefab builder.</summary>
        public void Bind(DuneFoilLocomotion craft, WalkerPlatformCarrier deck)
        {
            locomotion = craft;
            carrier = deck;
        }
    }
}
