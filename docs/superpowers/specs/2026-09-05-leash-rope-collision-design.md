# Leash rope collision — design

**Date:** 2026-09-05
**Status:** approved, not yet implemented
**Governs:** [`Assets/Game/Scripts/Items/Artifacts/Leash/`](../../../Assets/Game/Scripts/Items/Artifacts/Leash)
**Doc to update on landing:** [docs/AI/systems/LeashSystem.md](../../AI/systems/LeashSystem.md)
**Depended on by:** [2026-09-05-crucible-puzzle-design.md](2026-09-05-crucible-puzzle-design.md)

## Problem

A leash is a straight chord between two knots. It passes through walls, pillars and terrain, and
`LeashGround` papers over the worst of it by *drawing* the rope draped on the ground while the
constraint still measures the straight line underneath. Consequences today:

- A rope hauled around a corner keeps pulling through the corner.
- A rope cannot be wound round a post to tie something up.
- Rope length is not consumed by anything, so there is no way to trade reach for leverage.

The last of those is what a rope is *for*, and its absence is why the leash currently reads as a
tether rather than as rope.

## Model

A rope becomes a polyline `A → w₀ → w₁ → … → wₙ → B` instead of a chord. Everything else about the
system is preserved: fixed total length, per-end ownership, a distance limit rather than a spring,
resist-to-break, per-body pull summing.

- **`Length` is the total along the polyline.** Stretch is `polylineLength − Length`.
- **Pinned segments consume length.** The usable rope at one end is the total minus everything
  spent on the far side of its nearest waypoint. Walking away from a wrap point therefore reels the
  far end in — a winch made of legs, with no winch in the code. `LeashSystem.md`'s standing rule
  that "there is no winch anywhere in this system, so a player cannot pull themselves along a rope"
  survives intact: a player still cannot shorten their *own* free segment by moving.
- **Force runs along the first segment.** `ResolveEnd` pulls each end toward its *adjacent
  waypoint*, not toward the far end. This is the change that makes a wrap mechanical rather than
  cosmetic; without it a rope bent 90° round a pillar still drags its load straight at the far end.
- **Wrapping is derived, never authored and never sent.** Given the same endpoints and the same
  static colliders, two machines build the same list.

## Key types

| Type | Status | Role |
|---|---|---|
| `LeashPath` | **new** | The polyline. Owns the wrap list, `TotalLength`, `FreeLengthAt(end)`, `DirectionAt(end)`, and the insert/remove step. Pure enough to test without a scene except for the casts, which are injected. |
| `LeashWrap` | **new**, `readonly struct` | One waypoint: `Position`, `Normal`, `Collider`. |
| `Leash` | changed | `MeasureStretch` measures the path; `ResolveEnd` takes its direction from the path; `FixedUpdate` steps the path before measuring. |
| `LeashRope` | changed | Draws the polyline's points instead of a chord plus sag. Sag applies per free segment. |
| `LeashGround` | **deleted** | Its whole job was faking this. See Gotchas. |
| `LeashArtifact` | changed | New tunables: `wrapLayers`, `wrapClearance`, `wrapRadius`, `maxWrapPoints`. |

## The algorithm

Per `FixedUpdate`, for each end, on the segment between that end and its nearest waypoint (or the
far end, when there are none):

**Insert.** Spherecast of `wrapRadius` from the waypoint toward the end, against `wrapLayers`. On a
hit, insert a waypoint at `hit.point + hit.normal * wrapClearance`. Refuse to insert if the
resulting segment would be shorter than `wrapClearance * 2` — that is the degenerate case that
produces waypoint spam along a flat wall.

**Remove.** A waypoint `w` with neighbours `p` and `q` dies when a cast from `p` to `q` is clear.
This replaces the usual sign-of-cross-product convexity test, which is well behaved for a 2D rope
and unreliable against arbitrary 3D meshes. Line-of-sight is one cast, is obviously correct, and
cannot get stuck in the state where a waypoint's turn direction is ambiguous.

**Order matters:** remove before insert within a step, or a waypoint inserted this frame is
immediately re-tested against a stale neighbour.

**Caps.** At most `maxWrapPoints` (default 8) per rope; at most one insert per end per step. Beyond
the cap the rope stops wrapping rather than misbehaving — a rope with nine bends in it is a rope
somebody is abusing.

## Multiplayer

`LeashSystem.md`'s model is that **nothing about a rope's shape is on the wire** — every machine
rebuilds it from replicated endpoints. Wrapping must not break that.

- **`wrapLayers` must exclude dynamic colliders.** Two machines cast against the same static world
  and agree; they do not agree about where a rolling barrel was 40 ms ago. A rope will therefore
  pass through loose props exactly as it does today. That is a deliberate limitation, not an
  oversight, and it goes in `Gotchas`.
- No new `NetMsg`. No change to `NetArg`. No change to ownership.
- Late joiners rebuild wraps from the endpoints in the join snapshot, same as everything else.

## Persistence

Wrap points are **not saved**. `LeashSaveable`'s format is unchanged. On load the path rebuilds
from the two endpoints, which means a rope wound the long way round a pillar can come back wound
the short way. Accepted: saving a wrap list would key it to collider instance ids that do not
survive a reload, and the alternative — a path that disagrees with the world it loaded into — is
worse than one that is merely re-derived.

## Performance

Two spherecasts and up to two line-of-sight casts per rope per `FixedUpdate`, plus one insert cast.
Buffers owned per path, `NonAlloc` throughout, following `LeashGround`'s existing grow-and-recast
pattern rather than silently discarding hits. Ropes are few (a handful live at once); this is not a
budget concern, but the caps exist so that it cannot become one.

## Testing

Add to [LeashConstraintTests.cs](../../../Assets/Game/Editor/Tests/LeashConstraintTests.cs). The
castable parts are injected as a delegate so EditMode tests can supply a fake world:

- `PolylineLength` over 0, 1 and n waypoints.
- `FreeLengthAt` — that pinning consumes length from the correct end, and that the two ends' free
  lengths plus the pinned middle equal the total.
- `ShouldRemove` — clear line of sight removes; blocked line of sight keeps.
- Insert refusal on a degenerately short segment.
- `maxWrapPoints` is honoured.
- **Regression:** a rope with no waypoints behaves bit-for-bit as it does today. This is the test
  that protects every existing rope in the game.

## Out of scope

Rope-vs-rope collision. Rope-vs-dynamic-body collision. Friction at a wrap point (a wrap is
frictionless; the rope slides). Any change to the resist/break rules.

## What this changes for the existing game

Every rope starts snagging: lasso hand-offs (`LassoHitch.TieOff` produces a real `Leash`), towed
crates, leashed creatures, ropes tied to the lander. Hauling a crate across broken terrain will
catch where it used to glide. This is the intended behaviour, but it is a **feel change to shipped
content**, and it wants a playtest of the lasso and of creature-leading before it is called done.

## Gotchas to record in LeashSystem.md

- Wrapping is static-geometry only, and why.
- Remove-before-insert ordering.
- `ResolveEnd` pulls toward the adjacent waypoint; a change here that reverts to `aToB` silently
  turns every wrap back into decoration.
- `LeashGround` is gone; a downward height probe is not how a rope meets the world.
- Wrap points are re-derived on load and may differ from the path that was saved.
