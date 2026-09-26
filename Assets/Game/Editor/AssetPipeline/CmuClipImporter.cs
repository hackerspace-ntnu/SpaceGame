using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Import rules for the curated CMU motion-capture takes under <see cref="Folder"/>: each take
    /// becomes the clips its <c>cuts.json</c> entry names, Humanoid, played in place.
    ///
    /// <para>
    /// The CMU takes are raw sessions, not game clips: one file can hold a walk-in, three bows and
    /// a walk-out, all with the actor travelling across the capture floor. So nothing in the file's
    /// own take is used as-is. <see cref="CutsFile"/> lists, per take, the clips to cut out of it —
    /// a name, a start and an end in SECONDS (converted with the take's own sample rate, so the
    /// Blender analysis that produced the numbers and this importer cannot disagree about frames)
    /// and whether it loops. A take with no entry imports no clips.
    /// </para>
    /// <para>
    /// Root motion is baked away: position around the centre of mass, height from the feet,
    /// rotation from the body. Every humanoid here animates in place with <c>applyRootMotion</c>
    /// off, and a take left travelling would walk the pose out of its own capsule.
    /// </para>
    /// <para>
    /// <b>The pack's FBX files say TimeMode 7 (30 fps drop-frame), which Unity reads as a 1 fps
    /// sample rate:</b> every cut shorter than a second came out one second long, and every clip was
    /// resampled at 1 Hz. Each take copied in here has that one int rewritten to 6 (plain 30 fps);
    /// a take that still reports a rate this low is refused loudly below.
    /// </para>
    /// <para>
    /// The full 2,548-take pack lives Unity-invisible (and git-ignored) at
    /// <c>Assets/Game/Art/Animations/_Packed~/</c>; only the takes copied here are imported.
    /// </para>
    /// </summary>
    public class CmuClipImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/CMU/";
        public const string CutsFile = Folder + "cuts.json";

        [Serializable]
        private sealed class Cut
        {
            public string take;
            public string name;
            public float start;
            public float end;
            public bool loop;
        }

        [Serializable]
        private sealed class CutList
        {
            public Cut[] cuts = Array.Empty<Cut>();
        }

        private bool InScope => assetPath.StartsWith(Folder) && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>Bumped whenever a rule here changes, so Unity reimports what it already imported.</summary>
        public override uint GetVersion() => 2;

        // Below this the take's time mode was misread (see the class summary); nothing real is captured this slowly.
        private const float LowestPlausibleSampleRate = 10f;

        private void OnPreprocessModel()
        {
            if (!InScope) return;

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;

            // The cuts are an input to this import; a changed cuts.json must reimport the takes.
            context.DependsOnSourceAsset(CutsFile);
        }

        private void OnPreprocessAnimation()
        {
            if (!InScope) return;

            var importer = (ModelImporter)assetImporter;
            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            TakeInfo[] takes = importer.importedTakeInfos;
            if (defaults.Length == 0 || takes.Length == 0) return;

            string take = Path.GetFileNameWithoutExtension(assetPath);
            float rate = takes[0].sampleRate;
            if (rate < LowestPlausibleSampleRate)
            {
                Debug.LogError($"[CmuClipImporter] {assetPath} reports {rate} fps: its FBX TimeMode is 30 fps " +
                               "drop-frame, which Unity misreads. Rewrite GlobalSettings TimeMode 7 to 6 in the file.");
                return;
            }

            float lastFrame = defaults[0].lastFrame;
            var clips = new List<ModelImporterClipAnimation>();

            foreach (Cut cut in LoadCuts().cuts)
            {
                if (cut.take != take) continue;

                // A fresh copy per cut: ModelImporterClipAnimation is a class, so reusing one entry
                // left every clip of a many-cut take holding the settings of its last cut.
                ModelImporterClipAnimation clip = importer.defaultClipAnimations[0];
                clip.name = cut.name;
                clip.firstFrame = Mathf.Clamp(cut.start * rate, 0f, lastFrame);
                clip.lastFrame = Mathf.Clamp(cut.end * rate, clip.firstFrame + 1f, lastFrame);
                clip.loopTime = cut.loop;

                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = false;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = false;
                clip.heightFromFeet = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = false;
                clip.events = new AnimationEvent[0];
                clips.Add(clip);
            }

            importer.clipAnimations = clips.ToArray();
        }

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!InScope || clip.humanMotion) return;

            Debug.LogError($"[CmuClipImporter] '{clip.name}' in {assetPath} imported as a generic clip, so it " +
                           "will animate no humanoid. The take's avatar did not come out Humanoid.");
        }

        private static CutList LoadCuts()
        {
            if (!File.Exists(CutsFile))
                throw new FileNotFoundException($"[CmuClipImporter] {CutsFile} is missing; every CMU take needs its cuts.");
            return JsonUtility.FromJson<CutList>(File.ReadAllText(CutsFile));
        }
    }
}
