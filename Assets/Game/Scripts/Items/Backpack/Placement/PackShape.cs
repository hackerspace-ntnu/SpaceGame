using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Which cells of the pack's grid one item fills — a Tarkov-style footprint mask rather than a
    /// width and a height.
    ///
    /// <para>
    /// The mask is the whole reason the grid was worth building. Two L-shaped items can interlock
    /// into a corner that neither of their bounding boxes would fit in, and a pack that reasoned in
    /// bounding boxes would refuse a placement the player can plainly see is free. Everything else
    /// here — rotation, the derived default, the metre conversions — is in service of that one
    /// test in <see cref="Overlaps"/>.
    /// </para>
    /// <para>
    /// Immutable and cheap to copy. The backing array is shared between rotations of the same
    /// shape only when the rotation is the identity, so nothing can write through one shape into
    /// another. A solid rectangle carries no array at all, which is the common case: fifteen of the
    /// sixteen shipped items have no authored mask and get one from
    /// <see cref="ForFootprint"/>.
    /// </para>
    /// <para>
    /// No UnityEngine dependency beyond <see cref="Vector2"/>/<see cref="Vector2Int"/>, so the
    /// EditMode tests drive it directly.
    /// </para>
    /// </summary>
    public readonly struct PackShape
    {
        /// <summary>Cells across the shape's own local +X, before any yaw.</summary>
        public readonly int Width;

        /// <summary>Cells along the shape's own local +Y, before any yaw.</summary>
        public readonly int Height;

        /// <summary>
        /// Row-major, <c>y * Width + x</c>. <b>Null means a solid rectangle</b> — not an empty
        /// shape. Storing the common case as an absent array keeps the struct allocation-free for
        /// every item nobody has drawn a shape for.
        /// </summary>
        private readonly bool[] cells;

        private PackShape(int width, int height, bool[] cells)
        {
            Width = width;
            Height = height;
            this.cells = cells;
        }

        /// <summary>A shape with no cells at all. <see cref="PackLayout"/> refuses to place one.</summary>
        public static PackShape None => default;

        public bool IsEmpty => Width <= 0 || Height <= 0;

        /// <summary>True when every cell in the bounding block is filled.</summary>
        public bool IsRectangular => cells == null;

        public Vector2Int Size => new(Width, Height);

        /// <summary>The bounding block in metres — what the item is allowed to be as big as.</summary>
        public Vector2 Metres => new(Width * PackGrid.Cell, Height * PackGrid.Cell);

        /// <summary>
        /// Is this cell filled? Out-of-range indices answer false rather than throwing, which is
        /// what lets <see cref="Overlaps"/> probe one shape at another's offsets without a bounds
        /// test of its own.
        /// </summary>
        public bool this[int x, int y] =>
            x >= 0 && y >= 0 && x < Width && y < Height && (cells == null || cells[y * Width + x]);

        /// <summary>How many cells the item actually occupies, holes excluded.</summary>
        public int FilledCells
        {
            get
            {
                if (IsEmpty) return 0;
                if (cells == null) return Width * Height;

                int n = 0;
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i]) n++;

                return n;
            }
        }

        /// <summary>A solid block of cells.</summary>
        public static PackShape Rect(int width, int height)
        {
            width = Mathf.Max(0, width);
            height = Mathf.Max(0, height);

            return width == 0 || height == 0 ? default : new PackShape(width, height, null);
        }

        /// <summary>
        /// A shape from an authored mask, row-major.
        ///
        /// <para>
        /// A mask with nothing switched on is treated as a solid rectangle rather than as an item
        /// that occupies no space. That is a deliberate refusal to honour an authoring slip: a
        /// zero-cell shape would clash with nothing, so every item drawn blank by accident would
        /// stack invisibly on top of everything else on the mat. A short array is padded with
        /// filled cells for the same reason.
        /// </para>
        /// </summary>
        public static PackShape FromMask(int width, int height, IReadOnlyList<bool> mask)
        {
            width = Mathf.Max(0, width);
            height = Mathf.Max(0, height);

            if (width == 0 || height == 0) return default;
            if (mask == null) return Rect(width, height);

            var copy = new bool[width * height];
            bool any = false;
            bool all = true;

            for (int i = 0; i < copy.Length; i++)
            {
                bool on = i >= mask.Count || mask[i];

                copy[i] = on;
                any |= on;
                all &= on;
            }

            if (!any || all) return Rect(width, height);

            return new PackShape(width, height, copy);
        }

        /// <summary>
        /// The shape an item gets when nobody has drawn one: the smallest solid block its true
        /// footprint fits inside.
        ///
        /// <para>
        /// This is what stops the feature needing an authoring pass before it works at all. It also
        /// sets the rule the authored shapes are checked against — a derived block can never be too
        /// small for its item, so <see cref="PackShapes"/> only ever has to warn about an
        /// <em>authored</em> shape the item overflows.
        /// </para>
        /// <para>
        /// The tolerance is a cell rounding, not a fudge: an item measuring exactly two cells
        /// across must come out two cells, and <c>0.18f / 0.09f</c> in float is 2.0000002.
        /// </para>
        /// </summary>
        public static PackShape ForFootprint(Vector2 footprint)
        {
            const float tolerance = 1e-3f;

            int w = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(footprint.x) / PackGrid.Cell - tolerance));
            int h = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(footprint.y) / PackGrid.Cell - tolerance));

            return Rect(w, h);
        }

        /// <summary>
        /// The shape turned <paramref name="quarterTurns"/> right angles, the same sense
        /// <see cref="PackPlacement.Yaw"/> turns in: surface +X toward surface +Y.
        ///
        /// <para>
        /// A rectangle needs no array work — an odd number of turns just swaps the axes. A mask
        /// does, and the mapping is <c>(x, y) -> (Height - 1 - y, x)</c> per turn, which composes
        /// with itself to the 180-degree flip as it must.
        /// </para>
        /// </summary>
        public PackShape Rotated(int quarterTurns)
        {
            int q = ((quarterTurns % 4) + 4) % 4;

            if (q == 0 || IsEmpty) return this;

            if (cells == null) return q == 2 ? this : Rect(Height, Width);

            int nw = q == 2 ? Width : Height;
            int nh = q == 2 ? Height : Width;

            var turned = new bool[nw * nh];

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int tx, ty;

                    switch (q)
                    {
                        case 1: tx = Height - 1 - y; ty = x; break;
                        case 2: tx = Width - 1 - x;  ty = Height - 1 - y; break;
                        default: tx = y;             ty = Width - 1 - x; break;
                    }

                    turned[ty * nw + tx] = cells[y * Width + x];
                }
            }

            return new PackShape(nw, nh, turned);
        }

        /// <summary>
        /// Do two shapes, laid at their own origin cells on the same surface, share a filled cell?
        ///
        /// <para>
        /// This replaces the separating-axis test the free system used. Cells make SAT
        /// unnecessary — there are no oblique angles left to separate — and mask-aware, which SAT
        /// could never be: two rectangles either overlap or they do not, but two L-shapes can share
        /// a bounding box and still both fit.
        /// </para>
        /// </summary>
        public static bool Overlaps(Vector2Int originA, PackShape a, Vector2Int originB, PackShape b)
        {
            if (a.IsEmpty || b.IsEmpty) return false;

            // The smaller shape drives the loop; the larger is probed through its bounds-checking
            // indexer, so neither needs an intersection rectangle worked out first.
            if (a.Width * a.Height > b.Width * b.Height)
                return Overlaps(originB, b, originA, a);

            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    if (!a[x, y]) continue;

                    if (b[originA.x + x - originB.x, originA.y + y - originB.y]) return true;
                }
            }

            return false;
        }

        /// <summary>Every filled cell of the shape, as absolute cell indices from an origin.</summary>
        public void FillCells(Vector2Int origin, List<Vector2Int> into)
        {
            if (into == null) return;

            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    if (this[x, y]) into.Add(new Vector2Int(origin.x + x, origin.y + y));
        }
    }
}
