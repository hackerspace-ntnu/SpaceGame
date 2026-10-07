---
system: RopeCutting
layer: items
summary: "A beam parts a leash, a lasso or a grapple cable, each by its own break path"
paths:
  - Assets/Game/Scripts/Items/Artifacts/Ropes
  - Assets/Game/Editor/Tests/RopeCuttingTests.cs
symptoms:
  - "the laser staff burns straight through a rope and nothing happens"
  - "a rope is cut through a wall the beam already stopped at"
  - "a rope directly above or below the beam is cut as if the beam were flat"
  - "cutting a rope with two bends in it announces the snap several times"
  - "a rope is cut on the host and stays on for everybody else"
  - "the swinger keeps rope steering after somebody cut their grapple cable"
  - "one rope is cut and the rope beside it is skipped"
reads_with: [Artifacts, LeashSystem, Lasso, Multiplayer]
updated: 2026-09-07
---

# Rope cutting

The laser staff parts any rope its arc lies across. Three different rope systems accept that through
one seam, and each of them still breaks the way it already broke.

**Scope:** [`Assets/Game/Scripts/Items/Artifacts/Ropes/`](Assets/Game/Scripts/Items/Artifacts/Ropes)
(2 files), [RopeCuttingTests.cs](Assets/Game/Editor/Tests/RopeCuttingTests.cs).
**Related:** [Artifacts.md](Artifacts.md) (the staff itself) · [LeashSystem.md](LeashSystem.md) ·
[Lasso.md](Lasso.md) · [Multiplayer.md](Multiplayer.md)

**Not this system:** the hogtie's cordage. A tie has no rope visual to aim at
([Hogtie.md](Hogtie.md)), so there is nothing in the world for a beam to meet; it joins this
registry the day it is drawn, and the staff needs no change when it does.

## Model

- **No rope in this game has a collider, and none is given one.** A leash is a polyline derived every
  frame, a lasso is a Verlet cable, a grapple's cable is two points and a sag. Maintaining a chain of
  colliders at physics rate for three systems would also put ropes in front of every other query in
  the game — the beam's own self-skip loop, the grapple's aim, gunfire. So the cutter brings its own
  segment and asks the ropes directly.
- **A cut is instant, not a burn-through.** A rope is either in the beam or it is not; there is no
  dwell to accumulate and nothing carried between frames. A laser that had to be held on a rope
  would be one that cannot cut on a sweep, which is the only way anybody will actually do it.
- **Only a rope that is out.** A lasso still in the air and a grapple dart still flying append
  nothing. The loop is a projectile for the few tenths of a second it is up, and swatting one down
  would be a reflex nobody can aim and nobody can read.
- **The cutter passes the segment it actually reaches.** The staff passes muzzle to the point its
  beam stopped at, so a wall between the staff and a rope protects that rope with no line-of-sight
  test written anywhere, and a rope beside the holder's head is safe from a beam that leaves the
  fist a metre below it.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `ICuttableRope` | [ICuttableRope.cs](Assets/Game/Scripts/Items/Artifacts/Ropes/ICuttableRope.cs) | `AppendSpan` (world polyline, or nothing when the rope is not out), `Binds(body)` (is this rope on that body, either end) and `Cut` |
| `CuttableRopes` | [CuttableRopes.cs](Assets/Game/Scripts/Items/Artifacts/Ropes/CuttableRopes.cs) | `Register` / `Unregister` / `All`, `CutAlong(from, to, radius)`, `CutEveryRopeOn(body)`, and the pure `SegmentDistanceSq` |
| `LaserStaffArtifact` | [LaserStaffArtifact.cs](Assets/Game/Scripts/Items/Artifacts/Gadgets/LaserStaffArtifact.cs) | The only cutter. `ropeCutRadius` (0.15 m; zero disables), called from `CutRopes` beside `TickDamage` |

The three implementations, each keeping its own break path:

| Rope | Span | `Cut()` does | Registered by |
| --- | --- | --- | --- |
| [`Leash`](Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs) | `LeashPath.PointsBetween` — the drawn shape, wraps included | `Snap()`, which already announces on an anchor's channel | `OnEnable` / `OnDisable`, beside `Leash.All` |
| [`LassoArtifact`](Assets/Game/Scripts/Items/Artifacts/Lasso/LassoArtifact.cs) | hand → attach point, while `_isLassoed` | `SendRope(LassoVerb.Snapped)` — identical to tearing under strain | `Listen`, beside the net registration |
| [`GrapplingHookArtifact`](Assets/Game/Scripts/Items/Artifacts/Gadgets/GrapplingHookArtifact.cs) | hand → `RopeEnd`, while `_isGrappling` | broadcasts `GrappleVerb.Off` on the swinger's channel, then `StopGrapple` | `Listen`, beside the net registration |

`Binds`, per rope: a leash answers `Restrains` (either anchor `IsChildOf` the body); a lasso answers
the holder **or** the caught body; a grapple answers the swinger alone, because its far end is a
point in the world rather than a thing.

## Flows

1. The staff's `Update` traces the beam on every machine, exactly as before.
2. On the **server only** — the same gate `TickDamage` is behind — `CutRopes` calls
   `CuttableRopes.CutAlong(MuzzlePoint(), _endPoint, ropeCutRadius)`.
3. `CutAlong` walks the registry, appends each rope's span into a shared scratch list, and tests each
   of its segments against the beam segment with `SegmentDistanceSq`. Ropes inside the radius are
   collected, **not cut yet**.
4. Every collected rope is then cut, once, and announces the parting itself.

A respawn is the second caller, and it asks the other question:
[`RespawnRelease.Everything`](Assets/Game/Scripts/Gameplay/Game/Spawning/RespawnRelease.cs) calls
`CutEveryRopeOn(body)`, which gathers by `Binds` rather than by geometry and then cuts the same way.
Ropes are gathered before any is cut there too, for the same enumeration reason.

## Multiplayer

- **The verdict is the server's**, on the same side as the damage travelling with the beam. Judged on
  every machine it would be three verdicts on three slightly different rope shapes — the trap the
  leash's own break verdict was moved out of once already.
- **Nothing new goes on the wire.** Each rope reuses the message it already has:
  `NetMsg.LeashSnap`, `LassoVerb.Snapped`, `GrappleVerb.Off`. No new `NetMsg`, no new verb.
- **The grapple is the one release somebody else decides.** Normally the swinger announces their own,
  because the body handed back is theirs; a cut travels the other way down the same message, and the
  swinger reaches `StopGrapple` from the announcement, which is what gives back the tether and the
  lens.

## Persistence

Nothing new is stored. A cut leash is gone from `Leash.All` before `LeashSaveable` next captures, and
a lasso or grapple that has released is already save-clean. Cutting is a verb applied to live state,
never a state of its own.

## Gotchas

- **`SegmentDistanceSq` is deliberately three-dimensional, and the one other segment routine in the
  codebase is not.** `CaveSdfField.ClosestPointOnSegmentXZ` measures in plan view, which is exactly
  wrong for a rope hanging two metres above the beam. Do not "reuse" it here.
- **The degenerate branches are not padding.** All three systems draw a rope as two coincident points
  for a frame or two while it is being built or torn down. An unguarded division there returns NaN,
  which compares false against every threshold — so the failure is a rope that silently cannot be
  cut, not an exception.
- **Ropes are decided first and cut second.** Cutting mutates the registry: a leash's `Snap` destroys
  its GameObject and the `OnDisable` that follows unregisters it, inside the very loop that found it.
  Cutting during the enumeration steps over the rope after it. `RopeCuttingTests` pins this.
- **A rope is cut once however many of its segments are in the beam.** A leash draped over the arc
  presents several; cutting per segment would announce one snap per bend.
- **Registration is on a lifecycle seam, never on the state edge where the rope appears.** A leash
  registers while its component is enabled, an item while it is in a hand — a rope that is not out
  simply appends nothing. Registering on the catch would be one missed edge away from a destroyed
  component left in a static list.
- **A dedicated server does not hear its own broadcast.** `SendTo.ClientsAndHost` excludes it, so the
  grapple's `Cut` calls `StopGrapple` itself after sending. On a host the inline dispatch has already
  run it and the call is the no-op its first line is written to be. `Leash.Snap` and the lasso's
  request handler both do their local work before announcing, so neither needs this.
- **A rope has no idea anybody died.** Nothing in any of these three systems watches health, which is
  right — a corpse being dragged by a rope is a thing players do. The one moment it stops being right
  is a respawn, and that is `RespawnRelease`'s job, not a rope's.
- **The beam segment starts at the muzzle, not at `_rayOrigin`.** The trace begins at the holder's
  camera and the beam is drawn from the fist. Cutting along the ray would part ropes level with the
  holder's head that the visible arc never touched.

## Extending

A new cutter — a blade, a shear, a saw — implements nothing: it calls
`CuttableRopes.CutAlong(from, to, radius)` on the machine that already decides what it does to the
world, with the segment it actually reaches.

A new rope implements `ICuttableRope`, registers on whatever lifecycle seam it already has, and
returns its own shape and its own existing break. If a rope's break is not already a single method
that announces itself, write that first — `Cut` is not the place for a second break path.
