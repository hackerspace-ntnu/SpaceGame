using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view interactive handles for a <see cref="TerrainFeatureSpawner"/>'s footprint. Split out
/// of <c>TerrainFeatureSpawnerEditor</c> to keep both files small.
///
/// Two footprint modes, switched by the spawner's chosen feature type:
///   • AREA features  — a resizable box: six face-drag handles let the designer size the volume.
///   • LINEAR features — an editable Catmull-Rom path: a movable handle per control point, plus
///     "+"/"-" buttons to add/remove points, plus a half-width slider handle.
///
/// All edits go through <see cref="Undo"/> and mark the spawner dirty so the scene saves correctly.
/// </summary>
public static class TerrainFeatureHandles
{
    /// <summary>Draws and processes the footprint handles. Call from the editor's OnSceneGUI.</summary>
    public static void Draw(TerrainFeatureSpawner spawner)
    {
        if (spawner == null) return;
        if (spawner.UsesPath) DrawPathHandles(spawner);
        else DrawBoxHandles(spawner);
    }

    // -------------------------------------------------------------------------
    // Area features — resizable box
    // -------------------------------------------------------------------------

    static void DrawBoxHandles(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        Vector3 half = spawner.BoxHalfExtents;

        // One drag handle per +X/+Y/+Z face. Dragging a face moves only that axis' half-extent.
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        Vector3 newHalf = half;
        bool changed = false;

        for (int a = 0; a < 3; a++)
        {
            Vector3 localFace = Vector3.Scale(axes[a], half);
            Vector3 worldFace = t.TransformPoint(localFace);
            float size = HandleUtility.GetHandleSize(worldFace) * 0.18f;

            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(0.5f, 0.85f, 1f, 1f);
            Vector3 dragged = Handles.Slider(worldFace, t.TransformDirection(axes[a]),
                size, Handles.DotHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                // Project the new world handle position back to a local half-extent on this axis.
                Vector3 local = t.InverseTransformPoint(dragged);
                float v = Mathf.Max(1f, Vector3.Dot(local, axes[a]));
                newHalf[a] = v;
                changed = true;
            }
        }

        if (changed)
        {
            Undo.RecordObject(spawner, "Resize Terrain Feature Box");
            spawner.BoxHalfExtents = newHalf;
            EditorUtility.SetDirty(spawner);
        }

        // Passive wireframe for context.
        Handles.color = new Color(0.5f, 0.85f, 1f, 0.4f);
        Handles.matrix = t.localToWorldMatrix;
        Handles.DrawWireCube(Vector3.zero, newHalf * 2f);
        Handles.matrix = Matrix4x4.identity;
    }

    // -------------------------------------------------------------------------
    // Linear features — editable spline path
    // -------------------------------------------------------------------------

    static void DrawPathHandles(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        FeaturePath path = spawner.Path;
        if (path == null) return;

        // Auto-seed an empty path so a freshly switched linear feature is editable straight away.
        if (path.Count < 2)
        {
            Undo.RecordObject(spawner, "Init Terrain Feature Path");
            path.ResetToBoxDiagonal(spawner.BoxHalfExtents);
            EditorUtility.SetDirty(spawner);
        }

        // Per-control-point position handles.
        for (int i = 0; i < path.controlPoints.Count; i++)
        {
            Vector3 world = t.TransformPoint(path.controlPoints[i]);
            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(1f, 0.7f, 0.2f, 1f);
            Vector3 moved = Handles.PositionHandle(world, t.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(spawner, "Move Terrain Feature Path Point");
                path.controlPoints[i] = t.InverseTransformPoint(moved);
                EditorUtility.SetDirty(spawner);
            }

            // Insert / remove buttons floating beside each point.
            Handles.BeginGUI();
            Vector2 gui = HandleUtility.WorldToGUIPoint(world);
            if (GUI.Button(new Rect(gui.x + 12f, gui.y - 10f, 22f, 20f), "+"))
            {
                Undo.RecordObject(spawner, "Add Terrain Feature Path Point");
                Vector3 next = i + 1 < path.controlPoints.Count
                    ? (path.controlPoints[i] + path.controlPoints[i + 1]) * 0.5f
                    : path.controlPoints[i] + Vector3.right * 10f;
                path.controlPoints.Insert(i + 1, next);
                EditorUtility.SetDirty(spawner);
            }
            if (path.controlPoints.Count > 2 &&
                GUI.Button(new Rect(gui.x + 12f, gui.y + 12f, 22f, 20f), "-"))
            {
                Undo.RecordObject(spawner, "Remove Terrain Feature Path Point");
                path.controlPoints.RemoveAt(i);
                EditorUtility.SetDirty(spawner);
            }
            Handles.EndGUI();
        }

        // Smooth curve preview between the editable points.
        if (path.IsValid)
        {
            var spline = new FeatureSpline(path);
            Handles.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            Vector3 prev = t.TransformPoint(spline.Evaluate(0f));
            for (int i = 1; i <= 48; i++)
            {
                Vector3 cur = t.TransformPoint(spline.Evaluate(i / 48f));
                Handles.DrawLine(prev, cur);
                prev = cur;
            }
        }

        // Half-width handle at the path midpoint.
        if (path.IsValid)
        {
            var spline = new FeatureSpline(path);
            Vector3 mid = t.TransformPoint(spline.Evaluate(0.5f));
            Vector3 side = t.TransformDirection(Vector3.Cross(spline.Tangent(0.5f), Vector3.up).normalized);
            Vector3 widthWorld = mid + side * path.halfWidth;

            EditorGUI.BeginChangeCheck();
            Handles.color = Color.yellow;
            float hs = HandleUtility.GetHandleSize(widthWorld) * 0.16f;
            Vector3 dragged = Handles.Slider(widthWorld, side, hs, Handles.SphereHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(spawner, "Resize Terrain Feature Path Width");
                path.halfWidth = Mathf.Max(1f, Vector3.Distance(mid, dragged));
                EditorUtility.SetDirty(spawner);
            }
        }
    }
}
