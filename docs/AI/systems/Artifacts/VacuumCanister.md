---
artifact: VacuumCanister
status: design
authority: Server
continuous: true
uses: [Containment, SupplyCharge]
updated: 2026-09-07
---

# Vacuum canister (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Hold it on something and it is dragged, resisting, into the bottle. Carry the bottle anywhere.
Uncork it and out they come.

## What it does

Aim at a creature, a player, a mount or a loose prop and hold Use. A resistance meter fills while you
hold and drains while you do not; the target can fight it. When the meter tops out the target is
gone — the canister is now a full canister, a different item, carrying that specific creature.
Uncork it anywhere and they are back, with the health, the faction and the state they went in with.

Anything up to a mount fits. A rider goes in with their mount or not at all.

A bottled *player* is never removed from the game: they mash to get out faster and are free after 5 s
regardless. That is the whole griefing answer — the bottle is a five-second inconvenience aimed at a
person, and a permanent container aimed at anything else.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Range | 8 m |
| Fill time, unresisting | 2 s |
| Player escape | 5 s, shortened by mashing |
| Size gate | Collider bounds against the canister's rated volume |
| Tank drain / refill | 0.2 /s held, 0.08 /s idle |

## How it is built

- `UseAuthority.Server`, `IsContinuous => true`. Capture despawns a networked entity, which is the
  server's business and nobody else's.
- All the hard parts are [Containment](Containment.md): the captive as a saved record rather than a
  hidden GameObject, the full canister as a distinct item identity, and the resistance meter, which is
  `SnareStruggleMeter` — already shared by the net gun and the leash's hogtie, and now by a third
  consumer rather than reimplemented.
- The suction beam and the target's slide toward the mouth are `PresentHold` visuals on every machine.
- The tank is a [SupplyCharge](../SupplyCharge.md) fraction — the canister runs out of pull, not of
  charges, so it is never consumed out of the hotbar.

## Multiplayer

Server-authoritative. The struggle messages already exist for the net gun and are reused unchanged,
including the rule that the captive's own struggle message goes on the *captor's* relay. A contained
player's camera sits at the canister with input suppressed, the treatment the hogtie already gives.

## Persistence

This is the one artifact in the set with real saved state. The captive's record rides the item's
`ItemState`, so it survives a save, a reload, the captor dying, and the canister being dropped and
looted by someone else. A record whose prefab id will not resolve on load must fail loudly — a
creature quietly deleted on load is exactly the silent failure the persistence rules exist for.

## Model and art

About 0.5 m, one-handed. Clean issued equipment: a wide-mouthed canister with a thick glass window
down one side and a heavy latch on top, `SupplyGauge` beside the latch. Moving parts: the mouth's
shutter irises open while sucking, the latch throws when full, and the glass shows the captive —
small, dim and moving — which is the whole joke and worth the extra work. A full canister and an
empty one differ by that window, so the player can read a shelf of them at a glance.

## Risks

- **A captive that is also a rider.** Rider and mount are one capture or no capture. Separating them
  silently loses one of them.
- **The captor disconnecting.** The canister drops with the record intact. This should work by
  construction, and is the first thing to test anyway.
- **Double release.** Spawning the captive and clearing the record are one server-side step.
- **Colliders on a despawned body.** Disabling a collider removes a body from every query, which is
  a trap this codebase has already been bitten by. Despawn properly; do not hide.
