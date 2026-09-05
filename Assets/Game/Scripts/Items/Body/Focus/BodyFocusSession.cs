using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything the body screen does in the WORLD: the spawned camera in front of the player and
    /// the three <see cref="BodySite"/>s on their body. <c>BodyInventoryUI</c> is the conductor —
    /// it owns the carry and the hotbar tiles, and it tells this what the cursor holds; this tells
    /// it what the cursor is over and when a site was clicked. The boundary is kept in both
    /// directions: the session never reads a tile, and the UI never touches a renderer or a
    /// transform in the world.
    ///
    /// <para>
    /// Lives on the player prefab (wired by <c>GearGhostBuilder</c>) because the shot and the ghost
    /// prefabs are things to tune in the Inspector, not constants. Like <see cref="PackFocusSession"/>:
    /// nothing pauses, every exit is instant, and every exit path — I, Esc, death, the component
    /// being disabled — comes through <see cref="Exit"/>.
    /// </para>
    /// <para>
    /// Nothing here is sent to anyone. The camera, the ghosts, the hit rects and the hidden
    /// renderers are local to the machine of the player who opened the screen. A move leaves
    /// through <see cref="IBodyEquipment.RequestMove"/>, which already has its own server RPC, so a
    /// peer sees the player standing still exactly as they do today.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyFocusSession : MonoBehaviour
    {
        [Header("The shot")]
        [SerializeField] private BodyFocusCamera.Shot shot = BodyFocusCamera.Shot.Default;

        [Tooltip("Seconds the camera takes to fly back to the eye when the screen closes.")]
        [SerializeField, Min(0f)] private float flyOutSeconds = 0.25f;

        [Header("Look target")]
        [Tooltip("The bone the lens looks at — framing from the thighs up.")]
        [SerializeField] private HumanBodyBones chestBone = HumanBodyBones.Chest;

        [Tooltip("Substring hints for a non-humanoid rig.")]
        [SerializeField] private string[] chestBoneNameHints = { "Chest", "Spine" };

        [Tooltip("With no chest bone at all: this far above the player's origin. The origin is about a metre above the soles.")]
        [SerializeField] private float fallbackLookHeight = 0.4f;

        [Header("Ghosts")]
        [Tooltip("What an empty gauntlet site shows: the plain gauntlet base, the same bracer every " +
                 "real gauntlet is built on. Seated by ForearmSeat from its own GauntletFit, exactly " +
                 "as a worn gauntlet is.")]
        [SerializeField] private GameObject gauntletPlaceholder;

        [Tooltip("The stand-in for the back's mount when there is no pack on the back to point at — " +
                 "a mount frame rising past the shoulders, seated by its WornFit. With the rig " +
                 "shouldered, which is nearly always, its own lash rail is used instead.")]
        [SerializeField] private GameObject backPlaceholder;

        [Header("Feel")]
        [Tooltip("Seconds a sent move stays lit before we assume the server refused it.")]
        [SerializeField, Min(0.1f)] private float commitTimeoutSeconds = 1f;

        [Tooltip("Canvas pixels of slack round a site's projected box for the cursor.")]
        [SerializeField, Min(0f)] private float hitPaddingPx = 12f;

        [Tooltip("Half the width of each hit box on the torso's BACK place, metres — one on each " +
                 "protruding end of the worn pack's lash rail, which is all of that place a click " +
                 "can land on. Raise it and the two boxes reach in towards the arms.")]
        [SerializeField, Min(0.02f)] private float torsoHitMetres = 0.28f;

        [Header("Inspect stance")]
        [Tooltip("Let gear hold the body's arms out — only gear whose WornFit asks for it, which " +
                 "today is the wingsuit alone. Off leaves every item to be looked at in the idle.")]
        [SerializeField] private bool armsOut = true;

        [Tooltip("How far below horizontal each arm hangs, degrees, when gear does ask. 45 is the " +
                 "stance the worn wingsuit's leading edge is authored along — move one and the " +
                 "other has to move with it.")]
        [SerializeField, Range(0f, 60f)] private float armDroop = InspectStance.DefaultDroop;

        [Tooltip("How far in front of the shoulder line each arm reaches, degrees.")]
        [SerializeField, Range(-30f, 30f)] private float armForward;

        /// <summary>The session on screen, if any. At most one, on one machine.</summary>
        public static BodyFocusSession Active { get; private set; }

        public bool IsOpen => Active == this;

        /// <summary>The site under the cursor changed; null is nothing.</summary>
        public event Action<BodySlot?> HoverChanged;

        /// <summary>Left click on a site.</summary>
        public event Action<BodySlot> SiteClicked;

        /// <summary>Left click on the world, over no site and no UI.</summary>
        public event Action NothingClicked;

        /// <summary>
        /// The two forearm sites, which are built identically and differ only in which arm they sit
        /// on. Static, so opening the screen does not allocate a throwaway array to iterate two
        /// enum values.
        /// </summary>
        private static readonly BodySlot[] Gauntlets = { BodySlot.LeftGauntlet, BodySlot.RightGauntlet };

        private PlayerController player;
        private IBodyEquipment slots;
        private BodyEquipmentController worn;

        // Cached: LateUpdate poses the arms off it on every frame the screen is open, and the rig
        // on a player body does not change while they are standing in front of a camera looking
        // at their own gear.
        private Animator animator;

        private BodyFocusCamera focusCamera;
        private Transform lookAnchor;
        private Camera previousEyeOverride;

        private readonly BodySite[] sites = new BodySite[GearRef.BodySlotCount];

        private BodySlot? hovered;

        /// <summary>
        /// A site the UI says the cursor is over, when the cursor is over the UI and not the body.
        /// The gear screen's rail has a tile for each of these three slots, and pointing at one has
        /// to light the site it names — otherwise the rail and the figure are two screens that
        /// happen to share a canvas.
        /// </summary>
        private BodySlot? externalHover;

        private GearRef carried = GearRef.None;
        private InventoryItem carriedItem;

        /// <summary>
        /// Whether the gear on the torso — worn, or on its way there in the cursor — is shaped
        /// along the arms and so needs them held out. Re-read whenever anything about the sites
        /// changes rather than every frame: it is two <c>GetComponent</c> calls, and the answer can
        /// only change when a slot or the carry does.
        /// </summary>
        private bool gearHoldsArmsOut;

        private int committing = -1;
        private float commitDeadline;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            slots = GetComponent<IBodyEquipment>();
            worn = GetComponent<BodyEquipmentController>();
            animator = GetComponentInChildren<Animator>();
        }

        private void OnDisable() => Exit();

        // ── Entering and leaving ─────────────────────────────────────────────

        /// <summary>Takes the world. False when it cannot — another session is up, or this rig has no body slots.</summary>
        public bool Enter()
        {
            if (IsOpen) return true;
            if (Active != null || worn == null || slots == null) return false;

            // A replica of somebody else's body must never open a focus session on this machine.
            // BodyInventoryUI resolves "the local player" through GameplayMenuScope, and in a live
            // session that is the owned player object, so this should be unreachable — but the
            // component sits on every player prefab, including the copies of the other three
            // players standing next to you, and nothing else stands between a stray Enter() and a
            // camera flying to somebody else's chest to rearrange gear the server would refuse to
            // move. The same belt and braces PackFocusSession keeps, for the same reason. Owns
            // answers true for an unnetworked player, which is right: a solo session is a host of
            // one and the only body in it is yours.
            if (!Network.Owns(this)) return false;

            Camera eye = player != null ? player.PlayerCamera : null;

            // Resolved before the spawn because the camera is handed the result, and a rig with no
            // chest bone gets a stand-in GameObject to look at. Torn down again if the spawn is
            // refused, or a failed Enter would leave that stand-in parented under the player with
            // nothing that ever cleans it up: Exit only runs for a session that opened.
            Transform lookTarget = ResolveLookTarget();
            focusCamera = BodyFocusCamera.Spawn(lookTarget, transform.forward, transform, shot, eye);
            if (focusCamera == null)
            {
                DestroyLookAnchor();
                return false;
            }

            Active = this;

            // Stand the body down to a plain idle for the duration. The screen is a camera flown
            // round to look AT the character, so a pose left over from whatever they were doing
            // when they pressed I — arms out around a rifle, a gauntlet arm up, a torch held
            // forward — is the wrong subject in frame. Handed straight back in Exit.
            SetRelaxed(true);

            // And stand the GEAR up, which is the other half of the same idea. The wing pack is
            // stowed out in the world — folded shut so a walking character is not wearing a
            // wingspan — and this screen is the one place a player looks at their own back on
            // purpose, with the camera flown round for it. So here the wings are wings. Every
            // other item has no second model and is unmoved by this.
            //
            // BEFORE the sites are built, and that order is load bearing: a site hides the worn
            // item by switching its renderers off and holding the list, and a swap afterwards
            // would strand that list on the model that is no longer showing — leaving the item
            // invisible for good on this machine, which is the exact failure Exit's comment below
            // records the pack making once.
            worn.SetTorsoForm(WornVisual.Form.Inspected);

            ReportUnwiredPlaceholders();

            // Every anchor comes from the controller's own seams rather than being re-derived
            // here: a ghost has to sit exactly where the real thing will, and the controller is
            // what decides that. It resolved these bones in Start.
            foreach (BodySlot slot in Gauntlets)
                sites[(int)slot] = new BodySite(slot, worn, worn.HandSocket(slot), worn.ForearmBone(slot),
                                                null, null, gauntletPlaceholder, torsoHitMetres);

            sites[(int)BodySlot.Torso] = new BodySite(BodySlot.Torso, worn, null, null,
                                                      worn.BackBone, worn.ChestBone,
                                                      backPlaceholder, torsoHitMetres);

            // Labels project through the lens that is actually rendering — ours, for the duration.
            WorldOverlay overlay = WorldOverlay.Create();
            previousEyeOverride = overlay.EyeOverride;
            overlay.EyeOverride = focusCamera.Camera;

            slots.OnBodySlotChanged += OnSlotChanged;

            hovered = null;
            externalHover = null;
            carried = GearRef.None;
            carriedItem = null;
            committing = -1;

            ApplyAll();
            return true;
        }

        /// <summary>
        /// Take the upper-body pose off, or give it back. Resolved on demand rather than cached:
        /// this component lives on the player prefab and the rig is on the same body, but a teardown
        /// path can reach Exit after the rig has already gone.
        /// </summary>
        private void SetRelaxed(bool relaxed)
        {
            var rig = GetComponentInChildren<SpaceGame.Characters.PlayerAimRig>();
            if (rig != null) rig.Relaxed = relaxed;
        }

        /// <summary>Hands the world back. Safe to call when there is no session, and safe to call twice.</summary>
        public void Exit()
        {
            if (!IsOpen) return;

            Active = null;

            SetRelaxed(false);

            slots.OnBodySlotChanged -= OnSlotChanged;

            // The sites first, and unconditionally. Disposing one un-hides the renderers it
            // switched off and destroys its ghosts, and that has to happen whether or not there is
            // still a lens to see them through — this runs on teardown paths too, where nothing may
            // be moved and the only job left is putting back what was taken. A worn item whose
            // renderers were switched off and never switched back on is invisible for good, on
            // this machine, until the item is re-worn; the pack made exactly that mistake once.
            foreach (BodySite site in sites) site?.Dispose();
            Array.Clear(sites, 0, sites.Length);

            // After the sites, for the mirror of the reason it goes before them in Enter: their
            // Dispose puts back the renderers they switched off, and it has to find them on the
            // model it switched them off on. Unconditional, and safe with nothing worn — this
            // runs on teardown paths too, and gear left spread would wear a five-metre wingspan
            // through the world.
            worn.SetTorsoForm(WornVisual.Form.Worn);

            // Only ours, and only while it still is ours: something else may have claimed the
            // override since, and stamping our predecessor over that would leave a second screen
            // projecting through a lens it never asked for.
            WorldOverlay overlay = WorldOverlay.Instance;
            if (overlay != null && focusCamera != null && overlay.EyeOverride == focusCamera.Camera)
                overlay.EyeOverride = previousEyeOverride;
            previousEyeOverride = null;

            // Handed back before the flight rather than after it. FlyOut takes a quarter of a
            // second and offers no callback at the end, so an override left on for the flight would
            // be stranded on a destroyed camera by any teardown that came in the meantime. World
            // labels simply stop drawing while the lens flies home — the player's own camera stays
            // off until it lands — which is a better failure than labels pinned to a camera that
            // has gone.
            if (focusCamera != null) focusCamera.FlyOut(flyOutSeconds);
            focusCamera = null;

            DestroyLookAnchor();

            hovered = null;
            externalHover = null;
        }

        // ── What the UI tells us ─────────────────────────────────────────────

        /// <summary>The cursor now holds <paramref name="item"/> from <paramref name="from"/> (or nothing). Every site re-resolves.</summary>
        public void SetCarry(GearRef from, InventoryItem item)
        {
            if (!IsOpen) return;

            carried = from;
            carriedItem = item;
            ApplyAll();
        }

        /// <summary>
        /// The cursor is over the rail tile naming <paramref name="slot"/>, or over no tile at all.
        /// Only consulted while the cursor is over the UI, so it can never fight the body's own
        /// hit-test — a cursor on the figure is answered by the figure.
        /// </summary>
        public void SetExternalHover(BodySlot? slot)
        {
            if (!IsOpen) return;

            externalHover = slot;
        }

        /// <summary>A legal move to <paramref name="slot"/> was sent. The site stays lit until the answer or the timeout.</summary>
        public void Commit(BodySlot slot)
        {
            if (!IsOpen) return;

            committing = (int)slot;
            commitDeadline = Time.unscaledTime + commitTimeoutSeconds;
            sites[(int)slot]?.Commit();
        }

        /// <summary>A click on a site the carried item cannot go to.</summary>
        public void Refuse(BodySlot slot)
        {
            // Gated like everything else the UI drives: a refusal that arrived a frame after the
            // screen closed would beep at a player who is already looking at the world again.
            if (!IsOpen) return;

            sites[(int)slot]?.Refuse();
            Sfx.Play2D(SfxId.UiError);
        }

        /// <summary>
        /// Where a site is on the overlay, for the chips and captions. False when it is not showing
        /// — which includes a site the lens has turned its back on, so the Q and E chips leave with
        /// the arms they label rather than floating over the pack.
        /// </summary>
        public bool TryCanvasRect(BodySlot slot, out Rect rect)
        {
            rect = default;
            BodySite site = IsOpen ? sites[(int)slot] : null;
            return site != null && site.TryCanvasRect(WorldOverlay.Instance, hitPaddingPx, out rect);
        }

        public SiteState StateOf(BodySlot slot) =>
            IsOpen && sites[(int)slot] != null ? sites[(int)slot].State : SiteState.Empty;

        /// <summary>
        /// Which of the torso's two places a site is offering right now, so the caption can name it.
        /// Asked of the site rather than re-derived from the carry: one rule decides where a torso
        /// item goes, and a second copy of it here would eventually label the chest "Back".
        /// </summary>
        public EquipKind PlaceOf(BodySlot slot) =>
            IsOpen && sites[(int)slot] != null ? sites[(int)slot].Place : EquipKind.Back;

        // ── Per frame ────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (!IsOpen) return;

            // Death takes the screen rather than the other way round: the death camera and the
            // respawn flow want the player's own lens back, and neither of them asks for it.
            if (player != null && player.IsDead) { Exit(); return; }

            // Before the ghosts are measured and before the hit rects are projected, so what the
            // cursor can click is where the gear is actually drawn. LateUpdate is the only place
            // this can go: the Animator writes the pose between Update and here.
            //
            // Nothing is written on the ordinary frame, and that is the point: the arms hang where
            // the idle puts them, which is how the character stands everywhere else in the game.
            // Only gear authored along the arms takes them over — see WornFit.HoldsArmsOut.
            if (armsOut && gearHoldsArmsOut)
                InspectStance.Apply(animator, transform, armDroop, armForward);

            // The server never answered — a lost race, or a refusal, which announces nothing.
            if (committing >= 0 && Time.unscaledTime > commitDeadline)
            {
                int slot = committing;
                committing = -1;
                ApplyAll();
                sites[slot]?.Refuse();
            }

            // Asked once and shared by the hover and the click. The EventSystem answers both from
            // the same last raycast anyway, and asking twice only invites a frame where the hover
            // believes the cursor is over the world and the click believes it is over a tile.
            bool overUi = PointerOverUi();

            UpdateHover(overUi);

            foreach (BodySite site in sites) site?.Tick();

            if (!overUi && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (hovered.HasValue) SiteClicked?.Invoke(hovered.Value);
                else NothingClicked?.Invoke();
            }
        }

        private void UpdateHover(bool overUi)
        {
            BodySlot? now = overUi ? externalHover : null;

            WorldOverlay overlay = WorldOverlay.Instance;

            // Null camera, deliberately, for the same reason WorldOverlay.Project passes one: the
            // overlay is a Screen Space - Overlay canvas, and handing it the scene camera instead
            // silently returns points scaled wrong.
            if (!overUi && Mouse.current != null && overlay != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                       overlay.Layer, Mouse.current.position.ReadValue(), null, out Vector2 cursor))
            {
                // Where the player is looking from, which is what decides between two boxes that
                // both contain the cursor — see NearestSite.
                Vector3 lens = focusCamera != null && focusCamera.Camera != null
                    ? focusCamera.Camera.transform.position
                    : transform.position;

                // What the cursor is carrying outranks every geometric rule under it: holding a
                // gauntlet the arms win, holding a torso item the torso does. Read off the carried
                // item rather than the site, because it is the same question the move itself will
                // be judged by — BodySlotRules is what answers both.
                EquipKind? carriedKind = carriedItem != null ? carriedItem.equipKind : null;

                var nearest = new NearestSite();

                for (int i = 0; i < sites.Length; i++)
                {
                    if (sites[i] == null
                        || !sites[i].TryCursorHit(overlay, hitPaddingPx, cursor, out float centreSqr)) continue;

                    bool accepts = carriedKind.HasValue && BodySlotRules.Accepts((BodySlot)i, carriedKind.Value);
                    nearest.Offer(i, accepts, sites[i].DistanceFrom(lens), centreSqr);
                }

                if (nearest.Any) now = (BodySlot)nearest.Index;
            }

            if (now == hovered) return;

            hovered = now;
            ApplyAll();
            HoverChanged?.Invoke(hovered);
        }

        private void OnSlotChanged(BodySlot slot, InventorySlot contents)
        {
            // The answer landed — for this site or any other; either way the world moved.
            if ((int)slot == committing) committing = -1;
            ApplyAll();
        }

        private void ApplyAll()
        {
            if (!IsOpen) return;

            gearHoldsArmsOut = HoldsArmsOut(carriedItem) || HoldsArmsOut(ItemIn(BodySlot.Torso));

            EquipKind? carriedKind = carriedItem != null ? carriedItem.equipKind : null;

            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i] == null || i == committing) continue;

                var slot = (BodySlot)i;
                bool isHovered = hovered == slot;
                EquipKind? wornKind = KindIn(slot);
                SiteState state = BodySiteState.Resolve(slot, wornKind, carried, carriedKind, isHovered);
                sites[i].Apply(state, carriedItem, isHovered, wornKind);
            }
        }

        private EquipKind? KindIn(BodySlot slot)
        {
            InventorySlot contents = slots.GetSlot(slot);
            return contents == null || contents.IsEmpty ? null : contents.Item.equipKind;
        }

        private InventoryItem ItemIn(BodySlot slot)
        {
            InventorySlot contents = slots.GetSlot(slot);
            return contents == null || contents.IsEmpty ? null : contents.Item;
        }

        /// <summary>
        /// Whether this item wants the wearer's arms held out — see <see cref="WornFit.HoldsArmsOut"/>.
        ///
        /// <para>
        /// Read off the item's PREFAB, not off the instance worn on the body: an instance has been
        /// through <c>EquipItemSocket.Sanitize</c> and a ghost of one through <c>DisplayCopy</c>,
        /// and the same question has to answer the same way for gear on the body and for gear on
        /// its way there in the cursor. The prefab is the one copy neither of them can have edited.
        /// </para>
        /// </summary>
        private static bool HoldsArmsOut(InventoryItem item) =>
            item != null && item.itemPrefab != null
            && item.itemPrefab.TryGetComponent(out WornFit fit) && fit.HoldsArmsOut;

        private static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // ── Wiring and the look target ───────────────────────────────────────

        /// <summary>
        /// Say so, loudly, when a ghost prefab was never wired. An empty site with no placeholder
        /// draws nothing at all, and on screen that is indistinguishable from a site the screen does
        /// not know about — the exact silent failure this feature exists to avoid. A missing ghost
        /// is a degraded screen rather than a reason to refuse to open, so this reports and the
        /// session carries on.
        ///
        /// <para>
        /// Once per entry, which is where the wiring is actually read: the sites are rebuilt on
        /// every <see cref="Enter"/>. The same check in <c>LateUpdate</c> would say the same thing
        /// sixty times a second for as long as the screen stayed open.
        /// </para>
        /// </summary>
        private void ReportUnwiredPlaceholders()
        {
            if (gauntletPlaceholder == null)
                Debug.LogError("BodyFocusSession: 'gauntletPlaceholder' is not assigned — an empty forearm will " +
                               "show nothing at all. Build and wire the ghost prefabs with GearGhostBuilder.", this);

            if (backPlaceholder == null)
                Debug.LogError("BodyFocusSession: 'backPlaceholder' is not assigned — an empty back will " +
                               "show nothing at all. Build and wire the ghost prefabs with GearGhostBuilder.", this);
        }

        /// <summary>
        /// What the lens looks at: the chest bone, or a stand-in at chest height on a rig that has
        /// none. Never null, so the camera always has something to frame.
        /// </summary>
        private Transform ResolveLookTarget()
        {
            var animator = GetComponentInChildren<Animator>(true);
            Transform chest = BoneResolver.Resolve(animator, transform, chestBone, chestBoneNameHints);
            if (chest != null) return chest;

            if (lookAnchor == null)
            {
                lookAnchor = new GameObject("BodyFocusLookAnchor").transform;
                lookAnchor.SetParent(transform, false);
                lookAnchor.localPosition = Vector3.up * fallbackLookHeight;
            }

            return lookAnchor;
        }

        /// <summary>
        /// The stand-in for a chest bone, if this rig needed one. Rebuilt on the next entry rather
        /// than kept: a rig without a chest is rare, the anchor costs one GameObject to make, and a
        /// screen that opens and closes all game is exactly where objects left behind add up.
        /// </summary>
        private void DestroyLookAnchor()
        {
            if (lookAnchor != null) Destroy(lookAnchor.gameObject);
            lookAnchor = null;
        }
    }
}
