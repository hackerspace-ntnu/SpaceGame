---
system: SurfaceCoat
status: design
layer: items
summary: "A timed friction and material override painted onto a world surface — slick, ice, wet"
consumers: [SlickCan, CryoSprayer, StormFlask]
updated: 2026-09-07
---

# Surface coat (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

The world-facing twin of [StatusEffects](StatusEffects.md). A status hangs on a *body*; a coat hangs
on a *surface*. The Slick Can anchors this system, the Cryo Sprayer and Storm Flask reuse it.

## Model

- **A coat is a patch, not a flag on a collider.** Spraying a dune does not tag the terrain — it
  spawns a thin patch at the sprayed point with a radius, a kind and an expiry. Patches overlap and
  merge visually; the newest one wins where they cross.
- **Three kinds.** `Slick` (Slick Can, 20 s), `Ice` (Cryo Sprayer on liquid or wet ground,
  permanent until broken), `Wet` (Storm Flask rain, lasts as long as the cloud plus a little).
- **Movement asks the coat, the coat does not push movement.** A moving body queries the patch under
  its feet once per frame and scales its own grip. That keeps the player's own movement
  owner-authoritative, which is the codebase's rule; a server that wrote the player's velocity would
  be overwritten within a tick and nothing would report it.
- **Wheeled and legged movers ask the same question.** Vehicles and legged rigs read the same grip
  multiplier, so a slicked ramp is slick for everything that crosses it, not only the player.
- **`Ice` is geometry as well as grip.** Freezing a liquid makes it standable — the patch carries a
  collider, which the other two kinds do not.

## Multiplayer

- The server spawns and expires patches; every machine draws them. A patch is small enough to send
  as a position, a radius, a kind and an expiry, so no per-frame traffic.
- The grip multiplier is read locally by whichever machine owns the mover. Host and client reach the
  same answer because they are reading the same replicated patch.

## Persistence

`Slick` and `Wet` are not saved — both expire inside half a minute. `Ice` is the one kind that
outlives a session, so a frozen pool that a player has made a bridge of is worth saving as a small
record: position, radius, kind. That is the same shape as any other placed thing and can wait until
the Cryo Sprayer is actually built.

## Risks

- **Coats over a streaming boundary.** A patch belongs to the chunk under it. A coat sprayed on the
  seam between two chunks needs one owner or it is saved twice — the same rule every placed thing in
  this world already follows.
- **Free-falling grip.** A body in mid-air must not read a patch below it. Query only when grounded,
  or a player jumping over a slick pool skids in the air.
