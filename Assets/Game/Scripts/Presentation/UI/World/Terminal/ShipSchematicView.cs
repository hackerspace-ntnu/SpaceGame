using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// The SHIP page: the hull drawn in a hole in the glass, and the panel beside it that answers
    /// what the module under the cursor is and what the ship does without it.
    ///
    /// <para>
    /// The UI half of the schematic. <see cref="ShipSchematicStage"/> owns the miniature, the lens
    /// and the texture; this owns the cursor, the words and which module is pinned, and drives the
    /// stage's <see cref="ShipSchematicOrbit"/>.
    /// </para>
    /// <para>
    /// The cursor is read RAW off the mouse, like <c>TerminalFocusSession</c>'s exits and for the
    /// same reason: a session disables the player's input component, so no action on it fires. It
    /// also means a bystander — no focus session, so no event camera on the canvas — cannot drive
    /// the display, which is the same line the terminal already draws. They see the page the
    /// operator chose, turning slowly on its own.
    /// </para>
    /// <para>
    /// Nothing here is replicated or saved. Which modules are fitted arrives in the snapshot from
    /// state that is already both.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShipSchematicView : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private ShipSchematicStage stage;

        [Tooltip("The hole in the glass the miniature is drawn in. Its rect is the viewport.")]
        [SerializeField] private RawImage viewport;

        [Header("Panel")]
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI detailText;
        [SerializeField] private TextMeshProUGUI bodyText;

        [Header("Feel")]
        [Tooltip("Degrees of orbit per unit of scroll. Wheels report about 120 per notch; " +
                 "trackpads report a trickle, and both land somewhere sensible through this.")]
        [SerializeField] private float scrollSensitivity = 0.01f;

        [Tooltip("Canvas units the cursor may stray FROM THE PRESS POINT before the press stops " +
                 "being a click and starts turning the hull. Nothing turns inside it: a press that " +
                 "moves the picture cannot be answered as a click afterwards. The viewport is 320 " +
                 "units across, so this is a couple of per cent of it.")]
        [SerializeField, Min(0f)] private float dragDeadZone = 10f;

        [Tooltip("Seconds of no cursor before the hull starts turning on its own again.")]
        [SerializeField, Min(0f)] private float idleAfterSeconds = 3f;

        private static readonly ShipPartKind[] NoKinds = Array.Empty<ShipPartKind>();

        private ShipPartKind[] kinds = NoKinds;
        private int installedMask;

        private DragGesture drag;
        private Vector2 pressedAt;
        private float lastInput;
        private int hovered = ShipSchematicStage.NoPart;
        private int selected = ShipSchematicStage.NoPart;

        private void OnEnable()
        {
            if (stage == null || viewport == null) return;

            stage.SetViewport(Aspect());
            stage.SetRendering(true);
            ShowTexture();

            // A page put away keeps neither its pick nor its framing: coming back to a terminal
            // somebody left turned over and zoomed in reads as a stuck screen.
            Pick(ShipSchematicStage.NoPart);
            stage.Orbit.Home();
            Refresh();
        }

        private void OnDisable()
        {
            drag.Cancel();
            if (stage != null) stage.SetRendering(false);
            if (viewport != null) viewport.enabled = false;
        }

        /// <summary>
        /// Hangs the lens's texture on the glass, and keeps the hole switched off until there is
        /// one — a <see cref="RawImage"/> with no texture draws a solid white rectangle.
        /// </summary>
        private void ShowTexture()
        {
            viewport.texture = stage.Texture;
            viewport.enabled = stage.Texture != null;
        }

        /// <summary>One reading of the ship. Called by <see cref="TerminalScreen"/> a few times a second.</summary>
        public void Present(in TelemetrySnapshot snapshot)
        {
            kinds = snapshot.PartKinds ?? NoKinds;
            installedMask = snapshot.PartsInstalledMask;

            if (stage != null) stage.Apply(snapshot);
            Refresh();
        }

        /// <summary>
        /// Esc's first job. True when it was spent clearing the picked module, which is the caller's
        /// cue not to also close the terminal — the layered escape every other nested view in the
        /// game honours.
        /// </summary>
        public bool TryStepBack()
        {
            if (stage == null || selected == ShipSchematicStage.NoPart) return false;

            Pick(ShipSchematicStage.NoPart);
            Refresh();
            return true;
        }

        private void Update()
        {
            if (stage == null || viewport == null) return;

            // A lens rendering a ship for a room with nobody in it is the one cost this display
            // has, so it is the one thing that stops when the last reader walks away.
            stage.SetRendering(stage.WithinReadingDistance());
            if (!stage.Ready) return;

            // The texture is made on the first render; the viewport cannot be handed it earlier.
            if (viewport.texture != stage.Texture) ShowTexture();

            bool driven = ReadCursor();

            if (!driven && selected == ShipSchematicStage.NoPart &&
                Time.unscaledTime - lastInput > idleAfterSeconds)
            {
                stage.Orbit.Idle(Time.unscaledDeltaTime);
            }
        }

        // ── The cursor ───────────────────────────────────────────────────────

        /// <summary>Hover, drag, wheel and click. False when nobody is driving this display.</summary>
        private bool ReadCursor()
        {
            Camera eye = EventCamera();
            Mouse mouse = Mouse.current;

            if (eye == null || mouse == null)
            {
                drag.Cancel();
                Hover(ShipSchematicStage.NoPart);
                return false;
            }

            // Whether the cursor converts onto the glass at all is a different question from
            // whether it is over the hole: a drag may leave the hole and go on turning the hull,
            // but a point that does not convert is not a point at all and must not move anything.
            bool onGlass = TryPoint(eye, mouse.position.ReadValue(), out Vector2 point, out Vector2 uv);
            bool inside = onGlass && uv.x >= 0f && uv.x <= 1f && uv.y >= 0f && uv.y <= 1f;

            if (mouse.leftButton.wasPressedThisFrame && inside)
            {
                drag.Press(point);
                pressedAt = uv;
            }

            if (drag.Down && mouse.leftButton.isPressed && onGlass)
            {
                Vector2 turn = drag.Move(point, dragDeadZone);
                if (turn != Vector2.zero)
                {
                    stage.Orbit.Drag(turn);
                    lastInput = Time.unscaledTime;
                }
            }

            if (drag.Down && !mouse.leftButton.isPressed)
            {
                // Picked from where the press went down rather than where it came up. Inside the
                // dead zone those are all but the same point — but the one under the cursor when
                // the button went down is the module the reader saw light up, and answering with
                // any other one is the display arguing with its own highlight.
                if (drag.Release()) Click(pressedAt);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (inside && Mathf.Abs(scroll) > Mathf.Epsilon)
            {
                stage.Orbit.Zoom(scroll * scrollSensitivity);
                lastInput = Time.unscaledTime;
            }

            // The highlight stays lit through a press that is still a click — it is the answer to
            // "which one am I about to pick", and dropping it the instant the button goes down
            // reads as the display losing track of the module under the cursor. While the button
            // is down it shows what the press will pick, not what a cursor that has since crept a
            // unit or two is over, so the highlight and the click can never name different modules.
            Vector2 pointing = drag.Down ? pressedAt : uv;
            Hover(inside && !drag.Turning ? stage.Raycast(pointing) : ShipSchematicStage.NoPart);
            if (inside) lastInput = Time.unscaledTime;

            return inside || drag.Down;
        }

        /// <summary>
        /// Pick the module under the cursor: a different one replaces whatever was picked, the same
        /// one again clears it, and empty glass clears it too.
        ///
        /// <para>
        /// <b>The lens does not move.</b> Picking used to fly it in onto the module and dim the rest
        /// of the hull, which meant the other ten modules left the frame and the only way to reach
        /// one was to click empty space first — selection that fights being changed. The camera is
        /// now entirely the reader's: drag turns, wheel zooms, and what is under the cursor stays
        /// under the cursor.
        /// </para>
        /// </summary>
        private void Click(Vector2 uv)
        {
            int hit = stage.Raycast(uv);
            lastInput = Time.unscaledTime;

            Pick(hit == selected ? ShipSchematicStage.NoPart : hit);
            Refresh();
        }

        private void Hover(int socketIndex)
        {
            if (hovered == socketIndex) return;

            hovered = socketIndex;
            stage.SetHovered(socketIndex);
            Refresh();
        }

        private void Pick(int socketIndex)
        {
            selected = socketIndex;
            if (stage != null) stage.SetSelected(socketIndex);
        }

        /// <summary>
        /// The cursor in the viewport's own rect, and as 0..1 from its bottom left. The event
        /// camera is the focus camera the session lent the canvas, so this is the lens the reader
        /// is actually looking through.
        ///
        /// <para>
        /// True when the cursor lands on the glass's plane at all — which it does well outside the
        /// hole, and stops doing when the plane is edge-on or behind the lens. Whether it is over
        /// the hole is what <c>uv</c> says.
        /// </para>
        /// </summary>
        private bool TryPoint(Camera eye, Vector2 screenPoint, out Vector2 point, out Vector2 uv)
        {
            uv = Vector2.zero;
            point = Vector2.zero;

            RectTransform rect = viewport.rectTransform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPoint, eye, out point))
                return false;

            Rect area = rect.rect;
            uv = new Vector2((point.x - area.xMin) / area.width, (point.y - area.yMin) / area.height);

            return true;
        }

        private Camera EventCamera()
        {
            Canvas canvas = viewport.canvas;
            return canvas != null ? canvas.worldCamera : null;
        }

        private float Aspect()
        {
            Rect area = viewport.rectTransform.rect;
            return area.height > 0.001f ? area.width / area.height : 1f;
        }

        // ── The words ────────────────────────────────────────────────────────

        private void Refresh()
        {
            int shown = hovered != ShipSchematicStage.NoPart ? hovered : selected;

            if (shown == ShipSchematicStage.NoPart || shown >= kinds.Length)
            {
                Write(titleText, "HULL MODULES");
                Write(detailText, ShipPartInfo.OverviewCount(installedMask, kinds));
                Write(bodyText, ShipPartInfo.OverviewBody(installedMask, kinds));
                return;
            }

            ShipPartKind kind = kinds[shown];
            bool installed = ShipPartInfo.IsInstalled(installedMask, shown);

            Write(titleText, ShipPartInfo.Name(kind));
            Write(detailText, ShipPartInfo.Detail(
                installed,
                ShipPartInfo.FittedOfKind(installedMask, kinds, kind),
                ShipPartInfo.TotalOfKind(kinds, kind)));
            Write(bodyText, ShipPartInfo.Function(kind));
        }

        private static void Write(TMP_Text label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
