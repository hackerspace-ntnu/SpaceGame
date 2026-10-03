using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Import rules for the humanoid libraries that arrive as plain FBX files, one take per file:
    /// the Mixamo downloads in <c>Art/Animations/Humanoid</c>, and the Mocap Central, Motion Cast,
    /// EEJANAI cooking and ExplosiveLLC crafter packs under <c>ThirdParty</c> (see
    /// <see cref="Libraries"/>). Humanoid, played in place, event-free.
    ///
    /// <para>
    /// A take with entries in its library's <c>cuts.json</c> becomes exactly those clips (the long
    /// performances — a bartender's 27 seconds, a minute of acted emotion — are cut into the beats a
    /// gameplay system can ask for); a take without one imports as a single clip named after its
    /// file. Clip names are never renamed afterwards: an FBX clip's fileID is derived from its
    /// name, and every <c>CharacterAction</c> holds that fileID.
    /// </para>
    /// <para>
    /// Mixamo names every take <c>mixamo.com</c>, so the file name is the only identity a clip has.
    /// </para>
    /// </summary>
    public class LibraryClipImporter : AssetPostprocessor
    {
        /// <summary>One library: where its takes are and where its cuts live.</summary>
        private readonly struct Library
        {
            public readonly string Folder;

            /// <summary>Only files under one of these sub-folders are takes (the rest are models, props and demo content); null = anywhere in the library.</summary>
            public readonly string[] TakeFolders;

            public Library(string folder, string[] takeFolders)
            {
                Folder = folder;
                TakeFolders = takeFolders;
            }

            public string CutsFile => Folder + "cuts.json";

            public bool Owns(string path)
            {
                if (!path.StartsWith(Folder) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;
                if (TakeFolders == null) return true;

                string directory = Path.GetDirectoryName(path).Replace('\\', '/') + "/";
                foreach (string sub in TakeFolders)
                    if (directory == Folder + sub) return true;
                return false;
            }
        }

        // Mixamo: direct children only. Hold/, Recoil/ and Masks/ below it hold generated assets.
        // Motion Cast: the "No Root" copies only — the others are the same takes travelling across the floor.
        // Each take builds its avatar from its own skeleton: the cooking takes' root node is not the one on the
        // pack's robot model, so copying the robot's avatar is refused ("Copied Avatar Rig Configuration mis-match").
        private static readonly Library[] Libraries =
        {
            new Library("Assets/Game/Art/Animations/Humanoid/", new[] { "" }),
            new Library("Assets/ThirdParty/MocapCentral/", null),
            new Library("Assets/ThirdParty/Motion Cast-FREE01/", new[] { "No Root Animations/" }),
            new Library("Assets/ThirdParty/ExplosiveLLC/Crafting Mecanim Animation Pack FREE/", new[] { "Animations/" }),
            new Library("Assets/ThirdParty/EEJANAI_Team/", new[] { "CookingAnimations/FBX/" }),
        };

        /// <summary>Every folder holding takes this importer owns — what the content audit searches for clips nothing uses.</summary>
        public static IEnumerable<string> TakeFolders =>
            Libraries.SelectMany(library => library.TakeFolders == null
                    ? new[] { library.Folder }
                    : library.TakeFolders.Select(sub => library.Folder + sub))
                .Select(folder => folder.TrimEnd('/'));

        // Below this the take's FBX time mode was misread as 1 fps (see CmuClipImporter); nothing real is captured this slowly.
        private const float LowestPlausibleSampleRate = 10f;

        // Names that cycle without being cut: the packs name their cycles. Fidget idles (look around, scratch an
        // arm) and the Start/Stop of a cycle play once, so a bare "Idle" or "Walk" in the name is not enough.
        private static readonly Regex CycleName = new Regex(
            "_Loop|Idle_01$|Loco_Walk_(Fwd|Bwd|Left|Right|01)$|Walk_Swagger$|\\(loop\\)|loop$|^Carry-Idle$|^Idle$|WalkForward$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Pack prefixes and bake suffixes that are not part of what a clip is.
        private static readonly Regex PackAffixes = new Regex("^MCU_a[mf]_|^MCU_|^Crafter@|_Baked$|_NoRM$", RegexOptions.Compiled);

        /// <summary>Bumped whenever a rule here changes, so Unity reimports what it already imported.</summary>
        public override uint GetVersion() => 5;

        private static bool TryFindLibrary(string path, out Library library)
        {
            foreach (Library candidate in Libraries)
            {
                if (!candidate.Owns(path)) continue;
                library = candidate;
                return true;
            }
            library = default;
            return false;
        }

        private void OnPreprocessModel()
        {
            if (!TryFindLibrary(assetPath, out Library library)) return;

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;

            // The cuts are an input to this import; a changed cuts.json must reimport the takes.
            context.DependsOnSourceAsset(library.CutsFile);
        }

        private void OnPreprocessAnimation()
        {
            if (!TryFindLibrary(assetPath, out Library library)) return;

            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            TakeInfo[] takes = importer.importedTakeInfos;
            if (defaults.Length == 0 || takes.Length == 0) return;

            float rate = takes[0].sampleRate;
            if (rate < LowestPlausibleSampleRate)
            {
                Debug.LogError($"[LibraryClipImporter] {assetPath} reports {rate} fps: its FBX TimeMode is 30 fps " +
                               "drop-frame, which Unity misreads. Rewrite GlobalSettings TimeMode 7 to 6 in the file.");
                return;
            }

            string take = Path.GetFileNameWithoutExtension(assetPath);
            float lastFrame = defaults[0].lastFrame;
            var clips = new List<ModelImporterClipAnimation>();

            foreach (ClipCut cut in CutsOf(library, take))
            {
                // A fresh copy per cut: ModelImporterClipAnimation is a class (see CmuClipImporter).
                ModelImporterClipAnimation clip = importer.defaultClipAnimations[0];
                clip.name = cut.name;
                clip.firstFrame = Mathf.Clamp(cut.start * rate, 0f, lastFrame);
                clip.lastFrame = Mathf.Clamp(cut.end * rate, clip.firstFrame + 1f, lastFrame);
                clip.loopTime = cut.loop;
                BakeInPlace(clip, cut.travelsVertically);
                clips.Add(clip);
            }

            if (clips.Count == 0)
            {
                ModelImporterClipAnimation whole = importer.defaultClipAnimations[0];
                whole.name = PackAffixes.Replace(take, "");
                whole.loopTime = CycleName.IsMatch(whole.name);
                BakeInPlace(whole, false);
                clips.Add(whole);
            }

            importer.clipAnimations = clips.ToArray();
        }

        /// <summary>
        /// Played in place. Root motion is off on every humanoid Animator here, so horizontal travel
        /// is left OUT of the pose — extracted as root motion, which is simply discarded. "Bake into
        /// pose" would do the opposite: it keeps the travel in the pelvis, and a fall or a crawl
        /// then slides 2-4 m out of its capsule and snaps back when the action ends. Height stays in
        /// the pose (a kneel, a body on the floor are low), unless the take climbs. Orientation comes
        /// from the BODY, not the take's own root, like every other humanoid library in the project.
        /// Cycles are loop-posed so a seam that closes badly is blended shut. Vendor events name
        /// receivers this project does not have.
        /// </summary>
        private static void BakeInPlace(ModelImporterClipAnimation clip, bool travelsVertically)
        {
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = false;
            clip.lockRootHeightY = !travelsVertically;
            clip.keepOriginalPositionY = true;
            clip.heightFromFeet = false;
            clip.lockRootPositionXZ = false;
            clip.loopPose = clip.loopTime;
            clip.events = Array.Empty<AnimationEvent>();
        }

        private static IEnumerable<ClipCut> CutsOf(Library library, string take)
        {
            if (!File.Exists(library.CutsFile)) return Enumerable.Empty<ClipCut>();
            return ClipCutList.Load(library.CutsFile).cuts.Where(cut => cut.take == take);
        }

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!TryFindLibrary(assetPath, out _) || clip.humanMotion) return;

            Debug.LogError($"[LibraryClipImporter] '{clip.name}' in {assetPath} imported as a generic clip, so it " +
                           "will animate no humanoid. The take's avatar did not come out Humanoid.");
        }
    }
}
