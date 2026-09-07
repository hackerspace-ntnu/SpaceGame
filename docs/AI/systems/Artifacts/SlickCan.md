---
artifact: SlickCan
status: design
authority: Server
continuous: true
uses: [SurfaceCoat, StatusEffects, SupplyCharge]
updated: 2026-09-07
---

# Slick can (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

A frictionless film. Spray it on the ground and nothing crossing it can stop. Spray it on a creature
and nothing it stands on will hold it — and nothing anyone throws at it will stick either.

This is the anchor of [SurfaceCoat](SurfaceCoat.md): the first item to make a surface property a real
thing in this game.

## What it does

Hold Use and a fan of film sprays out to about 4 m. On the ground it lays a `Slick`
[SurfaceCoat](SurfaceCoat.md) patch — a body that walks onto it keeps sliding, and a slope becomes
impossible. On a body it applies the `Slick` [status](StatusEffects.md): they cannot get purchase to
walk or turn, and ropes, lassos and nets slide straight off them. Both last 20 s and wear off.

Sliding off a rope is deliberate. The item is not "make an enemy fall over"; it is "make a thing
ungrippable", including by you.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Range | 4 m |
| Fan half-angle | 30° — wide, this paints ground |
| Patch radius | 1.2 m per dab |
| Duration | 20 s, surface and body alike |
| Grip while slick | About 0.05 of normal |
| Tank drain / refill | 0.12 /s held, 0.06 /s idle |

## How it is built

- `UseAuthority.Server`, `IsContinuous => true`. It changes shared world surfaces and other people's
  bodies.
- Ground hits lay `SurfaceCoat` patches, throttled so a held spray does not carpet a chunk in
  colliderless patches.
- Body hits apply the `Slick` status.
- **Movement reads the property; the property does not push movement.** A slick player's own machine
  scales their grip, because player movement is owner-authoritative. A server that wrote their
  velocity would be overwritten inside a tick with nothing in the console — the documented failure.
- The rope and net rejection is one check in the catch path shared by the lasso, the leash's hogtie
  and the net gun, not three.
- Tank is a [SupplyCharge](../SupplyCharge.md) fraction with a `SupplyGauge`.

## Multiplayer

Server-authoritative for the patches and the flags. Every mover reads them locally, so host and
client agree without traffic per frame. Nothing new on the wire.

## Persistence

Nothing saved. Everything this item makes expires in 20 s.

## Model and art

About 0.4 m, one-handed — the smallest of the sprayers, closest to an aerosol can in the hand. Clean
issued equipment: a moulded can with a wide fan nozzle, a trigger guard, and a `SupplyGauge` strip up
one side. Moving parts: the trigger, the gauge, and a nozzle that visibly widens its fan while
spraying. The film itself is the art job — a high-gloss, low-roughness wet sheen that only reads at a
grazing angle, plus a faint rainbow sheen so a player can see a patch before they step in it. If it
is invisible it is a defect, not a trap.

## Risks

- **Slick in mid-air.** A body must only read the patch under it while grounded, or a jump over a
  slick pool skids through the air.
- **Slick under a vehicle.** Wheeled and legged movers must read the same multiplier, or a rider
  crosses a rink the player next to them cannot.
- **A slick player who cannot recover.** 20 s is a long time to be unable to walk. Playtest whether
  the body duration should be shorter than the ground duration; the doc says one number today
  because there is no evidence yet for two.
