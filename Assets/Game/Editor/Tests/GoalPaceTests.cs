// Agents on their way somewhere walk at a player's pace, and humanoids put down steps to match.
//
// MoveIntent.GaitSpeed is the one place an absolute walking pace becomes a motor speed; the
// stride rate is what keeps a humanoid's feet on the ground once that pace is past its run clip.
using NUnit.Framework;
using SpaceGame.Agents;
using SpaceGame.Presentation;

namespace SpaceGame.EditorTools
{
    public class GoalPaceTests
    {
        private const float Tolerance = 1e-4f;

        // A Raxy: walk 2.4 m/s, run 4.32 m/s.
        private const float OwnWalk = 2.4f;
        private const float OwnRun = 4.32f;

        [Test]
        public void NoPace_KeepsTheMotorsOwnGaits()
        {
            Assert.AreEqual(OwnWalk, MoveIntent.MoveTo(default).GaitSpeed(OwnWalk, OwnRun), Tolerance);
            Assert.AreEqual(OwnRun, MoveIntent.MoveTo(default, isRunning: true).GaitSpeed(OwnWalk, OwnRun), Tolerance);
        }

        [Test]
        public void APace_IsTheWalk_AndTheRunKeepsTheMotorsRatio()
        {
            Assert.AreEqual(6f, MoveIntent.MoveTo(default, walkSpeed: 6f).GaitSpeed(OwnWalk, OwnRun), Tolerance);
            Assert.AreEqual(6f * OwnRun / OwnWalk,
                            MoveIntent.MoveTo(default, isRunning: true, walkSpeed: 6f).GaitSpeed(OwnWalk, OwnRun), Tolerance);
        }

        [Test]
        public void TheMultiplier_StillScalesAPace()
        {
            Assert.AreEqual(4.8f, MoveIntent.MoveTo(default, speedMultiplier: 0.8f, walkSpeed: 6f).GaitSpeed(OwnWalk, OwnRun), Tolerance);
        }

        [Test]
        public void StrideRate_IsOneUpToTheRunClip_AndProportionalPastIt()
        {
            Assert.AreEqual(1f, HumanoidParams.StrideRate(4f, 7.2f), Tolerance);
            Assert.AreEqual(1f, HumanoidParams.StrideRate(7.2f, 7.2f), Tolerance);
            Assert.AreEqual(10f / 7.2f, HumanoidParams.StrideRate(10f, 7.2f), Tolerance);
        }
    }
}
