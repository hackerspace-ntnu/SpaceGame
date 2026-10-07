// A tool drawn or stowed travels between its two places as a picture: it starts where the old instance was, follows the bones, and
// lands exactly where its socket seated it.
using System.Collections.Generic;
using NUnit.Framework;
using SpaceGame.Agents;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public class ToolTransitTests
    {
        private const float Seconds = 0.4f;

        private readonly List<GameObject> made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made)
                if (go != null) Object.DestroyImmediate(go);
            made.Clear();
        }

        private Transform Make(string name, Vector3 at, Transform parent = null)
        {
            var go = new GameObject(name);
            made.Add(go);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            return go.transform;
        }

        [Test]
        public void ProgressIsEasedFromZeroToOne_AndATransitOfNoTimeIsOver()
        {
            Assert.AreEqual(0f, ToolTransit.Progress(0f, Seconds), 1e-5f);
            Assert.AreEqual(0.5f, ToolTransit.Progress(Seconds * 0.5f, Seconds), 1e-5f);
            Assert.AreEqual(1f, ToolTransit.Progress(Seconds, Seconds), 1e-5f);
            Assert.AreEqual(1f, ToolTransit.Progress(Seconds * 3f, Seconds), 1e-5f, "past the end it stays arrived");
            Assert.Less(ToolTransit.Progress(Seconds * 0.1f, Seconds), 0.1f, "eased: it leaves slowly");
            Assert.AreEqual(1f, ToolTransit.Progress(0f, 0f), "no time: arrived");
        }

        [Test]
        public void TheToolTravelsFromWhereItWasToWhereItsSocketSeatedIt_AndLandsExactly()
        {
            Transform hand = Make("hand", new Vector3(2f, 1f, 0f));
            Transform hip = Make("hip", Vector3.zero);
            Transform tool = Make("tool", new Vector3(0f, 0.3f, 0.1f), hip);
            Quaternion seated = tool.localRotation = Quaternion.Euler(0f, 90f, 0f);
            Vector3 rest = tool.localPosition;

            var transit = new ToolTransit(tool, ToolTransit.Anchored.Of(hand, hand), Seconds);

            Assert.IsTrue(transit.Tick(Seconds * 0.5f));
            Assert.Greater(tool.position.x, 0.5f, "half way it is out toward the hand, not yet on the hip");
            Assert.Less(tool.position.x, 1.9f);

            Assert.IsFalse(transit.Tick(Seconds), "it has arrived");
            Assert.AreEqual(0f, Vector3.Distance(rest, tool.localPosition), 1e-5f, "exactly where the socket seated it");
            Assert.AreEqual(0f, Quaternion.Angle(seated, tool.localRotation), 1e-3f);
        }

        [Test]
        public void ThePictureFollowsTheBonesWhileItTravels()
        {
            Transform hand = Make("hand", new Vector3(2f, 0f, 0f));
            Transform hip = Make("hip", Vector3.zero);
            Transform tool = Make("tool", Vector3.zero, hip);
            var transit = new ToolTransit(tool, ToolTransit.Anchored.Of(hand, tool), Seconds);

            hand.position += Vector3.up * 5f;
            hip.position += Vector3.down * 5f;
            transit.Tick(Seconds * 0.5f);

            Assert.AreEqual(0f, tool.position.y, 0.5f,
                            "the start rides the hand that moved up and the end the hip that moved down: half way is where they average out");
        }

        [Test]
        public void ATransitWhoseToolIsGoneEnds()
        {
            Transform hand = Make("hand", Vector3.zero);
            Transform tool = Make("tool", Vector3.zero, Make("hip", Vector3.zero));
            var transit = new ToolTransit(tool, ToolTransit.Anchored.Of(hand, tool), Seconds);

            Object.DestroyImmediate(tool.gameObject);

            Assert.IsFalse(transit.Tick(0.01f), "the tool was unequipped again mid-way");
        }
    }
}
