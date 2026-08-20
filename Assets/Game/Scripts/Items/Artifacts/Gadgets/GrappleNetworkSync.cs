// Replicates the grappling hook across the network.
//
// Authority: client-predicted, server-validated. The owner runs the pendulum locally so the swing
// stays responsive — that responsiveness IS the item — and the server only validates the anchor and
// relays it. A fully server-authoritative pull would put a round trip inside every swing.
//
// What actually replicates is the ANCHOR (hook point + whether the rope is out), not the rider's
// position: the player's own NetworkTransform already syncs where they end up. So observers see the
// rope and see the pilot swinging on it, without two systems fighting over the same body.
//
// Lives on the player (a NetworkObject) rather than the held gun, which has no RPC channel of its own.
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    [RequireComponent(typeof(NetworkObject))]
    public class GrappleNetworkSync : NetworkBehaviour
    {
        [Tooltip("How far from the player a hook anchor may be before the server rejects it. Should " +
                 "be the artifact's maxRange plus a little slack for latency.")]
        [SerializeField] private float maxAnchorRange = 75f;

        [Tooltip("Rope drawn for OTHER players' grapples. Left null, remote grapples sync but draw " +
                 "no rope — the swing is still visible via the player's own movement.")]
        [SerializeField] private LineRenderer remoteRope;

        [SerializeField] private int remoteRopeSegments = 12;
        [SerializeField] private float remoteRopeGravity = 4f;

        // The anchor, replicated to everyone. Owner writes it (it predicts locally); the server
        // validates on the way through.
        private readonly NetworkVariable<GrappleAnchor> anchor = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public struct GrappleAnchor : INetworkSerializable
        {
            public bool Attached;
            public Vector3 Point;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Attached);
                serializer.SerializeValue(ref Point);
            }
        }

        /// <summary>Owner-side: the local hook just latched on. Tell everyone where.</summary>
        public void ReportAttached(Vector3 point)
        {
            if (!IsOwner) return;

            // Range-check locally too, so an honest client never desyncs against the server's rule.
            if ((point - transform.position).sqrMagnitude > maxAnchorRange * maxAnchorRange)
                return;

            anchor.Value = new GrappleAnchor { Attached = true, Point = point };
            ValidateAnchorServerRpc(point);
        }

        /// <summary>Owner-side: the rope let go.</summary>
        public void ReportReleased()
        {
            if (!IsOwner) return;
            anchor.Value = new GrappleAnchor { Attached = false, Point = Vector3.zero };
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void ValidateAnchorServerRpc(Vector3 point, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            // The one thing worth enforcing at this authority level: a hook that reaches across the
            // map is a teleport. Clearing the anchor cancels the swing on every peer.
            if ((point - transform.position).sqrMagnitude > maxAnchorRange * maxAnchorRange)
            {
                RejectAnchorClientRpc();
            }
        }

        [Rpc(SendTo.Owner)]
        private void RejectAnchorClientRpc()
        {
            anchor.Value = new GrappleAnchor { Attached = false, Point = Vector3.zero };

            var artifact = GetComponentInChildren<GrapplingHookArtifact>(true);
            if (artifact != null)
                artifact.CancelFromNetwork();
        }

        private void LateUpdate()
        {
            // Only draw the rope for OTHER players — the owner's own rope is drawn by the artifact,
            // which knows the real per-frame rope length and shoot progress.
            if (IsOwner || remoteRope == null)
                return;

            GrappleAnchor current = anchor.Value;
            if (!current.Attached)
            {
                if (remoteRope.enabled) remoteRope.enabled = false;
                return;
            }

            DrawRemoteRope(current.Point);
        }

        private void DrawRemoteRope(Vector3 hookPoint)
        {
            remoteRope.enabled = true;

            int segments = Mathf.Max(2, remoteRopeSegments);
            remoteRope.positionCount = segments;

            Vector3 start = transform.position;
            float span = (hookPoint - start).magnitude;

            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)(segments - 1);
                Vector3 pos = Vector3.Lerp(start, hookPoint, t);
                // Same sag shape the owner's rope uses, so both views read as the same rope.
                pos.y -= Mathf.Sin(t * Mathf.PI) * remoteRopeGravity * (span / Mathf.Max(maxAnchorRange, 0.01f));
                remoteRope.SetPosition(i, pos);
            }
        }
    }
}
