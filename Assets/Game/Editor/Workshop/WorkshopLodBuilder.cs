// Gives every drawn piece of Workshop.prefab a LODGroup of its own, the last step of Rebuild Workshop Prefab.
//
// The workshop is a mountain with a maze inside it. From outside only the mountain's skin and the ship gate are
// seen; the rooms, tunnels, machinery and furniture (most of its 1.6 M triangles) are behind rock and are only
// ever seen from within, or down the ship tunnel through the open gate. So a piece inside is culled once the
// camera is well clear of it, and a piece outside is never culled on the map. Both drop to the FBX's Mesh LOD
// levels (generateMeshLods, set by the prefab builder) with distance: a copy renderer beside the piece forces
// a level, since a LODGroup tier cannot choose one (LevelOfDetail.md).
//
// Distances are measured past the piece's own edge (half its bounds' diagonal from its centre), so a room you
// stand in is at full detail wherever in it you stand, and a tunnel net spanning the whole maze is too.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceGame.EditorTools
{
    public static class WorkshopLodBuilder
    {
        /// <summary>How far past its edge a piece keeps each level, in metres.</summary>
        private readonly struct Reach
        {
            public readonly float full;
            public readonly float half;
            public readonly float culled;

            public Reach(float full, float half, float culled)
            {
                this.full = full;
                this.half = half;
                this.culled = culled;
            }
        }

        // Inside: past a room's own edge the rock is between it and the camera, except down the ship tunnel; the
        // main room is still drawn from the lane in front of the open gate. It keeps half detail until it is culled:
        // the lower levels shed the thin clay walls and ceilings a room is seen from inside by (renders, 2026-10-12).
        // Outside: the skin is the landmark, never culled on the 4 x 3 km map (its diagonal), and drops to low
        // detail only where its stripes are a few pixels wide.
        public const float InsideCulledMetres = 150f;
        private static readonly Reach Inside = new(full: 25f, half: InsideCulledMetres, culled: InsideCulledMetres);
        private static readonly Reach Outside = new(full: 100f, half: 300f, culled: 5000f);

        /// <summary>What is seen from outside the mountain: its skin, the ship tunnel, and the gate in it.</summary>
        private static readonly string[] OutsideParts = { "Static_Mountain", "Static_ShipTunnel" };
        private const string GateRoot = "GearDoor";

        // Mesh LOD levels the two lower tiers force: each level halves the triangles.
        private const int HalfDetailMeshLod = 1;
        private const int LowDetailMeshLod = 3;

        // A copy of a part smaller than this casts no shadow: at that range it would be a speck in the shadow map.
        private const float ShadowlessBelowMetres = 2f;

        // The player camera's vertical field of view (GameSettings default) the distances are converted with.
        private const float ReferenceFovDegrees = 60f;

        /// <summary>A screen height of 1 fills the screen, and a transition above it is never reached.</summary>
        private const float LargestTransition = 0.999f;
        private const float TransitionStep = 0.001f;

        public const string HalfDetailSuffix = "_LOD1";
        public const string LowDetailSuffix = "_LOD2";

        /// <summary>A LODGroup on every enabled mesh renderer under <paramref name="root"/> not already in one.</summary>
        public static string Attach(GameObject root)
        {
            var grouped = new HashSet<Renderer>();
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true))
            foreach (LOD lod in group.GetLODs())
            foreach (Renderer renderer in lod.renderers)
                if (renderer != null) grouped.Add(renderer);

            Transform gate = root.transform.Find(GateRoot);
            int groups = 0, levelled = 0, outside = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || grouped.Contains(renderer)) continue;
                if (!renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;

                bool seenOutside = IsOutside(renderer.transform, gate);
                if (Group(renderer, filter.sharedMesh, seenOutside ? Outside : Inside)) levelled++;
                if (seenOutside) outside++;
                groups++;
            }
            return $"LODs: {groups} groups ({levelled} with Mesh LOD levels, {outside} seen from outside)";
        }

        private static bool IsOutside(Transform piece, Transform gate)
        {
            if (gate != null && piece.IsChildOf(gate)) return true;
            foreach (string part in OutsideParts)
                if (piece.name.StartsWith(part)) return true;
            return false;
        }

        /// <summary>Group one renderer; true if its mesh has levels and it got lower-detail copies.</summary>
        private static bool Group(MeshRenderer original, Mesh mesh, Reach reach)
        {
            Bounds bounds = original.bounds;
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float edge = bounds.extents.magnitude;
            int deepest = mesh.lodCount - 1;

            var tiers = new List<LOD>();
            if (deepest > 0)
            {
                // A mesh with levels is never Batching Static: the batched copy draws its full level at every range.
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(original.gameObject) & ~StaticEditorFlags.BatchingStatic;
                GameObjectUtility.SetStaticEditorFlags(original.gameObject, flags);

                MeshRenderer half = Copy(original, mesh, HalfDetailSuffix, Mathf.Min(HalfDetailMeshLod, deepest), size, flags);
                tiers.Add(new LOD(HeightAt(size, edge + reach.full), new Renderer[] { original }));
                if (deepest > HalfDetailMeshLod && reach.half < reach.culled)
                {
                    MeshRenderer low = Copy(original, mesh, LowDetailSuffix, Mathf.Min(LowDetailMeshLod, deepest), size, flags);
                    tiers.Add(new LOD(HeightAt(size, edge + reach.half), new Renderer[] { half }));
                    tiers.Add(new LOD(HeightAt(size, edge + reach.culled), new Renderer[] { low }));
                }
                else
                {
                    tiers.Add(new LOD(HeightAt(size, edge + reach.culled), new Renderer[] { half }));
                }
            }
            else
            {
                tiers.Add(new LOD(HeightAt(size, edge + reach.culled), new Renderer[] { original }));
            }

            LODGroup group = original.gameObject.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(Descending(tiers));
            group.RecalculateBounds();
            return deepest > 0;
        }

        /// <summary>A renderer beside <paramref name="original"/> drawing the same mesh at a forced Mesh LOD level.</summary>
        private static MeshRenderer Copy(MeshRenderer original, Mesh mesh, string suffix, int level, float size, StaticEditorFlags flags)
        {
            var child = new GameObject(original.name + suffix) { layer = original.gameObject.layer };
            child.transform.SetParent(original.transform, false);
            GameObjectUtility.SetStaticEditorFlags(child, flags);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer copy = child.AddComponent<MeshRenderer>();
            copy.sharedMaterials = original.sharedMaterials;
            copy.forceMeshLod = (short)level;
            copy.receiveShadows = original.receiveShadows;
            copy.shadowCastingMode = size < ShadowlessBelowMetres ? ShadowCastingMode.Off : original.shadowCastingMode;
            copy.lightProbeUsage = original.lightProbeUsage;
            copy.reflectionProbeUsage = original.reflectionProbeUsage;
            return copy;
        }

        private static float HeightAt(float size, float distance) =>
            Mathf.Min(LargestTransition,
                      SettlementLodBaker.ScreenHeightAt(size, distance, ReferenceFovDegrees, QualitySettings.lodBias));

        /// <summary>
        /// LODGroup heights must fall strictly from tier to tier. A piece much larger than its reach clamps several
        /// tiers to the screen's full height; those stay at full detail further out rather than swap order.
        /// </summary>
        private static LOD[] Descending(List<LOD> tiers)
        {
            LOD[] levels = tiers.ToArray();
            for (int i = 1; i < levels.Length; i++)
                levels[i].screenRelativeTransitionHeight =
                    Mathf.Min(levels[i].screenRelativeTransitionHeight, levels[i - 1].screenRelativeTransitionHeight - TransitionStep);
            return levels;
        }
    }
}
