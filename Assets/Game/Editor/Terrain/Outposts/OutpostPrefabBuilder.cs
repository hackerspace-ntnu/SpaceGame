using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>One piece of an outpost as built: its row, its name in the prefab, and its instance (none for a prop, which is stood by <see cref="OutpostProps"/>).</summary>
    public sealed class PlacedPiece
    {
        public OutpostPiece row;
        public string name;
        public Transform instance;
    }

    /// <summary>
    /// Builds the outpost prefabs from <c>OutpostLayouts.json</c> (written by <c>raxy_outposts_export.py</c> from the outpost .blend files):
    /// every piece is an instance of its own decoration prefab, so an outpost brings the seats, work posts, colliders and fixtures those
    /// carry; then what the pieces' meshes hold but their prefabs lack is added -- ladders, collision a tower can be walked on, sit spots
    /// for the loose seats, props and the rests they are moved between. <b>Build Missing</b> leaves an existing prefab alone (it may have
    /// been hand-edited); <b>Rebuild All</b> rewrites every outpost from the layout, in place so the GUIDs and the scenes using them survive.
    /// </summary>
    public static class OutpostPrefabBuilder
    {
        public const string LayoutPath = "Assets/Game/Art/Models/Environment/Structures/Outpost/OutpostLayouts.json";
        public const string OutputDir = "Assets/Game/Prefabs/Environment/Structures/Outpost";
        private const string DecorationDir = "Assets/Game/Prefabs/Environment/Decorations";

        private const string PiecesName = "Pieces";
        private const string LaddersName = "Ladders";
        private const string CollisionName = "Collision";
        private const string SpotsName = "Spots";
        private const string PropsName = "Props";

        // Metres past the footprint's far edge: clear of the guy ropes and awnings, inside the 3 m the settlement snaps a named heart by.
        private const float HeartMargin = 2f;

        [MenuItem("Tools/SpaceGame/Outposts/Build Missing Outpost Prefabs")]
        private static void BuildMissing() => Run(overwrite: false);

        [MenuItem("Tools/SpaceGame/Outposts/Rebuild All Outpost Prefabs")]
        private static void RebuildAll()
        {
            if (EditorUtility.DisplayDialog("Rebuild all outposts", "Rewrites every outpost prefab from the layout. Hand edits to them are lost.", "Rebuild", "Cancel"))
                Run(overwrite: true);
        }

        public static List<string> Run(bool overwrite)
        {
            var notes = new List<string>();
            OutpostLayoutFile file = OutpostLayoutFile.Parse(File.ReadAllText(LayoutPath));
            Dictionary<string, GameObject> decorations = AssetDatabase.FindAssets("t:Prefab Deco_", new[] { DecorationDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToDictionary(Path.GetFileNameWithoutExtension, path => AssetDatabase.LoadAssetAtPath<GameObject>(path));

            Directory.CreateDirectory(OutputDir);
            foreach (OutpostLayout layout in file.outposts)
                notes.Add(BuildOne(layout, decorations, overwrite));

            Debug.Log("[Outposts]\n" + string.Join("\n", notes));
            return notes;
        }

        private static string BuildOne(OutpostLayout layout, Dictionary<string, GameObject> decorations, bool overwrite)
        {
            string path = $"{OutputDir}/{layout.name}.prefab";
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path) return $"{layout.name}: skipped, open in Prefab Mode";
            if (!overwrite && File.Exists(path)) return $"{layout.name}: kept (already built)";

            if (!File.Exists(path)) CreateEmptyPrefab(layout.name, path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                var missing = new List<string>();
                List<PlacedPiece> placed = PlacePieces(layout, decorations, root.transform, missing);
                string dressing = Dress(root.transform, placed);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{layout.name}: {placed.Count}/{layout.pieces.Length} pieces, {dressing}" +
                       (missing.Count > 0 ? $"; no prefab for {string.Join(", ", missing.Distinct())}" : "") + Verify(path, placed.Count);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void CreateEmptyPrefab(string name, string path)
        {
            var empty = new GameObject(name);
            PrefabUtility.SaveAsPrefabAsset(empty, path);
            Object.DestroyImmediate(empty);
        }

        private static List<PlacedPiece> PlacePieces(OutpostLayout layout, Dictionary<string, GameObject> decorations, Transform root, List<string> missing)
        {
            Transform pieces = Container(root, PiecesName);
            var counts = new Dictionary<string, int>();
            var placed = new List<PlacedPiece>();
            foreach (OutpostPiece row in layout.pieces)
            {
                counts[row.kind] = counts.TryGetValue(row.kind, out int n) ? n + 1 : 1;
                string name = $"{row.kind}_{counts[row.kind]:00}";

                if (OutpostProps.IsProp(row.kind))
                {
                    placed.Add(new PlacedPiece { row = row, name = name });
                    continue;
                }

                GameObject prefab = PrefabFor(row.kind, decorations, out float lift);
                if (prefab == null)
                {
                    missing.Add(row.kind);
                    continue;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pieces);
                instance.name = name;
                instance.transform.localPosition = row.Position + Vector3.down * lift;
                instance.transform.localRotation = row.Rotation;
                instance.transform.localScale = row.Scale;
                if (!OutpostItemModels.IsItem(row.kind)) HideRemovedParts(instance.transform, row);
                placed.Add(new PlacedPiece { row = row, name = name, instance = instance.transform });
            }
            return placed;
        }

        // The prefab is the whole decoration; the author's copy of it in the .blend may have lost parts (a bell frame without its roof,
        // bell and rope). A mesh the .blend no longer has under this piece is switched off here, on the instance, not in the prefab.
        private static void HideRemovedParts(Transform instance, OutpostPiece row)
        {
            if (row.parts.Length == 0) return;

            // A prefab whose model is one mesh names it after the decoration (deco_tableware_set), not after the part: nothing to match, nothing to hide.
            var kept = new HashSet<string>(row.parts);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (!renderers.Any(r => kept.Contains(r.name))) return;

            foreach (Renderer renderer in renderers)
                if (!kept.Contains(renderer.name)) renderer.gameObject.SetActive(false);
        }

        // A decoration is its own prefab; a carried item or tool stood about as scenery is a model of the item, whose origin is
        // where a hand holds it, so it is lowered by how far the model was raised to stand on the floor.
        private static GameObject PrefabFor(string kind, Dictionary<string, GameObject> decorations, out float lift)
        {
            lift = 0f;
            if (!OutpostItemModels.IsItem(kind)) return decorations.TryGetValue($"Deco_{kind}", out GameObject deco) ? deco : null;

            InventoryItem item = OutpostItemModels.LoadItem(kind);
            return item != null && item.itemPrefab != null ? OutpostItemModels.EnsureModel(item, carried: false, out lift) : null;
        }

        private static string Dress(Transform root, List<PlacedPiece> placed)
        {
            Transform ladders = Container(root, LaddersName);
            Transform collision = Container(root, CollisionName);
            Transform spots = Container(root, SpotsName);
            Transform props = Container(root, PropsName);

            int ladderCount = 0;
            foreach (PlacedPiece piece in placed.Where(p => p.instance != null))
            {
                OutpostCollision.Apply(collision, piece.instance, piece.row.kind);
                if (OutpostLadders.TryAdd(ladders, piece.instance, piece.row.kind)) ladderCount++;
            }

            AddHeart(root, placed);
            int seats = OutpostSeating.Add(spots, placed);
            int posts = OutpostPosts.Add(spots, placed);
            (int propCount, int restCount) = OutpostProps.Add(props, placed, Vector3.zero);
            return $"{ladderCount} ladders, {seats} sit spots, {posts} work posts, {propCount} props on {restCount} rests";
        }

        // The settlement guesses its walkable heart from the biggest group of places that walk to one another. An outpost's decks hold
        // as many places as its ground, so the guess can land on a deck and leave every place on the ground "an island". The heart is the
        // open ground just outside the outpost, which the camp's ground joins.
        private static void AddHeart(Transform root, List<PlacedPiece> placed)
        {
            Renderer[] renderers = placed.Where(p => p.instance != null).SelectMany(p => p.instance.GetComponentsInChildren<Renderer>()).ToArray();
            Bounds footprint = renderers[0].bounds;
            foreach (Renderer renderer in renderers) footprint.Encapsulate(renderer.bounds);

            var heart = new GameObject("Heart");
            heart.transform.SetParent(root, false);
            heart.transform.position = new Vector3(footprint.center.x, 0f, footprint.max.z + HeartMargin);
            heart.AddComponent<SettlementHeart>();
        }

        private static Transform Container(Transform root, string name)
        {
            var container = new GameObject(name).transform;
            container.SetParent(root, false);
            return container;
        }

        // The write is read back off disk: a prefab save that was silently discarded must not report success.
        private static string Verify(string path, int pieces)
        {
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (saved == null) return " -- NOT SAVED: the prefab cannot be read back";

            int found = saved.transform.Find(PiecesName)?.childCount ?? 0;
            int ladders = saved.GetComponentsInChildren<Ladder>(true).Length;
            int spotCount = saved.GetComponentsInChildren<SettlementSpot>(true).Length;
            return found == pieces - saved.GetComponentsInChildren<SettlementProp>(true).Length
                ? $" [saved: {ladders} ladder, {spotCount} spots in all]"
                : $" -- READ BACK {found} pieces, expected {pieces}";
        }
    }
}
