using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// An outpost's inspector: its prefab, its residents and its ground, then the settlement buttons. The settlement's own
    /// config slot and size multiplier are hidden: an outpost is its own configuration, and a multiplier would place the
    /// outpost itself twice.
    /// </summary>
    [CustomEditor(typeof(Outpost))]
    public sealed class OutpostEditor : Editor
    {
        private static readonly string[] Hidden = { "m_Script", "config", "sizeMultiplier" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, Hidden);
            serializedObject.ApplyModifiedProperties();

            var outpost = (Outpost)target;
            if (outpost.OutpostPrefab == null)
                EditorGUILayout.HelpBox("Assign an outpost prefab, then Generate.", MessageType.Info);
            else if (!outpost.HasResidents)
                EditorGUILayout.HelpBox("No culture: the residents are plain NPCs with no day, home or speech.", MessageType.Warning);

            SettlementEditor.DrawButtons(outpost);
        }
    }
}
