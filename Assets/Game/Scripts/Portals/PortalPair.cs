// The two apertures belonging to one shooter.
//
// It lives on the PLAYER, not on the gun, and that placement is the whole point
// of the class. Portals outlive the thing that made them: switching hotbar slot
// destroys the gun prefab, and a pair owned by the gun would take the player's
// portals down with it — which, since EquipmentController rebuilds the held
// item on every slot change, means portals vanishing whenever you glance at
// your inventory. Owning them per player also gives multiplayer the behaviour
// everyone expects for free: two players each have their own orange and blue,
// and neither can close the other's.
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Portals
{
    [DisallowMultipleComponent]
    public sealed class PortalPair : MonoBehaviour
    {
        /// <summary>The first barrel — orange. The one a player's very first shot comes out of.</summary>
        public const int Primary = 0;

        /// <summary>
        /// The second barrel — blue.
        ///
        /// There is no second trigger: both barrels are fired from the same button, in turn. See
        /// <see cref="PeekBarrel"/>, and the header of PortalGunItem for why the gun no longer has
        /// an alternate fire.
        ///
        /// Told apart by hue rather than by value, and deliberately: the two ends of a portal are
        /// the one pair of objects in this game a player has to identify instantly and from across
        /// a room, often through the other one. Two shades of the same colour lose that at the
        /// exact distance it matters.
        /// </summary>
        public const int Secondary = 1;

        private readonly Portal[] portals = new Portal[2];

        /// <summary>
        /// Which barrel the next shot should come out of.
        ///
        /// It lives here, with the portals, rather than on the gun, and for the same reason they
        /// do: the gun prefab is destroyed and rebuilt on every hotbar change, so a cursor kept on
        /// the item would reset to the orange barrel every time the player glanced at their
        /// inventory — and a gun that always fires the same barrel can only ever have one aperture
        /// open, which is precisely the failure this cursor exists to end.
        /// </summary>
        private int nextBarrel = Primary;

        /// <summary>
        /// How far off an aperture's plane a dab may land and still count as the same surface —
        /// the floor of it, for a very small blob.
        ///
        /// A real wall is bowed, panelled, and made of several colliders, and TERRAIN is none of
        /// those things: it rolls. A fixed 22 cm was fine on authored geometry and quietly wrong
        /// outdoors, where a stroke a couple of metres long climbs further than that over ordinary
        /// ground — so most of a sweep across a hillside was refused and the portal simply stopped
        /// following where the player was pointing.
        /// </summary>
        private const float PlaneToleranceFloor = 0.22f;

        /// <summary>
        /// How far off-plane paint may land, as a multiple of the blob's own radius.
        ///
        /// Scaling with the blob is what makes this self-tuning: how much ground a stroke covers,
        /// and therefore how much it can rise or fall over its length, is set by how big the blobs
        /// are. <see cref="Portal.ConformToSurface"/> is what then keeps the flattened result
        /// sitting on top of the bumps rather than buried in them.
        /// </summary>
        private const float PlaneToleranceRadii = 1.6f;

        /// <summary>How far off <paramref name="portal"/>'s plane paint of this size may still land.</summary>
        private static float PlaneToleranceFor(float radius) =>
            Mathf.Max(PlaneToleranceFloor, radius * PlaneToleranceRadii);

        /// <summary>
        /// The barrel a spray in progress is coming out of, or -1 when nobody is spraying.
        ///
        /// Here rather than on the gun for the same reason <see cref="nextBarrel"/> is:
        /// EquipmentController destroys and rebuilds the held item on every hotbar change, and a
        /// spray that survived a scroll of the wheel would carry on painting from a gun object
        /// that no longer exists.
        /// </summary>
        private int sprayBarrel = -1;

        private bool sprayGrows;
        private bool sprayStarted;
        private bool strokeBroken;
        private Vector3 lastDab;

        /// <summary>Which barrel is being sprayed right now, or -1.</summary>
        public int SprayBarrel => sprayBarrel;

        private void OnEnable()
        {
            this.NetOn(NetMsg.PortalsUsed, OnUsedRequested);
            this.NetOn(NetMsg.PortalsShut, OnShutElsewhere);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.PortalsUsed, OnUsedRequested);
            this.NetOff(NetMsg.PortalsShut, OnShutElsewhere);
        }

        /// <summary>
        /// Something went through this pair and closeOnTraversal shut it — on THIS machine.
        /// Tell the rest.
        ///
        /// Traversal is detected per machine from local physics, and a peer watching an
        /// interpolated remote body can miss the plane crossing entirely — so without this the
        /// pair stood open on that machine for the rest of its lifetime, and was still walkable
        /// there, while it was gone everywhere else. Announced by the machine that OWNS the
        /// traveller, because that is the one machine whose detection actually moved the body;
        /// every other machine's detection is cosmetic and may simply never fire.
        /// </summary>
        internal void AnnounceTraversal(PortalTraveller traveller)
        {
            if (traveller == null || !Network.Owns(traveller)) return;
            this.NetToServer(NetMsg.PortalsUsed);
        }

        /// <summary>
        /// Server side. Idempotent — the announcer has already shut its own copy, and offline the
        /// send above dispatches straight back into this handler on the same machine.
        /// </summary>
        private void OnUsedRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            this.NetToOthers(NetMsg.PortalsShut, arg, except: sender);
            ShutPair();
        }

        private void OnShutElsewhere(in NetArg arg, ulong sender) => ShutPair();

        /// <summary>
        /// Close both apertures without ending a spray in progress — the replicated mirror of
        /// Portal.ShutBehind, which also leaves the session alone. A spray pointed at a pair
        /// that has just been used simply opens a fresh aperture with its next blob, on every
        /// machine alike.
        /// </summary>
        private void ShutPair()
        {
            Close(Primary);
            Close(Secondary);
        }

        /// <summary>The pair belonging to <paramref name="owner"/>, created on first use.</summary>
        public static PortalPair Of(GameObject owner)
        {
            if (owner == null) return null;

            return owner.TryGetComponent(out PortalPair pair)
                ? pair
                : owner.AddComponent<PortalPair>();
        }

        public Portal Get(int index) =>
            index >= 0 && index < portals.Length ? portals[index] : null;

        /// <summary>How many of the two apertures are open right now.</summary>
        public int OpenCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < portals.Length; i++)
                    if (portals[i] != null) count++;
                return count;
            }
        }

        /// <summary>
        /// Which barrel the next shot should use, without claiming it.
        ///
        /// Peek and <see cref="CommitBarrel"/> are separate because a shot that fizzles — aimed at
        /// the sky, or at a surface that refuses portals — must not move the cursor on. Burning a
        /// barrel on a miss would mean two clicks at nothing leaves the player back where they
        /// started, and which barrel comes next would depend on what they had missed.
        ///
        /// <para>
        /// An EMPTY barrel always wins. Plain alternation is not enough on its own: once one
        /// aperture expires the cursor is as likely as not to be pointing at the one still open,
        /// and the shot that should have restored the pair would move the survivor instead —
        /// leaving one portal on screen and nothing to explain why.
        /// </para>
        /// </summary>
        public int PeekBarrel()
        {
            int other = 1 - nextBarrel;
            return portals[nextBarrel] != null && portals[other] == null ? other : nextBarrel;
        }

        /// <summary>
        /// A shot has gone out of <paramref name="index"/>; point the cursor at the other barrel.
        ///
        /// Called at the moment the shot is FIRED, not when the aperture opens, because the blob
        /// takes a visible fraction of a second to reach the wall. Waiting for the arrival would
        /// let two quick clicks both read "nothing open yet", both pick the same barrel, and the
        /// second simply move the aperture the first one had just placed.
        /// </summary>
        public void CommitBarrel(int index)
        {
            if (index < 0 || index >= portals.Length) return;
            nextBarrel = 1 - index;
        }

        /// <summary>
        /// Which barrel a spray starting at <paramref name="aimPoint"/> should come out of, and
        /// whether it is a top-up rather than a new aperture.
        ///
        /// Aiming at your own paint grows that aperture, in its own colour, instead of opening the
        /// other one. That is the only reason the gun needs an aim to pick a barrel at all — and
        /// it is what makes a portal that came out too small fixable rather than wasted. Anywhere
        /// else falls through to <see cref="PeekBarrel"/>, which is unchanged.
        /// </summary>
        public int ChooseSprayBarrel(Vector3 aimPoint, float margin, out bool grow)
        {
            for (int i = 0; i < portals.Length; i++)
            {
                Portal portal = portals[i];
                if (portal == null) continue;

                if (portal.DistanceFromPlane(aimPoint) > PlaneToleranceFloor) continue;
                if (!portal.WithinAperture(aimPoint, margin)) continue;

                grow = true;
                return i;
            }

            grow = false;
            return PeekBarrel();
        }

        /// <summary>
        /// Begin a spray on <paramref name="barrel"/>. Opens nothing yet — paint has to land first.
        ///
        /// <paramref name="grow"/> keeps whatever aperture is already in that slot and adds to its
        /// paint. False means the next dab to land re-places it somewhere new, with a clean shape.
        /// </summary>
        public void BeginSpray(int barrel, bool grow)
        {
            if (barrel < 0 || barrel >= portals.Length) return;

            sprayBarrel = barrel;
            sprayGrows = grow && portals[barrel] != null;
            sprayStarted = false;
            strokeBroken = false;
        }

        /// <summary>
        /// Lay <paramref name="steps"/> blobs of paint ending at <paramref name="worldPoint"/>,
        /// opening the aperture if this is the first one of the spray to land.
        ///
        /// The intermediate blobs are interpolated in the aperture's own plane by arithmetic and
        /// never by probing: every machine runs this from the same two points off the same hold
        /// stream, and a raycast in here would be the one place two machines could disagree about
        /// what shape the player painted.
        /// </summary>
        public Portal LayDab(Portal prefab, Vector3 worldPoint, Quaternion rotation, float radius,
                             int steps, Color colour, float lifetime, Collider host)
        {
            if (sprayBarrel < 0 || prefab == null) return null;

            Portal portal = portals[sprayBarrel];

            if (!sprayStarted)
            {
                sprayStarted = true;
                lastDab = worldPoint;

                if (!sprayGrows)
                {
                    // A zero size is how Open is told to leave the shape alone — see there. The
                    // aperture is placed here and given its outline dab by dab below.
                    portal = Open(sprayBarrel, prefab, worldPoint, rotation, host,
                                  Vector2.zero, colour, lifetime);
                    if (portal == null) return null;

                    portal.BeginStroke();
                }
            }

            if (portal == null) return null;

            // Paint that has left this wall does not belong to this aperture. Refused rather than
            // projected onto the plane anyway: projecting would put part of the opening inside the
            // masonry round the corner, which is far worse than a gap in the stroke.
            if (portal.DistanceFromPlane(worldPoint) > PlaneToleranceFor(radius))
            {
                lastDab = worldPoint;
                strokeBroken = true;
                return portal;
            }

            // A stroke resuming after paint went round a corner starts again rather than bridging
            // back to it. The projection of an off-plane point onto this wall is not somewhere the
            // player pointed, and interpolating from it paints a run of blobs across a stretch of
            // wall the jet never crossed.
            portal.AddStroke(portal.ToLocalPlane(lastDab), portal.ToLocalPlane(worldPoint),
                             strokeBroken ? 1 : steps, radius);

            // The shape just grew, so what it has to clear may have grown with it — a stroke that
            // reached onto a rise now has a rise under it.
            portal.ConformToSurface();

            lastDab = worldPoint;
            strokeBroken = false;
            return portal;
        }

        /// <summary>The trigger came up. There is nothing to tear down but the session itself.</summary>
        public void EndSpray()
        {
            sprayBarrel = -1;
            sprayGrows = false;
            sprayStarted = false;
            strokeBroken = false;
        }

        /// <summary>
        /// Open, or move, one of the two apertures.
        ///
        /// Moving the existing GameObject rather than destroying and respawning
        /// it keeps the aperture's material instances alive across a re-fire.
        /// Spawning a fresh pair of instanced materials every time somebody taps
        /// the trigger is a stutter you can hear the GC in.
        ///
        /// <paramref name="lifetime"/> is seconds until the aperture irises shut, or 0 for one
        /// that never does. It is set on every shot rather than authored on the prefab, because
        /// "how long a portal lasts" is the GUN's rule — a pair placed in a scene by hand is
        /// scenery and stays put.
        /// </summary>
        public Portal Open(int index, Portal prefab, Vector3 position, Quaternion rotation,
                           Collider host, Vector2 size, Color colour, float lifetime = 0f)
        {
            if (prefab == null || index < 0 || index >= portals.Length) return null;

            Portal portal = portals[index];
            if (portal == null)
            {
                portal = Instantiate(prefab);
                portal.name = $"Portal {(index == Primary ? "Primary" : "Secondary")} ({name})";
                portal.SetColour(colour);

                // An aperture now shuts on its own when its time is up, so the pair has to hear
                // about it from the portal rather than being the only thing that can end one.
                // Without this the slot holds a destroyed reference, and the next shot from that
                // barrel takes the "already have one, move it" branch on an object that is gone.
                portal.Closed += Forget;

                portals[index] = portal;
            }

            // A zero size means "leave the shape alone, dabs are coming" — the spray's way of
            // opening an aperture that has no outline yet. See LayDab.
            if (size.sqrMagnitude > 1e-6f) portal.SetSize(size);

            // On every Open, not only the instantiating one: a restore can hand a slot a portal
            // this pair has never seen. The back-reference is what lets a traversal replicate its
            // close — see AnnounceTraversal.
            portal.Pair = this;

            portal.Place(position, rotation, host, index);
            portal.SetLifetime(lifetime);

            Portal other = portals[1 - index];
            Portal.Link(portal, other);

            return portal;
        }

        /// <summary>Shut one aperture, leaving the other exactly as it is.</summary>
        public void Close(int index)
        {
            if (index < 0 || index >= portals.Length) return;

            Portal portal = portals[index];
            portals[index] = null;

            // Close() raises Closed, which lands in Forget below — harmless, because the slot is
            // already cleared. Nulling first rather than after is what makes the two entry points
            // (this one, and the portal expiring by itself) converge instead of racing.
            if (portal != null) portal.Close();
        }

        /// <summary>Shut both apertures — on death, on unequip-and-holster, on world unload.</summary>
        public void CloseAll()
        {
            // First, or a spray in flight is left pointing at an aperture that is being destroyed
            // and lays its next dab on a Unity fake-null.
            EndSpray();

            for (int i = 0; i < portals.Length; i++) Close(i);
        }

        /// <summary>
        /// An aperture told us it is shutting. Let go of the slot.
        ///
        /// Matched by reference rather than by index, because the portal reporting in may be one
        /// this pair has already replaced — a re-fire moves the existing aperture, but a restore
        /// or a world reload can put a different one in the slot while the old one is still
        /// finishing its own teardown.
        /// </summary>
        private void Forget(Portal portal)
        {
            for (int i = 0; i < portals.Length; i++)
                if (portals[i] == portal) portals[i] = null;
        }

        private void OnDestroy() => CloseAll();
    }
}
