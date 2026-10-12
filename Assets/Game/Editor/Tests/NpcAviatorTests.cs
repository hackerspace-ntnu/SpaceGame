// Assets/Game/Editor/Tests/NpcAviatorTests.cs
// An NPC flown on the wing pack's craft: seated in the cradle, steered by the flight plan through
// FlyingRigidbodyMotor, set down on the ground at touchdown and the craft retired; killed in the air,
// its body drops and the craft spirals in as a wreck.
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class NpcAviatorTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> junk = new();
        private IWorldService previousWorld;
        private InstantiatingWorld world;

        private GameObject craft;
        private NpcAviator aviator;
        private GameObject pilot;

        [SetUp]
        public void SetUp()
        {
            previousWorld = GameServices.World;
            world = new InstantiatingWorld(junk);
            GameServices.World = world;
            craft = Craft(junk);
            aviator = craft.GetComponent<NpcAviator>();
            pilot = Pilot(junk);
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        /// <summary>The NpcOrnithopter's flying parts, built in code: body, FlyingRigidbodyMotor, one-seat VesselSeats, NpcAviator.</summary>
        internal static GameObject Craft(List<Object> junk)
        {
            var go = new GameObject("NpcCraft");
            junk.Add(go);
            go.transform.position = FarAway;
            go.AddComponent<Rigidbody>().useGravity = false;
            var seat = new GameObject("SEAT_Cradle").transform;
            seat.SetParent(go.transform, false);
            var motor = go.AddComponent<FlyingRigidbodyMotor>();
            var motorSo = new UnityEditor.SerializedObject(motor);
            motorSo.FindProperty("altitudeHold").boolValue = false;
            motorSo.ApplyModifiedPropertiesWithoutUndo();
            typeof(FlyingRigidbodyMotor).GetMethod("Awake", Private).Invoke(motor, null);
            var seats = go.AddComponent<VesselSeats>();
            var so = new UnityEditor.SerializedObject(seats);
            UnityEditor.SerializedProperty list = so.FindProperty("seats");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = seat;
            so.ApplyModifiedPropertiesWithoutUndo();
            go.AddComponent<NpcAviator>();
            return go;
        }

        internal static GameObject Pilot(List<Object> junk)
        {
            var go = new GameObject("Pilot");
            junk.Add(go);
            go.transform.position = FarAway;
            go.AddComponent<HealthComponent>();
            go.AddComponent<AgentController>();
            return go;
        }

        private Vector3 GoalPoint => FarAway + Vector3.forward * 2000f;
        private bool Fly() => aviator.Fly(pilot, GoalPoint, 60f, 6f);
        private MoveIntent? Tick(float deltaTime = 0.02f) =>
            aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, deltaTime);
        private void Touchdown(float closingSpeed) =>
            typeof(NpcAviator).GetMethod("Touchdown", Private).Invoke(aviator, new object[] { craft.transform.position, closingSpeed });

        [Test]
        public void Fly_SeatsThePilot_AndStartsTheFlight()
        {
            Assert.IsTrue(Fly());
            Assert.AreEqual(pilot, aviator.Pilot);
            Assert.IsTrue(pilot.transform.IsChildOf(craft.transform), "the pilot is not in the cradle");
            Assert.IsTrue(pilot.GetComponent<AgentController>().RidesAsPassenger, "the pilot kept its feet");
            Assert.AreEqual(NpcFlightPhase.EnRoute, aviator.Phase);
        }

        [Test]
        public void Fly_RefusesWhileAlreadyFlying()
        {
            Assert.IsTrue(Fly());
            Assert.IsFalse(aviator.Fly(Pilot(junk), FarAway, 60f, 6f));
        }

        [Test]
        public void Tick_SteersTowardTheGoal()
        {
            Fly();
            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue);
            Assert.AreEqual(AgentIntentType.MoveToPosition, intent.Value.Type);
            Assert.Greater(intent.Value.TargetPosition.z, craft.transform.position.z, "not heading for a goal due north");
        }

        [Test]
        public void Touchdown_PutsThePilotDown_FreesIt_AndRetiresTheCraft()
        {
            GameObject released = null;
            bool alive = false;
            aviator.PilotReleased += (npc, a) => { released = npc; alive = a; };
            Fly();

            Touchdown(0f);

            Assert.IsNull(pilot.transform.parent, "the pilot is still in the cradle");
            Assert.IsFalse(pilot.GetComponent<AgentController>().RidesAsPassenger);
            CollectionAssert.Contains(world.Despawned, craft);
            Assert.AreEqual(pilot, released);
            Assert.IsTrue(alive);
        }

        [Test]
        public void AGentleLanding_IsFree_AndAHardArrivalHurts()
        {
            var health = pilot.GetComponent<HealthComponent>();
            int full = health.GetHealth;
            Fly();
            Touchdown(2f);
            Assert.AreEqual(full, health.GetHealth, "a gentle landing cost health");

            GameObject craft2 = Craft(junk);
            craft2.GetComponent<NpcAviator>().Fly(pilot, GoalPoint, 60f, 6f);
            typeof(NpcAviator).GetMethod("Touchdown", Private)
                .Invoke(craft2.GetComponent<NpcAviator>(), new object[] { craft2.transform.position, 25f });
            Assert.Less(health.GetHealth, full, "flying into the ground at 25 m/s cost nothing");
        }

        [Test]
        public void ACrashIntoAWall_EndsTheFlight()
        {
            Fly();
            typeof(NpcAviator).GetField("launchedAt", Private).SetValue(aviator, -999f);
            typeof(NpcAviator).GetField("lastVelocity", Private).SetValue(aviator, new Vector3(0f, 0f, 25f));

            typeof(NpcAviator).GetMethod("Crash", Private)
                .Invoke(aviator, new object[] { craft.transform.position, Vector3.back, false });

            Assert.IsNull(pilot.transform.parent);
            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void ThePilotDying_WrecksTheCraft_AndDropsTheBodyStraightDown()
        {
            bool releasedDead = false;
            aviator.PilotReleased += (npc, a) => releasedDead = npc == pilot && !a;
            Fly();

            pilot.GetComponent<HealthComponent>().Damage(999);

            Assert.IsNull(pilot.transform.parent, "the body rides on in the cradle");
            Assert.IsTrue(aviator.Wrecked);
            Assert.AreEqual(NpcFlightPhase.Wreck, aviator.Phase, "a pilotless craft flew on");
            Assert.IsTrue(releasedDead);
            CollectionAssert.DoesNotContain(world.Despawned, craft, "the wreck vanished in mid-air");
        }

        [Test]
        public void AWreckReachingTheGround_IsRetired()
        {
            Fly();
            pilot.GetComponent<HealthComponent>().Damage(999);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(ground);
            ground.transform.position = FarAway + Vector3.down * 1.5f;
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            Physics.SyncTransforms();

            Tick();

            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void ACraftWhosePilotWasTakenAway_RetiresItself()
        {
            Fly();
            Object.DestroyImmediate(pilot);   // its group folded: NpcSpawn.Remove under the craft

            Tick();

            CollectionAssert.Contains(world.Despawned, craft);
        }

        [Test]
        public void WithNoSiteFound_TheCraftLandsAtTheGoalOnTheGround()
        {
            Ground(GoalPoint, 80f);   // ground, but no NavMesh: nowhere to set down properly
            Fly();
            Tick();

            Assert.IsFalse(aviator.HasSite, "a site was found with no NavMesh to stand on");
            Assert.AreEqual(GoalPoint.y, aviator.LandingPoint.y, 0.01f, "the goal was not projected onto the ground");
            Assert.AreEqual(GoalPoint.x, aviator.LandingPoint.x, 0.01f);
            Assert.AreEqual(GoalPoint.z, aviator.LandingPoint.z, 0.01f);
        }

        [Test]
        public void WithReachableGroundNearTheGoal_TheSiteSearchChoosesALandingOnTheRings()
        {
            Ground(GoalPoint, 80f);
            NavMeshDataInstance mesh = BakeNavMesh(GoalPoint, 80f);
            try
            {
                Fly();
                typeof(NpcAviator).GetField("streamer", Private).SetValue(aviator, null);   // whatever scene the runner has open
                Tick();

                Assert.IsTrue(aviator.HasSite, "the site search never ran, or found nothing on flat NavMesh ground");
                float off = Vector2.Distance(new Vector2(aviator.LandingPoint.x, aviator.LandingPoint.z),
                                             new Vector2(GoalPoint.x, GoalPoint.z));
                Assert.That(off, Is.InRange(NpcAviator.CraftLanding.ringMin - 0.01f, NpcAviator.CraftLanding.ringMax + 0.01f));
            }
            finally
            {
                mesh.Remove();
            }
        }

        [Test]
        public void Fly_RefusesADeadPilot()
        {
            pilot.GetComponent<HealthComponent>().Damage(999);

            Assert.IsFalse(Fly(), "a corpse was seated and flown");
            Assert.IsNull(pilot.transform.parent);
        }

        // OnCollisionEnter is never sent in Edit Mode, even under PhysicsScene.Simulate, so these drive the
        // contact seam it forwards to with real colliders.
        [Test]
        public void ThePilotsBody_DroppingThroughTheHull_DoesNotRetireTheWreck()
        {
            var body = pilot.AddComponent<BoxCollider>();   // the body, still inside the cradle when it is let go
            Fly();
            PastLaunchGrace();

            pilot.GetComponent<HealthComponent>().Damage(999);
            Contact(body);

            CollectionAssert.DoesNotContain(world.Despawned, craft, "the wreck crashed into its own pilot's body");
        }

        [Test]
        public void SomethingLooseBumpingTheCraft_DoesNotEndTheFlight()
        {
            Fly();
            PastLaunchGrace();
            GameObject loose = Box();
            loose.AddComponent<Rigidbody>().useGravity = false;   // a projectile, a ragdoll, another craft

            Contact(loose.GetComponent<Collider>());

            CollectionAssert.DoesNotContain(world.Despawned, craft, "a dynamic body ended the flight");
            Assert.AreEqual(pilot, aviator.Pilot);
        }

        [Test]
        public void FlyingIntoTheWorld_EndsTheFlight()
        {
            Fly();
            PastLaunchGrace();

            Contact(Box().GetComponent<Collider>());   // a static rock

            CollectionAssert.Contains(world.Despawned, craft, "flying into a rock did not end the flight");
            Assert.IsNull(pilot.transform.parent);
        }

        [Test]
        public void FlyingIntoALayerOutsideTheCrashMask_DoesNotEndTheFlight()
        {
            Fly();
            PastLaunchGrace();
            var mask = (LayerMask)typeof(NpcAviator).GetField("crashMask", Private).GetValue(aviator);
            typeof(NpcAviator).GetField("crashMask", Private).SetValue(aviator, (LayerMask)(mask.value & ~1));   // not Default

            Contact(Box().GetComponent<Collider>());

            CollectionAssert.DoesNotContain(world.Despawned, craft);
        }

        // The goal straight under the craft, at touchdown height below it: with ground there it lands at once.
        private Vector3 GoalUnderTheCraft => craft.transform.position - Vector3.up * new NpcFlightSettings().TouchdownHeight;

        [Test]
        public void WithNoGroundUnderTheGoal_TheCraftCirclesAndNeverSetsThePilotDown()
        {
            float cruiseOver = pilot.transform.position.y;   // the take-off spot: the last ground it knows
            Assert.IsTrue(aviator.Fly(pilot, GoalUnderTheCraft, 60f, 6f));

            // Flown by a stand-in for the motor: 25 m/s toward each step's target, for 50 s.
            for (int i = 0; i < 50; i++)
            {
                MoveIntent? intent = Tick(1f);
                Assert.IsTrue(intent.HasValue, "the craft stopped flying: it landed with no ground under the goal");
                craft.transform.position = Vector3.MoveTowards(craft.transform.position, intent.Value.TargetPosition, 25f);
            }

            Assert.AreEqual(cruiseOver + 60f, craft.transform.position.y, 5f, "a circling craft did not hold cruise over the last ground it saw");
            CollectionAssert.DoesNotContain(world.Despawned, craft, "the craft landed over ground that never streamed in");
            Assert.AreEqual(NpcFlightPhase.EnRoute, aviator.Phase, "the craft approached a landing with no ground under it");
            Assert.IsTrue(pilot.transform.IsChildOf(craft.transform), "the pilot was stood in mid-air");
        }

        [Test]
        public void GroundStreamingInUnderTheGoal_LetsTheCraftLand()
        {
            Vector3 goal = GoalUnderTheCraft;
            Assert.IsTrue(aviator.Fly(pilot, goal, 60f, 6f));
            craft.transform.position += Vector3.up * 60f;   // climbed to cruise, circling
            Tick();
            CollectionAssert.DoesNotContain(world.Despawned, craft);

            Ground(goal, 80f);
            craft.transform.position = goal + Vector3.up * new NpcFlightSettings().TouchdownHeight;
            Physics.SyncTransforms();
            Tick();

            CollectionAssert.Contains(world.Despawned, craft, "the craft never landed once the ground had loaded");
            Assert.AreEqual(goal.y, pilot.transform.position.y, 0.5f, "the pilot was not stood on the ground");
        }

        [Test]
        public void CirclingTooLong_GivesTheGoalUp_AndLandsOnTheLastGroundFlownOver()
        {
            Vector3 lastGround = craft.transform.position + Vector3.down * 30f;
            Ground(lastGround, 20f);   // under the craft only, not under the goal
            Assert.IsTrue(aviator.Fly(pilot, craft.transform.position + Vector3.forward * 50f, 60f, 6f));

            float wait = (float)typeof(NpcAviator).GetField("groundWaitSeconds", Private).GetValue(aviator);
            Tick(wait + 1f);

            Assert.AreEqual(lastGround.y, aviator.LandingPoint.y, 0.01f, "the craft kept circling a goal with no ground");
            Assert.AreEqual(lastGround.z, aviator.LandingPoint.z, 0.01f);

            craft.transform.position = lastGround + Vector3.up * new NpcFlightSettings().TouchdownHeight;
            Physics.SyncTransforms();
            Tick();

            CollectionAssert.Contains(world.Despawned, craft, "the craft never landed on the ground it fell back to");
            Assert.AreEqual(lastGround.y, pilot.transform.position.y, 0.5f);
        }

        [Test]
        public void AWreckOverNoGround_IsRetiredOnceItHasFallenFarEnough()
        {
            Fly();
            pilot.GetComponent<HealthComponent>().Damage(999);
            Tick();
            CollectionAssert.DoesNotContain(world.Despawned, craft, "the wreck was retired before it fell");

            float depth = (float)typeof(NpcAviator).GetField("noGroundDepth", Private).GetValue(aviator);
            craft.transform.position += Vector3.down * (depth + 1f);
            Tick();

            CollectionAssert.Contains(world.Despawned, craft, "a wreck over no ground falls forever");
        }

        [Test]
        public void DestroyingTheCraftInFlight_StillReleasesThePilot()
        {
            GameObject released = null;
            bool alive = false;
            aviator.PilotReleased += (npc, a) => { released = npc; alive = a; };
            Fly();

            typeof(NpcAviator).GetMethod("OnDestroy", Private).Invoke(aviator, null);

            Assert.AreEqual(pilot, released, "whoever waits on the pilot's release never heard it");
            Assert.IsTrue(alive);
        }

        [Test]
        public void ATouchdownThatCannotSetThePilotDown_KeepsItAboard()
        {
            bool releasedAny = false;
            aviator.PilotReleased += (npc, a) => releasedAny = true;
            Fly();
            craft.SetActive(false);   // Unity will not reparent out of an inactive hierarchy

            LogAssert.Expect(LogType.Error, new Regex("could not set its pilot"));
            Touchdown(0f);
            Touchdown(0f);   // retried every tick while Landed: one error per landing, not one per tick

            Assert.IsTrue(pilot.transform.IsChildOf(craft.transform), "the pilot was lost with the craft");
            Assert.IsFalse(releasedAny, "a pilot still in the cradle was reported released");
            CollectionAssert.DoesNotContain(world.Despawned, craft, "the craft was retired with its pilot aboard");
        }

        [Test]
        public void ALivingNpcBrushingTheCraft_DoesNotEndTheFlight()
        {
            Fly();
            PastLaunchGrace();
            GameObject mate = Box();   // a Sky nomad: Default-layer collider, kinematic root body, health
            mate.AddComponent<Rigidbody>().isKinematic = true;
            mate.AddComponent<HealthComponent>();

            Contact(mate.GetComponent<Collider>());

            CollectionAssert.DoesNotContain(world.Despawned, craft, "a group-mate brushing the wings ended the flight");
            Assert.AreEqual(pilot, aviator.Pilot);
        }

        [Test]
        public void FlyingIntoACrewedHull_StillEndsTheFlight()
        {
            Fly();
            PastLaunchGrace();
            GameObject hull = Box();   // a sky transport: kinematic, health, seats
            hull.AddComponent<Rigidbody>().isKinematic = true;
            hull.AddComponent<HealthComponent>();
            hull.AddComponent<VesselSeats>();

            Contact(hull.GetComponent<Collider>());

            CollectionAssert.Contains(world.Despawned, craft, "flying into a hull did not end the flight");
        }

        [Test]
        public void DestroyingTheCraftAfterItsFlightEnded_DoesNotReleaseThePilotAgain()
        {
            int releases = 0;
            aviator.PilotReleased += (npc, a) => releases++;
            Fly();
            Touchdown(0f);

            typeof(NpcAviator).GetMethod("OnDestroy", Private).Invoke(aviator, null);

            Assert.AreEqual(1, releases, "a landed pilot was released twice");
        }

        [Test]
        public void DestroyingAWreck_DoesNotReleaseTheDeadPilotAgain()
        {
            int releases = 0;
            aviator.PilotReleased += (npc, a) => releases++;
            Fly();
            pilot.GetComponent<HealthComponent>().Damage(999);

            typeof(NpcAviator).GetMethod("OnDestroy", Private).Invoke(aviator, null);

            Assert.AreEqual(1, releases, "a dead pilot was released twice");
        }

        private void PastLaunchGrace() => typeof(NpcAviator).GetField("launchedAt", Private).SetValue(aviator, -999f);

        private void Ground(Vector3 top, float size)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(ground);
            ground.transform.position = top + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(size, 1f, size);
            Physics.SyncTransforms();
        }

        private NavMeshDataInstance BakeNavMesh(Vector3 top, float size)
        {
            NavMeshData data = BuildSlabNavMesh(top, size);
            junk.Add(data);
            return NavMesh.AddNavMeshData(data);
        }

        /// <summary>A NavMesh over a square slab whose walkable top is at <paramref name="top"/>. The caller destroys it.</summary>
        internal static NavMeshData BuildSlabNavMesh(Vector3 top, float size)
        {
            var source = new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                transform = Matrix4x4.Translate(top + Vector3.down * 0.5f),
                size = new Vector3(size, 1f, size),
            };
            return NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0),
                                                   new List<NavMeshBuildSource> { source },
                                                   new Bounds(top, new Vector3(size + 20f, 30f, size + 20f)),
                                                   Vector3.zero, Quaternion.identity);
        }

        private GameObject Box()
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(box);
            box.transform.position = craft.transform.position + Vector3.right;
            return box;
        }

        private void Contact(Collider other) =>
            typeof(NpcAviator).GetMethod("OnContact", Private)
                .Invoke(aviator, new object[] { other, other.transform.position, Vector3.left });
    }
}
