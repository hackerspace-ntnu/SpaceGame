// The half of a rope's physics that a player has to run on their own body.
//
// A rope is the server's to simulate — it is shared state, and both its ends have to agree. But a
// player's Rigidbody is the one thing the server is NOT authoritative over: their NetworkTransform
// is owner-authoritative, so anything the server pushes into that body is overwritten by the
// owner's next state update, silently, within a tick. That is the same failure that made
// server-side respawn teleports snap back.
//
// So the force is split. The server applies what it owns directly, and for a player endpoint it
// works out what the rope owes them and sends it here, where their own machine applies it. The
// message is broadcast because the messaging layer has no unicast; ownership filters it on arrival.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// Applies rope tugs to the player this sits on. Added on demand, never authored.
    ///
    /// On demand rather than on the prefab because a rope can be tied to any player at any time,
    /// and the alternative is a component every player carries for a case most of them never hit.
    /// Same reason and same shape as <see cref="LeashAttachable.GetOrAdd"/> and NetChannel's own.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class RopeTugReceiver : MonoBehaviour
    {
        private Rigidbody body;

        public static RopeTugReceiver Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out RopeTugReceiver existing)
                ? existing
                : player.AddComponent<RopeTugReceiver>();
        }

        private void Awake() => body = GetComponentInChildren<Rigidbody>();

        private void OnEnable() => this.NetOn(NetMsg.RopeTug, OnTugged);

        private void OnDisable() => this.NetOff(NetMsg.RopeTug, OnTugged);

        /// <summary>
        /// A velocity delta, not a force: the server already divided by this body's mass, because
        /// it is the machine that measured the rope. Applying it as a delta means a heavier player
        /// resists the rope correctly without either side having to agree about mass over the wire.
        /// </summary>
        private void OnTugged(in NetArg arg, ulong sender)
        {
            // Everyone receives this; only the machine that owns the body may move it. On any
            // other machine the body is a replica whose position is somebody else's to publish.
            if (!Network.Owns(this)) return;

            if (body == null || body.isKinematic) return;

            body.linearVelocity += arg.P;
        }
    }
}
