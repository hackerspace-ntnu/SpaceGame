// Builds Assets/Game/Prefabs/agents/vehicle/DesertCrawler.prefab from
// Assets/Game/Art/Models/Vehicles/Crawler/desert_crawler.fbx.
//
// The crawler is a six-legged habitat: a chassis on six mech legs, two container modules with a
// vehicle bay in the slot between them, and a mast gantry over their roofs. Unlike ShipRV, the FBX
// arrives WITH its rig — CRAWLER_Rig, a Root plus six Coxa/Hip/Knee/Ankle/Foot chains and four
// mast bones — because DesertCrawlerLocomotion measures that hierarchy live at Awake and poses it
// every frame. This script does not build the articulation; it wires up everything around it.
//
// What it does add:
//   * Collision, measured from the meshes rather than hardcoded, so re-exporting the model with
//     different proportions and re-running this still lands the boxes on the geometry.
//   * Per-limb collision parented to the meshes themselves, so the boxes ride the legs as the
//     solver moves them. WalkerGround rejects any collider under the machine's own root, so these
//     cannot make the walker plant its feet on itself.
//   * Locomotion, driver, AI and the carry volume that lets a player ride the deck. Not a mount
//     rig: the crawler is autonomous and is neither mountable nor steerable.
//
// The model is generated from Assets/Game/Art/Models/_Source~/models/vehicles/desert_crawler.py — see that file and
// Assets/Game/Art/Models/_Source~/models/vehicles/desert_crawler_BUILD.md. It has since been hand-edited,
// so re-running the generator would destroy work: refresh with desert_crawler_export.py instead.
//
// KNOWN GAP IN THE MODEL: the collector bucket's sieve, `Grid`, is not parented to anything in the
// .blend, so it arrives as a loose child of the FBX root rather than under `Collector_Bucket`. It
// looks right at rest and is wrong the instant CrawlerToolRig tips the bucket — the lip plate swings
// away and the sieve stays put. The fix belongs in the .blend (bone-parent `Grid` to
// `Collector_Bucket`, as the retired `Cube.001` was), not here.
//
// Model orientation: authored −Y forward in Blender, which the default FBX axis conversion lands
// on Unity's +Z, so there is deliberately no ModelYaw here.
//
// Re-run from: Tools ▸ Vehicles ▸ Build Desert Crawler Prefab
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.EditorTools
{
    public static class DesertCrawlerBuilder
    {
        private const string ModelPath = "Assets/Game/Art/Models/Vehicles/Crawler/desert_crawler.fbx";
        // The live prefab, the one persistentScene references. Was left pointing at the pre-restructure
        // path, where a rebuild wrote a second, orphaned prefab and reported success.
        private const string PrefabPath =
            "Assets/Game/Prefabs/Agents/Vehicles/Ground/DesertCrawler.prefab";

        private static readonly string[] LegIds = { "P1", "P2", "P3", "N1", "N2", "N3" };

        // Body meshes that get a collision box of their own, and the name the box takes. Anything not
        // listed is decoration — masts, flags, pipe runs, floodlights — and is deliberately not solid.
        private static readonly (string mesh, string col)[] BodyBoxes =
        {
            ("Mesh_Crawler_Chassis",    "COL_Chassis"),
            ("Mesh_Crawler_Prow",       "COL_Prow"),
            // The two truss runs under the deck. NOT `Mesh_Crawler_Gantry` — that one sits at
            // z = -12.77 in the model, some 21 m below the machine's own feet, and a box on it would
            // be a collider floating underground for anything walking past to trip on.
            ("Mesh_Crawler_Gantry.002", "COL_GantryPort"),
            ("Mesh_Crawler_Gantry.003", "COL_GantryStarboard"),
            // Front digging head. Parented to the meshes, so the boxes ride the boom as it pitches.
            ("Mesh_Dig_ArmP",           "COL_DigArmStarboard"),
            ("Mesh_Dig_ArmN",           "COL_DigArmPort"),
            ("Mesh_Dig_Brace",          "COL_DigBrace"),
            // The collector bucket, hand-modelled and still carrying its authored name. It was
            // `Cube.001` until the bucket was rebuilt as a wireframe sieve (`Grid`) plus this lip
            // plate. The box goes on the plate and NOT on the sieve, because the plate is the piece
            // parented to the `Collector_Bucket` bone — a box on the sieve would be left behind the
            // moment CrawlerToolRig tips the bucket. See the note in the header about `Grid`.
            ("Cube.002",                "COL_CollectorBucket"),
        };

        // Meshes the habitat build used to have and no longer does — the container modules, the deck
        // plating and the vehicle bay were all deleted by hand when the crawler stopped being a
        // habitat. Listed so a missing one is silence rather than a warning per rebuild.
        private static readonly string[] RetiredMeshes =
        {
            "Mesh_Crawler_Deck", "Mesh_Module_Starboard", "Mesh_Module_Port", "Mesh_Crawler_Bay",
        };

        // Limb meshes get a box each, named for the joint they hang off so the prefab reads the same
        // way rig_walker does.
        private static readonly (string suffix, string joint)[] LimbBoxes =
        {
            ("Upper", "Hip"),
            ("Lower", "Knee"),
            ("Foot",  "Ankle"),
        };

        [MenuItem("Tools/Vehicles/Build Desert Crawler Prefab")]
        public static void Build()
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[DesertCrawler] No model at {ModelPath}. Run " +
                               "Assets/Game/Art/Models/_Source~/models/vehicles/desert_crawler_export.py first.");
                return;
            }

            GameObject root = new GameObject("DesertCrawler");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                               InteractionMode.AutomatedAction);
            instance.transform.SetParent(root.transform, false);

            Dictionary<string, Transform> parts = root.GetComponentsInChildren<Transform>(true)
                .GroupBy(t => t.name)
                .ToDictionary(g => g.Key, g => g.First());

            Transform armature = WalkerRig.FindArmature(root.transform);
            if (armature == null || !parts.ContainsKey("Hip_P2"))
            {
                Debug.LogError("[DesertCrawler] The model has no leg rig — expected bones named " +
                               "Coxa_/Hip_/Knee_/Ankle_/Foot_<id>. Was the FBX exported with " +
                               "object_types including ARMATURE?");
                Object.DestroyImmediate(root);
                return;
            }

            DropModelOntoHips(root, instance);

            int boxes = 0;
            foreach ((string mesh, string col) in BodyBoxes)
            {
                boxes += AddBox(parts, mesh, col) ? 1 : 0;
            }
            foreach (string id in LegIds)
            {
                foreach ((string suffix, string joint) in LimbBoxes)
                {
                    boxes += AddLimbBox(parts, id, suffix, joint) ? 1 : 0;
                }
            }

            BoxCollider carry = BuildGantryDeck(root, parts, ref boxes);
            BuildBayFloor(root, parts, ref boxes);

            WireLocomotion(root, armature, carry);

            // Read anything wanted for the report BEFORE the scratch hierarchy goes away: `armature`
            // points into `root`, and a destroyed Transform throws on `.name`, not returns null.
            string rigName = armature.name;

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            Debug.Log($"[DesertCrawler] Built {PrefabPath}: {boxes} collision boxes, " +
                      $"{LegIds.Length} legs on {rigName}.");
        }

        /// Sink the model so the prefab's own origin sits on the hip plane rather than on the soles.
        ///
        /// This is not cosmetic. `DesertCrawlerLocomotion` treats its `body` transform as the hull and
        /// takes ride height as `body.y - averageFootY`. The model is authored standing on z = 0, so
        /// with the root left on the ground that difference is zero: the hull is pinned at foot level,
        /// every hip sits on the floor, and all six legs are asked to reach the ground from there —
        /// measured at 5.9x their own reach, permanently unreachable, and no leg ever swings because
        /// the gait cannot place a foothold it cannot reach.
        ///
        /// Measured off the rig rather than typed in, so re-proportioning the model re-derives it.
        private static void DropModelOntoHips(GameObject root, GameObject instance)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            float hipSum = 0f;
            int hips = 0;
            foreach (Transform t in all)
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
            Debug.Log($"[DesertCrawler] Origin set on the hip plane: ride height {rideHeight:F2} m.");
        }

        /// A box matching one mesh, parented to that mesh so it rides whatever bone moves it.
        private static bool AddBox(Dictionary<string, Transform> parts, string meshName, string colName)
        {
            if (!parts.TryGetValue(meshName, out Transform t))
            {
                Debug.LogWarning($"[DesertCrawler] No mesh '{meshName}'; skipping {colName}.");
                return false;
            }

            MeshFilter mf = t.GetComponent<MeshFilter>();
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

        /// A limb's collision box, hung DIRECTLY off its joint rather than off the mesh.
        ///
        /// The nesting matters and it is not obvious. `DesertCrawlerLocomotion.RadiusOf` takes each
        /// segment's capsule radius from the first `COL_`-prefixed box it finds under the joint, and it
        /// searches recursively — so a box parked one level down under the mesh is still found, but the
        /// depth-first walk reaches the *knee's* subtree before the thigh's own mesh, and the thigh ends
        /// up measuring the shin. The leg-clearance test then works from the wrong radii, footholds get
        /// pushed away from a leg it thinks is somewhere else, and the whole gait stops stepping.
        /// rig_walker carries its COL_Hip_*/COL_Knee_*/COL_Ankle_* as direct joint children; this
        /// matches that layout, which is the one the runtime is written against.
        ///
        /// The mesh is found by walking the joint's own subtree, NOT by name, and that is load-bearing.
        /// The scorpion claw was hand-built out of duplicated leg parts, so the model now contains a
        /// `Mesh_Leg_N1_Upper` parented to `Claw_01` and a `Mesh_Leg_N1_Upper.001` parented to
        /// `Hip_N1` — the real leg is the suffixed one. A name lookup takes the claw's copy and hangs
        /// leg collision on the arm, which is both wrong and very hard to see in the Inspector.
        /// Searching under the joint asks the question that actually matters: which mesh does this
        /// bone move?
        private static bool AddLimbBox(Dictionary<string, Transform> parts, string id, string suffix,
                                       string joint)
        {
            if (!parts.TryGetValue($"{joint}_{id}", out Transform bone)) return false;

            Transform mesh = FindLimbMesh(bone, suffix);
            if (mesh == null) return false;

            MeshFilter mf = mesh.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            var box = new GameObject($"COL_{joint}_{id}").transform;
            box.SetParent(bone, false);
            // Sit the box on the mesh's own placement under the bone, so it wraps the segment exactly
            // while still being a first-level child of the joint.
            box.localPosition = mesh.localPosition;
            box.localRotation = mesh.localRotation;
            box.localScale = mesh.localScale;

            BoxCollider bc = box.gameObject.AddComponent<BoxCollider>();
            bc.center = mf.sharedMesh.bounds.center;
            bc.size = mf.sharedMesh.bounds.size;
            return true;
        }

        /// The mesh a joint moves, identified by where it sits in the rig rather than what it is called.
        /// `contains` is matched rather than the whole name so that Blender's `.001` duplicate suffixes
        /// still resolve.
        private static Transform FindLimbMesh(Transform joint, string suffix)
        {
            string needle = $"_{suffix}";
            foreach (MeshFilter mf in joint.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.name.Contains(needle)) return mf.transform;
            }
            return null;
        }

        /// The walkable deck, plus the trigger volume that carries riders standing on it.
        ///
        /// Built on the chassis roof. It used to be built on `Mesh_Crawler_Gantry`, which made sense
        /// while that mesh was the walkway bridging the two container modules — but the modules are
        /// gone and that object now sits 21 m below the machine's feet, so the rails and the carry
        /// volume were being built underground.
        ///
        /// Two things about the chassis node make the obvious version of this wrong, and both used to
        /// bite. It is rotated −90° about X — the FBX conversion of a Z-up mesh — so in ITS local
        /// space +z is world up and +y runs fore-aft; building the rails "upward" along local +y laid
        /// four slabs out behind the machine instead. And it carries the FBX's ~80-90x import scale,
        /// so a metre typed straight into a size or an offset arrives ninety times too big: the 1.2 m
        /// rails were 108 m tall towers and the 2.2 m carry volume a 198 m column, both of which the
        /// Interactor happily raycast, which is how the machine could be mounted by aiming at empty
        /// sky. Every metre below therefore goes through `Metres`, which divides by the local scale.
        private static BoxCollider BuildGantryDeck(GameObject root, Dictionary<string, Transform> parts,
                                                   ref int boxes)
        {
            if (!parts.TryGetValue("Mesh_Crawler_Chassis", out Transform gantry)) return null;
            MeshFilter mf = gantry.GetComponent<MeshFilter>();
            if (mf == null) return null;

            Bounds b = mf.sharedMesh.bounds;
            Vector3 scale = gantry.lossyScale;
            // Local axes of the chassis node: x = beam, y = fore-aft, z = up.
            float railHeight = Metres(1.2f, scale.z);
            float railThickBeam = Metres(0.2f, scale.x);
            float railThickLong = Metres(0.2f, scale.y);
            float insetBeam = Metres(0.1f, scale.x);
            float insetLong = Metres(0.1f, scale.y);
            float top = b.max.z;
            float railZ = top + railHeight * 0.5f;

            // Rails: four thin walls round the deck, so a rider cannot walk off a machine that is
            // twenty metres up. Sized off the deck itself rather than typed in.
            (string name, Vector3 centre, Vector3 size)[] rails =
            {
                ("COL_Rail_Fore", new Vector3(b.center.x, b.min.y + insetLong, railZ),
                    new Vector3(b.size.x, railThickLong, railHeight)),
                ("COL_Rail_Aft", new Vector3(b.center.x, b.max.y - insetLong, railZ),
                    new Vector3(b.size.x, railThickLong, railHeight)),
                ("COL_Rail_Port", new Vector3(b.min.x + insetBeam, b.center.y, railZ),
                    new Vector3(railThickBeam, b.size.y, railHeight)),
                ("COL_Rail_Starboard", new Vector3(b.max.x - insetBeam, b.center.y, railZ),
                    new Vector3(railThickBeam, b.size.y, railHeight)),
            };
            foreach ((string name, Vector3 centre, Vector3 size) in rails)
            {
                var t = new GameObject(name).transform;
                t.SetParent(gantry, false);
                BoxCollider bc = t.gameObject.AddComponent<BoxCollider>();
                bc.center = centre;
                bc.size = size;
                boxes++;
            }

            var carryGo = new GameObject("CARRY_Gantry").transform;
            carryGo.SetParent(gantry, false);
            BoxCollider carry = carryGo.gameObject.AddComponent<BoxCollider>();
            float carryHeight = Metres(2.2f, scale.z);
            carry.center = new Vector3(b.center.x, b.center.y, top + carryHeight * 0.5f);
            carry.size = new Vector3(b.size.x, b.size.y, carryHeight);
            carry.isTrigger = true;
            return carry;
        }

        /// A distance in metres expressed in the local units of a node carrying `scale` on that axis.
        private static float Metres(float metres, float scale) =>
            Mathf.Approximately(scale, 0f) ? metres : metres / Mathf.Abs(scale);

        /// The vehicle bay floor — the one interior surface that has to hold something up.
        private static void BuildBayFloor(GameObject root, Dictionary<string, Transform> parts,
                                          ref int boxes)
        {
            if (!parts.TryGetValue("Mesh_Crawler_Bay", out Transform bay)) return;
            MeshFilter mf = bay.GetComponent<MeshFilter>();
            if (mf == null) return;

            Bounds b = mf.sharedMesh.bounds;
            var t = new GameObject("COL_BayFloor").transform;
            t.SetParent(bay, false);
            BoxCollider bc = t.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(b.center.x, b.min.y + 0.15f, b.center.z);
            bc.size = new Vector3(b.size.x, 0.3f, b.size.z);
            boxes++;
        }

        private static void WireLocomotion(GameObject root, Transform armature, BoxCollider carry)
        {
            DesertCrawlerLocomotion loco = root.AddComponent<DesertCrawlerLocomotion>();
            var so = new SerializedObject(loco);
            Set(so, "armatureRoot", armature);
            Set(so, "body", root.transform);
            SetFloat(so, "yawRange", 40f);
            SetFloat(so, "hipRange", 45f);
            SetFloat(so, "kneeRange", 60f);
            SetFloat(so, "ankleRange", 45f);
            SetFloat(so, "rollRange", 30f);
            // Two legs in the air at once, not one. Six legs swinging singly gives a duty of 1/6, a
            // 3.7 s gait cycle and 1.5 m/s — the machine shuffles. At two it still has four feet down,
            // comfortably clear of MinPlantedLegs = 3, and it moves at a pace that matches its stride.
            SetInt(so, "swingLegs", 2);
            SetFloat(so, "stepDuration", 0.5f);
            SetFloat(so, "stepClearance", 0.08f);
            SetFloat(so, "obstacleClearance", 1.5f);
            // Every layer. Left unset this serialises as 0, and a walker whose ground mask matches
            // nothing finds no ground at all: it never snaps down, never places a foothold, and stands
            // perfectly still with six planted feet looking for all the world like a rig fault.
            // The machine's own colliders are rejected by WalkerGround regardless of mask.
            SetInt(so, "groundMask", ~0);
            SetFloat(so, "rayStartAbove", 14f);
            SetFloat(so, "rayLength", 400f);
            SetBool(so, "snapToGroundOnStart", true);
            // Left on: the rest pose already stands the machine at its design height, so the measured
            // value is the authored one, and it re-derives itself if the model is re-proportioned.
            SetBool(so, "autoCalibrateRideHeight", true);
            SetFloat(so, "heightSmooth", 5f);
            SetBool(so, "drawGizmos", true);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Driver limits sized to the machine rather than left at the defaults, which are tuned for
            // something a quarter of this footprint: a 17 m foot span cannot corner in 6 m, and asking
            // for 6 m/s when the gait tops out around 3 just means permanently saturated throttle.
            DesertCrawlerDriver driver = root.AddComponent<DesertCrawlerDriver>();
            var dso = new SerializedObject(driver);
            SetFloat(dso, "moveSpeed", 3.2f);
            SetFloat(dso, "turnSpeed", 16f);
            SetFloat(dso, "acceleration", 1.2f);
            SetFloat(dso, "defaultStopDistance", 12f);
            SetFloat(dso, "cornerArriveRadius", 10f);
            SetFloat(dso, "navMeshSampleDistance", 25f);
            dso.ApplyModifiedPropertiesWithoutUndo();

            // Kinematic, gravity off. The locomotion writes the hull transform directly, so a dynamic
            // body would fight it every frame and win. It is here for the reason the agent baseline
            // wants one at all: without a Rigidbody every collider on this machine is a *static*
            // collider, and moving static colliders makes PhysX rebuild its broadphase tree each frame
            // — on a hundred-odd boxes that walk, that is the expensive way to do nothing. It also
            // makes the machine visible to the OverlapSphere / layer queries the agent systems use.
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // Deliberately NO MountModule / SteerModule. The crawler is an autonomous scrap collector,
            // not a vehicle: it wanders and works its tools under its own AI and the player never
            // drives it. WalkerPlatformCarrier stays — riding the deck as cargo is not steering it.
            WalkerPlatformCarrier carrier = root.AddComponent<WalkerPlatformCarrier>();
            if (carry != null)
            {
                var cso = new SerializedObject(carrier);
                Set(cso, "carryVolume", carry);
                cso.ApplyModifiedPropertiesWithoutUndo();
            }

            WireBrain(root, armature);
        }

        /// The AI stack, and the tool rig it drives.
        ///
        /// `DesertCrawlerDriver` is already an `IMovementMotor`, so the standard agent loop works here
        /// unchanged: `AgentController` ticks the modules, `WanderModule` returns a `MoveIntent`, and
        /// the driver turns that into a twist the legs carry. There is deliberately no `NavMeshAgent`
        /// — the driver paths with `NavMesh.CalculatePath` precisely so that nothing but the locomotion
        /// writes this transform.
        ///
        /// Wander radius and stop distance are sized to the machine. The defaults are tuned for
        /// something a person-sized, and a 17 m foot span asked to wander inside a 10 m circle spends
        /// its life turning on the spot.
        private static void WireBrain(GameObject root, Transform armature)
        {
            // Get-or-add, not add. Nothing added above requires an AgentController today, but the
            // agent components carry [RequireComponent] attributes that come and go; a second
            // AddComponent would give the prefab two controllers, and the one that answers
            // GetComponent is then a coin toss between a controller that has the modules and one that
            // does not.
            Ensure<AgentController>(root);
            Ensure<AgentTargeting>(root);

            WanderModule wander = root.AddComponent<WanderModule>();
            var wso = new SerializedObject(wander);
            SetInt(wso, "priority", ModulePriority.Fallback);
            SetBool(wso, "limitWanderRadius", false);
            SetFloat(wso, "freeRoamRadius", 120f);
            SetFloat(wso, "sampleDistance", 30f);
            SetFloat(wso, "minDestinationDistance", 25f);
            SetFloat(wso, "stopDistance", 8f);
            SetFloat(wso, "minWaitTime", 4f);
            SetFloat(wso, "maxWaitTime", 12f);
            wso.ApplyModifiedPropertiesWithoutUndo();

            CrawlerToolRig tools = root.AddComponent<CrawlerToolRig>();
            var tso = new SerializedObject(tools);
            Set(tso, "armatureRoot", armature);
            tso.ApplyModifiedPropertiesWithoutUndo();

            CrawlerToolModule toolBrain = root.AddComponent<CrawlerToolModule>();
            var mso = new SerializedObject(toolBrain);
            SetInt(mso, "priority", ModulePriority.Ambient);
            mso.ApplyModifiedPropertiesWithoutUndo();
        }

        /// Add a component only if a [RequireComponent] attribute has not already brought one along.
        private static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        // Private [SerializeField] fields are not reachable from an editor script any other way, and
        // making them public purely so this could set them would widen the runtime API for a build-time
        // convenience. A missing name fails loudly rather than silently doing nothing.
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
                Debug.LogWarning($"[DesertCrawler] {so.targetObject.GetType().Name} has no " +
                                 $"serialized field '{field}' — it was renamed; this value is unset.");
            return p;
        }
    }
}
