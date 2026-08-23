// Anything that can go through a portal.
//
// "Anything" is the requirement, so this deliberately knows nothing about what
// it is attached to. It handles a Rigidbody if there is one, a CharacterController
// if there is one, a NavMeshAgent if there is one, and a plain transform if
// there is not — via SaveTeleport, which already had to learn every one of
// those lessons for the save system and is the reason a teleport here does not
// quietly snap back within a frame (see project memory on teleporting bodies).
//
// Two things make traversal look continuous rather than like a cut:
//
//   • the CLONE — while an object straddles an aperture, a copy of it stands
//     out of the far one, so the half that has crossed is visible over there.
//     Without it a crate vanishes at the plane and reappears whole, which reads
//     as a teleporter, not a hole.
//   • the WALL PASS — the collider the aperture was cut into stops colliding
//     with this object for as long as it is in the hole. Otherwise you walk
//     into the picture.
//
// Subclass it to hook the moment of traversal — PlayerPortalTraveller does, to
// keep the camera pointing where the player was looking.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;

namespace SpaceGame.Portals
{
    [DisallowMultipleComponent]
    public class PortalTraveller : MonoBehaviour
    {
        [Header("Tracking")]
        [Tooltip("The point tested against the portal plane. Leave empty to use this transform. Set it to the centre of mass for anything whose origin sits at its feet.")]
        [SerializeField] private Transform trackedPoint;

        [Header("Clone")]
        [Tooltip("Show a copy of this object standing out of the far portal while it is passing through. Turn it off for anything whose visuals are too expensive or too stateful to duplicate.")]
        [SerializeField] private bool showClone = true;

        [Tooltip("Root of the renderers to clone. Leave empty to clone this whole object.")]
        [SerializeField] private Transform visualRoot;

        private static readonly int SliceNormalId = Shader.PropertyToID("_SliceNormal");
        private static readonly int SliceCentreId = Shader.PropertyToID("_SliceCentre");

        private readonly HashSet<Portal> insidePortals = new HashSet<Portal>();
        private readonly List<Collider> ownColliders = new List<Collider>();

        /// <summary>
        /// Which walls each aperture has this object being let through, so they can be put back.
        ///
        /// A list per portal, not one collider: a wall in a real level is several colliders and an
        /// aperture is routinely cut across the seam between them. See Portal.HostSurfaces.
        /// </summary>
        private readonly Dictionary<Portal, List<Collider>> ignoredWalls =
            new Dictionary<Portal, List<Collider>>();

        private GameObject clone;
        private Renderer[] cloneRenderers;
        private Renderer[] ownRenderers;
        private MaterialPropertyBlock sliceBlock;

        private Rigidbody body;

        /// <summary>
        /// The point tested against a portal's plane.
        ///
        /// Falls back to the middle of this object's own colliders rather than
        /// to its origin. Origins sit at the feet on most rigs and at a corner
        /// on plenty of props, and a crossing measured from there fires only
        /// once the object's base has passed the plane — which for a wall portal
        /// means the whole body pushes into the wall first.
        /// </summary>
        public Vector3 TrackedPoint =>
            trackedPoint != null
                ? trackedPoint.position
                : transform.TransformPoint(trackedLocalOffset);

        private Vector3 trackedLocalOffset;

        /// <summary>Fired after this object has come out of <c>to</c>. Position is already final.</summary>
        public event System.Action<Portal, Portal> Traversed;

        // ── Lookup ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The traveller a collider belongs to, adding one if it can.
        ///
        /// Searched upward, because the collider that touches a portal is almost
        /// never the object's root — it is a capsule on a rig, a wheel on a
        /// vehicle, a hand on a character.
        ///
        /// The ADDING is what makes "anything can go through" true. Requiring
        /// the component to be authored means a portal is a hole that only the
        /// handful of prefabs somebody remembered works on, and everything else
        /// — a dropped item, a barrel, an NPC, a vehicle nobody thought about —
        /// bounces off an invisible wall with no clue as to why.
        ///
        /// It is added only to things that MOVE: something with a Rigidbody, or
        /// a CharacterController. A collider with neither is level geometry, and
        /// teleporting the floor is not a feature.
        /// </summary>
        public static PortalTraveller For(Collider other, bool createIfMissing = false)
        {
            if (other == null) return null;

            PortalTraveller existing = other.GetComponentInParent<PortalTraveller>();
            if (existing != null || !createIfMissing) return existing;

            // The body is the right owner even when the collider is a child of
            // it: that is the transform physics actually moves.
            Rigidbody body = other.attachedRigidbody;
            if (body != null) return body.gameObject.AddComponent<PortalTraveller>();

            var controller = other.GetComponentInParent<CharacterController>();
            if (controller != null) return controller.gameObject.AddComponent<PortalTraveller>();

            // A creature driven purely by its NavMeshAgent. Every agent prefab in
            // this project also carries a kinematic Rigidbody and so is caught
            // above, but the agent is what actually moves them and a rig that
            // ever ships without the body should still be able to walk through a
            // hole rather than into it.
            var agent = other.GetComponentInParent<NavMeshAgent>();
            if (agent != null) return agent.gameObject.AddComponent<PortalTraveller>();

            return null;
        }

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            sliceBlock = new MaterialPropertyBlock();
            GetComponentsInChildren(true, ownColliders);
            ownRenderers = (visualRoot != null ? visualRoot : transform)
                .GetComponentsInChildren<Renderer>(true);

            trackedLocalOffset = MeasureCentre();
        }

        /// <summary>
        /// The middle of this object's colliders, in its own space.
        ///
        /// Measured once. Reading Collider.bounds every frame would work, but
        /// world bounds are axis-aligned and so their centre wanders as the
        /// object turns — the crossing test would then fire at a slightly
        /// different depth depending on which way something was facing.
        /// </summary>
        private Vector3 MeasureCentre()
        {
            bool any = false;
            var total = new Bounds(Vector3.zero, Vector3.zero);

            foreach (Collider collider in ownColliders)
            {
                if (collider == null || collider.isTrigger) continue;

                Bounds b = collider.bounds;
                if (!any) { total = b; any = true; }
                else total.Encapsulate(b);
            }

            return any ? transform.InverseTransformPoint(total.center) : Vector3.zero;
        }

        protected virtual void OnDisable()
        {
            // Restore every wall this object was allowed through. A traveller
            // destroyed or pooled mid-traversal would otherwise leave the
            // ignore in place for the next user of that collider pair.
            foreach (KeyValuePair<Portal, List<Collider>> pair in ignoredWalls)
                foreach (Collider wall in pair.Value)
                    SetWallIgnored(wall, false);

            ignoredWalls.Clear();
            insidePortals.Clear();
            DestroyClone();
            ClearSlice(ownRenderers);
        }

        // ── Portal bookkeeping ─────────────────────────────────────────────────

        internal void EnterPortal(Portal portal)
        {
            if (portal == null || !insidePortals.Add(portal)) return;

            if (!ignoredWalls.ContainsKey(portal))
            {
                var walls = new List<Collider>(portal.HostSurfaces);
                ignoredWalls[portal] = walls;

                foreach (Collider wall in walls) SetWallIgnored(wall, true);
            }

            EnsureClone();
        }

        internal void ExitPortal(Portal portal)
        {
            if (portal == null || !insidePortals.Remove(portal)) return;

            if (ignoredWalls.TryGetValue(portal, out List<Collider> walls))
            {
                ignoredWalls.Remove(portal);

                // Only restore a wall no OTHER portal still wants passable — two apertures on one
                // long wall is a normal case, and one of them closing must not re-solidify the
                // piece somebody is standing in the middle of.
                foreach (Collider wall in walls)
                    if (!StillNeeded(wall)) SetWallIgnored(wall, false);
            }

            if (insidePortals.Count == 0)
            {
                DestroyClone();
                ClearSlice(ownRenderers);
            }
        }

        private bool StillNeeded(Collider wall)
        {
            foreach (KeyValuePair<Portal, List<Collider>> pair in ignoredWalls)
                if (pair.Value.Contains(wall)) return true;

            return false;
        }

        private void SetWallIgnored(Collider wall, bool ignored)
        {
            if (wall == null) return;

            foreach (Collider own in ownColliders)
            {
                if (own == null || own.isTrigger) continue;
                Physics.IgnoreCollision(own, wall, ignored);
            }
        }

        // ── Traversal ──────────────────────────────────────────────────────────

        /// <summary>
        /// Move through <paramref name="from"/> and come out of <paramref name="to"/>.
        ///
        /// Whoever owns the object performs the move and everyone else finds out
        /// through the transform sync that is already running — the same rule
        /// NetworkedTeleport enforces, and for the same reason: the player body
        /// is owner-authoritative, so a server-side move of a remote player is
        /// overwritten within a tick and silently does nothing.
        ///
        /// Non-owners still run everything cosmetic. A peer watching someone
        /// step through a portal sees the clone and the slice, and the body
        /// itself arrives over the wire a moment later.
        /// </summary>
        internal void Traverse(Portal from, Portal to, Matrix4x4 transfer)
        {
            Vector3 position = transfer.MultiplyPoint3x4(transform.position);
            Quaternion rotation = transfer.rotation * transform.rotation;

            if (Network.Owns(this))
            {
                Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
                Vector3 angular = body != null ? body.angularVelocity : Vector3.zero;

                ApplyTraversal(from, to, transfer, position, rotation);

                if (body != null && !body.isKinematic)
                {
                    // Speedy thing goes in, speedy thing comes out. Rotating the
                    // vector rather than reapplying a speed along the new normal
                    // is what preserves a diagonal entry — otherwise every
                    // traversal straightens the trajectory out.
                    body.linearVelocity = transfer.MultiplyVector(velocity);
                    body.angularVelocity = transfer.MultiplyVector(angular);
                }

                // An agent that walked through has to be told where it is, or it
                // keeps navigating from the polygon it left behind.
                PlaceAgent(position);
            }

            // The clone belongs to the aperture that was just left; the next
            // frame's tracking builds a fresh one against the new portal.
            DestroyClone();
            ClearSlice(ownRenderers);

            Traversed?.Invoke(from, to);
        }

        /// <summary>
        /// Put this object's NavMeshAgent, if it has one, back on the mesh at
        /// <paramref name="position"/>.
        ///
        /// The sampling fallback is the part that matters. An exit aperture is
        /// on a wall as often as not, so the far side of a portal is regularly a
        /// point with no navigation mesh under it — and Warp to a point off the
        /// mesh does not fail loudly, it leaves the agent in a state where it
        /// simply stops moving and never says why. Landing the creature on the
        /// nearest real polygon is worse than landing it exactly where the
        /// transfer said, and far better than a chase that ends with an enemy
        /// frozen in mid-air.
        /// </summary>
        private void PlaceAgent(Vector3 position)
        {
            var agent = GetComponent<NavMeshAgent>();
            if (agent == null || !agent.enabled) return;

            agent.Warp(position);
            if (agent.isOnNavMesh) return;

            if (NavMesh.SamplePosition(position, out NavMeshHit hit, AgentLandingRadius,
                                       NavMesh.AllAreas))
                agent.Warp(hit.position);
        }

        /// <summary>How far from an exit an off-mesh agent may be dropped to find ground it can walk.</summary>
        private const float AgentLandingRadius = 8f;

        /// <summary>
        /// The move itself. Overridable because some travellers need more than a
        /// pose — a player has a camera whose pitch is stored outside the
        /// transform, and setting the body without it spins the view.
        /// </summary>
        protected virtual void ApplyTraversal(Portal from, Portal to, Matrix4x4 transfer,
                                              Vector3 position, Quaternion rotation)
        {
            // zeroVelocity: false — the velocity is restored by the caller,
            // rotated into the destination's frame. Letting SaveTeleport clear
            // it would turn every traversal into a dead stop.
            SaveTeleport.Move(gameObject, position, rotation, zeroVelocity: false);
        }

        // ── The clone ──────────────────────────────────────────────────────────

        private void EnsureClone()
        {
            if (!showClone || clone != null) return;

            Transform source = visualRoot != null ? visualRoot : transform;

            clone = Instantiate(source.gameObject);
            clone.name = source.name + " (portal clone)";

            // Strip everything that would make the copy a participant rather
            // than a picture. A clone that kept its colliders would push the
            // original around through the portal; one that kept its scripts
            // would run a second copy of the object's whole behaviour, and on a
            // player that means a second camera and a second input listener.
            foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true))
                Destroy(script);
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>(true))
                Destroy(rb);
            foreach (Camera cam in clone.GetComponentsInChildren<Camera>(true))
                Destroy(cam);
            foreach (AudioSource audio in clone.GetComponentsInChildren<AudioSource>(true))
                Destroy(audio);

            cloneRenderers = clone.GetComponentsInChildren<Renderer>(true);
        }

        private void DestroyClone()
        {
            if (clone == null) return;

            Destroy(clone);
            clone = null;
            cloneRenderers = null;
        }

        /// <summary>
        /// Pose the clone for this frame and cut both copies against their own
        /// aperture plane.
        ///
        /// The slice only takes effect on renderers whose material exposes
        /// _SliceNormal — PortalSliceable does. Everything else is left alone
        /// on purpose: silently replacing an authored material to make it
        /// sliceable is a worse defect than the overhang it would fix.
        /// </summary>
        internal void UpdateClone(Portal portal)
        {
            if (clone == null || portal == null || portal.Linked == null) return;

            Matrix4x4 transfer = portal.Linked.TransferFrom(portal);
            Transform source = visualRoot != null ? visualRoot : transform;

            clone.transform.SetPositionAndRotation(
                transfer.MultiplyPoint3x4(source.position),
                transfer.rotation * source.rotation);
            clone.transform.localScale = source.lossyScale;

            // Which way the object is going decides which half of each copy is
            // kept. Approaching from the front, the original keeps everything in
            // front of the plane and the clone keeps everything behind the far
            // one; once past, the halves swap.
            float side = Mathf.Sign(portal.SideOf(TrackedPoint));
            if (side == 0f) side = 1f;

            SetSlice(ownRenderers, portal.transform.forward * side, portal.transform.position);
            SetSlice(cloneRenderers, portal.Linked.transform.forward * -side,
                     portal.Linked.transform.position);
        }

        private void SetSlice(Renderer[] renderers, Vector3 normal, Vector3 centre)
        {
            if (renderers == null) return;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                renderer.GetPropertyBlock(sliceBlock);
                sliceBlock.SetVector(SliceNormalId, normal);
                sliceBlock.SetVector(SliceCentreId, centre);
                renderer.SetPropertyBlock(sliceBlock);
            }
        }

        private void ClearSlice(Renderer[] renderers)
        {
            if (renderers == null) return;

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;

                renderer.GetPropertyBlock(sliceBlock);
                // A zero normal is the shader's "not slicing" state, so this
                // clears the cut without having to remove the property block.
                sliceBlock.SetVector(SliceNormalId, Vector4.zero);
                renderer.SetPropertyBlock(sliceBlock);
            }
        }
    }
}
