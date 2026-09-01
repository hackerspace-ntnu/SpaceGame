using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// What is on the pack and where. Owns the overlap rules and the first-fit search; owns no
    /// GameObjects, no prefabs and no netcode.
    ///
    /// <para>
    /// Nothing here touches UnityEngine beyond <see cref="Vector2"/>, so the EditMode tests drive
    /// it as plain C# and the same instance can be built from a save record, from the wire, or
    /// from a player's drag without any of those three knowing about the others.
    /// </para>
    /// <para>
    /// <b>Placement is on a grid.</b> Every uv that goes in is snapped to the cell lattice
    /// <see cref="PackGrid"/> derives from the rig's own webbing pitch, and every item occupies a
    /// drawn set of cells rather than an oriented rectangle. Two consequences worth stating up
    /// front:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>The save and the wire did not change.</b> A snapped uv is still a <see cref="Vector2"/>
    /// in metres from the surface's (0,0) corner — it just happens to name a cell now. Nothing
    /// downstream of <see cref="PackPlacement"/> learned what a cell is, and there is no second
    /// save migration.
    /// </description></item>
    /// <item><description>
    /// <b>Nothing is refused for being unsnapped.</b> A uv from an old save, or from a machine
    /// that rounded differently, is snapped to the nearest cell and placed. Refusing it would lose
    /// the gear, which is the one outcome a restore may never have.
    /// </description></item>
    /// </list>
    /// </summary>
    public sealed class PackLayout
    {
        /// <summary>
        /// One item on the pack, and the cells it is actually sitting on.
        ///
        /// <para>
        /// The occupancy is deliberately not a field on <see cref="PackPlacement"/>. It is derived
        /// from the item's asset, so it is not saved and not sent — the save record and the wire
        /// struct both carry the item id alone and let the receiver resolve it. But an overlap test
        /// needs the cells of the items already down, and this class must not reach for an asset to
        /// get them, so the placement calls (which are handed the shape anyway) supply it once and
        /// it is carried alongside.
        /// </para>
        /// <para>
        /// <see cref="Oriented"/> is the shape as it lies, yaw already applied, so a clash test
        /// never re-rotates anything. <see cref="Origin"/> is its lowest cell.
        /// </para>
        /// </summary>
        private readonly struct Entry
        {
            public readonly PackPlacement Placement;
            public readonly Vector2Int Origin;
            public readonly PackShape Oriented;

            public Entry(PackPlacement placement, Vector2Int origin, PackShape oriented)
            {
                Placement = placement;
                Origin = origin;
                Oriented = oriented;
            }
        }

        private readonly List<Entry> entries = new();

        /// <summary>
        /// The public face of <see cref="entries"/>: a live projection, not a copy. Allocated once,
        /// so reading <see cref="Placements"/> costs nothing and — unlike a shared scratch list
        /// refilled per call — cannot be torn out from under a caller already iterating it.
        /// </summary>
        private readonly PlacementView view;

        public PackLayout() => view = new PlacementView(entries);

        /// <summary>Raised once after any change to the contents.</summary>
        public event Action OnChanged;

        public IReadOnlyList<PackPlacement> Placements => view;

        // ── Asking ───────────────────────────────────────────────────────────

        /// <summary>
        /// Would this shape sit legally here? Every filled cell must be on the surface's grid and
        /// clash with nothing already on <em>that same</em> surface.
        ///
        /// <para>
        /// <paramref name="uv"/> is snapped before the question is asked, so this answers about the
        /// cell the item would actually land in rather than about the pixel the cursor is over.
        /// <paramref name="ignoreItemId"/> excludes one item from the clash test, which is what
        /// lets an item be nudged without colliding with where it currently is.
        /// </para>
        /// <para>
        /// A HOLE in the mask may hang off the edge. That is not slack, it is the point: an
        /// L-shaped item pushed into a corner has an empty quadrant, and demanding that the empty
        /// quadrant be on the mat would refuse the placement the shape exists to allow.
        /// </para>
        /// </summary>
        public bool CanPlace(PackSurfaceId surface, Vector2 surfaceSize, PackShape shape,
                             Vector2 uv, float yaw, string ignoreItemId = null)
        {
            return TryResolve(surface, surfaceSize, shape, uv, yaw, out Vector2Int origin, out PackShape oriented)
                   && !Clashes(surface, origin, oriented, ignoreItemId);
        }

        /// <summary>
        /// The uv this placement would actually be stored at. Idempotent — see
        /// <see cref="PackGrid.Snap"/>, where that matters and why. Snaps the same cells
        /// <see cref="TryPlace"/> would occupy, overhang clamp included, so the preview and the
        /// placement never disagree about where an oversized item sits.
        /// </summary>
        public static Vector2 Snap(PackSurfaceId surface, Vector2 surfaceSize, PackShape shape,
                                   Vector2 uv, float yaw)
        {
            PackShape oriented = PackOverhang.Clamp(surface, surfaceSize,
                                                    shape.Rotated(PackGrid.QuarterTurns(yaw)));

            return oriented.IsEmpty ? uv : PackGrid.Snap(surfaceSize, uv, oriented.Size);
        }

        /// <summary>
        /// The placement whose cells cover a point on a surface — how a click names the thing the
        /// player meant. False on bare canvas, and on the hem, which belongs to no cell.
        /// </summary>
        public bool TryFindAt(PackSurfaceId surface, Vector2 surfaceSize, Vector2 uv,
                              out PackPlacement placement)
        {
            placement = default;

            // On a face that allows overhang, a click on the part of an item hanging past the
            // panel is a click on that item — see PackOverhang.ClampCell.
            Vector2Int cell = PackOverhang.ClampCell(surface, surfaceSize,
                                                     PackGrid.CellAt(surfaceSize, uv));

            if (!PackGrid.OnGrid(surfaceSize, cell)) return false;

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];

                if (entry.Placement.Surface != surface) continue;

                if (!entry.Oriented[cell.x - entry.Origin.x, cell.y - entry.Origin.y]) continue;

                placement = entry.Placement;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Is one cell of a surface free? For <c>PackGridVisual.BuildLatticeHalf</c>, which walks
        /// every cell of the hovered face and sorts each one into the free half of the drag-time
        /// lattice or the taken half.
        /// </summary>
        public bool CellIsFree(PackSurfaceId surface, Vector2Int cell, string ignoreItemId = null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];

                if (entry.Placement.Surface != surface) continue;
                if (ignoreItemId != null && entry.Placement.ItemId == ignoreItemId) continue;

                if (entry.Oriented[cell.x - entry.Origin.x, cell.y - entry.Origin.y]) return false;
            }

            return true;
        }

        /// <summary>
        /// The cells an item already on the pack is sitting on — what the ring drawn around a
        /// placed item is built from. False when the item is not on the pack.
        /// </summary>
        public bool TryOccupancy(string itemId, out PackSurfaceId surface, out Vector2Int origin,
                                 out PackShape oriented)
        {
            int index = IndexOf(itemId);

            if (index < 0)
            {
                surface = default;
                origin = default;
                oriented = PackShape.None;
                return false;
            }

            Entry entry = entries[index];

            surface = entry.Placement.Surface;
            origin = entry.Origin;
            oriented = entry.Oriented;

            return true;
        }

        /// <summary>
        /// A uv guaranteed to fall inside one of this item's own FILLED cells.
        ///
        /// <para>
        /// <b>Not the same as its <see cref="PackPlacement.Uv"/>, and that is the whole reason this
        /// exists.</b> Every request the pack sends names its item positionally — a point on a
        /// face, resolved by <see cref="TryFindAt"/> on the server, because a string will not fit
        /// in a <c>NetArg</c> and because the grab point is what the player clicked. The stored uv
        /// is the centre of the item's bounding BLOCK, and the centre of an L-shaped block is
        /// exactly the corner the L does not fill. Sending that, a mask-shaped item could be
        /// dragged, dropped or right-clicked to the hotbar and the server would find nothing under
        /// the point and quietly do nothing.
        /// </para>
        /// </summary>
        public bool TryAnchorUv(string itemId, Vector2 surfaceSize, out Vector2 uv)
        {
            uv = default;

            int index = IndexOf(itemId);
            if (index < 0) return false;

            Entry entry = entries[index];

            for (int y = 0; y < entry.Oriented.Height; y++)
            {
                for (int x = 0; x < entry.Oriented.Width; x++)
                {
                    if (!entry.Oriented[x, y]) continue;

                    uv = PackGrid.CentreUv(surfaceSize,
                                           new Vector2Int(entry.Origin.x + x, entry.Origin.y + y));
                    return true;
                }
            }

            return false;
        }

        // ── Changing ─────────────────────────────────────────────────────────

        /// <summary>Put an item on the pack. False leaves the layout exactly as it was.</summary>
        public bool TryPlace(string itemId, PackSurfaceId surface, Vector2 surfaceSize,
                             PackShape shape, Vector2 uv, float yaw)
        {
            if (string.IsNullOrEmpty(itemId)) return false;

            // An item is one object. Placing one that is already down is a caller bug, not a
            // second copy — moving is TryMove's job.
            if (IndexOf(itemId) >= 0) return false;

            if (!TryResolve(surface, surfaceSize, shape, uv, yaw, out Vector2Int origin, out PackShape oriented))
                return false;

            if (Clashes(surface, origin, oriented, null)) return false;

            entries.Add(Seat(itemId, surface, surfaceSize, origin, oriented, yaw));

            OnChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Move an item already on the pack, possibly to another surface. Refusing leaves it where
        /// it was, so a rejected drag snaps back rather than dropping the item.
        /// </summary>
        public bool TryMove(string itemId, PackSurfaceId surface, Vector2 surfaceSize,
                            PackShape shape, Vector2 uv, float yaw)
        {
            int index = IndexOf(itemId);
            if (index < 0) return false;

            if (!TryResolve(surface, surfaceSize, shape, uv, yaw, out Vector2Int origin, out PackShape oriented))
                return false;

            // The item's own current cells are not an obstacle to itself — without this, nudging
            // anything by one cell always fails.
            if (Clashes(surface, origin, oriented, itemId)) return false;

            entries[index] = Seat(itemId, surface, surfaceSize, origin, oriented, yaw);

            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Take an item off the pack. False means it was not on it.</summary>
        public bool Remove(string itemId)
        {
            int index = IndexOf(itemId);
            if (index < 0) return false;

            entries.RemoveAt(index);

            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Empty the pack.</summary>
        public void Clear()
        {
            if (entries.Count == 0) return;

            entries.Clear();

            OnChanged?.Invoke();
        }

        /// <summary>
        /// First free spot for a world pickup, which arrives with no opinion about where it goes.
        /// Walks the surface's cells in reading order at each legal turn.
        ///
        /// <para>
        /// <b>Four orientations, and only for a shape that has holes.</b> A solid rectangle looks
        /// the same at 0 and 180, so trying both would double the search for nothing; a mask does
        /// not, and an L that will not go in one way round often goes in another.
        /// </para>
        /// <para>
        /// The free system's 15, 30 and 45 degree probes are gone with the grid, and with them the
        /// diagonal seating they existed for. That was only ever needed by the 1.35 m LaserStaff,
        /// and <see cref="PackSurfaceId.LongGoods"/> was added to take it square on — see the note
        /// on that enum value.
        /// </para>
        /// <para>
        /// <paramref name="ignoreItemId"/> excludes one item from the clash test, exactly as
        /// <see cref="CanPlace"/> does. A swap needs it: the incoming item is looking for room in
        /// the space the OUTGOING one is still occupying, and without this the search has to be run
        /// against a layout that has already been mutated — which cannot then be undone without
        /// publishing an intermediate state to every other machine.
        /// </para>
        /// </summary>
        public bool TryFindSpot(PackSurfaceId surface, Vector2 surfaceSize, PackShape shape,
                                out Vector2 uv, out float yaw, string ignoreItemId = null,
                                bool allowTurns = true)
        {
            uv = Vector2.zero;
            yaw = 0f;

            if (shape.IsEmpty) return false;

            Vector2Int grid = PackGrid.CellsOn(surfaceSize);
            if (grid.x <= 0 || grid.y <= 0) return false;

            int turns = !allowTurns ? 1 : shape.IsRectangular ? 2 : 4;

            for (int q = 0; q < turns; q++)
            {
                PackShape oriented = PackOverhang.Clamp(surface, surfaceSize, shape.Rotated(q));

                // The BOUNDING block has to fit, which is very slightly conservative for a mask
                // whose overhanging quadrant is empty. Left that way on purpose: first-fit is the
                // path with no player pointing at anything, and a spot it declines is one the
                // player can still make by hand.
                if (oriented.Width > grid.x || oriented.Height > grid.y) continue;

                for (int y = 0; y <= grid.y - oriented.Height; y++)
                {
                    for (int x = 0; x <= grid.x - oriented.Width; x++)
                    {
                        var origin = new Vector2Int(x, y);

                        if (Clashes(surface, origin, oriented, ignoreItemId)) continue;

                        uv = PackGrid.BlockCentreUv(surfaceSize, origin, oriented.Size);
                        yaw = q * 90f;
                        return true;
                    }
                }
            }

            return false;
        }

        // ── Internals ────────────────────────────────────────────────────────

        /// <summary>
        /// Turn a loose uv and yaw into the cells an item would occupy. False when the shape is
        /// empty, the surface holds no whole cells, or a FILLED cell would fall off the grid.
        /// On a face that allows overhang the shape is first clamped to the face's span — the
        /// cells past the edge become overhang instead of a refusal.
        /// </summary>
        private static bool TryResolve(PackSurfaceId surface, Vector2 surfaceSize, PackShape shape,
                                       Vector2 uv, float yaw,
                                       out Vector2Int origin, out PackShape oriented)
        {
            origin = default;
            oriented = PackOverhang.Clamp(surface, surfaceSize,
                                          shape.Rotated(PackGrid.QuarterTurns(yaw)));

            if (oriented.IsEmpty) return false;

            Vector2Int grid = PackGrid.CellsOn(surfaceSize);
            if (grid.x <= 0 || grid.y <= 0) return false;

            origin = PackGrid.BlockOrigin(surfaceSize, uv, oriented.Size);

            for (int y = 0; y < oriented.Height; y++)
            {
                for (int x = 0; x < oriented.Width; x++)
                {
                    if (!oriented[x, y]) continue;

                    int cx = origin.x + x;
                    int cy = origin.y + y;

                    if (cx < 0 || cy < 0 || cx >= grid.x || cy >= grid.y) return false;
                }
            }

            return true;
        }

        /// <summary>The entry an accepted placement becomes: snapped uv, snapped yaw, its cells.</summary>
        private static Entry Seat(string itemId, PackSurfaceId surface, Vector2 surfaceSize,
                                  Vector2Int origin, PackShape oriented, float yaw)
        {
            Vector2 snapped = PackGrid.BlockCentreUv(surfaceSize, origin, oriented.Size);

            return new Entry(
                new PackPlacement(itemId, surface, snapped, PackGrid.SnapYaw(yaw)), origin, oriented);
        }

        private bool Clashes(PackSurfaceId surface, Vector2Int origin, PackShape oriented,
                             string ignoreItemId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry other = entries[i];

                if (other.Placement.Surface != surface) continue;
                if (ignoreItemId != null && other.Placement.ItemId == ignoreItemId) continue;

                if (PackShape.Overlaps(origin, oriented, other.Origin, other.Oriented)) return true;
            }

            return false;
        }

        private int IndexOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return -1;

            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Placement.ItemId == itemId) return i;

            return -1;
        }

        /// <summary>
        /// Reads the placements out of <see cref="entries"/> without copying them. A projection
        /// rather than a second list, so there is exactly one place a placement is stored and the
        /// old failure mode — an out-of-range from inside an overlap test, because one method
        /// updated a list and forgot its twin — cannot be written.
        /// </summary>
        private sealed class PlacementView : IReadOnlyList<PackPlacement>
        {
            private readonly List<Entry> source;

            public PlacementView(List<Entry> source) => this.source = source;

            public int Count => source.Count;

            public PackPlacement this[int index] => source[index].Placement;

            public IEnumerator<PackPlacement> GetEnumerator()
            {
                for (int i = 0; i < source.Count; i++)
                    yield return source[i].Placement;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
