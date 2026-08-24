using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One slot on the hotbar: a dark rounded tile, the item's icon filling it, a thin ring for
    /// state and a small number in the corner. Nothing else — the icon is the information, and
    /// everything the old pouch dressing spent pixels on now goes to a bigger icon.
    ///
    /// <para>
    /// <b>Built in code, not from a prefab.</b> The whole of this project's UI is generated —
    /// <see cref="UITheme"/> draws its own rounded rectangles, every full-screen menu assembles
    /// itself, and nothing under <c>Presentation</c> ships a PNG. A slot made of nested Images is
    /// exactly the thing that rots in a prefab when a field is renamed, and building it here means
    /// the shape and the code that drives it cannot disagree.
    /// </para>
    /// <para>
    /// <b>The selected slot is not a tint.</b> It lifts off the row, swells a little and its ring
    /// lights amber. That matters for the reason the menu design language gives: a state shown only
    /// by brightening the thing itself is a state nobody can read at a glance, and a hotbar has to
    /// be readable in peripheral vision while the player is looking elsewhere.
    /// </para>
    /// <para>
    /// <b>Dragging.</b> The pointer handlers here are the only EventSystem drag plumbing in the
    /// project. They do not implement a drag — they hand the gesture to
    /// <c>PackDragController</c>, which is already hit-testing the pack every frame and already
    /// knows what a legal placement is. <see cref="InventoryUI"/> is the bridge.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventorySlotUI : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler
    {
        private const float HatchInset = 14f;

        private int slotIndex;
        private InventoryUI parentUI;

        private RectTransform frame;
        private Image glow;
        private Image tile;
        private RawImage hatch;
        private Image itemIcon;
        private Image ring;
        private TextMeshProUGUI keyLabel;

        private bool selected;
        private bool hovered;
        private bool dropTarget;
        private bool reserved;

        // The shake's own tunables — seconds, starting amplitude, wiggle frequency — live on
        // HotbarStyle beside every other hotbar visual constant, not here.

        private Coroutine shaking;

        /// <summary>Whether this slot holds anything. Read by the drag gesture before it starts.</summary>
        public bool HasItem { get; private set; }

        /// <summary>This slot's rectangle, for the cursor hit-test in <see cref="InventoryUI"/>.</summary>
        public RectTransform Rect { get; private set; }

        // ── Construction ─────────────────────────────────────────────────────

        /// <summary>Makes one slot under <paramref name="parent"/>, ready for <see cref="Refresh"/>.</summary>
        public static InventorySlotUI Build(RectTransform parent, int index, InventoryUI owner)
        {
            var go = new GameObject($"Slot {index + 1}", typeof(RectTransform));
            go.layer = parent.gameObject.layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(HotbarStyle.SlotWidth, HotbarStyle.SlotHeight);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = HotbarStyle.SlotWidth;
            element.preferredHeight = HotbarStyle.SlotHeight;

            // The pointer target. Invisible, and the ONLY raycasting graphic on the slot — every
            // part below is raycastTarget false, so a gesture anywhere on the slot is one gesture
            // rather than a gesture against whichever sub-image happened to be on top of it.
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            // Without this the slot receives no pointer events at all. A CanvasRenderer culls a
            // fully transparent mesh by default, and GraphicRaycaster skips culled graphics — so an
            // invisible hit target is also an unhittable one unless the culling is turned off.
            var renderer = go.GetComponent<CanvasRenderer>();
            if (renderer != null) renderer.cullTransparentMesh = false;

            var slot = go.AddComponent<InventorySlotUI>();
            slot.Rect = rect;
            slot.Compose(rect, index);
            slot.Init(index, owner);

            return slot;
        }

        private void Compose(RectTransform root, int index)
        {
            // Everything visible hangs off this, so the selected tile can lift and swell as one
            // piece. The root itself belongs to the layout group and must not be moved.
            frame = Node(root, "Frame");
            Stretch(frame, 0f, 0f, 0f, 0f);

            glow = Layer(frame, "Glow", UITheme.GlowSprite, Clear(HotbarStyle.Amber));
            Stretch(glow.rectTransform, -18f, -18f, -18f, -18f);

            tile = Layer(frame, "Tile", UITheme.Rounded(HotbarStyle.TileRadius), HotbarStyle.Tile);
            Stretch(tile.rectTransform, 0f, 0f, 0f, 0f);

            // Shown only while this slot's item is out in the player's hand. A RawImage with a
            // scaled uv rect rather than Image.Type.Tiled — see HotbarStyle.HatchTexture.
            hatch = Node(tile.rectTransform, "Reserved").gameObject.AddComponent<RawImage>();
            hatch.texture = HotbarStyle.HatchTexture;
            hatch.color = Clear(HotbarStyle.Thread);
            hatch.raycastTarget = false;
            hatch.uvRect = new Rect(0f, 0f,
                                    (HotbarStyle.SlotWidth - HatchInset * 2f) / 10f,
                                    (HotbarStyle.SlotHeight - HatchInset * 2f) / 10f);
            Stretch(hatch.rectTransform, HatchInset, HatchInset, HatchInset, HatchInset);

            itemIcon = Layer(tile.rectTransform, "Icon", null, Color.white);
            itemIcon.preserveAspect = true;
            itemIcon.enabled = false;
            Stretch(itemIcon.rectTransform,
                    HotbarStyle.IconInset, HotbarStyle.IconInset,
                    HotbarStyle.IconInset, HotbarStyle.IconInset);

            // Last, so the ring reads as the tile's own edge rather than as a box behind the icon.
            ring = Layer(tile.rectTransform, "Ring", UITheme.Edge(HotbarStyle.TileRadius),
                         Clear(HotbarStyle.Amber));
            Stretch(ring.rectTransform, 0f, 0f, 0f, 0f);

            keyLabel = Label(frame, (index + 1).ToString());
        }

        // ── Refreshing ───────────────────────────────────────────────────────

        public void Init(int index, InventoryUI parent)
        {
            slotIndex = index;
            parentUI = parent;

            if (keyLabel != null) keyLabel.text = (index + 1).ToString();
        }

        /// <summary>Shows the slot as it now stands.</summary>
        /// <param name="isDropTarget">A drag is hovering here and would land in this slot.</param>
        /// <param name="isReserved">This slot's item is in the player's hand, mid-drag. The tile
        /// reads as empty, but as an empty tile that is spoken for.</param>
        public void Refresh(InventorySlot slot, bool isSelected, bool isHovered,
                            bool isDropTarget = false, bool isReserved = false)
        {
            selected = isSelected;
            hovered = isHovered;
            dropTarget = isDropTarget;
            reserved = isReserved;

            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            HasItem = item != null;

            if (itemIcon != null)
            {
                bool show = item != null && item.icon != null && !reserved;

                itemIcon.sprite = show ? item.icon : null;
                itemIcon.enabled = show;
            }

            Restyle();
        }

        /// <summary>
        /// Every visual consequence of the four state flags, in one pass over every part.
        ///
        /// One pass rather than a set of edits, so a state that is entered and left cannot leave a
        /// colour behind — which is the failure mode of a highlight that only ever gets turned on.
        /// </summary>
        private void Restyle()
        {
            if (frame == null) return;

            // Selection is a lift and a light, not a brightness. Hover is a smaller nudge of the
            // same shape, so the two read as one axis rather than as competing highlights.
            float lift = selected ? HotbarStyle.SelectedLift : hovered ? 2f : 0f;
            float swell = selected ? 1.06f : hovered ? 1.02f : 1f;

            frame.anchoredPosition = new Vector2(0f, lift);
            frame.localScale = new Vector3(swell, swell, 1f);

            // Translucent, so the world stays visible through the bar; an empty slot recedes
            // further than a full one, which is the hierarchy doing its job.
            tile.color = Tone(HotbarStyle.Tile,
                              selected ? 1.35f : hovered ? 1.15f : 1f,
                              HasItem || reserved ? 0.82f : 0.55f);

            // Safety orange beats amber here, deliberately: a live drop target is something about
            // to happen, and it has to out-shout the slot the player merely has selected.
            if (dropTarget)
            {
                ring.color = HotbarStyle.SafetyOrange;
                glow.color = Fade(HotbarStyle.SafetyOrange, 0.30f);
            }
            else if (selected)
            {
                ring.color = HotbarStyle.Amber;
                glow.color = Fade(HotbarStyle.Amber, 0.24f);
            }
            else if (hovered)
            {
                ring.color = Fade(HotbarStyle.Thread, 0.45f);
                glow.color = Clear(HotbarStyle.Amber);
            }
            else
            {
                ring.color = new Color(1f, 1f, 1f, 0.10f);
                glow.color = Clear(HotbarStyle.Amber);
            }

            hatch.color = reserved ? Fade(HotbarStyle.Thread, 0.30f) : Clear(HotbarStyle.Thread);

            keyLabel.color = selected
                ? HotbarStyle.Amber
                : Fade(HotbarStyle.Stencil, 0.65f);
        }

        // ── Pointer and drag ─────────────────────────────────────────────────

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (parentUI != null) parentUI.OnSlotHovered(slotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (parentUI != null) parentUI.OnSlotUnhovered(slotIndex);
        }

        /// <summary>
        /// A press that has started to move. Offered to the pack, which either takes the gesture or
        /// is not there — outside focus mode there is nowhere to drag an item TO, and the cursor is
        /// locked out there anyway.
        ///
        /// <para>
        /// Left button only. The InputSystemUIInputModule this project's EventSystem uses binds a
        /// right-click drag exactly like a left one, and the right button already has its own
        /// discrete gesture — <see cref="OnPointerClick"/>'s stow. A few pixels of hand tremor past
        /// the drag threshold on a right-click would otherwise fire this too, and the two gestures
        /// cannot be the same release.
        /// </para>
        /// <para>
        /// Declining means clearing <see cref="PointerEventData.pointerDrag"/>, not simply
        /// returning: left set, the EventSystem keeps routing move and end events to a slot that
        /// refused the drag, and the matching <see cref="OnEndDrag"/> would then resolve a gesture
        /// that never began. The same is true of the right-button case here, for the same reason.
        /// </para>
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                eventData.pointerDrag = null;
                return;
            }

            if (parentUI == null || !parentUI.BeginSlotDrag(slotIndex, eventData.position))
                eventData.pointerDrag = null;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (parentUI != null) parentUI.DragSlot(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (parentUI != null) parentUI.EndSlotDrag(eventData.position);
        }

        /// <summary>
        /// A drag let go over this slot.
        ///
        /// <para>
        /// It resolves nothing. <c>OnDrop</c> and <c>OnEndDrag</c> both fire for the same release,
        /// so exactly one of them may act — and it has to be the one that fires even when the
        /// release lands on nothing at all, which is <see cref="OnEndDrag"/>. What this is for is
        /// the slot under the pointer saying so, which is the same thing a hover says.
        /// </para>
        /// </summary>
        public void OnDrop(PointerEventData eventData)
        {
            if (parentUI != null) parentUI.OnSlotHovered(slotIndex);
        }

        /// <summary>
        /// Right-click: this slot's item goes onto the open pack, wherever the pack finds
        /// room. The rough mirror of right-clicking a pack item to take it — close enough to
        /// read as the same gesture in both directions, though the pack side fires on the
        /// press and this one waits for the click to resolve.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Right) return;
            if (parentUI != null) parentUI.RequestSlotStow(slotIndex);
        }

        // ── Refusal ──────────────────────────────────────────────────────────

        /// <summary>
        /// The slot saying "no": a short damped wiggle, used where a stow was refused. This is
        /// the whole refusal — there is deliberately no message to read — so it must be visible
        /// without being a spasm.
        /// </summary>
        public void Shake()
        {
            if (shaking != null) StopCoroutine(shaking);
            shaking = StartCoroutine(ShakeRoutine());
        }

        private void OnDisable()
        {
            // Unity kills a running coroutine outright on disable, without letting it reach its
            // own trailing Restyle() — so without this a slot deactivated mid-shake could be
            // reactivated later still sitting up to HotbarStyle.ShakePixels off-centre, with
            // nothing left to ever put it back.
            if (shaking == null) return;

            shaking = null;
            Restyle();
        }

        private System.Collections.IEnumerator ShakeRoutine()
        {
            for (float t = 0f; t < HotbarStyle.ShakeSeconds; t += Time.unscaledDeltaTime)
            {
                float fade = 1f - t / HotbarStyle.ShakeSeconds;
                float x = Mathf.Sin(t * HotbarStyle.ShakeFrequency) * HotbarStyle.ShakePixels * fade;

                frame.anchoredPosition = new Vector2(x, frame.anchoredPosition.y);
                yield return null;
            }

            shaking = null;
            Restyle();
        }

        // ── Small builders ───────────────────────────────────────────────────

        private static RectTransform Node(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);

            return rect;
        }

        private static Image Layer(RectTransform parent, string name, Sprite sprite, Color colour)
        {
            RectTransform rect = Node(parent, name);

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;

            // 9-sliced whenever the sprite has a border, which is how every UITheme rounded
            // rectangle is built; simple otherwise.
            image.type = sprite != null && sprite.border.sqrMagnitude > 0f
                ? Image.Type.Sliced
                : Image.Type.Simple;

            // Not a target. The invisible Image on the slot root is, and it covers all of this.
            image.raycastTarget = false;

            return image;
        }

        /// <summary>The slot's number, tucked into the tile's top-left corner over the icon.</summary>
        private static TextMeshProUGUI Label(RectTransform parent, string text)
        {
            RectTransform rect = Node(parent, "Key");

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 17f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;

            Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(30f, 26f), new Vector2(10f, -8f));

            return label;
        }

        private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>
        /// Pins a rect to one anchor point, or to one edge when the two anchors differ on an axis.
        /// A component of <paramref name="size"/> on an axis the anchors already span is a margin
        /// rather than a width — Unity's own rule for sizeDelta.
        /// </summary>
        private static void Anchor(RectTransform rect, Vector2 min, Vector2 max,
                                   Vector2 size, Vector2 offset)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(Mathf.Lerp(min.x, max.x, 0.5f), Mathf.Lerp(min.y, max.y, 0.5f));
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        /// <summary>The colour brightened by <paramref name="gain"/>, at <paramref name="alpha"/>.</summary>
        private static Color Tone(Color colour, float gain, float alpha) =>
            new(Mathf.Clamp01(colour.r * gain), Mathf.Clamp01(colour.g * gain),
                Mathf.Clamp01(colour.b * gain), colour.a * alpha);

        private static Color Fade(Color colour, float alpha) =>
            new(colour.r, colour.g, colour.b, alpha);

        /// <summary>
        /// The colour at zero alpha.
        ///
        /// Used rather than disabling the graphic, because a graphic toggled off and on rebuilds
        /// its mesh and re-enters the canvas batch, where a colour change does not.
        /// </summary>
        private static Color Clear(Color colour) => Fade(colour, 0f);
    }
}
