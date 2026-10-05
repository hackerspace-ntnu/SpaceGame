---
system: VehicleDust
layer: vehicles
summary: "Sand dust off every Strider machine: footfall and rolling clouds near, one huge sparse far cloud at range"
paths:
  - Assets/Game/Scripts/Vehicles/Dust/
  - Assets/Game/Editor/Vehicles/VehicleDustWiring.cs
  - Assets/Game/Scripts/Locomotion/Core/Footfall.cs
  - Assets/Game/Editor/Tests/StriderDustEmitterTests.cs
  - Assets/Game/Editor/Tests/StriderDustPrefabTests.cs
  - Assets/Game/Editor/Tests/WalkerFootfallTests.cs
  - Assets/Game/Editor/Tests/FarDustTests.cs
  - Assets/Game/Art/Materials/Vehicles/SandDust.mat
symptoms:
  - "the walking houses, crawlers, crabs or barges throw no dust"
  - "the crab outrider's rider sits metres above its shell after a rebuild"
  - "a legged machine's dust thins out or stops at top speed"
  - "far away the walking city's legs are frozen and nothing hides it"
reads_with: [Striders, Monowheel, Locomotion, Vehicles, SettlementLods]
updated: 2026-10-05
---

# Vehicle dust

Every machine in the Strider city throws the monowheel's lingering sand cloud ([Monowheel.md](Monowheel.md)): the legged ones (walking houses, desert crawlers, crab outriders) from **every foot that lands**, the dune barges from **where their tracks meet the sand**. Two small runtime components drive [`DustCloudRecipe`](Assets/Game/Editor/Support/DustCloudRecipe.cs) clouds from what each machine is already seen to do; one editor helper wires them into the builders that own the prefabs.

## Model

- **One look.** Every cloud is `DustCloudRecipe.Cloud` (8-12 s life, drag 2.5, billow 4.2x, churn) in one shared `SandDust.mat`: the monowheel dust's tint (0.78, 0.66, 0.47) and `_SoftFade` 1.2 m, through `VehicleDustWiring.SandMaterial()`. The recipe itself is untouched, so the monowheels rebuild unchanged.
- **Footfall dust** (`FootfallDust`, one cloud per machine): each `Footfall` ([Locomotion.md](Locomotion.md)) throws a ring of `puffsPerFootfall` puffs born at the sole's rim, thrown outward and up (`upwardTilt` 0.8) at 3 m/s per metre of `FootprintRadius`, each born 0.9 footprint radii across (±20%). So the house's 1.8-2.6 m feet throw ~4 m puffs, the crab's 0.5-0.7 m feet ~1 m ones. Emitted with explicit position, velocity and size, so the cloud's own shape and start speed are unused.
- **Rolling dust** (`RollingDust`, one cloud per contact): each contact's rate is `rateAtFullSpeed` × its **own** measured ground speed over `fullSpeed` (the hull's cruise) × grounded. Per contact rather than per hull, so a barge pivoting on the spot dusts at its track ends and a standing one does not.
- **LOD** on both: full to 80 m from the camera, none past 200 m (`lodNear`/`lodFar`). NaN distance (no camera, a test) = full.
- **Far dust** (`FarDust`, one cloud per vehicle, child `FX_FarDust`): the recipe's cloud with puffs 4× (8–12.8 m) and life 1.5× (12–18 s), born across the hull's footprint and thrown up, at `FarDustRateFraction` 0.2 of the near dust's peak × the vehicle's own measured speed over `fullSpeed` (the city's 2.7 m/s march). Fades **in** over the near dust's own band (`IDustLodBand`: 80–200 m legged/tracked, 60–150 m monowheels) — the two always sum to 1 — and is off past `FarDustCullDistance` 1500 m (the Strider LOD cull, [SettlementLods.md](SettlementLods.md)) and with no camera.

**Budget** (cap = peak rate × the recipe's 12 s longest life, `DustCloudRecipe.CapFor`):

| Machine | Emitter | Peak | Cap | In the city | Worst case |
|---|---|---|---|---|---|
| House | `FootfallDust`, 6 puffs | 3 footfalls/s (measured 2.4 flat out, 2.5 turning) | 216 | 3 | 648 |
| Crawler | `FootfallDust`, 5 puffs | 5 footfalls/s (3.9 / 4.1) | 300 | 2 | 600 |
| Crab outrider | `FootfallDust`, 2 puffs | 10 footfalls/s (8.05 / 8.2) | 240 | 2 | 480 |
| Barge, Lookout | `RollingDust`, 8 contacts | 3 puffs/s each at 4 m/s | 36 each | 2 | 576 |
| Compact barge | `RollingDust`, 4 contacts | 3 puffs/s each | 36 each | 1 | 144 |

Far dust adds ⌈0.2 × peak × 18 s⌉ per vehicle (house 65, crawler 90, crab 72, barges 87/44, monowheels 72): ~1 200 very large puffs for the whole city, only ever drawn far away.

**2 448 puffs** at most; with the eight scouts' monowheel layers (340 per wheel) about **5 170 for the whole city at once**. That needs every machine at top speed within 80 m of the camera. The city marches at 2.7 m/s, under half a house's top speed: a house walking 10 s at that pace held 60 of its 216. The puffs are big, so the cost to watch is overdraw near the column, not the count (GDC-L1-TECH-0002).

## Key types

| Type | File | Role |
|---|---|---|
| `FootfallDust` | [FootfallDust.cs](Assets/Game/Scripts/Vehicles/Dust/FootfallDust.cs) | Order 150, after the legs. `Present(cameraDistance)` reads `LeggedLocomotion.Footfalls` once per `StepCount`, `Emit`s a ring per foot, returns the puffs thrown; plays a stopped cloud first (`Emit` adds nothing to one that is not playing) |
| `RollingDust` | [RollingDust.cs](Assets/Game/Scripts/Vehicles/Dust/RollingDust.cs) | `Present(dt, cameraDistance)` sets each contact cloud's `rateOverTime`: a [`GroundSpeedGauge`](Assets/Game/Scripts/Vehicles/Tracks/GroundSpeedGauge.cs) per contact (`MeasureAlongStep`; the same gauge `TrackBelts` reads the barges' side speeds with, [TrackBelts.md](TrackBelts.md)), `MonowheelGround.TryHit` ±0.6 m about it, LOD. `ResetBaseline()` after a snap |
| `VehicleDustWiring` | [VehicleDustWiring.cs](Assets/Game/Editor/Vehicles/VehicleDustWiring.cs) | `AddFootfallDust(root, puffs, peakFootfallsPerSecond)` (throws without a `LeggedLocomotion`), `AddRollingDust(root, contacts, cruiseSpeed, ratePerContact)`, `SandMaterial()`, `FootfallCap`, `AddFarDust(root, nearPeakRate, cruiseSpeed)` (throws without near dust) |
| `FarDust` / `IDustLodBand` | [FarDust.cs](Assets/Game/Scripts/Vehicles/Dust/FarDust.cs) | `Present(dt, cameraDistance)` sets the rate from `GroundSpeedGauge` on its own transform × `Fade(d, near, far, cull)`; on its own GameObject so a copy works alone (the distant silhouette instantiates it) |
| per-machine numbers | `StriderCityBuilder`, `DesertCrawlerBuilder`, `StriderCrabOutriderBuilder` | `PuffsPerFootfall`, `PeakFootfallsPerSecond`; `StriderBargeBuilder.TrackDustPerContact` |

## Flows

- **Build:** `StriderCityBuilder.BuildHabitat` (into the variant, not the RigWalker), `DesertCrawlerBuilder.Build`, `StriderCrabOutriderBuilder.Build` (after `AttachRider`) and `StriderBargeBuilder` (one cloud under each `TrackContact_*`, `TopSpeed` from the barge's `TrackedHullMotor`) call `VehicleDustWiring`. The player's `RigWalker` and the wild `CrabWalker6` get none.
- **Each frame, footfall:** locomotion `Step` (100) lands feet → `FootfallDust.LateUpdate` (150) sees a new `StepCount` → `Emit` per foot, scaled by LOD.
- **Each frame, rolling:** `RollingDust.Update` → per contact: step since last frame → smoothed speed (a step implying over 50 m/s is a snap and keeps the old speed) → grounded? → rate.

## Multiplayer

Presented on every machine from what that machine already sees: the legs simulate everywhere, owning or following the body, so `Footfalls` fills on clients too; `RollingDust` reads the replicated hull transform. No message, no networked state, nothing spawned, nothing to register.

## Persistence

N/A: no state worth persisting. Particles, speeds and the last `StepCount` are re-derived every frame; a load is a snap that `maxPlausibleSpeed` refuses to read as speed.

## Gotchas

- **A particle system is a `Renderer`.** `StriderCrabOutriderBuilder.AttachRider` sizes the shell from every `Renderer` under the root; with the cloud added first the rider's seat went up 11 m. Add dust after anything that measures renderer bounds. At runtime too: `WorldOverlay` skips non-mesh renderers, `VisorReticle.TryBounds` does not, so a bracket framing a whole dusty machine frames its cloud.
- **A footfall cap is a measured rate.** Past `PeakFootfallsPerSecond` the cloud is full and new feet throw nothing. Change a machine's legs or `stepDuration` (the outrider's 0.22 s steps double the crab's rate) and `StriderDustPrefabTests` re-walks it flat out and turning, and fails until the constant is raised.
- **`Emit` on a cloud that is not playing adds nothing**, silently: in edit mode, in a preview scene, or after something stopped it. `FootfallDust` plays it first.
- **The crossfade band is the vehicle's, not a constant.** The monowheels' near dust fades over 60–150 m, the others over 80–200 m; `AddFarDust` reads the band from the machine's own near emitter, so call it after that emitter exists.
- **Tunables are serialized on the prefabs.** Retuning a `FootfallDust`/`RollingDust` default changes nothing until the builders run again (INVARIANTS: serialized fields keep their old value).

## Extending

- **Dust on another legged machine:** call `VehicleDustWiring.AddFootfallDust` from its builder (after anything that measures renderer bounds), measure its peak footfall rate walking flat out and turning on the spot, and add it to `StriderDustPrefabTests.LeggedMachines`.
- **Dust on a wheeled or tracked one:** put ground-level contact markers on it in its builder and call `AddRollingDust` with its cruise speed.
- **Another look** (darker dust, snow): a second material through `DustCloudRecipe.Material`; change the cloud itself only in the recipe, and prove the monowheels rebuild unchanged.
