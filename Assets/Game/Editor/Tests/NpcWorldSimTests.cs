// The virtual layer has one job — carry a group across kilometres it never renders — and one
// failure that would be invisible until a player walked into it: a record that drifts, stalls, or
// forgets what it was doing across a save.
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class NpcWorldSimTests
    {
        [SetUp]
        public void Setup() => WorldSiteRegistry.Clear();

        [TearDown]
        public void TearDown() => WorldSiteRegistry.Clear();

        [Test]
        public void AGroupCoversTheDistanceItsSpeedImplies()
        {
            // 2 km at 3.5 m/s is a little over nine and a half minutes of walking, which is the
            // scale the design is actually asking for. Ticked at 1 Hz, the same rate the simulator
            // runs at.
            var group = new NpcGroup
            {
                Position = Vector3.zero,
                GoalPosition = new Vector3(2000f, 0f, 0f),
                ArriveRadius = 8f,
                HasGoal = true,
            };

            const float speed = 3.5f;
            int ticks = 0;
            bool arrived = false;

            while (ticks < 2000 && !arrived)
            {
                arrived = group.AdvanceToward(speed, 1f);
                ticks++;
            }

            Assert.IsTrue(arrived, "the group should reach a goal 2 km away");

            float expected = 2000f / speed;
            Assert.AreEqual(expected, ticks, expected * 0.05f,
                "arrival time should follow from the speed, within a tick or two");
        }

        [Test]
        public void AFastGroupCannotStrideStraightPastASmallSite()
        {
            // The trap: with a step larger than the remaining distance and an arrive radius smaller
            // than the step, a naive check overshoots, turns round, overshoots again, and the group
            // orbits its destination forever without ever arriving.
            var group = new NpcGroup
            {
                Position = Vector3.zero,
                GoalPosition = new Vector3(3f, 0f, 0f),
                ArriveRadius = 1f,
                HasGoal = true,
            };

            Assert.IsTrue(group.AdvanceToward(speed: 50f, delta: 1f),
                "a step longer than the remaining distance must count as arrival");
            Assert.AreEqual(group.GoalPosition, group.Position);
        }

        [Test]
        public void HeightIsIgnoredWhileTravelling()
        {
            // Goals come from site markers, which sit on whatever geometry they were dropped on —
            // routinely tens of metres above or below the ground the group walks over. Chasing the
            // vertical component would make a group crawl toward a clifftop it is directly beneath.
            var group = new NpcGroup
            {
                Position = Vector3.zero,
                GoalPosition = new Vector3(100f, 250f, 0f),
                ArriveRadius = 5f,
                HasGoal = true,
            };

            group.AdvanceToward(10f, 1f);

            Assert.AreEqual(0f, group.Position.y, 0.001f, "travel is horizontal");
            Assert.AreEqual(10f, group.Position.x, 0.01f, "the full step goes into horizontal distance");
        }

        [Test]
        public void AGroupWithNoGoalStaysPut()
        {
            var group = new NpcGroup { Position = new Vector3(5f, 0f, 5f), HasGoal = false };

            Assert.IsFalse(group.AdvanceToward(10f, 1f));
            Assert.AreEqual(new Vector3(5f, 0f, 5f), group.Position);
        }

        [Test]
        public void ARecordSurvivesTheRoundTripThroughASave()
        {
            var group = new NpcGroup
            {
                Id = "salt-caravan",
                TemplateId = "salt-caravan",
                Position = new Vector3(1200f, 30f, -400f),
                GoalPosition = new Vector3(2400f, 12f, 900f),
                HasGoal = true,
                ArriveRadius = 14f,
                TaskIndex = 2,
                DwellRemaining = 33f,
                LastSiteId = "abc123",
                Lead = new Vector3(10f, 0f, 20f),
                HasLead = true,
                LeadAge = 44f,
            };

            NpcGroup.Record record = group.ToRecord();

            var restored = new NpcGroup { Id = record.id, TemplateId = record.templateId };
            restored.ApplyRecord(in record);

            Assert.AreEqual(group.Position, restored.Position);
            Assert.AreEqual(group.GoalPosition, restored.GoalPosition);
            Assert.AreEqual(group.HasGoal, restored.HasGoal);
            Assert.AreEqual(group.ArriveRadius, restored.ArriveRadius, 0.001f);
            Assert.AreEqual(group.TaskIndex, restored.TaskIndex, "the group must resume the job it was on");
            Assert.AreEqual(group.DwellRemaining, restored.DwellRemaining, 0.001f);
            Assert.AreEqual(group.LastSiteId, restored.LastSiteId);
            Assert.AreEqual(group.Lead, restored.Lead);
            Assert.IsTrue(restored.HasLead, "a bounty hunter squad must not forget your trail on load");
            Assert.AreEqual(group.LeadAge, restored.LeadAge, 0.001f);
        }

        [Test]
        public void ARecordFromBeforeArriveRadiusExistedGetsAUsableDefault()
        {
            // Old saves are read into a struct whose missing fields become zero, and a zero arrive
            // radius combined with a small step is the orbit-forever bug above.
            var record = new NpcGroup.Record { id = "old", templateId = "old", arriveRadius = 0f };

            var group = new NpcGroup();
            group.ApplyRecord(in record);

            Assert.Greater(group.ArriveRadius, 0f, "a zero radius from an old save must be replaced");
        }
    }
}
