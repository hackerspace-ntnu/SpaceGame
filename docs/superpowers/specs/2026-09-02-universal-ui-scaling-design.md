# Universal UI scaling — one canvas rule for every screen size

**Date:** 2026-09-02
**Reported as:** "there was a bug in the UI for VS, where different screen sizes had different sizes
of stuff."

## The defect

The project disagrees with itself about how its canvases scale, and the Versus lobby is where the
disagreement shows, because it is the only screen that converts between world space, screen pixels
and canvas pixels.

Three separate rules are live today:

| Where | Rule | Canvas at 2560x1080 |
| --- | --- | --- |
| `MainMenu.unity`, `PlayerHUD.prefab` | `MatchWidthOrHeight`, match **0** (width) | 1920 x 810 |
| 13 runtime-built canvases | `MatchWidthOrHeight`, match **0.5** | 2200 x 943 |
| `MatchResultUI`, `MatchLeaderboardUI`, `LoadingScreenUI` | match never assigned, so Unity's default **0** | 1920 x 810 |
| `LetterboxOverlay`, `PackHandVisuals` | no scaler / bare scaler, so `ConstantPixelSize` | 2560 x 1080 |

Two Versus helpers then re-derive the screen-to-canvas factor from a constant instead of reading it:

- `LobbyNameplates.CanvasScale()` returns `1920 / Screen.width`. That is the match-**0** answer, but
  the lobby rank draws on a `MenuScreen` canvas, which is match **0.5**. The measured pitch between
  two heads is therefore wrong by the ratio between the two rules, and the pitch is the only input
  to the nameplate font-size ladder.
- `LobbyPreviewCamera.BandFraction()` computes `canvasHeight = 1920 * Screen.height / Screen.width`,
  the same match-0 assumption, and uses it to decide how much of the frame the rank may occupy.

Worked example, 2560 x 1080 ultrawide:

| Quantity | Assumed | Actual | Error |
| --- | --- | --- | --- |
| Screen-to-canvas factor | 0.750 | 0.859 | 15% low |
| Canvas height | 810 | 943 | 14% low |

Both errors push the same way: names come out about 15% too small and the camera backs off further
than it needs to. On a window narrower than 16:9 both invert and names come out too large and
overlap. That is precisely "different screen sizes had different sizes of stuff".

`LobbyTeamPlates` is the control that proves the diagnosis: it measures its spacing directly in
canvas space (`plate.Row.anchoredPosition`) and needs no conversion, and it is the one rank overlay
that has never been reported as mis-sized.

## Design

### 1. One rule, in one place — `UIScale`

A new `Assets/Game/Scripts/Presentation/UI/Widgets/UIScale.cs` owns the reference resolution and the
match mode, and is the only thing in the project that configures a `CanvasScaler`. It also exposes
the scale factor and canvas size as pure functions of a screen size, so anything that needs to
reason about canvas geometry asks rather than re-derives.

### 2. The rule is `Expand`, not `MatchWidthOrHeight`

`ScreenMatchMode.Expand` scales by `min(w / 1920, h / 1080)`, so the canvas is never smaller than
the reference on either axis. Consequences:

- **At 16:9 and every wider aspect the canvas is exactly 1080 tall**, and the layout is
  pixel-identical to the authored 1920x1080 shot. Every ultrawide difference disappears by
  construction rather than by correction.
- At aspects narrower than 16:9 the canvas is exactly 1920 wide and taller than 1080.
- Every authored 1920x1080 layout always fits, at every aspect. No panel can overflow the canvas.

This also retires the documented gotcha that a menu page's vertical budget shrinks on a wide screen.
It cannot: the budget is now the same 308 canvas pixels at every aspect 16:9 and wider.

### 3. Content stays below the skyline at narrow aspects

The menu's clickable rows are dark navy and only read against sand, so they must sit below the 3D
set's skyline. The skyline is at a fixed *fraction* of screen height (fixed camera pitch, fixed
vertical FOV); `MenuEntry.ContentTop` is a fixed *pixel* offset from the top. Those agree only at one
aspect, and under `Expand` a 5:4 canvas is 1536 tall, which would put the content band above the
skyline and make it illegible — a regression this change must not ship.

`MenuEntry.ContentTopFor(canvasHeight)` returns the authored offset for a canvas 1080 tall or
shorter and scales it proportionally on a taller one. It is monotone and never raises content, so
16:9 and wider are unaffected.

### 4. The rank overlays measure in canvas space

`LobbyOverlayLayer` gains `TryToCanvas`, and `Place` is written in terms of it. `LobbyNameplates`
measures head pitch through it and its `CanvasScale()` constant is deleted, so the class no longer
holds an opinion about how the canvas scales. `LobbyPreviewCamera` asks `UIScale` for the canvas
height instead of computing one.

This is the same lesson the landing verification recorded: a check must not share its input with the
thing it checks. Here the fix is stronger — the conversion is removed rather than corrected.

## Testing

EditMode, in `Assets/Game/Editor/Tests/`:

1. `UIScaleTests` — the canvas is never smaller than the reference at any aspect; it is exactly
   1080 tall at 16:9 and wider; the scale factor and canvas size are consistent with each other.
2. `UIScaleTests` — every runtime-built canvas in the project goes through `UIScale`, by source scan,
   following the pattern `LobbyMenuWiringTests` already uses.
3. `MenuLayoutScalingTests` — the clickable band is the same height at 16:9 and every wider aspect,
   and content never rises above the skyline fraction at any aspect.

## Multiplayer and persistence

Neither applies, stated explicitly rather than skipped. Canvas scaling is per-machine presentation:
nothing here is replicated, nothing is authoritative, and no peer can observe another's window size.
No runtime state is introduced, so there is nothing to save; the one screen-related value that does
persist, the chosen resolution, already lives in `GameSettings` and is untouched.

## Game Development Constitution

- **GDC-L1-UX-0003** (make the interface communicate — readability and hierarchy; *objective*,
  confidence 4). A name that shrinks 15% below its intended size on one monitor and overlaps its
  neighbour on another has failed the readability test the principle sets. The fix is the
  presentation, not the player.
- **GDC-L1-UX-0006** (accessibility as design — scalable text, built in early; *contextual*,
  confidence 4). Text that scales predictably from one authored reference is the precondition for
  any later text-size option. The principle's own exception — small projects may ship a focused
  subset — applies: this change makes scalable text *possible*, and does not add a size slider.

No recorded disagreement in either principle bears on this decision.
