using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// One rope between two things.
    ///
    /// <para>
    /// A standalone scene object with no prefab and no NetworkObject. Every machine builds its own
    /// copy — <c>LeashArtifact.Present</c> runs everywhere — and every machine draws it. What is
    /// split is who RESOLVES it: each machine pulls only on the ends it owns, which for a player is
    /// their own machine and for everything else is the server. See <see cref="LeashEnd.ResolvedHere"/>.
    /// </para>
    /// <para>
    /// The constraint is a distance limit, not a spring: below <see cref="Length"/> the rope does
    /// nothing whatever. Past it, it acts in two deliberately separate parts —
    /// <see cref="ArrestSpeed"/> takes velocity off, <see cref="CorrectionDistance"/> gives position
    /// back — because a position error repaid as velocity does not converge. See the note above
    /// those two methods.
    /// </para>
    /// </summary>
    public class Leash : MonoBehaviour
    {
        // ── Ends ───────────────────────────────────────────────────────────────

        public LeashEnd A { get; } = new();
        public LeashEnd B { get; } = new();

        /// <summary>How much rope there is. Fixed once tied — see <see cref="PayOutTo"/>.</summary>
        public float Length { get; private set; } = 8f;

        /// <summary>
        /// Whether either end is still in somebody's hand.
        ///
        /// <para>
        /// Asked of the KIND, not of <see cref="LeashEnd.IsPlayer"/>, which is a broader question —
        /// it is also true of a player somebody else has roped, and such a rope is tied, not held.
        /// </para>
        /// </summary>
        public bool IsHeld =>
            A.Kind == LeashEndKind.PlayerHand || B.Kind == LeashEndKind.PlayerHand;

        /// <summary>Zero when slack, one at the stretch that breaks it. Drives how the rope is drawn.</summary>
        public float Tension01 { get; private set; }

        // ── Settings ───────────────────────────────────────────────────────────

        /// <summary>Everything about a rope that is a decision rather than a measurement.</summary>
        public struct Settings
        {
            public float length;

            /// <summary>Fraction of the remaining overstretch each end gives back per physics step.</summary>
            public float correction;

            /// <summary>Ceiling on the velocity one step of the constraint may take off an end, in m/s.</summary>
            public float maxCorrectionSpeed;

            /// <summary>Ceiling on how far one step may move an end, in metres.</summary>
            public float maxCorrectionStep;

            /// <summary>Metres past its length the rope tolerates before breaking. Zero never breaks.</summary>
            public float breakStretch;

            /// <summary>How long that overstretch must last. A momentary spike is not a break.</summary>
            public float breakTime;

            public LeashRope rope;
        }

        private Settings settings;

        // ── Live registry ──────────────────────────────────────────────────────

        private static readonly List<Leash> LiveLeashes = new();

        /// <summary>
        /// Every rope currently in the session.
        ///
        /// <para>
        /// A leash is not spawned from a prefab and is not parented to anything, so nothing else in
        /// the game can find one by any ordinary means. This list is how the saver enumerates ropes
        /// and how <see cref="LeashedBody"/> finds the ones tied to it. Static because a rope belongs
        /// to the session rather than to either of its ends.
        /// </para>
        /// </summary>
        public static IReadOnlyList<Leash> All => LiveLeashes;

        private void OnEnable()
        {
            if (!LiveLeashes.Contains(this)) LiveLeashes.Add(this);
        }

        private void OnDisable()
        {
            LiveLeashes.Remove(this);

            if (listening != null) listening.NetOff(NetMsg.LeashSnap, OnSnapAnnounced);
            listening = null;
        }

        // ── Breaking, across the network ───────────────────────────────────────
        //
        // A rope has no NetworkObject and no relay of its own, so a snap rides the channel of one
        // of its own ANCHORS — whichever of the two has a networked identity.

        /// <summary>How near the announced point a rope must pass to be the one that broke.</summary>
        private const float SnapTolerance = 1f;

        /// <summary>The anchor whose channel this rope's snap travels on, or null for a local rope.</summary>
        private Transform Channel =>
            NetArg.IdOf(A.Anchor != null ? A.Anchor.gameObject : null) != 0 ? A.Anchor
          : NetArg.IdOf(B.Anchor != null ? B.Anchor.gameObject : null) != 0 ? B.Anchor
          : null;

        private Transform listening;

        /// <summary>
        /// Keep the snap handler on an anchor that can carry it.
        ///
        /// Re-checked each step rather than registered once, because an end can be REPLACED — the
        /// hand end moving onto an object is the whole second half of a tie — and because an
        /// anchor's NetworkObject may spawn after the rope was built.
        /// </summary>
        private void RefreshChannel()
        {
            Transform wanted = Channel;
            if (wanted == listening) return;

            if (listening != null) listening.NetOff(NetMsg.LeashSnap, OnSnapAnnounced);

            listening = wanted;
            if (listening != null) listening.NetOn(NetMsg.LeashSnap, OnSnapAnnounced);
        }

        /// <summary>
        /// A rope broke somewhere else. Addressed the way an untie is, so every machine picks the
        /// same one. Idempotent: Dispose is, and a machine that no longer has the rope finds none.
        /// </summary>
        private void OnSnapAnnounced(in NetArg arg, ulong sender)
        {
            GameObject anchorObject = arg.Resolve();
            Transform anchor = anchorObject != null ? anchorObject.transform : null;

            Nearest(anchor, arg.P, SnapTolerance)?.Snap();
        }

        // ── Construction ───────────────────────────────────────────────────────

        /// <summary>
        /// Build an untied rope with its renderer already set up.
        ///
        /// <para>
        /// One factory rather than two copies of the same setup: the artifact builds ropes when a
        /// player clicks and the save system builds them when a world loads, and a rope that came
        /// back from a save must be indistinguishable from one that did not.
        /// </para>
        /// </summary>
        public static Leash Create(in Settings settings)
        {
            var go = new GameObject("Leash");
            var leash = go.AddComponent<Leash>();

            leash.settings = settings;
            leash.settings.rope = new LeashRope();
            leash.settings.rope.CopyFrom(settings.rope);
            leash.settings.rope.Build(go);
            leash.Length = Mathf.Max(0.5f, settings.length);

            return leash;
        }

        private void Awake()
        {
            // A rope outlives the chunk either of its ends was streamed in from. Without this,
            // unloading the scene that held a crate takes the rope tied to it as well.
            DontDestroyOnLoad(gameObject);
        }

        // ── Tying ──────────────────────────────────────────────────────────────

        /// <summary><paramref name="localOffset"/> is the knot in the target's own space. See LeashEnd.TieTo.</summary>
        public void TieEndTo(bool isA, GameObject targetRoot, Vector3 localOffset)
        {
            (isA ? A : B).TieTo(targetRoot, localOffset, this);
        }

        public void TieEndToHand(bool isA, GameObject playerRoot, Transform muzzle)
        {
            (isA ? A : B).TieToHand(playerRoot, muzzle, this);
        }

        public void PinEndTo(bool isA, Vector3 worldPoint) => (isA ? A : B).PinTo(worldPoint, this);

        /// <summary>
        /// Move whichever end is in a hand onto a world object — the second click of a tie.
        /// </summary>
        /// <param name="paidOutLength">
        /// The rope's new length, decided once by the clicking machine and sent. Zero leaves the
        /// length alone. It is deliberately not measured here: each machine runs this at its own
        /// moment, a relay apart, so measuring gave a rope tied across anything moving a different
        /// length on every machine — permanently, since the length is fixed once tied.
        /// </param>
        public void TieHandEndOnto(GameObject targetRoot, Vector3 localOffset, float paidOutLength)
        {
            LeashEnd hand = A.IsPlayer ? A : B.IsPlayer ? B : null;
            if (hand == null) return;

            hand.TieTo(targetRoot, localOffset, this);
            FinishTie(paidOutLength);
        }

        /// <summary>Drive the hand end into bare geometry — a wall, the ground — rather than an object.</summary>
        public void PinHandEndAt(Vector3 worldPoint, float paidOutLength)
        {
            LeashEnd hand = A.IsPlayer ? A : B.IsPlayer ? B : null;
            if (hand == null) return;

            hand.PinTo(worldPoint, this);
            FinishTie(paidOutLength);
        }

        private void FinishTie(float paidOutLength)
        {
            // Only ever longer. The caller has already applied its own margin and ceiling — see
            // LeashArtifact.OnRequestUse — so all that is left here is to refuse a shrink, which is
            // what stops a second tie quietly shortening a rope somebody paid out.
            if (paidOutLength > Length) Length = paidOutLength;

            settings.rope?.Bite();
        }

        /// <summary>Restore-only. Called by the save system; never from gameplay.</summary>
        public void RestoreEnd(bool isA, GameObject root, Vector3 localOffset, Vector3 worldPoint, bool held)
        {
            LeashEnd end = isA ? A : B;

            if (root == null) end.PinTo(worldPoint, this);
            else end.RestoreOnto(root, localOffset, held, this);
        }

        // ── Picking ────────────────────────────────────────────────────────────

        /// <summary>
        /// The rope a ray is pointing at, nearest first, or null.
        ///
        /// <para>
        /// Analytic rather than a collider: see <see cref="LeashRope.Aimed"/>. Only ropes drawn on
        /// this machine can be picked, which is every rope this machine knows about.
        /// </para>
        /// </summary>
        public static Leash Aimed(Ray ray, float maxDistance, float radius,
                                  out Vector3 point, out float alongRay)
        {
            point = Vector3.zero;
            alongRay = float.MaxValue;

            Leash best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < LiveLeashes.Count; i++)
            {
                Leash rope = LiveLeashes[i];
                if (rope == null || rope.settings.rope == null) continue;
                if (!rope.A.IsAlive || !rope.B.IsAlive) continue;

                if (!rope.settings.rope.Aimed(ray, maxDistance, radius, out float distance, out Vector3 on))
                    continue;

                if (distance >= nearest) continue;

                nearest = distance;
                alongRay = distance;
                best = rope;
                point = on;
            }

            return best;
        }

        /// <summary>
        /// The rope tied to <paramref name="anchor"/> passing closest to <paramref name="point"/>,
        /// which is given in that anchor's own local space.
        ///
        /// <para>
        /// This is how an untie — and a snap — reaches every machine. A rope has no NetworkObject
        /// and therefore no id to send, but it does have a SHAPE derived from two replicated
        /// endpoints, so a point on it identifies the same rope everywhere.
        /// </para>
        /// <para>
        /// Anchored rather than bare-world, because a bare world point names nothing once the thing
        /// it was clicked on starts moving: the click travels for a relay, and a rope on an animal
        /// running at 8 m/s has left the tolerance by the time a peer looks — so the rope came off
        /// on the clicking machine alone while the server went on constraining a creature nobody
        /// else could see a rope on. Resolved against the anchor, the point rides the animal.
        /// </para>
        /// <para>
        /// A null anchor searches every rope by world point, which is the right answer for an end
        /// pinned to bare geometry: that point is identical on every machine by definition.
        /// </para>
        /// <para>
        /// Two ropes tied between the same pair of objects lie on top of each other and are
        /// genuinely ambiguous here. They are also indistinguishable on screen, so whichever is
        /// picked looks identical; the only cost is that two machines could drop different ones.
        /// </para>
        /// </summary>
        public static Leash Nearest(Transform anchor, Vector3 point, float tolerance)
        {
            Vector3 world = anchor != null ? anchor.TransformPoint(point) : point;

            Leash best = null;
            float nearest = tolerance;

            for (int i = 0; i < LiveLeashes.Count; i++)
            {
                Leash rope = LiveLeashes[i];
                if (rope == null || rope.settings.rope == null) continue;

                // Narrowed to ropes that actually touch this anchor, which removes most of the
                // ambiguity the bare-world search had as a side effect of being correct.
                if (anchor != null && !rope.Touches(anchor)) continue;

                float distance = rope.settings.rope.DistanceTo(world);
                if (distance > nearest) continue;

                nearest = distance;
                best = rope;
            }

            return best;
        }

        /// <summary>Whether either end of this rope is tied to <paramref name="anchor"/>.</summary>
        public bool Touches(Transform anchor) =>
            anchor != null && (A.Anchor == anchor || B.Anchor == anchor);

        public bool ReferencesObject(GameObject go) =>
            go != null && ((A.Anchor != null && A.Anchor.gameObject == go) ||
                           (B.Anchor != null && B.Anchor.gameObject == go));

        // ── Constraint ─────────────────────────────────────────────────────────

        private float overstretchedSince = -1f;

        /// <summary>
        /// Whether both ends have been tied at least once.
        ///
        /// <para>
        /// A rope is built untied and then has its ends attached one at a time, so for an instant it
        /// legitimately has a dead end. Without this a physics step landing between the two calls
        /// would read that as "the thing I was tied to is gone" and dispose a rope that was merely
        /// half-built.
        /// </para>
        /// </summary>
        private bool everTied;

        private void FixedUpdate()
        {
            if (!everTied)
            {
                if (!A.IsAlive || !B.IsAlive) return;
                everTied = true;
            }

            if (!A.IsAlive || !B.IsAlive)
            {
                // The thing this was tied to is gone. A rope tied to nothing is not a rope.
                Dispose();
                return;
            }

            RefreshChannel();

            float stretch = MeasureStretch(out _);

            UpdateTension(stretch);

            // The break verdict is the SERVER's, or this machine's when there is nothing to
            // disagree with. Every machine can compute the stretch, but they compute it from
            // interpolated endpoints and can land on opposite sides of the threshold — and that
            // disagreement is permanent, because the machine that kept the rope goes on
            // constraining a creature nobody else can see a rope on, and which nobody can untie
            // because it is not drawn for them.
            //
            // A rope with no networked anchor is local to every machine anyway (see Channel), so
            // each is entitled to break its own copy.
            bool mineToDecide = !Network.IsNetworked || Network.Server || listening == null;

            if (mineToDecide && HasBroken(stretch)) return;

            // Player ends are deliberately skipped here. They are resolved by LeashedBody instead,
            // which runs after PlayerMovement — a pull applied before it is overwritten by the move
            // solve within the same step, which is exactly how the rope this replaces managed never
            // to move a player at all.
            if (!A.IsPlayer) ResolveEnd(A, B);
            if (!B.IsPlayer) ResolveEnd(B, A);
        }

        /// <summary>
        /// Apply what one end owes, if this machine is the one allowed to move it.
        ///
        /// <para>
        /// Public so <see cref="LeashedBody"/> can drive the player ends from its own, later,
        /// FixedUpdate without a second copy of the arithmetic.
        /// </para>
        /// </summary>
        public void ResolveEnd(LeashEnd self, LeashEnd other)
        {
            if (!self.ResolvedHere || !self.CanMove) return;
            if (!A.IsAlive || !B.IsAlive) return;

            float stretch = MeasureStretch(out Vector3 aToB);
            if (stretch <= 0f) return;

            float share = ShareOf(self.Mass, other.CanMove ? other.Mass : Mathf.Infinity);

            Vector3 toward = self == A ? aToB : -aToB;

            // A kinematic end reports no velocity even while it walks, so nothing is arrested on
            // that side and the position term does all the holding. That converges to a small
            // STANDING overstretch rather than to zero — a creature walking away at 4 m/s settles
            // about half a metre past the rope's length and stays there. Which is the right answer
            // and looks like one: a leashed animal straining at the end of its rope. It only ever
            // reaches breakStretch if the thing on the end is genuinely faster than the rope.
            float separation = Vector3.Dot(self.Velocity - other.Velocity, -toward);

            self.Pull(toward,
                      ArrestSpeed(separation, share, settings.maxCorrectionSpeed),
                      CorrectionDistance(stretch, share, settings.correction, settings.maxCorrectionStep));
        }

        /// <summary>
        /// How much of the constraint's work this end does, by inverse mass.
        ///
        /// <para>
        /// The lighter end moves further, which is both what physics says and what a player expects
        /// when they drag a crate and are barely slowed by a barrel. An end that cannot move at all
        /// is infinitely heavy and the other end does everything.
        /// </para>
        /// </summary>
        public static float ShareOf(float selfMass, float otherMass)
        {
            float wSelf = float.IsInfinity(selfMass) ? 0f : 1f / Mathf.Max(0.01f, selfMass);
            float wOther = float.IsInfinity(otherMass) ? 0f : 1f / Mathf.Max(0.01f, otherMass);

            float total = wSelf + wOther;
            return total > 0f ? wSelf / total : 0f;
        }

        // The constraint is deliberately in TWO parts, and keeping them apart is what makes it
        // stable. A position error corrected by adding velocity does not converge: the velocity it
        // adds is still there on the next step, so the ends keep accelerating toward each other,
        // sail through the correct distance and collide. That is the classic way to put energy into
        // a solver, and a single combined term cannot avoid it.
        //
        // So velocity is only ever REMOVED (ArrestSpeed), and the position error is given back as a
        // position (CorrectionDistance), which carries no momentum into the next step. Both are pure
        // and static so the thing this most needs to be — convergent — is shown by
        // LeashConstraintTests rather than judged by feel in play mode.

        /// <summary>
        /// The outward speed to take off this end: only ever the removal of motion, never the
        /// addition of it.
        ///
        /// <para>
        /// This is the half that makes a rope feel like a rope rather than a rubber band — a player
        /// walking into the end of one is halted, not gently discouraged. Scaled by
        /// <paramref name="share"/> because the figure passed in is the RELATIVE speed of the two
        /// ends: both of them cancelling all of it removes it twice over, and a pair that each
        /// over-corrects toward the other is a rope that hums.
        /// </para>
        /// </summary>
        public static float ArrestSpeed(float separation, float share, float ceiling) =>
            separation <= 0f || share <= 0f ? 0f : Mathf.Min(separation * share, ceiling);

        /// <summary>
        /// How far to move this end back toward the other — a fraction of what it owes, not all of it.
        ///
        /// <para>
        /// A fraction because resolving the whole error in one step is a hard snap: a visible jolt,
        /// and a fight with the interpolator. Successive steps take the same fraction of what is
        /// left, so the error decays geometrically and the rope closes in a fraction of a second
        /// without ever overshooting.
        /// </para>
        /// <para>
        /// <paramref name="maxStep"/> is the safety: an end that is suddenly hundreds of metres away
        /// has been teleported, streamed in, or carried off by a vehicle, and chasing that error at
        /// full rate is how the old rope slingshotted things across the map.
        /// </para>
        /// </summary>
        public static float CorrectionDistance(float stretch, float share, float correction, float maxStep) =>
            stretch <= 0f || share <= 0f ? 0f : Mathf.Min(stretch * share * correction, maxStep);

        /// <summary>Metres of rope owed, and the direction from A to B. Zero or less means slack.</summary>
        private float MeasureStretch(out Vector3 aToB)
        {
            Vector3 delta = B.Position - A.Position;
            float distance = delta.magnitude;
            aToB = distance > 0.0001f ? delta / distance : Vector3.forward;

            return distance - Length;
        }

        /// <summary>The player end tied to this body, or null. Used by <see cref="LeashedBody"/>.</summary>
        public LeashEnd PlayerEndOn(Rigidbody body)
        {
            if (body == null) return null;
            if (A.IsPlayer && A.Body == body) return A;
            if (B.IsPlayer && B.Body == body) return B;
            return null;
        }

        /// <summary>The end opposite <paramref name="end"/>.</summary>
        public LeashEnd Opposite(LeashEnd end) => end == A ? B : A;

        private void UpdateTension(float stretch)
        {
            float ceiling = settings.breakStretch > 0.01f ? settings.breakStretch : 1f;
            float now = Mathf.Clamp01(Mathf.Max(0f, stretch) / ceiling);

            // The crack fires on the EDGE into tension, not while under it, or a rope held taut
            // would crack continuously.
            if (now > 0.15f && Tension01 <= 0.15f) settings.rope?.Bite();

            Tension01 = now;
        }

        /// <summary>
        /// Has this rope been pulled past what it can take, for long enough to count?
        ///
        /// <para>
        /// Stretch rather than force, because stretch is the one quantity every machine agrees on —
        /// both ends' positions are replicated — so every machine reaches the same verdict with no
        /// message to send. Force cannot do that: it depends on masses and velocities that differ
        /// per machine, which is why the force test this replaces would have needed syncing and
        /// never had it. It is also the version a player can see coming.
        /// </para>
        /// </summary>
        private bool HasBroken(float stretch)
        {
            if (settings.breakStretch <= 0.01f) return false;

            if (stretch <= settings.breakStretch)
            {
                overstretchedSince = -1f;
                return false;
            }

            if (overstretchedSince < 0f) overstretchedSince = Time.time;
            if (Time.time - overstretchedSince < settings.breakTime) return false;

            Snap();
            return true;
        }

        // ── Render ─────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!A.IsAlive || !B.IsAlive || settings.rope == null) return;

            // Re-stated each frame rather than only when an end is tied: an end can be REPLACED —
            // the hand end moving onto an object is the whole second half of a tie — and a probe
            // still excluding the previous one would rest the rope on the thing it is now tied to.
            settings.rope.TiedBetween(A.Anchor, B.Anchor);
            settings.rope.Draw(A.Position, B.Position, Length, Tension01);
        }

        // ── Lifecycle ──────────────────────────────────────────────────────────

        /// <summary>The rope has been pulled apart. Sounds like it, then goes.</summary>
        public void Snap()
        {
            if (disposed) return;

            // Claimed BEFORE the send, and that ordering is load-bearing rather than tidy.
            // NetTo.All dispatches INLINE on the host — the broadcast runs inside this very call —
            // and the handler it reaches resolves this same rope by its point and calls Snap on it
            // again. Setting the flag after the send is unbounded recursion; setting it here makes
            // the re-entrant call a no-op, which is what every handler on this layer has to be.
            disposed = true;

            // Announced while the anchors are still here to address it with, and only by the
            // machine that DECIDED. Everyone else reaches Snap FROM the announcement, and a
            // re-broadcast from there would be a second loop.
            if (listening != null && (!Network.IsNetworked || Network.Server))
            {
                Transform anchor = listening;

                NetMessaging.NetSendTo(anchor.gameObject, NetMsg.LeashSnap,
                                       new NetArg { P = anchor.InverseTransformPoint(A.Position) }
                                           .With(anchor.gameObject),
                                       NetTo.All);
            }

            Sfx.Play(SfxId.ImpactMetal, A.Position);
            Teardown();
        }

        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            Teardown();
        }

        /// <summary>
        /// Let both ends go and remove the rope. Shared by <see cref="Dispose"/> and
        /// <see cref="Snap"/> so the flag that guards them can be claimed before either does
        /// anything that can re-enter.
        /// </summary>
        private void Teardown()
        {
            A.Release(this);
            B.Release(this);

            if (this != null) Remove(gameObject);
        }

        /// <summary>
        /// Destroy something, from either mode.
        ///
        /// <para>
        /// A rope is a bare <c>new GameObject</c> with no prefab behind it, so it is built and torn
        /// down by editor tooling and EditMode tests as readily as by play. <c>Destroy</c> is
        /// refused outside play mode — Unity logs an error and the object survives — which left
        /// ropes behind in the editor and failed any test that disposed one.
        /// </para>
        /// </summary>
        internal static void Remove(GameObject target)
        {
            if (target == null) return;

            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private void OnDestroy()
        {
            // Defensive: something else destroyed us — a scene unload, an editor stop — without
            // going through Dispose. Make sure no attachable is left holding a dead reference.
            if (disposed) return;
            disposed = true;

            A.Release(this);
            B.Release(this);
        }
    }
}
