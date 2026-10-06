// Wires the crash's oxygen beat onto its two prefabs. Idempotent: what is already there is updated in place (markers and
// components keep their fileIDs), and every save is read back.
//
//   * LooseOxygenPlant.prefab — the plant thrown out of the ship — becomes a Liftable: a handle bar across its control-head end,
//     the two grips on it, a heel under them and a foot under the far end, a kinematic body that is NOT interpolated (it is posed
//     in LateUpdate and an interpolating body puts itself back), and no NetworkTransform (its pose is derived on every machine
//     from the carrier's body, so a replicated one would fight it).
//   * PlayerShip.prefab — the mounted plant's fixture gains its cracks: a TorchRepairable with three seams on its front face, a
//     glowing crack on each and a light that follows the one being worked, its saver, and the mount's reference to it.
//
// PlayerShip.prefab is written by other sessions too: take the scratchpad's playership.lock before running the second item.
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Core.Persistence;
using SpaceGame.Gameplay;
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
        public const string LiftActionPath = "Assets/Game/ScriptableObjects/Animation/Actions/Mixamo/Lift Heavy.asset";
        public const string SetDownActionPath = "Assets/Game/ScriptableObjects/Animation/Actions/Mixamo/Set Down Heavy.asset";
        private const string BarMaterialPath = "Assets/Game/Art/Materials/Palette/Mat_Metal_Steel_Dark.mat";
        private const string SeamMaterialPath = "Assets/Game/Art/Materials/Vehicles/Mat_Emissive_Amber (DoubleSided).mat";

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

        [MenuItem("Tools/SpaceGame/Oxygen/Author Oxygen Plant Cracks (PlayerShip)")]
        public static void AuthorShipCracks()
        {
            var seamMaterial = AssetDatabase.LoadAssetAtPath<Material>(SeamMaterialPath);
            if (seamMaterial == null)
            {
                Debug.LogError($"[OxygenPlantRecovery] No seam material at {SeamMaterialPath}.");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(ShipPath);
            try
            {
                Transform fixture = FixtureOf(root);
                OxygenPlantMount mount = fixture.GetComponent<OxygenPlantMount>();
                TorchRepairable cracks = fixture.TryGetComponent(out TorchRepairable had) ? had : fixture.gameObject.AddComponent<TorchRepairable>();
                if (!fixture.TryGetComponent(out TorchRepairableSaveable _)) fixture.gameObject.AddComponent<TorchRepairableSaveable>();

                var seams = new List<Transform>();
                var glows = new List<Renderer>();
                for (int i = 0; i < SeamAt.Length; i++)
                {
                    Transform seam = Seam(fixture, i, seamMaterial);
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
                SerializedFields.Edit(mount, so => SerializedFields.Set(so, "damage", cracks));

                PrefabUtility.SaveAsPrefabAsset(root, ShipPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            VerifyShip();
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
        private static Transform Seam(Transform fixture, int index, Material material)
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
            float depth = SurfaceDepth(fixture, at, seam);
            seam.localPosition = new Vector3(at.x, at.y, depth + SeamStandOff);
            seam.localRotation = Quaternion.Euler(0f, 0f, SeamTilt[index]);
            seam.localScale = SeamSize;
            Renderer glow = seam.GetComponent<Renderer>();
            glow.sharedMaterial = material;
            glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return seam;
        }

        /// <summary>How far toward the aisle (+Z) the plant's front surface is at (x, y), in the fixture's frame: the nearest mesh hit.</summary>
        private static float SurfaceDepth(Transform fixture, Vector2 at, Transform skip)
        {
            float best = float.NegativeInfinity;
            var from = new Vector3(at.x, at.y, 3f);

            foreach (MeshFilter filter in fixture.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.transform == skip || filter.name.StartsWith(SeamPrefix)) continue;
                if (filter.name.StartsWith("Marker_")) continue;

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

                if (problems.Count > 0) Debug.LogError("[OxygenPlantRecovery] PlayerShip: " + string.Join("; ", problems));
                else Debug.Log("[OxygenPlantRecovery] The ship's oxygen plant has its cracks.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(saved);
            }
        }
    }
}
