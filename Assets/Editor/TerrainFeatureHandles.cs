using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view interactive handles for a <see cref="TerrainFeatureSpawner"/>'s footprint. Split out
/// of <c>TerrainFeatureSpawnerEditor</c> to keep both files small.
///
/// Three handle sets, picked by the spawner's feature type + footprint mode:
///
///   • AREA / Polygon mode — the closed outline is hand-authored. Each vertex is a draggable dot;
///     a "✕" button beside it deletes it; clicking anywhere on an edge inserts a new vertex at the
///     click point. A green arrow sets the feature Height.
///   • AREA / Noise mode — the outline is generated. The designer drags the Width / Breadth box
///     faces and the Height arrow; the outline regenerates live and is drawn read-only.
///   • LINEAR features — an editable Catmull-Rom path: a handle per control point, "+"/"-" buttons,
///     and a half-width slider.
///
/// All edits go through <see cref="Undo"/> and mark the spawner dirty so the scene saves correctly.
/// </summary>
public static class TerrainFeatureHandles
{
    /// <summary>Draws and processes the footprint handles. Call from the editor's OnSceneGUI.
    /// Returns true when the designer changed the footprint this frame, so the caller can
    /// regenerate the in-scene mesh immediately.</summary>
    public static bool Draw(TerrainFeatureSpawner spawner)
    {
        if (spawner == null) return false;
        return spawner.UsesPath ? DrawPathHandles(spawner) : DrawAreaHandles(spawner);
    }

    // =========================================================================
    // Area features
    // =========================================================================

    static bool DrawAreaHandles(TerrainFeatureSpawner spawner)
    {
        FeatureFootprint area = spawner.Area;
        if (area == null) return false;

        return area.mode == FootprintMode.Noise
            ? DrawNoiseHandles(spawner, area)
            : DrawPolygonHandles(spawner, area);
    }

    // -------------------------------------------------------------------------
    // Polygon mode — hand-edited closed outline
    // -------------------------------------------------------------------------

    static bool DrawPolygonHandles(TerrainFeatureSpawner spawner, FeatureFootprint area)
    {
        Transform t = spawner.transform;
        FeaturePolygon poly = area.polygon;
        bool changed = false;

        // Seed an empty polygon from the box so a freshly switched feature is editable at once.
        if (poly == null || !poly.IsValid)
        {
            Undo.RecordObject(spawner, "Init Terrain Feature Polygon");
            area.Refresh(spawner.Seed);
            poly = area.polygon;
            EditorUtility.SetDirty(spawner);
            changed = true;
        }

        // Outline first, so the vertex dots sit visually on top of it.
        DrawPolygonOutline(spawner, poly, new Color(0.35f, 0.8f, 1f, 1f));

        // Click-on-edge insertion: a faint marker tracks the nearest edge point under the mouse,
        // and a left-click there inserts a vertex. This is the primary "add a vertex" gesture.
        changed |= HandleEdgeClickInsert(spawner, poly);

        // Per-vertex drag handles + delete buttons. Drawn z-test Always so the dots stay visible
        // even when the generated rock mesh would otherwise cover them.
        var prevZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

        for (int i = 0; i < poly.vertices.Count; i++)
        {
            Vector3 world = t.TransformPoint(new Vector3(poly.vertices[i].x, 0f, poly.vertices[i].z));
            float hs = Mathf.Max(0.6f, HandleUtility.GetHandleSize(world) * 0.16f);

            EditorGUI.BeginChangeCheck();
            Handles.color = new Color(0.4f, 0.85f, 1f, 1f);
            Vector3 moved = Handles.FreeMoveHandle(world, hs, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(spawner, "Move Terrain Feature Vertex");
                poly.SetVertex(i, t.InverseTransformPoint(moved));
                EditorUtility.SetDirty(spawner);
                changed = true;
            }

            // Delete button beside the vertex (only while above the 3-vertex minimum).
            if (poly.vertices.Count > 3)
            {
                Handles.BeginGUI();
                Vector2 gui = HandleUtility.WorldToGUIPoint(world);
                var btn = new Rect(gui.x + 12f, gui.y - 11f, 22f, 22f);
                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUI.Button(btn, new GUIContent("✕", "Delete this vertex")))
                {
                    Undo.RecordObject(spawner, "Delete Terrain Feature Vertex");
                    poly.RemoveVertex(i);
                    EditorUtility.SetDirty(spawner);
                    changed = true;
                }
                GUI.backgroundColor = prevColor;
                Handles.EndGUI();
            }
        }
        Handles.zTest = prevZ;

        if (DrawHeightHandle(spawner)) changed = true;
        return changed;
    }

    /// <summary>
    /// Tracks the closest point on the polygon outline to the mouse ray and, on a plain left-click
    /// there, inserts a new vertex at that point. Draws a small green preview dot so the designer
    /// sees where the click will land. Returns true when a vertex was inserted.
    /// </summary>
    static bool HandleEdgeClickInsert(TerrainFeatureSpawner spawner, FeaturePolygon poly)
    {
        if (poly == null || !poly.IsValid) return false;
        Transform t = spawner.transform;
        Event e = Event.current;

        // Project the mouse ray onto the feature's local XZ plane (y = 0 in local space).
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Plane plane = new Plane(t.up, t.position);
        if (!plane.Raycast(ray, out float enter)) return false;
        Vector3 worldHit = ray.GetPoint(enter);
        Vector3 localHit = t.InverseTransformPoint(worldHit);

        // Nearest point on the outline to that hit.
        int edge = poly.ClosestEdge(localHit.x, localHit.z, out Vector3 localOnEdge);
        if (edge < 0) return false;
        Vector3 worldOnEdge = t.TransformPoint(new Vector3(localOnEdge.x, 0f, localOnEdge.z));

        // Only offer insertion when the mouse is genuinely near the outline (in screen pixels),
        // so it never competes with vertex dragging or fights the scene-navigation controls.
        Vector2 guiOnEdge = HandleUtility.WorldToGUIPoint(worldOnEdge);
        if (Vector2.Distance(guiOnEdge, e.mousePosition) > 18f) return false;

        // Preview dot.
        var prevZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = new Color(0.4f, 1f, 0.5f, 0.95f);
        float dotSize = HandleUtility.GetHandleSize(worldOnEdge) * 0.1f;
        Handles.DrawSolidDisc(worldOnEdge, t.up, dotSize);
        Handles.zTest = prevZ;
        SceneView.RepaintAll();

        // Plain left-click (no modifier) on the outline → insert.
        if (e.type == EventType.MouseDown && e.button == 0 && e.modifiers == EventModifiers.None)
        {
            Undo.RecordObject(spawner, "Insert Terrain Feature Vertex");
            poly.InsertOnNearestEdge(new Vector3(localOnEdge.x, 0f, localOnEdge.z));
            EditorUtility.SetDirty(spawner);
            e.Use();
            return true;
        }
        return false;
    }

    // -------------------------------------------------------------------------
    // Noise mode — generated outline, box-driven
    // -------------------------------------------------------------------------

    static bool DrawNoiseHandles(TerrainFeatureSpawner spawner, FeatureFootprint area)
    {
        // Keep the outline current with the knobs, then draw it read-only.
        area.Refresh(spawner.Seed);
        DrawPolygonOutline(spawner, area.polygon, new Color(0.55f, 0.95f, 0.7f, 0.9f));

        bool boxChanged = DrawBoxSizeHandles(spawner);
        bool heightChanged = DrawHeightHandle(spawner);

        // Resizing the box re-derives the noise outline, so the mesh must rebuild too.
        if (boxChanged) area.Refresh(spawner.Seed);
        return boxChanged || heightChanged;
    }

    // -------------------------------------------------------------------------
    // Shared area handles
    // -------------------------------------------------------------------------

    /// <summary>Draws the closed outline as a thick anti-aliased loop, rendered THROUGH the
    /// generated mesh (z-test disabled) so the footprint is always visible while editing.</summary>
    static void DrawPolygonOutline(TerrainFeatureSpawner spawner, FeaturePolygon poly, Color color)
    {
        if (poly == null || !poly.IsValid) return;
        Transform t = spawner.transform;

        int n = poly.vertices.Count;
        var loop = new Vector3[n + 1];
        for (int i = 0; i < n; i++)
            loop[i] = t.TransformPoint(new Vector3(poly.vertices[i].x, 0f, poly.vertices[i].z));
        loop[n] = loop[0];

        var prevZ = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = color;
        Handles.DrawAAPolyLine(5f, loop);
        Handles.zTest = prevZ;
    }

    /// <summary>Green up-arrow handle dragging the feature Height (the meshing band height).
    /// Returns true when dragged this frame.</summary>
    static bool DrawHeightHandle(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        FeatureFootprint area = spawner.Area;
        Vector3 topWorld = t.TransformPoint(new Vector3(0f, area.height * 0.5f, 0f));

        EditorGUI.BeginChangeCheck();
        Handles.color = new Color(0.6f, 1f, 0.6f, 1f);
        float ths = HandleUtility.GetHandleSize(topWorld) * 0.15f;
        Vector3 dragged = Handles.Slider(topWorld, t.up, ths, Handles.ArrowHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(spawner, "Resize Terrain Feature Height");
            float halfY = Mathf.Max(1f, Vector3.Dot(t.InverseTransformPoint(dragged), Vector3.up));
            area.height = halfY * 2f;
            EditorUtility.SetDirty(spawner);
            return true;
        }
        return false;
    }

    /// <summary>+X (Width) / +Z (Breadth) face handles that resize the footprint box. Returns true
    /// when a face was dragged this frame.</summary>
    static bool DrawBoxSizeHandles(TerrainFeatureSpawner spawner)
    {
        Transform t = spawner.transform;
        FeatureFootprint area = spawner.Area;
        Vector3 half = area.BoxHalfExtents;
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
                float v = Mathf.Max(1f, Vector3.Dot(t.InverseTransformPoint(dragged), axes[a]));
                newHalf[axes[a].x > 0f ? 0 : 2] = v;
                changed = true;
            }
        }
        if (changed)
        {
            Undo.RecordObject(spawner, "Resize Terrain Feature Box");
            area.width = newHalf.x * 2f;
            area.breadth = newHalf.z * 2f;
            EditorUtility.SetDirty(spawner);
        }
        return changed;
    }

    // =========================================================================
    // Linear features — editable spline path
    // =========================================================================

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
