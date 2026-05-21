using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view interactive handles for a <see cref="TerrainFeatureSpawner"/>'s footprint. Split out
/// of <c>TerrainFeatureSpawnerEditor</c> to keep both files small.
///
/// Two footprint modes, switched by the spawner's chosen feature type:
///   • AREA features  — an editable closed polygon: a movable handle per vertex, plus "+"/"-"
///     buttons to add/remove vertices. The polygon is the feature's organic outline. A height
///     handle still controls the box Y (the meshing band's vertical extent).
///   • LINEAR features — an editable Catmull-Rom path: a movable handle per control point, plus
///     "+"/"-" buttons to add/remove points, plus a half-width slider handle.
///
/// All edits go through <see cref="Undo"/> and mark the spawner dirty so the scene saves correctly.
/// </summary>
public static class TerrainFeatureHandles
{
    /// <summary>Draws and processes the footprint handles. Call from the editor's OnSceneGUI.
    /// Returns true when the designer changed the footprint this frame, so the caller can
    /// regenerate the in-scene mesh and the terrain reflects the new polygon / path immediately.</summary>
    public static bool Draw(TerrainFeatureSpawner spawner)
    {
        if (spawner == null) return false;
        return spawner.UsesPath ? DrawPathHandles(spawner) : DrawPolygonHandles(spawner);
    }

    // -------------------------------------------------------------------------
    // Area features — editable closed polygon
    // -------------------------------------------------------------------------

    /// <summary>Returns true when a polygon vertex was moved, added or removed this frame.</summary>
    static bool DrawPolygonHandles(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        FeaturePolygon poly = spawner.Footprint;
        if (poly == null) return false;
        bool changed = false;

        // Noise mode: the outline is generated, not hand-edited. Keep it fresh, draw it as a
        // read-only outline, and still offer the height handle + the box-size handles.
        if (spawner.FootprintIsGenerated)
        {
            spawner.RefreshGeneratedFootprint();
            DrawPolygonOutline(spawner, poly, new Color(0.55f, 0.95f, 0.7f, 0.9f));
            bool boxChanged = DrawBoxSizeHandles(spawner);
            bool heightChanged = DrawHeightHandle(spawner);
            // Resizing the box re-derives the noise outline, so the mesh must rebuild too.
            if (boxChanged) spawner.RefreshGeneratedFootprint();
            return boxChanged || heightChanged;
        }

        // Auto-seed an empty polygon from the box so a freshly switched area feature is editable.
        if (!poly.IsValid)
        {
            Undo.RecordObject(spawner, "Init Terrain Feature Polygon");
            poly.ResetToBox(spawner.BoxHalfExtents);
            EditorUtility.SetDirty(spawner);
        }

        // Per-vertex position handles (constrained to the local XZ plane).
        for (int i = 0; i < poly.vertices.Count; i++)
        {
            Vector3 world = t.TransformPoint(new Vector3(poly.vertices[i].x, 0f, poly.vertices[i].z));
            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(0.5f, 0.85f, 1f, 1f);
            float hs = HandleUtility.GetHandleSize(world) * 0.12f;
            Vector3 moved = Handles.FreeMoveHandle(world, hs, Vector3.zero, Handles.DotHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(spawner, "Move Terrain Feature Polygon Vertex");
                Vector3 local = t.InverseTransformPoint(moved);
                poly.vertices[i] = new Vector3(local.x, 0f, local.z);   // keep on the XZ plane
                EditorUtility.SetDirty(spawner);
                changed = true;
            }

            // Insert / remove buttons floating beside each vertex.
            Handles.BeginGUI();
            Vector2 gui = HandleUtility.WorldToGUIPoint(world);
            if (GUI.Button(new Rect(gui.x + 12f, gui.y - 10f, 22f, 20f), "+"))
            {
                Undo.RecordObject(spawner, "Add Terrain Feature Polygon Vertex");
                int next = (i + 1) % poly.vertices.Count;
                poly.vertices.Insert(i + 1, (poly.vertices[i] + poly.vertices[next]) * 0.5f);
                EditorUtility.SetDirty(spawner);
                changed = true;
            }
            if (poly.vertices.Count > 3 &&
                GUI.Button(new Rect(gui.x + 12f, gui.y + 12f, 22f, 20f), "-"))
            {
                Undo.RecordObject(spawner, "Remove Terrain Feature Polygon Vertex");
                poly.vertices.RemoveAt(i);
                EditorUtility.SetDirty(spawner);
                changed = true;
            }
            Handles.EndGUI();
        }

        DrawPolygonOutline(spawner, poly, new Color(0.5f, 0.85f, 1f, 0.9f));
        if (DrawHeightHandle(spawner)) changed = true;
        return changed;
    }

    /// <summary>Draws the closed polygon outline as a coloured loop (no interactive handles).</summary>
    static void DrawPolygonOutline(TerrainFeatureSpawner spawner, FeaturePolygon poly, Color color)
    {
        if (poly == null || !poly.IsValid) return;
        Transform t = spawner.transform;
        Handles.color = color;
        for (int i = 0; i < poly.vertices.Count; i++)
        {
            int j = (i + 1) % poly.vertices.Count;
            Handles.DrawLine(
                t.TransformPoint(new Vector3(poly.vertices[i].x, 0f, poly.vertices[i].z)),
                t.TransformPoint(new Vector3(poly.vertices[j].x, 0f, poly.vertices[j].z)));
        }
    }

    /// <summary>Green up-arrow handle that drags the box Y half-extent (the meshing band height).
    /// Returns true when dragged this frame.</summary>
    static bool DrawHeightHandle(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        Vector3 topWorld = t.TransformPoint(new Vector3(0f, spawner.BoxHalfExtents.y, 0f));
        EditorGUI.BeginChangeCheck();
        Handles.color = new Color(0.6f, 1f, 0.6f, 1f);
        float ths = HandleUtility.GetHandleSize(topWorld) * 0.15f;
        Vector3 dragged = Handles.Slider(topWorld, t.up, ths, Handles.ArrowHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(spawner, "Resize Terrain Feature Height");
            float y = Mathf.Max(2f, Vector3.Dot(t.InverseTransformPoint(dragged), Vector3.up));
            Vector3 h = spawner.BoxHalfExtents; h.y = y;
            spawner.BoxHalfExtents = h;
            EditorUtility.SetDirty(spawner);
            return true;
        }
        return false;
    }

    /// <summary>+X / +Z face handles that resize the bounding box — used in Noise mode, where the
    /// outline is generated from the box rather than hand-edited vertex by vertex. Returns true
    /// when a face was dragged this frame.</summary>
    static bool DrawBoxSizeHandles(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        Vector3 half = spawner.BoxHalfExtents;
        Vector3[] axes = { Vector3.right, Vector3.forward };
        Vector3 newHalf = half;
        bool changed = false;

        for (int a = 0; a < axes.Length; a++)
        {
            Vector3 worldFace = t.TransformPoint(Vector3.Scale(axes[a], half));
            float size = HandleUtility.GetHandleSize(worldFace) * 0.16f;
            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(0.55f, 0.95f, 0.7f, 1f);
            Vector3 dragged = Handles.Slider(worldFace, t.TransformDirection(axes[a]),
                size, Handles.CubeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                float v = Mathf.Max(2f, Vector3.Dot(t.InverseTransformPoint(dragged), axes[a]));
                newHalf[axes[a].x > 0f ? 0 : 2] = v;
                changed = true;
            }
        }
        if (changed)
        {
            Undo.RecordObject(spawner, "Resize Terrain Feature Box");
            spawner.BoxHalfExtents = newHalf;
            EditorUtility.SetDirty(spawner);
        }
        return changed;
    }

    // -------------------------------------------------------------------------
    // Linear features — editable spline path
    // -------------------------------------------------------------------------

    /// <summary>Returns true when a path point or width was changed this frame.</summary>
    static bool DrawPathHandles(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        FeaturePath path = spawner.Path;
        if (path == null) return false;
        bool changed = false;

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
                changed = true;
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
                changed = true;
            }
            if (path.controlPoints.Count > 2 &&
                GUI.Button(new Rect(gui.x + 12f, gui.y + 12f, 22f, 20f), "-"))
            {
                Undo.RecordObject(spawner, "Remove Terrain Feature Path Point");
                path.controlPoints.RemoveAt(i);
                EditorUtility.SetDirty(spawner);
                changed = true;
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
                changed = true;
            }
        }
        return changed;
    }
}
