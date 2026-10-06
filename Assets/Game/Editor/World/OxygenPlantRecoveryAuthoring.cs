// Wires the crash's oxygen beat onto its two prefabs. Idempotent: what is already there is updated in place (markers and
// components keep their fileIDs), and every save is read back.
//
//   * LooseOxygenPlant.prefab — the plant thrown out of the ship — becomes a Liftable: a handle bar across its control-head end,
//     the two grips on it, a heel under them and a foot under the far end, a kinematic body that is NOT interpolated (it is posed
//     in LateUpdate and an interpolating body puts itself back), and no NetworkTransform (its pose is derived on every machine
//     from the carrier's body, so a replicated one would fight it).
//   * PlayerShip.prefab — the mounted plant's fixture gains its cracks (a TorchRepairable with three seams on its front face, a
//     glowing crack on each and a light that follows the one being worked, its saver), its status lamps (green running, red
//     cracked, amber unpowered), and the EmptyMount shown while the plant is out: the torn frame, three ripped cables that
//     spark and crackle, and the LiftDock volume the carried plant is set in through. Also the transmitter fire's ignite
//     delay, which the plant coming online now sets off almost at once.
//
// PlayerShip.prefab is written by other sessions too: take the scratchpad's playership.lock before running the second item.
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class OxygenPlantRecoveryAuthoring
    {
        public const string LoosePlantPath = "Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/LooseOxygenPlant.prefab";
        public const string ShipPath = "Assets/Game/Prefabs/Agents/Vehicles/Spacecraft/PlayerShip.prefab";
        public const string LiftActionPath = "Assets/Game/ScriptableObjects/Animation/Actions/Mixamo/Heave Load Up.asset";
        public const string SetDownActionPath = "Assets/Game/ScriptableObjects/Animation/Actions/Mixamo/Set Load Down.asset";
        private const string BarMaterialPath = "Assets/Game/Art/Materials/Palette/Mat_Metal_Steel_Dark.mat";
        private const string SeamMaterialPath = "Assets/Game/Art/Materials/Vehicles/Mat_Emissive_Amber (DoubleSided).mat";
        private const string LampMaterialPath = "Assets/Game/Art/Materials/Vehicles/Mat_Emissive_Green_CRT (DoubleSided).mat";
        public const string EmptyFramePath = "Assets/Game/Art/Models/Vehicles/PlayerShip/oxygen_mount_empty.fbx";
        private const string SparksPath = "Assets/Game/Prefabs/Items/ShipParts/TransmitterSparks.prefab";

        // ── The loose plant, in its own frame (it lies on its back, control head toward -Z; measured off its box collider) ──

        public const string GripLeftName = "Lift_Grip_L";
        public const string GripRightName = "Lift_Grip_R";
        public const string HeelName = "Lift_Heel";
        public const string FootName = "Lift_Foot";
        private const string HandleName = "Lift_Handle";

        private static readonly Vector3 GripLeft = new(-0.28f, 0.62f, -2.02f);
        private static readonly Vector3 GripRight = new(0.28f, 0.62f, -2.02f);
        private static readonly Vector3 Heel = new(0f, 0f, -1.9f);
        private static readonly Vector3 Foot = new(0f, 0f, 1.9f);

        // The bar the hands close on, standing off the end on two posts.
        private const float BarRadius = 0.035f;
        private const float BarHalfLength = 0.42f;
        private const float PostRadius = 0.03f;
        private static readonly Vector3 PostLeft = new(-0.38f, 0.62f, -1.96f);
        private static readonly Vector3 PostRight = new(0.38f, 0.62f, -1.96f);
        private const float PostHalfLength = 0.07f;

        // ── The cracks, on the mounted fixture's front face (its +Z, toward the aisle), in its own frame ──

        public const string SeamPrefix = "Seam_";
        private const string SeamLightName = "SeamLight";
        private static readonly Vector2[] SeamAt = { new(-0.3f, 0.45f), new(0.32f, 1.2f), new(-0.18f, 2.0f) };
        private static readonly float[] SeamTilt = { 18f, -25f, 8f };
        private static readonly Vector3 SeamSize = new(0.035f, 0.3f, 0.02f);
        private const float SeamStandOff = 0.008f;

        // ── The empty mount, the dock and the lamps, in the fixture's own frame (deck at y 0, wall at z 0, aisle +Z) ──

        public const string EmptyMountName = "EmptyMount";
        private const string DockName = "PlantDock";
        private static readonly Vector3 DockCentre = new(0f, 1.1f, 0.7f);
        private static readonly Vector3 DockSize = new(1.4f, 2.3f, 1.4f);
        private static readonly string[] CableEnds = { "Marker_CableEnd_A", "Marker_CableEnd_B", "Marker_CableEnd_C" };

        public const string LampPrefix = "StatusLamp_";
        private static readonly Vector2[] LampAt = { new(0.16f, 1.74f), new(0.24f, 1.74f), new(0.32f, 1.74f) };
        private const float LampDiameter = 0.04f;

        // The transmitter fire catches this long after the plant comes online (ShipPartFireTuning.igniteDelay).
        public const float FireIgniteDelay = 3f;

        // How close, flat, the carrier must stand to the mount to set the plant in (OxygenPlantMount.mountRadius).
        public const float MountRadius = 5f;

        [MenuItem("Tools/SpaceGame/Oxygen/Author Liftable Loose Plant")]
        public static void AuthorLoosePlant()
        {
            var lift = AssetDatabase.LoadAssetAtPath<CharacterAction>(LiftActionPath);
            var setDown = AssetDatabase.LoadAssetAtPath<CharacterAction>(SetDownActionPath);
            if (lift == null || setDown == null)
            {
                Debug.LogError($"[OxygenPlantRecovery] The lift actions are missing ({LiftActionPath}, {SetDownActionPath}).");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(LoosePlantPath);
            try
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                if (root.TryGetComponent(out NetworkTransform replicated)) Object.DestroyImmediate(replicated);

                Rigidbody body = root.GetComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.None;

                Liftable liftable = root.TryGetComponent(out Liftable have) ? have : root.AddComponent<Liftable>();
                Transform left = Marker(root.transform, GripLeftName, GripLeft);
                Transform right = Marker(root.transform, GripRightName, GripRight);
                Transform heel = Marker(root.transform, HeelName, Heel);
                Transform foot = Marker(root.transform, FootName, Foot);
                Handle(root.transform);

                Transform dust = root.transform.Find("Dust");
                SerializedFields.Edit(liftable, so =>
                {
                    SerializedFields.SetString(so, "loadId", "oxygen-plant");
                    SerializedFields.SetString(so, "displayName", "Oxygen plant");
                    SerializedFields.Set(so, "gripLeft", left);
                    SerializedFields.Set(so, "gripRight", right);
                    SerializedFields.Set(so, "heel", heel);
                    SerializedFields.Set(so, "foot", foot);
                    SerializedFields.Set(so, "liftAction", lift);
                    SerializedFields.Set(so, "setDownAction", setDown);
                    SerializedFields.Set(so, "dust", dust != null ? dust.GetComponent<ParticleSystem>() : null);
                });

                PrefabUtility.SaveAsPrefabAsset(root, LoosePlantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            VerifyLoosePlant();
        }

        [MenuItem("Tools/SpaceGame/Oxygen/Author Oxygen Plant Fixture (PlayerShip)")]
        public static void AuthorShipFixture()
        {
            var seamMaterial = AssetDatabase.LoadAssetAtPath<Material>(SeamMaterialPath);
            var lampMaterial = AssetDatabase.LoadAssetAtPath<Material>(LampMaterialPath);
            var frame = AssetDatabase.LoadAssetAtPath<GameObject>(EmptyFramePath);
            var sparks = AssetDatabase.LoadAssetAtPath<GameObject>(SparksPath);
            if (seamMaterial == null || lampMaterial == null || frame == null || sparks == null)
            {
                Debug.LogError($"[OxygenPlantRecovery] Missing an input: {SeamMaterialPath}, {LampMaterialPath}, {EmptyFramePath} or {SparksPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ShipPath);
            try
            {
                Transform fixture = FixtureOf(root);
                OxygenPlantMount mount = fixture.GetComponent<OxygenPlantMount>();
                GameObject empty = EmptyMount(fixture, mount, frame, sparks);

                TorchRepairable cracks = fixture.TryGetComponent(out TorchRepairable had) ? had : fixture.gameObject.AddComponent<TorchRepairable>();
                if (!fixture.TryGetComponent(out TorchRepairableSaveable _)) fixture.gameObject.AddComponent<TorchRepairableSaveable>();

                var seams = new List<Transform>();
                var glows = new List<Renderer>();
                for (int i = 0; i < SeamAt.Length; i++)
                {
                    Transform seam = Seam(fixture, i, seamMaterial, empty.transform);
                    seams.Add(seam);
                    glows.Add(seam.GetComponent<Renderer>());
                }

                Light light = SeamLight(fixture, seams[0].position);

                SerializedFields.Edit(cracks, so =>
                {
                    SerializedFields.SetObjects(so, "seams", seams);
                    SerializedFields.SetObjects(so, "seamGlows", glows);
                    SerializedFields.Set(so, "seamLight", light);
                });

                OxygenPlantStatusLights lights = fixture.TryGetComponent(out OxygenPlantStatusLights hadLights)
                    ? hadLights
                    : fixture.gameObject.AddComponent<OxygenPlantStatusLights>();
                var lamps = new List<Renderer>();
                for (int i = 0; i < LampAt.Length; i++) lamps.Add(Lamp(fixture, i, lampMaterial, empty.transform));
                SerializedFields.Edit(lights, so =>
                {
                    SerializedFields.Set(so, "mount", mount);
                    SerializedFields.SetObjects(so, "lamps", lamps);
                });

                SerializedFields.Edit(mount, so =>
                {
                    SerializedFields.Set(so, "damage", cracks);
                    SerializedFields.Set(so, "emptyMount", empty);
                    SerializedFields.SetFloat(so, "mountRadius", MountRadius);
                });

                ShipPartFire fire = root.GetComponentInChildren<ShipPartFire>(true);
                if (fire != null) SerializedFields.Edit(fire, so => SerializedFields.SetFloat(so, "tuning.igniteDelay", FireIgniteDelay));

                PrefabUtility.SaveAsPrefabAsset(root, ShipPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            VerifyShip();
        }

        /// <summary>
        /// What the wall shows while the plant is out, under one inactive child: the torn frame (its own model), a spark
        /// emitter and a short circuit at each ripped cable end (read off the model's markers, never parented to them), and the
        /// dock volume. Rebuilt from scratch on every run.
        /// </summary>
        private static GameObject EmptyMount(Transform fixture, OxygenPlantMount mount, GameObject frame, GameObject sparks)
        {
            Transform old = fixture.Find(EmptyMountName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var empty = new GameObject(EmptyMountName);
            empty.transform.SetParent(fixture, false);

            var model = (GameObject)PrefabUtility.InstantiatePrefab(frame, empty.transform);
            model.name = "Frame";
            foreach (Collider c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            foreach (string marker in CableEnds)
            {
                Transform end = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == marker);
                if (end == null) throw new System.InvalidOperationException($"[OxygenPlantRecovery] {EmptyFramePath} has no {marker}.");

                var spark = (GameObject)PrefabUtility.InstantiatePrefab(sparks, empty.transform);
                spark.name = "CableSparks_" + marker.Substring(marker.Length - 1);
                spark.transform.position = end.position;
                spark.transform.rotation = fixture.rotation * Quaternion.Euler(-60f, 0f, 0f);
                var cable = spark.AddComponent<SparkingCable>();
                SerializedFields.Edit(cable, so => SerializedFields.Set(so, "sparks", spark.GetComponentInChildren<ParticleSystem>(true)));
            }

            var dock = new GameObject(DockName);
            dock.transform.SetParent(empty.transform, false);
            var box = dock.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = DockCentre;
            box.size = DockSize;
            var lift = dock.AddComponent<LiftDock>();
            SerializedFields.Edit(lift, so => SerializedFields.Set(so, "destination", mount));

            empty.SetActive(false);
            return empty;
        }

        /// <summary>One status lamp: a small emissive bead on the plant's front face, placed on the mesh like a seam.</summary>
        private static Renderer Lamp(Transform fixture, int index, Material material, Transform skip)
        {
            string name = LampPrefix + (char)('A' + index);
            Transform lamp = fixture.Find(name);
            if (lamp == null)
            {
                GameObject made = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                made.name = name;
                Object.DestroyImmediate(made.GetComponent<Collider>());
                lamp = made.transform;
                lamp.SetParent(fixture, false);
            }

            Vector2 at = LampAt[index];
            lamp.localPosition = new Vector3(at.x, at.y, SurfaceDepth(fixture, at, lamp, skip) + LampDiameter * 0.25f);
            lamp.localRotation = Quaternion.identity;
            lamp.localScale = Vector3.one * LampDiameter;
            Renderer renderer = lamp.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        /// <summary>
        /// The crew's spare bottles on the ship's gear wall come down EMPTY: the crash vents everyone's own, and filling one
        /// at the plant is the refill beat (RefillAirStep). Only new worlds see it — a loaded wall keeps its saved contents.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Oxygen/Author Empty Crew Bottles (Gear Wall)")]
        public static void AuthorEmptyCrewBottles()
        {
            const string wallPath = "Assets/Game/Prefabs/Items/Equipment/InventoryWall.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(wallPath);
            try
            {
                SerializedFields.Edit(root.GetComponent<WallInventory>(), so => SerializedFields.SetFloat(so, "perCrewCharge", 0f));
                PrefabUtility.SaveAsPrefabAsset(root, wallPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(wallPath).GetComponent<WallInventory>();
            float charge = new SerializedObject(saved).FindProperty("perCrewCharge").floatValue;
            if (charge > 0f) Debug.LogError($"[OxygenPlantRecovery] {wallPath} still lays its crew bottles on at {charge:0.00}.");
            else Debug.Log("[OxygenPlantRecovery] The gear wall's crew bottles come down empty.");
        }

        // ── The parts ────────────────────────────────────────────────────────

        private static Transform Marker(Transform root, string name, Vector3 position)
        {
            Transform marker = root.Find(name);
            if (marker == null)
            {
                marker = new GameObject(name).transform;
                marker.SetParent(root, false);
            }

            marker.localPosition = position;
            marker.localRotation = Quaternion.identity;
            marker.localScale = Vector3.one;
            return marker;
        }

        /// <summary>A steel bar across the control-head end on two short posts: the handle the prompt names.</summary>
        private static void Handle(Transform root)
        {
            var steel = AssetDatabase.LoadAssetAtPath<Material>(BarMaterialPath);
            Transform handle = root.Find(HandleName);
            if (handle == null)
            {
                handle = new GameObject(HandleName).transform;
                handle.SetParent(root, false);
            }

            Vector3 middle = (GripLeft + GripRight) * 0.5f;
            Rod(handle, "Bar", middle, Vector3.right, BarHalfLength, BarRadius, steel);
            Rod(handle, "Post_L", PostLeft, Vector3.forward, PostHalfLength, PostRadius, steel);
            Rod(handle, "Post_R", PostRight, Vector3.forward, PostHalfLength, PostRadius, steel);
        }

        private static void Rod(Transform parent, string name, Vector3 at, Vector3 along, float halfLength, float radius, Material material)
        {
            Transform rod = parent.Find(name);
            if (rod == null)
            {
                GameObject made = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                made.name = name;
                Object.DestroyImmediate(made.GetComponent<Collider>());
                rod = made.transform;
                rod.SetParent(parent, false);
            }

            rod.localPosition = at;
            rod.localRotation = Quaternion.FromToRotation(Vector3.up, along);
            rod.localScale = new Vector3(radius * 2f, halfLength, radius * 2f);
            rod.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>
        /// One glowing crack on the fixture's front face: placed where a ray from the aisle meets the plant's own mesh, so it lies
        /// on the surface rather than floating in front of the body collider.
        /// </summary>
        private static Transform Seam(Transform fixture, int index, Material material, Transform skip)
        {
            string name = SeamPrefix + (char)('A' + index);
            Transform seam = fixture.Find(name);
            if (seam == null)
            {
                GameObject made = GameObject.CreatePrimitive(PrimitiveType.Cube);
                made.name = name;
                Object.DestroyImmediate(made.GetComponent<Collider>());
                seam = made.transform;
                seam.SetParent(fixture, false);
            }

            Vector2 at = SeamAt[index];
            float depth = SurfaceDepth(fixture, at, seam, skip);
            seam.localPosition = new Vector3(at.x, at.y, depth + SeamStandOff);
            seam.localRotation = Quaternion.Euler(0f, 0f, SeamTilt[index]);
            seam.localScale = SeamSize;
            Renderer glow = seam.GetComponent<Renderer>();
            glow.sharedMaterial = material;
            glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return seam;
        }

        /// <summary>How far toward the aisle (+Z) the plant's front surface is at (x, y), in the fixture's frame: the nearest mesh hit.</summary>
        private static float SurfaceDepth(Transform fixture, Vector2 at, Transform self, Transform skip)
        {
            float best = float.NegativeInfinity;
            var from = new Vector3(at.x, at.y, 3f);

            foreach (MeshFilter filter in fixture.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.transform == self || filter.transform.IsChildOf(skip)) continue;
                if (filter.name.StartsWith(SeamPrefix) || filter.name.StartsWith(LampPrefix) || filter.name.StartsWith("Marker_")) continue;

                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;
                Matrix4x4 toFixture = fixture.worldToLocalMatrix * filter.transform.localToWorldMatrix;

                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = toFixture.MultiplyPoint3x4(vertices[triangles[t]]);
                    Vector3 b = toFixture.MultiplyPoint3x4(vertices[triangles[t + 1]]);
                    Vector3 c = toFixture.MultiplyPoint3x4(vertices[triangles[t + 2]]);
                    if (RayHitsTriangle(from, Vector3.back, a, b, c, out float distance))
                        best = Mathf.Max(best, from.z - distance);
                }
            }

            if (float.IsNegativeInfinity(best))
                throw new System.InvalidOperationException($"[OxygenPlantRecovery] No plant surface behind seam point {at}.");
            return best;
        }

        // Möller–Trumbore, both faces.
        private static bool RayHitsTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 ab = b - a, ac = c - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float det = Vector3.Dot(ab, p);
            if (Mathf.Abs(det) < 1e-8f) return false;

            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(direction, q) * inv;
            if (v < 0f || u + v > 1f) return false;

            distance = Vector3.Dot(ac, q) * inv;
            return distance > 0f;
        }

        private static Light SeamLight(Transform fixture, Vector3 at)
        {
            Transform holder = fixture.Find(SeamLightName);
            if (holder == null)
            {
                holder = new GameObject(SeamLightName).transform;
                holder.SetParent(fixture, false);
            }

            holder.position = at;
            Light light = holder.TryGetComponent(out Light had) ? had : holder.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.5f, 0.15f);
            light.range = 1.6f;
            light.intensity = 1.5f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        private static Transform FixtureOf(GameObject ship)
        {
            OxygenPlantMount mount = ship.GetComponentInChildren<OxygenPlantMount>(true);
            if (mount == null) throw new System.InvalidOperationException("[OxygenPlantRecovery] PlayerShip has no OxygenPlantMount.");
            return mount.transform;
        }

        // ── Read back ────────────────────────────────────────────────────────

        private static void VerifyLoosePlant()
        {
            GameObject saved = PrefabUtility.LoadPrefabContents(LoosePlantPath);
            try
            {
                Liftable liftable = saved.GetComponent<Liftable>();
                var problems = new List<string>();
                if (liftable == null) problems.Add("no Liftable");
                else if (!liftable.Shape.IsValid) problems.Add("the grips and the foot coincide");
                if (saved.GetComponent<NetworkTransform>() != null) problems.Add("a NetworkTransform is still there");
                if (saved.GetComponents<Component>().Any(c => c == null)) problems.Add("a missing script is still there");
                if (saved.GetComponent<Rigidbody>().interpolation != RigidbodyInterpolation.None) problems.Add("the body still interpolates");

                if (problems.Count > 0) Debug.LogError("[OxygenPlantRecovery] LooseOxygenPlant: " + string.Join("; ", problems));
                else Debug.Log("[OxygenPlantRecovery] LooseOxygenPlant is liftable.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }

        private static void VerifyShip()
        {
            GameObject saved = PrefabUtility.LoadPrefabContents(ShipPath);
            try
            {
                Transform fixture = FixtureOf(saved);
                var cracks = fixture.GetComponent<TorchRepairable>();
                var problems = new List<string>();
                if (cracks == null) problems.Add("no TorchRepairable");
                else if (cracks.SeamCount != SeamAt.Length) problems.Add($"{cracks.SeamCount} seams, not {SeamAt.Length}");
                if (fixture.GetComponent<TorchRepairableSaveable>() == null) problems.Add("no TorchRepairableSaveable");
                if (fixture.GetComponent<OxygenPlantMount>().Damage != cracks) problems.Add("the mount does not reference its cracks");
                Transform empty = fixture.Find(EmptyMountName);
                if (empty == null || empty.gameObject.activeSelf) problems.Add("no inactive EmptyMount");
                else if (empty.GetComponentInChildren<LiftDock>(true) == null) problems.Add("the empty mount has no dock");
                else if (empty.GetComponentsInChildren<SparkingCable>(true).Length != CableEnds.Length) problems.Add("the cables do not all spark");
                if (fixture.GetComponent<OxygenPlantStatusLights>() == null) problems.Add("no status lamps");
                ShipPartFire fire = saved.GetComponentInChildren<ShipPartFire>(true);
                if (fire != null && !Mathf.Approximately(new SerializedObject(fire).FindProperty("tuning.igniteDelay").floatValue, FireIgniteDelay))
                    problems.Add("the transmitter fire still waits its old delay");

                if (problems.Count > 0) Debug.LogError("[OxygenPlantRecovery] PlayerShip: " + string.Join("; ", problems));
                else Debug.Log("[OxygenPlantRecovery] The ship's oxygen plant has its cracks, lamps, empty mount and dock.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }
    }
}
