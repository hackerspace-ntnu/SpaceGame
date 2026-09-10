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
        /// read back out of how far it actually moved. False is the tempting alternative, an
        /// accumulated velocity field, which never sees the correction at all.
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
                if (measured) velocity = (y - lastY) / Dt;

                velocity = AgentCarry.Fall(new Vector3(0f, velocity, 0f),
                                           Gravity, Dt, TerminalSpeed).y;
                y += velocity * Dt;
                lastY = y;

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
    }
}
