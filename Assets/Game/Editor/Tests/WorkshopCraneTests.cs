using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The workshop's hoist under a driver's hand: one lever shares the hook and the lift, the levers ride a station's
    /// one-float request whole, driving stops at the ends of the runs, and a driven hoist comes back where it was left.
    /// Far from everything, so nothing stands in its way.
    /// </summary>
    public class WorkshopCraneTests
    {
        private static readonly Vector3 FarAway = new(9000f, 9000f, 9000f);

        private static readonly WorkshopCraneReach Reach = new()
        {
            bridge = new Vector2(-5.5f, 19.5f), trolley = new Vector2(-1.7f, 19.6f), hookDrop = 12.1f, liftClimb = 4.5f,
        };

        private GameObject host;
        private WorkshopSaveable saveable;
        private WorkshopCrane crane;

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Workshop under test");
            host.transform.position = FarAway;
            host.AddComponent<Workshop>();
            saveable = host.AddComponent<WorkshopSaveable>();

            var hoist = new GameObject("Crane");
            hoist.transform.SetParent(host.transform, false);
            crane = hoist.AddComponent<WorkshopCrane>();
            crane.Author(Reach, Vector3.right, Vector3.forward, Vector3.zero, new Bounds(Vector3.up, Vector3.one), null, null,
                         null, null, null);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(host);

        [Test]
        public void OneLeverWindsTheHookInBeforeItLiftsTheGantry()
        {
            Assert.AreEqual(3f, WorkshopCraneReach.HookDropOf(-3f));
            Assert.AreEqual(0f, WorkshopCraneReach.LiftOf(-3f), "a hook let down hangs from a gantry at the bottom");
            Assert.AreEqual(0f, WorkshopCraneReach.HookDropOf(2f), "a gantry up its towers carries a hook wound right in");
            Assert.AreEqual(2f, WorkshopCraneReach.LiftOf(2f));
            Assert.AreEqual(-3f, WorkshopCraneReach.HeightOf(3f, 0f));
            Assert.AreEqual(2f, WorkshopCraneReach.HeightOf(0f, 2f));
        }

        [Test]
        public void EveryLeverPositionCrossesTheWireWhole()
        {
            for (int b = -1; b <= 1; b++)
                for (int t = -1; t <= 1; t++)
                    for (int h = -1; h <= 1; h++)
                    {
                        var sent = new WorkshopCraneCommand(b, t, h);
                        Assert.AreEqual(sent, WorkshopCraneCommand.Unpack(sent.Pack()));
                    }

            Assert.IsTrue(WorkshopCraneCommand.Unpack(99f).IsIdle, "anything that is not a command leaves the hoist still");
        }

        [Test]
        public void DrivingStopsAtTheEndsOfTheRuns()
        {
            crane.Restore(true, new WorkshopCranePose(0f, 0f, 0f), false);
            for (int frame = 0; frame < 2000; frame++)
                crane.Drive(new WorkshopCraneCommand(1, -1, 1), 1f / 60f);

            Assert.AreEqual(Reach.bridge.y, crane.Pose.bridge, 1e-3f);
            Assert.AreEqual(Reach.trolley.x, crane.Pose.trolley, 1e-3f);
            Assert.AreEqual(Reach.liftClimb, crane.Pose.height, 1e-3f);
        }

        [Test]
        public void AHoistNobodyHasDrivenIsNotDrivenAndNotWritten()
        {
            Assert.IsFalse(crane.IsDriven);
            crane.Drive(new WorkshopCraneCommand(1, 0, 0), 1f);
            Assert.AreEqual(0f, crane.Pose.bridge, "it runs its round until somebody takes the console");
            Assert.IsNull(saveable.CaptureState(), "nothing to say: the clock has the hoist");
        }

        [Test]
        public void ADrivenHoistComesBackWhereItWasLeft()
        {
            crane.Restore(true, new WorkshopCranePose(7.25f, 3.5f, -6f), false);
            JObject saved = JObject.FromObject(saveable.CaptureState(), SaveSerializer.Serializer);

            saveable.RestoreState(null);
            Assert.IsFalse(crane.IsDriven, "no save is the round");

            saveable.RestoreState(saved);

            Assert.IsTrue(crane.IsDriven);
            Assert.AreEqual(new WorkshopCranePose(7.25f, 3.5f, -6f), crane.Pose);
        }
    }
}
