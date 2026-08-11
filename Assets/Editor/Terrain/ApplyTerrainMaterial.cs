using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class ApplyTerrainMaterial
    {
        [MenuItem("SpaceGame/Terrain/Apply Selected Material to All Terrains in Scene")]
        public static void ApplyToAll()
        {
            var mat = Selection.activeObject as Material;
            if (mat == null)
            {
                EditorUtility.DisplayDialog(
                    "Apply Terrain Material",
                    "Select a Material in the Project window first, then run this menu item.",
                    "OK");
                return;
            }

            var terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            if (terrains.Length == 0)
            {
                EditorUtility.DisplayDialog("Apply Terrain Material", "No Terrain components found in the open scenes.", "OK");
                return;
            }

            Undo.RecordObjects(terrains, "Apply Terrain Material");
            foreach (var t in terrains)
            {
                t.materialTemplate = mat;
                EditorUtility.SetDirty(t);
            }

            Debug.Log($"[ApplyTerrainMaterial] Assigned '{mat.name}' to {terrains.Length} Terrain(s).");
        }
    }
}
