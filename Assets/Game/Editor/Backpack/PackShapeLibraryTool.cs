using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Creates <c>PackShapes.asset</c>, fills it with every item's derived default, and wires it
    /// onto every <see cref="BackpackObject"/> prefab in the project.
    ///
    /// <para>
    /// A separate tool rather than another step inside <c>ExpeditionRigWiring</c>, deliberately:
    /// that one rebuilds the rig prefab and its five holder prefabs from FBX, which is a heavy and
    /// destructive thing to run for the sake of one object reference. This is idempotent and
    /// touches nothing else — run it once to get the asset, and again after adding items to top it
    /// up without disturbing shapes anybody has drawn.
    /// </para>
    /// <para>
    /// Nothing here is required for the grid to work. An unwired pack falls back to the shape
    /// <see cref="PackShape.ForFootprint"/> derives from each item's true size, which is what every
    /// item gets anyway until somebody draws it something better.
    /// </para>
    /// </summary>
    public static class PackShapeLibraryTool
    {
        private const string Folder = "Assets/Game/ScriptableObjects/Items";

        /// <summary>Beside <c>PackHolderLibrary.asset</c>, which answers the same kind of question.</summary>
        private const string AssetPath = Folder + "/PackShapes.asset";

        [MenuItem("Tools/SpaceGame/Items/Create Pack Shape Library")]
        public static void Run()
        {
            var log = new StringBuilder("Pack shape library\n");

            PackShapeLibrary library = LoadOrCreate(log);

            int added = Populate(library, log);
            int wired = Wire(library, log);

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();

            library.Invalidate();

            log.Append($"  {added} item(s) added, {wired} pack prefab(s) wired.\n")
               .Append($"  Cell size is {PackGrid.Cell:F3} m. Select the asset to draw shapes.\n");

            Debug.Log(log.ToString(), library);

            Selection.activeObject = library;
            EditorGUIUtility.PingObject(library);
        }

        /// <summary>
        /// Bring every UNDRAWN row back into agreement with the item it describes.
        ///
        /// <para>
        /// A row is undrawn when its mask is a solid rectangle: that is exactly what
        /// <see cref="Populate"/> seeds, so it carries no authorship and re-deriving it loses
        /// nothing. A row with any hole in it is somebody's drawing and is never touched — the same
        /// rule <see cref="Populate"/> follows, applied to rows that already exist.
        /// </para>
        /// <para>
        /// It is needed because a seeded row is a snapshot of a size that can move afterwards.
        /// <c>ItemScaleLadder</c> resized twelve items on 2026-08-25 and every one of their rows
        /// still described the old size — which does not shrink the item, because the pack draws
        /// gear at true size regardless. It makes the item overhang the cells the layout reserved
        /// for it and lie through whatever is in the next cell along, which is what
        /// <c>PackShapes.WarnIfOversized</c> spends its time complaining about.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Items/Reseed Undrawn Pack Shapes")]
        public static void Reseed()
        {
            var log = new StringBuilder("Pack shape reseed\n");

            var library = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(AssetPath);

            if (library == null)
            {
                Debug.LogError($"No shape library at {AssetPath}. Run 'Create Pack Shape Library' first.");
                return;
            }

            ItemFootprint.ClearCache();

            int changed = 0;
            int drawn = 0;

            for (int i = 0; i < library.Entries.Count; i++)
            {
                PackShapeLibrary.Entry entry = library.Entries[i];

                if (entry?.item == null) continue;

                if (!IsSolidRectangle(entry))
                {
                    drawn++;
                    log.Append($"  kept   {entry.item.itemName}: {entry.width} x {entry.height} is drawn\n");
                    continue;
                }

                PackShape derived = PackShape.ForFootprint(ItemFootprint.FootprintOf(entry.item));

                if (derived.Width == entry.width && derived.Height == entry.height) continue;

                log.Append($"  reseed {entry.item.itemName}: {entry.width} x {entry.height} -> ")
                   .Append($"{derived.Width} x {derived.Height}\n");

                entry.width = derived.Width;
                entry.height = derived.Height;
                entry.cells = new bool[derived.Width * derived.Height];

                for (int c = 0; c < entry.cells.Length; c++) entry.cells[c] = true;

                changed++;
            }

            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            library.Invalidate();

            // The warning is once-per-item-per-session, so without this a row that has just been
            // corrected still reads as already-complained-about and the fix cannot be confirmed.
            PackShapes.ClearWarnings();

            log.Append($"  {changed} row(s) reseeded, {drawn} drawn row(s) left alone.\n");
            Debug.Log(log.ToString(), library);
        }

        /// <summary>
        /// Is this row still the plain block <see cref="Populate"/> seeds — every cell filled?
        /// A single hole means somebody drew it, and drawn shapes are not this tool's to change.
        /// </summary>
        private static bool IsSolidRectangle(PackShapeLibrary.Entry entry)
        {
            if (entry.cells == null || entry.cells.Length != entry.width * entry.height) return false;

            for (int i = 0; i < entry.cells.Length; i++)
                if (!entry.cells[i]) return false;

            return true;
        }

        private static PackShapeLibrary LoadOrCreate(StringBuilder log)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PackShapeLibrary>(AssetPath);

            if (existing != null)
            {
                log.Append($"  reusing {AssetPath}\n");
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Game/ScriptableObjects", "Items");

            PackShapeLibrary library = ScriptableObject.CreateInstance<PackShapeLibrary>();

            AssetDatabase.CreateAsset(library, AssetPath);

            log.Append($"  created {AssetPath}\n");

            return library;
        }

        /// <summary>
        /// One row per item asset, seeded with the block its true size derives. Rows that already
        /// exist are left exactly as they are — this tool must never overwrite a drawn shape.
        /// </summary>
        private static int Populate(PackShapeLibrary library, StringBuilder log)
        {
            var have = new HashSet<InventoryItem>();

            for (int i = 0; i < library.Entries.Count; i++)
                if (library.Entries[i]?.item != null) have.Add(library.Entries[i].item);

            string[] guids = AssetDatabase.FindAssets("t:InventoryItem");
            int added = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);

                if (item == null || have.Contains(item)) continue;

                PackShape derived = PackShape.ForFootprint(ItemFootprint.FootprintOf(item));

                var cells = new bool[derived.Width * derived.Height];
                for (int c = 0; c < cells.Length; c++) cells[c] = true;

                library.Entries.Add(new PackShapeLibrary.Entry
                {
                    item = item,
                    width = derived.Width,
                    height = derived.Height,
                    cells = cells,
                    allowRotation = true,
                });

                log.Append($"  + {item.itemName}: {derived.Width} x {derived.Height}\n");
                added++;
            }

            return added;
        }

        /// <summary>
        /// Point every pack prefab at the library. Through <see cref="SerializedObject"/> because
        /// <c>shapes</c> is a private <c>[SerializeField]</c>, the same way
        /// <c>ExpeditionRigWiring</c> reaches <c>holders</c>.
        /// </summary>
        private static int Wire(PackShapeLibrary library, StringBuilder log)
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            int wired = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                if (prefab == null) continue;

                var pack = prefab.GetComponent<BackpackObject>();
                if (pack == null) continue;

                var so = new SerializedObject(pack);
                SerializedProperty field = so.FindProperty("shapes");

                if (field == null)
                {
                    log.Append("  FAILED   BackpackObject has no 'shapes' field any more.\n");
                    continue;
                }

                if (field.objectReferenceValue == library) continue;

                field.objectReferenceValue = library;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(prefab);
                PrefabUtility.SavePrefabAsset(prefab);

                log.Append($"  wired {path}\n");
                wired++;
            }

            return wired;
        }
    }
}
