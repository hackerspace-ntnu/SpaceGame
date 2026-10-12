using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SpaceGame.World.Posters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceGame.EditorTools.Posters
{
    /// <summary>
    /// Bakes a <see cref="WantedPoster"/>: renders its agent (<see cref="PortraitCapture"/>),
    /// prints it (<see cref="PosterStylizer"/>), sets the sheet (<see cref="PosterLayout"/>), writes
    /// the PNG and hands it to the poster. Usually run for you by <see cref="WantedPosterAutoBake"/>.
    ///
    /// <para>
    /// A poster's picture is named by a key over everything that shapes it (<see cref="KeyOf"/>),
    /// so identical posters share one file and re-baking overwrites it in place for all of them.
    /// Owns the PNGs in <see cref="TextureFolder"/> and the poster's baked fields; nothing else.
    /// </para>
    /// </summary>
    public static class WantedPosterBaker
    {
        public const string StylePath = "Assets/Game/ScriptableObjects/Posters/WantedPosterStyle.asset";
        public const string TextureFolder = "Assets/Game/Art/Textures/Posters";
        public const string PassesFolder = "Temp/PosterPasses";

        private const string TitleFontPath = "Assets/Game/Art/Fonts/Posters/BowlbyOneSC-Regular.ttf";
        private const string LabelFontPath = "Assets/Game/Art/Fonts/Posters/BlackOpsOne-Regular.ttf";
        private const string ValueFontPath = "Assets/Game/Art/Fonts/Posters/ChakraPetch-SemiBold.ttf";
        private const int MaxTextureSize = 2048;
        private const int KeyLength = 10;

        [MenuItem("Tools/SpaceGame/Posters/Bake All Wanted Posters")]
        private static void BakeAllMenu() => Report(BakeAll());

        [MenuItem("Tools/SpaceGame/Posters/Bake Selected Wanted Posters")]
        private static void BakeSelectedMenu() => Report(Selected().Select(Bake).ToList());

        [MenuItem("Tools/SpaceGame/Posters/Bake Selected Wanted Posters", true)]
        [MenuItem("Tools/SpaceGame/Posters/Export Portrait Passes (Selected)", true)]
        private static bool SelectionValidate() => Selected().Any();

        /// <summary>Read-only: writes the renders behind each selected poster's portrait to <see cref="PassesFolder"/>.</summary>
        [MenuItem("Tools/SpaceGame/Posters/Export Portrait Passes (Selected)")]
        private static void ExportPassesMenu()
        {
            foreach (WantedPoster poster in Selected()) Debug.Log("[Posters] " + ExportPasses(poster));
        }

        [MenuItem("Tools/SpaceGame/Posters/Delete Unused Poster Pictures")]
        private static void DeleteUnusedMenu() => Debug.Log("[Posters] " + DeleteUnused());

        private static IEnumerable<WantedPoster> Selected() =>
            Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<WantedPoster>(true)).Distinct();

        /// <summary>
        /// The poster's identity for baking: its agent and every field printed or framed. Style
        /// changes are not in it — re-bake everything after editing the style.
        /// </summary>
        public static string KeyOf(WantedPoster poster)
        {
            GameObject agent = poster.Agent;
            string who = agent == null ? "none"
                : EditorUtility.IsPersistent(agent) ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(agent))
                : GlobalObjectId.GetGlobalObjectIdSlow(agent).ToString();
            string what = string.Join("|", who, poster.DisplayName, poster.Reward, poster.Status, ColorUtility.ToHtmlStringRGB(poster.Accent),
                poster.HeightLabel, poster.PortraitSpan.ToString("0.###", CultureInfo.InvariantCulture),
                poster.Headroom.ToString("0.###", CultureInfo.InvariantCulture));
            return Hash128.Compute(what).ToString().Substring(0, KeyLength);
        }

        /// <summary>True when the poster shows the picture its current fields call for.</summary>
        public static bool IsCurrent(WantedPoster poster) => poster.Poster != null && poster.BakedFrom == KeyOf(poster);

        /// <summary>
        /// Bakes every poster on a prefab in the project and in the open scenes. Posters inside
        /// a nested prefab are left to their own prefab, whose bake they inherit.
        /// </summary>
        public static List<string> BakeAll()
        {
            var lines = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                foreach (WantedPoster poster in prefab.GetComponentsInChildren<WantedPoster>(true))
                    if (poster.Agent != null && !IsNested(poster, prefab)) lines.Add(Bake(poster));
            }

            foreach (WantedPoster poster in Object.FindObjectsByType<WantedPoster>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (poster.Agent != null && !IsCurrent(poster)) lines.Add(Bake(poster));

            return lines;
        }

        // A poster that came in with a prefab nested inside this one. A variant's own root is an
        // instance of its base too, so "is part of an instance" alone would skip every variant.
        private static bool IsNested(WantedPoster poster, GameObject prefabRoot) =>
            PrefabUtility.IsPartOfPrefabInstance(poster) && PrefabUtility.GetNearestPrefabInstanceRoot(poster) != prefabRoot;

        /// <summary>Bakes one poster, hands it the picture and re-reads the result. Returns a one-line report.</summary>
        public static string Bake(WantedPoster poster)
        {
            WantedPosterStyle style = LoadStyle();
            PortraitPasses passes = PortraitCapture.Capture(poster, style);
            PosterImage portrait = PosterStylizer.Stylize(passes, poster.Accent, style);
            string heightText = string.IsNullOrWhiteSpace(poster.HeightLabel)
                ? passes.SubjectHeight.ToString("0.00", CultureInfo.InvariantCulture)
                : poster.HeightLabel;
            PosterImage sheet = PosterLayout.Compose(poster, style, portrait, heightText, LoadFonts());

            string key = KeyOf(poster);
            Texture2D texture = WriteTexture(TexturePath(poster, key), sheet);
            Hand(poster, texture, key);

            List<string> problems = Verify(poster, style, key);
            string line = problems.Count == 0
                ? $"{poster.name}: baked {poster.DisplayName} ({poster.Agent.name}) -> {AssetDatabase.GetAssetPath(texture)}"
                : $"{poster.name}: FAILED VERIFY — {string.Join("; ", problems)}";
            if (problems.Count > 0) Debug.LogError("[Posters] " + line, poster);
            return line;
        }

        /// <summary>
        /// Read-only: writes <c>shade</c> (the lit render), <c>roles</c> (each material in the ink
        /// it was given: slate skin, accent, grey neutral, magenta eyes) and <c>print</c> (the
        /// finished portrait). Touches no asset. Returns where they went.
        /// </summary>
        public static string ExportPasses(WantedPoster poster)
        {
            WantedPosterStyle style = LoadStyle();
            PortraitPasses passes = PortraitCapture.Capture(poster, style);
            var roles = new PosterImage(passes.Width, passes.Height, Color.clear);
            for (int i = 0; i < roles.Pixels.Length; i++)
            {
                int m = passes.Material[i];
                if (m < 0) continue;
                roles.Pixels[i] = passes.Roles[m] switch
                {
                    InkRole.Skin => style.slate,
                    InkRole.Accent => poster.Accent,
                    InkRole.Eye => Color.magenta,
                    _ => Color.gray,
                };
            }

            string folder = $"{PassesFolder}/{Safe(poster.DisplayName)}";
            Directory.CreateDirectory(folder);
            WritePng(passes.Shade, $"{folder}/shade.png");
            WritePng(roles, $"{folder}/roles.png");
            WritePng(PosterStylizer.Stylize(passes, poster.Accent, style), $"{folder}/print.png");
            return $"{poster.name}: passes written to {Path.GetFullPath(folder)}";
        }

        /// <summary>
        /// Deletes every poster picture no prefab or scene in the project still uses. A changed
        /// poster bakes under a new name and leaves its old picture behind.
        /// </summary>
        public static string DeleteUnused()
        {
            string[] users = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
            var used = new HashSet<string>(AssetDatabase.GetDependencies(users, true));
            var deleted = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (used.Contains(path)) continue;
                if (!AssetDatabase.DeleteAsset(path)) throw new InvalidOperationException($"[Posters] Could not delete {path}.");
                deleted.Add(path);
            }

            return deleted.Count == 0 ? "no unused poster pictures" : $"deleted {deleted.Count}:\n" + string.Join("\n", deleted);
        }

        private static WantedPosterStyle LoadStyle()
        {
            var style = AssetDatabase.LoadAssetAtPath<WantedPosterStyle>(StylePath);
            if (style == null) throw new InvalidOperationException($"[Posters] Missing style asset {StylePath}.");
            return style;
        }

        private static PosterFonts LoadFonts() => new()
        {
            Title = LoadFont(TitleFontPath),
            Label = LoadFont(LabelFontPath),
            Value = LoadFont(ValueFontPath),
        };

        private static Font LoadFont(string path)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null) throw new InvalidOperationException($"[Posters] Missing font {path}.");
            return font;
        }

        private static string TexturePath(WantedPoster poster, string key) => $"{TextureFolder}/Wanted_{Safe(poster.DisplayName)}_{key}.png";

        private static string Safe(string name)
        {
            string letters = new(name.Where(char.IsLetterOrDigit).ToArray());
            return letters.Length > 0 ? letters : "Unknown";
        }

        private static Texture2D WriteTexture(string path, PosterImage sheet)
        {
            Directory.CreateDirectory(TextureFolder);
            WritePng(sheet, path);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.maxTextureSize = MaxTextureSize;
            // The sheet is 4:5; the default import would squash it to the nearest power of two.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Gives the poster its picture and saves that wherever the poster lives: a prefab, a prefab instance or a scene.</summary>
        private static void Hand(WantedPoster poster, Texture2D texture, string key)
        {
            if (EditorUtility.IsPersistent(poster))
            {
                poster.SetBaked(texture, key);
                PrefabUtility.SavePrefabAsset(poster.transform.root.gameObject);
                return;
            }

            Undo.RecordObject(poster, "Bake wanted poster");
            poster.SetBaked(texture, key);
            if (PrefabUtility.IsPartOfPrefabInstance(poster)) PrefabUtility.RecordPrefabInstancePropertyModifications(poster);
            EditorUtility.SetDirty(poster);
            if (poster.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(poster.gameObject.scene);
        }

        private static void WritePng(PosterImage image, string path)
        {
            Texture2D texture = image.ToTexture();
            try
            {
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Re-reads what the bake wrote, off disk where it can.</summary>
        private static List<string> Verify(WantedPoster poster, WantedPosterStyle style, string key)
        {
            var problems = new List<string>();
            string path = TexturePath(poster, key);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) problems.Add($"no picture at {path}");
            else if (texture.width != style.sheetPixels.x || texture.height != style.sheetPixels.y)
                problems.Add($"picture is {texture.width}x{texture.height}, expected {style.sheetPixels}");

            if (poster.Poster != texture || poster.BakedFrom != key) problems.Add("the poster was not handed its picture");
            if (poster.Sheet == null) problems.Add("no sheet to show it on");
            if (poster.GetComponentInChildren<Collider>(true) == null) problems.Add("no collider, so it cannot be read");

            if (EditorUtility.IsPersistent(poster))
            {
                string prefabPath = AssetDatabase.GetAssetPath(poster);
                GameObject reloaded = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    bool saved = reloaded.GetComponentsInChildren<WantedPoster>(true).Any(p => p.Poster == texture && p.BakedFrom == key);
                    if (!saved) problems.Add($"{prefabPath} did not keep the picture on disk");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(reloaded);
                }
            }

            return problems;
        }

        private static void Report(List<string> lines)
        {
            Debug.Log($"[Posters] Baked {lines.Count} wanted poster(s):\n" + string.Join("\n", lines));
        }
    }
}
