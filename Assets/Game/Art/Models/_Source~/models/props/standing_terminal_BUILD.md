# Standing terminal — build record

The lander's console: a leaning cassette-futurism CRT cabinet the player walks up to, zooms in
on, and reads. **The model is hand-built.** It is the `Coll_CrtMonitor_Kiosk` variation inside
[`components/props/crt_monitor.blend`](../../components/props/crt_monitor.blend), reworked by
the user on 2026-09-05 from the generated kiosk head into a whole unit — housing, lit key
column, screen plate and a geometry-nodes key strip (`Cube`) — and it ships from there.

    2.006 m tall, 0.78 m wide, 0.895 m deep. Leans back 23.8°.
    Screen 0.595 x 0.445 m (4:3), face centre 1.743 m above the floor, tilted with the lean
    so its normal points forward AND up: it faces a viewer whose eye is above it.

Ships as `Assets/Game/Art/Models/Props/standing_terminal.fbx` via
[`standing_terminal_export.py`](standing_terminal_export.py) — `_exportlib.export(...,
keep_collection="Coll_CrtMonitor_Kiosk")`, so whatever the user adds to that collection ships
and nothing is named in the script. 4 meshes, 4 624 tris.

> **`crt_monitor.blend` is hand-edited and is the source of truth.** Never re-run
> `crt_monitor.py` over it. Its other three variations (Desk, Wide, Radar) are still the
> generated ones.

## History: the retired assembly

The first build (same day) assembled a terminal from three new components —
`crt_monitor` Kiosk head, `keyboard_deck` Full deck, `console_pedestal` Cabinet — in a model
file `standing_terminal.blend`, with a deck bracket and two marker cubes. The user then built
the unit they actually wanted by hand inside the Kiosk collection, and the assembly was
**retired**: `standing_terminal.blend` and `standing_terminal.py` were deleted, the FBX name
was kept so the prefab's GUID survives, and the three components stay in the library as
built-ahead parts (the deck and pedestal variations are unused by anything today).

What the assembly's markers used to carry, the Unity builder now measures:

| Then | Now |
|---|---|
| `Marker_StandingTerminal_ScreenFace` at the glass centre | `ScreenPlane.Measure` over `Mesh_CrtMonitor_Kiosk_Screen`'s own triangles: centre, outward normal, up, size |
| `Marker_StandingTerminal_Focus` 0.95 m out | `TerminalShot.Distance` fits the glass height into the frame |
| origin at floor level by construction | `StandingTerminalBuilder.StandOnTheFloor` lifts the model so its lowest point is y = 0 |

## What Unity does with it

[`StandingTerminalBuilder`](../../../../../Editor/Environment/StandingTerminalBuilder.cs)
(**Tools ▸ SpaceGame ▸ Build Standing Terminal Prefab**) builds
`Assets/Game/Prefabs/Environment/Structures/Facilities/StandingTerminal.prefab`: collider,
`TerminalConsole` (replicated page + operator), `TerminalFocusSession` (the zoom-in),
`ShipTelemetrySource`, and a world-space `TerminalScreen` canvas on the glass with three pages.
`PlayerShipBuilder.BuildStandingTerminal` nests it in the cockpit at scale **1.0** — not the
crew's 1.7x — because the screen's lean faces an eye above it. See
[docs/AI/systems/Terminal.md](../../../../../../docs/AI/systems/Terminal.md).

## Things worth knowing about the hand-built file

- The key strip is an object named `Cube` with an `Array` geometry-nodes modifier and **no
  material**. The FBX exporter bakes the modifier; the builder gives the material-less renderer
  `Mat_Metal_Steel_Dark (DoubleSided)` rather than shipping Unity's pink default. Naming it and
  assigning a palette material in Blender would let the builder stop doing that.
- The Kiosk objects carry the lean as an object rotation (−23.8° about X) and the housing's
  origin sits mid-cabinet, not at the floor. Both are fine: nothing downstream reads the
  transform for anything but its world matrix.
- `_zverify` and the library index still treat the file as a component with four stacked
  variations; the Kiosk's pairs are the only real ones.
