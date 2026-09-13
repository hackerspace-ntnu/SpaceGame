---
artifact: StrapOnBooster
status: shipped
authority: Server
continuous: false
uses: [DragonRocketFlight]
updated: 2026-09-09
---

# Strap-on booster (design)

**Shipped.** This page is the original brief and is kept for the reasoning behind the decisions.
For what the code actually does, read [StrapOnBooster](../StrapOnBooster.md) — it is the governing
doc, and where this page and that one disagree, that one is right. Read
[../Artifacts.md](../Artifacts.md) first.

A one-shot rocket you clamp to any physics object — a crate, a hull plate, a saddled animal, a
teammate, your own back. It burns hard for 2 s along its own facing and then falls off, spent.

## What it does

Aim at something and use. The booster leaves your hand, clamps where the aim ray hit, and lights.
Thrust runs along the booster's forward axis as clamped, so **how you stick it on is the aim** — a
booster on the side of a crate makes the crate slide, one on the bottom makes it fly. Two seconds
later the burn ends, the clamp releases and the spent booster drops.

The ride hurts. Hitting something at speed applies crash damage on the closing speed, the same rule
the ornithopter uses, so strapping one to a friend is funny and expensive.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Burn duration | 2 s |
| Thrust | 40 m/s², about 95 m for a player *(shipped; the brief proposed ~25 m)* |
| Crash damage | On closing speed, ornithopter curve |
| Charges | 5 per pack *(shipped; the brief proposed 1)* |

## How it is built

- `UseAuthority.Server` — it attaches to, and then moves, someone else's body.
- Attach is a small `BoosterMount` component holding the target Rigidbody, the local attach pose and
  the remaining burn. Thrust is a force at the attach point, so an off-centre clamp spins the target.
  That is the point of the item.
- The booster prefab is a registered network prefab: it exists in the world, attached to a thing
  everyone can see.
- The flame and smoke are the jetpack's — a `JetFlame` cone at the muzzle and `JetSmoke` puffs.
  Local-only, never a network prefab. *(Shipped that way; the brief originally proposed the
  `DragonRocket` presentation path.)*
- **A clamped-on rider goes through the vehicle.** A mounted rider's body is kinematic and discards
  velocity in silence. A booster clamped to a rider asks the mount through `ITowable`, the way the
  grappling hook does; a booster clamped to the mount itself already talks to the right body.

## Multiplayer

Server-authoritative throughout: attachment, thrust and damage. The one split is a booster on a
*player* — their movement is owner-authoritative, so the server tells that player's machine to apply
the thrust rather than writing it. Crash damage stays on the server.

## Persistence

A carried booster is an ordinary item with a charge. An attached, burning booster is 2 s of state and
is not saved. An attached booster that has *not* been lit — if that is ever allowed — would need a
record; today it lights on attach, so it does not.

## Model and art

About 0.43 m, one-handed, carried like a thermos. Clean issued equipment: a moulded cylinder in the
kit's colour with a bell nozzle at one end and a clamp at the other, banded in warning red so it
reads as "this end goes on the thing".

*Shipped model:* `strap_on_booster.blend`, exported by collection —
`Coll_StrapOnBooster_Armed` and `Coll_StrapOnBooster_Spent`. The hinged jaw is **gone**: the clamp is
one fixed jaw, so `BoosterShell.jaw` is unset and `CloseJaw()` is a no-op. The single striped band
became three red bands (`Mesh_StrapOnBooster_BandFore` / `BandMid` / `BandAft`). The arming lamp is
still the readout — geometry present on the armed model and absent on the spent one.

## Risks

- **Force on a kinematic body.** The single most likely silent failure. A booster on a parked vehicle,
  a mounted rider or a frozen creature will do nothing at all with no error. *(Settled: a rider is
  handed to the mount through `ITowable`; everything else takes the clamp, burns and does not move.
  The flame is the feedback that the answer was "no".)*
- **Clamping to a moving target.** The attach pose is in the target's local space, so a target that
  rotates carries the booster with it. Store the pose local, never world.
- **Griefing.** Strapping a booster to a teammate is the best use of the item and also the worst.
  That is a balance question for playtesting, not something to design out here.
