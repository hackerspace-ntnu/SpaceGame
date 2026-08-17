// The mounted camera's "leave it where I put it" arithmetic.
//
// The behaviour these pin is a feel decision, not an implementation detail: a rider who swings the
// camera out to watch the ostrich run from the side keeps that view for three seconds and then gets
// it back over about eleven more. Both halves matter — a hold that leaks is a camera that wanders
// off on its own, and a return that races is the camera overruling the player.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class MountLookMathTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void WrapAngle_FoldsPastHalfTurnToTheNegativeSide()
        {
            Assert.AreEqual(-10f, MountLookMath.WrapAngle(350f), Tolerance);
        }

        [Test]
        public void WrapAngle_FoldsBelowNegativeHalfTurnToThePositiveSide()
        {
            Assert.AreEqual(170f, MountLookMath.WrapAngle(-190f), Tolerance);
        }

        [Test]
        public void WrapAngle_KeepsHalfTurnPositive()
        {
            // (-180, 180], so exactly a half turn stays at +180 rather than flipping sign every
            // frame the camera sits on it.
            Assert.AreEqual(180f, MountLookMath.WrapAngle(180f), Tolerance);
        }

        [Test]
        public void WrapAngle_LeavesAnglesAlreadyInRangeAlone()
        {
            Assert.AreEqual(90f, MountLookMath.WrapAngle(90f), Tolerance);
            Assert.AreEqual(-90f, MountLookMath.WrapAngle(-90f), Tolerance);
            Assert.AreEqual(0f, MountLookMath.WrapAngle(0f), Tolerance);
        }

        [Test]
        public void WrapAngle_FoldsMultipleTurns()
        {
            // The offset accumulates without bound, so a rider who spins the camera round and round
            // genuinely does reach these.
            Assert.AreEqual(-10f, MountLookMath.WrapAngle(710f), Tolerance);
        }

        [Test]
        public void StepRecentre_HoldsTheViewBeforeTheDelayElapses()
        {
            float held = MountLookMath.StepRecentre(90f, timeSinceInput: 2.9f, delay: 3f,
                                                    speed: 8f, deltaTime: 0.016f);
            Assert.AreEqual(90f, held, Tolerance);
        }

        [Test]
        public void StepRecentre_MovesAtExactlySpeedTimesDeltaOnceTheDelayPasses()
        {
            float stepped = MountLookMath.StepRecentre(90f, timeSinceInput: 3.1f, delay: 3f,
                                                       speed: 8f, deltaTime: 0.5f);
            Assert.AreEqual(90f - 4f, stepped, Tolerance);
        }

        [Test]
        public void StepRecentre_DoesNotOvershootZero()
        {
            // A frame spike must not throw the camera past centre and out the other side.
            float stepped = MountLookMath.StepRecentre(2f, timeSinceInput: 10f, delay: 3f,
                                                       speed: 8f, deltaTime: 5f);
            Assert.AreEqual(0f, stepped, Tolerance);
        }

        [Test]
        public void StepRecentre_ReturnsTheShortWayFromAWideOffset()
        {
            // 350° is 10° to the left, not 350° to the right. Recentring the long way round is the
            // thing WrapAngle exists to prevent, so it has to hold through StepRecentre too.
            float stepped = MountLookMath.StepRecentre(350f, timeSinceInput: 10f, delay: 3f,
                                                       speed: 8f, deltaTime: 0.5f);
            Assert.AreEqual(-6f, stepped, Tolerance, "should have moved from -10 towards 0, not from 350");
        }

        [Test]
        public void StepRecentre_WrapsEvenWhileHolding()
        {
            float held = MountLookMath.StepRecentre(350f, timeSinceInput: 0f, delay: 3f,
                                                    speed: 8f, deltaTime: 0.016f);
            Assert.AreEqual(-10f, held, Tolerance);
        }

        [Test]
        public void StepRecentre_WithZeroSpeedNeverReturns()
        {
            // Someone setting the speed to 0 in the inspector means "never recentre"; it must not
            // be read as "recentre instantly".
            float held = MountLookMath.StepRecentre(90f, timeSinceInput: 999f, delay: 3f,
                                                    speed: 0f, deltaTime: 0.5f);
            Assert.AreEqual(90f, held, Tolerance);
        }

        [Test]
        public void StepRecentre_ReturnsFromBothSides()
        {
            float fromLeft = MountLookMath.StepRecentre(-90f, timeSinceInput: 10f, delay: 3f,
                                                        speed: 8f, deltaTime: 0.5f);
            Assert.AreEqual(-86f, fromLeft, Tolerance);
        }

        [Test]
        public void StepRecentre_TakesTheAdvertisedTimeToComeHomeFromASideView()
        {
            // The number quoted in the design: 90° at 8°/s is about eleven seconds of drift. Walked
            // one 60 fps frame at a time so the per-frame path is what is measured, not the formula.
            float offset = 90f;
            float deltaTime = 1f / 60f;
            int frames = 0;

            while (Mathf.Abs(offset) > Tolerance && frames < 10000)
            {
                offset = MountLookMath.StepRecentre(offset, timeSinceInput: 10f, delay: 3f,
                                                    speed: 8f, deltaTime: deltaTime);
                frames++;
            }

            float seconds = frames * deltaTime;
            Assert.That(seconds, Is.EqualTo(11.25f).Within(0.1f));
        }
    }
}
