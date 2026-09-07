---
artifact: CryoSprayer
status: design
authority: Server
continuous: true
uses: [StatusEffects, SurfaceCoat, SupplyCharge]
updated: 2026-09-07
---

# Cryo sprayer (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Freezes what it hits. Liquids become ground you can walk on. Foam and creatures become brittle
statues that hold whatever pose they were in.

## What it does

Hold Use and a plume of vapour comes out. What it lands on depends on what it is:

- **A creature or player** freezes solid — `Frozen`: no movement, no attacks, the pose held exactly
  as it was. It thaws after 10 s. A hard hit while frozen shatters it and kills it outright, which
  makes the sprayer half a crowd control tool and half an execution setup.
- **A liquid or wet surface** becomes `Ice`: a standable patch. That is a way across water and a way
  to make a slope no one can climb.
- **Foam** from the [Foam Gun](FoamGun.md) turns brittle and breaks apart on contact.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Range | 5 m |
| Cone half-angle | 15° — narrower than the flamethrower, this is a precision tool |
| Freeze time | 1.5 s of continuous spray on one target |
| Frozen duration | 10 s |
| Shatter threshold | One hit above a serialized damage floor |
| Tank drain / refill | 0.15 /s held, 0.06 /s idle |

## How it is built

- `UseAuthority.Server`, `IsContinuous => true`.
- **A frozen thing is a state swap, not a simulation.** The body is replaced by a frozen prefab
  posed from the live one — the skinned pose is snapshotted into the statue at the moment of
  freezing. Per-object freeze simulation is the expensive version of this and buys nothing.
- Statues are registered network prefabs; the swap and the swap back are server-side.
- `Frozen` is a kind in [StatusEffects](StatusEffects.md). The agent's own suppression is derived
  from the flag every frame, never written into the agent — writing it means a creature that reloads
  frozen forever.
- Ice patches are [SurfaceCoat](SurfaceCoat.md), which is also where the collider on an `Ice` patch
  is defined.
- Tank is a [SupplyCharge](../SupplyCharge.md) fraction with a `SupplyGauge`.

## Multiplayer

Server-authoritative: it despawns and respawns bodies. The frost build-up on a target before it
freezes is presentation, driven from the replicated progress so every machine sees the same thing
coming.

## Persistence

`Frozen` is not saved — a frozen creature reloads thawed, which is a small gift and the right
trade against a creature that reloads permanently stuck. `Ice` patches are the exception: they can
outlive a session and are a small record — position, radius, kind — which belongs to
[SurfaceCoat](SurfaceCoat.md), not to this item.

## Model and art

About 0.5 m, one-handed. Clean issued equipment in the kit's colours with a cold accent: a moulded
body, a ribbed cryogenic line running from a squat bottle to a finned nozzle, and rime frosting the
last few centimetres. `SupplyGauge` on the bottle. Moving parts: the fins turn white and frost creeps
back along the barrel while spraying, then clears when idle. The statue material is the other half of
the art job — pale blue, faintly translucent, with the original silhouette intact so you can tell
what you froze.

## Risks

- **Freezing a rider or a mount.** The rider is kinematic and parented into the seat. Swapping either
  one for a statue while they are joined is the case most likely to leave a body in a bad state.
- **The pose snapshot.** A statue that reverts to a T-pose is the obvious failure, and it will only
  show on a skinned rig, not on a crate.
- **Frozen and dead.** Shattering must go through the normal death path, not a special delete, or
  loot, saves and the death-control flag all disagree.
