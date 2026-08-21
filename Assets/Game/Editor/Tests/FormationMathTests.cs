// A caravan reads as a caravan or it reads as furniture, and the difference is entirely in these
// offsets. Asserting on them beats watching six agents cross a scene and forming an opinion.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public class FormationMathTests
    {
        private static FormationShape Shape(int lanes) => new FormationShape
        {
            Lanes = lanes,
            RowSpacing = 4f,
            LaneSpacing = 3f,
            LateralJitter = 0f,        // zeroed so the SHAPE is what is under test, not the variation
            LongitudinalJitter = 0f,
            DriftAmplitude = 0f,
            DriftRate = 0f,
        };

        [Test]
        public void SingleFilePutsEveryoneOnTheCentreLine()
        {
            FormationShape shape = Shape(1);

            for (int i = 0; i < 5; i++)
            {
                Vector2 offset = FormationMath.SlotOffset(i, in shape);

                // Exactly zero: the row stagger is suppressed for a single lane, because the one
                // formation a user explicitly asked to be a straight line should be one.
                Assert.AreEqual(0f, offset.x, 0.001f, $"follower {i} should stay on the centre line");
                Assert.AreEqual((i + 1) * 4f, offset.y, 0.001f, $"follower {i} should be one more row back");
            }
        }

        [Test]
        public void TwoLanesWalkAbreastAndThenStartANewRow()
        {
            FormationShape shape = Shape(2);

            Vector2 first = FormationMath.SlotOffset(0, in shape);
            Vector2 second = FormationMath.SlotOffset(1, in shape);
            Vector2 third = FormationMath.SlotOffset(2, in shape);

            Assert.AreEqual(first.y, second.y, 0.001f, "the first two share a row");
            Assert.AreEqual(3f, Mathf.Abs(first.x - second.x), 0.001f, "and are one lane apart");
            Assert.Greater(third.y, first.y, "the third starts a new row further back");
        }

        [Test]
        public void RowsAreStaggeredSoTheFormationIsNotAGrid()
        {
            // Without this the column is a rectangle of evenly spaced animals, which is the single
            // artefact that makes a travelling group read as scenery rather than as people.
            FormationShape shape = Shape(2);

            Vector2 row0 = FormationMath.SlotOffset(0, in shape);
            Vector2 row1 = FormationMath.SlotOffset(2, in shape);

            // Assert.That rather than AreNotEqual: the latter has no tolerance overload, so a delta
            // passed to it is silently taken as the message argument and the comparison becomes
            // exact — which for floats is a test that passes for the wrong reason.
            Assert.That(Mathf.Abs(row0.x - row1.x), Is.GreaterThan(0.01f),
                "consecutive rows must not share lane positions");
        }

        [Test]
        public void SlotsSitBehindTheLeaderAlongItsHeading()
        {
            FormationShape shape = Shape(2);
            Vector3 leader = new Vector3(100f, 0f, 100f);
            Vector3 heading = Vector3.forward;

            Vector3 slot = FormationMath.SlotPosition(0, leader, heading, in shape, memberSeed: 1, time: 0f);

            Assert.Less(slot.z, leader.z, "a follower belongs behind the leader, not in front of it");
            Assert.AreEqual(0f, slot.y, 0.001f, "formation is a horizontal arrangement");
        }

        [Test]
        public void SlotsRotateWithTheLeadersHeading()
        {
            FormationShape shape = Shape(1);
            Vector3 leader = Vector3.zero;

            Vector3 north = FormationMath.SlotPosition(0, leader, Vector3.forward, in shape, 1, 0f);
            Vector3 east = FormationMath.SlotPosition(0, leader, Vector3.right, in shape, 1, 0f);

            Assert.Less(north.z, -1f, "heading north puts the follower to the south");
            Assert.Less(east.x, -1f, "heading east puts the follower to the west");
        }

        [Test]
        public void PerMemberVariationIsFixedRatherThanReRolled()
        {
            // This is the property that makes the jitter read as individuals with a habitual place
            // instead of as twitching. Anything re-rolled per frame is noise, and noise reads as a
            // bug however small it is.
            var shape = new FormationShape
            {
                Lanes = 2, RowSpacing = 4f, LaneSpacing = 3f,
                LateralJitter = 1f, LongitudinalJitter = 1f,
                DriftAmplitude = 0f, DriftRate = 0f,
            };

            Vector3 a = FormationMath.SlotPosition(0, Vector3.zero, Vector3.forward, in shape, 4242, 0f);
            Vector3 b = FormationMath.SlotPosition(0, Vector3.zero, Vector3.forward, in shape, 4242, 0f);

            Assert.AreEqual(a, b, "the same member at the same time must get the same slot");

            Vector3 other = FormationMath.SlotPosition(0, Vector3.zero, Vector3.forward, in shape, 99, 0f);
            Assert.AreNotEqual(a, other, "different members must not land on identical slots");
        }

        [Test]
        public void StragglersSpeedUpAndLeadersEaseOff()
        {
            // The single most important number for whether a group holds together: without it a
            // member that loses ground never recovers it and the column stretches into stragglers.
            float behind = FormationMath.CatchUpSpeed(distanceToSlot: 6f, tolerance: 1.5f, gain: 0.12f,
                                                      minimum: 0.85f, maximum: 1.35f);
            float inPlace = FormationMath.CatchUpSpeed(0f, 1.5f, 0.12f, 0.85f, 1.35f);

            Assert.Greater(behind, 1f, "a member behind its slot must move faster than normal");
            Assert.Less(inPlace, 1f, "a member already there should ease off");
            Assert.LessOrEqual(behind, 1.35f, "and never exceed the clamp");
        }

        [Test]
        public void CatchUpSpeedIsClampedAtBothEnds()
        {
            Assert.AreEqual(1.35f, FormationMath.CatchUpSpeed(500f, 1.5f, 0.12f, 0.85f, 1.35f), 0.001f);
            Assert.AreEqual(0.85f, FormationMath.CatchUpSpeed(0f, 100f, 0.12f, 0.85f, 1.35f), 0.001f);
        }

        [Test]
        public void ADegenerateShapeDoesNotProduceNaN()
        {
            // Shapes come off a prefab where anybody can type a zero.
            var broken = new FormationShape { Lanes = 0, RowSpacing = 0f, LaneSpacing = 0f };

            Vector3 slot = FormationMath.SlotPosition(3, Vector3.zero, Vector3.zero, in broken, 7, 12f);

            Assert.IsFalse(float.IsNaN(slot.x) || float.IsNaN(slot.y) || float.IsNaN(slot.z),
                "a zeroed shape and a zero heading must still yield a real position");
        }
    }
}
