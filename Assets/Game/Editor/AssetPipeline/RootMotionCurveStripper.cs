using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Strips curves bound to the model root out of imported animation clips.
    ///
    /// A curve whose binding path is the empty string drives the ROOT GameObject's own transform —
    /// the same transform the scene places, the NavMesh drives, and UnderTerrainGuard lifts. When a
    /// clip carries those curves the Animator overwrites the object's world position every frame
    /// with the values baked at export, which sit at the origin. The object is placed at its spawn
    /// point, play starts, and it snaps to 0,0,0.
    ///
    /// This is NOT what the root-motion import toggles guard against. Those (lockRootHeightY and
    /// friends) only shape motion that Unity extracts and re-applies through applyRootMotion, which
    /// is off on these rigs. An explicit root curve bypasses that path entirely and writes the
    /// transform directly, so the toggles can all read "locked" while the object still teleports.
    ///
    /// Rigs built from rigid parts are the ones that hit this: their meshes are parented to bones
    /// rather than skinned, so the exporter treats the armature root as an animated object and bakes
    /// a curve for it. A skinned rig deforms through the bindpose and never gets one — which is why
    /// the DuneRat (skinned, 0 root curves) always worked and the Golem (30 rigid parts, 10 root
    /// curves per clip) never did.
    ///
    /// Removing the curve leaves the bone hierarchy untouched: every child bone keeps its own curve,
    /// so the animation plays exactly as authored — it simply no longer claims ownership of where
    /// the object stands. Anything that genuinely needs animation-driven displacement should use
    /// root motion, which survives this because Unity extracts it before these curves are read.
    /// </summary>
    public class RootMotionCurveStripper : AssetPostprocessor
    {
        private void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            int removed = 0;

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                // Empty path == the root object itself. A bone's curve always carries its hierarchy
                // path, so this can never match a child by accident.
                if (!string.IsNullOrEmpty(binding.path)) continue;
                if (!binding.propertyName.StartsWith("m_Local")) continue;

                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }

            if (removed > 0)
            {
                Debug.Log($"[RootMotionCurveStripper] Removed {removed} root-transform curve(s) from " +
                          $"'{clip.name}' in {assetPath}. The clip no longer moves the object it is on.");
            }
        }
    }
}
