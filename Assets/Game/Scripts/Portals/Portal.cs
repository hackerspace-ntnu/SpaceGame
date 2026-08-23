// One aperture.
//
// A portal is two things that happen to share a transform, and keeping them
// straight is most of what this file is about:
//
//   • a WINDOW — a rectangle of screen showing a render taken from behind the
//     other aperture. That half lives in PortalRenderer, because it has to
//     happen at a specific point in the frame and for every portal at once.
//   • a DOOR — a plane that anything crossing comes out of the other side.
//     That half lives here.
//
// The two halves must agree exactly, or the illusion breaks in the most obvious
// possible way: the view says the far room is one step ahead, and the player
// walks into a wall. So both are derived from the same transfer matrix,
// <see cref="TransferFrom"/>, and nothing else is allowed to compute one.
//
// A portal is NOT a NetworkObject. Placement replicates as a message describing
// where the aperture went, and every machine builds its own copy from that, the
// same way the grappling hook replicates a rope rather than an anchor entity.
// That is what lets portals work offline, on a host and on a peer with no
// network prefab registration — see project memory on unregistered network
// prefabs failing on clients only.
//
// WHY THE DOOR SCANS INSTEAD OF LISTENING FOR TRIGGERS. It used to rely on
// OnTriggerEnter/OnTriggerExit and so it never ran at all: the traveller volume
// is a BoxCollider on a CHILD object, and Unity delivers trigger messages only
// to the collider's own GameObject and to its attached Rigidbody's. A portal has
// neither on the child, so the callbacks were being sent to a GameObject with no
// Portal on it and traversal silently did nothing in every scene, forever. The
// volume is now swept explicitly once a frame, which cannot be broken by moving
// a component between two objects and does not care whether the thing walking
// through has a Rigidbody, a CharacterController or only a NavMeshAgent.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Portals
{
    /// <summary>
    /// ExecuteAlways so an aperture placed in a scene by hand is live while the
    /// level is being built. Without it Portal.All is empty outside play mode,
    /// nothing subscribes the renderer, and the surface shows its dead-end
    /// swirl — which is indistinguishable from the portal being broken.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Portal : MonoBehaviour
    {
        [Header("Aperture")]
        [Tooltip("Width and height of the opening, in metres. The visible aperture is the ellipse inscribed in it. Big enough to run, ride or drive through without lining yourself up, and to be obviously a way through rather than a decal on a wall.")]
        [SerializeField] private Vector2 size = new Vector2(3.45f, 6.15f);

        [Tooltip("Seconds the aperture takes to iris open after placement.")]
        [SerializeField] private float openDuration = 0.22f;

        [Header("Parts")]
        [SerializeField] private Renderer surfaceRenderer;
        [SerializeField] private Renderer rimRenderer;
        [Tooltip("The volume straddling the plane that traversal sweeps. Kept as a collider so its shape is visible and editable, but switched OFF in play mode — the sweep is explicit, and a live trigger sitting in a doorway is found by every physics query in the game that does not think to ignore triggers.")]
        [SerializeField] private BoxCollider travellerVolume;

        [Header("Pairing")]
        [Tooltip("The aperture this one comes out of. The portal gun sets it at runtime; set it by hand for a pair placed in a scene.")]
        [SerializeField] private Portal linked;

        [Tooltip("Which barrel this aperture belongs to — 0 orange, 1 blue. Only used for naming and tinting.")]
        [SerializeField] private int index;

        [Tooltip("The collider this aperture is cut into. Traversal stops colliding with it while something is in the hole. The gun fills it in at runtime; set it by hand for a portal placed in a scene, or things will walk into the wall instead of through it.")]
        [SerializeField] private Collider hostSurface;

        [Header("Traversal")]
            [Tooltip("How far in front of and behind the plane the traversal volume reaches. Deep enough that a fast body is inside it for at least one physics step before it reaches the plane, and no deeper: the reach BEHIND the plane is also how far into a room somebody can stand and still be allowed through the wall the aperture is cut into.")]
        [SerializeField] private float volumeDepth = 2f;

        [Tooltip("Smallest speed along the portal normal that still counts as going through, so a body resting on the plane does not flicker between sides.")]
        [SerializeField] private float minimumCrossingSpeed = 0.02f;

        [Header("Lifetime")]
        [Tooltip("Seconds the aperture stays open before it irises shut and destroys itself. 0 is forever, which is what a pair placed in a scene by hand wants; the gun sets its own on every shot.")]
        [SerializeField] private float lifetime;

        [Tooltip("Seconds the aperture spends closing at the end of its life. Taken out of the lifetime, not added to it — a twenty second portal is gone at twenty seconds.")]
        [SerializeField] private float closeDuration = 0.35f;

        // ── Shader property ids ────────────────────────────────────────────────
        private static readonly int OpenId       = Shader.PropertyToID("_Open");
        private static readonly int HasViewId    = Shader.PropertyToID("_HasView");
        private static readonly int ViewId       = Shader.PropertyToID("_PortalTexture");
        private static readonly int BodyColourId = Shader.PropertyToID("_Colour");
        private static readonly int RimColourId  = Shader.PropertyToID("_Colour");

        /// <summary>Every portal alive in this scene. PortalRenderer walks it once a frame.</summary>
        public static readonly List<Portal> All = new List<Portal>();

        /// <summary>
        /// The aperture this one comes out of, or null while it is unpaired.
        ///
        /// A serialized field rather than a runtime-only property, because a
        /// pair placed in a scene has to survive being saved: an auto-property
        /// is not serialized, so a hand-authored pair came back from disk
        /// unlinked and showed its dead-end swirl with no clue why.
        /// </summary>
        public Portal Linked => linked;

        /// <summary>Which barrel fired it — 0 is the orange aperture, 1 the blue.</summary>
        public int Index => index;

        public Vector2 Size => size;

        /// <summary>
        /// The tint this aperture was opened with.
        ///
        /// Remembered rather than read back off the material, which is a per-portal INSTANCE created
        /// in <see cref="EnsureMaterials"/> and released in OnDisable — so asking the renderer what
        /// colour a portal is answers correctly only while it happens to be enabled, and a save that
        /// asked at the wrong moment would restore a black aperture.
        /// </summary>
        public Color Colour => colour;

        /// <summary>The plane of the opening, facing out into the room.</summary>
        public Plane Plane => new Plane(transform.forward, transform.position);

        /// <summary>The render of the far side, owned and sized by PortalRenderer.</summary>
        public RenderTexture ViewTexture { get; set; }

        public Renderer SurfaceRenderer => surfaceRenderer;

        /// <summary>What the aperture was cut into, so traversal can ignore it. May be null.</summary>
        public Collider HostSurface => hostSurface;

        /// <summary>
        /// Everything a traveller has to be let through to get from one side of this aperture to
        /// the other.
        ///
        /// A list rather than <see cref="HostSurface"/> alone, because a wall in a real level is
        /// almost never one collider — a modular kit, a mesh split by streaming chunk, a pillar
        /// flush against it, a floor slab meeting it at the sill. PortalPlacement already knows
        /// this and fits apertures across such seams deliberately. Letting a traveller through only
        /// the ONE collider the placement raycast happened to name means the player walks into the
        /// piece next to it and stops dead in the middle of the picture, which reads as the portal
        /// being fake rather than as a collision bug.
        /// </summary>
        public IReadOnlyList<Collider> HostSurfaces => hostSurfaces;

        private readonly List<Collider> hostSurfaces = new List<Collider>();

        /// <summary>How thick a wall an aperture is assumed to be cut through, for the pass-through.</summary>
        private const float WallThickness = 1f;

        /// <summary>Fired the moment this aperture shuts, before the GameObject goes. Never after.</summary>
        public event Action<Portal> Closed;

        /// <summary>Seconds this aperture was opened for, or 0 for one that never expires.</summary>
        public float Lifetime => lifetime;

        /// <summary>How long this aperture has been open. Runs outside play mode too — see <see cref="Clock"/>.</summary>
        public float Age => Clock - openedAt;

        /// <summary>
        /// Seconds left before this aperture shuts, or <see cref="float.PositiveInfinity"/> for
        /// one placed in a scene by hand.
        /// </summary>
        public float Remaining =>
            lifetime > 0f ? Mathf.Max(0f, lifetime - Age) : float.PositiveInfinity;

        /// <summary>
        /// Has this aperture outlived its <see cref="Lifetime"/>?
        ///
        /// Asked by <see cref="PortalPair"/> as well as by this component, so that the pair can
        /// let go of the slot in the same frame the aperture closes rather than a frame later
        /// holding a destroyed reference.
        /// </summary>
        public bool Expired => lifetime > 0f && Age >= lifetime;

        /// <summary>Where a traveller was, last time this aperture looked.</summary>
        private struct Sample
        {
            public float Side;
            public Vector3 Point;

            /// <summary>Clock at the moment this traveller last came out of an aperture.</summary>
            public float TraversedAt;
        }

        private readonly Dictionary<PortalTraveller, Sample> tracked =
            new Dictionary<PortalTraveller, Sample>();
        private readonly List<PortalTraveller> departed = new List<PortalTraveller>();
        private readonly List<PortalTraveller> pass = new List<PortalTraveller>();
        private readonly HashSet<PortalTraveller> seen = new HashSet<PortalTraveller>();

        /// <summary>
        /// Shared scratch for the volume sweep.
        ///
        /// Static and shared because the sweep is synchronous and finishes before the next portal
        /// starts one — the same shape AlertBroadcaster and NoiseEmitter use for theirs. 64 is far
        /// more than an aperture-sized box ever holds; anything past it is dropped, which costs a
        /// frame of tracking on the least relevant collider in an already absurd pile.
        /// </summary>
        private static readonly Collider[] Sweep = new Collider[64];

        /// <summary>
        /// How long after coming out of an aperture a traveller is ignored by it.
        ///
        /// An exit puts the traveller in FRONT of the destination moving away, which is not a
        /// crossing — but a body that is also being pushed by physics on the same frame can dip
        /// back over the plane once, and re-crossing immediately sends it straight back where it
        /// came from. Rare, and catastrophic when it happens, so it is bought out cheaply.
        /// </summary>
        private const float ReentryCooldown = 0.2f;

        /// <summary>
        /// Slack added to the swept box, beyond the aperture itself.
        ///
        /// The sweep decides who is allowed through the wall, not who teleports —
        /// <see cref="WithinAperture"/> still gates the crossing — so it is deliberately generous.
        /// A traveller dropped out of the far side lands near the rim, and a box measured exactly
        /// to the opening would fail to see them for the frame it matters most.
        /// </summary>
        private const float SweepMargin = 0.4f;

        private bool closing;

        // Per-portal MATERIAL INSTANCES, not MaterialPropertyBlocks.
        //
        // The block version bound correctly by every measurement — GetPropertyBlock
        // came back holding the right RenderTexture, _HasView at 1, _Open at 1 —
        // and the shader still sampled solid black. Whatever the reason (and the
        // SRP Batcher was ruled out by switching it off), a per-object texture
        // override through a property block is not a mechanism worth trusting for
        // the one thing this whole feature exists to show. An instanced material
        // is the mechanism the renderer cannot ignore.
        //
        // The cost is one material per aperture, and there are two per player.
        private Material surfaceMaterial;
        private Material rimMaterial;
        private Material surfaceSource;
        private Material rimSource;

        private float openedAt = -999f;
        private Color colour = Color.white;

        /// <summary>
        /// A clock that runs outside play mode.
        ///
        /// <c>Time.time</c> is play-mode time and sits at zero in the editor, so
        /// the opening animation below measured an age of zero forever, _Open
        /// stayed at 0, and the surface shader clipped the entire aperture away.
        /// The symptom was a portal that rendered its view perfectly into a
        /// texture nobody could see — invisible in the Scene view and in any
        /// edit-mode capture, and fine the moment you pressed Play, which is the
        /// worst possible way for a bug to present itself.
        /// </summary>
        private static float Clock =>
            Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            EnsureMaterials();
            ApplySize();
        }

        /// <summary>
        /// Give this aperture its own copy of the surface and halo materials.
        ///
        /// Hidden and not saved, and the originals are put back in OnDisable, so
        /// a portal placed in a scene does not leave an instanced material
        /// behind in it.
        /// </summary>
        private void EnsureMaterials()
        {
            if (surfaceRenderer != null && surfaceMaterial == null &&
                surfaceRenderer.sharedMaterial != null)
            {
                surfaceSource = surfaceRenderer.sharedMaterial;
                surfaceMaterial = new Material(surfaceSource)
                {
                    name = surfaceSource.name + " (portal instance)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                surfaceRenderer.sharedMaterial = surfaceMaterial;
            }

            if (rimRenderer != null && rimMaterial == null &&
                rimRenderer.sharedMaterial != null)
            {
                rimSource = rimRenderer.sharedMaterial;
                rimMaterial = new Material(rimSource)
                {
                    name = rimSource.name + " (portal instance)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                rimRenderer.sharedMaterial = rimMaterial;
            }
        }

        private void ReleaseMaterials()
        {
            if (surfaceMaterial != null)
            {
                if (surfaceRenderer != null) surfaceRenderer.sharedMaterial = surfaceSource;
                DestroyMaterial(surfaceMaterial);
                surfaceMaterial = null;
            }

            if (rimMaterial != null)
            {
                if (rimRenderer != null) rimRenderer.sharedMaterial = rimSource;
                DestroyMaterial(rimMaterial);
                rimMaterial = null;
            }
        }

        private static void DestroyMaterial(Material material)
        {
            // Destroy is deferred to the end of a frame an editor scene does not
            // have, so outside play mode the instance would survive and leak.
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }

        /// <summary>
        /// Work out what wall a hand-placed aperture is cut into.
        ///
        /// An aperture the gun opens is told, in <see cref="Place"/>. One dragged into a scene by
        /// a designer never goes through that call, and without this its pass-through list is
        /// empty — so it looks like a way through and stops the player dead in the picture, which
        /// is the single worst way for a portal to be wrong.
        ///
        /// Start rather than Awake: a collider is not necessarily registered with the physics
        /// scene until every Awake in the scene has run, and this is a physics query.
        /// </summary>
        private void Start()
        {
            if (hostSurfaces.Count == 0) GatherHostSurfaces();
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            openedAt = Clock;

            // Asking for the renderer here is what makes a portal self-sufficient:
            // nothing has to be placed in a scene, and a portal dropped into an
            // empty scene still draws. The first one to exist builds it.
            _ = PortalRenderer.Instance;
        }

        private void OnDisable()
        {
            All.Remove(this);
            ReleaseMaterials();

            // Release every traveller still straddling the plane. Skipping this
            // leaves objects permanently ignoring the wall they were passing
            // through, which is invisible until something falls out of the level.
            ReleaseAll();

            // The renderer holds a screen-sized RenderTexture per aperture. An expiring portal is
            // the common case now rather than the exceptional one — one every twenty seconds per
            // barrel per player — so it says so itself instead of relying on whoever destroyed it
            // to remember. Existing, not Instance: this also runs during a scene unload, where
            // building a renderer to tell it about a portal that is going away is an error.
            if (PortalRenderer.Existing != null) PortalRenderer.Existing.Forget(this);
        }

        // ── Pairing and placement ──────────────────────────────────────────────

        /// <summary>
        /// Pair two apertures, unpairing whatever either was attached to.
        ///
        /// Passing null for one side is how a portal is orphaned — it keeps
        /// existing and showing its dead-end swirl, which is deliberate: a
        /// player who fires only one barrel should see something happen.
        /// </summary>
        public static void Link(Portal a, Portal b)
        {
            if (a != null && a.linked != null && a.linked != b) a.linked.linked = null;
            if (b != null && b.linked != null && b.linked != a) b.linked.linked = null;

            if (a != null) a.linked = b;
            if (b != null) b.linked = a;
        }

        /// <summary>
        /// Put the aperture on a surface and start its opening animation.
        ///
        /// <paramref name="host"/> is the collider it was cut into. It is
        /// remembered rather than modified: traversal switches collisions with
        /// it off per traveller, which is reversible and does not disturb the
        /// wall for anything that is not currently in the hole.
        /// </summary>
        public void Place(Vector3 position, Quaternion rotation, Collider host, int index)
        {
            transform.SetPositionAndRotation(position, rotation);
            hostSurface = host;
            this.index = index;
            openedAt = Clock;
            closing = false;

            // Anything still tracked belonged to the old location.
            ReleaseAll();

            ApplySize();
            GatherHostSurfaces();
        }

        /// <summary>
        /// Find every piece of wall directly behind the opening. See <see cref="HostSurfaces"/>.
        ///
        /// Once, at placement, rather than per frame: a wall does not move, and this is the only
        /// moment at which the aperture's position is new. Anything with a Rigidbody is left out —
        /// walls are static, and a crate leaning against one, or a creature standing on the far
        /// side, is not something a traveller should be allowed to walk through.
        /// </summary>
        private void GatherHostSurfaces()
        {
            hostSurfaces.Clear();
            if (hostSurface != null) hostSurfaces.Add(hostSurface);

            Vector3 centre = transform.position - transform.forward * (WallThickness * 0.5f);
            var half = new Vector3(size.x * 0.5f, size.y * 0.5f, WallThickness * 0.5f);

            int count = Physics.OverlapBoxNonAlloc(centre, half, Sweep, transform.rotation, ~0,
                                                   QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = Sweep[i];
                if (collider == null || collider.isTrigger) continue;
                if (collider.attachedRigidbody != null) continue;
                if (hostSurfaces.Contains(collider)) continue;

                hostSurfaces.Add(collider);
            }
        }

        /// <summary>
        /// How long this aperture has left, counted from now. 0 means it never expires.
        ///
        /// Set per shot rather than authored on the prefab, so the gun owns "portals last twenty
        /// seconds" while a pair placed in a scene by hand stays put. Every machine calls this with
        /// the same number the moment it opens its own copy of the aperture, which is why the
        /// lifetime needs nothing on the wire: the placement message is what synchronises them, and
        /// two machines timing the same twenty seconds from within a few milliseconds of each other
        /// is as synchronised as an expiry ever needs to be.
        /// </summary>
        public void SetLifetime(float seconds)
        {
            lifetime = Mathf.Max(0f, seconds);
            closing = false;
        }

        /// <summary>
        /// Shut the aperture now, telling whoever owns it first.
        ///
        /// Safe to call twice, and safe to call from inside <see cref="LateUpdate"/>: the removal
        /// from <see cref="All"/> happens in OnDisable, and nothing iterates that list from here.
        /// </summary>
        public void Close()
        {
            if (closing) return;
            closing = true;

            Closed?.Invoke(this);

            // Break the pairing before the object goes, so the survivor stops rendering a view of
            // an aperture that is being destroyed rather than finding out through a Unity fake-null
            // on some later frame.
            Link(this, null);

            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        public void SetSize(Vector2 metres)
        {
            size = metres;
            ApplySize();
        }

        /// <summary>Tint both halves of the aperture. Called once, at spawn.</summary>
        public void SetColour(Color colour)
        {
            EnsureMaterials();

            this.colour = colour;

            if (surfaceMaterial != null) surfaceMaterial.SetColor(BodyColourId, colour);
            if (rimMaterial != null) rimMaterial.SetColor(RimColourId, colour);
        }

        private void ApplySize()
        {
            // Only the child quads carry the size. The portal root must stay at
            // unit scale, because TransferFrom composes its localToWorldMatrix
            // with another portal's worldToLocalMatrix — any scale there would
            // be applied to the traveller as well, and a player would come out
            // of a wide portal wider than they went in.
            if (surfaceRenderer != null)
                surfaceRenderer.transform.localScale = new Vector3(size.x, size.y, 1f);

            if (rimRenderer != null)
                rimRenderer.transform.localScale = new Vector3(size.x * 1.5f, size.y * 1.35f, 1f);

            if (travellerVolume != null)
            {
                travellerVolume.isTrigger = true;
                travellerVolume.size = new Vector3(size.x, size.y, volumeDepth * 2f);
                travellerVolume.center = Vector3.zero;

                // Kept for the inspector and the gizmo, switched off for physics. Traversal sweeps
                // the same box itself (see the file header), so this collider has no job left — and
                // leaving a live trigger hanging in the middle of a room is not free: every query
                // in the game that does not pass QueryTriggerInteraction.Ignore, which is most of
                // them, would find the portal's own volume in front of the wall it is cut into.
                if (Application.isPlaying) travellerVolume.enabled = false;
            }
        }

        // ── The transfer matrix — the single source of truth ───────────────────

        /// <summary>
        /// The transform taking anything measured against <paramref name="from"/>
        /// into the space of this portal, turned to face back out of it.
        ///
        /// The 180 degree spin is the whole trick. Portals face out of their
        /// walls, so a naive mapping would drop the traveller behind this one,
        /// walking into the wall it is cut into. Turning them about the
        /// aperture's up axis is what makes "into the back of one" mean "out of
        /// the front of the other".
        ///
        /// Used for the camera pose, the traveller's position, their rotation
        /// and their velocity. They cannot be allowed to disagree.
        /// </summary>
        public Matrix4x4 TransferFrom(Portal from)
        {
            if (from == null) return Matrix4x4.identity;

            return transform.localToWorldMatrix
                 * Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f))
                 * from.transform.worldToLocalMatrix;
        }

        /// <summary>Signed distance along the outward normal. Negative means behind the plane.</summary>
        public float SideOf(Vector3 worldPoint) =>
            Vector3.Dot(worldPoint - transform.position, transform.forward);

        /// <summary>
        /// Is <paramref name="worldPoint"/> inside the elliptical opening, rather
        /// than merely somewhere on its infinite plane?
        ///
        /// Without this, walking anywhere along the wall a portal is set into
        /// teleports you, because the plane does not end where the picture does.
        /// </summary>
        public bool WithinAperture(Vector3 worldPoint, float margin = 0f)
        {
            Vector3 local = transform.InverseTransformPoint(worldPoint);
            float u = local.x / Mathf.Max(size.x * 0.5f + margin, 1e-4f);
            float v = local.y / Mathf.Max(size.y * 0.5f + margin, 1e-4f);
            return u * u + v * v <= 1f;
        }

        /// <summary>
        /// The aperture a straight move from <paramref name="from"/> to <paramref name="to"/>
        /// passes through, or null.
        ///
        /// This is the traversal path for everything that moves by rewriting its
        /// own transform instead of being pushed around by physics — which is
        /// every raycasting projectile in the game. The trigger volume cannot
        /// serve them: a collider with no Rigidbody raises no trigger callback
        /// at all, and even one that did is only sampled once a frame, which a
        /// bullet covering fifty metres a second steps clean over. A segment
        /// test has neither problem and is exact at any speed.
        ///
        /// Front to back only, and the nearest crossing wins, so a shot lined up
        /// through both apertures at once goes through the first one it meets.
        /// </summary>
        public static Portal Crossing(Vector3 from, Vector3 to, out Vector3 entry,
                                      out Matrix4x4 transfer)
        {
            entry = to;
            transfer = Matrix4x4.identity;

            Portal best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < All.Count; i++)
            {
                Portal portal = All[i];
                if (portal == null || portal.Linked == null) continue;

                float before = portal.SideOf(from);
                float after = portal.SideOf(to);

                // The back of an aperture is the wall it is cut into, so only a
                // front-to-back move is a way through.
                if (before <= 0f || after > 0f) continue;

                Vector3 point = Vector3.Lerp(from, to, before / Mathf.Max(before - after, 1e-6f));
                if (!portal.WithinAperture(point)) continue;

                float distance = (point - from).sqrMagnitude;
                if (distance >= nearest) continue;

                nearest = distance;
                best = portal;
                entry = point;
            }

            if (best == null) return null;

            transfer = best.Linked.TransferFrom(best);
            return best;
        }

        // ── Traversal ──────────────────────────────────────────────────────────

        /// <summary>
        /// Expire, sweep, then watch every tracked traveller for the moment it crosses the plane.
        ///
        /// LateUpdate rather than FixedUpdate: the clone has to be posed after
        /// everything that moves the original has run, or it lags a frame behind
        /// and the two halves of the object visibly disagree at the seam.
        /// </summary>
        private void LateUpdate()
        {
            // The WINDOW is drawn outside play mode — that is what ExecuteAlways is for, so an
            // aperture being placed in a level shows what is on the other side.
            PublishSurfaceState();

            // The DOOR is not. The sweep adopts whatever it finds, adding a PortalTraveller to it
            // where there is none, and doing that while somebody is editing a scene would quietly
            // attach components to authored objects and dirty the scene they are working in. Play
            // mode, or a test calling AdvanceTraversal deliberately.
            if (Application.isPlaying) AdvanceTraversal();
        }

        /// <summary>
        /// One frame of the door: expire, sweep the opening, then carry whatever crossed.
        ///
        /// Public and separate from <see cref="LateUpdate"/> so the behaviour can be stepped
        /// deliberately — which is the only way it can be tested at all. LateUpdate does not run
        /// outside play mode, and the failure this replaced was precisely a frame hook that was
        /// never called: a test that cannot drive the step cannot tell "the traversal is wrong"
        /// from "the traversal never happens", and those are the same symptom on screen.
        /// </summary>
        public void AdvanceTraversal()
        {
            if (Expired)
            {
                Close();
                return;
            }

            SweepVolume();
            StepCrossings();
        }

        /// <summary>
        /// Find everything standing in the opening, and let go of everything that has left.
        ///
        /// This is the half that used to be Unity's job through OnTriggerEnter and never once ran —
        /// see the file header. Doing it by hand also buys two things the callbacks could not: it
        /// works for anything, however it is built and whatever moves it, and it re-acquires a
        /// traveller that was already standing in the aperture when it opened, which a trigger
        /// enter message never fires for.
        /// </summary>
        private void SweepVolume()
        {
            // An unpaired aperture is a picture, not a door. Sweeping it anyway would switch off
            // the wall it is cut into for anybody who walked up to a dead end, and they would step
            // into solid rock.
            if (Linked == null)
            {
                ReleaseAll();
                return;
            }

            seen.Clear();

            var half = new Vector3(size.x * 0.5f + SweepMargin,
                                   size.y * 0.5f + SweepMargin,
                                   volumeDepth);

            int count = Physics.OverlapBoxNonAlloc(transform.position, half, Sweep,
                                                   transform.rotation, ~0,
                                                   QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider collider = Sweep[i];
                if (collider == null || !MightTravel(collider)) continue;

                // createIfMissing: anything that moves gets a traveller the moment it reaches an
                // aperture, so nothing has to be prepared in advance to be able to go through.
                // See PortalTraveller.For.
                PortalTraveller traveller = PortalTraveller.For(collider, createIfMissing: true);
                if (traveller == null || !seen.Add(traveller)) continue;

                if (tracked.ContainsKey(traveller)) continue;

                tracked[traveller] = new Sample
                {
                    Side = SideOf(traveller.TrackedPoint),
                    Point = traveller.TrackedPoint,

                    // Explicitly, not left at the struct's zero: play-mode time STARTS at zero, so
                    // a default would put every traveller inside the re-entry cooldown for the
                    // first fifth of a second of a session.
                    TraversedAt = float.NegativeInfinity,
                };
                traveller.EnterPortal(this);
            }

            departed.Clear();
            foreach (KeyValuePair<PortalTraveller, Sample> entry in tracked)
                if (entry.Key == null || !seen.Contains(entry.Key)) departed.Add(entry.Key);

            foreach (PortalTraveller traveller in departed)
            {
                Release(traveller);
                tracked.Remove(traveller);
            }
        }

        /// <summary>
        /// Could this collider belong to something that goes through a hole?
        ///
        /// A cheap gate in front of <see cref="PortalTraveller.For"/>, which walks a hierarchy
        /// three times. The sweep is unmasked on purpose — a portal has to work on whatever layer
        /// the level happens to use — so most of what it returns every frame is the wall the
        /// aperture is cut into, and answering "no" for those without touching their parents is
        /// what keeps an unmasked sweep affordable.
        /// </summary>
        private static bool MightTravel(Collider collider) =>
            collider.attachedRigidbody != null
            || collider is CharacterController
            || collider.GetComponentInParent<PortalTraveller>() != null;

        private void StepCrossings()
        {
            if (tracked.Count == 0) return;

            // Over a snapshot of the keys rather than the dictionary itself. Every traveller in
            // this loop has its sample rewritten, and one of them may traverse — which hands it to
            // the far aperture, whose Adopt writes into a dictionary that could be this one when a
            // portal is somehow linked to itself. Enumerating a copy is a line of code; the
            // alternative is an InvalidOperationException thrown out of a frame update.
            pass.Clear();
            foreach (KeyValuePair<PortalTraveller, Sample> entry in tracked)
                pass.Add(entry.Key);

            departed.Clear();

            foreach (PortalTraveller traveller in pass)
            {
                if (traveller == null)
                {
                    departed.Add(traveller);
                    continue;
                }

                if (!tracked.TryGetValue(traveller, out Sample previous)) continue;

                Vector3 point = traveller.TrackedPoint;
                float current = SideOf(point);

                traveller.UpdateClone(this);

                if (Crossed(previous, point, current) && Linked != null &&
                    Clock - previous.TraversedAt > ReentryCooldown)
                {
                    departed.Add(traveller);
                    Traverse(traveller);
                    continue;
                }

                tracked[traveller] = new Sample
                {
                    Side = current,
                    Point = point,
                    TraversedAt = previous.TraversedAt,
                };
            }

            foreach (PortalTraveller traveller in departed)
                tracked.Remove(traveller);
        }

        /// <summary>
        /// Did the move from <paramref name="previous"/> to <paramref name="point"/> go through
        /// the opening?
        ///
        /// The aperture test is applied where the SEGMENT met the plane, not where the traveller
        /// ended up. Those are the same point only for something moving slowly and straight on: a
        /// player sprinting diagonally through the edge of an opening is a step that starts inside
        /// the ellipse and finishes well outside it, and testing the endpoint refuses exactly the
        /// crossings that feel most like they should have worked.
        /// </summary>
        private bool Crossed(Sample previous, Vector3 point, float current)
        {
            // Front to back only. The back of an aperture is the wall it is cut into.
            if (previous.Side <= 0f || current > 0f) return false;

            // Far enough to be a real crossing rather than a body resting on the plane jittering.
            float travelled = previous.Side - current;
            if (travelled <= minimumCrossingSpeed) return false;

            Vector3 meeting = Vector3.Lerp(previous.Point, point,
                                           previous.Side / Mathf.Max(travelled, 1e-6f));

            return WithinAperture(meeting, 0.25f);
        }

        private void ReleaseAll()
        {
            if (tracked.Count == 0) return;

            departed.Clear();
            foreach (KeyValuePair<PortalTraveller, Sample> entry in tracked)
                departed.Add(entry.Key);

            foreach (PortalTraveller traveller in departed)
                Release(traveller);

            tracked.Clear();
        }

        private void Traverse(PortalTraveller traveller)
        {
            Portal destination = Linked;
            Matrix4x4 transfer = destination.TransferFrom(this);

            Release(traveller);
            traveller.Traverse(this, destination, transfer);

            // Hand the traveller straight to the destination's bookkeeping,
            // already on the far side. Waiting for OnTriggerEnter there would
            // leave it colliding with the wall behind the exit for one physics
            // step, which at any speed means being shoved back out.
            destination.Adopt(traveller);
        }

        /// <summary>Take over a traveller that has just arrived out of this aperture.</summary>
        internal void Adopt(PortalTraveller traveller)
        {
            if (traveller == null) return;

            tracked[traveller] = new Sample
            {
                Side = SideOf(traveller.TrackedPoint),
                Point = traveller.TrackedPoint,
                TraversedAt = Clock,
            };
            traveller.EnterPortal(this);
        }

        /// <summary>Stop a traveller passing through the wall this portal is cut into.</summary>
        private void Release(PortalTraveller traveller)
        {
            if (traveller == null) return;
            traveller.ExitPortal(this);
        }

        // ── Presentation ───────────────────────────────────────────────────────

        /// <summary>
        /// Push the aperture's current state onto its materials.
        ///
        /// Called from LateUpdate for the opening animation, and again by
        /// PortalRenderer the moment it has finished rendering this portal.
        /// The second call is the one that matters: the render target is
        /// created and filled during beginContextRendering, which is AFTER
        /// every LateUpdate in the frame, so a surface that only heard about
        /// its texture from LateUpdate showed the previous frame's — and, on
        /// the frame a portal opens, showed none at all. That last case is
        /// indistinguishable from the portal being broken, which is exactly
        /// what it looked like: a lit rim around a black hole.
        /// </summary>
        internal void PublishSurfaceState()
        {
            float open = openDuration > 0f
                ? Mathf.Clamp01((Clock - openedAt) / openDuration)
                : 1f;

            // Eased, so the aperture snaps wide and then settles rather than
            // growing linearly, which reads as a loading bar.
            open = 1f - (1f - open) * (1f - open);

            // The iris shutting at the end of a timed life. Taken out of the lifetime rather than
            // added to it, so "portals last twenty seconds" is true of when they are GONE — and so
            // that a player watching one narrow has a moment's warning rather than having the hole
            // vanish from under them.
            if (lifetime > 0f && closeDuration > 0f)
                open = Mathf.Min(open, Mathf.Clamp01(Remaining / closeDuration));

            EnsureMaterials();

            if (surfaceMaterial != null)
            {
                surfaceMaterial.SetFloat(OpenId, open);
                surfaceMaterial.SetFloat(HasViewId,
                    ViewTexture != null && Linked != null ? 1f : 0f);
                if (ViewTexture != null) surfaceMaterial.SetTexture(ViewId, ViewTexture);
            }

            if (rimMaterial != null) rimMaterial.SetFloat(OpenId, open);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0.02f));
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, volumeDepth * 2f));
            Gizmos.DrawRay(Vector3.zero, Vector3.forward * 0.6f);
        }
    }
}
