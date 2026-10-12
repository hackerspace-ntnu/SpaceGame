// A small lander for EditMode tests of the transmitter and what hangs off it: a fitted belly motor and the
// transmitter socket, woken and registered so ObjectiveWorld.Ship answers with it. Shared by
// ShipSignalTests and DishTowerQuestTests. Nothing runs Awake or OnEnable in EditMode, so both are done
// here by hand, and the registry is cleaned by Forget.
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Tests
{
    internal static class TestHulls
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>Socket bit of the transmitter on <see cref="TransmitterRack"/>.</summary>
        public const int TransmitterBit = 0b10;

        /// <summary>
        /// Sockets 0 (a fitted belly motor) and 1 (the transmitter, holding its burnt-out unit when
        /// <paramref name="broken"/>), a <see cref="ShipSignal"/> beside the rack, registered first.
        /// </summary>
        public static ShipPartRack TransmitterRack(List<Object> spawned, bool broken, out ShipSignal signal)
        {
            var root = new GameObject("TestShip");
            spawned.Add(root);
            Socket(root, "Part_Belly_A", ShipPartKind.SmallMotor);
            Socket(root, "Part_Transmitter_A", ShipPartKind.Transmitter);

            var rack = root.AddComponent<ShipPartRack>();
            var rso = new SerializedObject(rack);
            rso.FindProperty("authoredInstalledMask").intValue = 0b01;
            rso.FindProperty("authoredBrokenMask").intValue = broken ? TransmitterBit : 0;
            rso.ApplyModifiedPropertiesWithoutUndo();
            typeof(ShipPartRack).GetMethod("Awake", Hidden).Invoke(rack, null);

            signal = root.AddComponent<ShipSignal>();

            List<ShipPartRack> racks = ActiveRacks();
            racks.RemoveAll(r => r == null);
            racks.Insert(0, rack);
            return rack;
        }

        /// <summary>Takes this fixture's racks back out of the registry. Call from TearDown.</summary>
        public static void Forget(List<Object> spawned) =>
            ActiveRacks().RemoveAll(r => r == null || spawned.Contains(r.gameObject));

        private static void Socket(GameObject root, string name, ShipPartKind kind)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var so = new SerializedObject(go.AddComponent<ShipPartSocket>());
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static List<ShipPartRack> ActiveRacks() =>
            (List<ShipPartRack>)typeof(ShipPartRack)
                .GetField("active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    }
}
