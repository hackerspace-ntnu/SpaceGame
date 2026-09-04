using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// One place on the body where worn gear lives, as the body screen shows it: a gauntlet site
    /// on each forearm and one on the trunk.
    ///
    /// <para>
    /// A site is anchored to <b>the same transform the worn item uses</b> — the forearm bone with
    /// the hand's <see cref="EquipItemSocket"/> for its thumb side, a trunk bone with the prefab's
    /// <see cref="WornFit"/> for the torso — so a ghost sits exactly where the real thing will. Its
    /// ghosts are <see cref="DisplayCopy"/> copies (no scripts, no physics) drawn with one
    /// translucent <see cref="TintMaterials"/> material each.
    /// </para>
    /// <para>
    /// The torso site has <b>two</b> such places and one slot: back gear on the spine, chest gear on
    /// the chest, chosen by the kind of whatever the site is currently about (see
    /// <c>PlaceFor</c>). <b>All three sites signal the same way</b>: a translucent ghost of what
    /// goes there, on the spot it goes. The torso used to be the exception — it outlined the
    /// expedition rig's real lash rail instead — and that was the weakest signifier of the three,
    /// because the rail is a thin bar buried in the rig's rack and it reads as "a bit of your
    /// luggage is highlighted" rather than as a slot. The rail is still where a back item is
    /// SEATED; it is no longer what lights up.
    /// </para>
    /// <para>
    /// What it shows is decided elsewhere (<see cref="BodySiteState.Resolve"/>) and handed in
    /// through <see cref="Apply"/>. This class only knows how each state LOOKS. Everything it
    /// creates is local to this machine and dies with <see cref="Dispose"/>.
    /// </para>
    /// </summary>
    public sealed class BodySite
    {
        // ── The look of each state. The alphas are what make a ghost a ghost. ──
        private static readonly Color PlaceholderBody = WithAlpha(UITheme.Accent, 0.22f);
        private static readonly Color PlaceholderHover = WithAlpha(UITheme.Accent, 0.35f);
        private static readonly Color PlaceholderOutline = WithAlpha(UITheme.Accent, 0.7f);
        private static readonly Color PreviewBody = WithAlpha(HotbarStyle.Amber, 0.55f);
        private static readonly Color PreviewHover = WithAlpha(HotbarStyle.Amber, 0.8f);
        private static readonly Color PreviewOutline = WithAlpha(HotbarStyle.Amber, 0.9f);
        private static readonly Color CommitBody = WithAlpha(HotbarStyle.Amber, 0.9f);
        private static readonly Color RefusedBody = WithAlpha(UITheme.Danger, 0.45f);
        private static readonly Color ReservedBody = WithAlpha(UITheme.Muted, 0.30f);

        // ── Feel ──
        private const float PopSeconds = 0.15f;
        private const float PopScale = 1.06f;
        private const float ShakeSeconds = 0.25f;
        private const float ShakeMetres = 0.006f;
        private const float ShakeFrequency = 55f;

        /// <summary>
        /// How much wider than a resting rim an emphasised one is drawn: the site under the cursor,
        /// and the flick a refused click gives. A weight rather than a width, because
        /// <see cref="OutlineShell"/> earns the width from the traced item's own size.
        /// </summary>
        private const float EmphasisWeight = 1.3f;

        /// <summary>
        /// One site's three rim materials.
        ///
        /// <para>
        /// <b>Per site, never shared between them.</b> <see cref="OutlineShell.Build"/> writes
        /// <c>_OutlineWidth</c> onto the material it is handed — the width is per visual, computed
        /// from that visual's own size — so two sites tracing shells from ONE material would fight
        /// over it and both shells would render at whichever width was written last. Three tiny
        /// materials per site is the price of each site's outline being its own.
        /// </para>
        /// </summary>
        public sealed class Palette : IDisposable
        {
            public readonly Material SwapRim = TintMaterials.Rim("BodySwapRim", HotbarStyle.Amber, OutlineShell.MinOutlineWidth);
            public readonly Material HoverRim = TintMaterials.Rim("BodyHoverRim", new Color(1f, 0.92f, 0.6f, 1f), OutlineShell.MinOutlineWidth);
            public readonly Material RefusedRim = TintMaterials.Rim("BodyRefusedRim", new Color(1f, 0.42f, 0.36f, 1f), OutlineShell.MinOutlineWidth);

            public void Dispose()
            {
                UnityEngine.Object.Destroy(SwapRim);
                UnityEngine.Object.Destroy(HoverRim);
                UnityEngine.Object.Destroy(RefusedRim);
            }
        }

        /// <summary>A ghost copy and what it needs to be shaken and popped back to rest.</summary>
        private sealed class Ghost
        {
            public GameObject Go;
            public Material Tint;
            public Vector3 RestPosition;
            public Vector3 RestScale;
            public GameObject Of;   // the prefab this is a copy of, so a changed one is rebuilt
        }

        public BodySlot Slot { get; }
        public SiteState State { get; private set; }

        /// <summary>
        /// Which of the torso's two places this site is currently about, for a caller that has to
        /// name it. Meaningless for a gauntlet site, which has one place and always answers `Back`.
        /// </summary>
        public EquipKind Place => place;

        private readonly BodyEquipmentController body;
        private readonly EquipItemSocket socket;   // gauntlets: the hand, for its thumb side; null for the torso
        private readonly Transform forearm;        // gauntlets: the bone the device is strapped to; null for the torso
        private readonly Transform spine;          // the torso's BACK place; null for gauntlets
        private readonly Transform chest;          // the torso's CHEST place; null for gauntlets, and on a rig with no chest bone
        private readonly GameObject placeholderPrefab;

        /// <summary>This site's own rim materials — see <see cref="Palette"/> for why they are not shared.</summary>
        private readonly Palette palette;

        private Ghost placeholder;
        private Ghost preview;
        private readonly List<GameObject> shell = new();

        private GameObject hiddenWorn;
        private readonly List<Renderer> hidden = new();

        private InventoryItem lastCarried;

        /// <summary>Both <see cref="Apply"/> arguments a redraw after a refusal has to reproduce.</summary>
        private EquipKind? lastWornKind;

        /// <summary>Which of the torso's two places the last <see cref="Apply"/> was about. Meaningless for a gauntlet site.</summary>
        private EquipKind place = EquipKind.Back;

        private bool hovered;
        private float popUntil;
        private float shakeUntil;
        private bool animating;

        public BodySite(BodySlot slot, BodyEquipmentController body, EquipItemSocket socket, Transform forearm,
                        Transform spine, Transform chest, GameObject placeholderPrefab)
        {
            Slot = slot;
            this.body = body;
            this.socket = socket;
            this.forearm = forearm;
            this.spine = spine;
            this.chest = chest;
            this.placeholderPrefab = placeholderPrefab;
            palette = new Palette();
        }

        /// <summary>Is there anywhere to seat a ghost? False on a rig with no such bone.</summary>
        public bool HasAnchor => Slot == BodySlot.Torso ? spine != null : (forearm != null && socket != null);

        /// <summary>
        /// Which of the torso's two places this site is pointing at right now.
        ///
        /// <para>
        /// The carry wins over what is worn, because the carry is what the player is asking a
        /// question about: lift a chest device off the rail and the site should already be offering
        /// the chest. With nothing carried it follows the worn item, and with neither it rests on
        /// the back — the default home, and the one the lash rail is standing there advertising.
        /// </para>
        /// </summary>
        private EquipKind PlaceFor(InventoryItem carried, EquipKind? wornKind) =>
            carried != null && BodySlotRules.Accepts(Slot, carried.equipKind) ? carried.equipKind
            : wornKind ?? EquipKind.Back;

        // ── State ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Show <paramref name="state"/>. <paramref name="carried"/> is what the cursor holds, for
        /// previews; <paramref name="wornKind"/> is the kind of whatever is already in this slot,
        /// which together decide which of the torso's two places this site is currently about.
        /// </summary>
        public void Apply(SiteState state, InventoryItem carried, bool isHovered, EquipKind? wornKind)
        {
            State = state;
            lastCarried = carried;
            lastWornKind = wornKind;
            hovered = isHovered;
            place = PlaceFor(carried, wornKind);

            GameObject worn = body.WornInstance(Slot);
            bool reserve = state == SiteState.Reserved;

            SetWornHidden(worn, reserve);

            // A swap normally says everything with an amber outline on what is already there. On the
            // torso it cannot, when the swap MOVES the gear between the two places: they are on
            // opposite sides of the body, so the outline is behind the player at the moment the lens
            // is in front of them (and the other way round). The preview at the new place is the
            // half that has to be visible, so a crossing swap draws both.
            bool crossesPlace = Slot == BodySlot.Torso && state == SiteState.SwapOutline
                                && wornKind.HasValue && carried != null
                                && carried.equipKind != wornKind.Value;

            bool showPreview = state is SiteState.Preview or SiteState.Committing || crossesPlace;
            bool wantsAnchor = state is SiteState.Empty or SiteState.Reserved
                               || (state == SiteState.Refused && worn == null);

            // Every site shows its stand-in the same way: while nothing is carried and this slot
            // is empty, and out of the way the moment a preview of a real item takes its place.
            bool showPlaceholder = wantsAnchor;

            if (showPreview) EnsurePreview(carried);
            else DestroyGhost(ref preview);

            if (showPlaceholder) EnsurePlaceholder(carried);
            else if (Alive(placeholder)) placeholder.Go.SetActive(false);

            OutlineShell.Clear(shell);

            // The rim belongs to the WORN item, and to nothing else: an empty site says what it
            // is with its translucent ghost, which paints its own edge through TintMaterials
            // rather than through a shell. Two rims for one slot is what this ordering prevents.
            if (worn != null && !reserve)
            {
                Material rim = state switch
                {
                    SiteState.SwapOutline => palette.SwapRim,
                    SiteState.Committing => palette.SwapRim,
                    SiteState.Refused => palette.RefusedRim,
                    SiteState.Worn when hovered => palette.HoverRim,
                    _ => null,
                };
                if (rim != null) OutlineShell.Build(worn, rim, hovered ? EmphasisWeight : 1f, shell);
            }

            Recolour();
        }

        /// <summary>A legal click was sent. Brighten and pop until the answer redraws us.</summary>
        public void Commit()
        {
            State = SiteState.Committing;
            popUntil = Time.unscaledTime + PopSeconds;
            animating = true;
            Recolour();
        }

        /// <summary>A refused click: a red flick and a shake, then back to whatever we were showing.</summary>
        public void Refuse()
        {
            shakeUntil = Time.unscaledTime + ShakeSeconds;
            animating = true;

            // Only the worn item gets a red rim. An empty site has nothing of its own to outline
            // — its ghost is the thing that turns red, through Recolour below.
            GameObject worn = body.WornInstance(Slot);
            if (worn != null && State != SiteState.Reserved)
                OutlineShell.Build(worn, palette.RefusedRim, EmphasisWeight, shell);

            Recolour();
        }

        /// <summary>Drive the pop and the shake. Call once a frame while the screen is up.</summary>
        public void Tick()
        {
            if (!animating) return;

            float now = Time.unscaledTime;
            Ghost ghost = Showing(preview) ? preview : Showing(placeholder) ? placeholder : null;

            bool shaking = now < shakeUntil;
            bool popping = now < popUntil;

            if (ghost != null)
            {
                Transform t = ghost.Go.transform;

                // Along the ghost's own X, in world metres: the parent is a bone whose scale is not 1.
                float parentScale = Mathf.Max(1e-4f, t.parent != null ? t.parent.lossyScale.x : 1f);
                float jitter = shaking
                    ? Mathf.Sin(now * ShakeFrequency) * ShakeMetres * ((shakeUntil - now) / ShakeSeconds) / parentScale
                    : 0f;
                t.localPosition = ghost.RestPosition + new Vector3(jitter, 0f, 0f);

                float pop = popping ? Mathf.Lerp(PopScale, 1f, 1f - (popUntil - now) / PopSeconds) : 1f;
                t.localScale = ghost.RestScale * pop;
            }

            if (!shaking && !popping)
            {
                animating = false;
                // The flash is over: draw the state we were in before the refusal again.
                if (State != SiteState.Committing) Apply(State, lastCarried, hovered, lastWornKind);
            }
            else
            {
                Recolour();
            }
        }

        // ── Screen space ──────────────────────────────────────────────────────

        /// <summary>
        /// What this site is showing right now, in the same order <see cref="Apply"/> decides what
        /// to draw — so the box the cursor is tested against, and the distance it is judged by, are
        /// always about the thing the player can currently see.
        /// </summary>
        private GameObject Visual => Showing(preview) ? preview.Go
            : Showing(placeholder) ? placeholder.Go
            : body.WornInstance(Slot);

        /// <summary>
        /// How far what this site is showing is from <paramref name="point"/> — the lens, when the
        /// screen is deciding which of two overlapping sites a click belongs to. Infinity when the
        /// site is showing nothing, which keeps it out of that comparison entirely.
        ///
        /// <para>
        /// Measured to the centre of the visual's bounds rather than to the nearest face. A worn
        /// wing is metres across and its near corner reaches past the arm in front of it, so the
        /// nearest point would hand the click back to the very item this distance exists to rank
        /// behind; a centre says where the thing actually IS.
        /// </para>
        /// </summary>
        public float DistanceFrom(Vector3 point)
        {
            GameObject visual = Visual;
            if (visual == null) return float.PositiveInfinity;

            bool any = false;
            Bounds bounds = default;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled || renderer.gameObject.name == OutlineShell.ShellName) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            return any ? Vector3.Distance(bounds.center, point) : float.PositiveInfinity;
        }

        /// <summary>
        /// Where this site is on the overlay, in canvas pixels: the projected box of whatever it is
        /// currently showing, padded. False when nothing is showing or it is behind the lens.
        ///
        /// <para>
        /// The hit test is done here, in screen space, on purpose. Three sites do not justify
        /// colliders, and a trigger anywhere near the player's hierarchy or on a gameplay layer is a
        /// thing the movement probes, the scanner and other players' rays can hit.
        /// </para>
        /// <para>
        /// Disabled renderers are skipped, which is what makes the <see cref="SiteState.Reserved"/>
        /// rect the PLACEHOLDER'S: the worn item is hidden by switching its renderers off, and a
        /// rect measured off the thing the player can no longer see would take clicks meant for the
        /// ghost standing in its place.
        /// </para>
        /// </summary>
        public bool TryCanvasRect(WorldOverlay overlay, float padding, out Rect rect)
        {
            rect = default;
            if (overlay == null) return false;

            GameObject visual = Visual;
            if (visual == null) return false;

            bool any = false;
            Vector2 min = Vector2.positiveInfinity;
            Vector2 max = Vector2.negativeInfinity;

            foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null || !renderer.enabled || renderer.gameObject.name == OutlineShell.ShellName) continue;

                Bounds b = renderer.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                             (i & 2) == 0 ? b.min.y : b.max.y,
                                             (i & 4) == 0 ? b.min.z : b.max.z);
                    // A corner behind the lens is skipped, not fatal. Renderer.bounds is a world
                    // AABB and so is generously larger than the item; with the camera pulled in
                    // against a wall, one corner of it can fall behind the near plane while the
                    // site is still plainly on screen. Failing the whole site there would make it
                    // silently un-clickable — including the site a carried item came FROM, which
                    // would strand the carry. The rect built from the corners that do project is
                    // smaller than the true one, which costs a little slack at the edges and never
                    // puts the site somewhere it is not.
                    if (!overlay.Project(corner, out Vector2 p)) continue;
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                    any = true;
                }
            }

            if (!any) return false;
            rect = Rect.MinMaxRect(min.x - padding, min.y - padding, max.x + padding, max.y + padding);
            return true;
        }

        // ── Teardown ──────────────────────────────────────────────────────────

        /// <summary>
        /// Destroy every ghost, un-hide the worn item, clear the shells. Safe to call twice, and
        /// the end of this site: the palette it repaints through is gone afterwards, so a session
        /// that closes and opens again builds new sites rather than reusing these.
        /// </summary>
        public void Dispose()
        {
            RestoreWorn();
            OutlineShell.Clear(shell);
            DestroyGhost(ref placeholder);
            DestroyGhost(ref preview);
            palette.Dispose();
        }

        // ── Ghosts ────────────────────────────────────────────────────────────

        /// <summary>
        /// The stand-in for an empty place.
        ///
        /// <para>
        /// For the back it is the authored mount frame, seated on the pack's lash rail by the same
        /// call that wears real gear — so it stands over the shoulders, where a back item goes and
        /// where the front-on lens can see it. The chest has no authored ghost at all and
        /// deliberately so: the only way to
        /// reach an empty chest is to be holding a chest item, and a dim copy of that item sitting
        /// where it would go says more than a generic plate would — so it needs no model of its
        /// own. That is why this resolves per call rather than being fixed at construction.
        /// </para>
        /// </summary>
        private GameObject PlaceholderPrefab(InventoryItem carried) =>
            Slot == BodySlot.Torso && place == EquipKind.Chest
                ? (carried != null ? carried.itemPrefab : null)
                : placeholderPrefab;

        private void EnsurePlaceholder(InventoryItem carried)
        {
            GameObject prefab = PlaceholderPrefab(carried);
            if (prefab == null) { DestroyGhost(ref placeholder); return; }

            // A ghost hangs off a bone in the player's rig, so a rig that went away took it with
            // it. Drop the husk rather than calling into it; the rebuild below either succeeds on
            // whatever is there now or leaves the site drawing nothing. A ghost of the WRONG prefab
            // is dropped the same way — the torso's stand-in changes with the place it is standing
            // in for, the way the preview changes with what the cursor picks up.
            if (placeholder != null && (!Alive(placeholder) || placeholder.Of != prefab))
                DestroyGhost(ref placeholder);

            if (placeholder == null)
            {
                placeholder = MakeGhost(prefab, "BodyGhost_" + Slot, PlaceholderBody, PlaceholderOutline);
                if (placeholder == null) return;
                placeholder.Of = prefab;
            }

            placeholder.Go.SetActive(true);
        }

        private void EnsurePreview(InventoryItem carried)
        {
            GameObject prefab = carried != null ? carried.itemPrefab : null;
            if (prefab == null) { DestroyGhost(ref preview); return; }

            if (Alive(preview) && preview.Of == prefab) return;

            DestroyGhost(ref preview);
            preview = MakeGhost(prefab, "BodyPreview_" + Slot, PreviewBody, PreviewOutline);
            if (preview != null) preview.Of = prefab;
        }

        /// <summary>
        /// A stripped copy of <paramref name="prefab"/>, seated the way the real item is worn and
        /// repainted with one translucent material.
        ///
        /// <para>
        /// Seated by the very call <see cref="BodyEquipmentController"/> wears the real thing with,
        /// down to reading the fit off the PREFAB: a display copy has had every MonoBehaviour taken
        /// off it, so asking the copy for its <see cref="WornFit"/> would answer null and seat the
        /// ghost at the bone, at the wrong size, promising a place the gear does not land.
        /// </para>
        /// <para>
        /// The copy arrives at unit scale rather than the prefab's own — <see cref="DisplayCopy"/>
        /// normalises it — which the seating then overwrites: always for a gauntlet, and for a torso
        /// item whenever its fit names a size. A torso prefab carrying no fit at all is already
        /// documented as unwearable, so the one case where the two could differ is one nothing
        /// ships.
        /// </para>
        /// </summary>
        private Ghost MakeGhost(GameObject prefab, string name, Color bodyColour, Color outlineColour)
        {
            if (prefab == null || !HasAnchor) return null;

            // The same call the controller wears the real thing with, so a ghost of a chest device
            // cannot end up on the spine while the device itself lands on the sternum.
            Transform trunk = WornSeat.BoneFor(place, spine, chest);
            Transform anchor = Slot == BodySlot.Torso ? trunk : forearm;
            GameObject copy = DisplayCopy.Make(prefab, anchor);
            if (copy == null) return null;
            copy.name = name;

            if (Slot == BodySlot.Torso)
                WornSeat.Apply(copy, trunk, prefab.GetComponent<WornFit>(), body.TorsoMount(place));
            else ForearmSeat.Apply(copy, forearm, socket.Socket, socket.GripRotation,
                                   Slot == BodySlot.LeftGauntlet, prefab.GetComponent<GauntletFit>());

            Material tint = TintMaterials.Translucent(name, bodyColour, outlineColour, OutlineShell.WidthFor(copy, 1f));

            foreach (Renderer renderer in copy.GetComponentsInChildren<Renderer>(true))
            {
                var materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = tint;
                renderer.sharedMaterials = materials;

                // A ghost is a promise, not an object: it must not darken the arm it is drawn over
                // or throw a second shadow of gear the player is not wearing yet. It cannot be hit
                // by anything either — DisplayCopy destroys every collider outright, so no ray,
                // overlap or probe can find it — and it keeps the item's own layer, which is by
                // definition one the player's camera renders, so the lens sees it exactly as it
                // sees the real worn item.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return new Ghost
            {
                Go = copy,
                Tint = tint,
                RestPosition = copy.transform.localPosition,
                RestScale = copy.transform.localScale,
            };
        }

        private static void DestroyGhost(ref Ghost ghost)
        {
            if (ghost == null) return;
            if (ghost.Go != null) UnityEngine.Object.Destroy(ghost.Go);
            if (ghost.Tint != null) UnityEngine.Object.Destroy(ghost.Tint);
            ghost = null;
        }

        /// <summary>Built, and not destroyed under us with the rig it hangs off.</summary>
        private static bool Alive(Ghost ghost) => ghost != null && ghost.Go != null;

        /// <summary>Alive and currently on screen.</summary>
        private static bool Showing(Ghost ghost) => Alive(ghost) && ghost.Go.activeSelf;

        private void Recolour()
        {
            bool flashing = Time.unscaledTime < shakeUntil;

            if (Showing(placeholder))
            {
                Color colour = flashing || State == SiteState.Refused ? RefusedBody
                    : State == SiteState.Reserved ? ReservedBody
                    : hovered ? PlaceholderHover
                    : PlaceholderBody;
                TintMaterials.SetBody(placeholder.Tint, colour);
            }

            if (Showing(preview))
            {
                Color colour = flashing ? RefusedBody
                    : State == SiteState.Committing ? CommitBody
                    : hovered ? PreviewHover
                    : PreviewBody;
                TintMaterials.SetBody(preview.Tint, colour);
            }
        }

        // ── The worn item ─────────────────────────────────────────────────────

        /// <summary>
        /// Hide the worn item while it is being carried, by switching its renderers off. Local only
        /// — peers keep seeing it where it is — and restored on every exit path, because the pack
        /// outlived its hand once and hid an item for good.
        /// </summary>
        private void SetWornHidden(GameObject worn, bool hide)
        {
            // The same instance, still hidden: leave it alone. Everything else restores first —
            // including the case where what we hid has been DESTROYED under us, which a swap on
            // another machine does. Phrased as "is this still the one we hid" rather than "has it
            // changed" precisely for that: a destroyed instance answers null to both sides of a
            // comparison, and the changed-phrasing would take the destroyed one for "nothing was
            // hidden" and strand the dead renderers in the list.
            if (hide && worn != null && hiddenWorn == worn) return;

            RestoreWorn();
            if (!hide || worn == null) return;

            hiddenWorn = worn;
            foreach (Renderer renderer in worn.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled || renderer.gameObject.name == OutlineShell.ShellName) continue;
                renderer.enabled = false;
                hidden.Add(renderer);
            }
        }

        private void RestoreWorn()
        {
            foreach (Renderer renderer in hidden)
                if (renderer != null) renderer.enabled = true;   // destroyed with a slot change: nothing to restore

            hidden.Clear();
            hiddenWorn = null;
        }

        private static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);
    }
}
