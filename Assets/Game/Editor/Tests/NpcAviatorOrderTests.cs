// Assets/Game/Editor/Tests/NpcAviatorOrderTests.cs
// The NPC craft's orders beyond "fly there and land": boarded with no order it cruises straight on; CruiseTo
// circles a point at cruise and never lands or gives up; Escort keeps a station on any Transform, holding its
// heading on station and circling where the anchor was when it is gone; LandOnDeck comes down on a surface the
// ground probe cannot see (a hull's deck) and puts the pilot down there.
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.Tests
{
    public class NpcAviatorOrderTests
    {
        private static readonly Vector3 FarAway = new Vector3(150000f, 6000f, 150000f);
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const float CruiseHeight = 60f;
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
            craft = NpcAviatorTests.Craft(junk);
            aviator = craft.GetComponent<NpcAviator>();
            pilot = NpcAviatorTests.Pilot(junk);

            // No drift: a station is then exactly where the maths says.
            var escort = (EscortSettings)typeof(NpcAviator).GetField("escort", Private).GetValue(aviator);
            escort.driftAmplitude = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.World = previousWorld;
            foreach (Object o in junk) if (o != null) Object.DestroyImmediate(o);
            junk.Clear();
        }

        private MoveIntent? Tick(float deltaTime = 0.02f) =>
            aviator.Tick(new AgentContext { Self = craft.transform, Position = craft.transform.position }, deltaTime);

        private Transform Anchor(Vector3 position, float yaw)
        {
            var go = new GameObject("Anchor");
            junk.Add(go);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        [Test]
        public void Boarded_WithAGroundHint_TheCraftCruisesOverThatGround_NotOverThePilot()
        {
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 500f));
            MoveIntent? intent = Tick();

            Assert.AreEqual(FlightOrder.Cruise, aviator.Order, "boarded with no order, the craft should cruise straight on");
            Assert.IsTrue(intent.HasValue);
            Assert.Less(intent.Value.TargetPosition.y, craft.transform.position.y,
                        "a craft made 500 m over its ground climbed as if the pilot's mid-air position were the ground");
        }

        [Test]
        public void ACruise_NeverLands_NorGivesUp_HoweverLongItCircles()
        {
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * CruiseHeight));
            aviator.CruiseTo(FarAway + Vector3.forward * 10f, 0.7f);

            for (int i = 0; i < 20; i++)
            {
                MoveIntent? intent = Tick(10f);
                Assert.IsTrue(intent.HasValue);
                Assert.AreEqual(AgentIntentType.MoveToPosition, intent.Value.Type, "the cruising craft stopped flying");
                Assert.AreEqual(0.7f, intent.Value.SpeedMultiplier, 0.001f, "the cruise speed was not applied");
            }

            Assert.IsTrue(aviator.ReachedCruisePoint, "a craft over its point does not report it is circling there");
            Assert.IsFalse(aviator.GaveUp, "a cruise gave its point up like a landing whose ground never streamed in");
            Assert.AreEqual(FlightOrder.Cruise, aviator.Order);
            Assert.IsTrue(pilot.transform.IsChildOf(craft.transform), "the cruising pilot was put down");
        }

        [Test]
        public void AnEscort_AimsAtItsStation_InTheAnchorsHeadingFrame()
        {
            Transform anchor = Anchor(FarAway + Vector3.forward * 100f, 90f);
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 1000f));
            aviator.Escort(FlightStation.Fixed(anchor, new Vector3(10f, 0f, 0f), 0));

            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue);
            Vector3 station = anchor.position + new Vector3(0f, 0f, -10f);   // right of an anchor heading +X is -Z
            Assert.Less(Vector3.Distance(intent.Value.TargetPosition, station), 0.5f, "the craft is not aiming at its station");
            Assert.IsFalse(aviator.AnchorLost);
        }

        [Test]
        public void OnStation_TheEscortHoldsItsAnchorsHeading()
        {
            Transform anchor = Anchor(FarAway, 90f);
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 1000f));
            aviator.Escort(FlightStation.Fixed(anchor, Vector3.up * 3f, 0));
            craft.transform.position = anchor.position + Vector3.up * 3f;

            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue && intent.Value.OverrideFacing, "on station the escort faces its travel, not its anchor's heading");
            Vector3 facing = intent.Value.FacePosition - craft.transform.position;
            Assert.Greater(Vector3.Dot(facing.normalized, Vector3.right), 0.99f);
        }

        [Test]
        public void WithItsAnchorGone_AnEscortCirclesWhereItWas_AndNeverLandsByItself()
        {
            Transform anchor = Anchor(FarAway + Vector3.forward * 50f, 0f);
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 1000f));
            aviator.Escort(FlightStation.Fixed(anchor, Vector3.zero, 0));
            Tick();

            Object.DestroyImmediate(anchor.gameObject);
            for (int i = 0; i < 10; i++)
            {
                MoveIntent? intent = Tick(10f);
                Assert.IsTrue(intent.HasValue && intent.Value.Type == AgentIntentType.MoveToPosition, "the orphaned escort stopped flying");
            }

            Assert.IsTrue(aviator.AnchorLost);
            Assert.AreEqual(FlightOrder.Escort, aviator.Order);
            CollectionAssert.DoesNotContain(world.Despawned, craft, "the orphaned escort landed on its own");
        }

        [Test]
        public void AfterAnEscort_LandAt_FliesToTheGoalToLand()
        {
            Transform anchor = Anchor(FarAway, 0f);
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 1000f));
            aviator.Escort(FlightStation.Fixed(anchor, Vector3.zero, 0));
            Tick();

            Vector3 goal = FarAway + Vector3.back * 2000f;
            aviator.LandAt(goal);
            MoveIntent? intent = Tick();

            Assert.AreEqual(FlightOrder.Land, aviator.Order);
            Assert.AreEqual(NpcFlightPhase.EnRoute, aviator.Phase);
            Assert.Less(intent.Value.TargetPosition.z, craft.transform.position.z, "not heading for a goal due south");
        }

        [Test]
        public void OnTheWayToADeck_TheCraftCruisesOverTheDeck_NotOverTheGroundFarBelowIt()
        {
            // The terrain is 1000 m down (the hint); the deck point is level with the craft, 220 m off.
            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 1000f));
            craft.transform.position = FarAway + new Vector3(0f, 30f, -220f);
            Tick();   // airborne, cruising straight on

            aviator.LandOnDeck(FarAway);
            MoveIntent? intent = Tick();

            Assert.IsTrue(intent.HasValue);
            Assert.GreaterOrEqual(intent.Value.TargetPosition.y, craft.transform.position.y - 0.01f,
                                  "a craft heading for a deck sank toward cruise height over the terrain beneath it");
        }

        [Test]
        public void LandOnDeck_TakesTheDeckForTheGround_AndPutsThePilotDownOnIt()
        {
            // A deck with a body (never ground to the probe), the craft just over it at touchdown height.
            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            junk.Add(deck);
            deck.transform.position = FarAway + Vector3.down * 0.5f;
            deck.transform.localScale = new Vector3(40f, 1f, 40f);
            deck.AddComponent<Rigidbody>().isKinematic = true;
            Physics.SyncTransforms();

            Assert.IsTrue(aviator.Board(pilot, CruiseHeight, 6f, FarAway + Vector3.down * 1000f));
            craft.transform.position = FarAway + Vector3.up * 30f;
            aviator.Escort(FlightStation.Fixed(deck.transform, Vector3.up * 30f, 0));
            Tick();   // airborne, well clear of the deck

            aviator.LandOnDeck(FarAway);
            Assert.IsTrue(aviator.HasSite, "a deck landing looked for a site of its own");
            Tick();   // still 30 m up: the plan sees it has flown (it cannot land before that)
            Assert.AreEqual(NpcFlightPhase.Approach, aviator.Phase, "30 m straight over the deck point, the craft is not coming down");

            craft.transform.position = FarAway + Vector3.up * new NpcFlightSettings().TouchdownHeight;
            Tick();

            CollectionAssert.Contains(world.Despawned, craft, "the craft never touched down on the deck");
            Assert.IsNull(pilot.transform.parent, "the pilot is still in the cradle");
            Assert.Less(Vector3.Distance(pilot.transform.position, FarAway), 1f,
                        "the pilot was put down somewhere other than the deck point (the ground below it?)");
        }
    }
}
