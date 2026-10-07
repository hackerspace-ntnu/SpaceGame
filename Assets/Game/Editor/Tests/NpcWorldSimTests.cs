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
        public void AGroupThatStops_KeepsFacingTheWayItWalked()
        {
            // The live spawn and the distant silhouette both face Heading: a city that reaches its stop
            // must not swing round to face north.
            var group = new NpcGroup
            {
                Position = Vector3.zero,
                GoalPosition = new Vector3(40f, 0f, 0f),
                ArriveRadius = 2f,
                HasGoal = true,
            };

            while (!group.AdvanceToward(10f, 1f)) { }
            group.HasGoal = false;

            Assert.Less(Vector3.Distance(Vector3.right, group.Heading), 1e-4f, $"heading {group.Heading}");
        }

        [Test]
        public void AGroupThatNeverHadAGoal_FacesPlusZ() =>
            Assert.AreEqual(Vector3.forward, new NpcGroup { Position = new Vector3(5f, 0f, 5f) }.Heading);

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

        // ── A group is not folded under a member in flight ─────────────────────────

        private sealed class TestAirborneCarrier : MonoBehaviour, IAirborneCarrier { }

        private const System.Reflection.BindingFlags Private =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        private readonly System.Collections.Generic.List<Object> junk = new();

        private GameObject Junk(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            junk.Add(go);
            return go;
        }

        private static void Call(NpcWorldSim sim, string method, params object[] args) =>
            typeof(NpcWorldSim).GetMethod(method, Private).Invoke(sim, args);

        /// <summary>A sim with one player at the origin and one spawned group whose one member is at <paramref name="memberAt"/>.</summary>
        private NpcWorldSim SpawnedGroupWithAPlayer(Vector3 memberAt, out NpcGroup group, out GameObject member)
        {
            var template = new NpcGroupTemplate { id = "fold-test", useStartPosition = true };
            var sim = Junk("Sim", Vector3.zero).AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Call(sim, "Awake");
            Call(sim, "Start");
            var players = (System.Collections.Generic.List<Transform>)typeof(NpcWorldSim).GetField("players", Private).GetValue(sim);
            players.Add(Junk("Player", Vector3.zero).transform);

            group = sim.FindGroup("fold-test");
            member = Junk("Member", memberAt);
            group.Live.Add(member);
            group.Spawned = true;
            return sim;
        }

        private GameObject InTheAir(GameObject member)
        {
            GameObject craft = Junk("Craft", member.transform.position + Vector3.up * 60f);
            craft.AddComponent<TestAirborneCarrier>();
            member.transform.SetParent(craft.transform, worldPositionStays: true);
            return craft;
        }

        private void CleanUpSim()
        {
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        // NpcSpawn.Remove destroys a folded member with Object.Destroy, which edit mode refuses (and logs).
        private static void ExpectFoldedMemberDestroy() =>
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Destroy may not be called from edit mode"));

        [Test]
        public void ASpawnedGroupBeyondTheDespawnRadius_IsNotFolded_WhileAMemberIsInFlight()
        {
            try
            {
                NpcWorldSim sim = SpawnedGroupWithAPlayer(new Vector3(500f, 0f, 0f), out NpcGroup group, out GameObject member);
                Assume.That(500f, Is.GreaterThan(sim.DespawnRadius));
                InTheAir(member);

                Call(sim, "TickGroup", group, 1f);

                Assert.IsTrue(group.Spawned, "folded with a member in the air: its craft is retired mid-flight in front of the player");
            }
            finally { CleanUpSim(); }
        }

        [Test]
        public void ASpawnedGroupHeldByAFlight_FoldsOnceItHasLanded()
        {
            try
            {
                NpcWorldSim sim = SpawnedGroupWithAPlayer(new Vector3(500f, 0f, 0f), out NpcGroup group, out GameObject member);
                InTheAir(member);
                Call(sim, "TickGroup", group, 1f);
                Assume.That(group.Spawned, "the flight did not hold the fold");

                member.transform.SetParent(null, worldPositionStays: true);
                ExpectFoldedMemberDestroy();
                Call(sim, "TickGroup", group, 1f);

                Assert.IsFalse(group.Spawned, "a landed group beyond the despawn radius stayed real");
            }
            finally { CleanUpSim(); }
        }

        [Test]
        public void ASpawnedGroupInFlight_StillFolds_PastTheAirborneFoldRadius()
        {
            try
            {
                NpcWorldSim sim = SpawnedGroupWithAPlayer(new Vector3(5000f, 0f, 0f), out NpcGroup group, out GameObject member);
                InTheAir(member);

                ExpectFoldedMemberDestroy();
                Call(sim, "TickGroup", group, 1f);

                Assert.IsFalse(group.Spawned, "a flight far past every player kept its group real for ever");
            }
            finally { CleanUpSim(); }
        }
    
        // ── An air patrol ──────────────────────────────────────────────────────────

        private static NpcGroupTemplate AirPatrol(float spawnRadius) => new NpcGroupTemplate
        {
            id = "air-patrol-test",
            useStartPosition = true,
            travelSpeed = 17.5f,
            airPatrol = new NpcGroupAirPatrol
            {
                route = new[] { Vector3.zero, new Vector3(1000f, 0f, 0f), new Vector3(1000f, 0f, 1000f) },
                spawnRadius = spawnRadius,
            },
        };

        private NpcWorldSim SimWith(NpcGroupTemplate template)
        {
            var sim = Junk("Sim", Vector3.zero).AddComponent<NpcWorldSim>();
            typeof(NpcWorldSim).GetField("templates", Private).SetValue(sim, new[] { template });
            Call(sim, "Awake");
            Call(sim, "Start");
            return sim;
        }

        private static float SpawnRadiusFor(NpcWorldSim sim, NpcGroupTemplate template) =>
            (float)typeof(NpcWorldSim).GetMethod("SpawnRadiusFor", Private).Invoke(sim, new object[] { template });

        [Test]
        public void AnAirPatrol_SpawnsFurtherOutThanAWalkingGroup_ButNeverWhereItWouldFoldAgain()
        {
            try
            {
                NpcWorldSim sim = SimWith(AirPatrol(550f));
                float airborneFold = (float)typeof(NpcWorldSim).GetField("airborneFoldRadius", Private).GetValue(sim);

                Assert.AreEqual(550f, SpawnRadiusFor(sim, AirPatrol(550f)), 0.01f);
                Assert.AreEqual(sim.SpawnRadius, SpawnRadiusFor(sim, new NpcGroupTemplate()), 0.01f, "a walking group's radius changed");
                Assert.AreEqual(airborneFold, SpawnRadiusFor(sim, AirPatrol(airborneFold * 3f)), 0.01f,
                                "a patrol spawning past the airborne fold radius would fold on the next tick and spawn again");
            }
            finally { CleanUpSim(); }
        }

        [Test]
        public void AnAirPatrol_IsMadeOnceAtItsOwnCruiseHeight_AndItsSpawnRunsToTheEnd()
        {
            // Playtest 2026-10-07: an air patrol has no vessel, so no rider list, and Spawn threw on its first
            // flier, every sim tick it was in range. Each throw left one more flier hanging in the sky, never
            // ordered: it read a fall, deployed and landed — "way too many flyers ... close to the ground".
            // These bare fliers cannot fly, so GroupFlight takes them away (logged, and refused in edit mode).
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                Vector3 far = new Vector3(150000f, 0f, 150000f);
                GameObject flier = Junk("PatrolFlier", far + Vector3.down * 5000f);
                NpcGroupTemplate template = AirPatrol(550f);
                template.startPosition = far;
                template.airPatrol.route = new[] { far, far + new Vector3(1000f, 0f, 0f) };
                template.airPatrol.cruiseHeight = 180f;
                template.members = new[]
                {
                    new NpcGroupMemberSpec { prefab = flier, isLeader = true, count = 1 },
                    new NpcGroupMemberSpec { prefab = flier, count = 2 },
                };
                NpcWorldSim sim = SimWith(template);
                NpcGroup group = sim.FindGroup("air-patrol-test");

                Call(sim, "Spawn", group, template);

                var made = new System.Collections.Generic.List<Transform>();
                foreach (Transform each in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (each.name == "PatrolFlier(Clone)") made.Add(each);
                foreach (Transform each in made) junk.Add(each.root.gameObject);

                Assert.AreEqual(3, made.Count, "every planned flier is made, and each only once");
                foreach (Transform each in made)
                    Assert.AreEqual(far.y + 180f, each.position.y, 3f, $"'{each.name}' was not made at the patrol's cruise height");
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                CleanUpSim();
            }
        }

        [Test]
        public void AFoldedAirPatrol_FliesItsLoop_NotAnErrand()
        {
            try
            {
                NpcWorldSim sim = SimWith(AirPatrol(550f));
                NpcGroup group = sim.FindGroup("air-patrol-test");

                Call(sim, "TickGroup", group, 1f);

                Assert.IsTrue(group.HasGoal);
                Assert.AreEqual(new Vector3(1000f, 0f, 0f), group.GoalPosition, "a patrol at its first waypoint did not head for the second");
                Assert.IsFalse(group.Spawned, "no player is near, yet it spawned");
            }
            finally { CleanUpSim(); }
        }
    }
}
