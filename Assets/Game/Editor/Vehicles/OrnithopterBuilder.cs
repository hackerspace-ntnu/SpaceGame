// Builds Assets/Prefabs/agents/vehicle/DuneOrnithopter.prefab from
// Assets/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx.
//
// The FBX arrives WITH its rig -- Arm_DuneOrnithopter, 30 bones -- because OrnithopterWingRig
// walks that hierarchy live at Initialise and OrnithopterWingAnimator poses it every frame. This
// script does not build the articulation; it wires up everything around it:
//
//   * The prefab origin dropped onto the CRADLE, so the craft pitches and rolls about roughly
//     where the pilot's chest is. Rotating about the model origin instead swings the rider around
//     the fuselage like a fairground ride.
//   * The seat, pitched 90 degrees so the rider lies PRONE and face-down in the cradle, which is
//     the whole posture this machine was designed around.
//   * Collision, measured off the meshes rather than hardcoded.
//   * The flight motor, the mount rig and a camera boom sized for a 10 m machine.
//
// The model is generated from Assets/Art/Models/_Source~/models/vehicles/dune_ornithopter.py and exported by
// dune_ornithopter_export.py -- see Assets/Art/Models/_Source~/models/vehicles/dune_ornithopter_BUILD.md.
//
// Model orientation: authored -Y forward in Blender, which the default FBX axis conversion lands
// on Unity's +Z. There is deliberately no yaw correction here.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Dune Ornithopter Prefab
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Vehicles.Ornithopter;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class OrnithopterBuilder
    {
        private const string ModelPath =
            "Assets/Art/Models/Vehicles/Ornithopter/dune_ornithopter.fbx";
        private const string PrefabPath =
            "Assets/Prefabs/agents/vehicle/DuneOrnithopter.prefab";

        /// Meshes that get a collision box. Everything else -- wings, cloth, gears -- is decoration:
        /// a 10 m wingspan of collider would snag on terrain the craft should be flying past.
        private static readonly (string mesh, string col)[] HullBoxes =
        {
            ("Mesh_Fuselage_Core", "COL_Fuselage"),
            ("Mesh_Fuselage_Nose", "COL_Nose"),
            ("Mesh_Fuselage_Boom", "COL_Boom"),
            ("Mesh_Cradle_Pad", "COL_Cradle"),
        };

        /// The six panels that must arrive skinned. A plain MeshRenderer here means the wings will
        /// never deform and the whole animator is wasted -- worth failing the build over.
        private static readonly string[] SkinnedPanels =
        {
            "Mesh_Wing_L_Frame", "Mesh_Wing_L_Web",
            "Mesh_Wing_R_Frame", "Mesh_Wing_R_Web",
            "Mesh_TailFan_Frame", "Mesh_TailFan_Web",
        };

        [MenuItem("Tools/Vehicles/Build Dune Ornithopter Prefab")]
        public static void Build()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[Ornithopter] No model at {ModelPath}. Run " +
                               "Assets/Art/Models/_Source~/models/vehicles/dune_ornithopter_export.py first.");
                return;
            }

            var root = new GameObject("DuneOrnithopter");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.transform.SetParent(root.transform, false);
            instance.name = "Model";

            Dictionary<string, Transform> parts = root.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());

            if (!parts.ContainsKey("Bone_Shoulder_L") || !parts.ContainsKey("Bone_Cradle"))
            {
                Debug.LogError("[Ornithopter] The model has no rig -- expected Bone_Shoulder_L/R, " +
                               "Bone_Digit_*, Bone_Cradle. Was the FBX exported with object_types " +
                               "including ARMATURE?");
                Object.DestroyImmediate(root);
                return;
            }

            if (!VerifySkinning(root))
            {
                Object.DestroyImmediate(root);
                return;
            }

            float span = MeasureSpan(root);
            DropOriginOntoCradle(root, instance, parts);

            int boxes = HullBoxes.Count(b => AddBox(parts, b.mesh, b.col));

            Transform seat = BuildSeat(root, parts);
            Transform dismount = BuildDismountPoint(root, parts);
            Transform pivot = BuildCameraPivot(root, parts);

            WireFlight(root, instance, seat, dismount, pivot, span);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Ornithopter] Built {PrefabPath}: {span:F2} m span, {boxes} collision boxes, " +
                      "30-bone rig, prone seat.");
        }

        /// <summary>
        /// The wings and tail fan are skinned; every other part is bone-parented. If the export ever
        /// loses that, the shoulders will still flap and the cloth will hang in space behind them --
        /// a symptom that looks like an animator bug and is not one.
        /// </summary>
        private static bool VerifySkinning(GameObject root)
        {
            var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Select(s => s.name)
                .ToHashSet();

            string[] missing = SkinnedPanels.Where(p => !skinned.Contains(p)).ToArray();
            if (missing.Length == 0)
                return true;

            Debug.LogError($"[Ornithopter] These panels are not skinned: {string.Join(", ", missing)}. " +
                           "The wings will not deform. Re-export with the Armature modifiers intact " +
                           "-- dune_ornithopter_export.py asserts this before writing the FBX.");
            return false;
        }

        /// <summary>Wingspan, measured off the meshes. Used to size the camera boom.</summary>
        private static float MeasureSpan(GameObject root)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                min = Mathf.Min(min, r.bounds.min.x);
                max = Mathf.Max(max, r.bounds.max.x);
            }
            return max > min ? max - min : 0f;
        }

        /// <summary>
        /// Put the prefab's origin on the cradle rather than on the model origin.
        ///
        /// The flight motor writes rotation to the root transform, so whatever point the root sits on
        /// is the point the craft pivots about. On the cradle, pitching feels like the pilot dropping
        /// their own shoulder. On the model origin -- out on the fuselage centreline, well forward of
        /// and below the rider -- the same input swings the rider through an arc.
        /// </summary>
        private static void DropOriginOntoCradle(GameObject root, GameObject instance,
                                                 Dictionary<string, Transform> parts)
        {
            if (!parts.TryGetValue("Bone_Cradle", out Transform cradle))
                return;

            Vector3 offset = cradle.position - root.transform.position;
            instance.transform.localPosition = -offset;
            Debug.Log($"[Ornithopter] Origin set on the cradle, offset {offset.magnitude:F3} m.");
        }

        private static bool AddBox(Dictionary<string, Transform> parts, string meshName, string colName)
        {
            if (!parts.TryGetValue(meshName, out Transform t))
            {
                Debug.LogWarning($"[Ornithopter] No mesh '{meshName}'; skipping {colName}.");
                return false;
            }

            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            // Local bounds and parented to the mesh, so the box rides whatever bone moves it rather
            // than being frozen at the rest pose.
            var box = new GameObject(colName).transform;
            box.SetParent(t, false);
            BoxCollider bc = box.gameObject.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = mf.sharedMesh.bounds.size;
            return true;
        }

        /// <summary>
        /// The prone cradle. This is the one piece of placement that is genuinely about posture rather
        /// than geometry.
        ///
        /// MountModule parents the rider to this transform at identity rotation, so the SEAT's
        /// orientation IS the rider's. A player capsule stands along its own +Y; pitching the seat
        /// +90 degrees about X lays that axis down along the craft's +Z, which puts the rider face
        /// down, head forward, slung under the belly -- the posture the machine was modelled around.
        /// </summary>
        private static Transform BuildSeat(GameObject root, Dictionary<string, Transform> parts)
        {
            var seat = new GameObject("SEAT_Cradle").transform;
            seat.SetParent(root.transform, false);

            if (parts.TryGetValue("Mesh_Cradle_Pad", out Transform pad))
            {
                var mf = pad.GetComponent<MeshFilter>();
                seat.position = mf != null
                    ? pad.TransformPoint(mf.sharedMesh.bounds.center)
                    : pad.position;
            }

            seat.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return seat;
        }

        /// <summary>
        /// Where the rider is put down. Below and behind the cradle: dismounting at altitude drops
        /// them, which is intended -- the pack is usable while falling, so bailing out and redeploying
        /// is a move rather than a death sentence.
        /// </summary>
        private static Transform BuildDismountPoint(GameObject root, Dictionary<string, Transform> parts)
        {
            var point = new GameObject("DismountPoint").transform;
            point.SetParent(root.transform, false);
            point.localPosition = new Vector3(0f, -1.2f, -1.5f);
            return point;
        }

        /// <summary>Camera target: above the cradle, so the machine sits low in frame with sky above.</summary>
        private static Transform BuildCameraPivot(GameObject root, Dictionary<string, Transform> parts)
        {
            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.0f, 0f);
            return pivot;
        }

        private static void WireFlight(GameObject root, GameObject instance, Transform seat,
                                       Transform dismount, Transform pivot, float span)
        {
            // Dynamic, gravity off. The flight model integrates weight itself as one equation with
            // lift and drag; Unity's gravity on top would be a second, uncoordinated pull and the
            // stall would read as a brick. The motor sets this at runtime too -- belt and braces,
            // because a prefab saved with useGravity on flies wrong for exactly one frame and that is
            // enough to notice.
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.mass = 150f;
            rb.linearDamping = 0f;
            rb.angularDamping = 0f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            OrnithopterFlightMotor motor = root.AddComponent<OrnithopterFlightMotor>();
            var mo = new SerializedObject(motor);
            Set(mo, "body", rb);
            SetFloat(mo, "spreadDuration", 0.6f);
            SetFloat(mo, "launchAirspeed", 12f);
            SetFloat(mo, "groundProbeDistance", 1.4f);
            SetInt(mo, "groundMask", ~0);
            SetFloat(mo, "landingGraceSeconds", 0.35f);
            mo.ApplyModifiedPropertiesWithoutUndo();

            OrnithopterWingAnimator animator = root.AddComponent<OrnithopterWingAnimator>();
            var ao = new SerializedObject(animator);
            Set(ao, "armatureRoot", instance.transform);
            ao.ApplyModifiedPropertiesWithoutUndo();

            MountModule mount = root.AddComponent<MountModule>();
            var mso = new SerializedObject(mount);
            Set(mso, "seatPoint", seat);
            Set(mso, "dismountPoint", dismount);
            Set(mso, "thirdPersonPivot", pivot);

            // Nobody walks up to this craft and presses E: it does not exist until the wing pack
            // spawns it, already mounted. Leaving direct interaction on would make every hull collider
            // a boarding point on a machine that is only ever boarded one way.
            SetBool(mso, "mountableByDirectInteraction", false);

            // Camera boom scaled off the measured span, so re-proportioning the model re-derives the
            // framing instead of leaving it tuned for a machine that no longer exists.
            float boom = Mathf.Max(8f, span * 1.1f);
            SetVector3(mso, "thirdPersonOffset", new Vector3(0f, span * 0.35f, -boom));
            SetFloat(mso, "thirdPersonDistance", boom);
            SetFloat(mso, "thirdPersonLookAhead", span * 1.4f);
            // Softer than a ground vehicle: a flying machine changes attitude constantly and a stiff
            // follow turns every correction into a camera jolt.
            SetFloat(mso, "thirdPersonFollowLerp", 10f);
            SetFloat(mso, "thirdPersonAimLerp", 12f);
            SetFloat(mso, "thirdPersonYawLerp", 8f);
            SetFloat(mso, "defaultMountedPitch", -8f);

            // The reason this flag exists. See MountModule.Camera.cs.
            SetBool(mso, "followMountPitch", true);
            mso.ApplyModifiedPropertiesWithoutUndo();

            // SteerModule carries [RequireComponent(MountModule, AgentController)], so this brings an
            // AgentController along with it.
            SteerModule steer = root.AddComponent<SteerModule>();
            var sso = new SerializedObject(steer);
            Set(sso, "mountModule", mount);
            // Space / Left Ctrl. Already exists as an action -- it was added for ShipRV -- so the
            // ornithopter flies on the input asset exactly as it is.
            SetString(sso, "verticalActionName", "Vertical");
            // No jump, no leap: this machine is never on the ground while it is being ridden.
            SetBool(sso, "jumpEnabled", false);
            SetBool(sso, "leapEnabled", false);
            // The motor reads Move.y as pitch and Move.x as roll, and smoothing those before they
            // arrive would add a second lag on top of the craft's own inertia.
            SetFloat(sso, "turnSmoothTime", 0.05f);
            sso.ApplyModifiedPropertiesWithoutUndo();

            Ensure<AgentController>(root);
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // Private [SerializeField] fields are not reachable from an editor script any other way, and
        // making them public purely so this could set them would widen the runtime API for a
        // build-time convenience. A missing name warns loudly rather than silently doing nothing.
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

        private static void SetString(SerializedObject so, string field, string value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.stringValue = value;
        }

        private static void SetVector3(SerializedObject so, string field, Vector3 value)
        {
            SerializedProperty p = Find(so, field);
            if (p != null) p.vector3Value = value;
        }

        private static SerializedProperty Find(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
                Debug.LogWarning($"[Ornithopter] {so.targetObject.GetType().Name} has no serialized " +
                                 $"field '{field}' -- it was renamed; this value is unset.");
            return p;
        }
    }
}
