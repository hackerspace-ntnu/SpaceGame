using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Asks the whole item roster the one question <see cref="ItemPackOrientation"/> answers four
    /// prefabs at a time: <b>does this thing lie down on the mat the way it would lie down on a
    /// shelf?</b>
    ///
    /// <para>
    /// <b>Why an audit and not a fix.</b> A stowed item keeps its own up —
    /// <c>ItemFootprint.FootprintOf</c> is DEFINED as <c>(size.x, size.z)</c> — so "this item is
    /// standing on its end" is authored data, and the correction is a rotation of the prefab's
    /// contents. Which rotation, though, is a judgement no measurement can make. Putting the
    /// SMALLEST axis up maximises the face the item rests on, which is right for a slab, a pole or
    /// a board; it is wrong for anything with a grip, a sight or a base, because a rifle set down
    /// on a table rests on its magazine and not on its side, and a canister rests on its foot.
    /// And even where the axis is settled, the SIGN is not: turning a gun +90 about X and -90 about
    /// X both lay it down, and one of them lays it down upside down. That is a decision made by
    /// looking at the thing, which is exactly what <see cref="ItemPackOrientation"/>'s own header
    /// says about the pose in the hand.
    /// </para>
    /// <para>
    /// So this prints the evidence and names the candidate, and a human or an agent with the Editor
    /// open turns each one into an <see cref="ItemPackOrientation"/> entry — or into a rotation in
    /// the owning builder script, for the two thirds of the roster whose prefabs are rebuilt
    /// wholesale and would swallow a prefab edit without a word.
    /// </para>
    /// <para>
    /// <b>The cost column is the point.</b> Laying a long item down turns its footprint from its
    /// cross-section into its LENGTH, and the rig only holds 255 cells. A pole that occupied 10 of
    /// them standing occupies 70 lying down. That is what a real pole does on a real pack and it may
    /// well be the right call — but it is a capacity decision as much as a readability one
    /// (<c>GDC-L1-UX-0003</c> against <c>GDC-L1-SYS-0008</c>), and it should be made with the
    /// number in front of you rather than one item at a time.
    /// </para>
    /// </summary>
    public static class PackOrientationAudit
    {
        private const string RigPrefab = "Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab";

        /// <summary>An item is "square enough" that which of two axes is up does not read.</summary>
        private const float Indistinguishable = 0.08f;

        [MenuItem("Tools/SpaceGame/Items/Audit Pack Orientation (whole roster)")]
        public static void Audit()
        {
            // Sizes are cached per prefab for the life of a session, and packSize, the scale ladder
            // and the FBXs all move underneath them.
            ItemFootprint.ClearCache();

            InventoryItem[] items = Resources.LoadAll<InventoryItem>("Items")
                .Where(i => i != null)
                .OrderBy(i => i.itemName)
                .ToArray();

            var log = new StringBuilder("Pack orientation, whole roster\n")
                .Append("  cell ").Append(PackGrid.Cell.ToString("F3"))
                .Append(" m, items drawn at PackScale.Factor ")
                .Append(PackScale.Factor.ToString("F2")).Append("\n\n");

            List<PackSurface> faces = Faces(log);

            int standing = 0;
            int onEdge = 0;

            foreach (InventoryItem item in items)
            {
                if (item.itemPrefab == null)
                {
                    log.Append("  ").Append(item.itemName).Append("\n    NO PREFAB\n");
                    continue;
                }

                Vector3 size = ItemFootprint.SizeOf(item.itemPrefab);
                int longest = ItemFootprint.MaxAxis(size);
                int smallest = MinAxis(size);

                bool flat = smallest == 1;
                bool standsOnEnd = longest == 1;

                if (standsOnEnd) standing++;
                else if (!flat) onEdge++;

                log.Append("  ").Append(item.itemName)
                   .Append("\n    size     ").Append(size.ToString("F3"))
                   .Append("  (x, y, z)  ")
                   .Append(flat ? "lies flat"
                                : standsOnEnd ? "STANDS ON END — its LONGEST axis is up"
                                              : "on edge — a bigger axis than y is horizontal")
                   .Append('\n');

                Append(log, "    now      ", size, faces);

                if (flat) continue;

                // The candidate: the turn that brings the smallest axis up. Named as an axis, not a
                // signed rotation, because the sign is the part a measurement cannot settle.
                Vector3 turned = WithSmallestUp(size, smallest);

                log.Append("    turn     about ").Append(smallest == 0 ? 'Z' : 'X')
                   .Append(" (either sign lays it down; only one is right way up)\n");

                Append(log, "    would    ", turned, faces);

                float second = Second(size);
                if (Mathf.Abs(size.y - second) <= Indistinguishable * Mathf.Max(size.y, second))
                    log.Append("    NOTE     y and the axis beside it are within ")
                       .Append((Indistinguishable * 100f).ToString("F0"))
                       .Append("% — turning this one changes the footprint and almost nothing a "
                               + "player can see. Weigh the cells against that.\n");
            }

            log.Append("\n  ").Append(items.Length).Append(" items, ").Append(standing)
               .Append(" standing on end, ").Append(onEdge).Append(" on edge.\n")
               .Append("  Fix a hand-authored prefab in ItemPackOrientation; fix a builder-owned "
                       + "one in its builder, or the next run undoes it.\n");

            Debug.Log(log.ToString());
        }

        /// <summary>
        /// The rig's live faces, read off the built prefab rather than off a table, so this reports
        /// against the pack that actually exists.
        /// </summary>
        private static List<PackSurface> Faces(StringBuilder log)
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab);

            if (rig == null)
            {
                log.Append("  NO RIG   ").Append(RigPrefab)
                   .Append(" — the 'fits' columns are omitted. Run the rig builder.\n\n");
                return new List<PackSurface>();
            }

            List<PackSurface> faces = rig.GetComponentsInChildren<PackSurface>(true)
                                         .Where(s => s != null).ToList();

            log.Append("  faces    ")
               .Append(string.Join(", ", faces.Select(
                   s => $"{s.Id} {PackGrid.CellsOn(s.Size).x}x{PackGrid.CellsOn(s.Size).y}")))
               .Append("\n\n");

            return faces;
        }

        /// <summary>One line: the footprint in cells, and every face that would take it.</summary>
        private static void Append(StringBuilder log, string label, Vector3 size,
                                   IReadOnlyList<PackSurface> faces)
        {
            PackShape shape = PackShape.ForFootprint(ItemFootprint.FootprintOf(size));

            log.Append(label).Append(shape.Width).Append(" x ").Append(shape.Height)
               .Append(" cells = ").Append(shape.Width * shape.Height);

            if (faces.Count > 0)
            {
                string[] fits = faces
                    .Where(f => Accepts(f, shape))
                    .Select(f => f.Id.ToString())
                    .ToArray();

                log.Append("   fits: ")
                   .Append(fits.Length > 0 ? string.Join(", ", fits) : "NOTHING ON THE RIG");
            }

            log.Append('\n');
        }

        /// <summary>
        /// Would this face take this shape anywhere, at either orientation? Asked through
        /// <see cref="PackOverhang"/>, because the rack takes a shape longer than itself
        /// ski-fashion and the back panels take one on both axes — a bare rectangle test would
        /// report the rack refusing exactly the gear it exists for.
        /// </summary>
        private static bool Accepts(PackSurface face, PackShape shape)
        {
            for (int turns = 0; turns < 2; turns++)
            {
                PackShape oriented = shape.Rotated(turns);
                PackShape clamped = PackOverhang.Clamp(face.Id, face.Size, oriented);
                Vector2Int cells = PackGrid.CellsOn(face.Size);

                if (clamped.Width <= cells.x && clamped.Height <= cells.y) return true;
            }

            return false;
        }

        /// <summary>The axes swapped so the smallest one is up. Sizes only — this names no sign.</summary>
        private static Vector3 WithSmallestUp(Vector3 size, int smallest) => smallest == 0
            ? new Vector3(size.y, size.x, size.z)
            : new Vector3(size.x, size.z, size.y);

        /// <summary>
        /// Index of the smallest component. Ties resolve to the LATER axis, the mirror of
        /// <see cref="ItemFootprint.MaxAxis"/>, so a cube is never reported as needing a turn.
        /// </summary>
        private static int MinAxis(Vector3 v)
        {
            if (v.y <= v.x && v.y <= v.z) return 1;

            return v.x < v.z ? 0 : 2;
        }

        /// <summary>The smaller of the two axes that are not y — what y would be traded against.</summary>
        private static float Second(Vector3 v) => Mathf.Min(v.x, v.z);
    }
}
