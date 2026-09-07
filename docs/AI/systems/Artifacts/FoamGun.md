---
artifact: FoamGun
status: design
authority: Owner
continuous: true
uses: [StatusEffects, SupplyCharge]
updated: 2026-09-07
---

# Foam gun (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Hold to spray blobs that swell into solid geometry: a ramp up a cliff, a plug in a hole, a friend
encased to the neck. Everything it makes has a clock on it.

## What it does

Holding Use lays down dabs along the aim ray, throttled to about six a second, with a budget of 24
live dabs — the same shape as the Portal Spray Can, and for the same reason: a hold stream with no
budget is a way to fill a chunk with colliders. Each dab lands, swells over about 0.4 s into a
sphere, and merges visually with its neighbours into one lumpy mass you can stand on.

Foam that lands on a body is different from foam that lands on the ground. On the ground it is
terrain and lives 60 s. On a body it is `Foamed` — held in place — and lives 10 s, or less if
someone breaks it. That split is deliberate: being stuck should be a setback, not a sentence, while
a ramp you built should still be there when you have climbed back down.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Dab rate | 6 /s |
| Live dab budget | 24 |
| Blob radius | 0.45 m grown over 0.4 s |
| Terrain lifetime | 60 s |
| Encasement lifetime | 10 s |
| Tank drain / refill | 0.1 /s held, 0.05 /s idle |

## How it is built

- `UseAuthority.Owner` for the request, `IsContinuous => true`; the *blobs* are server-spawned on the
  throttle. The owner decides where the dab goes because only the owner has a live camera; the server
  decides that a collider exists in the world because that is shared state.
- Blobs are registered network prefabs. They are spawned objects, not `Present()` visuals — unlike a
  projectile, everyone has to collide with the same lump.
- No NavMesh carving. Agents avoid foam by physics, not by a runtime bake; a bake per dab is a cost
  this does not earn.
- Encasement applies `Foamed` from [StatusEffects](StatusEffects.md) rather than parenting or
  freezing the body — one system already answers "held in place".
- Expiry is the server's, on the blob itself. A blob that outlives its chunk unloading goes with the
  chunk.

## Multiplayer

The dab stream rides the existing hold path. Spawned blobs replicate as ordinary network objects, so
a client sees the same geometry it is standing on. Nothing new on the wire.

## Persistence

Nothing here is saved. Every blob dies inside a minute, so a save taken mid-spray loads a world with
no foam in it — which is honest and needs no record. If foam is ever made permanent, that decision
brings a save record with it and this section is wrong; say so in the same commit.

## Model and art

About 0.5 m, one-handed. A moulded pistol body with a wide bell nozzle and a stubby cartridge
under it, `SupplyGauge` on the cartridge. Moving parts: the cartridge shows the fill, and the bell
has a shutter that irises open while spraying. The foam itself is the more interesting art problem —
an off-white, faintly translucent material with a rough normal, so a mass of merged spheres reads as
one substance rather than a pile of balls.

## Risks

- **Blob count.** 24 live network objects per player, and several players spraying, is the first
  performance question to measure rather than assume.
- **Foam under the player.** Spraying at your own feet while standing there is a way to launch
  yourself or clip into geometry. Grow the blob rather than spawning it at full size, and let physics
  push out gently.
- **A ramp built over a chunk boundary.** Blobs belong to the chunk they land in. Half a ramp
  vanishing when a chunk unloads is a defect the player will read as random.
