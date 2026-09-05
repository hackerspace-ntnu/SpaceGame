// The far end. A cell that arrives here and stops is a cell that has made it.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>The receptacle the cell has to be seated in.</summary>
    public class CrucibleSocket : NetworkBehaviour
    {
        [SerializeField] private CrucibleRoom room;

        [Tooltip("Seconds the cell must sit still in here before it counts as seated.")]
        [SerializeField, Min(0f)] private float settleSeconds = 0.5f;

        private CrucibleCarrier resting;
        private float restingSince;

        private void OnTriggerEnter(Collider other)
        {
            if (!IsServer) return;

            var carrier = other.GetComponentInParent<CrucibleCarrier>();
            if (carrier == null) return;

            resting = carrier;
            restingSince = Time.time;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsServer) return;
            if (other.GetComponentInParent<CrucibleCarrier>() != resting) return;

            resting = null;
        }

        private void Update()
        {
            if (!IsServer || resting == null || room == null) return;

            // Settled as well as present. A cell swinging through the socket on a rope has not been
            // seated in it, and without this the room is solved by flinging the cell past the hole —
            // which is both easier than the puzzle and completely unsatisfying to have done.
            if (!resting.Settled)
            {
                restingSince = Time.time;
                return;
            }

            if (Time.time - restingSince < settleSeconds) return;

            room.Solve();
            resting = null;
        }
    }
}
