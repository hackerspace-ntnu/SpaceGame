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

        /// <summary>Zero when slack, one when fully taut. Drives how the rope is drawn.</summary>
        public float Tension01 { get; private set; }

        /// <summary>
        /// Whether the rope is actually pulling on anything.
        ///
        /// <para>
        /// Gates the struggle: strain is what it costs to fight a rope, and a slack rope is not
        /// fighting you. Without this, walking away from a knot you are standing next to would tear
        /// the rope off after <c>resistSeconds</c> without it ever having gone taut.
        /// </para>
        /// </summary>
        public bool IsTaut => Tension01 > 0f;

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

            /// <summary>Seconds of squarely-away struggle to tear free of an equally strong end.</summary>
            public float resistSeconds;

            /// <summary>Strain given back per second when not struggling.</summary>
            public float strainDecay;

            /// <summary>
            /// What a rope may bend around. MUST be static geometry only — see
            /// <see cref="LeashWorldCast"/> for why a dynamic collider here desynchronises the
            /// rope's shape between machines.
            /// </summary>
            public LayerMask wrapLayers;

            /// <summary>Probe radius, in metres. Roughly the rope's own thickness.</summary>
            public float wrapRadius;

            /// <summary>How far off a surface a bend sits, in metres.</summary>
            public float wrapClearance;

            /// <summary>Ceiling on bends in one rope.</summary>
            public int maxWrapPoints;

            public LeashRope rope;
        }

        private Settings settings;

        /// <summary>
        /// The rope's shape. Derived here on every machine from the two replicated endpoints and
        /// never sent — see <see cref="LeashPath"/>.
        /// </summary>
        private readonly LeashPath path = new();

        private LeashWorldCast probe;

        private LeashPath.Tuning wrapTuning;

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

            leash.probe = new LeashWorldCast(settings.wrapLayers, settings.wrapRadius);
            leash.wrapTuning = new LeashPath.Tuning
            {
                clearance = settings.wrapClearance,
                maxWraps = Mathf.Max(0, settings.maxWrapPoints),
            };

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

            // Re-stated every step rather than once at tie time: an end can be REPLACED — the hand
            // end moving onto an object is the second half of every tie — and a probe still
            // excluding the previous one would let the rope wrap around what it is now tied to.
            // Null only if something built this component without going through Create, which is
            // the documented sole factory. Skipping the step degrades to the straight chord this
            // replaced rather than throwing every physics frame.
            if (probe != null)
            {
                probe.Ignoring(A.Anchor, B.Anchor);
                path.Step(A.Position, B.Position, probe.Cast, wrapTuning);
            }

            float stretch = MeasureStretch();

            UpdateTension(stretch);

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

            float stretch = MeasureStretch();
            if (stretch <= 0f) return;

            float share = ShareOf(self.Mass, other.CanMove ? other.Mass : Mathf.Infinity);

            // Each end pulls toward its own nearest BEND, not toward the far end. Pull toward the far
            // end and a rope wrapped ninety degrees round a pillar still drags its load through the
            // pillar — the wrap becomes decoration.
            Vector3 toward = path.DirectionFrom(self == A, A.Position, B.Position);
            Vector3 otherToward = path.DirectionFrom(other == A, A.Position, B.Position);

            // A kinematic end reports no velocity even while it walks, so nothing is arrested on
            // that side and the position term does all the holding. That converges to a small
            // STANDING overstretch rather than to zero — a creature walking away at 4 m/s settles
            // about half a metre past the rope's length and stays there. Which is the right answer
            // and looks like one: a leashed animal straining at the end of its rope. The tow term
            // below is what decides whether that standing overstretch grows or the creature is
            // hauled back — nothing snaps on stretch any more, by design.
            // Each end's own contribution to the rope getting longer. With a bend in it the two ends
            // no longer move along one shared axis, so a single relative-velocity term measures the
            // wrong thing. With no bend, otherToward is exactly -toward and this reduces to the
            // relative velocity it replaces — pinned by SeparationRate_WithNoWraps_MatchesRelativeVelocity.
            float separation = Vector3.Dot(self.Velocity, -toward)
                             + Vector3.Dot(other.Velocity, -otherToward);

            self.Pull(toward,
                      ArrestSpeed(separation, share, settings.maxCorrectionSpeed),
                      CorrectionDistance(stretch, share, settings.correction, settings.maxCorrectionStep),
                      TowCap(NetPullOn(self, other, toward), self.Mass));
        }

        /// <summary>
        /// How much spare pull the rest of the world has on <paramref name="self"/>, along this
        /// rope's own direction. Positive means this end is losing and is about to be towed.
        ///
        /// <para>
        /// Every OTHER rope on the same body is counted too, projected onto <paramref name="toward"/>
        /// so crews hauling the same way add and crews hauling opposite ways cancel. Without that,
        /// each rope resolves as though it were the only one — which is the same double-counting
        /// <see cref="ShareOf"/> exists to prevent within a single rope.
        /// </para>
        /// </summary>
        private float NetPullOn(LeashEnd self, LeashEnd other, Vector3 toward)
        {
            float netPull = other.PullStrength - self.PullStrength;

            if (self.Attachable == null || self.Attachable.Leashes.Count <= 1) return netPull;

            // Cleared and refilled rather than allocated: a fresh List per end per step is 50 Hz
            // of garbage. Held per ROPE rather than in one static, because Snap re-enters this
            // class inline on the host and a buffer shared across a re-entrant call is the
            // NetChannel re-entrancy trap wearing a different hat.
            contributions.Clear();

            foreach (Leash rope in self.Attachable.Leashes)
            {
                if (rope == null || rope == this) continue;

                LeashEnd mine = rope.A.Anchor == self.Anchor ? rope.A : rope.B;
                LeashEnd theirs = rope.Opposite(mine);
                if (!mine.IsAlive || !theirs.IsAlive) continue;

                Vector3 pullDirection = (theirs.Position - mine.Position).normalized;

                contributions.Add((theirs.PullStrength - mine.PullStrength)
                                  * Vector3.Dot(pullDirection, toward));
            }

            return netPull + CombinedPull(contributions);
        }

        /// <summary>Scratch for <see cref="NetPullOn"/>. See the note there on why it is not static.</summary>
        private readonly List<float> contributions = new();

        // ── Resist ─────────────────────────────────────────────────────────────

        private float strainA, strainB;

        /// <summary>How far through tearing free the given end is, 0 to 1.</summary>
        public float StrainOn(LeashEnd end) => end == A ? strainA : strainB;

        /// <summary>Record a struggle step. Owner-local: strain is never sent, only the snap is.</summary>
        public void SetStrainOn(LeashEnd end, float value)
        {
            if (end == A) strainA = value; else strainB = value;
        }

        /// <summary>
        /// The authored base figure. Named to avoid colliding with the static
        /// <see cref="ResistSeconds"/>, which scales this by the captor's pull — a property and a
        /// method may not share a name in one C# type.
        /// </summary>
        public float ResistBaseSeconds => settings.resistSeconds;

        /// <summary>Strain given back per second when the body stops struggling.</summary>
        public float StrainDecay => settings.strainDecay;

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

        /// <summary>
        /// How hard this end can HAUL — as distinct from how hard it is to move, which is mass.
        ///
        /// <para>
        /// Mass times top speed, because both come off the prefab: the two machines resolving the
        /// two ends derive the same number independently, which is what lets a contest be decided
        /// with nothing on the wire. Nothing in this game publishes a force — every mover is
        /// authored in velocity — so this is the closest honest stand-in for one.
        /// </para>
        /// <para>
        /// A static anchor scores ZERO rather than infinity: it resists everything and tows
        /// nothing. Returning early also keeps <c>Infinity * 0f</c> — a NaN that would poison
        /// every clamp downstream — from ever being evaluated.
        /// </para>
        /// </summary>
        public static float PullOf(float mass, float topSpeed)
        {
            if (float.IsInfinity(mass) || mass <= 0f) return 0f;
            if (topSpeed <= 0f) return 0f;

            return mass * topSpeed;
        }

        /// <summary>
        /// The fastest this end may be dragged, in m/s — force over mass, and the only place
        /// <see cref="PullOf"/> is consulted.
        ///
        /// <para>
        /// This replaces <c>LeashEnd.Restrain</c>, which capped every pull at the speed the body
        /// already had and so made it impossible for a rope to move anything that was standing
        /// still. The cap applies only to the end that is being OUT-PULLED: an end that is winning,
        /// or evenly matched, is not being towed and needs no ceiling. That exemption is what keeps
        /// two passive bodies — two crates, both scoring zero — closing normally rather than
        /// freezing at a cap of nothing.
        /// </para>
        /// </summary>
        public static float TowCap(float netPull, float mass)
        {
            if (netPull <= 0f) return Mathf.Infinity;
            if (float.IsInfinity(mass)) return 0f;

            return netPull / Mathf.Max(0.01f, mass);
        }

        /// <summary>
        /// The net pull on one body from every rope tied to it, signed along each rope's own
        /// direction. Several crews hauling the same hull add; two hauling it apart cancel.
        ///
        /// <para>
        /// Summing per BODY rather than resolving each rope alone is also a correctness fix: two
        /// ropes on one body each cancelling the full relative speed removes it twice, which is
        /// the same double-counting <c>share</c> exists to prevent within a single rope.
        /// </para>
        /// </summary>
        public static float CombinedPull(IReadOnlyList<float> signedPulls)
        {
            if (signedPulls == null) return 0f;

            float total = 0f;
            for (int i = 0; i < signedPulls.Count; i++) total += signedPulls[i];

            return total;
        }

        /// <summary>
        /// How long a struggle against <paramref name="theirPull"/> should take, in seconds.
        ///
        /// <para>
        /// A ratio rather than a difference, so it stays sane at both ends of the scale: tearing
        /// free of the lander is proportionally harder than tearing free of another player, and
        /// tearing free of a crate is quick without ever being instant.
        /// </para>
        /// </summary>
        public static float ResistSeconds(float theirPull, float myPull, float baseSeconds)
        {
            float mine = Mathf.Max(1f, myPull);
            float ratio = Mathf.Max(0.1f, theirPull / mine);

            return Mathf.Max(0.1f, baseSeconds * ratio);
        }

        /// <summary>
        /// How much of a body's attempt to leave the knot is actually being stopped by the rope,
        /// 0 (going where it wants) to 1 (going nowhere, or losing ground).
        ///
        /// <para>
        /// This is what tells a TOW from a STRUGGLE, and nothing else can: both are the same input,
        /// a player holding a movement key that points away from a taut rope. Before this the rope
        /// counted every haul as an escape attempt, and because a dropped item scores zero pull —
        /// no motor, so <see cref="LeashEnd.TopSpeed"/> is 0 and <see cref="PullOf"/> returns 0 —
        /// every item hit the ratio floor in <see cref="ResistSeconds"/> at 0.2 s. Hauling anything
        /// tore the rope off in a fifth of a second, before the load had moved.
        /// </para>
        /// <para>
        /// The difference is not in the input but in the result. A load that comes along with you
        /// is not restraining you, however hard you are pulling; a post that will not move is
        /// restraining you completely. So strain is charged for the part of the movement the rope
        /// actually cancelled, which is exactly the part a player experiences as being stuck.
        /// </para>
        /// </summary>
        /// <param name="wishAway">How squarely the body is trying to leave, `dot(wish, away)`, 0–1.</param>
        /// <param name="actualAway">Its real speed along that direction, m/s. Negative while losing ground.</param>
        /// <param name="topSpeed">What it would manage unimpeded. Zero disables the struggle.</param>
        public static float HeldBackFraction(float wishAway, float actualAway, float topSpeed)
        {
            // Not trying to leave, or nothing to leave with. The second guard is the divide: every
            // end without a motor answers TopSpeed 0, and 0/0 would poison every clamp downstream.
            if (wishAway <= 0f || topSpeed <= 0f) return 0f;

            float wanted = wishAway * topSpeed;

            return 1f - Mathf.Clamp01(actualAway / wanted);
        }

        /// <summary>
        /// One step of a struggle. <paramref name="away"/> is how squarely the body is pulling
        /// against the rope — the dot of its move input with the direction away from the knot,
        /// so leaning sideways earns nothing.
        ///
        /// <para>
        /// Pure, and takes its own delta, because <c>Time.time</c> starts at zero outside play
        /// mode and an EditMode test that read the clock would be measuring the Editor's frame.
        /// Clamped at both ends: never negative, so resting banks no credit against the next rope,
        /// and never past 1, so a long step cannot overshoot the snap.
        /// </para>
        /// </summary>
        public static float ResistStrain(float strain, float away, float resistSeconds,
                                         float dt, float decay)
        {
            float gain = Mathf.Max(0f, away) / Mathf.Max(0.1f, resistSeconds);

            strain += gain > 0f ? gain * dt : -decay * dt;

            return Mathf.Clamp01(strain);
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

        /// <summary>
        /// Metres of rope owed. Zero or less means slack.
        ///
        /// <para>
        /// Measured along the PATH, not between the knots. That one word is the winch: a rope bent
        /// round a corner measures longer for the same two endpoints, so walking away from the
        /// corner draws the far end in — with no winch anywhere in this system, and without a player
        /// being able to shorten their own free segment by moving.
        /// </para>
        /// </summary>
        private float MeasureStretch() => path.TotalLength(A.Position, B.Position) - Length;

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

        /// <summary>
        /// Metres of overstretch drawn as fully taut. A fixed figure now that nothing breaks on
        /// stretch: this only ever governed how the rope LOOKS, and it was scaling against a
        /// threshold that no longer exists.
        /// </summary>
        private const float FullyTautStretch = 1f;

        private void UpdateTension(float stretch)
        {
            float now = Mathf.Clamp01(Mathf.Max(0f, stretch) / FullyTautStretch);

            // The crack fires on the EDGE into tension, not while under it, or a rope held taut
            // would crack continuously.
            if (now > 0.15f && Tension01 <= 0.15f) settings.rope?.Bite();

            Tension01 = now;
        }

        // ── Render ─────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!A.IsAlive || !B.IsAlive || settings.rope == null) return;

            // Re-stated each frame rather than only when an end is tied: an end can be REPLACED —
            // the hand end moving onto an object is the whole second half of a tie — and a probe
            // still excluding the previous one would rest the rope on the thing it is now tied to.
            settings.rope.Draw(path.PointsBetween(A.Position, B.Position), Length, Tension01);
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
            // Whoever DECIDED announces it. That used to be the server, because the verdict was
            // stretch and every machine could measure it; resist is accumulated from movement
            // input, which only the struggling player's own machine has. Peers reach Snap from
            // the announcement and must not re-broadcast, which the disposed flag above ensures.
            if (listening != null && (!Network.IsNetworked || Network.Owns(listening)
                                      || Network.Server))
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
