using System.Collections.Generic;
using TMPro;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Everything focus mode draws that is not the pack itself: the hover rim, the carried copy —
    /// in the item's own materials — and the item's name by the cursor.
    ///
    /// <para>
    /// Nothing is drawn where a carried item CAME from, because nothing is left there: an item in
    /// the hand stops being drawn on the mat for as long as it is held — see
    /// <see cref="BackpackObject.SetInHand"/>.
    /// </para>
    ///
    /// <para>
    /// Split out of <c>PackHandController</c> so that file can be about <em>what the player is
    /// doing</em> and this one about <em>what that looks like</em>. They are genuinely separable:
    /// nothing here reads input, decides a placement or talks to the network, and nothing there
    /// touches a material.
    /// </para>
    /// <para>
    /// Every material is an instance created here and destroyed in <see cref="Dispose"/>. Unity
    /// does not collect materials with the objects that used them, and a focus session is opened
    /// hundreds of times.
    /// </para>
    /// </summary>
    public sealed class PackHandVisuals
    {
        /// <summary>
        /// Metres the carried copy floats above the surface it is over. Low on purpose: the copy
        /// is depth-tested against the verdict cells lying on the face (see
        /// <see cref="PackGridVisual"/>), and under the focus camera's pitch every centimetre of
        /// lift slides the patch of cells the copy occludes about a centimetre across the face —
        /// at 0.03 m the cut-out still sits on the copy's own footprint rather than a visible
        /// cell toward the camera, while the copy still reads as picked up.
        /// </summary>
        /// <remarks>Metres above the mat, so it scales with the mat — the argument above is
        /// about how far the occluded patch slides per centimetre of lift under a fixed camera
        /// pitch, and the camera moved back by the same factor.</remarks>
        private static readonly float CarryLift = PackScale.Apply(0.03f);

        /// <summary>The refusal flash: a red outline shell traced round the carried copy. The
        /// copy keeps the item's own materials at all times, so the flash is a rim, never a
        /// repaint — the verdict colour itself lives in the cells under the copy.</summary>
        private static readonly Color DeniedRim = new(1f, 0.42f, 0.36f, 1f);

        private static readonly Color HoverRim = new(1f, 0.92f, 0.6f, 1f);

        /// Relative weights, keeping the denied flash the wider of the two and the hover rim the
        /// finer — the proportions both roles were originally authored with.
        private const float HoverWeight = 1f;
        private const float DeniedWeight = 1.2f;

        private readonly Material deniedMaterial;
        private readonly Material hoverMaterial;

        private GameObject proxy;
        private PackSurface proxySurface;
        private float proxyYaw;

        /// <summary>
        /// The world point the copy's footprint is currently centred over — the seat uv while it
        /// is on a face, the free point the controller picked while it is not. A world anchor
        /// rather than a remembered uv, because the copy travels off the faces entirely (see
        /// <see cref="MoveCarryFree"/>) and a uv only means anything while it is on one; every
        /// move is a translation against this, so the seating offset the copy was built with
        /// rides along unchanged wherever it goes.
        /// </summary>
        private Vector3 proxyAnchor;

        private Canvas labelCanvas;
        private TextMeshProUGUI label;

        // The shell objects standing in for the hover rim and the refusal flash. They are parented
        // to the renderers they trace, so a display copy destroyed under us — a layout change from
        // another player does exactly that — takes its shells with it and leaves nothing dangling.
        private readonly List<GameObject> rimShell = new();
        private readonly List<GameObject> deniedShell = new();
        private GameObject rimmed;

        public PackHandVisuals()
        {
            // No carry material at all: the carried copy keeps the ITEM'S OWN materials, so what
            // the player holds looks exactly like the thing they are placing. The verdict is not
            // painted onto it — it is the green/red cells on the face beneath it — and the
            // refusal flash below is an outline shell round it, never a repaint.
            deniedMaterial = TintMaterials.Rim("PackDeniedRim", DeniedRim, OutlineShell.MinOutlineWidth);
            hoverMaterial = TintMaterials.Rim("PackHover", HoverRim, OutlineShell.MinOutlineWidth);
        }

        // ── Hover ────────────────────────────────────────────────────────────

        /// <summary>Rim-light one placed item and un-rim whatever was lit before. Null clears.</summary>
        public void SetHovered(GameObject visual)
        {
            // The null case is never skipped: a display copy destroyed under us leaves `rimmed`
            // equal to null by Unity's own comparison, and skipping would strand the shell list.
            if (visual != null && rimmed == visual) return;

            rimmed = visual;
            OutlineShell.Build(rimmed, hoverMaterial, HoverWeight, rimShell);
        }

        // ── The carried copy ─────────────────────────────────────────────────

        /// <summary>
        /// Put a copy of <paramref name="itemPrefab"/> in the player's hand, at true size.
        ///
        /// <para>
        /// A separate copy rather than the placed one, because the placed one belongs to
        /// <c>BackpackObject</c> and is destroyed and rebuilt wholesale on every layout change —
        /// including the one this carry is about to cause.
        /// </para>
        /// <para>
        /// Answers whether a copy is actually standing there. <see cref="BackpackItemVisual"/>
        /// hands back null for a prefab it cannot build, and the caller records "the copy exists"
        /// to decide whether to keep moving it — so reporting the attempt rather than the result
        /// would leave the carry running invisibly while the cells kept promising a landing.
        /// </para>
        /// </summary>
        public bool BeginCarry(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            EndCarry();
            Rebuild(itemPrefab, surface, uv, yaw);

            return proxy != null;
        }

        /// <summary>
        /// Follow the cursor across a face.
        ///
        /// <para>
        /// Within one face this is a translation, because a surface is planar and rigid: the world
        /// delta between two anchor points is exact, and re-seating from scratch every frame would
        /// mean an Instantiate and a DestroyImmediate per frame. Crossing to another face, or
        /// turning, changes the seating, so those rebuild — both are things a player does a
        /// handful of times per carry.
        /// </para>
        /// </summary>
        public void MoveCarry(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            if (proxy == null || surface == null) return;

            if (surface != proxySurface || !Mathf.Approximately(yaw, proxyYaw))
            {
                Rebuild(itemPrefab, surface, uv, yaw);
                return;
            }

            MoveTo(surface.ToWorld(uv, 0f));
        }

        /// <summary>
        /// Follow the cursor where there is no face under it at all.
        ///
        /// <para>
        /// The carry is on screen everywhere, not only over the rig: off the faces the copy rides
        /// a point the controller picks on the cursor ray, keeping the seating it had when it
        /// left the last face — its scale, that face's orientation, the lift. Nothing snaps and
        /// no cells are drawn out here; the copy is simply the thing in the player's hand,
        /// travelling.
        /// </para>
        /// <para>
        /// A turn rebuilds, exactly as it does in <see cref="MoveCarry"/>: the copy is the single
        /// readout of what is in the hand, so a click on the sand that rotates the item has to be
        /// visible on the sand — not held in state until the cursor next crosses a face and then
        /// snapping a quarter turn there. The rebuild re-seats against the face the copy kept
        /// (scale and orientation frame) and the translation below puts it straight back on the
        /// cursor point, so nothing moves but the turn.
        /// </para>
        /// </summary>
        public void MoveCarryFree(GameObject itemPrefab, Vector3 worldPoint, float yaw)
        {
            if (proxy == null) return;

            if (!Mathf.Approximately(yaw, proxyYaw) && proxySurface != null)
            {
                Rebuild(itemPrefab, proxySurface, proxySurface.Size * 0.5f, yaw);
                if (proxy == null) return;
            }

            MoveTo(worldPoint);
        }

        /// <summary>Translate the copy so its anchor lands on <paramref name="worldPoint"/> —
        /// translation only, so the built-in seating offset (centre height, pivot correction,
        /// lift) rides along unchanged.</summary>
        private void MoveTo(Vector3 worldPoint)
        {
            proxy.transform.position += worldPoint - proxyAnchor;
            proxyAnchor = worldPoint;
        }

        /// <summary>
        /// Flashes a refusal round the held copy, or clears it: a red outline shell traced over
        /// the copy's renderers — the same machinery as the hover rim — so the item's own
        /// materials are never touched.
        ///
        /// Not the ordinary "this spot is taken" readout — the ghost cells carry that, in green
        /// and red, on every frame. This is the one click that can change nothing at all: red
        /// cells under a SYMMETRIC item, where the quarter turn a refused click answers with
        /// would occupy the identical cells. A click that does nothing has to say so, or the
        /// button reads as broken.
        /// </summary>
        public void SetCarryDenied(bool denied)
        {
            if (denied && proxy != null) OutlineShell.Build(proxy, deniedMaterial, DeniedWeight, deniedShell);
            else OutlineShell.Clear(deniedShell);
        }

        public void EndCarry()
        {
            // The shell parts die with the proxy either way; clearing the list here is what stops
            // it accumulating dead references across carries.
            OutlineShell.Clear(deniedShell);

            if (proxy != null) Object.Destroy(proxy);

            proxy = null;
            proxySurface = null;
        }

        private void Rebuild(GameObject itemPrefab, PackSurface surface, Vector2 uv, float yaw)
        {
            // The flash's shell traces renderers on the copy that is about to go; a flash caught
            // mid-air ends here rather than dangling on destroyed objects. (The controller's
            // timer clears its own half of the state on its own schedule — SetCarryDenied(false)
            // on an already-clear shell is a no-op.)
            OutlineShell.Clear(deniedShell);

            if (proxy != null) Object.Destroy(proxy);

            proxy = BackpackItemVisual.Build(itemPrefab, surface, uv, yaw);
            proxySurface = surface;
            proxyYaw = yaw;
            proxyAnchor = surface != null ? surface.ToWorld(uv, 0f) : Vector3.zero;

            if (proxy == null) return;

            // The copy Build hands back carries one BoxCollider for the cursor ray. On the thing
            // the cursor is currently holding that collider is in the way of everything behind it,
            // including the surface the player is trying to drop it on.
            foreach (Collider collider in proxy.GetComponentsInChildren<Collider>(true))
                Object.Destroy(collider);

            // A world-space nudge, so unlike every uv above it this one does not pass through
            // ToWorld and has to be told about the display scale itself. On the gear wall a lift
            // left at its logical size would let the carried copy graze the board it is enlarged
            // over.
            proxy.transform.position += surface.transform.up * (CarryLift * surface.DisplayScale);

            // And that is ALL: the copy keeps the item's original materials — Build never touches
            // a renderer — so the thing in the player's hand has its normal colours, looking
            // exactly as it will placed. Being opaque and lifted, it also writes depth over the
            // verdict cells on the face below, which is what cuts its silhouette out of them.
        }

        // ── The name by the cursor ───────────────────────────────────────────

        /// <summary>
        /// The hovered item's name, small and low contrast, beside the cursor. Empty hides it.
        ///
        /// Deliberately not a panel. A pack whose whole point is that you can SEE what is in it
        /// does not need a tooltip window over the thing you are looking at.
        /// </summary>
        public void ShowName(string text, Vector2 screenPosition)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (labelCanvas != null) labelCanvas.gameObject.SetActive(false);
                return;
            }

            EnsureLabel();

            labelCanvas.gameObject.SetActive(true);
            label.text = text;
            label.rectTransform.position = screenPosition + new Vector2(18f, -18f);
        }

        private void EnsureLabel()
        {
            if (labelCanvas != null) return;

            var go = new GameObject("PackHoverLabel") { hideFlags = HideFlags.HideAndDontSave };

            labelCanvas = go.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Over the HUD, which is still on screen: focus mode keeps the hotbar because items
            // are carried onto it.
            labelCanvas.sortingOrder = 500;

            // Without a scaler this canvas is ConstantPixelSize, so the caption below is 15
            // PHYSICAL pixels — legible at 1080p and unreadable on a 4K panel. The label is still
            // placed by screen point, which an overlay canvas takes in screen pixels whatever its
            // scale factor, so only the type size changes.
            UIScale.Apply(go);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);

            label = textGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 15f;
            label.color = new Color(0.92f, 0.92f, 0.9f, 0.62f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.rectTransform.sizeDelta = new Vector2(320f, 26f);
        }

        // ── Teardown ─────────────────────────────────────────────────────────

        /// <summary>Destroys everything this owns: the outline shells, the label and
        /// the two materials.</summary>
        public void Dispose()
        {
            SetHovered(null);
            EndCarry();

            // SetHovered(null) and EndCarry above already cleared these unless nothing was lit, in
            // which case the early-out skipped them. Cheap and idempotent either way.
            OutlineShell.Clear(rimShell);
            OutlineShell.Clear(deniedShell);

            if (labelCanvas != null) Object.Destroy(labelCanvas.gameObject);
            labelCanvas = null;
            label = null;

            Object.Destroy(deniedMaterial);
            Object.Destroy(hoverMaterial);
        }
    }
}
