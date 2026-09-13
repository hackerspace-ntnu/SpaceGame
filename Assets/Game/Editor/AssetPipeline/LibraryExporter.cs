using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Exports the picture half of the plain-language library site: one preview render per
    /// artifact, creature, vehicle and piece of gear, plus a <c>library.json</c> manifest naming
    /// them.
    ///
    /// <para>
    /// The prose half lives in <c>docs/library/blurbs.md</c> and is written by hand. This tool
    /// never reads or writes that file, so re-exporting after adding a creature cannot destroy
    /// the descriptions — <c>tools/build_library_site.py</c> is what joins the two.
    /// </para>
    ///
    /// <para>
    /// Output goes to <c>docs/library/</c>, outside <c>Assets/</c>, deliberately. These PNGs are
    /// website output, not game content: imported into the project they would cost import time,
    /// carry .meta files and show up in asset searches beside the real item sprites, which is
    /// exactly the confusion <see cref="BatchIconGenerator"/> already had to dig the project out
    /// of once.
    /// </para>
    /// </summary>
    public static class LibraryExporter
    {
        private const int Resolution = 512;

        /// <summary>
        /// Magenta, and the site keys it out to transparency so a card of any colour can sit
        /// behind the model in either theme.
        ///
        /// <para>
        /// Asking for a transparent background here does not work: the preview rig flattens what
        /// it returns to opaque RGB, so alpha 0 arrives as solid black — which is invisible on a
        /// dark card and a black box on a light one. A key colour is the way out, and it has to
        /// be one no model contains, because anything the models actually use would be punched
        /// out of the model itself. Nothing in this project is magenta.
        /// </para>
        /// </summary>
        private static readonly Color Background = new Color(1f, 0f, 1f, 1f);

        /// <summary>
        /// Prefab roots to sweep, and the category each contributes to. Item assets are found
        /// through <see cref="InventoryItem"/> instead, so their prefabs are not listed here.
        /// </summary>
        private static readonly (string Path, string Category)[] PrefabRoots =
        {
            ("Assets/Game/Prefabs/agents/creatures",         "agents"),
            ("Assets/Game/Prefabs/agents/Robots",            "agents"),
            ("Assets/Game/Prefabs/agents/Characters",        "agents"),
            ("Assets/Game/Prefabs/agents/Vehicles",          "vehicles"),
            ("Assets/Game/Prefabs/Vehicles",                 "vehicles"),
            ("Assets/Game/Prefabs/Items/Equipment",          "gear"),
        };

        /// <summary>Which item folder maps to which category.</summary>
        private static readonly Dictionary<string, string> ItemFolderCategory =
            new Dictionary<string, string>
            {
                { "Artifacts", "artifacts" },
                { "Supplies",  "gear" },
                { "ShipParts", "gear" },
            };

        /// <summary>
        /// Prefabs that exist for a technical reason and would read as duplicates on a page whose
        /// whole job is to show one picture per thing. Each is a second copy of an entry that is
        /// already in the set, not a thing a player would recognise separately.
        /// </summary>
        private static readonly HashSet<string> SkipPrefabs = new HashSet<string>
        {
            "RoverNoHierarchy",   // hierarchy-flattened build of Rover
            "PatrolRobot 1",      // three placement variants of one robot
            "PatrolRobot 2",
            "PatrolRobot 3",
        };

        /// <summary>
        /// Folders swept up by a parent root that hold parts rather than things. A placement
        /// ghost is the translucent preview of an item you are about to put down, and a holder is
        /// the strap or clip that pins gear to the rig — both are components of an entry that is
        /// already listed, and neither is something a player would name.
        /// </summary>
        private static readonly string[] SkipFolders = { "/Ghosts/", "/Holders/" };

        [MenuItem("Tools/SpaceGame/Export Library Site Data")]
        public static void Export()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string outDir = Path.Combine(root, "docs/library");
            string imageDir = Path.Combine(outDir, "images");
            Directory.CreateDirectory(imageDir);

            var entries = new List<Entry>();
            entries.AddRange(CollectItems());
            entries.AddRange(CollectPrefabs(entries));

            var log = new StringBuilder();
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    Entry e = entries[i];
                    EditorUtility.DisplayProgressBar("Exporting library", e.Name,
                        (float)i / entries.Count);

                    Texture2D tex = PrefabPreviewRenderer.Render(
                        e.Prefab, PrefabPreviewRenderer.DefaultAngle,
                        Resolution, Background, out string note);

                    if (tex == null)
                    {
                        log.Append("\nSKIP  " + e.Id + " — " + note);
                        e.Image = null;
                        continue;
                    }

                    File.WriteAllBytes(Path.Combine(imageDir, e.Id + ".png"), tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    e.Image = "images/" + e.Id + ".png";
                    log.Append("\nOK    " + e.Id.PadRight(24) + e.Category.PadRight(12)
                        + (note.Length > 0 ? "(" + note + ")" : ""));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            File.WriteAllText(Path.Combine(outDir, "library.json"), ToJson(entries));
            Debug.Log("Library exported to " + outDir + " (" + entries.Count(e => e.Image != null)
                + "/" + entries.Count + " rendered):" + log);
        }

        /// <summary>Every <see cref="InventoryItem"/> in a category folder, rendered from the
        /// same prefab its inventory icon uses.</summary>
        private static IEnumerable<Entry> CollectItems()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:InventoryItem", new[] { "Assets/Game/Resources/Items" });

            foreach (string guid in guids.OrderBy(g => g))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(path);
                if (item == null || item.itemPrefab == null) continue;

                string folder = Path.GetFileName(Path.GetDirectoryName(path));
                if (!ItemFolderCategory.TryGetValue(folder, out string category)) continue;

                yield return new Entry
                {
                    Id = item.name,
                    Name = string.IsNullOrEmpty(item.itemName) ? Humanise(item.name) : item.itemName,
                    Category = category,
                    Kind = category == "artifacts" ? item.equipKind.ToString() : folder,
                    Source = path,
                    Prefab = item.iconPrefab != null ? item.iconPrefab : item.itemPrefab,
                    Identity = item.itemPrefab,
                };
            }
        }

        /// <summary>
        /// Prefabs from <see cref="PrefabRoots"/>, skipping any whose prefab an item entry
        /// already covers — the Jetpack is both an <see cref="InventoryItem"/> and a prefab under
        /// Equipment, and it should appear once.
        ///
        /// <para>
        /// What counts as "already covered" is the item's <see cref="InventoryItem.itemPrefab"/>,
        /// never the prefab it was merely photographed from. The Wing Pack is shot from the
        /// unfurled ornithopter, and keying this on the render prefab silently deleted the
        /// ornithopter from the vehicles list — a folded pack on your back and an aircraft are
        /// two things a player would name separately, whatever they share in the project.
        /// </para>
        /// </summary>
        private static IEnumerable<Entry> CollectPrefabs(List<Entry> already)
        {
            var taken = new HashSet<GameObject>(
                already.Select(e => e.Identity).Where(p => p != null));

            foreach ((string rootPath, string category) in PrefabRoots)
            {
                if (!AssetDatabase.IsValidFolder(rootPath)) continue;

                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { rootPath });
                foreach (string guid in guids.OrderBy(g => g))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (SkipPrefabs.Contains(name)) continue;
                    if (SkipFolders.Any(f => path.Contains(f))) continue;

                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || !taken.Add(prefab)) continue;

                    yield return new Entry
                    {
                        Id = name.Replace(' ', '_'),
                        Name = Humanise(name),
                        Category = category,
                        Kind = Path.GetFileName(rootPath),
                        Source = path,
                        Prefab = prefab,
                        Identity = prefab,
                    };
                }
            }
        }

        /// <summary>"DuneOrnithopter" to "Dune Ornithopter". Leaves names that are already
        /// spaced or lowercase alone.</summary>
        private static string Humanise(string raw)
        {
            var sb = new StringBuilder(raw.Length + 6);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                bool boundary = i > 0 && char.IsUpper(c)
                    && (!char.IsUpper(raw[i - 1]) || (i + 1 < raw.Length && char.IsLower(raw[i + 1])));
                if (boundary && sb[sb.Length - 1] != ' ') sb.Append(' ');
                sb.Append(c);
            }
            string s = sb.ToString().Trim();
            return char.ToUpper(s[0]) + s.Substring(1);
        }

        private static string ToJson(List<Entry> entries)
        {
            var sb = new StringBuilder("[\n");
            var ordered = entries.OrderBy(e => e.Category).ThenBy(e => e.Name).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                Entry e = ordered[i];
                sb.Append("  {")
                  .Append("\"id\": ").Append(Quote(e.Id)).Append(", ")
                  .Append("\"name\": ").Append(Quote(e.Name)).Append(", ")
                  .Append("\"category\": ").Append(Quote(e.Category)).Append(", ")
                  .Append("\"kind\": ").Append(Quote(e.Kind)).Append(", ")
                  .Append("\"source\": ").Append(Quote(e.Source)).Append(", ")
                  .Append("\"image\": ").Append(e.Image == null ? "null" : Quote(e.Image))
                  .Append("}").Append(i < ordered.Count - 1 ? ",\n" : "\n");
            }
            return sb.Append("]\n").ToString();
        }

        private static string Quote(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private class Entry
        {
            public string Id;
            public string Name;
            public string Category;
            public string Kind;
            public string Source;
            public string Image;

            /// <summary>What gets photographed.</summary>
            public GameObject Prefab;

            /// <summary>What the entry *is*, for deciding whether two entries are the same
            /// thing. Usually identical to <see cref="Prefab"/>, and deliberately not so for an
            /// item photographed from something other than itself.</summary>
            public GameObject Identity;
        }
    }
}
