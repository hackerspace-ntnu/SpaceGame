// Builds Assets/Prefabs/agents/creatures/HorseRobot.prefab from
// Assets/Art/Models/Creatures/Robotic/Horse/horse_robot.fbx.
//
// The FBX arrives WITH its rig -- HORSE_Rig, a Root plus four Coxa/Hip/Knee/Ankle/Foot chains, a
// neck ending in a head and jaw, and a tail -- because `HorseLocomotion` measures that hierarchy
// live at Initialise and poses it every frame. This script does not build the articulation; it
// wires up everything around it:
//
//   * The prefab origin dropped onto the HIP PLANE. Ride height is `body.y - meanFootY`, so a root
//     left on the soles makes that zero, pins the body at hoof level and asks every leg to reach
//     the ground from there.
//   * Collision, measured off the meshes rather than hardcoded, and parented to the joints so the
//     boxes ride the legs as the solver moves them.
//   * Locomotion, driver, spine motion, and the mount rig that makes it rideable. That last part
//     is PREFAB WIRING, not code: `LeggedDriver` already implements `IRiderControllable`, so a
//     `MountModule` + `SteerModule` pair is the whole of it.
//
// The model is generated from Assets/Art/Models/_Source~/models/creatures/horse_robot.py and exported by
// horse_robot_export.py -- see Assets/Art/Models/_Source~/models/creatures/horse_robot_BUILD.md.
//
// Model orientation: authored -Y forward in Blender, which the default FBX axis conversion lands on
// Unity's +Z. There is deliberately no yaw correction here.
//
// Re-run from: Tools ▸ Creatures ▸ Build Horse Robot Prefab
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Creatures;
using SpaceGame.Creatures.Horse;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class HorseBuilder
    {
        private const string ModelPath =
            "Assets/Art/Models/Creatures/Robotic/Horse/horse_robot.fbx";
        private const string PrefabPath = "Assets/Prefabs/agents/creatures/HorseRobot.prefab";

        private static readonly string[] LegIds = { "FL", "FR", "HL", "HR" };

        /// Body meshes that get a collision box. Anything not listed is decoration.
        private static readonly (string mesh, string col)[] BodyBoxes =
        {
            ("Mesh_Horse_Barrel", "COL_Barrel"),
            ("Mesh_Horse_Head", "COL_Head"),
            ("Mesh_Horse_NeckBase", "COL_NeckBase"),
        };

        /// Limb meshes get a box each, named for the joint they hang off.
        private static readonly (string suffix, string joint)[] LimbBoxes =
        {
            ("Upper", "Hip"),
            ("Lower", "Knee"),
            ("Cannon", "Ankle"),
        };

        [MenuItem("Tools/Creatures/Build Horse Robot Prefab")]
        public static void Build()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Horse] No model at {ModelPath}. Run " +
                               "Assets/Art/Models/_Source~/models/creatures/horse_robot_export.py first.");
                return;
            }

            var root = new GameObject("HorseRobot");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.transform.SetParent(root.transform, false);

            Dictionary<string, Transform> parts = root.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());

            Transform armature = WalkerRig.FindArmature(root.transform);
            if (armature == null || !parts.ContainsKey("Hip_FL"))
            {
                Debug.LogError("[Horse] The model has no leg rig -- expected bones named " +
                               "Coxa_/Hip_/Knee_/Ankle_/Foot_<id>. Was the FBX exported with " +
                               "object_types including ARMATURE?");
                Object.DestroyImmediate(root);
                return;
            }

            DropModelOntoHips(root, instance);

            int boxes = 0;
            foreach ((string mesh, string col) in BodyBoxes) boxes += AddBox(parts, mesh, col) ? 1 : 0;
            foreach (string id in LegIds)
            {
                foreach ((string suffix, string joint) in LimbBoxes)
                    boxes += AddLimbBox(parts, id, suffix, joint) ? 1 : 0;
            }

            Transform seat = BuildSeat(root, parts);
            MountModule mount = WireLocomotion(root, armature, seat);
            BuildMountStation(root, parts, mount);

            // Read anything wanted for the report BEFORE the scratch hierarchy goes away: a destroyed
            // Transform throws on `.name` rather than returning null.
            string rigName = armature.name;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Horse] Built {PrefabPath}: {boxes} collision boxes, " +
                      $"{LegIds.Length} legs on {rigName}.");
        }

        /// Sink the model so the prefab's own origin sits on the hip plane rather than on the hooves.
        ///
        /// Measured off the rig rather than typed in, so re-proportioning the model re-derives it. On
        /// this machine the two pairs of hips are at DIFFERENT heights -- that is the point of it -- so
        /// this is their mean, which is where a horse's back is anyway.
        private static void DropModelOntoHips(GameObject root, GameObject instance)
        {
            float hipSum = 0f;
            int hips = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Hip_")) continue;
                hipSum += t.position.y;
                hips++;
            }
            if (hips == 0) return;

            float soleY = float.MaxValue;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
                soleY = Mathf.Min(soleY, r.bounds.min.y);

            float rideHeight = hipSum / hips - soleY;
            instance.transform.localPosition = new Vector3(0f, -rideHeight, 0f);
            Debug.Log($"[Horse] Origin set on the hip plane: ride height {rideHeight:F3} m.");
        }

        /// A box matching one mesh, parented to that mesh so it rides whatever bone moves it.
        private static bool AddBox(Dictionary<string, Transform> parts, string meshName, string colName)
        {
            if (!parts.TryGetValue(meshName, out Transform t))
            {
                Debug.LogWarning($"[Horse] No mesh '{meshName}'; skipping {colName}.");
                return false;
            }

            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            // Local bounds, not world: parented to the mesh, the collider inherits its transform, so
            // the box stays right as the leg swings instead of being frozen at the rest pose.
            var box = new GameObject(colName).transform;
            box.SetParent(t, false);
            BoxCollider bc = box.gameObject.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = mf.sharedMesh.bounds.size;
            return true;
        }

        /// A limb's collision box, hung DIRECTLY off its joint rather than off the mesh, and found by
        /// walking the joint's own subtree rather than by name -- which mesh does this bone move is the
        /// question that actually matters, and it survives Blender's `.001` duplicate suffixes.
        private static bool AddLimbBox(Dictionary<string, Transform> parts, string id, string suffix,
                                       string joint)
        {
            if (!parts.TryGetValue($"{joint}_{id}", out Transform bone)) return false;

            Transform mesh = FindLimbMesh(bone, suffix);
            if (mesh == null) return false;

            var mf = mesh.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            var box = new GameObject($"COL_{joint}_{id}").transform;
            box.SetParent(bone, false);
            box.localPosition = mesh.localPosition;
            box.localRotation = mesh.localRotation;
            box.localScale = mesh.localScale;

            BoxCollider bc = box.gameObject.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = mf.sharedMesh.bounds.size;
            return true;
        }

        private static Transform FindLimbMesh(Transform joint, string suffix)
        {
            string needle = $"_{suffix}";
            foreach (MeshFilter mf in joint.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name.Contains(needle)) return mf.transform;
            return null;
        }

        /// Where a rider sits: on the saddle, derived from the saddle mesh's own bounds so
        /// re-proportioning the model moves the seat with it.
        private static Transform BuildSeat(GameObject root, Dictionary<string, Transform> parts)
        {
            var seat = new GameObject("SEAT_Saddle").transform;
            seat.SetParent(root.transform, false);

            if (parts.TryGetValue("Mesh_Horse_Saddle", out Transform saddle))
            {
                var mf = saddle.GetComponent<MeshFilter>();
                Vector3 top = mf != null
                    ? saddle.TransformPoint(new Vector3(mf.sharedMesh.bounds.center.x,
                                                        mf.sharedMesh.bounds.max.y,
                                                        mf.sharedMesh.bounds.center.z))
                    : saddle.position;
                seat.position = top;
            }
            else
            {
                seat.localPosition = new Vector3(0f, 0.6f, 0f);
            }
            return seat;
        }

        private static MountModule WireLocomotion(GameObject root, Transform armature, Transform seat)
        {
            HorseLocomotion loco = root.AddComponent<HorseLocomotion>();
            var so = new SerializedObject(loco);
            Set(so, "armatureRoot", armature);
            Set(so, "body", root.transform);
            // Joint travel. The coxa's range is what lets a planted hoof stay planted while the body
            // turns, so it is generous even though nothing here strides off it.
            SetFloat(so, "yawRange", 35f);
            SetFloat(so, "hipRange", 55f);
            SetFloat(so, "kneeRange", 70f);
            SetFloat(so, "ankleRange", 45f);
            SetFloat(so, "rollRange", 25f);
            // Seconds a hoof spends in the air at full speed. Top speed is DERIVED from this and the
            // stride, so it is the one number that sets how fast the machine can go.
            SetFloat(so, "stepDuration", 0.38f);
            SetFloat(so, "stepClearance", 0.10f);
            SetFloat(so, "obstacleClearance", 0.25f);
            // Every layer. Left unset this serialises as 0, and a walker whose ground mask matches
            // nothing never snaps down and never places a foothold -- which looks exactly like a rig
            // fault. The machine's own colliders are rejected by WalkerGround regardless of mask.
            SetInt(so, "groundMask", ~0);
            SetFloat(so, "rayStartAbove", 3f);
            SetFloat(so, "rayLength", 60f);
            SetBool(so, "snapToGroundOnStart", true);
            SetBool(so, "autoCalibrateRideHeight", true);
            SetFloat(so, "heightSmooth", 6f);
            SetFloat(so, "fallGravity", 20f);
            SetFloat(so, "maxFallSpeed", 40f);
            SetFloat(so, "footGroundTolerance", 0.2f);
            SetFloat(so, "fallThresholdFraction", 0.5f);
            SetBool(so, "drawGizmos", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            HorseDriver driver = root.AddComponent<HorseDriver>();
            var dso = new SerializedObject(driver);
            // Asked for well above what the legs can carry on purpose: the locomotion clamps to its own
            // derived MaxSpeed, so this only has to be high enough not to be the binding constraint.
            SetFloat(dso, "moveSpeed", 8f);
            SetFloat(dso, "turnSpeed", 90f);
            SetFloat(dso, "acceleration", 3f);
            SetFloat(dso, "defaultStopDistance", 3f);
            SetFloat(dso, "cornerArriveRadius", 3f);
            SetFloat(dso, "navMeshSampleDistance", 8f);
            dso.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<HorseSpineMotion>();

            // Kinematic, gravity off. The locomotion writes the transform directly (invariant I4), so a
            // dynamic body would fight it every frame and win. It is here because without a Rigidbody
            // every collider on this machine is a STATIC one, and moving static colliders makes PhysX
            // rebuild its broadphase tree each frame -- and because it is what makes the machine
            // visible to the OverlapSphere and layer queries the agent systems use.
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            MountModule mount = root.AddComponent<MountModule>();
            var mso = new SerializedObject(mount);
            Set(mso, "seatPoint", seat);
            mso.ApplyModifiedPropertiesWithoutUndo();

            // SteerModule carries [RequireComponent(MountModule, AgentController)], so this brings an
            // AgentController along with it. Get-or-add below rather than a second AddComponent, or
            // the prefab ends up with two and which one answers GetComponent is a coin toss.
            root.AddComponent<SteerModule>();

            Ensure<AgentController>(root);
            Ensure<AgentTargeting>(root);

            WanderModule wander = root.AddComponent<WanderModule>();
            var wso = new SerializedObject(wander);
            SetInt(wso, "priority", ModulePriority.Fallback);
            SetBool(wso, "limitWanderRadius", false);
            SetFloat(wso, "freeRoamRadius", 60f);
            SetFloat(wso, "sampleDistance", 12f);
            SetFloat(wso, "minDestinationDistance", 10f);
            SetFloat(wso, "stopDistance", 3f);
            SetFloat(wso, "minWaitTime", 3f);
            SetFloat(wso, "maxWaitTime", 9f);
            wso.ApplyModifiedPropertiesWithoutUndo();

            return mount;
        }

        /// The interaction point a player walks up to in order to mount, alongside the saddle.
        private static void BuildMountStation(GameObject root, Dictionary<string, Transform> parts,
                                              MountModule mount)
        {
            var go = new GameObject("DOOR_MountStation");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.1f, 0f);

            BoxCollider trigger = go.AddComponent<BoxCollider>();
            trigger.size = new Vector3(2.4f, 2.4f, 2.4f);
            trigger.isTrigger = true;

            MountStation station = go.AddComponent<MountStation>();
            var so = new SerializedObject(station);
            Set(so, "mount", mount);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Add a component only if a [RequireComponent] attribute has not already brought one along.
        private static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // Private [SerializeField] fields are not reachable from an editor script any other way, and
        // making them public purely so this could set them would widen the runtime API for a build-time
        // convenience. A missing name warns loudly rather than silently doing nothing.
        private static void Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.objectReferenceValue = value;
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.floatValue = value;
        }

        private static void SetInt(SerializedObject so, string field, int value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.intValue = value;
        }

        private static void SetBool(SerializedObject so, string field, bool value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.boolValue = value;
        }

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[Horse] {so.targetObject.GetType().Name} has no serialized field " +
                                 $"'{field}' -- it was renamed; this value is unset.");
            return p;
        }
    }
}
