using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SpaceGame.Items;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// One gear tile: a dark rounded square, the item's icon filling it, a thin ring for state and
    /// a small key label in the corner. The hotbar's slots, the worn-gear tiles beside the bar and
    /// the six tiles of the body screen are all this one thing, so a change to how a slot looks
    /// changes every place a slot is drawn.
    ///
    /// <para>
    /// <b>Built in code, not from a prefab.</b> The whole of this project's UI is generated —
    /// <see cref="UITheme"/> draws its own rounded rectangles — and a tile made of nested Images
    /// rots in a prefab when a field is renamed.
    /// </para>
    /// <para>
    /// <b>Selection is not a tint.</b> The selected tile lifts off the row, swells a little and its
    /// ring lights amber, because a state shown only by brightening the thing itself cannot be read
    /// in peripheral vision.
    /// </para>
    /// <para>
    /// The tile knows nothing about what a click means. It raises <see cref="Clicked"/> and
    /// <see cref="HoverChanged"/>; whoever built it decides.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GearTile : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
        private const float HatchInset = 14f;
        private const float BadgeSize = 18f;

        private RectTransform frame;
        private Image glow;
        private Image tile;
        private RawImage hatch;
        private Image itemIcon;
        private Image ring;
        private Image wornBadge;
        private TextMeshProUGUI keyLabel;

        private bool selected;
        private bool hovered;
        private bool dropTarget;
        private bool refused;
        private bool reserved;
        private bool hasItem;

        private Coroutine shaking;

        /// <summary>This tile's rectangle, for cursor hit-tests from outside the EventSystem.</summary>
        public RectTransform Rect { get; private set; }

        /// <summary>Left click on the tile.</summary>
        public event Action<GearTile> Clicked;

        /// <summary>The pointer came over the tile (true) or left it (false).</summary>
        public event Action<GearTile, bool> HoverChanged;

        // ── Construction ─────────────────────────────────────────────────────

        /// <summary>Makes one tile under <paramref name="parent"/>, ready for <see cref="Refresh"/>.</summary>
        /// <param name="scale">Size relative to a hotbar slot. The back tile beside the bar is smaller than the slots.</param>
        public static GearTile Build(RectTransform parent, string name, string keyLabel, float scale = 1f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(HotbarStyle.SlotWidth * scale, HotbarStyle.SlotHeight * scale);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = rect.sizeDelta.x;
            element.preferredHeight = rect.sizeDelta.y;

            // The pointer target. Invisible, and the ONLY raycasting graphic on the tile — every
            // part below is raycastTarget false, so a gesture anywhere on the tile is one gesture
            // rather than a gesture against whichever sub-image happened to be on top of it.
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            // Without this the tile receives no pointer events at all. A CanvasRenderer culls a
            // fully transparent mesh by default, and GraphicRaycaster skips culled graphics — so an
            // invisible hit target is also an unhittable one unless the culling is turned off.
            var renderer = go.GetComponent<CanvasRenderer>();
            if (renderer != null) renderer.cullTransparentMesh = false;

            var built = go.AddComponent<GearTile>();
            built.Rect = rect;
            built.Compose(rect, keyLabel);

            return built;
        }

        private void Compose(RectTransform root, string key)
        {
            // Everything visible hangs off this, so the selected tile can lift and swell as one
            // piece. The root itself belongs to the layout group and must not be moved.
            frame = Node(root, "Frame");
            Stretch(frame, 0f, 0f, 0f, 0f);

            glow = Layer(frame, "Glow", UITheme.GlowSprite, Clear(HotbarStyle.Amber));
            Stretch(glow.rectTransform, -18f, -18f, -18f, -18f);

            tile = Layer(frame, "Tile", UITheme.Rounded(HotbarStyle.TileRadius), HotbarStyle.Tile);
            Stretch(tile.rectTransform, 0f, 0f, 0f, 0f);

            // Shown only while this tile's item is out in the player's hand. A RawImage with a
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

            // A worn kind lying in a hotbar slot: a small disc in the far corner, so "why can't I
            // fire this" has a visible answer without a word of text.
            wornBadge = Layer(tile.rectTransform, "Worn", UITheme.CircleSprite, Clear(HotbarStyle.Thread));
            Anchor(wornBadge.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                   new Vector2(BadgeSize, BadgeSize), new Vector2(-12f, 12f));

            keyLabel = Label(frame, key);
        }

        // ── Refreshing ───────────────────────────────────────────────────────

        public void SetKeyLabel(string text)
        {
            if (keyLabel != null) keyLabel.text = text ?? string.Empty;
        }

        /// <summary>Shows the tile as it now stands.</summary>
        /// <param name="isDropTarget">The cursor is over this tile with something in hand and a click would land it here.</param>
        /// <param name="isRefused">The cursor is over this tile with something in hand and it does not fit.</param>
        /// <param name="isReserved">This tile's item is in the player's hand. The tile reads as empty, but spoken for.</param>
        /// <param name="isWorn">The item is a worn kind lying in a hotbar slot, where it is inert.</param>
        public void Refresh(InventoryItem item, bool isSelected, bool isHovered,
                            bool isDropTarget = false, bool isRefused = false,
                            bool isReserved = false, bool isWorn = false)
        {
            selected = isSelected;
            hovered = isHovered;
            dropTarget = isDropTarget;
            refused = isRefused;
            reserved = isReserved;
            hasItem = item != null;

            if (itemIcon != null)
            {
                bool show = item != null && item.icon != null && !reserved;

                itemIcon.sprite = show ? item.icon : null;
                itemIcon.enabled = show;
            }

            if (wornBadge != null)
                wornBadge.color = isWorn && hasItem && !reserved ? Fade(HotbarStyle.Thread, 0.85f) : Clear(HotbarStyle.Thread);

            Restyle();
        }

        /// <summary>
        /// Every visual consequence of the state flags, in one pass over every part.
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

            // Translucent, so the world stays visible through the bar; an empty tile recedes
            // further than a full one, which is the hierarchy doing its job.
            tile.color = Tone(HotbarStyle.Tile,
                              selected ? 1.35f : hovered ? 1.15f : 1f,
                              hasItem || reserved ? 0.82f : 0.55f);

            // Safety orange beats amber here, deliberately: a live drop target is something about
            // to happen, and it has to out-shout the tile the player merely has selected. Red is
            // the refusal, and it is the same colour the pack paints a cell that will not take.
            if (refused)
            {
                ring.color = UITheme.Danger;
                glow.color = Fade(UITheme.Danger, 0.22f);
            }
            else if (dropTarget)
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

        // ── Pointer ──────────────────────────────────────────────────────────

        public void OnPointerEnter(PointerEventData eventData) => HoverChanged?.Invoke(this, true);

        public void OnPointerExit(PointerEventData eventData) => HoverChanged?.Invoke(this, false);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke(this);
        }

        // ── Refusal ──────────────────────────────────────────────────────────

        /// <summary>
        /// The tile saying "no": a short damped wiggle. This is the whole refusal — there is
        /// deliberately no message to read — so it must be visible without being a spasm.
        /// </summary>
        public void Shake()
        {
            if (!isActiveAndEnabled) return;
            if (shaking != null) StopCoroutine(shaking);
            shaking = StartCoroutine(ShakeRoutine());
        }

        private void OnDisable()
        {
            // Unity kills a running coroutine outright on disable, without letting it reach its
            // own trailing Restyle() — so without this a tile deactivated mid-shake could be
            // reactivated later still sitting off-centre, with nothing left to ever put it back.
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

            // Not a target. The invisible Image on the tile root is, and it covers all of this.
            image.raycastTarget = false;

            return image;
        }

        /// <summary>The key, tucked into the tile's top-left corner over the icon.</summary>
        private static TextMeshProUGUI Label(RectTransform parent, string text)
        {
            RectTransform rect = Node(parent, "Key");

            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text ?? string.Empty;
            label.fontSize = 17f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;

            // Wide enough for the longest key any tile carries — "SPACE ×2", on the body screen's
            // torso tile, at ~82 px in this face. It was 60 px while every key was one character,
            // which wrapped that one onto a second line and clipped it against the 26 px height.
            Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f),
                   new Vector2(104f, 26f), new Vector2(10f, -8f));

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
