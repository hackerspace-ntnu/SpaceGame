using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RoverBogieIK))]
public class RoverBogieIKEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        GUILayout.Space(8f);

        RoverBogieIK bogie = (RoverBogieIK)target;
        if (GUILayout.Button("Auto Setup From Children"))
        {
            Undo.RecordObject(bogie, "Auto Setup Rover Bogie IK");
            bogie.AutoSetupFromChildren();
            EditorUtility.SetDirty(bogie);
        }
    }
}
