---
system: SurfaceCoat
status: implemented
layer: items
summary: "A timed friction override painted onto a world surface — one kind left, wet ground"
consumers: [StormFlask]
symptoms:
  - "a coat kind I sprayed does nothing and lays no patch at all"
  - "the cryo sprayer leaves no frost circles on the ground any more"
updated: 2026-09-13
---

# Surface coat

Read [../Artifacts.md](../Artifacts.md) first.

The world-facing twin of [StatusEffects](StatusEffects.md). A status hangs on a *body*; a coat hangs
on a *surface*. **One kind is left — `Wet`, the Storm Flask's rain.** The
[Cryo Sprayer](CryoSprayer.md) laid the other two and no longer coats the ground at all.

## Model

- **A coat is a patch, not a flag on a collider.** Spraying a dune does not tag the terrain — it
  spawns a thin patch at the sprayed point with a radius, a kind and an expiry. Patches overlap and
  merge visually; the newest one wins where they cross.
- **One kind.** `Wet` (Storm Flask rain, 0.55 grip, lasts as long as the cloud plus a little).
  `Slick` (0) and `Ice` (1) were the cryo sprayer's and are **retired wire ids** — see the Gotchas.
- **Movement asks the coat, the coat does not push movement.** A moving body queries the patch under
  its feet once per frame and scales its own grip. That keeps the player's own movement
  owner-authoritative, which is the codebase's rule; a server that wrote the player's velocity would
  be overwritten within a tick and nothing would report it.
- **Every mover asks the same question.** Vehicles, legged rigs and NavMesh agents read the same
  grip multiplier through `GroundGrip`, so wet ground is slippery for everything that crosses it,
  not only the player. A NavMesh agent applies it to its `acceleration` alone and never to its
  angular speed — see [NavMeshSystem](../NavMeshSystem.md).
- **A coat is grip and a film, and nothing else.** No kind carries a collider now; the slab that
  made a frozen pool standable went with `Ice`.

## Key types

| Type | What it is |
| --- | --- |
| `SurfaceCoatField` | The session's patches, on the `NetworkGameManager` prefab. Sprays, expires, answers `GroundGrip`. |
| `SurfaceCoats` | The facade everything outside this system uses — `Spray`, `Break`, `Has`, `GripAt`. |
| `SurfaceCoatBehaviour` | One kind: its clock, footprint, grip, vertical reach and film material. |
| `WetCoat` | The only kind. |
| `SurfaceCoatPatch` | One patch in the world: a decal box, a radius and a clock. |

## Multiplayer

- The server spawns and expires patches; every machine draws them. A patch is small enough to send
  as a position, a radius, a kind and an expiry, so no per-frame traffic. `NetMsg.CoatSprayed` is
  `A` id, `B` kind, `P` centre, `R.x` radius, `R.y` seconds (0 = permanent).
- A joining client is restated every live patch as an ordinary announcement.
- The grip multiplier is read locally by whichever machine owns the mover. Host and client reach the
  same answer because they are reading the same replicated patch.

## Persistence

**Nothing is saved.** Every kind there is expires inside half a minute, and a quicksave that reloaded
a puddle the player had already walked past would be worse than no record — the same call
[StatusEffects](StatusEffects.md) makes about a body that is on fire. The `coats` save key and its
`SurfaceCoatSaveable` went with `Ice`, which was the one kind that outlived a session.

## Gotchas

- **`Slick = 0` and `Ice = 1` are retired, not free.** The kind travels as `NetArg.B`, so a new coat
  that reused either number would be read by an older peer as frost or as a sheet of ice.
  `SurfaceCoatKinds.Count` stays 3 for the same reason: it is the size of the per-kind array, and the
  hole at 0 and 1 has to keep being covered.
- **A coat sprayed with no field in the session does nothing and says so once.** The field belongs on
  the `NetworkGameManager` prefab; `SurfaceCoats` logs one warning and every query answers "bare
  ground".
- **A patch belongs to the chunk under its CENTRE.** A coat on a seam overlaps two chunks and has one
  centre, so exactly one chunk's unload takes it away.
- **A body in mid-air must not read a patch below it.** Query only when grounded, or a player jumping
  over a puddle skids in the air.

## Extending

A new coat is a new enum value (a NEW number — never 0 or 1), a new `SurfaceCoatBehaviour` subclass,
one serialized field on `SurfaceCoatField` and one `Slot(...)` line, plus `SurfaceCoatKinds.Count`.
A kind that needs to refuse a surface, carry a collider or be saved has to bring that machinery back
with it: all three were removed with `Ice` rather than left standing with nothing implementing them.
