---
artifact: BottledSingularity
status: design
authority: Server
continuous: false
uses: [RepulsorBlast]
updated: 2026-09-07
---

# Bottled singularity (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

A thrown bottle that inhales everything within 8 m for 3 s, holds it in a knot, then flings it all
back out. It does not know who threw it.

## What it does

Throw the bottle. Where it lands it opens: for 3 s everything loose within 8 m is dragged toward the
bottle, pull strongest at the centre and falling off with distance. Props, creatures, players,
loose vehicles — no exemptions, including the thrower. Then it lets go in one outward impulse and
everything ragdolls away on a scatter that every machine draws identically.

The comedy is entirely in the lack of exemptions. Throw it short and you are part of the pile.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Radius | 8 m |
| Inhale duration | 3 s |
| Pull at centre / edge | full / roughly a quarter, smooth falloff |
| Outward impulse | Repulsor-class, scattered by the seed |
| Charges | 1 per bottle |

## How it is built

- `UseAuthority.Server`. `OnRequestUse` on the owner puts the throw origin in `P` and the aim in `R`;
  the bottle flies a closed-form arc so every machine draws the same throw.
- The bottle is a registered network prefab — it is a thing in the world that everyone collides with
  and that persists past the throw. The *effect* is not: the swirl, the dust and the debris are local
  `Present()` visuals.
- The pull and the fling reuse `RepulsorBlast`. That is a cone with an outward sign; this is a sphere
  with an inward sign for 3 s and then one outward frame. Same math, same ragdoll path — extract the
  shared part rather than writing a second one.
- The owner rolls one seed into `NetArg.B` and the outward scatter is derived from it by pure static
  math, the pattern `GravelBlastMath` and `DragonRocketFlight` already use.

## Multiplayer

Server-authoritative: it moves other people's bodies and other people's props. The exception is the
players it catches — a player's own movement is owner-authoritative, so the server tells each caught
player's machine to apply the pull, rather than writing their velocity itself. Writing it directly is
the silent failure this codebase already documents: applied on the server, overwritten a tick later,
nothing in the console.

## Persistence

An unthrown bottle is an ordinary item with a charge. A bottle mid-effect is 3 s of world state and
is not saved; a save taken during it loads with the bottle spent and everything at rest.

## Model and art

About 0.2 m — small, thrown, held in a fist rather than aimed down a barrel. Clean issued equipment:
a squat sealed flask with a machined collar and a black core behind thick glass, faint blue rim
light. Moving parts: the collar's iris opens when it lands, and the core visibly swells during the
inhale and snaps flat at the release. No hold pose — it is a bottle, and the firearm poses lie about
it, same call as the Lightning Spell.

## Risks

- **A player caught while riding.** A mounted rider's body is kinematic; pulling it does nothing.
  Either the pull ignores mounted riders, or it asks the vehicle through `ITowable`. Decide before
  building, and write down which.
- **Sucking a player through geometry.** A steady pull toward a point on the far side of a wall is a
  way into the terrain. Pull along a path that is clear, or stop pulling when blocked.
- **The bottle itself.** It must not inhale itself into a loop, and it must be destroyed cleanly on
  the server after the fling.
