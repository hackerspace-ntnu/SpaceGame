// Assets/Game/Editor/Vehicles/NpcOrnithopterBuilder.cs
// Builds the NPC craft as a Prefab Variant of the player's DuneOrnithopter: the same model, wing rig,
// wing animator and audio, with the player's flight motor, mount, steering and every saver removed,
// and the simple NPC flight added — FlyingRigidbodyMotor (banking on), NpcOrnithopterWings (feeds the
// wing animator), one-seat VesselSeats and NpcAviator. Re-runnable: it rebuilds from the base every time.
// DuneOrnithopter.prefab itself is hand-owned and never written here.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core.Persistence;
using SpaceGame.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Ornithopter;

namespace SpaceGame.EditorTools
{
    public static class NpcOrnithopterBuilder
    {
        public const string BasePath = "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/DuneOrnithopter.prefab";
        public const string PrefabPath = "Assets/Game/Prefabs/Agents/Vehicles/Aircraft/NpcOrnithopter.prefab";
        private const string SeatName = "SEAT_Cradle";

        // The NPC craft's flight (NpcFlightPlanTests fly the plan against exactly these two).
        public const float CruiseSpeed = 25f;     // Spike 5.2a E4 cruised the player's craft at 22-28 m/s
        public const float Acceleration = 8f;
        private const float FaceRotateSpeed = 1.5f;
        private const float BankPerTurnRate = 0.5f;
        private const float MaxBank = 35f;
        private const float MaxPitch = 40f;

        // A dead pilot's body and a landed pilot are both put on NavMesh within this; matches
        // NpcPassenger.dismountSampleDistance.
        private const float SeatNavMeshReach = 6f;

        // Dependents before what they depend on: Unity refuses to remove a component another requires.
        // OrnithopterSaveable requires the flight motor, and MountNetworkSync/SteerModule require MountModule.
        private static readonly Type[] Removed =
        {
            typeof(MountNetworkSync), typeof(SteerModule), typeof(MountModule), typeof(OrnithopterFlightMotor),
        };

        [MenuItem("Tools/SpaceGame/Vehicles/Build NPC Ornithopter")]
        public static void Build()
        {
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
            if (basePrefab == null)
            {
                Debug.LogError($"[NpcOrnithopterBuilder] No base craft at {BasePath}.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
            try
            {
                foreach (MonoBehaviour saver in instance.GetComponents<MonoBehaviour>().Where(c => c is ISaveable).ToArray())
                    UnityEngine.Object.DestroyImmediate(saver);
                if (instance.TryGetComponent(out SaveableEntity entity)) UnityEngine.Object.DestroyImmediate(entity);
                foreach (Type type in Removed)
                    if (instance.TryGetComponent(type, out Component c)) UnityEngine.Object.DestroyImmediate(c);

                AddMotor(instance);
                instance.AddComponent<NpcOrnithopterWings>();
                AddSeat(instance);
                AddAviator(instance);
                PointControllerAtMotor(instance);

                PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }

            // The worker, never SyncMenu (a modal dialog parks the chain). Then re-serialize so the
            // NetworkObject's hash reaches the YAML, as SkyFleetBuilder does.
            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            AssetDatabase.ForceReserializeAssets(new[] { PrefabPath });
            Verify();
        }

        private static void AddMotor(GameObject craft)
        {
            var motor = craft.AddComponent<FlyingRigidbodyMotor>();
            var so = new SerializedObject(motor);
            SerializedFields.Set(so, "body", craft.GetComponent<Rigidbody>());
            SerializedFields.SetFloat(so, "maxSpeed", CruiseSpeed);
            SerializedFields.SetFloat(so, "acceleration", Acceleration);
            SerializedFields.SetFloat(so, "deceleration", Acceleration);
            SerializedFields.SetFloat(so, "faceRotateSpeed", FaceRotateSpeed);
            SerializedFields.SetBool(so, "kinematicHull", false);      // a dynamic body: collisions end a flight
            SerializedFields.SetBool(so, "altitudeHold", false);       // NpcAviator owns the height
            SerializedFields.SetBool(so, "gravityWhenIdle", false);
            SerializedFields.SetFloat(so, "bankPerTurnRate", BankPerTurnRate);
            SerializedFields.SetFloat(so, "maxBank", MaxBank);
            SerializedFields.SetBool(so, "pitchAlongPath", true);
            SerializedFields.SetFloat(so, "maxPitch", MaxPitch);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSeat(GameObject craft)
        {
            Transform seat = craft.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == SeatName)
                ?? throw new InvalidOperationException($"{BasePath} has no '{SeatName}' marker to seat the NPC pilot on.");
            var seats = craft.AddComponent<VesselSeats>();
            var so = new SerializedObject(seats);
            SerializedProperty list = so.FindProperty("seats");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = seat;
            SerializedFields.SetVector3(so, "seatOffset", Vector3.zero);
            SerializedFields.SetFloat(so, "navMeshReach", SeatNavMeshReach);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Only flying into the world ends an NPC flight: the layers vision treats as solid geometry.
        private static void AddAviator(GameObject craft)
        {
            var so = new SerializedObject(craft.AddComponent<NpcAviator>());
            SerializedFields.SetInt(so, "crashMask", PerceptionModule.SolidGeometryLayers);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // The base leaves MotorComponent empty (auto-resolved); named explicitly so the NPC craft can
        // never resolve anything but the simple motor.
        private static void PointControllerAtMotor(GameObject craft)
        {
            var so = new SerializedObject(craft.GetComponent<AgentController>());
            SerializedFields.Set(so, "MotorComponent", craft.GetComponent<FlyingRigidbodyMotor>());
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Read the written prefab back off disk and fail loudly on anything the build did not stick.</summary>
        public static void Verify()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException($"{PrefabPath} was not written.");
            if (prefab.GetComponent<OrnithopterFlightMotor>() != null || prefab.GetComponent<MountModule>() != null ||
                prefab.GetComponent<SaveableEntity>() != null || prefab.GetComponents<MonoBehaviour>().Any(c => c is ISaveable))
                throw new InvalidOperationException($"{PrefabPath} still carries the player's motor, mount or a saver: the removed-component overrides did not stick.");
            if (prefab.GetComponent<NpcAviator>() == null || prefab.GetComponent<FlyingRigidbodyMotor>() == null ||
                !(prefab.GetComponent<IOrnithopterFlightState>() is NpcOrnithopterWings) ||
                prefab.GetComponent<VesselSeats>()?.Capacity != 1)
                throw new InvalidOperationException($"{PrefabPath} is missing its motor, wings presenter, aviator or its one seat.");
            if (new SerializedObject(prefab.GetComponent<NpcAviator>()).FindProperty("crashMask").intValue == ~0)
                throw new InvalidOperationException($"{PrefabPath}'s NpcAviator crashMask was never written: every layer would end a flight.");
            if (!NetworkPrefabRegistrar.IsRegistered(PrefabPath))
                throw new InvalidOperationException($"{PrefabPath} is not in the network prefab list.");
        }
    }
}
