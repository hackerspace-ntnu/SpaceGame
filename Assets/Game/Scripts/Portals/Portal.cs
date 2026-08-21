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
        [Tooltip("Trigger volume straddling the plane. Anything inside it is a candidate for traversal.")]
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

        private readonly Dictionary<PortalTraveller, float> tracked =
            new Dictionary<PortalTraveller, float>();
        private readonly List<PortalTraveller> departed = new List<PortalTraveller>();

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
            foreach (PortalTraveller traveller in new List<PortalTraveller>(tracked.Keys))
                Release(traveller);
            tracked.Clear();
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

            // Anything still tracked belonged to the old location.
            foreach (PortalTraveller traveller in new List<PortalTraveller>(tracked.Keys))
                Release(traveller);
            tracked.Clear();

            ApplySize();
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

        private void OnTriggerEnter(Collider other)
        {
            // createIfMissing: anything that moves gets a traveller the moment
            // it touches an aperture, so nothing has to be prepared in advance
            // to be able to go through. See PortalTraveller.For.
            PortalTraveller traveller = PortalTraveller.For(other, createIfMissing: true);
            if (traveller == null || tracked.ContainsKey(traveller)) return;

            tracked[traveller] = SideOf(traveller.TrackedPoint);
            traveller.EnterPortal(this);
        }

        private void OnTriggerExit(Collider other)
        {
            PortalTraveller traveller = PortalTraveller.For(other);

            if (traveller == null || !tracked.ContainsKey(traveller)) return;

            // A traveller that just went through leaves this trigger too, and
            // it has already been released by Traverse. Releasing twice is
            // harmless, but re-enabling collision with a wall it is now behind
            // is not — Release checks that for us.
            Release(traveller);
            tracked.Remove(traveller);
        }

        /// <summary>
        /// Watch every tracked traveller for the moment it crosses the plane.
        ///
        /// LateUpdate rather than FixedUpdate: the clone has to be posed after
        /// everything that moves the original has run, or it lags a frame behind
        /// and the two halves of the object visibly disagree at the seam.
        /// </summary>
        private void LateUpdate()
        {
            PublishSurfaceState();

            if (tracked.Count == 0) return;

            departed.Clear();

            foreach (KeyValuePair<PortalTraveller, float> entry in tracked)
            {
                PortalTraveller traveller = entry.Key;
                if (traveller == null)
                {
                    departed.Add(traveller);
                    continue;
                }

                float previous = entry.Value;
                float current = SideOf(traveller.TrackedPoint);

                traveller.UpdateClone(this);

                // Crossed from the front to the back of the plane, far enough
                // to be a real crossing rather than jitter, and through the
                // opening rather than through the wall beside it.
                bool crossed = previous > 0f && current <= 0f
                            && Mathf.Abs(current - previous) > minimumCrossingSpeed
                            && WithinAperture(traveller.TrackedPoint, 0.25f);

                if (crossed && Linked != null)
                {
                    departed.Add(traveller);
                    Traverse(traveller);
                    continue;
                }

                tracked[traveller] = current;
            }

            foreach (PortalTraveller traveller in departed)
                tracked.Remove(traveller);
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
            tracked[traveller] = SideOf(traveller.TrackedPoint);
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
