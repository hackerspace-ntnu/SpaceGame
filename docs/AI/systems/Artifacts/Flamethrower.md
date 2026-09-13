---
artifact: Flamethrower
status: shipped
authority: Server
continuous: true
uses: [StatusEffects, SupplyCharge]
updated: 2026-09-08
---

# Flamethrower (design)

**Shipped.** This page is the original brief and is kept for the reasoning behind the decisions.
For what the code actually does, read [Flamethrower](../Flamethrower.md) — it is the governing doc,
and where this page and that one disagree, that one is right. Read [../Artifacts.md](../Artifacts.md)
first.

A held lance that throws a continuous cone of fire. What it touches keeps burning after the flame
moves on. The second reference continuous item alongside the Laser Staff, and the reason
[StatusEffects](StatusEffects.md) exists.

## What it does

Hold Use and a jet of flame reaches about 6 m ahead in a narrow cone. Anything caught in the cone
takes `Burning` — damage over time on its own clock, which means the flamethrower never resolves
damage per tick itself. Burning creatures panic: they break off whatever they were doing and flee
until the fire is out. That is the whole item. The interesting play is elsewhere — a burning creature
runs *somewhere*, and rain from the [Storm Flask](StormFlask.md) puts it out.

## Numbers to start from

| Knob | Value | Note |
| --- | --- | --- |
| Range | 6 m | Short. The item is a crowd tool, not a rifle |
| Cone half-angle | 25° | Wide enough to catch a group at close range |
| Burn damage | 4 /s for 5 s | Owned by `Burning`, not by this item |
| Tank drain | 0.15 /s held | About 6.5 s of continuous fire from full |
| Tank refill | 0.06 /s, after 1.5 s idle | Full again in about 18 s of not firing |

Every one of these is serialized on the item so it can be tuned in the Inspector.

## How it is built

- `UsableItem` subclass, `UseAuthority.Server`, `IsContinuous => true`. `WantsHold` stays false — the
  flame stops when the button does, unlike the laser staff's self-timed burn.
- `OnRequestHold` on the owner puts the aim ray in `P`/`R` from `AimProvider.GetAimRay()`. Never read
  `AimTransform.forward`: mounted, that points at the seat.
- `Hold` on the server sweeps the cone, applies `Burning` to every `StatusReceiver` it finds, and
  drains the tank. `PresentHold` on every machine drives the jet, the light and the sound.
- The final hold tick with `active == false` must stop the jet, including on a machine that never
  saw the ticks before it.
- Tank is a [SupplyCharge](../SupplyCharge.md) fraction on the item instance, drawn by a
  [SupplyGauge](../SupplyGauge.md) on the model. Empty means the jet stops, not that the item is
  consumed — override `OnMaxUsesReached` to stay silent so it is never removed from the hotbar.

## Multiplayer

Server-authoritative: fire applies a status to shared world state. Clients present the jet
immediately on the owner's machine so it does not wait for a round trip, and every peer draws the
same jet from the hold stream. No new messages — the hold path in `EquipmentController` carries it.

## Persistence

The tank fraction is `SupplyCharge`, which already survives equip, stow, drop, save and the wire.
`Burning` on a target is not saved — see [StatusEffects](StatusEffects.md). Nothing else on this item
holds state.

## Model and art

About 0.9 m, two-handed — the only long tool in the set, so the silhouette says "this one is
serious". Clean issued equipment: a moulded shell in the kit's colour with a fuel bottle clamped
under the barrel, a hose looping from bottle to muzzle, and a `SupplyGauge` plate on the bottle's
flank. Moving parts: the trigger, the gauge fill, a small pilot flame at the muzzle that idles when
the item is equipped and roars when fired. Hold pose is the two-handed firearm aim — it *is* shaped
like a firearm, so unlike the Lightning Spell the available pose does not lie about it.

## Risks

- **Ticking damage on the wrong machine.** The cone sweep and the status application both belong to
  the server. A client that also applies burn will not desync visibly, which is exactly why it will
  survive review — check it explicitly.
- **A jet left burning.** `holdTimeout` must exceed the 0.2 s keepalive. 0.5 s is the tightest
  shipped value and is the right one here.
- **Panic and targeting.** Making a creature flee has to go through the agent's own module, not by
  writing its destination. Two systems writing a NavMesh destination is a stall with nothing in the
  console.
