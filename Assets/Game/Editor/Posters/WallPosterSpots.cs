// Finds where wanted posters can hang on a building's outer walls and gives the building a "WallPosters" board: a
// BountyNoticeBoard whose slots are those wall spots, so the town's hunts are pasted round the town by themselves, the
// same way its notice boards hang them (Bounties.md). Each building gets its own spots, found on its own walls.
//
// A spot is a patch of the building's visible shell (its Model renderers — never colliders, which are hulls and
// ramps) that is upright, flat over a whole poster, one material, not cloth or plants, clear in front for a reader,
// and away from every door. The best few, spaced apart, become slots, the one nearest the main door first: with one
// hunt open, that is the poster everybody walks past.
//
// Adds only. A building that already has WallPosters or a notice board of its own (a plaza) is left alone, so spots
// moved by hand are never overwritten. The search runs on a copy in a preview scene; the prefab is written once,
// with LoadPrefabContents / SaveAsPrefabAsset, and read back.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.World;
using SpaceGame.World.Bounties;
using SpaceGame.World.Posters;

namespace SpaceGame.EditorTools.Posters
{
    public static class WallPosterSpots
    {
        private const string Menu = "Tools/SpaceGame/Posters/Add Wall Poster Spots (Selected Buildings)";
        private const string Tag = "[WallPosterSpots]";
        private const string BoardName = "WallPosters";
        private const string ShellName = "Model";
        private const string PosterPrefabPath = "Assets/Game/Prefabs/Environment/Decorations/Posters/WantedPoster.prefab";

        // The player is 3 m tall: a sheet centred at these heights reads at eye level from the street.
        private static readonly float[] CentreHeights = { 2.3f, 2.0f, 2.6f };
        private const float PosterScale = 1.3f;
        private const int MaxSpots = 3;
        private const float AzimuthStepDegrees = 3f;
        private const float MinSpotSpacing = 5f;
        private const float DoorClearance = 3f;
        private const float ReaderClearance = 1.5f;
        private const float StandOff = 0.02f;
        private const float MaxUprightTilt = 10f;
        private const float MaxNormalSpread = 20f;
        private const float MaxDepthSpread = 0.08f;
        private const int SamplesPerSide = 3;
        private static readonly string[] SoftMaterials = { "Fabric", "Plant", "Glow", "Water", "Fibre" };

        private readonly struct Spot
        {
            public readonly Vector3 position;
            public readonly Vector3 normal;
            public readonly float depthSpread;

            public Spot(Vector3 position, Vector3 normal, float depthSpread)
            {
                this.position = position;
                this.normal = normal;
                this.depthSpread = depthSpread;
            }
        }

        [MenuItem(Menu)]
        private static void AddToSelected()
        {
            int added = 0;
            foreach (GameObject prefab in Selection.GetFiltered<GameObject>(SelectionMode.Assets))
            {
                string path = AssetDatabase.GetAssetPath(prefab);
                if (!path.EndsWith(".prefab")) continue;
                if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path)
                {
                    Debug.LogWarning($"{Tag} {prefab.name} is open in Prefab Mode; close it and run again.", prefab);
                    continue;
                }
                if (Add(path)) added++;
            }
            Debug.Log($"{Tag} {added} building(s) given wall poster spots.");
        }

        [MenuItem(Menu, true)]
        private static bool CanAdd() => !EditorApplication.isPlaying && Selection.GetFiltered<GameObject>(SelectionMode.Assets).Length > 0;

        /// <summary>Gives the prefab at <paramref name="path"/> its wall spots. False when it is skipped or none fit.</summary>
        public static bool Add(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab.transform.Find(BoardName) != null || prefab.GetComponentInChildren<BountyNoticeBoard>(true) != null)
            {
                Debug.Log($"{Tag} {prefab.name}: already has a board, left alone.", prefab);
                return false;
            }
            if (prefab.transform.Find(ShellName) == null)
            {
                Debug.LogWarning($"{Tag} {prefab.name}: no '{ShellName}' child, so no walls to search.", prefab);
                return false;
            }

            List<Spot> spots = Search(prefab, PosterSize() * PosterScale);
            if (spots.Count == 0)
            {
                Debug.Log($"{Tag} {prefab.name}: no wall takes a poster.", prefab);
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var board = new GameObject(BoardName);
                board.transform.SetParent(root.transform, false);
                var slots = new Transform[spots.Count];
                for (int i = 0; i < spots.Count; i++)
                {
                    var slot = new GameObject($"Slot_{i + 1}").transform;
                    slot.SetParent(board.transform, false);
                    slot.SetLocalPositionAndRotation(spots[i].position, Quaternion.LookRotation(spots[i].normal, Vector3.up));
                    slot.localScale = Vector3.one * PosterScale;
                    slots[i] = slot;
                }
                var notices = board.AddComponent<BountyNoticeBoard>();
                var so = new SerializedObject(notices);
                SerializedProperty list = so.FindProperty("slots");
                list.arraySize = slots.Length;
                for (int i = 0; i < slots.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved) Debug.LogError($"{Tag} {prefab.name}: saving the prefab failed.", prefab);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            var written = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.Find(BoardName);
            int hung = written != null && written.TryGetComponent(out BountyNoticeBoard check)
                ? new SerializedObject(check).FindProperty("slots").arraySize
                : 0;
            if (hung != spots.Count)
            {
                Debug.LogError($"{Tag} {prefab.name}: wrote {spots.Count} spot(s) but the saved prefab has {hung}.", prefab);
                return false;
            }
            Debug.Log($"{Tag} {prefab.name}: {spots.Count} wall spot(s).", prefab);
            return true;
        }

        /// <summary>The printed sheet's width and height at scale 1, measured off the poster prefab.</summary>
        private static Vector2 PosterSize()
        {
            var poster = AssetDatabase.LoadAssetAtPath<WantedPoster>(PosterPrefabPath);
            Vector3 size = poster.Sheet.bounds.size;
            return new Vector2(size.x, size.y);
        }

        // ── the search, on a copy in a preview scene ────────────────────────────────────────────

        private static List<Spot> Search(GameObject prefab, Vector2 poster)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview);
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                Transform shell = copy.transform.Find(ShellName);
                var walls = new List<MeshCollider>();
                var everything = new List<MeshCollider>();
                foreach (MeshFilter filter in copy.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer _)) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    everything.Add(collider);
                    if (filter.transform.IsChildOf(shell)) walls.Add(collider);
                }
                Physics.SyncTransforms();
                if (walls.Count == 0) return new List<Spot>();

                Bounds bounds = walls[0].bounds;
                foreach (MeshCollider wall in walls) bounds.Encapsulate(wall.bounds);
                List<Vector3> doors = copy.GetComponentsInChildren<SettlementEntrance>(true).Select(e => e.transform.position).ToList();

                var found = new List<Spot>();
                float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) + ReaderClearance + 1f;
                for (float azimuth = 0f; azimuth < 360f; azimuth += AzimuthStepDegrees)
                {
                    Vector3 outward = Quaternion.Euler(0f, azimuth, 0f) * Vector3.forward;
                    foreach (float height in CentreHeights)
                    {
                        Vector3 axis = new Vector3(bounds.center.x, height, bounds.center.z);
                        if (TrySpot(new Ray(axis + outward * reach, -outward), reach, walls, everything, doors, poster, out Spot spot))
                        {
                            found.Add(spot);
                            break;
                        }
                    }
                }

                return Pick(found, doors);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        /// <summary>Where <paramref name="ray"/> first meets the building, if a poster hangs there.</summary>
        private static bool TrySpot(Ray ray, float reach, List<MeshCollider> walls, List<MeshCollider> everything,
                                    List<Vector3> doors, Vector2 poster, out Spot spot)
        {
            spot = default;
            if (!Nearest(everything, ray, reach, out RaycastHit hit) || !walls.Contains((MeshCollider)hit.collider)) return false;

            Vector3 normal = hit.normal;
            if (Vector3.Angle(normal, Vector3.ProjectOnPlane(normal, Vector3.up)) > MaxUprightTilt) return false;
            normal = Vector3.ProjectOnPlane(normal, Vector3.up).normalized;
            if (Vector3.Dot(normal, -ray.direction) < 0.5f) return false;

            int material = MaterialAt(hit);
            if (material < 0 || IsSoft(hit.collider.GetComponent<MeshRenderer>().sharedMaterials[material])) return false;

            foreach (Vector3 door in doors)
                if (Vector2.Distance(new Vector2(door.x, door.z), new Vector2(hit.point.x, hit.point.z)) < DoorClearance) return false;

            // The whole sheet must lie on the same flat, upright patch of the same material.
            Vector3 right = Vector3.Cross(Vector3.up, normal);
            float nearest = float.MaxValue, farthest = float.MinValue;
            for (int i = 0; i < SamplesPerSide; i++)
            for (int j = 0; j < SamplesPerSide; j++)
            {
                float u = (i / (SamplesPerSide - 1f) - 0.5f) * poster.x;
                float v = (j / (SamplesPerSide - 1f) - 0.5f) * poster.y;
                Vector3 onWall = hit.point + right * u + Vector3.up * v;
                var probe = new Ray(onWall + normal * ReaderClearance, -normal);
                if (!Nearest(everything, probe, ReaderClearance * 2f, out RaycastHit sample)) return false;
                if (sample.collider != hit.collider || MaterialAt(sample) != material) return false;
                if (Vector3.Angle(sample.normal, normal) > MaxNormalSpread) return false;
                float depth = Vector3.Dot(sample.point - hit.point, normal);
                nearest = Mathf.Min(nearest, depth);
                farthest = Mathf.Max(farthest, depth);
            }
            if (farthest - nearest > MaxDepthSpread) return false;

            // A reader stands in front of it: nothing but air for a stride out from every corner and the centre.
            Vector3 face = hit.point + normal * (farthest + StandOff);
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                Vector3 from = face + normal * StandOff + right * (i * poster.x * 0.5f) + Vector3.up * (j * poster.y * 0.5f);
                if (Nearest(everything, new Ray(from, normal), ReaderClearance, out _)) return false;
            }

            spot = new Spot(face, normal, farthest - nearest);
            return true;
        }

        /// <summary>The flattest spots, spaced apart, the one nearest the first entrance (the main door) first.</summary>
        private static List<Spot> Pick(List<Spot> found, List<Vector3> doors)
        {
            var picked = new List<Spot>();
            foreach (Spot spot in found.OrderBy(s => s.depthSpread))
            {
                if (picked.Count == MaxSpots) break;
                if (picked.Any(p => Vector3.Distance(p.position, spot.position) < MinSpotSpacing)) continue;
                picked.Add(spot);
            }
            if (doors.Count > 0) picked = picked.OrderBy(s => Vector3.Distance(s.position, doors[0])).ToList();
            return picked;
        }

        private static bool Nearest(List<MeshCollider> colliders, Ray ray, float distance, out RaycastHit nearest)
        {
            nearest = default;
            bool any = false;
            foreach (MeshCollider collider in colliders)
            {
                if (!collider.Raycast(ray, out RaycastHit hit, distance)) continue;
                if (any && hit.distance >= nearest.distance) continue;
                nearest = hit;
                any = true;
            }
            return any;
        }

        /// <summary>The submesh (= material slot) of the triangle <paramref name="hit"/> struck, or -1.</summary>
        private static int MaterialAt(RaycastHit hit)
        {
            Mesh mesh = ((MeshCollider)hit.collider).sharedMesh;
            int index = hit.triangleIndex * 3;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                var sub = mesh.GetSubMesh(i);
                if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount) return i;
            }
            return -1;
        }

        private static bool IsSoft(Material material) =>
            material == null || SoftMaterials.Any(soft => material.name.Contains(soft));
    }
}
