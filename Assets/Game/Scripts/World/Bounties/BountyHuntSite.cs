// Where a hunt's cast stands: drop one in the chunk scene, point it at the hunt, and put its BountyHuntSpots under it.
// Saving the scene writes its place into the hunt (BountyHuntRecorder), so the visor knows it while this chunk is not
// loaded; when the chunk loads on the server while that hunt is open, the board stands the cast here, and keeps them
// to their spots for as long as this chunk is loaded (BountyHuntRun).
//
// Scenery: no NetworkObject, nothing saved. The people it spawns are ordinary saved entities.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.World.Bounties
{
    [DisallowMultipleComponent]
    public sealed class BountyHuntSite : MonoBehaviour
    {
        private const float GizmoRadius = 1f;
        private const float GizmoFacingLength = 2f;

        private static readonly List<BountyHuntSite> Loaded = new();
        private static readonly List<BountyHuntSpot> SpotBuffer = new();

        [Tooltip("The hunt whose cast stands here.")]
        [SerializeField] private BountyHunt hunt;

        public BountyHunt Hunt => hunt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Loaded.Clear();

        /// <summary>The loaded site of <paramref name="openHunt"/>, or null while its chunk is not loaded.</summary>
        public static BountyHuntSite For(BountyHunt openHunt)
        {
            if (openHunt == null) return null;
            foreach (BountyHuntSite site in Loaded)
                if (site.hunt == openHunt) return site;
            return null;
        }

        /// <summary>The spots under this site tagged <paramref name="tag"/>, in hierarchy order (a patrol walks them in it).</summary>
        public void SpotsTagged(string tag, List<BountyHuntSpot> into)
        {
            into.Clear();
            GetComponentsInChildren(SpotBuffer);
            foreach (BountyHuntSpot spot in SpotBuffer)
                if (spot.SpotTag == tag) into.Add(spot);
        }

        private void OnEnable() => Loaded.Add(this);

        private void OnDisable() => Loaded.Remove(this);

        private void Update()
        {
            if (!Network.Decides) return;

            BountyBoard board = BountyBoard.Instance;
            if (board != null && board.WantsCastAt(this)) board.SpawnCast(this);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, GizmoRadius);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * GizmoFacingLength);
        }
    }
}
