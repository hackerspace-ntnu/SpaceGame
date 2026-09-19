// Raising every authored agent to VisionBaseline, and keeping it there.
//
// Changing a component's defaults changes nothing already serialized: every prefab saved before the
// change still carries the old 110 degree cone and 35 m range, and so does every creature whose
// builder wrote its own narrower numbers. This pass walks the agent prefabs and the TargetingProfile
// assets and lifts any value below the baseline up to it. It never lowers a wider value -- a prey
// animal's 220 degree cone is a design choice, not drift.
//
// Idempotent, in the mould of EntityFactionWiring: re-running it on a wired project changes nothing.
// VisionBaselineTests reads the same rules, so a creature that falls below the baseline fails there
// rather than quietly going half-blind.
//
// Re-run from: Tools > SpaceGame > Agents > Wire Vision Baseline
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SpaceGame.Agents;

namespace SpaceGame.EditorTools
{
    public static class VisionBaselineWiring
    {
        private const string AgentPrefabFolder = "Assets/Game/Prefabs/Agents";

        /// <summary>
        /// Prefabs, by file name, that deliberately see less than the baseline.
        ///
        /// <para>
        /// LightningConjurer: a stationary boss whose AgentTargeting ranges ARE its activation
        /// distance (<c>LightningConjurerBuilder.ActivationRange</c>, line of sight off). It has no
        /// PerceptionModule; raising its acquisition range to 80 m would wake it from three times
        /// the distance its encounter is built around.
        /// </para>
        /// </summary>
        private static readonly string[] Exempt = { "LightningConjurer" };

        public static bool IsExempt(GameObject prefab) => Exempt.Contains(prefab.name);

        // Prefabs that nest other prefabs come after the ones they nest, so a base is raised before
        // anything containing it is inspected and no redundant override is written on the outer one.
        public static IEnumerable<string> AgentPrefabPaths() =>
            AssetDatabase.FindAssets("t:Prefab", new[] { AgentPrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => AssetDatabase.GetDependencies(path, true).Count(d => d.EndsWith(".prefab")))
                .ThenBy(path => path, System.StringComparer.Ordinal);

        public static IEnumerable<TargetingProfile> TargetingProfiles() =>
            AssetDatabase.FindAssets("t:TargetingProfile")
                .Select(guid => AssetDatabase.LoadAssetAtPath<TargetingProfile>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(profile => profile != null);

        /// <summary>
        /// Lift every PerceptionModule and inline AgentTargeting under <paramref name="root"/> to the
        /// baseline. Each value below it is described in <paramref name="report"/>; with
        /// <paramref name="apply"/> false nothing is written, which is how the test asks the question.
        /// Answers whether anything was below.
        /// </summary>
        public static bool Raise(GameObject root, List<string> report, bool apply)
        {
            bool below = false;

            foreach (PerceptionModule perception in root.GetComponentsInChildren<PerceptionModule>(true))
            {
                var so = new SerializedObject(perception);
                below |= RaiseTo(so, "fieldOfViewAngle", VisionBaseline.MinFieldOfView, root.name, report);
                below |= RaiseTo(so, "memoryDuration", VisionBaseline.MinMemory, root.name, report);
                if (apply) so.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (AgentTargeting targeting in root.GetComponentsInChildren<AgentTargeting>(true))
            {
                var so = new SerializedObject(targeting);

                // An assigned profile overrides every inline value; the profile is checked on its own.
                if (so.FindProperty("profile").objectReferenceValue != null) continue;

                below |= RaiseTo(so, "acquisitionRange", VisionBaseline.MinAcquisitionRange, root.name, report);
                below |= RaiseTo(so, "loseRange", VisionBaseline.MinLoseRange, root.name, report);
                if (apply) so.ApplyModifiedPropertiesWithoutUndo();
            }

            return below;
        }

        /// <summary>The same, for a TargetingProfile asset.</summary>
        public static bool Raise(TargetingProfile profile, List<string> report, bool apply)
        {
            var so = new SerializedObject(profile);
            bool below = RaiseTo(so, "acquisitionRange", VisionBaseline.MinAcquisitionRange, profile.name, report);
            below |= RaiseTo(so, "loseRange", VisionBaseline.MinLoseRange, profile.name, report);
            below |= RaiseTo(so, "memoryDuration", VisionBaseline.MinMemory, profile.name, report);

            if (apply && below)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(profile);
            }

            return below;
        }

        private static bool RaiseTo(SerializedObject so, string property, float minimum, string owner,
                                    List<string> report)
        {
            // A renamed field would otherwise read as "nothing below the baseline" and pass forever.
            SerializedProperty value = so.FindProperty(property)
                ?? throw new System.InvalidOperationException(
                    $"{so.targetObject.GetType().Name} has no serialized '{property}'. It was renamed; " +
                    "fix VisionBaselineWiring.");

            if (value.floatValue >= minimum) return false;

            report.Add($"{owner}: {so.targetObject.GetType().Name}.{property} {value.floatValue} -> {minimum}");
            value.floatValue = minimum;
            return true;
        }

        [MenuItem("Tools/SpaceGame/Agents/Wire Vision Baseline")]
        public static void WireAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[VisionBaselineWiring] Leave Play mode first; prefab saves are discarded while playing.");
                return;
            }

            var report = new List<string>();
            int prefabsChanged = 0;

            foreach (string path in AgentPrefabPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || IsExempt(prefab)) continue;

                // Ask first against the asset, so a prefab already at the baseline is never re-saved.
                if (!Raise(prefab, new List<string>(), apply: false)) continue;

                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Raise(contents, report, apply: true);
                    PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);

                    // A read-only AssetDatabase discards a prefab save and says nothing at all.
                    if (!saved)
                    {
                        Debug.LogError($"[VisionBaselineWiring] Could not save {path}. The AssetDatabase refused the write.");
                        continue;
                    }

                    prefabsChanged++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            int profilesChanged = TargetingProfiles().Count(profile => Raise(profile, report, apply: true));

            AssetDatabase.SaveAssets();
            Debug.Log($"[VisionBaselineWiring] {prefabsChanged} prefab(s) and {profilesChanged} targeting " +
                      $"profile(s) raised to the vision baseline.\n{string.Join("\n", report)}");
        }
    }
}
