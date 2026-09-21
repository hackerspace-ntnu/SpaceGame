// The Generate button.
//
// RobotSettlementGenerator only has [ContextMenu] entries, which means right-clicking the component
// header and knowing they are there. A town is something you iterate on — generate, look, widen the
// spacing, generate again — so the commands are buttons, and the last Verify report is on the
// component where you can read it rather than somewhere in the console behind fifty other lines.
//
// The recipe asset is optional and this inspector keeps it that way: the settings on the component
// are the ordinary case, and the two buttons here move settings into an asset or back out of one,
// so choosing wrongly at the start costs nothing.
using UnityEditor;
using UnityEngine;
using SpaceGame.World.Towns;

namespace SpaceGame.EditorTools
{
    [CustomEditor(typeof(TownGenerator))]
    public class TownGeneratorEditor : Editor
    {
        private const string SettingsProperty = "settings";

        public override void OnInspectorGUI()
        {
            var generator = (TownGenerator)target;

            DrawProperties(generator);
            DrawRecipeSwap(generator);

            EditorGUILayout.Space();

            if (generator.GeneratedRoot != null)
                EditorGUILayout.HelpBox(
                    "Generate moves what is already here rather than replacing it, so changing the " +
                    "spacing or the radii keeps every object's save identity. Only prefabs the " +
                    "settings no longer ask for are destroyed, and Generate refuses to do that " +
                    "until Confirm Regenerate is ticked.",
                    MessageType.Info);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Generate", GUILayout.Height(30)))
                        Run(generator, "Generate Town", generator.Generate);

                    if (GUILayout.Button("Reroll", GUILayout.Height(30), GUILayout.Width(80)))
                        Run(generator, "Reroll Town", generator.Reroll);

                    if (GUILayout.Button("Clear", GUILayout.Height(30), GUILayout.Width(80)))
                        Run(generator, "Clear Town", generator.Clear);
                }

                if (GUILayout.Button("Check settings without generating"))
                {
                    bool ok = generator.Precheck(out string why);
                    generator.lastReportOk = ok;
                    generator.lastReport = ok ? "Settings look fit to generate." : why;
                    MarkDirty(generator);
                }
            }

            if (Application.isPlaying)
                EditorGUILayout.HelpBox(
                    "Generation is edit-time only. The world NavMesh is one author-time bake and " +
                    "nothing bakes at runtime, so a town generated in play mode could not be walked on.",
                    MessageType.Info);

            if (!string.IsNullOrEmpty(generator.lastReport))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last report", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(generator.lastReport,
                    generator.lastReportOk ? MessageType.Info : MessageType.Error);
            }
        }

        /// <summary>
        /// The default inspector, except that the inline settings are greyed out while a shared
        /// recipe asset is overriding them — otherwise you edit fields for ten minutes and wonder
        /// why the town never changes.
        /// </summary>
        private void DrawProperties(TownGenerator generator)
        {
            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                bool overridden = property.propertyPath == SettingsProperty && generator.UsingSharedRecipe;
                bool isScript = property.propertyPath == "m_Script";

                using (new EditorGUI.DisabledScope(overridden || isScript))
                    EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRecipeSwap(TownGenerator generator)
        {
            EditorGUILayout.Space();

            if (generator.UsingSharedRecipe)
            {
                if (!GUILayout.Button("Copy the recipe into these settings and unlink it")) return;

                Undo.RecordObject(generator, "Unlink Town Recipe");
                generator.settings = Clone(generator.sharedRecipe.recipe);
                generator.sharedRecipe = null;
                MarkDirty(generator);
                return;
            }

            if (!GUILayout.Button("Save these settings as a shared Recipe asset…")) return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Town Recipe", "TownRecipe", "asset",
                "A Town Recipe asset lets several towns share one set of settings.");

            if (string.IsNullOrEmpty(path)) return;

            var asset = CreateInstance<TownRecipeAsset>();
            asset.recipe = Clone(generator.settings);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(generator, "Link Town Recipe");
            generator.sharedRecipe = asset;
            MarkDirty(generator);

            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// A deep copy. A field-by-field assignment would leave both sides sharing the same group
        /// lists, so editing the component would silently edit the asset as well.
        /// <c>EditorJsonUtility</c> rather than <c>JsonUtility</c> because only it round-trips the
        /// prefab and material references intact.
        /// </summary>
        internal static TownRecipe Clone(TownRecipe source)
        {
            var copy = new TownRecipe();
            if (source != null) EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), copy);
            return copy;
        }

        private static void Run(TownGenerator generator, string label, System.Action command)
        {
            Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, label);
            command();
            MarkDirty(generator);
        }

        private static void MarkDirty(TownGenerator generator)
        {
            EditorUtility.SetDirty(generator);

            if (!Application.isPlaying && generator.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }
    }
}
