// A walking city's crew member who rides a STANDING post rather than a seat: the cyborg elder, who
// stands on four legs and cannot sit. CrewShift keeps its house's last seats for these (and only
// these); this half lives on the rider and deals with its legs.
//
// Seating takes an NPC's motors (NpcSeating), but a legged machine's body is owned by its
// LeggedLocomotion, which writes the transform from its own path every LateUpdate (Locomotion.md,
// invariant I4). Left running on a moving deck it would drag the elder back to where it boarded.
// So while the rider's parent chain holds a VesselSeats the legs are switched off, standing in the
// pose they had, and when it is set down they are switched back on and resume from wherever the
// post put it (LeggedLocomotion.ResumeFromCarry).
//
// The test is the parenting, not the authority's seat record, because parenting is what replicates:
// every machine parks and resumes its own copy's legs, with no message.
//
// Order 90, before LeggedLocomotion's LateUpdate (100): seating and unseating happen in Update
// (CrewShift), so by now the transform is final, and the legs never get one frame to write a stale
// path over it.
using SpaceGame.Locomotion;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    [DefaultExecutionOrder(90)]
    [DisallowMultipleComponent]
    public class StandingRider : MonoBehaviour
    {
        private LeggedLocomotion legs;
        private Transform checkedParent;
        private bool parked;

        /// <summary>Whether <paramref name="member"/> (a spawned NPC or a prefab) rides a standing post.</summary>
        public static bool Is(GameObject member) => member != null && member.TryGetComponent(out StandingRider _);

        /// <summary>True while a carrier holds this rider and its legs are parked.</summary>
        public bool Parked => parked;

        private void LateUpdate() => SyncWithCarrier();

        /// <summary>Park the legs under a carrier, give them back once off it. Public so a test can tick it.</summary>
        public void SyncWithCarrier()
        {
            Transform parent = transform.parent;
            if (parent == checkedParent) return;
            checkedParent = parent;

            bool carried = parent != null && parent.GetComponentInParent<VesselSeats>() != null;
            if (carried == parked) return;
            parked = carried;

            if (legs == null && !TryGetComponent(out legs)) return;
            legs.enabled = !carried;
            if (!carried) legs.ResumeFromCarry();
        }
    }
}
