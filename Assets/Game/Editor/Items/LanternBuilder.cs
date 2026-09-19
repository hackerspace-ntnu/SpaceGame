// Builds the camp lantern: the first placeable, and the worked example of the pair.
//
// A placeable is TWO prefabs, and this is why they cannot be one:
//
//   Lantern.prefab        the thing in your hand and lying in the sand. Grip pose, pickup,
//                         physics, and PlaceableItem, which spawns the other one and spends
//                         itself doing it.
//   PlacedLantern.prefab  the thing standing on the ground. A Light, a collider you cannot walk
//                         through, and PlacedObject, which hands the item back on right-click.
//
// The recipe both halves share with every other ground placeable is PlaceablePairBuilder; what is
// here is only what makes it a lantern.
//
// Re-running is safe and is the intended workflow: both prefabs are rebuilt in place, so anything
// added by hand in the Inspector is discarded by the next run with nothing said.
//
// Re-run from: Tools > Items > Build Camp Lantern
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class LanternBuilder
    {
        private const string Fbx = "Assets/Game/Art/Models/Items/camp_lantern.fbx";
        private const string HeldPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/Lantern.prefab";
        private const string PlacedDir = "Assets/Game/Prefabs/Items/Placed";
        private const string PlacedPath = PlacedDir + "/PlacedLantern.prefab";
        private const string AssetPath = "Assets/Game/Resources/Items/Artifacts/Lantern.asset";
        private const string FlameEmpty = "LIGHT_Flame";

        // Warm and short-range: it lights a camp, not a football pitch, and a placeable light that
        // outshines the sun is how a survival game stops being dark.
        private static readonly Color FlameColour = new Color(1.00f, 0.77f, 0.42f);
        private const float FlameRange = 9f;
        private const float FlameIntensity = 3.2f;

        [MenuItem("Tools/Items/Build Camp Lantern")]
        public static void Build()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (source == null)
            {
                Debug.LogError($"No lantern model at {Fbx}. Export it first:\n" +
                               "  blender --background --python components/props/camp_lantern_export.py");
                return;
            }

            var asset = PlaceablePairBuilder.EnsureItemAsset(AssetPath, "Camp Lantern");
            GameObject placed = BuildPlaced(source, asset);
            GameObject held = PlaceablePairBuilder.BuildHeld(source, "Lantern", HeldPath, asset, placed, FlameEmpty);
            PlaceablePairBuilder.Link(asset, held);

            AssetDatabase.SaveAssets();
            Debug.Log($"Camp lantern built.\n  held:   {HeldPath}\n  placed: {PlacedPath}\n" +
                      $"  asset:  {AssetPath}\n" +
                      "Now run Tools > SpaceGame > Multiplayer > Sync Network Prefabs (BOTH prefabs " +
                      "spawn at runtime), then Tools > Generate All Item Icons.");
        }

        /// <summary>
        /// The lantern standing on the ground. A spawned NetworkObject, so it needs the network
        /// prefab list and a SaveableEntity or it is gone on the next load.
        /// </summary>
        private static GameObject BuildPlaced(GameObject source, SpaceGame.Items.InventoryItem asset)
        {
            StaticPropBuilder.EnsureFolder(PlacedDir);
            GameObject root = PlaceablePairBuilder.Instantiate(source, "PlacedLantern");
            try
            {
                // Solid, not a trigger: you should not be able to walk through a lantern, and a solid
                // collider is also what lets the crosshair land on it. The interactable is on the ROOT,
                // which is where GetComponentInParent finds it from whichever child was hit.
                var box = root.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.16f, 0f);
                box.size = new Vector3(0.26f, 0.32f, 0.26f);

                // Placed where the model says, not where a constant guesses.
                Transform flame = PlaceablePairBuilder.FindChild(root.transform, FlameEmpty);
                var lightGo = new GameObject("Flame");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.localPosition =
                    flame != null ? flame.localPosition : new Vector3(0f, 0.135f, 0f);

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = FlameColour;
                light.range = FlameRange;
                light.intensity = FlameIntensity;
                light.shadows = LightShadows.Soft;

                PlaceablePairBuilder.AddPlacedWiring(root, asset, "Camp lantern");
                return PrefabUtility.SaveAsPrefabAsset(root, PlacedPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
