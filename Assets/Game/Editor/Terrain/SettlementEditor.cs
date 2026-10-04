using SpaceGame.Agents.Residents.EditorTools;
using SpaceGame.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The settlement's buttons. <b>Generate</b> is the whole thing in one press — layout, decorations, the
    /// people in their beds, made residents when the config has a culture — and stays quick enough to preview
    /// with. <b>Generate + Bake World NavMesh</b> does the same and then re-bakes the world NavMesh, which
    /// agents need to walk the new layout in play.
    /// </summary>
    [CustomEditor(typeof(Settlement), true)]
    public sealed class SettlementEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var settlement = (Settlement)target;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Generate")) Generate(settlement);
                if (GUILayout.Button(SettlementNavMeshBake.MenuLabel)) SettlementNavMeshBake.GenerateAndBake(settlement);
                if (GUILayout.Button("Clear"))
                {
                    settlement.Clear();
                    EditorSceneManager.MarkSceneDirty(settlement.gameObject.scene);
                }
            }
            if (settlement.HasResidents && GUILayout.Button("Open Residents Window")) ResidentsWindow.Open(settlement);
        }

        [MenuItem("CONTEXT/Settlement/Generate")]
        private static void GenerateFromMenu(MenuCommand command) => Generate((Settlement)command.context);

        [MenuItem("CONTEXT/Settlement/Generate", true)]
        private static bool CanGenerate() => !EditorApplication.isPlaying;

        /// <summary>
        /// Gives the character prefabs the resident stack, generates, and checks what the residents will need
        /// at runtime. Leaves the world NavMesh as it was: use Generate + Bake World NavMesh before play.
        /// </summary>
        public static void Generate(Settlement settlement)
        {
            if (settlement.HasResidents) ResidentStackBuilder.EnsureStack(settlement.CharacterPrefabs());
            settlement.Generate();
            // A settlement placed as a prefab instance keeps only what is recorded as an override: the heart Generate just found
            // (a private field) would be lost the first time the scene is reloaded from disk.
            PrefabUtility.RecordPrefabInstancePropertyModifications(settlement);
            ResidentsWindow.ForgetPreview();
            if (settlement.HasResidents) ResidentValidator.Report(settlement);
            EditorSceneManager.MarkSceneDirty(settlement.gameObject.scene);
        }
    }
}
