// The shape of a group on the move, as arithmetic.
//
// Separated from the module for the same reason MountLookMath and RiderPoseMath are: the
// interesting part is a handful of offsets and a catch-up curve, and those are worth being able to
// assert on directly rather than by watching six agents walk across a scene and forming an opinion.
//
// The whole design problem in one sentence: a column that is exactly a column reads as a conga
// line, and a group with no column at all reads as a crowd that happens to be going the same way.
// What follows is a grid with three separate things wrong with it on purpose — staggered rows, a
// fixed per-member offset, and a slow drift — none of which are random per frame, because anything
// re-rolled per frame is jitter and jitter reads as a bug.
using UnityEngine;

namespace SpaceGame.Agents
{
    /// <summary>How a formation is laid out. All distances in metres.</summary>
    [System.Serializable]
    public struct FormationShape
    {
        [Tooltip("How many may walk abreast. 1 is strict single file, 2 gives you a line that is " +
                 "mostly a line, 3+ is a loose travelling mob.")]
        public int Lanes;

        [Tooltip("Distance between rows, front to back.")]
        public float RowSpacing;

        [Tooltip("Distance between members walking abreast.")]
        public float LaneSpacing;

        [Tooltip("Fixed sideways offset each member keeps, in metres either way. This is the one " +
                 "that stops the formation looking printed — it never changes for a given member, " +
                 "so they read as individuals with a habitual place rather than as noise.")]
        public float LateralJitter;

        [Tooltip("Fixed front-to-back offset each member keeps. Keep below RowSpacing or rows " +
                 "start swapping places.")]
        public float LongitudinalJitter;

        [Tooltip("How far a member slowly sways from its slot as it walks.")]
        public float DriftAmplitude;

        [Tooltip("How fast that sway cycles. Well below stride frequency — anything near it reads " +
                 "as a limp.")]
        public float DriftRate;

        /// <summary>A mounted caravan: two abreast, generous spacing, plenty of slop.</summary>
        public static FormationShape Caravan => new FormationShape
        {
            Lanes = 2,
            RowSpacing = 4.5f,
            LaneSpacing = 3f,
            LateralJitter = 0.9f,
            LongitudinalJitter = 1.2f,
            DriftAmplitude = 0.7f,
            DriftRate = 0.08f,
        };

        /// <summary>A squad moving with intent: single file, tight, disciplined.</summary>
        public static FormationShape Column => new FormationShape
        {
            Lanes = 1,
            RowSpacing = 3f,
            LaneSpacing = 2f,
            LateralJitter = 0.5f,
            LongitudinalJitter = 0.5f,
            DriftAmplitude = 0.3f,
            DriftRate = 0.1f,
        };

        public FormationShape Sanitised() => new FormationShape
        {
            Lanes = Mathf.Max(1, Lanes),
            RowSpacing = Mathf.Max(0.5f, RowSpacing),
            LaneSpacing = Mathf.Max(0.5f, LaneSpacing),
            LateralJitter = Mathf.Max(0f, LateralJitter),
            LongitudinalJitter = Mathf.Max(0f, LongitudinalJitter),
            DriftAmplitude = Mathf.Max(0f, DriftAmplitude),
            DriftRate = Mathf.Max(0f, DriftRate),
        };
    }

    public static class FormationMath
    {
        /// <summary>
        /// Where follower <paramref name="followerIndex"/> belongs relative to the leader, before
        /// any per-member variation. X is metres to the leader's right, Y is metres behind.
        ///
        /// <para>
        /// Odd rows are nudged sideways by a third of a lane. Without it the formation is a
        /// rectangular grid, and a rectangular grid of six animals crossing a desert looks like
        /// furniture — the stagger is what makes the same six read as a group that arranged itself.
        /// </para>
        /// </summary>
        public static Vector2 SlotOffset(int followerIndex, in FormationShape shape)
        {
            FormationShape s = shape.Sanitised();

            int index = Mathf.Max(0, followerIndex);
            int row = index / s.Lanes;
            int lane = index % s.Lanes;

            float laneCentre = (s.Lanes - 1) * 0.5f;
            float lateral = (lane - laneCentre) * s.LaneSpacing;

            // Only where there is more than one lane to stagger against. Applying it to a single
            // file would push the one formation that is meant to be a straight line off its line,
            // which is exactly what someone choosing Lanes = 1 asked not to happen.
            if (s.Lanes > 1 && (row & 1) == 1)
                lateral += s.LaneSpacing * 0.33f;

            float behind = (row + 1) * s.RowSpacing;

            return new Vector2(lateral, behind);
        }

        /// <summary>
        /// The world position follower <paramref name="followerIndex"/> should be heading for.
        ///
        /// <paramref name="memberSeed"/> must be stable for the lifetime of the member — it is what
        /// makes that member's personal offset personal. Passing something that changes (a frame
        /// count, a fresh random) turns the whole formation into noise.
        /// </summary>
        public static Vector3 SlotPosition(int followerIndex, Vector3 leaderPosition, Vector3 heading,
                                           in FormationShape shape, int memberSeed, float time)
        {
            FormationShape s = shape.Sanitised();

            Vector3 forward = heading;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;

            Vector3 right = Vector3.Cross(Vector3.up, forward);

            Vector2 slot = SlotOffset(followerIndex, in s);

            // Fixed per member, so it is a habit rather than a twitch.
            float lateralBias = Signed01(memberSeed, 17) * s.LateralJitter;
            float longitudinalBias = Signed01(memberSeed, 31) * s.LongitudinalJitter;

            // Perlin rather than sine: two members on nearby phases would visibly beat against each
            // other on a sine, which is the one artefact that reads as machinery.
            float phase = Hash01(memberSeed, 53) * 100f;
            float driftLateral = (Mathf.PerlinNoise(time * s.DriftRate + phase, 0.5f) - 0.5f) * 2f * s.DriftAmplitude;
            float driftLong = (Mathf.PerlinNoise(0.5f, time * s.DriftRate + phase) - 0.5f) * 2f * s.DriftAmplitude;

            float lateral = slot.x + lateralBias + driftLateral;
            float behind = Mathf.Max(0f, slot.y + longitudinalBias + driftLong);

            return leaderPosition - forward * behind + right * lateral;
        }

        /// <summary>
        /// How fast a follower should move given how far it has fallen behind its slot.
        ///
        /// <para>
        /// This is the single most important number for whether a group reads as a group. The
        /// alternative — everyone at the same speed — means a member that loses ground to a rock or
        /// a fight never recovers it, and the formation slowly stretches into a queue and then into
        /// stragglers. Speeding up when behind and easing off when ahead keeps the shape without
        /// the leader ever having to wait.
        /// </para>
        /// </summary>
        public static float CatchUpSpeed(float distanceToSlot, float tolerance, float gain,
                                         float minimum, float maximum)
        {
            float excess = distanceToSlot - Mathf.Max(0f, tolerance);
            return Mathf.Clamp(1f + excess * gain, minimum, maximum);
        }

        /// <summary>Deterministic 0..1 from two integers. No allocation, no UnityEngine.Random state.</summary>
        public static float Hash01(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13;
                h *= 0x85EBCA6B;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Deterministic -1..1.</summary>
        public static float Signed01(int seed, int salt) => Hash01(seed, salt) * 2f - 1f;
    }
}
