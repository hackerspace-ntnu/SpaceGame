using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Import rules for the Quaternius Universal Animation Library under
    /// <see cref="Folder"/>: one Humanoid clip per take, looped by name, pose-baked, event-free.
    ///
    /// <para>
    /// The library ships as one FBX holding a mannequin and every take, on an Unreal-mannequin
    /// skeleton (<c>pelvis</c>, <c>spine_01</c>, <c>clavicle_l</c>…) that Unity's Humanoid
    /// auto-mapper reads without help. Its clips only reach our characters through retargeting,
    /// so the avatar is the single point of failure — and Unity does not fail loudly when it
    /// cannot build one: it downgrades the avatar to generic and imports clips that animate
    /// nothing (see ArtPipeline.md, "a re-exported character stops animating and the console is
    /// clean"). <see cref="OnPostprocessAnimation"/> turns that silence into an error.
    /// </para>
    /// <para>
    /// The settings are re-derived on every import from the takes themselves, so a clip's
    /// settings are not edited in the inspector — change the rule here instead. Clip names are
    /// never renamed either: an FBX clip's fileID is derived from its name, and every
    /// <c>CharacterAction</c> that uses the clip holds that fileID.
    /// </para>
    /// </summary>
    public class QuaterniusClipImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/Quaternius/";

        /// <summary>The library's reference-pose take: a T-pose, not an animation.</summary>
        private const string PoseTake = "A_TPose";

        /// <summary>Takes whose name ends with this are cycles.</summary>
        private const string LoopSuffix = "_Loop";

        /// <summary>
        /// Held poses the library names without the suffix — <c>Sword_Idle</c>,
        /// <c>Pistol_Aim_Down</c> — which are held for as long as a weapon is up, so they loop too.
        /// Every other take plays once.
        /// </summary>
        private const string IdleSuffix = "_Idle";
        private const string AimInfix = "_Aim_";

        private bool InScope => assetPath.StartsWith(Folder);

        /// <summary>Bumped whenever a rule here changes, so Unity reimports what it already imported.</summary>
        public override uint GetVersion() => 2;

        private void OnPreprocessModel()
        {
            if (!InScope) return;

            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!InScope) return;

            var importer = (ModelImporter)assetImporter;
            var clips = new List<ModelImporterClipAnimation>();

            foreach (ModelImporterClipAnimation take in importer.defaultClipAnimations)
            {
                string name = StripArmaturePrefix(take.name);
                if (name == PoseTake) continue;

                take.name = name;
                take.loopTime = name.EndsWith(LoopSuffix) || name.EndsWith(IdleSuffix) || name.Contains(AimInfix);

                // In-place clips: whatever the pelvis does stays in the pose. Root motion is off
                // on every humanoid Animator here, so anything left unbaked would be motion nobody
                // reads — a jump that never leaves the ground, a lunge that snaps back.
                // Orientation from the BODY, not the take's own root: the library's root node faces
                // backwards, and "Original" played every full-body clip with the character turned
                // round to face away from where it was walking. The Mixamo clips here use the body too.
                take.lockRootRotation = true;
                take.keepOriginalOrientation = false;
                take.lockRootHeightY = true;
                take.keepOriginalPositionY = true;
                take.heightFromFeet = false;
                take.lockRootPositionXZ = true;
                take.keepOriginalPositionXZ = true;

                // Vendor events name receivers this project does not have, and each one logs
                // "AnimationEvent has no receiver" every time it fires. Timing lives on the
                // CharacterAction's phase marks instead.
                take.events = new AnimationEvent[0];

                clips.Add(take);
            }

            importer.clipAnimations = clips.ToArray();
        }

        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!InScope || clip.humanMotion) return;

            Debug.LogError($"[QuaterniusClipImporter] '{clip.name}' in {assetPath} imported as a " +
                           "generic clip, so it will animate no humanoid. The model's avatar did not " +
                           "come out Humanoid — check the FBX's Rig tab (isHuman) before using it.");
        }

        /// <summary>Blender-exported takes arrive as "Armature|Walk_Loop"; keep only the take.</summary>
        private static string StripArmaturePrefix(string take)
        {
            int bar = take.LastIndexOf('|');
            return bar < 0 ? take : take.Substring(bar + 1);
        }
    }
}
