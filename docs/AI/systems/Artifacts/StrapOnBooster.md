---
artifact: StrapOnBooster
status: design
authority: Server
continuous: false
uses: [DragonRocketFlight]
updated: 2026-09-07
---

# Strap-on booster (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

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
| Thrust | Enough to launch a player about 25 m; scaled by target mass |
| Crash damage | On closing speed, ornithopter curve |
| Charges | 1 per booster |

## How it is built

- `UseAuthority.Server` — it attaches to, and then moves, someone else's body.
- Attach is a small `BoosterMount` component holding the target Rigidbody, the local attach pose and
  the remaining burn. Thrust is a force at the attach point, so an off-centre clamp spins the target.
  That is the point of the item.
- The booster prefab is a registered network prefab: it exists in the world, attached to a thing
  everyone can see.
- The flame trail and smoke reuse the `DragonRocket` presentation path — local-only, owner seed,
  never a network prefab.
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

About 0.4 m, one-handed, carried like a thermos. Clean issued equipment: a moulded cylinder in the
kit's colour with a bell nozzle at one end, a hinged clamp jaw at the other, and a striped hazard
band around the middle so it reads as "this end goes on the thing". Moving parts: the jaws snap shut
on attach, the nozzle glows and gimbals slightly during the burn, and a red arming light goes out
when spent.

## Risks

- **Force on a kinematic body.** The single most likely silent failure. A booster on a parked vehicle,
  a mounted rider or a frozen creature will do nothing at all with no error. Decide what each of those
  means and handle it explicitly.
- **Clamping to a moving target.** The attach pose is in the target's local space, so a target that
  rotates carries the booster with it. Store the pose local, never world.
- **Griefing.** Strapping a booster to a teammate is the best use of the item and also the worst.
  That is a balance question for playtesting, not something to design out here.
