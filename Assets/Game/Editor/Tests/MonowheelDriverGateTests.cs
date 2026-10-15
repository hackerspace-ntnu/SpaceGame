using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.Monowheel;

namespace SpaceGame.EditorTools
{
    /// The two things a Strider monowheel does when nobody is at its tiller: its column brain is parked
    /// (MonowheelDriverGate), and a killed wheel stops dead and cannot be mounted (MonowheelWreck).
    public class MonowheelDriverGateTests
    {
        private readonly List<Object> junk = new();

        [TearDown]
        public void TearDown() { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); junk.Clear(); }

        private (MonowheelDriverGate gate, FormationModule formation, GoalTravelModule travel) NewGate()
        {
            var go = new GameObject("Wheel");
            junk.Add(go);
            var formation = go.AddComponent<FormationModule>();
            var travel = go.AddComponent<GoalTravelModule>();
            var gate = go.AddComponent<MonowheelDriverGate>();
            var so = new SerializedObject(gate);
            SerializedProperty modules = so.FindProperty("drivenModules");
            modules.arraySize = 2;
            modules.GetArrayElementAtIndex(0).objectReferenceValue = formation;
            modules.GetArrayElementAtIndex(1).objectReferenceValue = travel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return (gate, formation, travel);
        }

        [Test]
        public void NoRiderAndNoPlayer_ParksTheColumnBrain()
        {
            var (gate, formation, travel) = NewGate();
            gate.Tick(hasNpcRider: false, playerMounted: false);
            Assert.IsFalse(formation.enabled);
            Assert.IsFalse(travel.enabled);
            Assert.IsTrue(gate.IsParked);
        }

        [Test]
        public void ARiderAboard_GivesItBack()
        {
            var (gate, formation, travel) = NewGate();
            gate.Tick(false, false);
            gate.Tick(hasNpcRider: true, playerMounted: false);
            Assert.IsTrue(formation.enabled);
            Assert.IsTrue(travel.enabled);
            Assert.IsFalse(gate.IsParked);
        }

        [Test]
        public void WhileAPlayerRides_TheGateStandsAside_ForMountModulesOwnSuppression()
        {
            var (gate, formation, _) = NewGate();
            formation.enabled = false;   // MountModule's suppression, recorded on its side
            gate.Tick(hasNpcRider: false, playerMounted: true);
            Assert.IsFalse(gate.IsParked, "the gate must not record what MountModule switched off");
            formation.enabled = true;    // MountModule's restore on dismount
            gate.Tick(false, playerMounted: true);
            Assert.IsTrue(formation.enabled, "nothing is parked while a player is mounted");
        }

        [Test]
        public void AfterAPlayerStepsOff_ARiderlessWheelIsParkedAgain()
        {
            var (gate, formation, travel) = NewGate();
            gate.Tick(hasNpcRider: false, playerMounted: false);
            Assert.IsFalse(formation.enabled);
            gate.Tick(false, false);
            Assert.IsFalse(formation.enabled);
            Assert.IsFalse(travel.enabled);
        }

        [Test]
        public void AModuleSwitchedOffByOthers_StaysOff()
        {
            var (gate, formation, travel) = NewGate();
            travel.enabled = false;
            gate.Tick(false, false);
            gate.Tick(true, false);
            Assert.IsTrue(formation.enabled);
            Assert.IsFalse(travel.enabled, "the gate gives back only what it took");
        }

        [Test]
        public void AWreck_StopsTheMotor_AndTakesTheSaddleAway_AndARepairGivesThemBack()
        {
            var go = new GameObject("Wheel");
            junk.Add(go);
            go.AddComponent<Rigidbody>();
            var motor = go.AddComponent<MonowheelMotor>();
            go.AddComponent<HealthComponent>();
            var mount = go.AddComponent<MountModule>();
            var wreck = go.AddComponent<MonowheelWreck>();

            wreck.Wreck();
            Assert.IsFalse(motor.enabled, "the motor would keep steering towards its last destination");
            Assert.IsFalse(mount.enabled, "a wreck must not offer its saddle");
            Assert.AreEqual(0f, motor.Speed);

            wreck.Repair();
            Assert.IsTrue(motor.enabled);
            Assert.IsTrue(mount.enabled);
        }
    }
}
