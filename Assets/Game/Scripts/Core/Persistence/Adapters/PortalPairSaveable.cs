using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;
using SpaceGame.Portals;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists the two apertures a player left standing in the world.
    ///
    /// <para>
    /// <b>Why this cannot be an entity record.</b> A portal is not spawned through
    /// <c>GameServices.World.Spawn</c> — <see cref="PortalPair.Open"/> calls <c>Instantiate</c>
    /// directly, deliberately, because a portal is not a networked object at all: placement travels
    /// as a message and every machine builds its own copy. So a portal has no
    /// <c>SaveableEntity</c>, no <c>prefabId</c> and nothing for the world store to rebuild it
    /// from, and it never appeared in a save file in any form.
    /// </para>
    /// <para>
    /// <b>Why it lives on the player.</b> Because the pair does. <c>PortalPair.Of</c> adds itself to
    /// the shooter, so that switching hotbar slot — which destroys the gun — does not take the
    /// player's portals down with it. Two players each own their own orange and blue and neither can
    /// close the other's, and a record that belongs to the player rather than to the world is the
    /// only shape that keeps that true. Same reasoning as <c>BackpackSaveable</c>, which keeps a
    /// deployed pack's contents on the player who deployed it.
    /// </para>
    /// <para>
    /// <b>Why it is deferred.</b> Two things have to exist before a portal can be put back, and
    /// neither does at restore time: the WALL — <c>hostSurface</c> is a live <c>Collider</c>, which
    /// no record can hold, and is re-found by probing behind the aperture exactly the way
    /// <c>PortalGunItem.OpenPortal</c> re-finds it on each machine — and the GROUND the wall is part
    /// of, which is still streaming in. Re-placed too early, a portal opens against nothing and
    /// loses its collision pass-through, which is the bug where you walk into the wall the aperture
    /// is cut into.
    /// </para>
    /// </summary>
    public class PortalPairSaveable : MonoBehaviour, ISaveable, IDeferredSaveable
    {
        public const string Key = "portals";       // written into save files — NEVER rename

        [Tooltip("Aperture prefab to re-open saved portals from. Optional — left empty, the portal " +
                 "gun item's own prefab reference is used, which is the same one that opened them.")]
        [SerializeField] private Portal portalPrefab;

        [Tooltip("Layers a restored portal may re-find its wall on. Matches the gun's surface mask.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        private State pending;
        private bool hasPending;

        public string SaveKey => Key;

        public struct PortalState
        {
            public bool open;
            public Vector3 position;
            public Quaternion rotation;
            public Vector2 size;

            /// <summary>
            /// The blobs of paint this aperture is made of — x and y in the portal's own plane,
            /// z the radius, all in metres.
            ///
            /// Absent from every record written before the gun became a spray can, and from every
            /// aperture placed in a scene by hand, where it deserialises to null. It means the
            /// same thing both times: this one is the ellipse inscribed in <see cref="size"/>.
            /// </summary>
            public Vector3[] dabs;
            public Color colour;

            /// <summary>
            /// Seconds this aperture had left when the world was written, or 0 for one that never
            /// expires.
            ///
            /// Remaining rather than a start time, because a save file has no clock the next
            /// session shares — <c>Time.time</c> restarts at zero and the wall clock is not what
            /// the aperture was counting. Storing what is LEFT means a portal fired ten seconds
            /// before a save comes back with ten seconds on it, whenever that turns out to be.
            ///
            /// Absent from records written before portals expired at all, where it deserialises to
            /// 0 — which is the same value a scene-placed aperture writes, and means the same
            /// thing: this one does not run out.
            /// </summary>
            public float remaining;
        }

        public struct State
        {
            public PortalState primary;
            public PortalState secondary;

            /// <summary>
            /// Whether the two were paired. Captured rather than inferred from both being open,
            /// because an unpaired aperture is a real state the gun can produce — one barrel fired
            /// — and it shows a dead-end swirl instead of a way through.
            /// </summary>
            public bool linked;
        }

        public object CaptureState()
        {
            // GetComponent and never PortalPair.Of: capture must not mutate, and Of() would add a
            // PortalPair to every player who has never touched the gun.
            PortalPair pair = GetComponent<PortalPair>();
            if (pair == null) return null;

            Portal primary = pair.Get(PortalPair.Primary);
            Portal secondary = pair.Get(PortalPair.Secondary);

            if (primary == null && secondary == null) return null;

            return new State
            {
                primary = Describe(primary),
                secondary = Describe(secondary),
                linked = primary != null && secondary != null && primary.Linked == secondary,
            };
        }

        /// <summary>
        /// Restore and re-open in one go, for a caller with no deferred pass to wait for.
        ///
        /// <para>
        /// <see cref="RestoreState"/> only STAGES — the aperture is actually cut in
        /// <see cref="OnLoadComplete"/>, which <c>SaveManager</c> invokes from its deferred load
        /// passes. A client joining a running session never runs one, so a staged record would sit
        /// there for ever. <c>SessionSnapshot</c> calls this instead, and only once the shooter
        /// exists on this machine, which is the one thing the deferred pass was buying.
        /// </para>
        /// </summary>
        public void ApplyNow(JObject state)
        {
            RestoreState(state);
            OnLoadComplete();
        }

        public void RestoreState(JObject state)
        {
            hasPending = false;
            pending = default;

            if (state == null)
            {
                // No portals in the record means the player had none. Said out loud rather than
                // assumed, because a load into a live session meets a player who may be standing
                // between two apertures they opened a minute ago.
                PortalPair live = GetComponent<PortalPair>();
                if (live != null) live.CloseAll();
                return;
            }

            pending = state.ToObject<State>(SaveSerializer.Serializer);
            hasPending = pending.primary.open || pending.secondary.open;
        }

        /// <summary>
        /// Re-open the apertures, once there is a world for them to be cut into.
        ///
        /// Runs many times — once world-wide, again on every player bind, again per late chunk
        /// hydrate — so it must be idempotent. It is consumed on the first pass that succeeds in
        /// finding a prefab, because unlike a rider, nothing it needs is another party who might
        /// still be arriving; re-applying it later would move portals the player has since re-fired.
        /// </summary>
        public void OnLoadComplete()
        {
            if (!hasPending) return;

            Portal prefab = ResolvePrefab();
            if (prefab == null)
            {
                Debug.LogWarning($"[Save] '{name}' had saved portals and no aperture prefab to " +
                                 "re-open them from. Assign one on PortalPairSaveable, or make sure " +
                                 "the portal gun item is in the item registry.", this);
                hasPending = false;
                return;
            }

            PortalPair pair = PortalPair.Of(gameObject);
            if (pair == null) return;

            hasPending = false;

            Reopen(pair, prefab, PortalPair.Primary, pending.primary);
            Reopen(pair, prefab, PortalPair.Secondary, pending.secondary);

            // Open() links whichever apertures exist, so an unlinked pair has to be taken apart
            // again afterwards — the record's answer wins over the convenience default.
            if (!pending.linked)
                Portal.Link(pair.Get(PortalPair.Primary), null);
        }

        /// <summary>The paint an aperture is made of, or null if it is a plain ellipse.</summary>
        public static Vector3[] DescribeDabs(Portal portal)
        {
            if (portal == null || portal.Stencil.IsEllipse) return null;

            var dabs = new Vector3[portal.Stencil.Count];

            for (int i = 0; i < dabs.Length; i++)
            {
                PortalDab dab = portal.Stencil.Dabs[i];
                dabs[i] = new Vector3(dab.Centre.x, dab.Centre.y, dab.Radius);
            }

            return dabs;
        }

        /// <summary>
        /// Put <paramref name="dabs"/> back on <paramref name="portal"/>, or leave its ellipse
        /// alone.
        ///
        /// Doing nothing for a null array is the whole backwards-compatibility story: a record
        /// written before spraying existed has no dabs, and the aperture it restores is the
        /// ellipse <see cref="PortalPair.Open"/> already gave it.
        /// </summary>
        public static void ApplyDabs(Portal portal, Vector3[] dabs)
        {
            if (portal == null || dabs == null || dabs.Length == 0) return;

            portal.BeginStroke();

            for (int i = 0; i < dabs.Length; i++)
                portal.AddDab(new Vector2(dabs[i].x, dabs[i].y), dabs[i].z);
        }

        private static PortalState Describe(Portal portal)
        {
            if (portal == null) return default;

            return new PortalState
            {
                open = true,
                position = portal.transform.position,
                rotation = portal.transform.rotation,
                size = portal.Size,
                dabs = DescribeDabs(portal),
                colour = portal.Colour,

                // Floored just above zero rather than clamped to it: zero is the "never expires"
                // value, so an aperture with a hundredth of a second left must not come back
                // immortal. It shuts on the frame after the load instead, which is what it was
                // about to do anyway.
                remaining = portal.Lifetime <= 0f
                    ? 0f
                    : Mathf.Max(0.01f, portal.Remaining),
            };
        }

        private void Reopen(PortalPair pair, Portal prefab, int index, PortalState state)
        {
            if (!state.open) return;

            Portal portal = pair.Open(index, prefab, state.position, state.rotation,
                                      FindHost(state.position, state.rotation), state.size,
                                      state.colour, state.remaining);

            ApplyDabs(portal, state.dabs);
        }

        /// <summary>
        /// The wall this aperture was cut into, probed for the way the gun probes for it.
        ///
        /// A null answer is survivable and deliberately not an error: it costs traversal its
        /// collision pass-through and nothing else, which is exactly what the gun accepts when it
        /// opens a portal on geometry that has since moved.
        /// </summary>
        private Collider FindHost(Vector3 position, Quaternion rotation)
        {
            Vector3 normal = rotation * Vector3.forward;

            return Physics.Raycast(position + normal * 0.2f, -normal, out RaycastHit hit, 0.6f,
                                   surfaceMask, QueryTriggerInteraction.Ignore)
                ? hit.collider
                : null;
        }

        /// <summary>
        /// The aperture prefab, from the inspector if somebody wired one and otherwise from the
        /// portal gun item itself — which is where the answer really lives, and means restoring
        /// portals needs no asset wiring at all.
        /// </summary>
        private Portal ResolvePrefab()
        {
            if (portalPrefab != null) return portalPrefab;

            foreach (InventoryItem item in Registry<InventoryItem>.All)
            {
                if (item == null || item.itemPrefab == null) continue;

                var gun = item.itemPrefab.GetComponentInChildren<PortalGunItem>(true);
                if (gun == null || gun.PortalPrefab == null) continue;

                portalPrefab = gun.PortalPrefab;
                return portalPrefab;
            }

            return null;
        }
    }
}
