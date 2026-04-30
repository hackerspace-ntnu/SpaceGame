#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Draws a collapsible description box at the top of every BehaviourModuleBase Inspector.
// The description comes from the module's ModuleDescription property override.
[CustomEditor(typeof(BehaviourModuleBase), editorForChildClasses: true)]
public class BehaviourModuleEditor : Editor
{
    private static GUIStyle helpStyle;

    private const string PrefKeyPrefix = "BehaviourModuleHelp_";

    private static GUIStyle HelpStyle
    {
        get
        {
            if (helpStyle == null)
            {
                helpStyle = new GUIStyle(EditorStyles.label)
                {
                    wordWrap = true,
                    richText = true,
                    padding = new RectOffset(8, 8, 6, 6)
                };
                helpStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            }

            return helpStyle;
        }
    }

    public override void OnInspectorGUI()
    {
        if (target == null)
        {
            return;
        }

        var module = target as BehaviourModuleBase;
        if (module == null)
        {
            DrawDefaultInspector();
            return;
        }

        string desc = module.ModuleDescription;

        if (!string.IsNullOrEmpty(desc))
        {
            string prefKey = PrefKeyPrefix + target.GetType().Name;
            bool expanded = EditorPrefs.GetBool(prefKey, false);

            Rect headerRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.18f, 0.18f));

            Rect foldoutRect = new Rect(headerRect.x + 4, headerRect.y + 3, headerRect.width - 8, headerRect.height - 4);
            bool newExpanded = EditorGUI.Foldout(foldoutRect, expanded, " 📋  Description", true);
            if (newExpanded != expanded)
                EditorPrefs.SetBool(prefKey, newExpanded);

            if (newExpanded)
            {
                Rect boxRect = EditorGUILayout.BeginVertical();
                EditorGUI.DrawRect(new Rect(boxRect.x, boxRect.y, boxRect.width, boxRect.height), new Color(0.15f, 0.15f, 0.15f));
                GUILayout.Space(4);
                GUILayout.Label(desc, HelpStyle);
                GUILayout.Space(4);
                EditorGUILayout.EndVertical();
            }

            GUILayout.Space(4);
        }

        DrawDefaultInspector();
    }
}
#endif
