// Builds the dune-barge prefabs — one per variant of dune_barge.blend — from the FBXs that
// dune_barge_export.py writes to Assets/Game/Art/Models/Vehicles/DuneBarge/ (refresh with that script,
// never by hand):
//
//   DuneBarge          Collection 2, the full barge: cockpit, neck, hold, stern castle
//   DuneBargeCompact   Collection 3, the rear hull only, with a lookout cabin on the castle
//   DuneBargeLookout   Collection 4, the full barge with a lookout cabin on the castle
//
// One prefab per variant, walked in place like the player ship: a barge will move, and an additive
// interior scene restores its visitors to a FIXED return point, so it cannot follow a hull.
//
// Everything is measured from the export's markers and rig, so a re-export re-derives it:
//   * COL_DuneBarge_#### islands into box / convex colliders (ModelMarkerImport); LAD_* into Ladders.
//   * Bone_Door_*: bulkhead doors. An ArticulatedPart on the hinge bone, the switch and a box collider on
//     the leaf (what the player looks at, and what blocks when shut); swung to whichever side is free.
//   * Bone_Hatch_R / _L: side-hatch lids, shut at rest. The lid carries a HatchPassage — using it opens
//     the lid and crawls the player through along the HATCH_<side>_* marks.
//   * Bone_Lid_*: any other hatch (the stern hatch): a lid that swings out, used like a door.
//   * InteriorReveal: the fit-out (<stem>_groups.json "interior") is drawn only for a machine whose camera
//     is in a VOL_* room, or near a hatch sill, a door or an OPEN_* opening.
//   * A SandstormShelter per room, riding (kinematic Rigidbody + WalkerPlatformCarrier), and the netcode
//     + save wiring for a transform-driven hull.
//
// The model is scaled by ModelScale AFTER the collision is built: the islands are in Blender metres, and
// ×1.6 is what makes the barge fit the 3 m player (hatches 1.9 m, cockpit 3.5 m headroom).
//
// Re-run from: Tools ▸ Vehicles ▸ Build Dune Barge Prefabs
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Core;
using SpaceGame.Core.Persistence;
using SpaceGame.Core.Persistence.EditorTools;
using SpaceGame.Vehicles;
using SpaceGame.World;
using SpaceGame.World.Safety;
using SpaceGame.World.Weather;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class DuneBargeBuilder
    {
        public const string ArtFolder = "Assets/Game/Art/Models/Vehicles/DuneBarge/";
        public const string PrefabFolder = "Assets/Game/Prefabs/Vehicles/DuneBarge/";
        public const string CollisionPrefix = "COL_DuneBarge_";
        public const string LadderPrefix = "LAD_";
        public const string OpeningPrefix = "OPEN_";
        public const string RoomPrefix = "VOL_";
        public const string DoorBonePrefix = "Bone_Door_";
        public const string LidBonePrefix = "Bone_Lid_";
        public const string HatchBonePrefix = "Bone_Hatch_";
        public const string LeafColliderName = "LeafCollider";
        public const string Exporter = "dune_barge_export.py";

        /// <summary>Model scale in the prefab: the export is 1:1 with the .blend, sized for 2 m crew.</summary>
        public const float ModelScale = 1.6f;

        /// <summary>Prefab name → FBX stem. Keys must match dune_barge_export.py's VARIANTS.</summary>
        public static readonly (string Prefab, string Stem)[] Variants =
        {
            ("DuneBarge", "dune_barge"),
            ("DuneBargeCompact", "dune_barge_compact"),
            ("DuneBargeLookout", "dune_barge_lookout"),
        };

        public static string PrefabPath(string prefab) => PrefabFolder + prefab + ".prefab";

        private const float DoorOpenDegrees = 100f;
        private const float LidOpenDegrees = 110f;
        private const float CarryHeadroom = 3.5f;       // m of carry volume above the highest roof, for riders on it
        private const float BodyMass = 60000f;
        private const float SyncPositionThreshold = 0.02f;
        private const float SyncRotationThreshold = 0.5f;

        [System.Serializable]
        private class Groups
        {
            public string[] interior;
            public string[] exterior;
        }

        [MenuItem("Tools/Vehicles/Build Dune Barge Prefabs")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[DuneBarge] Build out of Play mode: a prefab saved in Play mode ships with no save id.");
                return;
            }
            StaticPropBuilder.EnsureFolder(PrefabFolder.TrimEnd('/'));   // hull assets are created before the prefab
            foreach ((string prefab, string stem) in Variants) Build(prefab, stem);

            AssetDatabase.SaveAssets();
            foreach ((string prefab, _) in Variants)
            {
                NetworkObjectDefaults.KeepSceneMigrationSync(PrefabPath(prefab));
                // A NetworkObject created by script ships GlobalObjectIdHash 0; it resolves only against
                // the saved asset, so re-import and reserialize or the hash never reaches the YAML.
                AssetDatabase.ImportAsset(PrefabPath(prefab), ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.ForceReserializeAssets(Variants.Select(v => PrefabPath(v.Prefab)).ToArray());
            Debug.Log(NetworkPrefabRegistrar.Sync(out _, out _));
            if (!SaveableWiring.TryWirePrefabs())
                Debug.LogError("[DuneBarge] Save wiring failed.");
        }

        private static void Build(string prefab, string stem)
        {
            string modelPath = ArtFolder + stem + ".fbx";
            string groupsPath = ArtFolder + stem + "_groups.json";
            StaticPropBuilder.ConfigureImporter(modelPath);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            TextAsset groupsJson = AssetDatabase.LoadAssetAtPath<TextAsset>(groupsPath);
            if (model == null || groupsJson == null)
            {
                Debug.LogError($"[DuneBarge] Missing {modelPath} or {groupsPath}. Run {Exporter} first.");
                return;
            }

            GameObject root = new GameObject(prefab);
            try
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "Model";
                instance.transform.SetParent(root.transform, false);
                Vector3 ls = instance.transform.lossyScale;
                if ((ls - Vector3.one).sqrMagnitude > 1e-6f)
                    throw new System.InvalidOperationException($"Model imported at lossyScale {ls:F4}, not 1 - the islands are in metres.");

                ModelMarkerImport.CollisionCounts islands = ModelMarkerImport.BuildIslandColliders(
                    instance, CollisionPrefix, "Collision", PrefabFolder + prefab + "Hulls.asset", Exporter);
                AssetDatabase.SaveAssets();
                int ladders = ModelMarkerImport.GatherLadders(instance, LadderPrefix, "Ladders", Exporter);
                instance.transform.localScale = Vector3.one * ModelScale;
                Physics.SyncTransforms();

                var parts = root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                var hull = new List<Collider>(instance.GetComponentsInChildren<Collider>(true));
                List<Transform> doors = Named(parts, DoorBonePrefix);
                foreach (Transform hinge in doors) hull.Add(WireDoor(root, hinge, DoorOpenDegrees));
                foreach (Transform hinge in Named(parts, LidBonePrefix)) hull.Add(WireOutwardLid(root, hinge));
                var sills = new List<Transform>();
                foreach (Transform hinge in Named(parts, HatchBonePrefix)) hull.Add(WireHatch(root, parts, hinge, sills));
                foreach (HatchPassage passage in root.GetComponentsInChildren<HatchPassage>(true)) Rebind(passage, hull.ToArray());

                Bounds[] rooms = Rooms(root, parts);
                Groups groups = JsonUtility.FromJson<Groups>(groupsJson.text);
                Renderer[] interior = groups.interior.Where(parts.ContainsKey).SelectMany(n => parts[n].GetComponents<Renderer>()).ToArray();
                Transform[] openings = sills.Concat(Named(parts, OpeningPrefix)).Concat(doors).ToArray();
                root.AddComponent<InteriorReveal>().Configure(interior, rooms, openings);
                for (int i = 0; i < rooms.Length; i++)
                {
                    var shelterGo = new GameObject("Shelter_" + (i + 1));
                    shelterGo.transform.SetParent(root.transform, false);
                    shelterGo.AddComponent<SandstormShelter>().SetLocalVolume(rooms[i]);
                }
                WireRiding(root, instance);
                WireNetworkAndPersistence(root);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(prefab));
                Debug.Log($"[DuneBarge] Built {PrefabPath(prefab)}: collision {islands}, {ladders} ladders, {doors.Count} doors, " +
                          $"{sills.Count} crawl hatches, {rooms.Length} rooms, {interior.Length} interior renderers revealed on entry.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- doors, lids and hatches

        /// Hinge bone → ArticulatedPart; switch and collider on the leaf. Swings to whichever side of the
        /// doorway has less in the way (a bunk, a hammock, a wall), measured against the barge's colliders.
        private static Collider WireDoor(GameObject root, Transform hinge, float degrees)
        {
            Transform leaf = Leaf(hinge);
            Vector3 axis = HingeAxis(hinge);
            Collider hull = AddLeafCollider(leaf);
            float sign = FreerSide(root, hinge, axis, leaf, hull, degrees);
            AddSwitch(leaf.gameObject, AddPart(hinge, hinge.InverseTransformDirection(axis), sign * degrees));
            return hull;
        }

        /// A hatch lid used like a door (the stern hatch): swings out, away from the hull and up.
        private static Collider WireOutwardLid(GameObject root, Transform hinge)
        {
            Transform leaf = Leaf(hinge);
            Vector3 axis = HingeAxis(hinge);
            Collider hull = AddLeafCollider(leaf);
            float sign = SwingSign(hinge, axis, leaf, LidOpenDegrees, c => Outward(root, hinge, c));
            AddSwitch(leaf.gameObject, AddPart(hinge, hinge.InverseTransformDirection(axis), sign * LidOpenDegrees));
            return hull;
        }

        /// A side hatch: the lid opens outward and up; the HatchPassage sits on the lid (what the player looks
        /// at) and the lid's replicated switch on a collider-less child, so interacting always means "use the
        /// hatch", never just "flap the lid". A hatch with no crawl marks is an ordinary outward lid.
        private static Collider WireHatch(GameObject root, Dictionary<string, Transform> parts, Transform hinge, List<Transform> sills)
        {
            string side = hinge.name.Substring(HatchBonePrefix.Length);
            if (!parts.TryGetValue($"HATCH_{side}_Sill", out Transform sill)) return WireOutwardLid(root, hinge);

            Transform lid = Leaf(hinge);
            Vector3 axis = HingeAxis(hinge);
            Collider hull = AddLeafCollider(lid);
            float sign = SwingSign(hinge, axis, lid, LidOpenDegrees, c => Outward(root, hinge, c));
            ArticulatedPart part = AddPart(hinge, hinge.InverseTransformDirection(axis), sign * LidOpenDegrees);
            var switchGo = new GameObject("LidSwitch");
            switchGo.transform.SetParent(hinge, false);
            ArticulatedPartInteraction sw = AddSwitch(switchGo, part);
            lid.gameObject.AddComponent<HatchPassage>().Configure(sw, part, Find(parts, $"HATCH_{side}_Outer"), sill,
                                                                  Find(parts, $"HATCH_{side}_Inner"), null);
            sills.Add(sill);
            return hull;
        }

        private static Transform Leaf(Transform hinge) =>
            hinge.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault()?.transform
            ?? throw new System.InvalidOperationException($"{hinge.name} carries no mesh - re-rig with dune_barge_rig.py.");

        /// FBX bones point along their local Y; the rig lays each hinge bone along its hinge axis.
        private static Vector3 HingeAxis(Transform hinge) => (hinge.rotation * Vector3.up).normalized;

        /// How far a point is from the hull's centre line and above the hinge: bigger is further out.
        private static float Outward(GameObject root, Transform hinge, Vector3 offsetFromHinge)
        {
            Vector3 p = root.transform.InverseTransformPoint(hinge.position + offsetFromHinge);
            Vector3 h = root.transform.InverseTransformPoint(hinge.position);
            return new Vector2(p.x, p.z).magnitude - new Vector2(h.x, h.z).magnitude + (p.y - h.y);
        }

        /// The sign that moves the leaf's centre furthest along `score`.
        private static float SwingSign(Transform hinge, Vector3 axis, Transform leaf, float degrees, System.Func<Vector3, float> score)
        {
            Vector3 c = LeafOffset(hinge, axis, leaf);
            return score(Quaternion.AngleAxis(degrees, axis) * c) >= score(Quaternion.AngleAxis(-degrees, axis) * c) ? 1f : -1f;
        }

        /// The sign whose swept leaf, at full swing, overlaps fewer of the barge's own colliders (the scene the
        /// builder runs in may have anything else at the origin).
        private static float FreerSide(GameObject root, Transform hinge, Vector3 axis, Transform leaf, Collider own, float degrees)
        {
            LeafOffset(hinge, axis, leaf);
            Bounds local = leaf.GetComponent<MeshFilter>().sharedMesh.bounds;
            Vector3 half = Vector3.Scale(local.extents, leaf.lossyScale) * 0.9f;
            int Blocked(float sign)
            {
                Quaternion turn = Quaternion.AngleAxis(sign * degrees, axis);
                Vector3 centre = hinge.position + turn * (leaf.TransformPoint(local.center) - hinge.position);
                return Physics.OverlapBox(centre, half, turn * leaf.rotation)
                              .Count(c => c != own && !c.isTrigger && c.transform.IsChildOf(root.transform));
            }
            return Blocked(1f) <= Blocked(-1f) ? 1f : -1f;
        }

        private static ArticulatedPart AddPart(Transform hinge, Vector3 localAxis, float degrees)
        {
            ArticulatedPart part = hinge.gameObject.AddComponent<ArticulatedPart>();
            var so = new SerializedObject(part);
            so.FindProperty("motion").enumValueIndex = (int)ArticulatedPart.MotionType.Rotate;
            so.FindProperty("axis").vector3Value = localAxis.normalized;
            so.FindProperty("openAngle").floatValue = degrees;
            so.ApplyModifiedPropertiesWithoutUndo();
            return part;
        }

        private static ArticulatedPartInteraction AddSwitch(GameObject on, ArticulatedPart part)
        {
            ArticulatedPartInteraction sw = on.AddComponent<ArticulatedPartInteraction>();
            var so = new SerializedObject(sw);
            SerializedProperty list = so.FindProperty("parts");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = part;
            so.ApplyModifiedPropertiesWithoutUndo();
            return sw;
        }

        /// From the hinge to the leaf's centre. A leaf centred on its own hinge has no side to swing to, which
        /// is a rig fault rather than anything the builder can choose for it.
        private static Vector3 LeafOffset(Transform hinge, Vector3 axis, Transform leaf)
        {
            Vector3 offset = leaf.GetComponent<Renderer>().bounds.center - hinge.position;
            if (Vector3.ProjectOnPlane(offset, axis).sqrMagnitude < 1e-6f)
                throw new System.InvalidOperationException($"{leaf.name} is centred on its own hinge - re-rig with dune_barge_rig.py.");
            return offset;
        }

        /// The leaf's own convex hull, on a child of the leaf (the Interactor and the tests resolve it by
        /// name). It is exactly the shape dune_barge_export.py's Obstacles clears every set-down spot
        /// against, so the two tools share one model of the lid. An oriented box around a curved lid is
        /// larger than that hull and overlapped two ladder exits and a hatch mark per variant.
        private static Collider AddLeafCollider(Transform leaf)
        {
            var go = new GameObject(LeafColliderName);
            go.transform.SetParent(leaf, false);
            var hull = go.AddComponent<MeshCollider>();
            hull.sharedMesh = leaf.GetComponent<MeshFilter>().sharedMesh;
            hull.convex = true;
            Physics.SyncTransforms();
            return hull;
        }

        private static void Rebind(HatchPassage passage, Collider[] hull)
        {
            var so = new SerializedObject(passage);
            SerializedProperty list = so.FindProperty("hull");
            list.arraySize = hull.Length;
            for (int i = 0; i < hull.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = hull[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------- rooms, riding, netcode

        /// VOL_<n>_Min / _Max pairs → boxes in root space.
        private static Bounds[] Rooms(GameObject root, Dictionary<string, Transform> parts)
        {
            var rooms = new List<Bounds>();
            foreach (KeyValuePair<string, Transform> min in parts.Where(p => p.Key.StartsWith(RoomPrefix) && p.Key.EndsWith("_Min")))
            {
                Transform max = Find(parts, min.Key.Substring(0, min.Key.Length - 4) + "_Max");
                var b = new Bounds(root.transform.InverseTransformPoint(min.Value.position), Vector3.zero);
                b.Encapsulate(root.transform.InverseTransformPoint(max.position));
                rooms.Add(b);
            }
            if (rooms.Count == 0) throw new System.InvalidOperationException($"No {RoomPrefix}* rooms - re-export with {Exporter}.");
            return rooms.ToArray();
        }

        private static void WireRiding(GameObject root, GameObject model)
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;       // the hull is transform-driven; a dynamic body would fight it
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.mass = BodyMass;

            Bounds all = LocalBounds(root, model.GetComponentsInChildren<Renderer>(true));
            all.Encapsulate(all.max + Vector3.up * CarryHeadroom);
            var carryGo = new GameObject("CarryVolume");
            carryGo.transform.SetParent(root.transform, false);
            BoxCollider carry = carryGo.AddComponent<BoxCollider>();
            carry.isTrigger = true;
            carry.center = all.center;
            carry.size = all.size;
            WalkerPlatformCarrier carrier = root.AddComponent<WalkerPlatformCarrier>();
            var so = new SerializedObject(carrier);
            so.FindProperty("carryVolume").objectReferenceValue = carry;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// A transform-driven, player-ridden hull: networked (ownership may move to a driver, so it must not
        /// die with its owner), its doors on the message relay, saved and streamed with its chunk.
        private static void WireNetworkAndPersistence(GameObject root)
        {
            NetworkObject net = root.AddComponent<NetworkObject>();
            net.DontDestroyWithOwner = true;
            root.AddComponent<NetRelay>();
            ClientNetworkTransform sync = root.AddComponent<ClientNetworkTransform>();
            sync.UseUnreliableDeltas = true;
            sync.PositionThreshold = SyncPositionThreshold;
            sync.RotAngleThreshold = SyncRotationThreshold;
            sync.UseQuaternionSynchronization = true;
            sync.InLocalSpace = false;
            sync.Interpolate = true;
            root.AddComponent<SaveableEntity>();
            SceneTracked tracked = root.AddComponent<SceneTracked>();
            tracked.SetPolicy(SceneTracked.UnloadPolicy.Migrate);
            tracked.SetKeepChunksLoaded(false);
            root.AddComponent<UnderTerrainGuard>();
        }

        // ---------------------------------------------------------------- helpers

        private static List<Transform> Named(Dictionary<string, Transform> parts, string prefix) =>
            parts.Where(p => p.Key.StartsWith(prefix)).OrderBy(p => p.Key).Select(p => p.Value).ToList();

        private static Transform Find(Dictionary<string, Transform> parts, string name) =>
            parts.TryGetValue(name, out Transform t) ? t
                : throw new System.InvalidOperationException($"No '{name}' in the model - re-export with {Exporter}.");

        private static Bounds LocalBounds(GameObject root, IEnumerable<Renderer> renderers)
        {
            Bounds? acc = null;
            foreach (Renderer r in renderers)
            {
                Bounds w = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new Vector3((i & 1) == 0 ? w.min.x : w.max.x, (i & 2) == 0 ? w.min.y : w.max.y,
                                                 (i & 4) == 0 ? w.min.z : w.max.z);
                    Vector3 p = root.transform.InverseTransformPoint(corner);
                    if (acc == null) acc = new Bounds(p, Vector3.zero);
                    else { Bounds b = acc.Value; b.Encapsulate(p); acc = b; }
                }
            }
            return acc ?? throw new System.InvalidOperationException("no renderers to bound");
        }
    }
}
