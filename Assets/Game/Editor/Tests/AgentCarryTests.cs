// What it takes to hold a creature off its own NavMesh, checked without a NavMesh.
//
// Two properties decide whether a hoist works, and neither can be seen from the outside once it is
// wrong. The first is that a flat drag is not mistaken for a lift: the knot on an animal's back is
// below the knot in a player's hand, so every rope in the game pulls slightly upward and a naive
// test puts leashed animals in the air for being walked. The second is CONVERGENCE, and it is the
// same property LeashConstraintTests pins for the rope itself, arriving from the other side: a
// carried body integrating its own accumulated velocity is repaid the rope's correction as
// position AND keeps the velocity that correction was meant to cancel, so it sinks through the
// rope instead of hanging from it.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp
// types, and an asmdef cannot reference Assembly-CSharp.
using NUnit.Framework;
using SpaceGame.Agents;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class AgentCarryTests
    {
        private const float Dt = 0.02f;                       // a 50 Hz physics step
        private static readonly Vector3 Gravity = new Vector3(0f, -18f, 0f);   // this world's
        private const float TerminalSpeed = 30f;
        private const float EnterSlope = 0.7f;                // the motor's default

        // ── Telling a lift from a drag ────────────────────────────────────────

        [Test]
        public void IsLift_FlatDragOnAThreeMetreRope_IsNotALift()
        {
            // A player's hand is about a metre above a rat's back. Over three metres of rope that
            // is the entire vertical component of a perfectly ordinary walk.
            Vector3 ask = new Vector3(2.8f, 0.9f, 0f);

            Assert.IsFalse(AgentCarry.IsLift(ask, EnterSlope));
        }

        [Test]
        public void IsLift_FlatDragOnAShortRope_IsStillNotALift()
        {
            // The worst case for the threshold: stood right over the animal, rope barely longer
            // than the height difference between the two knots.
            Vector3 ask = new Vector3(1.2f, 0.9f, 0f);

            Assert.IsFalse(AgentCarry.IsLift(ask, EnterSlope));
        }

        [Test]
        public void IsLift_JetpackClimb_IsALift()
        {
            Vector3 ask = new Vector3(0.4f, 2.5f, 0.2f);

            Assert.IsTrue(AgentCarry.IsLift(ask, EnterSlope));
        }

        [Test]
        public void IsLift_PullingDownwards_IsNeverALift()
        {
            Assert.IsFalse(AgentCarry.IsLift(new Vector3(0f, -3f, 0f), EnterSlope));
            Assert.IsFalse(AgentCarry.IsLift(Vector3.zero, EnterSlope));
        }

        // ── The fall ──────────────────────────────────────────────────────────

        [Test]
        public void Fall_AddsGravityAndKeepsHorizontalMomentum()
        {
            Vector3 fallen = AgentCarry.Fall(new Vector3(4f, 0f, 0f), Gravity, Dt, TerminalSpeed);

            Assert.AreEqual(4f, fallen.x, 1e-4f);
            Assert.AreEqual(-0.36f, fallen.y, 1e-4f);
        }

        [Test]
        public void Fall_IsCappedAtTerminalSpeed()
        {
            Vector3 fallen = AgentCarry.Fall(new Vector3(0f, -TerminalSpeed, 0f),
                                             Gravity, Dt, TerminalSpeed);

            Assert.AreEqual(-TerminalSpeed, fallen.y, 1e-4f);
        }

        // ── Landing ───────────────────────────────────────────────────────────

        [Test]
        public void HasLanded_ToleranceGrowsWithTheFall()
        {
            // 30 m/s crosses 0.6 m in one step. A body that high above the mesh has to land on
            // this step or it is underground on the next.
            Assert.IsTrue(AgentCarry.HasLanded(10.5f, 10f, -TerminalSpeed, Dt, 0.15f));

            // The same gap, barely moving, is a body still hanging in the air.
            Assert.IsFalse(AgentCarry.HasLanded(10.5f, 10f, -0.1f, Dt, 0.15f));
        }

        [Test]
        public void HasLanded_BelowTheGround_HasArrived()
        {
            Assert.IsTrue(AgentCarry.HasLanded(9.4f, 10f, -TerminalSpeed, Dt, 0.15f));
        }

        // ── Convergence ───────────────────────────────────────────────────────

        /// <summary>
        /// One second and a half of a creature hanging off a rope whose far end does not move,
        /// stepped exactly as the motor steps it: the fall runs first (the motor's execution order
        /// is -100), the rope's position-only correction second.
        /// </summary>
        /// <param name="measured">
        /// True steps the way <c>NavMeshAgentMotor.FixedUpdate</c> does — this body's velocity is
        /// read back out of how far it actually moved over the whole of the last step, the fall
        /// included. False is the tempting alternative, an accumulated velocity field, which never
        /// sees the correction at all.
        /// </param>
        private static (float gap, float swing) Hang(bool measured, int steps = 600)
        {
            const float ropeY = 10f;      // the pilot's knot, held still
            const float length = 3f;
            const float share = 0.667f;   // a 40 kg rat against an 80 kg player
            const float correction = 0.35f;
            const float maxStep = 0.5f;

            float y = ropeY - length;
            float lastY = y;
            float velocity = 0f;
            float low = float.MaxValue;
            float high = float.MinValue;

            for (int i = 0; i < steps; i++)
            {
                // The mark is taken BEFORE the fall, exactly as the motor takes it, so the reading
                // covers the whole of the last step — the fall it ran itself as well as the rope's
                // correction. See Drop below for what marking it afterwards costs.
                if (measured)
                {
                    velocity = (y - lastY) / Dt;
                    lastY = y;
                }

                velocity = AgentCarry.Fall(new Vector3(0f, velocity, 0f),
                                           Gravity, Dt, TerminalSpeed).y;
                y += velocity * Dt;

                float stretch = ropeY - y - length;
                if (stretch > 0f) y += Mathf.Min(stretch * share * correction, maxStep);

                if (i < steps - 100) continue;
                low = Mathf.Min(low, y);
                high = Mathf.Max(high, y);
            }

            return (ropeY - low, high - low);
        }

        [Test]
        public void Hang_MeasuringTheVelocityBack_SettlesJustBelowTheRope()
        {
            (float gap, float swing) = Hang(measured: true);

            // A couple of centimetres of sag and nothing moving: the vertical form of the standing
            // overstretch a leashed animal already shows horizontally.
            Assert.Less(gap, 3.1f);
            Assert.Greater(gap, 3f);
            Assert.Less(swing, 0.01f);
        }

        [Test]
        public void Hang_AccumulatingTheVelocityInstead_SinksThroughTheRope()
        {
            // The control. Kept because the failure it describes is silent: the creature is still
            // on the end of an intact rope, it is simply metres below where the rope can reach.
            (float gap, _) = Hang(measured: false, steps: 300);

            Assert.Greater(gap, 10f);
        }

        // ── The fall, once nothing is holding the body up ──────────────────────

        /// <summary>
        /// Two seconds of a released creature with no rope on it, stepped the way the motor steps
        /// it. Returns the speed it has reached.
        /// </summary>
        /// <param name="markBeforeTheFall">
        /// True is the motor: the position the next step measures against is taken BEFORE the fall
        /// moves the body, so the reading is this body's real velocity. False is where the mark
        /// used to be — after the move — which leaves the fall out of its own measurement.
        /// </param>
        private static float Drop(bool markBeforeTheFall, int steps = 100)
        {
            float y = 100f;
            float lastY = y;
            float velocity;

            for (int i = 0; i < steps; i++)
            {
                velocity = (y - lastY) / Dt;
                if (markBeforeTheFall) lastY = y;

                velocity = AgentCarry.Fall(new Vector3(0f, velocity, 0f),
                                           Gravity, Dt, TerminalSpeed).y;
                y += velocity * Dt;

                if (!markBeforeTheFall) lastY = y;
            }

            return (y - lastY) / Dt;
        }

        [Test]
        public void Drop_ReachesTerminalSpeed()
        {
            // 18 m/s² for two seconds is well past terminal, which is the figure HasLanded sizes
            // its tolerance from — so if this cannot be reached, neither can that.
            Assert.AreEqual(-TerminalSpeed, Drop(markBeforeTheFall: true), 0.5f);
        }

        [Test]
        public void Drop_MarkingAfterTheFall_NeverAcceleratesAtAll()
        {
            // The control, and the bug it describes: a body whose own motion is left out of its
            // own measurement is handed gravity from rest every step. It never exceeds one step of
            // it, and a creature let go at altitude sinks at walking pace with nothing holding it.
            Assert.AreEqual(-0.36f, Drop(markBeforeTheFall: false), 0.01f);
        }

        // ── A motor strapped to the creature ──────────────────────────────────

        [Test]
        public void ThrustDragSpeed_BuildsUpAndThenStopsAtTheCeiling()
        {
            const float acceleration = 40f;
            const float ceiling = 20f;

            float speed = AgentCarry.ThrustDragSpeed(0f, acceleration, Dt, ceiling);
            Assert.AreEqual(0.8f, speed, 1e-4f);

            for (int i = 0; i < 200; i++)
                speed = AgentCarry.ThrustDragSpeed(speed, acceleration, Dt, ceiling);

            Assert.AreEqual(ceiling, speed, 1e-4f);
        }

        /// <summary>
        /// A booster clamped to a creature's underside, stepped as the motor steps it: one step of
        /// thrust is a·dt², and the measurement carries it forward as momentum.
        /// </summary>
        private static (float peak, float speedAtBurnout) Boost(float acceleration, int burnSteps)
        {
            float y = 0f;
            float lastY = y;
            float peak = 0f;
            float velocity = 0f;

            for (int i = 0; i < burnSteps * 8; i++)
            {
                velocity = (y - lastY) / Dt;
                lastY = y;

                velocity = AgentCarry.Fall(new Vector3(0f, velocity, 0f),
                                           Gravity, Dt, TerminalSpeed).y;
                y += velocity * Dt;

                if (i < burnSteps) y += acceleration * Dt * Dt;

                peak = Mathf.Max(peak, y);
                if (i == burnSteps - 1) velocity = (y - lastY) / Dt;
            }

            return (peak, velocity);
        }

        [Test]
        public void Boost_TwoSecondsOfThrust_LaunchesTheCreatureLikeAPlayer()
        {
            // The booster's own tuning: 40 m/s² against this world's 18 leaves 22 of climb, which
            // is what puts a boosted PLAYER near 95 m at about 44 m/s off the ground. A creature
            // is not a special case (GDC-L1-SYS-0002) and arrives at the same numbers.
            (float peak, float speedAtBurnout) = Boost(40f, 100);

            Assert.Greater(speedAtBurnout, 40f);
            Assert.Less(speedAtBurnout, 48f);
            Assert.Greater(peak, 80f);
            Assert.Less(peak, 110f);
        }

        [Test]
        public void Boost_OneStepOfThrust_MovesTheBodyCentimetres()
        {
            // The bug this arithmetic replaced, stated as a number. The item used to hand the
            // motor a point 60 m out along the thrust and the carry moved the body onto it, every
            // physics step: 3 km/s, out of the chunk before the flame was drawn, and it read as
            // the creature vanishing the moment a booster touched it.
            float step = 40f * Dt * Dt;

            Assert.Less(step, 0.02f);
        }
    }
}
