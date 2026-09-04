using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    /// <summary>
    /// Anything that holds gear by free placement on flat faces: the contents, the faces, the
    /// display copies laid on them, and the transfers to and from a hotbar.
    ///
    /// <para>
    /// Extracted from <see cref="BackpackObject"/> when the ship's inventory wall arrived. The two
    /// have nothing in common as objects — one is a rig that folds and rides on a back, the other
    /// is a rack bolted to a bulkhead — but everything in common as CONTAINERS, and the half that
    /// is common is the half with all the arithmetic in it. Copying it would have put
    /// <see cref="RebuildVisuals"/>, the swap rule and the first-fit rule in the codebase twice,
    /// where they could disagree about what fits.
    /// </para>
    /// <para>
    /// Two things are left to the subclass, because they are the only two a wall and a rig
    /// genuinely answer differently: which faces are reachable right now
    /// (<see cref="Reaches"/> — a wall's are always, a rig's depend on the fold), and where a
    /// request goes on the wire (<see cref="RequestTake"/> / <see cref="RequestStow"/> — the pack
    /// borrows its wearer's channel, the wall has a NetworkObject of its own).
    /// </para>
    /// </summary>
    public abstract class PackContainer : MonoBehaviour
    {
        [Tooltip("The flat faces items can be laid on, in any order — a surface is identified by " +
                 "its own PackSurfaceId, not by its position here. Left empty this resolves to " +
                 "every PackSurface in the children, so a model whose SURF_ empties carry the " +
                 "component needs no wiring at all.")]
        [SerializeField] private PackSurface[] surfaces = new PackSurface[0];

        [Tooltip("The straps, cords, sleeves and clips laid over placed items. Optional — with no " +
                 "library every item simply lies bare on its surface.")]
        [SerializeField] private HolderLibrary holders;

        [Tooltip("Which cells of the grid each item fills. Optional — an item with no row in it, " +
                 "and a container with no library at all, falls back to the solid block " +
                 "PackShape.ForFootprint derives from the item's true size. Authoring a shape is " +
                 "how you say 'this one is not a rectangle'.")]
        [SerializeField] private PackShapeLibrary shapes;

        [Tooltip("How much bigger than its own grid this container is DRAWN. 1 for the backpack, " +
                 "PackScale.WallDisplay for the ship's gear wall. It multiplies the mapping from " +
                 "a uv to the world point it is drawn at and NOTHING else — not the cell, not the " +
                 "face rectangle, not a single stored or replicated number.")]
        [SerializeField] private float displayScale = 1f;

        [Header("Starting contents")]
        [Tooltip("Laid out first-fit when the container is built. Two lists purely because that " +
                 "is how they were authored before the strap/pocket split went away — they are " +
                 "now one pool and the order between them means nothing.")]
        [SerializeField] private List<InventoryItem> startingStrapItems = new();
        [SerializeField] private List<InventoryItem> startingMainItems = new();

        /// <summary>
        /// What is held and where. Built on first use rather than in Awake, because storage has to
        /// exist whenever someone asks for it — and Awake is not one of those moments you can count
        /// on. The editor never runs it on a component added to an object outside play mode, so an
        /// EditMode test, an inspector tool, or any code touching a container before the first
        /// frame would otherwise be handed a null.
        /// </summary>
        public PackLayout Layout => layout ??= new PackLayout();

        /// <summary>The faces items can be laid on. Never null; possibly empty.</summary>
        public IReadOnlyList<PackSurface> Surfaces => ResolvedSurfaces();

        /// <summary>
        /// The per-item grid shapes, or null when nobody wired one — in which case every item gets
        /// the block derived from its own footprint. Read by the placement controllers and by the
        /// save codec, which both have to reach the same conclusion about an item as this does.
        /// </summary>
        public PackShapeLibrary Shapes => shapes;

        /// <summary>The cells an item occupies here, authored or derived.</summary>
        public PackShape ShapeFor(InventoryItem item) => PackShapes.For(item, shapes);

        /// <summary>
        /// How much bigger than its own grid this container is DRAWN. 1 on the rig,
        /// <see cref="PackScale.WallDisplay"/> on the ship's gear wall.
        ///
        /// <para>
        /// <b>The drawn frame and the logical frame are two different things, and this is the only
        /// number between them.</b> Everything a container REASONS about — the cell, the face
        /// rectangle, which cells a shape covers, the uv in a placement, the uv in a save, the uv
        /// on the wire — is the logical frame and never sees this. Everything a player LOOKS at —
        /// the board, the ghost cells, the hover lattice, the display copies, the holders, the
        /// straps, the face collider the aim ray hits — is the drawn frame and is this many times
        /// bigger. So a container can be made to read larger across a room without a single cell,
        /// a single byte of save, or a single item's capacity moving.
        /// </para>
        /// <para>
        /// It lives here rather than on <see cref="PackSurface"/> because it is a property of the
        /// CONTAINER — every face of one thing is drawn at one size — and because one authored
        /// number that the faces read is a number that cannot drift between them.
        /// <c>PackSurface.DisplayScale</c> is what reads it, and it walks up to find this rather
        /// than being pushed a copy: a container is routinely built by <c>Instantiate</c> outside
        /// play mode, where nothing pushes anything, which is the same reason
        /// <see cref="ResolvedSurfaces"/> refuses to cache.
        /// </para>
        /// <para>
        /// Guarded against zero and negative rather than clamped in the inspector: a serialized
        /// field left at its C# default by an <c>AddComponent</c> in an EditMode fixture reads 0,
        /// and a 0 here would collapse every face to a point instead of failing.
        /// </para>
        /// </summary>
        public float DisplayScale => displayScale > 1e-6f ? displayScale : 1f;

        private PackLayout layout;

        /// <summary>
        /// Item id to the asset it names, for everything this container has been handed directly.
        ///
        /// <see cref="Registry{T}"/> is the general answer and the fallback below, but it only
        /// knows assets that <c>RegistryLoader</c> found under Resources. A starting item wired
        /// straight onto a prefab, or an item minted by a test, is not in it — and resolving those
        /// to null would quietly refuse to display or hand over gear that is being held.
        /// </summary>
        private readonly Dictionary<string, InventoryItem> known = new();

        /// One live display object per placed item, keyed the same way the layout is.
        private readonly Dictionary<string, GameObject> visuals = new();

        /// <summary>
        /// The holder laid over each placed item, keyed the same way again.
        ///
        /// A parallel dictionary rather than a child of the item's own display copy, because the
        /// two are scaled differently on purpose: the item is fitted uniformly and the holder is
        /// stretched non-uniformly to the item's footprint. Parenting one to the other would put
        /// the item's fit into the holder's stretch, and a holder's whole job is to be the exact
        /// size of the thing under it.
        /// </summary>
        protected readonly Dictionary<string, GameObject> holderVisuals = new();

        /// <summary>
        /// The lattice of cells drawn around each placed item, keyed the same way again.
        ///
        /// A third parallel dictionary for the same reason the holders are a second one: it is
        /// built in the SURFACE's frame and the item's display copy is scaled to the item, so
        /// parenting the grid under the item would multiply the item's fit scale into a mesh that
        /// is supposed to measure the face.
        /// </summary>
        private readonly Dictionary<string, GameObject> gridVisuals = new();

        /// <summary>
        /// The strap bands lashing each placed item down, keyed the same way again.
        ///
        /// A fourth parallel dictionary, for the grid's reason: the bands are built in the
        /// SURFACE's frame from the item's sampled silhouette, so parenting them under the item's
        /// fitted display copy would multiply the item's scale into webbing that is supposed to
        /// measure the face. Handles rather than GameObjects, because each band's mesh has to be
        /// destroyable after Unity has already torn the band object down — see
        /// <see cref="PackStrapVisual.Destroy"/>.
        /// </summary>
        private readonly Dictionary<string, PackStrapVisual.Handle> strapVisuals = new();

        /// <summary>
        /// The one placed item that is in a hand on THIS machine, and is therefore not drawn on
        /// the face. Null the rest of the time.
        ///
        /// <para>
        /// Lifting an item is local — nothing is sent and the layout still holds it exactly where
        /// it was — so between the lift and the click that puts it down the item is on screen
        /// twice: the copy under the cursor and the one it was lifted off. Two of the same thing,
        /// one of which cannot be interacted with, reads as a bug rather than as a preview.
        /// </para>
        /// <para>
        /// Kept here rather than in the hand because a placed item is FOUR objects — the display
        /// copy, its holder, the cells it sits on and the straps over it — and only this class
        /// knows all four; and because every rebuild has to put the item straight back out of
        /// sight, which nothing outside this class gets a chance to do.
        /// </para>
        /// </summary>
        private string inHandItemId;

        /// <summary>
        /// Set while a bulk rebuild is running. A layout change is a single coarse event, so
        /// adopting twelve placements off the wire would otherwise tear the whole display down and
        /// build it back up twelve times over.
        /// </summary>
        protected bool rebuilding;

        private readonly List<PackSurface> reachable = new();

        // ------------------------------------------------------------------ lifecycle
        //
        // Not Awake/OnDestroy: BackpackObject has its own and would have to remember to chain to
        // base. Named hooks a subclass calls from its own Awake are one thing to forget instead of
        // four, and they are callable from an EditMode fixture, where Awake never runs at all.

        /// <summary>
        /// Subscribe the display and lay out whatever was authored. Call from the subclass's Awake.
        /// </summary>
        protected void BeginContents()
        {
            Layout.OnChanged += RebuildVisuals;

            // Suppressed for the same reason the wire's bulk adopt is: the layout raises one event
            // per item, and an unsuppressed load of a dozen starting items would tear the display
            // down and build it back up a dozen times before the first frame.
            rebuilding = true;

            // NOT TryStow, which is the player-facing path and is therefore gated on Reaches.
            // Authored contents are a record of where the gear already is rather than somebody
            // choosing a face, exactly like a save being read back. Same rule, and the same reason,
            // as TryPlace not being gated.
            foreach (InventoryItem item in startingStrapItems) StowAuthored(item);
            foreach (InventoryItem item in startingMainItems) StowAuthored(item);

            rebuilding = false;
        }

        /// <summary>Release the display and the meshes it owns. Call from the subclass's OnDestroy.</summary>
        protected void EndContents()
        {
            // The field, not the property: a container destroyed before anything ever asked for its
            // contents should not build a layout on its way out.
            if (layout != null) layout.OnChanged -= RebuildVisuals;

            // The strap meshes are per-placement builds Unity never collects on its own. The
            // display hierarchy dies with the container, but a HideAndDontSave mesh dies only when
            // somebody destroys it — RebuildVisuals covers every rebuild, this covers the last.
            // The handles keep the meshes reachable even when Unity took the band objects down
            // before this ran.
            foreach (PackStrapVisual.Handle straps in strapVisuals.Values)
                PackStrapVisual.Destroy(straps);

            strapVisuals.Clear();
        }

        // ------------------------------------------------------------------ the subclass's half

        /// <summary>
        /// Can a player put something on this face right now? True for every face by default,
        /// which is the whole answer for anything that does not fold.
        /// </summary>
        public virtual bool Reaches(PackSurfaceId id) => true;

        /// <summary>
        /// Ask the server for whatever is at a point on one of the faces. <b>The taker's own
        /// machine must go through here, not through <see cref="TryTakeToHotbar"/>.</b>
        ///
        /// <para>
        /// Two players can be reaching into one container, so which of them gets the last thing in
        /// it is the server's to decide.
        /// </para>
        /// </summary>
        public abstract void RequestTake(PackSurfaceId surface, Vector2 uv, Interactor interactor);

        /// <summary>
        /// The mirror of <see cref="RequestTake"/>: somebody wants one of their hotbar slots put
        /// down here. <b>The stower's own machine must go through here.</b>
        /// </summary>
        public abstract void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv,
                                         float yaw, Interactor interactor);

        /// <summary>Run after every display rebuild, for whatever the subclass hangs off one.</summary>
        protected virtual void OnVisualsRebuilt() { }

        // ------------------------------------------------------------------ faces

        /// <summary>
        /// The authored array, or every PackSurface in the children when it was left empty.
        ///
        /// Resolved lazily and not cached, because a container is often built by Instantiate and
        /// its surfaces are FBX children — a cache filled by an Awake that never ran, which is the
        /// normal case for a component added outside play mode, would be empty for good.
        /// </summary>
        protected IReadOnlyList<PackSurface> ResolvedSurfaces()
        {
            if (surfaces != null && surfaces.Length > 0) return surfaces;

            return GetComponentsInChildren<PackSurface>(true);
        }

        /// <summary>
        /// The faces a player can use, for first-fit.
        ///
        /// Into a reused buffer rather than a fresh list per call, because both callers can call
        /// this often enough over a session to make the churn worth avoiding. The result is only
        /// ever read before the next call — there is no path where one of these walks is still in
        /// progress when another starts.
        /// </summary>
        protected IReadOnlyList<PackSurface> ReachableSurfaces()
        {
            IReadOnlyList<PackSurface> all = ResolvedSurfaces();

            reachable.Clear();

            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && Reaches(all[i].Id)) reachable.Add(all[i]);

            return reachable;
        }

        /// <summary>The surface with this id, or null if there is no such face here.</summary>
        public PackSurface SurfaceFor(PackSurfaceId id)
        {
            IReadOnlyList<PackSurface> all = ResolvedSurfaces();

            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Id == id) return all[i];

            return null;
        }

        /// <summary>
        /// The face RESERVED for <paramref name="item"/> that has room for it right now, or null.
        /// The rig has exactly one — the oxygen bottle's socket on the centre back panel — and
        /// today it is the only reserved face in the game.
        ///
        /// <para>
        /// <b>Only a reserved face answers.</b> An ordinary face takes anything that fits, so it
        /// would qualify for every item there is and say nothing at all. A socket is different in
        /// kind: it is the one place on the container the player has no way of guessing at, because
        /// the ordinary readout — cells that go green under the cursor — only speaks about the face
        /// already being aimed at, and a socket has to be FOUND before it can be aimed at.
        /// </para>
        /// <para>
        /// <b>Room is asked, not assumed.</b> A socket already holding a bottle is full, and
        /// naming it would send the player to a face that answers red the moment they arrive.
        /// <paramref name="ignoreItemId"/> excludes the item in the player's own hand, exactly as
        /// <see cref="PackLayout.TryFindSpot"/> does for a swap — it is still ON the layout while
        /// it is being carried.
        /// </para>
        /// </summary>
        public PackSurface SocketFor(InventoryItem item, string ignoreItemId = null)
        {
            if (item == null) return null;

            IReadOnlyList<PackSurface> all = ResolvedSurfaces();
            PackShape shape = ShapeFor(item);

            for (int i = 0; i < all.Count; i++)
            {
                PackSurface surface = all[i];

                if (surface == null) continue;
                if (surface.AcceptsOnly == null || surface.AcceptsOnly.Count == 0) continue;
                if (!surface.AcceptsItem(item)) continue;
                if (!Reaches(surface.Id)) continue;

                if (Layout.TryFindSpot(surface.Id, surface.Size, shape, out _, out _, ignoreItemId))
                    return surface;
            }

            return null;
        }

        // ------------------------------------------------------------------ contents

        /// <summary>The asset an item id names, or null if nothing here or in the registry knows it.</summary>
        public InventoryItem ItemFor(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            return known.TryGetValue(itemId, out InventoryItem item) && item != null
                ? item
                : Registry<InventoryItem>.Get(itemId);
        }

        /// <summary>
        /// The placement whose cells cover a point on a surface, which is how a take names the
        /// thing the player actually aimed at. False when they hit bare canvas.
        ///
        /// A cell lookup, not a 1 mm probe rectangle against every footprint: the layout already
        /// knows which cells each item is on, so the hit test is an array index rather than a scan
        /// with a separating-axis test inside it.
        /// </summary>
        public bool TryFindAt(PackSurfaceId surfaceId, Vector2 uv, out PackPlacement placement)
        {
            placement = default;

            PackSurface surface = SurfaceFor(surfaceId);
            if (surface == null) return false;

            return Layout.TryFindAt(surfaceId, surface.Size, uv, out placement);
        }

        /// <summary>
        /// The point that names this placement to <see cref="TryFindAt"/> — a cell the item really
        /// fills, which its stored centre uv is not guaranteed to be once masks exist. Every
        /// request that identifies an item positionally has to use this. Falls back to the stored
        /// uv, which is right for every rectangle.
        /// </summary>
        public Vector2 AnchorUv(PackPlacement placement)
        {
            PackSurface surface = SurfaceFor(placement.Surface);

            return surface != null && Layout.TryAnchorUv(placement.ItemId, surface.Size, out Vector2 uv)
                ? uv
                : placement.Uv;
        }

        /// <summary>
        /// Put an item at a named spot. False leaves the container exactly as it was.
        ///
        /// Deliberately NOT gated on <see cref="Reaches"/>. This is the primitive every restore
        /// goes through — <see cref="AdoptPlacements"/>, and the save codec behind it — and a
        /// restore is not a player choosing a face, it is a record of where the gear already is.
        /// Refusing it because a leaf happens to be the other way up would either lose the item or
        /// shuffle it somewhere nobody asked for. The gate lives on the player-facing paths.
        /// </summary>
        public bool TryPlace(InventoryItem item, PackSurfaceId surfaceId, Vector2 uv, float yaw)
        {
            if (item == null || string.IsNullOrEmpty(item.ID)) return false;

            PackSurface surface = SurfaceFor(surfaceId);
            if (surface == null) return false;

            // A reserved face refuses before the geometry is ever asked. Gated even here, on the
            // primitive every RESTORE goes through, unlike Reaches: a save or a wire message
            // naming an item that face no longer takes — one written before it was reserved, or
            // by a build that reserved it differently — must not put it back. Nothing is lost by
            // refusing, because every restore path falls through to first-fit.
            if (!surface.AcceptsItem(item)) return false;

            // Cached BEFORE the layout is told, not after. TryPlace raises OnChanged synchronously
            // and the display rebuild that answers it resolves every placement's id back to an
            // asset — so an item registered afterwards is invisible for exactly the rebuild that
            // was meant to show it, and silently, because an unresolvable id is skipped.
            known[item.ID] = item;

            // Snapped here rather than inside the layout, because whether an item may turn at all
            // is a property of the ITEM's authored row and the layout has no library to ask.
            return Layout.TryPlace(item.ID, surfaceId, surface.Size, PackShapes.For(item, shapes),
                                   uv, PackShapes.SnapYaw(item, shapes, yaw));
        }

        /// <summary>Slide something already held somewhere else, possibly onto another face.</summary>
        public bool TryMove(string itemId, PackSurfaceId surfaceId, Vector2 uv, float yaw)
        {
            InventoryItem item = ItemFor(itemId);
            if (item == null) return false;

            // A move is always a player action, so unlike TryPlace this one is gated — the aim
            // cannot legitimately be on a face that is currently against the sand.
            if (!Reaches(surfaceId)) return false;

            PackSurface surface = SurfaceFor(surfaceId);
            if (surface == null) return false;

            if (!surface.AcceptsItem(item)) return false;

            return Layout.TryMove(itemId, surfaceId, surface.Size, PackShapes.For(item, shapes),
                                  uv, PackShapes.SnapYaw(item, shapes, yaw));
        }

        /// <summary>
        /// Overflow target for world pickups, which arrive with no opinion about where they go.
        /// First-fit across the faces in order; false means there is genuinely no room for it.
        /// </summary>
        public bool TryStow(InventoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ID)) return false;

            // Before, not after — see the note in TryPlace.
            known[item.ID] = item;

            // Reachable faces only. First-fit is the one path with no player pointing at anything,
            // so it is the one that would otherwise put gear under the mat and never mention it.
            return TryArrange(Layout, ReachableSurfaces(), item, shapes);
        }

        /// <summary>
        /// First-fit over EVERY face, gate and all. <b>Restores only.</b>
        ///
        /// The ungated twin of <see cref="TryStow"/>, and it exists for the reason written out over
        /// <see cref="TryPlace"/>: a starting item wired onto a prefab, or a saved placement whose
        /// spot no longer fits, is a record of where the gear already is rather than a player
        /// choosing a face.
        /// </summary>
        protected bool StowAuthored(InventoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ID)) return false;

            // Before, not after — see the note in TryPlace.
            known[item.ID] = item;

            return TryArrange(Layout, ResolvedSurfaces(), item, shapes);
        }

        /// <summary>
        /// Move a hotbar slot's item onto a face, at the exact spot and turn given.
        /// <b>Server side only</b> — callers want <see cref="RequestStow"/>.
        ///
        /// <para>
        /// <b>The container is filled before the hotbar is emptied</b>, the same order
        /// <see cref="TryTakeToHotbar"/> uses and for the same reason: a placement that is going to
        /// be refused must be refused while the item is still safely somewhere. Doing it the other
        /// way round means every "there is no room" is an item deleted out of the world.
        /// </para>
        /// <para>
        /// <b>There is no fallback.</b> A spot that is taken by the time this runs — another player
        /// got there first — is a refusal, and the item stays in the hotbar. The player only ever
        /// sends this for cells they watched turn green, so anything else is a lie about what they
        /// asked for.
        /// </para>
        /// </summary>
        public bool TryStowFromHotbar(IPlayerInventory hotbar, int slotIndex,
                                      PackSurfaceId surfaceId, Vector2 uv, float yaw)
        {
            if (hotbar == null) return false;
            if (slotIndex < 0 || slotIndex >= hotbar.GetInventorySize()) return false;

            InventorySlot slot = hotbar.GetSlot(slotIndex);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null || string.IsNullOrEmpty(item.ID)) return false;

            // The layout is keyed by id, so an item already held cannot be placed a second time —
            // TryPlace answers false for it. Caught here instead, because reaching the hotbar
            // removal below on a refused placement is exactly the item-deleting path this method is
            // ordered to avoid, and because the same asset really can be in both places at once:
            // the hotbar holds items by reference, not by instance.
            if (TryFindPlacement(item.ID, out _)) return false;

            if (!Reaches(surfaceId) || !TryPlace(item, surfaceId, uv, yaw)) return false;

            // Cannot fail: the index was bounds-checked against this very hotbar and the slot was
            // read out of it. Undone rather than trusted anyway, because the failure would be one
            // item in two places, which nothing downstream would ever notice.
            if (!hotbar.TryRemoveItem(slotIndex))
            {
                Layout.Remove(item.ID);
                return false;
            }

            return true;
        }

        /// <summary>Is this asset already lying here? The layout is keyed by id, so a second copy
        /// of one can never be placed — see <see cref="TryStowFromHotbar"/>.</summary>
        public bool Holds(string itemId) => TryFindPlacement(itemId, out _);

        /// <summary>Where an id currently sits, if it is here at all.</summary>
        protected bool TryFindPlacement(string itemId, out PackPlacement placement)
        {
            IReadOnlyList<PackPlacement> placements = Layout.Placements;

            for (int i = 0; i < placements.Count; i++)
            {
                if (placements[i].ItemId != itemId) continue;

                placement = placements[i];
                return true;
            }

            placement = default;
            return false;
        }

        /// <summary>Take something out. Null if it was not here.</summary>
        public InventoryItem TakeOut(string itemId)
        {
            InventoryItem item = ItemFor(itemId);

            // `known` keeps the entry. It is a resolver cache, and an item on its way to a hotbar
            // is very likely on its way back — a swap puts one down the same frame it lifts one.
            return Layout.Remove(itemId) ? item : null;
        }

        /// <summary>
        /// Replace the contents wholesale, measuring each item as it goes down.
        ///
        /// <para>
        /// This is the only way a layout can be rebuilt from a record, and it is deliberately not a
        /// bulk assignment. <see cref="PackLayout"/> keeps a footprint beside every placement and
        /// nothing may inject one without it — a placement whose footprint were guessed would clash
        /// correctly on the machine that made it and wrongly on every other.
        /// </para>
        /// <para>
        /// A placement the layout refuses — the item was resized, or the save predates a surface
        /// being narrowed — falls back to first-fit rather than being dropped. Losing gear silently
        /// on a load is the worse failure by a distance.
        /// </para>
        /// </summary>
        public void AdoptPlacements(IEnumerable<PackPlacement> incoming)
        {
            rebuilding = true;

            try
            {
                Layout.Clear();

                // `known` is a cache, not contents, and is deliberately NOT cleared with them.
                // An id that arrives off the wire can only be resolved through the registry, so
                // throwing away what this container already knows would lose exactly the assets the
                // registry cannot supply — starting items wired onto a prefab, and test doubles.
                if (incoming == null) return;

                foreach (PackPlacement placement in incoming)
                {
                    InventoryItem item = ItemFor(placement.ItemId);
                    if (item == null) continue;

                    // StowAuthored, not TryStow: the fallback is still part of a restore, so it may
                    // not be confined to the faces a player could reach right now.
                    if (!TryPlace(item, placement.Surface, placement.Uv, placement.Yaw))
                        StowAuthored(item);
                }
            }
            finally
            {
                rebuilding = false;
                RebuildVisuals();
            }
        }

        /// <summary>
        /// The first-fit rule, in one place because three callers need to agree on it: a world
        /// pickup overflowing into a container, a v1 save being arranged onto surfaces that did not
        /// exist when it was written, and a placement off the wire whose spot no longer fits.
        /// </summary>
        public static bool TryArrange(PackLayout layout, IReadOnlyList<PackSurface> surfaces,
                                      InventoryItem item, PackShapeLibrary shapes)
        {
            if (layout == null || surfaces == null || item == null || string.IsNullOrEmpty(item.ID))
                return false;

            PackShape shape = PackShapes.For(item, shapes);

            // An item forbidden from turning is offered only the one orientation. The search cannot
            // work that out for itself — it is handed a shape, not an item — and letting it report
            // a turned spot that TryPlace then straightens would find room and lose it again.
            bool mayTurn = PackShapes.AllowsRotation(item, shapes);

            // TWO passes, and the order is the point. A face RESERVED for this item is where the
            // item belongs — the pack is plumbed into its socket — so it is offered before the
            // general shelves. Single-pass first-fit walks the table in order and puts the bottle
            // on the mat, leaving the one place that means anything empty; nothing about that is
            // visible, because the mat is a perfectly good spot.
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantReserved = pass == 0;

                for (int i = 0; i < surfaces.Count; i++)
                {
                    PackSurface surface = surfaces[i];
                    if (surface == null) continue;

                    // First-fit is the one path with nobody pointing at anything, so a reserved
                    // face has to opt out here or the search fills the bottle's socket with the
                    // first thing that happens to fit it.
                    if (!surface.AcceptsItem(item)) continue;

                    bool reserved = surface.AcceptsOnly != null && surface.AcceptsOnly.Count > 0;
                    if (reserved != wantReserved) continue;

                    if (layout.TryFindSpot(surface.Id, surface.Size, shape, out Vector2 uv,
                                           out float yaw, ignoreItemId: null, allowTurns: mayTurn)
                        && layout.TryPlace(item.ID, surface.Id, surface.Size, shape, uv, yaw))
                        return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------ taking

        /// <summary>
        /// Move whatever is under a point into the given hotbar. <b>Server side only</b> — callers
        /// want <see cref="RequestTake"/>.
        ///
        /// <para>
        /// Both halves of this transfer replicate themselves, and neither of them from here. The
        /// hotbar is <c>PlayerInventoryNetwork</c>'s, which is server-authoritative and pushes
        /// every slot change out through its own NetworkList; this side's half is watched by
        /// whatever replicates this container's layout. Doing anything else on the taker's machine
        /// as well would double the transfer up.
        /// </para>
        /// <para>
        /// A full hotbar is not a refusal — it is a SWAP: the held item goes into the player's
        /// selected hotbar slot and whatever was in that slot takes its place. Refusing instead is
        /// what made a full hotbar feel like a broken interaction, because the only way out was to
        /// drop something on the ground first.
        /// </para>
        /// </summary>
        public bool TryTakeToHotbar(PackSurfaceId surfaceId, Vector2 uv, IPlayerInventory hotbar)
        {
            if (hotbar == null) return false;

            if (!TryFindAt(surfaceId, uv, out PackPlacement placement)) return false;

            InventoryItem heldItem = ItemFor(placement.ItemId);
            if (heldItem == null) return false;

            if (!HotbarCanResolve(heldItem)) return false;

            // Tested BEFORE the item leaves, because a take-then-put-back whose add failed in
            // between would have already fired a change and destroyed the display object.
            if (hotbar.TryAddItem(heldItem))
            {
                TakeOut(placement.ItemId);
                return true;
            }

            return TrySwapWithHotbar(placement, heldItem, hotbar);
        }

        /// <summary>
        /// Would a take land? Non-mutating, and <b>presentation only</b> — it is a prediction, not
        /// a decision: the server still decides, and by the time it does another player may have
        /// taken the space.
        ///
        /// <para>
        /// The interesting answer is the last one. A full hotbar is a SWAP, and a swap needs
        /// somewhere for the displaced item to go; when there is nowhere, the take is refused on
        /// the server and every machine stays exactly as it was — which on the taker's screen is a
        /// press that did nothing at all. <paramref name="refused"/> distinguishes exactly that
        /// case, which is what the caller uses to decide whether the refusal is worth a visible
        /// flash.
        /// </para>
        /// <para>
        /// <paramref name="targetSlot"/> answers a DIFFERENT question at -1 than at a real index.
        /// -1 is <see cref="TryTakeToHotbar"/>'s question — "any empty slot, else swap the
        /// SELECTED one". A real index is "put it in THIS box, swapping only if it is occupied",
        /// whose fallback searches every reachable face rather than only the one aimed at. The two
        /// are not interchangeable: an empty NAMED slot never needs a swap even when every OTHER
        /// slot is full.
        /// </para>
        /// </summary>
        public bool CanTakeToHotbar(PackSurfaceId surfaceId, Vector2 uv, IPlayerInventory hotbar,
                                    out bool refused, int targetSlot = -1)
        {
            refused = false;

            if (hotbar == null) return false;
            if (!TryFindAt(surfaceId, uv, out PackPlacement placement)) return false;

            InventoryItem packItem = ItemFor(placement.ItemId);
            if (packItem == null) return false;

            InventoryItem held;

            if (targetSlot >= 0)
            {
                if (targetSlot >= hotbar.GetInventorySize()) return false;

                InventorySlot slot = hotbar.GetSlot(targetSlot);
                held = slot != null && !slot.IsEmpty ? slot.Item : null;

                // An empty NAMED slot needs no swap at all — the take writes straight into it, even
                // when every OTHER slot on the bar is full.
                if (held == null) return true;

                // The same asset already sitting where it would land: refused outright, ahead of
                // any room test, because the layout is keyed by id and no spot would ever be
                // approved for it.
                if (held.ID == packItem.ID)
                {
                    refused = true;
                    return false;
                }
            }
            else
            {
                if (HasEmptySlot(hotbar)) return true;

                // The slot the swap will use, chosen exactly as TrySwapWithHotbar chooses it.
                int target = hotbar.SelectedSlotIndex;
                if (target < 0 || target >= hotbar.GetInventorySize()) target = 0;

                InventorySlot heldSlot = hotbar.GetSlot(target);
                held = heldSlot != null && !heldSlot.IsEmpty ? heldSlot.Item : null;
            }

            if (held == null || string.IsNullOrEmpty(held.ID)) return false;

            PackSurface surface = SurfaceFor(placement.Surface);
            if (surface == null) return false;

            PackShape heldShape = PackShapes.For(held, shapes);
            bool mayTurnHeld = PackShapes.AllowsRotation(held, shapes);

            // The outgoing item ignored in both tests, because the space it is vacating is exactly
            // the space the incoming one is being offered — the same trick, for the same reason,
            // that both swap paths use to avoid mutating the layout to ask the question.
            bool aimReaches = targetSlot < 0 || Reaches(placement.Surface);

            // Whether this FACE will have the displaced item at all, asked once. A reserved face
            // is a socket: the bottle can come out of it, but whatever the player was holding
            // cannot go in. Only the same-face attempts below are gated — the cross-face fallback
            // is TryArrange's, which asks every face this same question for itself.
            bool faceTakesHeld = surface.AcceptsItem(held);

            if (aimReaches && faceTakesHeld
                && Layout.CanPlace(placement.Surface, surface.Size, heldShape,
                                   placement.Uv, placement.Yaw, placement.ItemId))
                return true;

            if (targetSlot < 0)
            {
                // TrySwapWithHotbar's own fallback: the same face, second try, nowhere else.
                if (faceTakesHeld
                    && Layout.TryFindSpot(placement.Surface, surface.Size, heldShape,
                                          out _, out _, placement.ItemId, mayTurnHeld)) return true;
            }
            else
            {
                // The named-slot fallback: first-fit across EVERY reachable surface, not just this
                // one. Predicted against the same reachable set for the same reason as everywhere
                // else here: predicting anything narrower is a prediction that can say yes to a
                // drop the server then refuses.
                IReadOnlyList<PackSurface> all = ReachableSurfaces();

                for (int i = 0; i < all.Count; i++)
                {
                    PackSurface candidate = all[i];
                    if (candidate == null) continue;

                    // The same question TryArrange will ask of each face when this actually runs.
                    // Geometry alone is not the whole test: a face can refuse an item it has room
                    // for — because the face is reserved, or because the ITEM is confined to other
                    // faces (ItemGrip.ConfinedToSurfaces) — and a prediction that skips it approves
                    // a take whose displaced item then has nowhere to land.
                    if (!candidate.AcceptsItem(held)) continue;

                    if (Layout.TryFindSpot(candidate.Id, candidate.Size, heldShape,
                                           out _, out _, placement.ItemId, mayTurnHeld)) return true;
                }
            }

            refused = true;
            return false;
        }

        /// <summary>
        /// Refuses — loudly — any transfer toward a hotbar of an item the registry cannot resolve.
        ///
        /// <para>
        /// This container resolves items through its own references first (<see cref="ItemFor"/>),
        /// so it can hold, draw and hand over an asset the registry never loaded — one authored
        /// from outside <c>Assets/Game/Resources/Items</c>. The hotbar has no such cache: it
        /// stores nothing but the <c>ID</c> and resolves every read through
        /// <c>Registry&lt;InventoryItem&gt;</c>, so handing such an item over "succeeds" and then
        /// every read of the slot resolves null — gone from the mat, never in the bar. That is
        /// wrong authored data, so the answer is an error naming the asset and a refusal that
        /// leaves the item where the player can still see it. <c>PackStartingItemTests</c> sweeps
        /// shipped prefabs for exactly this.
        /// </para>
        /// </summary>
        public static bool HotbarCanResolve(InventoryItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.ID)) return false;
            if (Registry<InventoryItem>.Get(item.ID) != null) return true;

            // No registry at all means nowhere the ID round trip can lose the item: an EditMode
            // test or a bare scene has no RegistryLoader, and its hotbars are plain
            // PlayerInventory objects holding direct references. Only a POPULATED registry that
            // cannot answer for this ID is the authored-data fault this refuses.
            using (IEnumerator<InventoryItem> entries = Registry<InventoryItem>.All.GetEnumerator())
                if (!entries.MoveNext()) return true;

            Debug.LogError($"[Pack] Refused to hand '{item.name}' (ID {item.ID}) to a hotbar: the " +
                           "item registry cannot resolve that ID, so the slot would read as empty " +
                           "and the item would be lost. The item asset must live under " +
                           "Assets/Game/Resources/Items.", item);
            return false;
        }

        private static bool HasEmptySlot(IPlayerInventory hotbar)
        {
            for (int i = 0; i < hotbar.GetInventorySize(); i++)
            {
                InventorySlot slot = hotbar.GetSlot(i);
                if (slot == null || slot.IsEmpty) return true;
            }

            return false;
        }

        /// <summary>
        /// The full-hotbar path. Only ever called once TryAddItem has already refused, which is the
        /// proof that every hotbar slot is occupied — and that is what makes the middle of this
        /// safe: clearing one slot leaves EXACTLY one empty, so the following TryAddItem can only
        /// land in the slot just cleared. No IPlayerInventory member has to be added for it.
        /// </summary>
        private bool TrySwapWithHotbar(PackPlacement placement, InventoryItem packItem,
                                       IPlayerInventory hotbar)
        {
            // Nothing selected still has to do something. The player is aiming at an item and
            // pressing interact; "nothing happened" is the exact failure this method exists to
            // remove, so the swap falls back to the first slot rather than refusing.
            int target = hotbar.SelectedSlotIndex;
            if (target < 0 || target >= hotbar.GetInventorySize()) target = 0;

            InventorySlot heldSlot = hotbar.GetSlot(target);
            InventoryItem held = heldSlot != null && !heldSlot.IsEmpty ? heldSlot.Item : null;

            // Unreachable on a genuinely full hotbar, but a rogue IPlayerInventory that refuses adds
            // for its own reasons would otherwise have its item destroyed by the take below.
            if (held == null || string.IsNullOrEmpty(held.ID)) return false;

            PackSurface surface = SurfaceFor(placement.Surface);
            if (surface == null) return false;

            // Where the displaced item is going, settled BEFORE anything moves. Under fixed slots
            // this was free — the pocket the item vacated was exactly the right shape. It is not
            // free any more: a 1.35 m staff coming out does not leave a canister-shaped hole.
            //
            // Ignoring the outgoing item is what lets the incoming one be offered the space the
            // outgoing one still occupies, without the layout having to be mutated first and put
            // back on failure — which on the server would publish a phantom state to every client.
            PackShape heldShape = PackShapes.For(held, shapes);
            Vector2 uv = placement.Uv;
            float yaw = placement.Yaw;

            // The same gate the prediction above applies, and it has to be the same or a green
            // preview turns into a press that does nothing.
            bool faceTakesHeld = surface.AcceptsItem(held);

            bool inPlace = faceTakesHeld
                           && Layout.CanPlace(placement.Surface, surface.Size, heldShape, uv, yaw,
                                              placement.ItemId);

            // Same spot first, because that is where the player is looking. Anywhere on the same
            // face second. A swap that silently moved gear onto a different panel would read as the
            // container shuffling its own contents.
            if (!inPlace && (!faceTakesHeld
                             || !Layout.TryFindSpot(placement.Surface, surface.Size, heldShape,
                                                    out uv, out yaw, placement.ItemId,
                                                    PackShapes.AllowsRotation(held, shapes))))
                return false;

            // Clears the slot. It does NOT spawn a world pickup — that is PlayerInventory.DropItem,
            // a different method — so nothing can leak onto the ground mid-swap.
            if (!hotbar.TryRemoveItem(target)) return false;

            if (!hotbar.TryAddItem(packItem))
            {
                // Cannot happen once a slot has been cleared, but leaving the hotbar a slot short
                // would be a silent item loss, so put the held item back and abandon the swap.
                hotbar.TryAddItem(held);
                if (hotbar.SelectedSlotIndex != target) hotbar.SelectSlot(target);
                return false;
            }

            TakeOut(placement.ItemId);

            // Cannot fail: the spot was tested with the outgoing item ignored, and the outgoing
            // item is what has just left. Reported rather than assumed anyway, because the failure
            // would be a silently destroyed item — the one outcome this whole method is written to
            // avoid, and one that no assertion downstream would catch.
            if (!TryPlace(held, placement.Surface, uv, yaw))
            {
                Debug.LogError($"{GetType().Name}: the swap proved '{held.itemName}' fits at {uv} " +
                               $"on {placement.Surface} and then could not place it. The item is lost.",
                               this);
            }

            // Required, not cosmetic. PlayerInventory.TryRemoveItem nulls SelectedSlotIndex when it
            // removes the selected slot, so without this the player finishes the swap holding
            // nothing while the item they just took sits unselected in their hand slot.
            //
            // Guarded on the selection having actually moved, because the networked hotbar does NOT
            // clear it — and PlayerInventoryNetwork.SelectSlot is a TOGGLE, so re-selecting a slot
            // that is already selected deselects it.
            if (hotbar.SelectedSlotIndex != target) hotbar.SelectSlot(target);

            return true;
        }

        // ------------------------------------------------------------------ display

        /// <summary>
        /// Stop drawing one placed item because it has been lifted into a hand on this machine, or
        /// pass null to draw everything again. See <see cref="inHandItemId"/>.
        /// </summary>
        public void SetInHand(string itemId)
        {
            if (inHandItemId == itemId) return;

            string released = inHandItemId;
            inHandItemId = itemId;

            ShowPlaced(released, true);
            ShowPlaced(inHandItemId, false);
        }

        /// <summary>
        /// Show or hide everything drawn for one placed item. Silent about ids it does not know: an
        /// item taken out from under a carry has no display left to restore, and the hand asks for
        /// exactly that on its way to letting go.
        /// </summary>
        private void ShowPlaced(string itemId, bool shown)
        {
            if (string.IsNullOrEmpty(itemId)) return;

            if (visuals.TryGetValue(itemId, out GameObject visual) && visual != null)
                visual.SetActive(shown);

            if (holderVisuals.TryGetValue(itemId, out GameObject holder) && holder != null)
                holder.SetActive(shown);

            if (gridVisuals.TryGetValue(itemId, out GameObject grid) && grid != null)
                grid.SetActive(shown);

            if (strapVisuals.TryGetValue(itemId, out PackStrapVisual.Handle straps) && straps.Object != null)
                straps.Object.SetActive(shown);
        }

        /// <summary>
        /// Tear the whole display down and build it again.
        ///
        /// <see cref="PackLayout.OnChanged"/> is one coarse event with no index in it, on purpose:
        /// a placement can move between faces, so "slot 7 changed" is not a thing that can be said.
        /// A container holds a handful of items, so rebuilding all of them is cheaper than the
        /// bookkeeping that would let it rebuild one.
        /// </summary>
        protected void RebuildVisuals()
        {
            if (rebuilding) return;

            foreach (GameObject visual in visuals.Values)
                if (visual != null) Destroy(visual);

            visuals.Clear();

            // Torn down with the items, in the same pass. A holder outliving the item it covers is
            // a strap cinched around nothing, and it would go on cinching nothing for the rest of
            // the session — nothing else ever looks at these objects again.
            foreach (GameObject holder in holderVisuals.Values)
                if (holder != null) Destroy(holder);

            holderVisuals.Clear();

            // Same pass again, and for the same reason: a lattice outliving the item it measures
            // would be a rectangle of cells drawn around nothing.
            foreach (GameObject grid in gridVisuals.Values)
                if (grid != null) Destroy(grid);

            gridVisuals.Clear();

            // Same pass once more, but through PackStrapVisual.Destroy: a band's mesh is built
            // fresh per placement, so destroying only the GameObject would strand one mesh per
            // rebuild for the rest of the session.
            foreach (PackStrapVisual.Handle straps in strapVisuals.Values)
                PackStrapVisual.Destroy(straps);

            strapVisuals.Clear();

            // Resolved once. Unwired, this walks the whole hierarchy, and asking per item would
            // walk it once per placed item on every change.
            IReadOnlyList<PackSurface> all = ResolvedSurfaces();

            foreach (PackPlacement placement in Layout.Placements)
            {
                InventoryItem item = ItemFor(placement.ItemId);
                if (item == null || item.itemPrefab == null) continue;

                PackSurface surface = null;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null && all[i].Id == placement.Surface) { surface = all[i]; break; }

                if (surface == null)
                {
                    Debug.LogWarning($"{GetType().Name}: nothing here answers to surface " +
                                     $"{placement.Surface}, so '{item.itemName}' is held but not shown.",
                                     this);
                    continue;
                }

                GameObject visual = BackpackItemVisual.Build(
                    item.itemPrefab, surface, placement.Uv, placement.Yaw);

                if (visual == null) continue;

                visuals[placement.ItemId] = visual;

                // Null all the way down when there is no library or no art for this item's shape.
                // A missing holder is cosmetic and must never cost the item its display copy.
                GameObject holder = HolderBuilder.Build(
                    holders, item.itemPrefab, surface, placement.Uv, placement.Yaw);

                if (holder != null) holderVisuals[placement.ItemId] = holder;

                // The cells this item is on, drawn around it. Not a highlight of the face — the
                // item's own mask, which is the only way an L-shaped item can show the player
                // which corner it actually left free.
                if (!Layout.TryOccupancy(placement.ItemId, out _, out Vector2Int origin,
                                         out PackShape oriented))
                    continue;

                GameObject grid = PackGridVisual.BuildPlaced(surface, origin, oriented);

                if (grid != null) gridVisuals[placement.ItemId] = grid;

                // The bands lashing the item down: one per grid line crossing its footprint,
                // wrapped over the silhouette the display copy actually renders — which is why the
                // copy is the argument, and why this runs only after it is fully seated.
                PackStrapVisual.Handle straps = PackStrapVisual.Build(surface, origin, oriented, visual);

                if (!straps.IsEmpty) strapVisuals[placement.ItemId] = straps;
            }

            // Straight back out of sight, because everything above was built afresh and knows
            // nothing of the carry. Last, after the straps: PackStrapVisual samples the display
            // copy's silhouette, and it can only sample a copy that is still there to sample.
            ShowPlaced(inHandItemId, false);

            OnVisualsRebuilt();
        }
    }
}
