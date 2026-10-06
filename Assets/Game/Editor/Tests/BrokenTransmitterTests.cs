// Guards the lander's burnt-out transmitter: the broken unit that starts seated in its socket, the
// fire it starts after the landing, and the extinguisher that puts it out.
//
// In Editor/ rather than beside the other EditMode tests because these touch Assembly-CSharp types,
// and an asmdef cannot reference Assembly-CSharp. Built by hand like ShipPartsTests: nothing runs
// Awake in EditMode, so the rack is woken explicitly where a test needs its authored state.
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    public class BrokenTransmitterTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        private const string ShipPrefabPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/PlayerShip.prefab";
        private const string ExtinguisherPrefabPath =
            "Assets/Game/Prefabs/Items/Artifacts/Gadgets/FireExtinguisher.prefab";

        private readonly List<UnityEngine.Object> spawned = new();

        private readonly ShipPartFireTuning tuning = new() { igniteDelay = 60f, startStrength = 0.2f, growSeconds = 40f };

        [TearDown]
        public void CleanUp()
        {
            foreach (UnityEngine.Object o in spawned)
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            spawned.Clear();
        }

        // ─────────────────────────── The broken unit in its socket ───────────────────────────

        [Test]
        public void ABrokenUnitBlocksFittingUntilItIsRemoved()
        {
            ShipPartRack rack = BrokenRack(out _);

            Assert.IsTrue(rack.IsBroken(0), "a new ship did not start with its burnt-out unit seated.");
            Assert.IsFalse(rack.Accepts(0, ShipPartKind.Transmitter),
                           "a working transmitter could be fitted on top of the burnt-out one.");
            Assert.IsFalse(rack.TryInstall(0, ShipPartKind.Transmitter), "the fit went through anyway.");

            Assert.IsTrue(rack.TryRemoveBroken(0), "the burnt-out unit could not be taken off.");
            Assert.IsFalse(rack.IsBroken(0), "the socket still reads broken after the unit was taken.");
            Assert.IsFalse(rack.TryRemoveBroken(0), "a second take of the same unit succeeded.");

            Assert.IsTrue(rack.TryInstall(0, ShipPartKind.Transmitter),
                          "the freed socket refused the working transmitter.");
        }

        [Test]
        public void ABrokenUnitIsNotAFittedModule()
        {
            ShipPartRack rack = BrokenRack(out _);

            Assert.IsFalse(rack.IsInstalled(0), "the burnt-out unit counts as fitted.");
            Assert.IsFalse(rack.IsComplete, "the ship reads airworthy with only a burnt-out transmitter.");

            rack.TryRemoveBroken(0);
            Assert.IsFalse(rack.IsComplete, "removing the broken unit made the ship airworthy.");

            rack.TryInstall(0, ShipPartKind.Transmitter);
            Assert.IsTrue(rack.IsComplete, "the working transmitter did not complete the ship.");
        }

        [Test]
        public void TheBrokenUnitSurvivesASaveAndAnOldSaveSeatsIt()
        {
            ShipPartRack rack = BrokenRack(out _);
            var saver = rack.gameObject.AddComponent<ShipPartsSaveable>();

            Assert.IsNull(saver.CaptureState(), "a ship at its authored state wrote a record.");

            rack.TryRemoveBroken(0);
            JObject removed = RoundTrip(saver.CaptureState());

            ShipPartRack reloaded = BrokenRack(out _);
            reloaded.gameObject.AddComponent<ShipPartsSaveable>().RestoreState(removed);
            Assert.IsFalse(reloaded.IsBroken(0), "a removed burnt-out unit came back after a reload.");

            // A save from before the broken field existed: the installed mask only.
            ShipPartRack old = BrokenRack(out _);
            old.TryRemoveBroken(0);
            old.gameObject.AddComponent<ShipPartsSaveable>().RestoreState(JObject.Parse("{\"installed\":0}"));
            Assert.IsTrue(old.IsBroken(0), "an old save without the field did not seat the burnt-out unit.");

            ShipPartRack fitted = BrokenRack(out _);
            fitted.gameObject.AddComponent<ShipPartsSaveable>().RestoreState(JObject.Parse("{\"installed\":1}"));
            Assert.IsTrue(fitted.IsInstalled(0) && !fitted.IsBroken(0),
                          "an old save with the transmitter fitted got a burnt-out one seated on top.");
        }

        // ─────────────────────────── The fire's rules ───────────────────────────

        [Test]
        public void TheFireCatchesOnlyAfterItIsArmed()
        {
            var s = new ShipPartFireState();

            s = ShipPartFireRules.Step(s, 500f, armed: false, tuning);
            Assert.AreEqual(ShipPartFirePhase.Dormant, s.phase, "it caught before the oxygen plant was running.");
            Assert.AreEqual(0f, s.armedSeconds, "the clock ran before the fire was armed.");

            s = ShipPartFireRules.Step(s, 59f, armed: true, tuning);
            Assert.AreEqual(ShipPartFirePhase.Dormant, s.phase, "it caught before its delay.");

            s = ShipPartFireRules.Step(s, 2f, armed: true, tuning);
            Assert.AreEqual(ShipPartFirePhase.Burning, s.phase, "it did not catch after its delay.");
            Assert.AreEqual(tuning.startStrength, s.strength, 1e-4f, "it did not start small.");

            s = ShipPartFireRules.Step(s, 1000f, armed: true, tuning);
            Assert.AreEqual(1f, s.strength, 1e-4f, "a fire left alone did not grow to, and stop at, full strength.");
        }

        [Test]
        public void ADousedFireGoesOutForGood()
        {
            var s = new ShipPartFireState { phase = ShipPartFirePhase.Burning, strength = 0.5f };

            s = ShipPartFireRules.Douse(s, 0.3f);
            Assert.IsTrue(s.IsBurning, "a partial douse put the fire out.");

            s = ShipPartFireRules.Douse(s, 0.3f);
            Assert.AreEqual(ShipPartFirePhase.Out, s.phase, "a fire knocked to zero is still burning.");

            s = ShipPartFireRules.Step(s, 10000f, armed: true, tuning);
            Assert.AreEqual(ShipPartFirePhase.Out, s.phase, "a fire that was put out lit again.");
        }

        [Test]
        public void TheBrokenUnitComesOutOnlyAfterItHasBurned()
        {
            ShipPartRack rack = BrokenRack(out BrokenShipPart unit);
            ShipPartFire fire = rack.GetComponent<ShipPartFire>();

            Assert.IsFalse(unit.TakeFor(), "a unit that has not burned yet came out of its socket.");
            StringAssert.Contains("Jammed", unit.Prompt, "the readout does not say it is jammed.");

            fire.Restore(new ShipPartFireState { phase = ShipPartFirePhase.Burning, strength = 0.6f });
            Assert.IsFalse(unit.TakeFor(), "a burning unit was taken off its cradle.");
            StringAssert.Contains("On fire", unit.Prompt, "the readout does not say it is on fire.");
            Assert.IsTrue(rack.IsBroken(0), "a refused take emptied the socket anyway.");

            fire.Douse(1f);
            StringAssert.Contains("take it off", unit.Prompt, "the readout does not offer the take once it is out.");
            Assert.IsTrue(unit.TakeFor(), "the unit could not be taken once the fire was out.");
            Assert.IsFalse(rack.IsBroken(0), "the taken unit is still in the socket.");
        }

        /// <summary>
        /// The defect: the fire burned with no flames on any machine. BodyFire's emitters do not
        /// play on awake; a burning body starts them through FlameLayers, and the first version of
        /// this fire only made the instance active.
        /// </summary>
        [Test]
        public void ABurningUnitShowsUprightFlames()
        {
            ShipPartRack rack = BrokenRack(out _);
            ShipPartFire fire = rack.GetComponent<ShipPartFire>();

            var firePoint = new GameObject("FirePoint").transform;
            firePoint.SetParent(rack.transform, false);
            firePoint.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.right);
            var fso = new SerializedObject(fire);
            fso.FindProperty("firePoint").objectReferenceValue = firePoint;
            fso.ApplyModifiedPropertiesWithoutUndo();

            fire.Restore(new ShipPartFireState { phase = ShipPartFirePhase.Burning, strength = 0.5f });

            // The flame half of Present only: the other half starts the burn loop, and FMOD has no
            // runtime outside play mode (Flamethrower.md, Gotchas).
            typeof(ShipPartFire).GetMethod("EnsureFlames", Hidden).Invoke(fire, null);
            typeof(ShipPartFire).GetMethod("PresentFlames", Hidden).Invoke(fire, new object[] { true });

            Transform flames = firePoint.Find("Flames");
            Assert.IsNotNull(flames, "a burning unit has no flames under its fire point.");

            ParticleSystem[] emitters = flames.GetComponentsInChildren<ParticleSystem>(true);
            Assert.IsNotEmpty(emitters, "the flames have no emitters.");
            foreach (ParticleSystem ps in emitters)
                Assert.IsTrue(ps.isEmitting, $"'{ps.name}' is not emitting: the fire burns with no flames.");

            Assert.Greater(Vector3.Dot(flames.up, Vector3.up), 0.99f,
                           "the flames took the socket's turn and burn sideways into the room.");
        }

        [Test]
        public void TheFireIsNotArmedUntilTheOxygenPlantIsBackAndRunning()
        {
            ShipPartRack rack = BrokenRack(out _);
            ShipPartFire fire = rack.GetComponent<ShipPartFire>();
            PropertyInfo armed = typeof(ShipPartFire).GetProperty("Armed", Hidden);

            var fixture = new GameObject("OxygenGenerator");
            fixture.transform.SetParent(rack.transform, false);
            var generator = fixture.AddComponent<OxygenGenerator>();
            var mount = fixture.AddComponent<OxygenPlantMount>();

            mount.Restore(true, Vector3.zero, Vector3.forward * 40f);
            Assert.IsFalse((bool)armed.GetValue(fire), "the fire was armed with the oxygen plant thrown out.");

            mount.Restore(false, Vector3.zero, Vector3.forward * 40f);
            Assert.IsFalse((bool)armed.GetValue(fire), "the fire was armed by a plant with no power.");

            FieldInfo plantField = typeof(OxygenGenerator).GetField("plant", Hidden);
            object plant = plantField.GetValue(generator);
            plant.GetType().GetField("Battery").SetValue(plant, 1f);
            plantField.SetValue(generator, plant);
            Assert.IsTrue((bool)armed.GetValue(fire), "the plant is home and running and the fire is not armed.");

            rack.TryRemoveBroken(0);
            Assert.IsFalse((bool)armed.GetValue(fire), "the fire was armed with no burnt-out unit in the socket.");
        }

        [Test]
        public void AHuskFizzlesOutOverItsLife()
        {
            Assert.AreEqual(1f, FizzlingHusk.SparkShare(0f, 15f), 1e-4f, "a fresh husk does not spark at full rate.");
            Assert.AreEqual(1f, FizzlingHusk.SparkShare(4f, 15f), 1e-4f, "the sparks fade before a third of its life.");
            Assert.Less(FizzlingHusk.SparkShare(12f, 15f), 0.5f, "the sparks have not died down near the end.");
            Assert.AreEqual(0f, FizzlingHusk.SparkShare(15f, 15f), 1e-4f, "a husk at the end of its life still sparks.");
        }

        [Test]
        public void TheFireSurvivesASave()
        {
            ShipPartRack rack = BrokenRack(out _);
            ShipPartFire fire = rack.GetComponent<ShipPartFire>();
            var saver = rack.gameObject.AddComponent<ShipPartFireSaveable>();

            Assert.IsNull(saver.CaptureState(), "an unlit fire with no clock wrote a record.");

            fire.Restore(new ShipPartFireState { phase = ShipPartFirePhase.Burning, strength = 0.42f, armedSeconds = 75f });
            JObject burning = RoundTrip(saver.CaptureState());

            ShipPartRack reloaded = BrokenRack(out _);
            reloaded.gameObject.AddComponent<ShipPartFireSaveable>().RestoreState(burning);
            ShipPartFireState back = reloaded.GetComponent<ShipPartFire>().State;
            Assert.AreEqual(ShipPartFirePhase.Burning, back.phase, "a burning fire reloaded unlit.");
            Assert.AreEqual(0.42f, back.strength, 1e-4f, "a burning fire reloaded at a different strength.");

            fire.Douse(1f);
            JObject outRecord = RoundTrip(saver.CaptureState());
            ShipPartRack again = BrokenRack(out _);
            again.gameObject.AddComponent<ShipPartFireSaveable>().RestoreState(outRecord);
            Assert.AreEqual(ShipPartFirePhase.Out, again.GetComponent<ShipPartFire>().State.phase,
                            "a fire put out reloaded able to burn again.");
        }

        // ─────────────────────────── The extinguisher ───────────────────────────

        [Test]
        public void TheExtinguisherHoldsAFewSecondsOfFogAndDoesNotRefillItself()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ExtinguisherPrefabPath);
            Assert.IsNotNull(prefab, $"No extinguisher at {ExtinguisherPrefabPath}.");
            Assert.IsNotNull(prefab.GetComponent<FireExtinguisherArtifact>(), "the prefab has no FireExtinguisherArtifact.");

            var tank = prefab.GetComponent<SupplyReservoir>();
            Assert.IsNotNull(tank, "the extinguisher has no tank, so its charge would be endless.");

            var so = new SerializedObject(tank);
            float drain = so.FindProperty("drainPerSecond").floatValue;
            Assert.Greater(drain, 0f, "the tank never drains.");
            Assert.That(1f / drain, Is.InRange(3f, 12f), "a full extinguisher is not a few seconds of fog.");
            Assert.AreEqual(0f, so.FindProperty("refillPerSecond").floatValue, "the extinguisher refills itself.");

            GameObject copy = UnityEngine.Object.Instantiate(prefab);
            spawned.Add(copy);
            var live = copy.GetComponent<SupplyReservoir>();
            live.SetCharge(1f);
            live.Tick(1f, drawing: true);
            Assert.AreEqual(1f - drain, live.Charge, 1e-3f, "a second of fog did not cost a second of charge.");
            live.Tick(100f, drawing: false);
            Assert.AreEqual(1f - drain, live.Charge, 1e-3f, "the tank filled back up on its own.");
        }

        [Test]
        public void TheShipStartsWithItsBurntOutTransmitterAndAFire()
        {
            var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            Assert.IsNotNull(ship, $"No ship at {ShipPrefabPath}.");

            var rack = ship.GetComponent<ShipPartRack>();
            int index = -1;
            for (int i = 0; i < rack.Sockets.Count; i++)
                if (rack.Sockets[i].Kind == ShipPartKind.Transmitter) index = i;

            Assert.GreaterOrEqual(index, 0, "the ship has no transmitter socket.");
            Assert.AreNotEqual(0, rack.AuthoredBrokenMask & (1 << index),
                               "the transmitter socket is not authored with its burnt-out unit.");
            Assert.AreEqual(0, rack.AuthoredBrokenMask & ~(1 << index),
                            "a socket other than the transmitter's is authored broken.");
            Assert.IsNotNull(ship.GetComponent<ShipPartFire>(), "the ship has no fire for its broken unit.");
            Assert.IsNotNull(ship.GetComponent<ShipPartFireSaveable>(), "the ship's fire is never saved.");
            Assert.IsNotNull(ship.GetComponentInChildren<BrokenShipPart>(true), "nothing in the socket can be taken off.");
        }

        // ─────────────────────────── Fixture ───────────────────────────

        /// <summary>
        /// One transmitter socket authored broken, a fire beside the rack, and the takeable unit in
        /// the socket — the lander's arrangement, small. The rack is woken so it holds its authored
        /// broken unit, as a newly placed ship does.
        /// </summary>
        private ShipPartRack BrokenRack(out BrokenShipPart unit)
        {
            var root = new GameObject("TestShip");
            spawned.Add(root);

            var socketGo = new GameObject("Part_Transmitter_A");
            socketGo.transform.SetParent(root.transform, false);
            var socket = socketGo.AddComponent<ShipPartSocket>();
            var sso = new SerializedObject(socket);
            sso.FindProperty("kind").enumValueIndex = (int)ShipPartKind.Transmitter;

            var unitGo = new GameObject("BrokenUnit");
            unitGo.transform.SetParent(socketGo.transform, false);
            // No husk prefab: in EditMode there is no world to spawn into, and the take's rule is
            // what is under test, not the husk.
            unit = unitGo.AddComponent<BrokenShipPart>();

            sso.FindProperty("brokenUnit").objectReferenceValue = unitGo;
            sso.ApplyModifiedPropertiesWithoutUndo();

            var rack = root.AddComponent<ShipPartRack>();
            var rso = new SerializedObject(rack);
            rso.FindProperty("authoredBrokenMask").intValue = 1;
            rso.ApplyModifiedPropertiesWithoutUndo();

            var fire = root.AddComponent<ShipPartFire>();
            var fso = new SerializedObject(fire);
            fso.FindProperty("socket").objectReferenceValue = socket;
            fso.ApplyModifiedPropertiesWithoutUndo();

            typeof(ShipPartRack).GetMethod("Awake", Hidden).Invoke(rack, null);
            return rack;
        }

        private static JObject RoundTrip(object captured) =>
            JObject.Parse(JsonConvert.SerializeObject(captured, SaveSerializer.Settings));
    }
}
