// The thing you are carrying: a power cell, and deliberately a cheap one.
//
// It is destroyed by the lava constantly, so it has to be something a fresh copy of can roll into
// the cradle without anyone raising an eyebrow. That is the whole reason the reward sits behind a
// socket rather than BEING this object — an ancient relic that respawns after falling in lava is a
// worse story than a spare fuse, and it is a story the room would tell several times a minute.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// One power cell.
    ///
    /// <para>
    /// Server-owned, and that is load-bearing rather than incidental. A leash end is resolved by
    /// whichever machine owns it (<c>LeashEnd.ResolvedHere</c>), so putting this body on the server
    /// puts BOTH ropes' cell-ends on one machine — which is the only way two people hauling one
    /// object in opposite directions produces one agreed position. No new ownership concept; the
    /// existing rule already says this.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrucibleCarrier : NetworkBehaviour
    {
        [Tooltip("Speed below which the cell counts as settled, for seating it in the socket.")]
        [SerializeField, Min(0f)] private float settledSpeed = 0.35f;

        private Rigidbody body;

        private void Awake() => body = GetComponent<Rigidbody>();

        /// <summary>Whether the cell is close enough to at rest to be seated in something.</summary>
        public bool Settled => body != null && body.linearVelocity.magnitude < settledSpeed;

        /// <summary>
        /// Put this cell back in its cradle, at rest. Server only.
        ///
        /// <para>
        /// <c>transform.position</c> does not move a body in this project — the physics step undoes
        /// it within the frame. The rigidbody write is the one that survives.
        /// </para>
        /// </summary>
        public void Recradle(Vector3 at)
        {
            if (!IsServer || body == null) return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = at;
            body.MovePosition(at);
        }
    }
}
