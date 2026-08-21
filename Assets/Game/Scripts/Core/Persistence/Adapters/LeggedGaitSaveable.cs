using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Locomotion;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how a legged machine was standing: where each foot was, where the gait clock had got
    /// to, and how high the body was riding.
    ///
    /// <b>Cosmetic, and worth saying so plainly.</b> Nothing here changes where a machine is, what it
    /// can reach, or whether it can walk — the pose is already restored and the legs re-find the
    /// ground within a stride either way. What it buys is the absence of one visible stumble per
    /// legged creature per load: without it every foot snaps to its default stance under its hip, the
    /// gait clock restarts at zero, and <c>heightPrimed</c> comes back false so the body settles onto
    /// the ground from wherever the smoothing happens to start. Six legs' worth of that at once, on
    /// every crawler and ostrich and horse in view, is what a load currently looks like.
    ///
    /// <b>The phase is restored verbatim.</b> The gait is a clock advanced by DISTANCE TRAVELLED, and
    /// each leg owns a fixed slice of it — so a phase that is anything other than exactly what it was
    /// puts a foot in the wrong slice. It is assigned, never blended toward and never filtered: the
    /// standing rule for this locomotion is that nothing at stride frequency may go through a filter,
    /// because a filter costs amplitude and phase at the frequency it passes.
    ///
    /// <b>The one restore that is not just numbers.</b> <c>LeggedLocomotion.Start</c> calls
    /// <c>SnapToGround</c>, which drops the machine and calls <c>GroundFeet</c> — resetting every foot
    /// to its rest position. It is skipped once a restore has spoken, which is the only behavioural
    /// change on the locomotion side and is the difference between this saver working and this saver
    /// being silently undone on one of the two hydration orders.
    ///
    /// Not deferred: the footholds are world positions captured in the same frame as the body pose the
    /// store has already put back, so the two agree the moment the record lands. Waiting would mean
    /// the machine visibly settling first and then being corrected.
    /// </summary>
    public class LeggedGaitSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "gait";

        private LeggedLocomotion locomotion;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private LeggedLocomotion Locomotion =>
            locomotion != null ? locomotion : locomotion = GetComponent<LeggedLocomotion>();

        public string SaveKey => Key;

        /// <summary>
        /// The wire shape, mirroring <see cref="LeggedLocomotion.LegSnapshot"/>.
        ///
        /// A separate type on purpose: the locomotion lives in its own assembly, which must not
        /// acquire a dependency on Newtonsoft to be persisted, and a saver must never serialize a
        /// gameplay type directly — the wire shape has to be free to stay still while the runtime one
        /// changes.
        /// </summary>
        public struct LegRecord
        {
            public Vector3 foot;
            public Vector3 groundNormal;
            public Vector3 swingFrom;
            public Vector3 swingTo;
            public float phaseOffset;
            public bool swinging;
            public float swingT;
            public float swingLift;
            public float swingSpan;
            public bool wasInSlice;
            public bool grounded;
            public float stanceTime;
            public float load;
        }

        public struct State
        {
            public float phase;
            public Vector3 pathPos;
            public float yaw;
            public float smoothedHeight;
            public bool heightPrimed;
            public float fallVelocity;
            public bool falling;
            public LegRecord[] legs;
        }

        public object CaptureState()
        {
            if (Locomotion == null) return null;

            LeggedLocomotion.Snapshot? captured = Locomotion.CaptureLocomotion();

            // Null means the rig never came up — no leg chains were found — and there is genuinely
            // nothing to keep about how a machine with no legs was standing.
            if (!captured.HasValue) return null;

            LeggedLocomotion.Snapshot snapshot = captured.Value;

            var state = new State
            {
                phase = snapshot.Phase,
                pathPos = snapshot.PathPos,
                yaw = snapshot.Yaw,
                smoothedHeight = snapshot.SmoothedHeight,
                heightPrimed = snapshot.HeightPrimed,
                fallVelocity = snapshot.FallVelocity,
                falling = snapshot.Falling,
                legs = new LegRecord[snapshot.Legs.Length],
            };

            for (int i = 0; i < snapshot.Legs.Length; i++)
            {
                LeggedLocomotion.LegSnapshot leg = snapshot.Legs[i];
                state.legs[i] = new LegRecord
                {
                    foot = leg.Foot,
                    groundNormal = leg.GroundNormal,
                    swingFrom = leg.SwingFrom,
                    swingTo = leg.SwingTo,
                    phaseOffset = leg.PhaseOffset,
                    swinging = leg.Swinging,
                    swingT = leg.SwingT,
                    swingLift = leg.SwingLift,
                    swingSpan = leg.SwingSpan,
                    wasInSlice = leg.WasInSlice,
                    grounded = leg.Grounded,
                    stanceTime = leg.StanceTime,
                    load = leg.Load,
                };
            }

            return state;
        }

        public void RestoreState(JObject state)
        {
            if (Locomotion == null) return;

            // No record means the machine had not been walked yet — nothing to put back, and the
            // machine's own Start is the right thing to leave in charge. Deliberately NOT forced to a
            // synthetic zero stance: unlike a cooldown, "no gait recorded" has a correct default that
            // the locomotion already computes for itself from the ground it is standing on, and
            // asserting one from here would be a worse answer than the one it reaches.
            if (state == null) return;

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            var snapshot = new LeggedLocomotion.Snapshot
            {
                Phase = restored.phase,
                PathPos = restored.pathPos,
                Yaw = restored.yaw,
                SmoothedHeight = restored.smoothedHeight,
                HeightPrimed = restored.heightPrimed,
                FallVelocity = restored.fallVelocity,
                Falling = restored.falling,
                Legs = ToLegs(restored.legs),
            };

            Locomotion.RestoreLocomotion(in snapshot);
        }

        private static LeggedLocomotion.LegSnapshot[] ToLegs(LegRecord[] records)
        {
            if (records == null) return System.Array.Empty<LeggedLocomotion.LegSnapshot>();

            var legs = new LeggedLocomotion.LegSnapshot[records.Length];

            for (int i = 0; i < records.Length; i++)
            {
                LegRecord r = records[i];
                legs[i] = new LeggedLocomotion.LegSnapshot
                {
                    Foot = r.foot,
                    GroundNormal = r.groundNormal,
                    SwingFrom = r.swingFrom,
                    SwingTo = r.swingTo,
                    PhaseOffset = r.phaseOffset,
                    Swinging = r.swinging,
                    SwingT = r.swingT,
                    SwingLift = r.swingLift,
                    SwingSpan = r.swingSpan,
                    WasInSlice = r.wasInSlice,
                    Grounded = r.grounded,
                    StanceTime = r.stanceTime,
                    Load = r.load,
                };
            }

            return legs;
        }
    }
}
