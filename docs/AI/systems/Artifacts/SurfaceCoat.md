---
system: SurfaceCoat
status: implemented
layer: items
summary: "A timed friction and material override painted onto a world surface — slick, ice, wet"
consumers: [CryoSprayer, StormFlask]
updated: 2026-09-09
---

# Surface coat

Read [../Artifacts.md](../Artifacts.md) first.

The world-facing twin of [StatusEffects](StatusEffects.md). A status hangs on a *body*; a coat hangs
on a *surface*. The [Cryo Sprayer](CryoSprayer.md) lays two of the three kinds and the Storm Flask
lays the third.

## Model

- **A coat is a patch, not a flag on a collider.** Spraying a dune does not tag the terrain — it
  spawns a thin patch at the sprayed point with a radius, a kind and an expiry. Patches overlap and
  merge visually; the newest one wins where they cross.
- **Three kinds.** `Slick` (Cryo Sprayer on anything else level enough to stand on, 20 s), `Ice`
  (Cryo Sprayer on liquid or wet ground, permanent until broken), `Wet` (Storm Flask rain, lasts as
  long as the cloud plus a little).
- **`Slick` and `Ice` leave the same grip, 0.03.** They are the same plume's cold and the difference
  between them is what is THERE, not how much of it a foot can use — a player who has learnt what
  frozen ground does to them should not have to learn it twice because one patch landed on water
  (GDC-L1-SYS-0006). `Wet` sits far away at 0.55 so rain and frost stay tellable apart.
- **Movement asks the coat, the coat does not push movement.** A moving body queries the patch under
  its feet once per frame and scales its own grip. That keeps the player's own movement
  owner-authoritative, which is the codebase's rule; a server that wrote the player's velocity would
  be overwritten within a tick and nothing would report it.
- **Every mover asks the same question.** Vehicles, legged rigs and NavMesh agents read the same
  grip multiplier through `GroundGrip`, so a frosted ramp is slippery for everything that crosses
  it, not only the player. A NavMesh agent applies it to its `acceleration` alone and never to its
  angular speed — see [NavMeshSystem](../NavMeshSystem.md).
- **`Ice` is geometry as well as grip.** Freezing a liquid makes it standable — the patch carries a
  collider, which the other two kinds do not.

## Multiplayer

- The server spawns and expires patches; every machine draws them. A patch is small enough to send
  as a position, a radius, a kind and an expiry, so no per-frame traffic.
- The grip multiplier is read locally by whichever machine owns the mover. Host and client reach the
  same answer because they are reading the same replicated patch.

## Persistence

`Slick` and `Wet` are not saved — both expire inside half a minute. `Ice` is the one kind that
outlives a session, so a frozen pool that a player has made a bridge of is saved as a small record:
position, radius, kind. That is the same shape as any other placed thing.

## Risks

- **Coats over a streaming boundary.** A patch belongs to the chunk under it. A coat sprayed on the
  seam between two chunks needs one owner or it is saved twice — the same rule every placed thing in
  this world already follows.
- **Free-falling grip.** A body in mid-air must not read a patch below it. Query only when grounded,
  or a player jumping over a slick pool skids in the air.
