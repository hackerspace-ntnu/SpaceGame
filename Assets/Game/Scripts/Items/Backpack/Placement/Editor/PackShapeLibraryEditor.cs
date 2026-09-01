using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The clickable grid. Draws each item's mask as a board of squares you paint by dragging
    /// across it, instead of a <c>bool[]</c> of forty-eight elements in the default inspector.
    ///
    /// <para>
    /// That is the whole reason this file exists. A mask is a picture of a shape, and the default
    /// array drawer shows it as a column of checkboxes labelled "Element 23" — which is not
    /// something a person can read a shape out of, let alone draw one into. The grid is also the
    /// only place the two numbers that must agree can be shown together: the cells an item claims,
    /// and the metres it actually measures.
    /// </para>
    /// <para>
    /// Everything is written through <see cref="SerializedObject"/> so undo works and the asset is
    /// marked dirty the way Unity expects. Editing the arrays directly would look identical until
    /// the first time somebody closed the editor without saving.
    /// </para>
    /// </summary>
    [CustomEditor(typeof(PackShapeLibrary))]
    public sealed class PackShapeLibraryEditor : Editor
    {
        /// <summary>Pixels per cell. Big enough to hit reliably with a drag.</summary>
        private const float CellPixels = 22f;

        private const float CellGap = 2f;

        private static readonly Color FilledColour = new(0.98f, 0.78f, 0.35f, 1f);
        private static readonly Color EmptyColour = new(0.22f, 0.22f, 0.24f, 1f);
        private static readonly Color GridColour = new(0.35f, 0.36f, 0.40f, 1f);

        private readonly HashSet<int> expanded = new();

        /// <summary>
        /// Which value a drag is painting. A drag has to commit to one — set or clear — on the cell
        /// it started on, or dragging across a mixed row flips every cell it touches twice.
        /// </summary>
        private bool paintValue;

        private bool painting;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty entries = serializedObject.FindProperty("entries");

            EditorGUILayout.HelpBox(
                $"Cells are {PackGrid.Cell * 100f:F0} mm — one rung of the rig's webbing ladder. " +
                "An item with no row here gets the smallest solid block its true size fits in, so " +
                "only draw the ones that are not rectangles.",
                MessageType.None);

            EditorGUILayout.Space();

            for (int i = 0; i < entries.arraySize; i++)
            {
                if (DrawEntry(entries, i)) { i--; continue; }

                EditorGUILayout.Space(4f);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Add item"))
            {
                entries.InsertArrayElementAtIndex(entries.arraySize);

                SerializedProperty added = entries.GetArrayElementAtIndex(entries.arraySize - 1);

                added.FindPropertyRelative("item").objectReferenceValue = null;
                added.FindPropertyRelative("width").intValue = 2;
                added.FindPropertyRelative("height").intValue = 2;
                added.FindPropertyRelative("allowRotation").boolValue = true;

                Resize(added, 2, 2, 2, 2);
            }

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed && target is PackShapeLibrary library) library.Invalidate();
        }

        /// <summary>One row. Returns true when it removed itself, so the caller can re-index.</summary>
        private bool DrawEntry(SerializedProperty entries, int index)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);

            SerializedProperty itemProp = entry.FindPropertyRelative("item");
            SerializedProperty widthProp = entry.FindPropertyRelative("width");
            SerializedProperty heightProp = entry.FindPropertyRelative("height");
            SerializedProperty cellsProp = entry.FindPropertyRelative("cells");
            SerializedProperty rotateProp = entry.FindPropertyRelative("allowRotation");

            var item = itemProp.objectReferenceValue as InventoryItem;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            bool open = expanded.Contains(index);
            bool nowOpen = EditorGUILayout.Foldout(
                open, item != null ? item.itemName : "(no item)", true);

            if (nowOpen != open)
            {
                if (nowOpen) expanded.Add(index);
                else expanded.Remove(index);
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label($"{widthProp.intValue} x {heightProp.intValue}", EditorStyles.miniLabel);

            if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(64f)))
            {
                entries.DeleteArrayElementAtIndex(index);
                expanded.Clear();

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return true;
            }

            EditorGUILayout.EndHorizontal();

            if (!nowOpen)
            {
                EditorGUILayout.EndVertical();
                return false;
            }

            EditorGUILayout.PropertyField(itemProp);
            EditorGUILayout.PropertyField(rotateProp);

            int oldWidth = widthProp.intValue;
            int oldHeight = heightProp.intValue;

            EditorGUILayout.BeginHorizontal();

            int width = Mathf.Clamp(EditorGUILayout.IntField("Cells across", oldWidth), 1, 24);
            int height = Mathf.Clamp(EditorGUILayout.IntField("up", oldHeight), 1, 24);

            EditorGUILayout.EndHorizontal();

            if (width != oldWidth || height != oldHeight)
            {
                widthProp.intValue = width;
                heightProp.intValue = height;

                // Resized around the (0,0) corner, keeping whatever cells still exist. Rebuilding
                // it blank would throw away a shape every time somebody nudged a number.
                Resize(entry, oldWidth, oldHeight, width, height);
            }

            EnsureLength(cellsProp, width * height);

            DrawGrid(cellsProp, width, height);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Fill", EditorStyles.miniButton)) SetAll(cellsProp, true);
            if (GUILayout.Button("Clear", EditorStyles.miniButton)) SetAll(cellsProp, false);
            if (GUILayout.Button("Invert", EditorStyles.miniButton)) Invert(cellsProp);

            if (GUILayout.Button("From item size", EditorStyles.miniButton) && item != null)
            {
                PackShape derived = PackShape.ForFootprint(ItemFootprint.FootprintOf(item));

                widthProp.intValue = derived.Width;
                heightProp.intValue = derived.Height;

                EnsureLength(cellsProp, derived.Width * derived.Height);
                SetAll(cellsProp, true);
            }

            EditorGUILayout.EndHorizontal();

            DrawMeasurement(item, width, height);

            EditorGUILayout.EndVertical();
            return false;
        }

        /// <summary>
        /// The one check the grid cannot make by eye: is the block big enough for the item that
        /// actually gets drawn on it?
        ///
        /// <para>
        /// Items are rendered at true size, so a mask smaller than the item does not shrink
        /// anything — it makes the item overhang the cells the layout reserved and lie through
        /// whatever is next to it. Reported here as well as at runtime, because the editor is where
        /// it can still be fixed.
        /// </para>
        /// </summary>
        private static void DrawMeasurement(InventoryItem item, int width, int height)
        {
            if (item == null) return;

            Vector2 footprint = ItemFootprint.FootprintOf(item);
            var block = new Vector2(width * PackGrid.Cell, height * PackGrid.Cell);

            const float slack = 1e-3f;
            bool tight = footprint.x > block.x + slack || footprint.y > block.y + slack;

            string text = $"Item measures {footprint.x:F3} x {footprint.y:F3} m; " +
                          $"{width} x {height} cells is {block.x:F3} x {block.y:F3} m.";

            EditorGUILayout.HelpBox(
                tight
                    ? text + " The item is bigger than its shape and will overhang it."
                    : text,
                tight ? MessageType.Warning : MessageType.None);
        }

        // ── The board ────────────────────────────────────────────────────────

        private void DrawGrid(SerializedProperty cells, int width, int height)
        {
            float w = width * (CellPixels + CellGap) + CellGap;
            float h = height * (CellPixels + CellGap) + CellGap;

            Rect area = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));

            EditorGUI.DrawRect(area, GridColour);

            Event e = Event.current;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Row 0 at the BOTTOM, because uv +Y runs up the surface and a mask drawn
                    // upside down relative to the thing it describes is a bug waiting to be
                    // authored.
                    var rect = new Rect(
                        area.x + CellGap + x * (CellPixels + CellGap),
                        area.y + CellGap + (height - 1 - y) * (CellPixels + CellGap),
                        CellPixels, CellPixels);

                    int index = y * width + x;
                    if (index >= cells.arraySize) continue;

                    SerializedProperty cell = cells.GetArrayElementAtIndex(index);

                    EditorGUI.DrawRect(rect, cell.boolValue ? FilledColour : EmptyColour);

                    if (!rect.Contains(e.mousePosition)) continue;

                    if (e.type == EventType.MouseDown && e.button == 0)
                    {
                        // The value the whole drag paints, decided by the cell it began on.
                        paintValue = !cell.boolValue;
                        painting = true;
                        cell.boolValue = paintValue;

                        e.Use();
                        GUI.changed = true;
                    }
                    else if (e.type == EventType.MouseDrag && painting)
                    {
                        cell.boolValue = paintValue;

                        e.Use();
                        GUI.changed = true;
                    }
                }
            }

            if (e.type == EventType.MouseUp) painting = false;
        }

        // ── Array plumbing ───────────────────────────────────────────────────

        private static void EnsureLength(SerializedProperty cells, int length)
        {
            if (cells.arraySize == length) return;

            int was = cells.arraySize;

            cells.arraySize = length;

            // New cells arrive filled. A grid that grew and came back with a blank column reads as
            // the resize having eaten the shape.
            for (int i = was; i < length; i++)
                cells.GetArrayElementAtIndex(i).boolValue = true;
        }

        /// <summary>Re-lay a mask onto a differently sized board, anchored at (0,0).</summary>
        private static void Resize(SerializedProperty entry, int oldWidth, int oldHeight,
                                   int width, int height)
        {
            SerializedProperty cells = entry.FindPropertyRelative("cells");

            var kept = new bool[width * height];

            for (int i = 0; i < kept.Length; i++) kept[i] = true;

            for (int y = 0; y < Mathf.Min(oldHeight, height); y++)
            {
                for (int x = 0; x < Mathf.Min(oldWidth, width); x++)
                {
                    int from = y * oldWidth + x;

                    if (from >= cells.arraySize) continue;

                    kept[y * width + x] = cells.GetArrayElementAtIndex(from).boolValue;
                }
            }

            cells.arraySize = kept.Length;

            for (int i = 0; i < kept.Length; i++)
                cells.GetArrayElementAtIndex(i).boolValue = kept[i];
        }

        private static void SetAll(SerializedProperty cells, bool value)
        {
            for (int i = 0; i < cells.arraySize; i++)
                cells.GetArrayElementAtIndex(i).boolValue = value;

            GUI.changed = true;
        }

        private static void Invert(SerializedProperty cells)
        {
            for (int i = 0; i < cells.arraySize; i++)
            {
                SerializedProperty cell = cells.GetArrayElementAtIndex(i);
                cell.boolValue = !cell.boolValue;
            }

            GUI.changed = true;
        }
    }
}
