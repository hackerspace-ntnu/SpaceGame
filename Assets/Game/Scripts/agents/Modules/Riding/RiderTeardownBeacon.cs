// Tells MountModule that the rider it is holding is going away, in the one window where nothing
// else can.
//
// The problem this exists to solve: reparenting a GameObject that Unity is destroying is illegal
// ("Cannot set the parent of the GameObject 'X' while it is being destroyed"), and there is NO way
// to detect that state by inspecting the rider. Measured in the editor — inside OnDestroy, on the
// object being destroyed, `rider == null` is still false and ReferenceEquals is false too. Unity's
// null-overload only starts reporting dead once destruction has finished, which is after the point
// where the reparent has already been refused. See RiderDestroyTeardownTests.
//
// So the rider has to announce it, and the only hook that fires early enough is its own OnDestroy.
// This component is that announcement and nothing else. MountModule attaches it to the rider at
// mount time and reads it before every reparent, which is what keeps the guard out of the six
// separate call sites that can reach Dismount().
//
// Deliberately not [DisallowMultipleComponent]-free: one per rider is enough, and MountModule
// reuses an existing one rather than stacking them.
using UnityEngine;

namespace SpaceGame.Agents
{
    [DisallowMultipleComponent]
    public class RiderTeardownBeacon : MonoBehaviour
    {
        /// <summary>
        /// True from the moment Unity begins destroying this rider. Once set it never clears — a
        /// destroyed GameObject does not come back, and a pooled one gets a fresh component.
        /// </summary>
        public bool IsBeingDestroyed { get; private set; }

        private void OnDestroy() => IsBeingDestroyed = true;

        /// <summary>
        /// Is this transform safe to reparent right now? Answers the two ways a rider can be
        /// un-reparentable — already gone, or mid-destruction — behind one call, so callers never
        /// have to remember that the plain null check is insufficient.
        /// </summary>
        public static bool CanReparent(Transform rider)
        {
            if (rider == null)
                return false;

            // GetComponent is safe here: the managed component survives destruction long enough to
            // be found, which is the entire reason this approach works.
            RiderTeardownBeacon beacon = rider.GetComponent<RiderTeardownBeacon>();
            return beacon == null || !beacon.IsBeingDestroyed;
        }

        /// <summary>Attach (or reuse) the beacon on a rider about to be seated.</summary>
        public static void Arm(Transform rider)
        {
            if (rider == null)
                return;
            if (!rider.TryGetComponent(out RiderTeardownBeacon _))
                rider.gameObject.AddComponent<RiderTeardownBeacon>();
        }
    }
}
