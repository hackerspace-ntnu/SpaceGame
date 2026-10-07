// The ladder volume, without a world: which side a climber stands on and where the volume ends.
// LadderClimber takes hold of whatever Ladder.At returns, so a volume that reaches behind the rungs
// or above the top is a player grabbing a ladder from the wrong side or in mid-air.
using NUnit.Framework;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class LadderTests
    {
        private const float Top = 5f;
        private GameObject root;
        private Ladder ladder;

        // A ladder at the origin whose exit floor is 1 m toward -Z, so a climber stands on +Z.
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Ladder");
            var top = new GameObject("Top").transform;
            top.SetParent(root.transform, false);
            top.localPosition = new Vector3(0f, Top, 0f);
            var exit = new GameObject("Exit").transform;
            exit.SetParent(root.transform, false);
            exit.localPosition = new Vector3(0f, Top, -1f);

            ladder = root.AddComponent<Ladder>();
            ladder.Configure(top, exit);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(root);

        [Test]
        public void TheClimberStandsOnTheSideAwayFromTheExit()
        {
            Assert.AreEqual(Vector3.forward, ladder.TowardClimber);
        }

        [Test]
        public void FeetInFrontOfTheRungsAreAtTheLadder()
        {
            Assert.IsTrue(ladder.Contains(new Vector3(0f, 0f, 0.8f)));
            Assert.IsTrue(ladder.Contains(new Vector3(0.5f, 2f, 0.1f)));
        }

        [Test]
        public void FeetBehindTheRungsBesideThemOrAboveTheTopAreNot()
        {
            Assert.IsFalse(ladder.Contains(new Vector3(0f, 0f, -0.5f)), "behind, on the exit side");
            Assert.IsFalse(ladder.Contains(new Vector3(1f, 0f, 0.5f)), "beside");
            Assert.IsFalse(ladder.Contains(new Vector3(0f, Top + 0.1f, 0.5f)), "above the top");
            Assert.IsFalse(ladder.Contains(new Vector3(0f, 0f, 2f)), "out of reach");
        }

        [Test]
        public void TheTopBandReachesBackOverTheExitFloorAtTheStepOffHeight()
        {
            Assert.IsTrue(ladder.TopContains(new Vector3(0f, Top, -1f)), "standing on the exit");
            Assert.IsTrue(ladder.TopContains(new Vector3(0f, Top - 0.3f, 0.5f)), "dropping into the gap");
            Assert.IsFalse(ladder.TopContains(new Vector3(0f, Top - 1f, 0.5f)), "well below the top");
            Assert.IsFalse(ladder.TopContains(new Vector3(0f, Top, -2f)), "back across the floor");
        }

        [Test]
        public void AnUnwiredLadderHoldsNobody()
        {
            var bare = new GameObject("Bare").AddComponent<Ladder>();
            try
            {
                Assert.IsFalse(bare.Contains(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(bare.gameObject);
            }
        }
    }
}
