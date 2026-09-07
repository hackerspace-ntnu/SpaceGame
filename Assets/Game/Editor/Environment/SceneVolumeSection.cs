// The global Volume's colour grading, drawn inside the Look Lab.
//
// It belongs there because it is upstream of everything the Look Lab tunes: the quantize
// filter snaps whatever the Volume hands it, so exposure and saturation decide which
// palette entries a scene can even reach. Judging a palette while the grade that feeds it
// lives in another window is guesswork.
//
// Unlike the rest of the window this writes to an *asset*, so it goes through Undo and
// SetDirty exactly as the Volume's own Inspector would. There is no ephemeral mode to
// offer here: a VolumeProfile has no in-memory copy that a domain reload would restore.
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.EditorTools.Environment
{
    internal static class SceneVolumeSection
    {
        /// <summary>
        /// Draws exposure, contrast and saturation for the highest-priority global Volume
        /// in the open scenes, or explains why it cannot. Returns true if anything changed.
        /// </summary>
        public static bool Draw()
        {
            Volume volume = FindGlobalVolume();
            if (volume == null)
            {
                EditorGUILayout.HelpBox(
                    "No global Volume in the open scenes, so there is no grade to tune.",
                    MessageType.Info);
                return false;
            }

            VolumeProfile profile = volume.sharedProfile;
            if (profile == null)
            {
                EditorGUILayout.HelpBox(
                    $"'{volume.name}' has no profile assigned.", MessageType.Info);
                return false;
            }

            EditorGUILayout.LabelField(profile.name, EditorStyles.miniLabel);

            if (!profile.TryGet(out ColorAdjustments grade))
            {
                if (GUILayout.Button("Add Color Adjustments"))
                {
                    Undo.RecordObject(profile, "Add Color Adjustments");
                    profile.Add<ColorAdjustments>(overrides: true);
                    EditorUtility.SetDirty(profile);
                }

                return false;
            }

            EditorGUI.BeginChangeCheck();

            // Each override is inert until its own checkbox is on, so a slider that appears
            // to do nothing is nearly always an override that was never enabled — the two
            // are drawn on one line here for exactly that reason.
            float exposure = Row("Post exposure", grade.postExposure, -3f, 3f);
            float contrast = Row("Contrast", grade.contrast, -100f, 100f);
            float saturation = Row("Saturation", grade.saturation, -100f, 100f);

            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            Undo.RecordObject(profile, "Tune the global Volume");
            grade.postExposure.value = exposure;
            grade.contrast.value = contrast;
            grade.saturation.value = saturation;
            EditorUtility.SetDirty(profile);
            return true;
        }

        private static float Row(string label, VolumeParameter<float> parameter,
                                 float min, float max)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                parameter.overrideState = EditorGUILayout.Toggle(
                    parameter.overrideState, GUILayout.Width(16f));
                using (new EditorGUI.DisabledScope(!parameter.overrideState))
                {
                    return EditorGUILayout.Slider(label, parameter.value, min, max);
                }
            }
        }

        /// <summary>
        /// The one the camera actually resolves to: global, enabled, highest priority.
        /// A local Volume is skipped because whether it applies depends on where the camera
        /// is standing, which is not a thing this window can show.
        /// </summary>
        private static Volume FindGlobalVolume()
        {
            return Object.FindObjectsByType<Volume>(FindObjectsSortMode.None)
                .Where(v => v.isGlobal && v.enabled && v.gameObject.activeInHierarchy)
                .OrderByDescending(v => v.priority)
                .FirstOrDefault();
        }
    }
}
