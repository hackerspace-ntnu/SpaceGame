---
artifact: InflatorNozzle
status: design
authority: Server
continuous: true
uses: [StatusEffects, SupplyCharge]
updated: 2026-09-07
---

# Inflator nozzle (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Pump a target bigger and lighter until it drifts off the ground — then keep pumping and it bursts,
harmlessly, back to normal.

## What it does

Hold Use on anything: a crate, a creature, a player. It swells. As it swells its mass falls faster
than its volume grows, so it gets floaty, then buoyant, then it is bobbing away over the dunes with
someone shouting inside it. Stop pumping and it deflates on its own.

Over-inflate and it pops: a loud burst, a puff, and the target snaps back to normal size instantly.
No damage. The punishment for over-pumping is that you wasted the pumping.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Range | 5 m |
| Full inflation | 3 s of held pumping |
| Max scale | 2.5x |
| Mass at max | 0.15x — buoyant well before the pop |
| Deflate rate | Half the inflate rate |
| Pop | At the top of the range, held 1 s longer |

## How it is built

- `UseAuthority.Server`, `IsContinuous => true`. It changes another body's scale and mass, which is
  shared world state.
- **One signed scalar drives everything.** `Inflation` runs −1…+1 and both scale and mass are curves
  over it. Negative is the Ballast Bolt — heavier and smaller — so that item, when it comes, is this
  code with a sign flipped rather than a second implementation. Build the property, not the item.
- `Inflated` is a kind in [StatusEffects](StatusEffects.md), so its expiry, replication and creature
  reaction come from there.
- A creature's own movement has to cope with being 2.5x scale. Legged rigs measure their stride from
  leg geometry, so a scaled rig needs its locomotion re-measured rather than its animation sped up —
  or inflation is applied to the visual and the collider only, and the design says so.
- Tank is a [SupplyCharge](../SupplyCharge.md) fraction with a `SupplyGauge` on the model.

## Multiplayer

Server-authoritative for the scalar. Each machine derives scale and mass from the replicated value,
so nothing per-frame goes on the wire. An inflated *player* is the usual split: the server owns the
number, the owner's movement code reads it.

## Persistence

Not saved. Inflation drains to zero within seconds of being left alone, so a save loads everything at
its authored size. If a permanently inflated thing is ever wanted, that is a saved scalar and this
section is wrong.

## Model and art

About 0.5 m, one-handed, unmistakably a pump: a moulded barrel with a plunger grip, a coiled hose,
and a brass-collared nozzle at the end. A round pressure dial sits on the barrel — the one gauge in
the set that is a needle rather than a bar, because it reads the *target's* inflation, not the tank.
Moving parts: the plunger strokes while pumping, the dial needle climbs into a red arc before the
pop, and the hose flexes.

## Risks

- **Scaling a rig.** Scaling a skinned creature is not free — colliders, NavMesh agent radius and
  step height all follow. The cheapest honest version inflates the visual and a single proxy collider
  and leaves the agent alone; decide which before building.
- **Scaling a player.** Camera height, capsule and step offset all move. This is the case most likely
  to put someone through the floor.
- **Buoyancy.** Making something float means beating gravity, which is 18 here, not 9.81.
