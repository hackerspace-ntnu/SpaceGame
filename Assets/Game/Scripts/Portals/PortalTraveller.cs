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
// Two rules make "anything" hold up for the things that are not one object:
//
//   • a COMPOSITE travels as one. A rider is parented to their mount and a held
//     item to the hand holding it, so whatever is on top does the crossing and
//     everything beneath it comes along in the same move. See Carrier.
//   • anything keeping world-space state OUTSIDE its transform is told, through
//     SaveTeleport raising ITeleportAware. That is what carries a walking
//     machine's path position and its planted feet through the aperture, and
//     without it a legged creature is put back where it started within a frame.
//
// Subclass it only to change the MOVE ITSELF — PlayerPortalTraveller does, to
// keep the player upright and the camera pointing where they were looking.
// Reacting to having been moved is ITeleportAware's job, not a subclass's, and
// works for every other way of being teleported too.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Locomotion;

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
        private Ghost[] ghosts;
        private Transform[] sourceBones;
        private Transform[] cloneBones;
        private Renderer[] ownRenderers;
        private MaterialPropertyBlock sliceBlock;

        private Rigidbody body;

        /// <summary>
        /// Everything under this object that decides where it may walk by casting rays rather than
        /// by pushing against the world. See <see cref="IGroundProbeExclusions"/>.
        /// </summary>
        private IGroundProbeExclusions[] probeUsers;

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

        /// <summary>
        /// The picture currently standing out of the far aperture, or null when nothing is being
        /// cloned.
        ///
        /// Public for the same reason <see cref="Portal.AdvanceTraversal"/> is: what the clone is
        /// MADE OF is the thing that broke, and there is no other way to look at it from a test.
        /// </summary>
        public GameObject Clone => clone;

        /// <summary>Fired after this object has come out of <c>to</c>. Position is already final.</summary>
        public event System.Action<Portal, Portal> Traversed;

        /// <summary>
        /// Is this object standing in an aperture right now, and so making its own way through?
        ///
        /// Asked by anything that CARRIES other objects without parenting them —
        /// <c>WalkerPlatformCarrier</c> is the one — so that a passenger who is going through the
        /// hole under their own name is not also dragged through by the deck they are standing on.
        /// A parented passenger needs no such question: <see cref="Carrier"/> already means only the
        /// outermost traveller crosses.
        /// </summary>
        public bool InPortal => insidePortals.Count > 0;

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
            if (existing != null) return Carrier(existing);
            if (!createIfMissing) return null;

            // The body is the right owner even when the collider is a child of
            // it: that is the transform physics actually moves.
            Rigidbody body = other.attachedRigidbody;
            if (body != null) return Carrier(body.gameObject.AddComponent<PortalTraveller>());

            var controller = other.GetComponentInParent<CharacterController>();
            if (controller != null)
                return Carrier(controller.gameObject.AddComponent<PortalTraveller>());

            // A creature driven purely by its NavMeshAgent. Every agent prefab in
            // this project also carries a kinematic Rigidbody and so is caught
            // above, but the agent is what actually moves them and a rig that
            // ever ships without the body should still be able to walk through a
            // hole rather than into it.
            var agent = other.GetComponentInParent<NavMeshAgent>();
            if (agent != null) return Carrier(agent.gameObject.AddComponent<PortalTraveller>());

            return null;
        }

        /// <summary>
        /// The OUTERMOST traveller above this one — whatever is actually carrying it through.
        ///
        /// A composite entity is one traveller, not several. A rider is parented to their mount and
        /// a held item to the hand holding it, so moving the mount already moves everything aboard:
        /// letting the rider traverse under its own name as well applies the transfer to them
        /// TWICE, and a passenger arrives as far past the exit as the two apertures are apart.
        ///
        /// Resolved at the moment of the sweep rather than cached, because being carried is not a
        /// property of a prefab — it is mounting, dismounting, picking something up and dropping it
        /// again, several times a minute.
        ///
        /// The whole rig comes along for free once this points at the top: the clone is built from
        /// every renderer beneath it, and the wall pass-through from every collider beneath it.
        /// </summary>
        private static PortalTraveller Carrier(PortalTraveller traveller)
        {
            if (traveller == null) return null;

            PortalTraveller top = traveller;

            // From the PARENT each time, or GetComponentInParent keeps answering with the
            // component it was given and this never terminates.
            for (Transform above = traveller.transform.parent; above != null; )
            {
                PortalTraveller carrier = above.GetComponentInParent<PortalTraveller>();
                if (carrier == null) break;

                top = carrier;
                above = carrier.transform.parent;
            }

            return top;
        }

        protected virtual void Awake()
        {
            body = GetComponent<Rigidbody>();
            probeUsers = GetComponentsInChildren<IGroundProbeExclusions>(true);
            sliceBlock = new MaterialPropertyBlock();
            GetComponentsInChildren(true, ownColliders);
            RefreshRenderers();

            trackedLocalOffset = MeasureCentre();
            MeasureGirth();
        }

        /// <summary>
        /// The smallest opening this object could pass through, as a half-width and a half-height
        /// in metres.
        ///
        /// Half the two SMALLEST of its three dimensions, which is the cross-section it presents
        /// when turned the best possible way. Deliberately the most generous reading of "does it
        /// fit": a portal game should let a long thing through end-on rather than measure it
        /// broadside and refuse, and being generous here costs nothing, because the thing this
        /// gates is the absurd case — a six-legged habitat squeezing through a doorway — not the
        /// marginal one.
        ///
        /// Measured in the object's OWN frame, so it is a property of the object and not of which
        /// way it happens to be facing. That is also what makes it worth measuring once.
        /// </summary>
        public Vector2 Girth
        {
            get
            {
                // Lazily, as well as in Awake, for the same reason RefreshRenderers is: a traveller
                // a test added outside play mode never had an Awake, and a girth of zero fits
                // through anything — which would quietly pass a test written to prove the opposite.
                if (!girthMeasured) MeasureGirth();
                return girth;
            }
        }

        private Vector2 girth;
        private Vector3 localExtents;
        private bool girthMeasured;

        /// <summary>
        /// Re-read which renderers this object is currently made of.
        ///
        /// Not measured once and kept: a player picks things up, puts a backpack on and takes a
        /// helmet off, so a list captured at spawn is stale by the time they walk into a hole.
        /// Cheap enough to redo at the one moment it is used, which is when a clone is built.
        /// </summary>
        private void RefreshRenderers()
        {
            // Built here as well as in Awake, because a traveller that a test added outside play
            // mode never had an Awake - AddComponent raises none of Unity's messages there.
            if (sliceBlock == null) sliceBlock = new MaterialPropertyBlock();

            ownRenderers = (visualRoot != null ? visualRoot : transform)
                .GetComponentsInChildren<Renderer>(true);
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

        /// <summary>
        /// Measure <see cref="Girth"/>: this object's own box, in its own rotated frame.
        ///
        /// Collider.bounds is NOT usable for this even though it is right there. World bounds are
        /// axis-aligned, so a creature standing diagonally measures wider in two axes at once than
        /// it really is — a thing that fits when facing north stops fitting when facing north-east,
        /// which is exactly the kind of intermittent refusal nobody would ever diagnose. So each
        /// collider's own shape is taken in ITS space and its corners are brought into this one.
        ///
        /// Rotation only, never InverseTransformPoint: that would divide out this object's scale
        /// and hand back a size in local units, while an aperture is authored in metres.
        /// </summary>
        private void MeasureGirth()
        {
            if (ownColliders.Count == 0) GetComponentsInChildren(true, ownColliders);

            girthMeasured = true;
            girth = Vector2.zero;
            localExtents = Vector3.zero;

            Quaternion toLocal = Quaternion.Inverse(transform.rotation);
            Vector3 origin = transform.position;

            bool any = false;
            var total = new Bounds(Vector3.zero, Vector3.zero);

            foreach (Collider collider in ownColliders)
            {
                if (collider == null || collider.isTrigger) continue;
                if (!ShapeOf(collider, out Bounds shape)) continue;

                Vector3 centre = shape.center;
                Vector3 extents = shape.extents;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    Vector3 point = toLocal *
                        (collider.transform.TransformPoint(centre + offset) - origin);

                    if (!any) { total = new Bounds(point, Vector3.zero); any = true; }
                    else total.Encapsulate(point);
                }
            }

            if (!any) return;

            localExtents = total.extents;

            // The two smallest of the three. A thing goes through an opening the narrow way.
            Vector3 size = total.size;
            float a = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float c = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float b = size.x + size.y + size.z - a - c;

            girth = new Vector2(a * 0.5f, b * 0.5f);
        }

        /// <summary>
        /// One collider's box in its OWN local space, or false for a shape with no size to read.
        ///
        /// By type rather than through bounds, because bounds is the world AABB — see MeasureGirth.
        /// A collider this does not recognise is skipped rather than guessed at: an unmeasured
        /// collider makes the object read as smaller than it is, and the failure mode of that is a
        /// creature that should not fit being let through, which is the thing being prevented.
        /// </summary>
        private static bool ShapeOf(Collider collider, out Bounds shape)
        {
            switch (collider)
            {
                case BoxCollider box:
                    shape = new Bounds(box.center, box.size);
                    return true;

                case SphereCollider sphere:
                    shape = new Bounds(sphere.center, Vector3.one * (sphere.radius * 2f));
                    return true;

                case CapsuleCollider capsule:
                {
                    float across = capsule.radius * 2f;
                    var size = new Vector3(across, across, across);

                    // A capsule shorter than it is wide is a sphere; height is the OVERALL length
                    // including both caps, so it can never be less than the diameter.
                    size[capsule.direction] = Mathf.Max(capsule.height, across);

                    shape = new Bounds(capsule.center, size);
                    return true;
                }

                case CharacterController controller:
                {
                    float across = controller.radius * 2f;
                    shape = new Bounds(controller.center,
                                       new Vector3(across, Mathf.Max(controller.height, across), across));
                    return true;
                }

                case MeshCollider mesh when mesh.sharedMesh != null:
                    shape = mesh.sharedMesh.bounds;
                    return true;
            }

            shape = default;
            return false;
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

            // Collision is only HALF of "the wall is not there", and the missing half is why a
            // portal used to be a door only physics bodies could walk through.
            //
            // Physics.IgnoreCollision has no effect at all on a raycast. A legged machine never
            // pushes against the world — it casts rays to ask where it may put a foot and whether
            // the ground ahead is climbable — so every probe still found the wall, the climb gate
            // read the far side of the aperture as a cliff, and the machine stopped dead at the rim
            // of a hole it was entitled to walk through.
            if (probeUsers == null)
                probeUsers = GetComponentsInChildren<IGroundProbeExclusions>(true);

            foreach (IGroundProbeExclusions probes in probeUsers)
                probes?.ExcludeFromGroundProbes(wall, ignored);
        }

        /// <summary>
        /// Half this object's own thickness along <paramref name="worldNormal"/>.
        ///
        /// What "touching" means, measured rather than guessed. An aperture has to decide whether a
        /// body has reached its surface, and the distance from the object's CENTRE to the plane
        /// says nothing on its own: the same half metre is a dune rat pressed hard against the wall
        /// and an ostrich still a stride away.
        ///
        /// The standard projection of a box onto an axis, so it is exact for any orientation rather
        /// than only for the one the object was authored in.
        /// </summary>
        public float HalfDepthAlong(Vector3 worldNormal)
        {
            if (!girthMeasured) MeasureGirth();

            Vector3 axis = Quaternion.Inverse(transform.rotation) * worldNormal;

            return Mathf.Abs(axis.x) * localExtents.x
                 + Mathf.Abs(axis.y) * localExtents.y
                 + Mathf.Abs(axis.z) * localExtents.z;
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

                WarnIfStuck(from, position);
            }

            // The clone belongs to the aperture that was just left; the next
            // frame's tracking builds a fresh one against the new portal.
            DestroyClone();
            ClearSlice(ownRenderers);

            Traversed?.Invoke(from, to);
        }

        /// <summary>
        /// Say so when a traversal did every piece of its bookkeeping and did not actually move
        /// the object.
        ///
        /// This exists because the failure it catches is INVISIBLE. Everything else about a
        /// traversal succeeds independently of the placement — the traveller is released by the
        /// entry, adopted by the exit, its clone destroyed, its slice cleared, its Traversed event
        /// raised — so a placement that quietly did nothing leaves a creature walking at an
        /// aperture and straight on past it, with a clean console and nothing to search for. That
        /// is precisely what a discarded <c>NavMeshAgent.Warp</c> result did, and the only reason
        /// it took a person playing the game to find it.
        ///
        /// A warning is the whole remedy. Nothing is retried here: if the object is still standing
        /// at the entry, the honest thing is to name it rather than to invent a second placement
        /// policy underneath the two that already ran.
        /// </summary>
        private void WarnIfStuck(Portal from, Vector3 intended)
        {
            // Against where it was ASKED to go, not against where it started: an exit a step away
            // from the entry is a legitimate portal pair, and measuring the distance travelled
            // would call that one stuck.
            if ((transform.position - intended).sqrMagnitude <= StuckTolerance * StuckTolerance)
                return;

            Debug.LogWarning(
                $"[Portal] '{name}' crossed {from.name} but was not placed: it is at " +
                $"{transform.position} and the exit put it at {intended}. A NavMeshAgent refuses a " +
                "warp to a point it cannot map onto a polygon, so an exit with no navigation mesh " +
                "near it leaves the creature where it was.", this);
        }

        /// <summary>How far from the intended exit an object may land and still count as placed.</summary>
        private const float StuckTolerance = 1.5f;

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

            // The RETURN VALUE, not agent.isOnNavMesh. This gate used to ask the latter, and it is
            // true exactly when the fallback is needed: a Warp that was refused leaves the agent
            // standing on the perfectly good mesh it never left, so "is it on a mesh" answers yes
            // and the landing search below never ran. Every creature that walked into an aperture
            // whose exit was above the floor — which is most of them, a portal being a hole in a
            // wall — was silently not moved at all.
            if (agent.Warp(position)) return;

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

        /// <summary>One renderer of the original, and the picture of it standing out of the far aperture.</summary>
        private sealed class Ghost
        {
            public Renderer Source;
            public Transform SourceTransform;
            public Transform Transform;
            public Renderer Renderer;
            public SkinnedMeshRenderer SourceSkin;
            public SkinnedMeshRenderer Skin;
        }

        /// <summary>
        /// Build the picture: one renderer at a time, by hand.
        ///
        /// This used to be <c>Instantiate</c> of the whole object followed by <c>Destroy</c> on
        /// every script, collider and body that came with the copy, and that is wrong twice over.
        ///
        /// Instantiate copies EVERYTHING, and on the one thing anybody actually walks a portal
        /// with - the player - everything is a second live player: its scripts, its Rigidbody, its
        /// NetworkObject, its input listeners, its save identity, all of them running Awake before
        /// a single line of the strip pass gets to look at them. The save system logged the
        /// duplicate reassigning an entity id on the way past.
        ///
        /// And the strip pass cannot even finish. Unity refuses to remove a component that another
        /// component declares <c>[RequireComponent]</c> on, so on the player prefab it logged seven
        /// "Can't remove X because Y depends on it" errors and left the copy alive and networked -
        /// NetworkObject, NetworkTransform, PlayerMovement, HealthComponent, EffectManager,
        /// BackpackController and the Rigidbody all survived it.
        ///
        /// A picture is made of renderers. So this makes one out of renderers, and there is
        /// nothing left to strip.
        /// </summary>
        private void EnsureClone()
        {
            if (!showClone || clone != null) return;

            RefreshRenderers();

            Transform source = visualRoot != null ? visualRoot : transform;

            // The root stays at the origin, unrotated and unscaled, for the clone's whole life:
            // every part below it is posed in world space, and a parent at identity is what makes
            // a local pose and a world pose the same thing.
            clone = new GameObject(source.name + " (portal clone)");

            var bones = new Dictionary<Transform, Transform>();
            var built = new List<Ghost>();

            foreach (Renderer renderer in ownRenderers)
            {
                Ghost ghost = BuildGhost(renderer, bones);
                if (ghost != null) built.Add(ghost);
            }

            ghosts = built.ToArray();

            sourceBones = new Transform[bones.Count];
            cloneBones = new Transform[bones.Count];

            int next = 0;
            foreach (KeyValuePair<Transform, Transform> bone in bones)
            {
                sourceBones[next] = bone.Key;
                cloneBones[next] = bone.Value;
                next++;
            }
        }

        /// <summary>
        /// The picture of one renderer, or null for anything that cannot be drawn as a copy.
        ///
        /// Particle systems, trails and line renderers are that last group: their geometry is a
        /// simulation's output rather than a mesh, and copying one copies the simulation - the old
        /// clone came with a second emitter, running.
        /// </summary>
        private Ghost BuildGhost(Renderer source, Dictionary<Transform, Transform> bones)
        {
            if (source == null) return null;

            var skin = source as SkinnedMeshRenderer;
            Mesh mesh = null;

            if (skin != null)
            {
                if (skin.sharedMesh == null) return null;
            }
            else
            {
                if (!(source is MeshRenderer)) return null;
                if (!source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                    return null;

                mesh = filter.sharedMesh;
            }

            var go = new GameObject(source.name);
            go.layer = source.gameObject.layer;
            go.transform.SetParent(clone.transform, false);

            var ghost = new Ghost
            {
                Source = source,
                SourceTransform = source.transform,
                Transform = go.transform,
            };

            if (skin != null)
            {
                SkinnedMeshRenderer copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skin.sharedMesh;

                // Skinning reads each bone's world matrix and nothing else - the shape of the
                // hierarchy those bones hang in is never consulted - so the copy's skeleton is a
                // flat row of transforms under the clone's root, each posed from the bone it
                // stands for. Rebuilding the real hierarchy would cost a walk and buy nothing.
                copy.bones = MapBones(skin.bones, bones);
                copy.rootBone = MapBone(skin.rootBone, bones);

                // A skinned renderer's bounds are derived from its root bone's pose, which for
                // this copy is somewhere it was never told about; without this the picture culls
                // itself away at exactly the angles a portal is worth looking through from.
                copy.updateWhenOffscreen = true;

                ghost.SourceSkin = skin;
                ghost.Skin = copy;
                ghost.Renderer = copy;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                ghost.Renderer = go.AddComponent<MeshRenderer>();
            }

            ghost.Renderer.sharedMaterials = source.sharedMaterials;
            ghost.Renderer.shadowCastingMode = source.shadowCastingMode;
            ghost.Renderer.receiveShadows = source.receiveShadows;

            return ghost;
        }

        private Transform[] MapBones(Transform[] sources, Dictionary<Transform, Transform> bones)
        {
            if (sources == null) return null;

            var mapped = new Transform[sources.Length];
            for (int i = 0; i < sources.Length; i++) mapped[i] = MapBone(sources[i], bones);

            return mapped;
        }

        /// <summary>The clone's stand-in for one of the original's bones, made the first time it is asked for.</summary>
        private Transform MapBone(Transform bone, Dictionary<Transform, Transform> bones)
        {
            if (bone == null) return null;
            if (bones.TryGetValue(bone, out Transform existing)) return existing;

            var go = new GameObject(bone.name);
            go.transform.SetParent(clone.transform, false);

            bones[bone] = go.transform;
            return go.transform;
        }

        private void DestroyClone()
        {
            if (clone == null) return;

            // DestroyImmediate outside play mode: Destroy is refused there outright, and the clone
            // is now something an edit-mode test can build - which is the only place what it is
            // made of can be checked. The same rule Portal uses for its materials.
            if (Application.isPlaying) Destroy(clone);
            else DestroyImmediate(clone);

            clone = null;
            ghosts = null;
            sourceBones = null;
            cloneBones = null;
        }

        /// <summary>
        /// Pose the clone for this frame and cut both copies against their own aperture plane.
        ///
        /// Posed part by part rather than by moving the clone's root, because the parts of
        /// anything animated move relative to each other: posing only the root would show the far
        /// room this object's rest pose while the original walks.
        ///
        /// The slice only takes effect on renderers whose material exposes _SliceNormal -
        /// PortalSliceable does. Everything else is left alone on purpose: silently replacing an
        /// authored material to make it sliceable is a worse defect than the overhang it would fix.
        /// </summary>
        internal void UpdateClone(Portal portal)
        {
            if (clone == null || portal == null || portal.Linked == null) return;

            Matrix4x4 transfer = portal.Linked.TransferFrom(portal);

            // Bones before renderers: the skinned pictures are drawn from them.
            for (int i = 0; i < cloneBones.Length; i++)
                Pose(cloneBones[i], sourceBones[i], transfer);

            foreach (Ghost ghost in ghosts)
            {
                if (ghost.Source == null || ghost.Renderer == null) continue;

                Pose(ghost.Transform, ghost.SourceTransform, transfer);

                // A picture of something switched off is not a picture of it: helmets come off and
                // held items are put away, and the copy has to follow.
                ghost.Renderer.enabled =
                    ghost.Source.enabled && ghost.SourceTransform.gameObject.activeInHierarchy;

                if (ghost.Skin != null) CopyBlendShapes(ghost);
            }

            // Which way the object is going decides which half of each copy is kept. Approaching
            // from the front, the original keeps everything in front of the plane and the clone
            // keeps everything behind the far one; once past, the halves swap.
            float side = Mathf.Sign(portal.SideOf(TrackedPoint));
            if (side == 0f) side = 1f;

            SetSlice(ownRenderers, portal.transform.forward * side, portal.transform.position);
            SliceClone(portal.Linked.transform.forward * -side, portal.Linked.transform.position);
        }

        /// <summary>Put <paramref name="target"/> where the far aperture puts <paramref name="source"/>.</summary>
        private static void Pose(Transform target, Transform source, Matrix4x4 transfer)
        {
            if (target == null || source == null) return;

            target.SetPositionAndRotation(transfer.MultiplyPoint3x4(source.position),
                                          transfer.rotation * source.rotation);

            // lossyScale, not localScale: the clone's parts hang off a root at identity rather
            // than off a copy of the original's hierarchy, so each one has to carry the scale of
            // the whole chain it was lifted out of.
            target.localScale = source.lossyScale;
        }

        private static void CopyBlendShapes(Ghost ghost)
        {
            int count = ghost.Skin.sharedMesh.blendShapeCount;
            for (int i = 0; i < count; i++)
                ghost.Skin.SetBlendShapeWeight(i, ghost.SourceSkin.GetBlendShapeWeight(i));
        }

        /// <summary>
        /// Cut the clone against the far aperture's plane, keeping whatever the original is tinted
        /// with.
        ///
        /// Seeded from the ORIGINAL's property block rather than from an empty one, because a
        /// property block replaces every value a renderer had: building the slice on a blank block
        /// would strip the suit's colour off the half of the player standing in the other room.
        /// </summary>
        private void SliceClone(Vector3 normal, Vector3 centre)
        {
            if (ghosts == null) return;

            foreach (Ghost ghost in ghosts)
            {
                if (ghost.Source == null || ghost.Renderer == null) continue;

                ghost.Source.GetPropertyBlock(sliceBlock);
                sliceBlock.SetVector(SliceNormalId, normal);
                sliceBlock.SetVector(SliceCentreId, centre);
                ghost.Renderer.SetPropertyBlock(sliceBlock);
            }
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
