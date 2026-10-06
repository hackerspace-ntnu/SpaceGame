// Guards the lander's intercepted signal: which settlement the working transmitter's signal leads to,
// what the terminal shows before and after the transmitter works, the two objective steps that hang off
// it, and that the choice survives a save.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types, and
// an asmdef cannot reference Assembly-CSharp. Built by hand like BrokenTransmitterTests: nothing runs
// Awake or OnEnable in EditMode, so the rack is woken and registered explicitly.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Persistence;
using SpaceGame.Presentation;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.Tests
{
    public class ShipSignalTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private const string ShipPrefabPath = "Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/PlayerShip.prefab";
        private const string TerminalPrefabPath = "Assets/Game/Prefabs/Environment/Structures/Facilities/StandingTerminal.prefab";
        private const string ChainPath = "Assets/Game/ScriptableObjects/Objectives/CrashSiteChain.asset";
        private const string FactionFolder = "Assets/Game/ScriptableObjects/Factions/Core/";
        private const string RelationshipsPath = FactionFolder + "GlobalRelationships.asset";

        private readonly List<Object> spawned = new();

        [TearDown]
        public void CleanUp()
        {
            ActiveRacks().RemoveAll(r => r == null || spawned.Contains(r.gameObject));

            foreach (Object o in spawned)
                if (o != null) Object.DestroyImmediate(o);
            spawned.Clear();
        }

        // ─────────────────────────── The destination rule ───────────────────────────

        [Test]
        public void TheSignalLeadsToTheNearestFixedFriendlyTownPastTheWreck()
        {
            FactionDefinition crew = Faction("Crew");
            FactionDefinition friendly = Faction("Friendly");
            FactionDefinition walkers = Faction("Walkers", wandering: true);
            FactionDefinition robots = Faction("Robots", hostile: true);
            FactionRelationshipTable table = Table();

            var towns = new[]
            {
                Town("beside-the-wreck", friendly, new Vector3(120f, 0f, 0f)),
                Town("walking-city", walkers, new Vector3(0f, 0f, 400f)),
                Town("robot-town", robots, new Vector3(-450f, 0f, 0f)),
                Town("no-faction", null, new Vector3(0f, 0f, -420f)),
                Town("far-friend", friendly, new Vector3(900f, 0f, 0f)),
                Town("near-friend", friendly, new Vector3(0f, 80f, -600f)),
            };

            Assert.IsTrue(SignalDestinationRule.TryChoose(towns, Vector3.zero, 300f, crew, table, out var chosen),
                          "no town qualified, though two fixed, friendly towns stand past the wreck.");
            Assert.AreEqual("near-friend", chosen.id,
                            "the signal did not lead to the nearest fixed, friendly town further than 300 m " +
                            "(the walking city, the hostile town, the factionless one and the one beside the wreck " +
                            "must all be skipped; height must not count).");
        }

        [Test]
        public void TheTownTheWreckCameDownBesideIsSkippedAndTheChoiceIsDeterministic()
        {
            FactionDefinition crew = Faction("Crew");
            FactionDefinition friendly = Faction("Friendly");
            FactionRelationshipTable table = Table();

            var onlyNear = new[] { Town("home", friendly, new Vector3(250f, 0f, 0f)) };
            Assert.IsFalse(SignalDestinationRule.TryChoose(onlyNear, Vector3.zero, 300f, crew, table, out _),
                           "a town 250 m off was chosen with a 300 m minimum.");
            Assert.IsTrue(SignalDestinationRule.TryChoose(onlyNear, Vector3.zero, 200f, crew, table, out _),
                          "the minimum distance is not the tunable it claims to be.");

            var tied = new[]
            {
                Town("b-town", friendly, new Vector3(500f, 0f, 0f)),
                Town("a-town", friendly, new Vector3(-500f, 0f, 0f)),
            };
            SignalDestinationRule.TryChoose(tied, Vector3.zero, 300f, crew, table, out var first);
            SignalDestinationRule.TryChoose(tied.Reverse().ToArray(), Vector3.zero, 300f, crew, table, out var second);
            Assert.AreEqual("a-town", first.id, "a tie was not settled by the lower id.");
            Assert.AreEqual(first.id, second.id, "the same towns in another order chose another town.");
        }

        [Test]
        public void TheShippedFactionsKeepTheSignalOffMovingCitiesAndTheClankers()
        {
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(RelationshipsPath);
            FactionDefinition humans = Load("HumansFaction");
            Assert.IsNotNull(table, $"No relationship table at {RelationshipsPath}.");

            Assert.IsTrue(Load("StriderFaction").wandering, "the Striders' walking city is not marked wandering.");
            Assert.IsTrue(Load("SkyTribeFaction").wandering, "the Sky Tribe's flying city is not marked wandering.");
            Assert.IsFalse(SignalDestinationRule.Welcomes(Load("StriderFaction"), humans, table), "the signal may lead to a Strider city.");
            Assert.IsFalse(SignalDestinationRule.Welcomes(Load("SkyTribeFaction"), humans, table), "the signal may lead to the Sky City.");
            Assert.IsFalse(SignalDestinationRule.Welcomes(Load("ClankerFaction"), humans, table), "the signal may lead to the Clankers.");
            Assert.IsTrue(SignalDestinationRule.Welcomes(Load("DriftersFaction"), humans, table), "the nomads' towns are refused.");
            Assert.IsTrue(SignalDestinationRule.Welcomes(humans, humans, table), "the colony is refused.");
        }

        [Test]
        public void BearingIsClockwiseFromNorth()
        {
            Assert.AreEqual(0f, SignalDestinationRule.Bearing(Vector3.zero, Vector3.forward), 1e-3f);
            Assert.AreEqual(90f, SignalDestinationRule.Bearing(Vector3.zero, Vector3.right), 1e-3f);
            Assert.AreEqual(270f, SignalDestinationRule.Bearing(Vector3.zero, Vector3.left), 1e-3f);
            Assert.AreEqual(500f, SignalDestinationRule.FlatDistance(Vector3.zero, new Vector3(300f, 90f, 400f)), 1e-3f);
        }

        // ─────────────────────────── The terminal ───────────────────────────

        [Test]
        public void OnlyTheHullDrawingWorksUntilAWorkingTransmitterIsFitted()
        {
            TelemetrySnapshot offline = Snapshot(installedMask: 0b01);
            Assert.IsFalse(ShipTelemetry.TransmitterOnline(offline));
            Assert.IsFalse(ShipTelemetry.ShowsStatic(TerminalScreen.ShipPage, offline), "the hull drawing went to static.");
            Assert.IsTrue(ShipTelemetry.ShowsStatic(TerminalScreen.StatusPage, offline), "STATUS works with no transmitter.");
            Assert.IsTrue(ShipTelemetry.ShowsStatic(TerminalScreen.GpsPage, offline), "GPS works with no transmitter.");
            Assert.IsTrue(ShipTelemetry.ShowsStatic(TerminalScreen.CommsPage, offline), "COMMS works with no transmitter.");
            Assert.IsFalse(ShipTelemetry.CommsTabShown(offline), "the COMMS tab is up with no transmitter.");
            Assert.AreEqual(ShipTelemetry.OfflineLine, ShipTelemetry.CommsPage(offline));

            TelemetrySnapshot online = Snapshot(installedMask: 0b11);
            Assert.IsTrue(ShipTelemetry.TransmitterOnline(online));
            for (int page = 0; page < TerminalConsole.PageCount; page++)
                Assert.IsFalse(ShipTelemetry.ShowsStatic(page, online), $"page {page} is static with the transmitter working.");
            Assert.IsTrue(ShipTelemetry.CommsTabShown(online));
            Assert.AreEqual("COMMS", TerminalConsole.PageNames[TerminalScreen.CommsPage]);
        }

        [Test]
        public void TheBurntOutUnitIsNotAWorkingTransmitter()
        {
            ShipPartRack rack = Rack(out ShipSignal signal);
            Assert.IsTrue(rack.IsBroken(1), "the fixture's transmitter socket should start holding its burnt-out unit.");
            Assert.IsFalse(signal.TransmitterWorking, "the burnt-out unit counts as a working transmitter.");

            rack.RestoreMasks(0b10, 0);
            Assert.IsTrue(signal.TransmitterWorking, "a fitted transmitter does not count as working.");
            Assert.IsTrue(ShipSignal.IsTransmitterFitted(rack));
        }

        [Test]
        public void TheCommsPagePrintsTheCallAndWhereItComesFrom()
        {
            TelemetrySnapshot s = Snapshot(installedMask: 0b11);
            s.Position = new Vector3(100f, 0f, 100f);
            s.SignalTranscript = "Follow this carrier.";

            StringAssert.Contains("SCANNING", ShipTelemetry.CommsPage(s), "a carrier with nothing heard yet says nothing.");

            s.Signal = SignalDestination.Nowhere;
            StringAssert.Contains("NO VOICE TRAFFIC", ShipTelemetry.CommsPage(s));

            s.Signal = SignalDestination.To("colony", "Humans", new Vector3(100f, 30f, 1600f), 140f);
            string page = ShipTelemetry.CommsPage(s);
            StringAssert.Contains("\"Follow this carrier.\"", page);
            StringAssert.Contains("ORIGIN           HUMANS", page);
            StringAssert.Contains("BEARING          000°  N", page);
            StringAssert.Contains("RANGE            1.50 km", page);
        }

        // ─────────────────────────── The objective steps ───────────────────────────

        [Test]
        public void AnswerTheSignalBeginsOnceTheSignalIsHeardAndIsMetOnArrival()
        {
            Rack(out ShipSignal signal);
            var world = new ObjectiveWorld();
            var step = Track(ScriptableObject.CreateInstance<AnswerSignalStep>());

            Assert.IsFalse(step.TryBegin(world), "the step began before the transmitter heard anything.");

            signal.Restore(SignalDestination.To("colony", "Humans", new Vector3(0f, 0f, 500f), 140f));
            Assert.IsTrue(step.TryBegin(world), "the step did not begin once the signal was heard.");
            Assert.IsTrue(step.TryGetWaypoint(world, out Vector3 waypoint), "no waypoint to the signal's source.");
            Assert.AreEqual(500f, waypoint.z, 1e-3f);
            Assert.IsTrue(step.TryGetBeacon(world, out _), "no beacon over the signal's source.");
            StringAssert.Contains("Humans", step.Status(world));

            SignalDestination d = signal.Destination;
            Assert.IsFalse(AnswerSignalStep.HasArrived(d, 300f, 15f), "arrived 300 m out from a town 140 m across.");
            Assert.IsTrue(AnswerSignalStep.HasArrived(d, 150f, 15f), "not arrived inside the town's reach.");
            Assert.IsFalse(AnswerSignalStep.HasArrived(SignalDestination.Nowhere, 0f, 15f));

            signal.Restore(SignalDestination.Nowhere);
            Assert.IsTrue(step.TryBegin(world));
            Assert.IsTrue(step.IsMet(world), "a signal that leads nowhere leaves the step standing.");
        }

        [Test]
        public void FitTheTransmitterFollowsTheSocketAndIsMetByAWorkingOne()
        {
            ShipPartRack rack = Rack(out _);
            var world = new ObjectiveWorld();
            var step = Track(ScriptableObject.CreateInstance<FitTransmitterStep>());

            Assert.IsFalse(step.IsMet(world), "met with the burnt-out unit still in its socket.");
            Assert.IsTrue(step.TryGetWaypoint(world, out _), "no waypoint to the burnt-out unit.");
            StringAssert.Contains("Pull", step.Status(world), "a burnt-out unit with no fire on the hull does not ask to be pulled.");

            rack.RestoreMasks(0, 0);
            StringAssert.Contains("radar dish", step.Status(world), "an empty socket does not send the crew to the tower.");
            Assert.IsFalse(step.IsMet(world));

            rack.RestoreMasks(0b10, 0);
            Assert.IsTrue(step.IsMet(world), "a working transmitter in the socket does not meet the step.");
        }

        [Test]
        public void TheChainAsksForTheTransmitterThenTheSignalBeforeTheWholeRepair()
        {
            var chain = AssetDatabase.LoadAssetAtPath<ObjectiveChain>(ChainPath);
            Assert.IsNotNull(chain, $"No chain at {ChainPath}.");

            List<string> ids = chain.Steps.Select(s => s != null ? s.Id : null).ToList();
            int fit = ids.IndexOf("fit-transmitter");
            int answer = ids.IndexOf("answer-signal");
            int repair = ids.IndexOf("repair-ship");

            Assert.GreaterOrEqual(fit, 0, "the chain never asks for the transmitter.");
            Assert.AreEqual(fit + 1, answer, "answering the signal does not follow fitting the transmitter.");
            Assert.Less(answer, repair, "the signal waits for the whole ship to be repaired.");
            Assert.IsInstanceOf<FitTransmitterStep>(chain.Steps[fit]);
            Assert.IsInstanceOf<AnswerSignalStep>(chain.Steps[answer]);
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "two steps share an id, so a save cannot tell them apart.");

            // A save from before the two steps keeps its place by id.
            Assert.AreEqual(repair, ObjectiveDirector.Resolve(chain, "repair-ship", false, true).Step,
                            "a save at the repair step lost its place.");
        }

        // ─────────────────────────── Persistence ───────────────────────────

        [Test]
        public void TheSignalSurvivesASave()
        {
            Rack(out ShipSignal signal);
            var saver = signal.gameObject.AddComponent<ShipSignalSaveable>();
            Assert.IsNull(saver.CaptureState(), "a transmitter that never worked wrote a record.");

            signal.Restore(SignalDestination.To("colony-id", "Humans", new Vector3(2786.3f, 109.9f, 533.8f), 138.9f));
            JObject record = RoundTrip(saver.CaptureState());

            Rack(out ShipSignal reloaded);
            reloaded.gameObject.AddComponent<ShipSignalSaveable>().RestoreState(record);
            Assert.AreEqual(signal.Destination, reloaded.Destination, "the destination came back different after a reload.");

            signal.Restore(SignalDestination.Nowhere);
            Rack(out ShipSignal nowhere);
            nowhere.gameObject.AddComponent<ShipSignalSaveable>().RestoreState(RoundTrip(saver.CaptureState()));
            Assert.IsTrue(nowhere.Destination.Received, "a signal heard leading nowhere reloaded unheard, so it would be chosen again.");
            Assert.IsFalse(nowhere.Destination.HasDestination);

            Rack(out ShipSignal old);
            old.gameObject.AddComponent<ShipSignalSaveable>().RestoreState(null);
            Assert.IsFalse(old.Destination.Received, "a save without the record reloaded with a signal heard.");
        }

        // ─────────────────────────── The shipped prefabs ───────────────────────────

        [Test]
        public void TheShipCarriesItsSignalAndItsSaver()
        {
            var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            Assert.IsNotNull(ship, $"No ship at {ShipPrefabPath}.");

            var signal = ship.GetComponent<ShipSignal>();
            Assert.IsNotNull(signal, "the ship has no ShipSignal, so its transmitter hears nothing.");
            Assert.IsNotNull(ship.GetComponent<ShipSignalSaveable>(), "the ship's signal is never saved.");

            var so = new SerializedObject(signal);
            Assert.IsNotNull(so.FindProperty("crewFaction").objectReferenceValue, "the signal does not know who the crew are.");
            Assert.IsNotNull(so.FindProperty("relationships").objectReferenceValue, "the signal cannot tell a hostile town.");
            Assert.AreEqual(300f, so.FindProperty("minimumDistance").floatValue, 1e-3f);
        }

        [Test]
        public void TheTerminalHasACommsPageAndStaticWired()
        {
            var terminal = AssetDatabase.LoadAssetAtPath<GameObject>(TerminalPrefabPath);
            Assert.IsNotNull(terminal, $"No terminal at {TerminalPrefabPath}.");

            var screen = terminal.GetComponentInChildren<TerminalScreen>(true);
            var so = new SerializedObject(screen);
            Assert.AreEqual(TerminalConsole.PageCount, so.FindProperty("pages").arraySize, "a page has no root on the glass.");
            Assert.AreEqual(TerminalConsole.PageCount, so.FindProperty("tabs").arraySize, "a page has no tab.");
            for (int i = 0; i < TerminalConsole.PageCount; i++)
                Assert.IsNotNull(so.FindProperty("pages").GetArrayElementAtIndex(i).objectReferenceValue, $"page {i} is unwired.");

            Assert.IsNotNull(so.FindProperty("commsText").objectReferenceValue, "the COMMS page has nowhere to print.");
            Assert.IsNotNull(so.FindProperty("commsTab").objectReferenceValue, "the COMMS tab cannot be hidden.");
            var overlay = so.FindProperty("staticOverlay").objectReferenceValue as GameObject;
            Assert.IsNotNull(overlay, "there is no static to show while the transmitter is offline.");
            Assert.IsNotNull(overlay.GetComponentInChildren<TerminalStatic>(true), "the static does not move.");
            Assert.IsNotNull(so.FindProperty("staticText").objectReferenceValue, "the no-carrier line is unwired.");
        }

        // ─────────────────────────── Fixture ───────────────────────────

        /// <summary>
        /// A hull with two sockets, a fitted belly motor (0) and the transmitter (1) holding its burnt-out
        /// unit, and the signal beside the rack. Registered first, so ObjectiveWorld.Ship answers with it.
        /// </summary>
        private ShipPartRack Rack(out ShipSignal signal)
        {
            var root = Track(new GameObject("TestShip"));
            Socket(root, "Part_Belly_A", ShipPartKind.SmallMotor);
            Socket(root, "Part_Transmitter_A", ShipPartKind.Transmitter);

            var rack = root.AddComponent<ShipPartRack>();
            var rso = new SerializedObject(rack);
            rso.FindProperty("authoredInstalledMask").intValue = 0b01;
            rso.FindProperty("authoredBrokenMask").intValue = 0b10;
            rso.ApplyModifiedPropertiesWithoutUndo();
            typeof(ShipPartRack).GetMethod("Awake", Hidden).Invoke(rack, null);

            signal = root.AddComponent<ShipSignal>();

            List<ShipPartRack> racks = ActiveRacks();
            racks.RemoveAll(r => r == null);
            racks.Insert(0, rack);
            return rack;
        }

        private static void Socket(GameObject root, string name, ShipPartKind kind)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var so = new SerializedObject(go.AddComponent<ShipPartSocket>());
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TelemetrySnapshot Snapshot(int installedMask)
        {
            TelemetrySnapshot s = TelemetrySnapshot.Empty;
            s.PartKinds = new[] { ShipPartKind.SmallMotor, ShipPartKind.Transmitter };
            s.PartsInstalledMask = installedMask;
            return s;
        }

        private FactionDefinition Faction(string name, bool wandering = false, bool hostile = false)
        {
            var faction = Track(ScriptableObject.CreateInstance<FactionDefinition>());
            faction.factionName = name;
            faction.wandering = wandering;
            faction.defaultStance = hostile ? FactionRelationship.Hostile : FactionRelationship.Neutral;
            return faction;
        }

        private FactionRelationshipTable Table() => Track(ScriptableObject.CreateInstance<FactionRelationshipTable>());

        private static WorldSiteCatalog.TownEntry Town(string id, FactionDefinition faction, Vector3 position) =>
            new() { id = id, name = id, position = position, radius = 50f, faction = faction };

        private static FactionDefinition Load(string asset)
        {
            var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(FactionFolder + asset + ".asset");
            Assert.IsNotNull(faction, $"No faction at {FactionFolder}{asset}.asset.");
            return faction;
        }

        private T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        private static List<ShipPartRack> ActiveRacks() =>
            (List<ShipPartRack>)typeof(ShipPartRack)
                .GetField("active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private static JObject RoundTrip(object captured) =>
            captured == null ? null : JObject.Parse(JsonConvert.SerializeObject(captured, SaveSerializer.Settings));
    }
}
