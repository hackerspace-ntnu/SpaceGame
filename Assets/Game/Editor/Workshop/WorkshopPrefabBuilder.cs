// Assembles Workshop.prefab from the FBX set and workshop_layout.json that workshop_export.py writes.
//
// A kept builder, not a one-off pass: the workshop's moving parts are wired by name from the layout file, and a
// re-export of the model changes those names and ratios. Rebuild rewrites the prefab in place (same GUID), so
// HAND EDITS TO Workshop.prefab ARE LOST ON REBUILD. Put them here, or in a prefab that nests this one.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Vehicles;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopPrefabBuilder
    {
        public const string ModelFolder = "Assets/Game/Art/Models/Environment/Structures/Workshop/";
        public const string PrefabPath = "Assets/Game/Prefabs/Environment/Structures/Workshop/Workshop.prefab";
        public const string LayoutPath = ModelFolder + "workshop_layout.json";

        // Everything but ContributeGI; and the same without Batching Static, for a mesh the static batcher's
        // 16-bit indices cannot hold (docs/AI/systems/ArtPipeline.md).
        private const StaticEditorFlags Scenery = (StaticEditorFlags)94;
        private const StaticEditorFlags SceneryUnbatched = (StaticEditorFlags)90;
        private const int MaxBatchedVertices = 65535;

        private const string CollisionPrefix = "COLT_";
        private const string WallMarker = "_Wall_";

        // The carry box is the square round everything that turns, and only whoever has a turning piece under
        // their feet is carried: fixed stairs stand inside the main room, and every room's wheel post is fixed.
        private const float CarryHeight = 3.8f;
        private const float CarryFloorClearance = 0.2f;
        private const float CarryFootingReach = 2.5f;

        // A wheel: the box the crosshair finds is as wide as the wheel, which is this many times the room's radius
        // in the small rooms and capped for the main room's. Its holder may stand this much further out than the
        // spot the model marks, and the room turns this fast with a button held.
        private const float SmallRoomRadius = 6f;
        private const float SmallWheelBox = 1.8f;
        private const float MainWheelBox = 3.4f;
        private const float StanceSlack = 0.9f;
        private const float SmallRoomDegreesPerSecond = 28f;
        private const float MainRoomDegreesPerSecond = 12f;

        private const float LampRange = 18f;
        private const float LampIntensity = 12f;
        private static readonly Color LampColour = new(1f, 0.78f, 0.52f);

        [MenuItem("Tools/SpaceGame/Workshop/Rebuild Workshop Prefab")]
        private static void RebuildMenu() => Debug.Log("[Workshop] " + Rebuild());

        /// <summary>Rebuild the prefab, and report what was read back off disk.</summary>
        public static string Rebuild()
        {
            if (!File.Exists(LayoutPath)) return $"no layout at {LayoutPath}. Run workshop_export.py.";
            WorkshopLayout layout = WorkshopLayout.Parse(File.ReadAllText(LayoutPath));

            ImportWithoutMaterials(ModelFolder + "workshop_collision.fbx");

            var root = new GameObject("Workshop");
            List<string> homeProblems;
            string gate;
            try
            {
                gate = Compose(root, layout);
                homeProblems = WorkshopHomeBuilder.Furnish(root);
                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            string registered = NetworkPrefabRegistrar.Sync(out int added, out int total);
            AssetDatabase.SaveAssets();
            string home = homeProblems.Count == 0 ? "home furnished" : "HOME: " + string.Join("; ", homeProblems);
            return Verify(layout) + $"; {gate}; {home}; network prefabs: {added} added of {total}. {registered}";
        }

        /// <summary>Lay the workshop out under <paramref name="root"/>; returns what its gear doors, crane and mission were built with.</summary>
        private static string Compose(GameObject root, WorkshopLayout layout)
        {
            GameObject model = Place(root, "workshop.fbx", "Model");
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                bool tooBig = filter.sharedMesh != null && filter.sharedMesh.vertexCount > MaxBatchedVertices;
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject, tooBig ? SceneryUnbatched : Scenery);
            }

            GameObject collision = Place(root, "workshop_collision.fbx", "Collision");
            foreach (MeshFilter filter in collision.GetComponentsInChildren<MeshFilter>(true))
            {
                // On the FBX's own node, which carries the import's scale and turn: a scale-1 object would hold a
                // collider a hundredth the size.
                MakeCollider(filter, convex: false);
                GameObjectUtility.SetStaticEditorFlags(filter.gameObject, SceneryUnbatched);
            }

            GameObject moving = Place(root, "workshop_moving.fbx", "Machinery");
            var nodes = new Dictionary<string, Transform>();
            foreach (Transform node in moving.GetComponentsInChildren<Transform>(true)) nodes[node.name] = node;

            var turntables = new Dictionary<string, WorkshopTurntable>();
            foreach (WorkshopLayout.Turntable entry in layout.turntables)
                if (nodes.TryGetValue(entry.node, out Transform node)) turntables[entry.node] = AddTurntable(node);

            WorkshopGearTrain train = moving.AddComponent<WorkshopGearTrain>();
            WireGearTrain(train, layout, nodes);

            WorkshopMachinery machinery = root.AddComponent<WorkshopMachinery>();
            var wiring = new SerializedObject(machinery);
            SerializedProperty rooms = wiring.FindProperty("rooms");
            rooms.arraySize = layout.wheels.Length;
            foreach (WorkshopLayout.Wheel wheel in layout.wheels)
                rooms.GetArrayElementAtIndex(WorkshopLayout.RoomOf(wheel.channel)).objectReferenceValue = Find(turntables, wheel.turntable);
            SerializedFields.Set(wiring, "gearTrain", train);
            SerializedProperty round = wiring.FindProperty("hoistRound");
            int[] stops = layout.hoist.stops;
            round.FindPropertyRelative("stops").vector3IntValue = new Vector3Int(stops[0], stops[1], stops[2]);
            round.FindPropertyRelative("metresPerStop").vector3Value = WorkshopLayout.V(layout.hoist.metresPerStop);
            round.FindPropertyRelative("liftStops").intValue = stops[3];
            round.FindPropertyRelative("liftMetresPerStop").floatValue = layout.hoist.metresPerStop[3];
            SerializedProperty limits = round.FindPropertyRelative("hookLimits");
            limits.arraySize = layout.hoist.hookLimits.Length;
            for (int i = 0; i < limits.arraySize; i++)
                limits.GetArrayElementAtIndex(i).intValue = layout.hoist.hookLimits[i];
            wiring.ApplyModifiedPropertiesWithoutUndo();

            Workshop workshop = root.AddComponent<Workshop>();
            var owner = new SerializedObject(workshop);
            SerializedFields.Set(owner, "machinery", machinery);
            owner.ApplyModifiedPropertiesWithoutUndo();

            AddWheels(root.transform, layout, turntables);
            AddLamps(root.transform, layout);
            string gate = GearDoorBuilder.Attach(root, layout, nodes, out GearDoor gateDoor);
            string vault = GearDoorBuilder.AttachVault(root, layout, nodes, out GearDoor vaultDoor);
            string hoist = WorkshopCraneBuilder.Attach(root, layout, nodes, machinery, out WorkshopCrane crane, out Transform console);
            wiring.Update();
            SerializedFields.Set(wiring, "crane", crane);
            wiring.ApplyModifiedPropertiesWithoutUndo();
            return $"{gate}; {vault}; {hoist}; " + WorkshopMissionBuilder.Attach(root, layout, gateDoor, vaultDoor, crane, console);
        }

        // ── Parts ────────────────────────────────────────────────────────────

        private static GameObject Place(GameObject root, string file, string name)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelFolder + file);
            if (asset == null) throw new FileNotFoundException("Run workshop_export.py: missing " + ModelFolder + file);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            instance.name = name;
            instance.transform.SetParent(root.transform, false);
            return instance;
        }

        /// <summary>The mesh stops being drawn and becomes what is walked on and bumped into.</summary>
        private static MeshCollider MakeCollider(MeshFilter filter, bool convex)
        {
            if (filter.TryGetComponent(out MeshRenderer renderer)) renderer.enabled = false;

            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = convex;
            return collider;
        }

        private static WorkshopTurntable AddTurntable(Transform node)
        {
            float reach = 0f;
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith(CollisionPrefix)) continue;

                MeshCollider piece = MakeCollider(filter, convex: true);
                Bounds turning = piece.bounds;
                foreach (Vector3 corner in new[] { turning.min, turning.max })
                    reach = Mathf.Max(reach, Mathf.Abs(corner.x - node.position.x), Mathf.Abs(corner.z - node.position.z));
                if (!filter.name.Contains(WallMarker)) continue;

                // A wall piece crosses tunnel mouths as the room turns. Carving, so a bake leaves the mouth open
                // and the obstacle shuts it in play, the way a gate leaf does.
                NavMeshObstacle obstacle = filter.gameObject.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.center = piece.sharedMesh.bounds.center;
                obstacle.size = piece.sharedMesh.bounds.size;
                obstacle.carving = true;
            }

            Rigidbody body = node.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            // The model counts a room's angle anticlockwise seen from above, which is a turn about Unity's down.
            WorkshopTurntable turntable = node.gameObject.AddComponent<WorkshopTurntable>();
            var so = new SerializedObject(turntable);
            SerializedFields.SetVector3(so, "localAxis", node.InverseTransformDirection(Vector3.down).normalized);
            so.ApplyModifiedPropertiesWithoutUndo();

            // On a holder at scale 1 and world-upright, so the box is in metres whatever the FBX node's own scale.
            Transform carried = Child(node, "CarryVolume");
            carried.SetPositionAndRotation(node.position, Quaternion.identity);
            carried.localScale = Vector3.one / node.lossyScale.x;

            BoxCollider carry = carried.gameObject.AddComponent<BoxCollider>();
            carry.isTrigger = true;
            carry.center = new Vector3(0f, CarryFloorClearance + (CarryHeight * 0.5f), 0f);
            carry.size = new Vector3(2f * reach, CarryHeight, 2f * reach);

            WalkerPlatformCarrier carrier = node.gameObject.AddComponent<WalkerPlatformCarrier>();
            carrier.BindCarryVolume(carry);
            carrier.CarryOnlyThoseStandingOnIt(CarryFootingReach);
            return turntable;
        }

        /// <summary>
        /// A wheel for every turning room: fixed, at the grip the model marks, turning the room it stands in.
        /// </summary>
        private static void AddWheels(Transform root, WorkshopLayout layout, Dictionary<string, WorkshopTurntable> turntables)
        {
            Transform wheels = Child(root, "Wheels");
            foreach (WorkshopLayout.Wheel entry in layout.wheels)
            {
                if (!turntables.TryGetValue(entry.turntable, out WorkshopTurntable room)) continue;

                bool small = entry.radius < SmallRoomRadius;
                Vector3 grip = WorkshopLayout.V(entry.pos);
                Vector3 stance = WorkshopLayout.V(entry.stance);

                Transform point = Child(wheels, "Wheel_" + entry.id);
                point.position = grip;

                BoxCollider box = point.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one * (small ? SmallWheelBox : MainWheelBox);

                var so = new SerializedObject(point.gameObject.AddComponent<WorkshopWheel>());
                so.FindProperty("id").stringValue = entry.id;
                SerializedFields.Set(so, "room", room);
                SerializedFields.Set(so, "carrier", room.GetComponent<WalkerPlatformCarrier>());
                so.FindProperty("degreesPerSecond").floatValue = small ? SmallRoomDegreesPerSecond : MainRoomDegreesPerSecond;
                so.FindProperty("reach").floatValue =
                    Vector2.Distance(new Vector2(grip.x, grip.z), new Vector2(stance.x, stance.z)) + StanceSlack;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void WireGearTrain(WorkshopGearTrain train, WorkshopLayout layout, Dictionary<string, Transform> nodes)
        {
            var so = new SerializedObject(train);

            SerializedProperty spinners = so.FindProperty("spinners");
            spinners.arraySize = 0;
            foreach (WorkshopLayout.Spinner entry in layout.spinners)
                if (nodes.TryGetValue(entry.node, out Transform node))
                    AddSpinner(spinners, node, WorkshopLayout.V(entry.axis), entry.ratio, entry.channel);

            SerializedProperty sliders = so.FindProperty("sliders");
            sliders.arraySize = 0;
            foreach (WorkshopLayout.Slider entry in layout.sliders)
            {
                if (!nodes.TryGetValue(entry.node, out Transform node)) continue;

                SerializedProperty row = Append(sliders);
                row.FindPropertyRelative("node").objectReferenceValue = node;
                Transform frame = node.parent;
                row.FindPropertyRelative("direction").vector3Value =
                    frame.InverseTransformDirection(WorkshopLayout.V(entry.dir)).normalized / frame.lossyScale.x;
                row.FindPropertyRelative("perUnit").floatValue = entry.perUnit;
                row.FindPropertyRelative("channel").intValue = (int)WorkshopLayout.Channel(entry.channel);
            }

            SerializedProperty cables = so.FindProperty("cables");
            cables.arraySize = 0;
            foreach (WorkshopLayout.Cable entry in layout.cables)
            {
                if (!nodes.TryGetValue(entry.node, out Transform node)) continue;

                SerializedProperty row = Append(cables);
                row.FindPropertyRelative("node").objectReferenceValue = node;
                row.FindPropertyRelative("axis").intValue = UprightAxis(node);
                row.FindPropertyRelative("length").floatValue = entry.length;
                row.FindPropertyRelative("channel").intValue = (int)WorkshopLayout.Channel(entry.channel);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddSpinner(SerializedProperty list, Transform node, Vector3 worldAxis, float ratio, string channel)
        {
            SerializedProperty row = Append(list);
            row.FindPropertyRelative("node").objectReferenceValue = node;
            row.FindPropertyRelative("localAxis").vector3Value = node.InverseTransformDirection(worldAxis).normalized;
            row.FindPropertyRelative("ratio").floatValue = ratio;
            row.FindPropertyRelative("channel").intValue = (int)WorkshopLayout.Channel(channel);
        }

        private static SerializedProperty Append(SerializedProperty list)
        {
            list.arraySize++;
            return list.GetArrayElementAtIndex(list.arraySize - 1);
        }

        /// <summary>Which of the node's own axes points most nearly up or down.</summary>
        private static int UprightAxis(Transform node)
        {
            int best = 0;
            float most = 0f;
            for (int axis = 0; axis < 3; axis++)
            {
                Vector3 local = Vector3.zero;
                local[axis] = 1f;
                float up = Mathf.Abs(Vector3.Dot(node.TransformDirection(local), Vector3.up));
                if (up <= most) continue;
                most = up;
                best = axis;
            }
            return best;
        }

        private static void AddLamps(Transform root, WorkshopLayout layout)
        {
            Transform lamps = Child(root, "Lamps");
            for (int i = 0; i < layout.lamps.Length; i++)
            {
                Transform lamp = Child(lamps, $"Lamp_{i:00}");
                lamp.position = WorkshopLayout.V(layout.lamps[i].pos);

                Light light = lamp.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = LampRange;
                light.intensity = LampIntensity;
                light.color = LampColour;
                light.shadows = LightShadows.None;
            }
        }

        private static void ImportWithoutMaterials(string path)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer) return;
            if (importer.materialImportMode == ModelImporterMaterialImportMode.None) return;

            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.SaveAndReimport();
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static T Find<T>(Dictionary<string, T> map, string key) where T : Object =>
            map.TryGetValue(key, out T value) ? value : null;

        // ── Read back ────────────────────────────────────────────────────────

        /// <summary>
        /// Re-read the prefab off disk and count what must be there. The AssetDatabase sometimes discards a save
        /// without a word, and a workshop with an unwired gear train looks exactly like one that is standing still.
        /// </summary>
        private static string Verify(WorkshopLayout layout)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (saved == null) return "NOT SAVED: the prefab cannot be read back";

            var train = new SerializedObject(saved.GetComponentInChildren<WorkshopGearTrain>(true));
            SerializedProperty spinners = train.FindProperty("spinners");
            int wired = 0;
            for (int i = 0; i < spinners.arraySize; i++)
                if (spinners.GetArrayElementAtIndex(i).FindPropertyRelative("node").objectReferenceValue != null) wired++;

            int expected = layout.spinners.Length;
            int turntables = saved.GetComponentsInChildren<WorkshopTurntable>(true).Length;
            int wheels = saved.GetComponentsInChildren<WorkshopWheel>(true).Length;
            int colliders = saved.GetComponentsInChildren<MeshCollider>(true).Length;
            int lamps = saved.GetComponentsInChildren<Light>(true).Length;

            string verdict = wired == expected && turntables == layout.turntables.Length &&
                             wheels == layout.wheels.Length
                ? "saved"
                : "READ BACK WRONG";
            return $"{verdict}: {wired}/{expected} spinning parts, {turntables} turning rooms, {wheels} wheels, " +
                   $"{colliders} mesh colliders, {lamps} lamps";
        }
    }
}
