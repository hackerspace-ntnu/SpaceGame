using System.Collections.Generic;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// Readies the selected terrains for rock painting: gives them a detail map if they have
    /// none, adds every rock biome of the open scene's <see cref="RockField"/> as a detail
    /// prototype (what the Paint Details palette lists), and sets Detail Distance to 0 so Unity
    /// does not draw its own copies of those prototypes. Then paint rocks with Paint Details.
    /// </summary>
    public static class RockPaintSetup
    {
        private const string MenuPath = "Tools/World/Rocks/Add Rock Paint To Selected Terrains";

        // Metres per detail cell when a terrain has no detail map yet: the paint is read
        // blended and noise-warped, so cells this size never show as squares.
        private const float DetailCellMetres = 8f;
        private const int MinDetailResolution = 128;
        private const int MaxDetailResolution = 2048;
        private const int DetailPatchResolution = 32;

        [MenuItem(MenuPath)]
        private static void AddRockPaint()
        {
            RockField field = Object.FindFirstObjectByType<RockField>();
            if (field == null)
            {
                Debug.LogError("[RockPaintSetup] No RockField in the open scenes. Add RockField.prefab first.");
                return;
            }
            var terrains = new List<Terrain>();
            foreach (GameObject selected in Selection.gameObjects)
                if (selected.TryGetComponent(out Terrain terrain))
                    terrains.Add(terrain);
            if (terrains.Count == 0)
            {
                Debug.LogError("[RockPaintSetup] Select one or more terrains first.");
                return;
            }
            foreach (Terrain terrain in terrains)
                Prepare(terrain, field.Biomes);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanAddRockPaint() => Selection.activeGameObject != null &&
                                                 Selection.activeGameObject.GetComponent<Terrain>() != null;

        private static void Prepare(Terrain terrain, IReadOnlyList<RockField.Biome> biomes)
        {
            TerrainData data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Add Rock Paint");
            Undo.RecordObject(terrain, "Add Rock Paint");

            if (data.detailResolution == 0)
            {
                int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(data.size.x / DetailCellMetres)),
                                             MinDetailResolution, MaxDetailResolution);
                data.SetDetailResolution(resolution, DetailPatchResolution);
                Debug.Log($"[RockPaintSetup] '{terrain.name}' had no detail map; it now has {resolution}² " +
                          $"({data.size.x / resolution:0.#} m cells).", terrain);
            }

            var prototypes = new List<DetailPrototype>(data.detailPrototypes);
            foreach (RockField.Biome biome in biomes)
            {
                if (biome.paintPrototype == null || prototypes.Exists(p => p.prototype == biome.paintPrototype))
                    continue;
                prototypes.Add(new DetailPrototype
                {
                    usePrototypeMesh = true,
                    prototype = biome.paintPrototype,
                    renderMode = DetailRenderMode.VertexLit,
                    useInstancing = true,
                });
                Debug.Log($"[RockPaintSetup] '{terrain.name}' can now paint '{biome.paintPrototype.name}'.", terrain);
            }
            data.detailPrototypes = prototypes.ToArray();

            if (terrain.detailObjectDistance > 0f)
            {
                Debug.Log($"[RockPaintSetup] '{terrain.name}' Detail Distance {terrain.detailObjectDistance} -> 0, " +
                          "so only RockField draws the rocks.", terrain);
                terrain.detailObjectDistance = 0f;
            }
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(terrain);
        }
    }
}
