# Settlement LODs: the Strider city and the Sky fleet — design

**Date:** 2026-10-05 · **Branch:** `Feat/lods-for-strider-and-sky-settlement` · **Status:** approved in conversation, awaiting spec review

## Intent (the user's words)

> "generate LODs for the sky settlement and the strider settlement. … for both lods we still want to use
> the particles, but for the LODs for the striders, the dust particles can be quite huge and not as many,
> so that we can 'hide' that it is LOD behind the dust clouds from moving vehicles."

Answered while designing: the Strider city should be **faster and visible from afar** — today it exists
only within `NpcWorldSim.spawnRadius` (250 m) of a player and is invisible beyond. The user does not
model LODs by hand: everything is generated.

**Success:** the live city and the Sky fleet cost a fraction of today's draw calls at distance; the
Strider city is seen marching (in dust) from the edge of the loaded ground and hands over to the live
city without a visible jump; particles run at every distance; host and clients see the same thing.

## Measured starting point (2026-10-05)

| Prefab | Renderers | Material slots | Triangles | LOD today |
| --- | --- | --- | --- | --- |
| `StriderHabitatWalker` | 1024 | 1024 | 113 k | none |
| `StriderDuneBarge` (×3 variants) | 428 | 1348 | 277 k | none |
| `DesertCrawler` | 112 | 382 | 286 k | none |
| `StriderCrabOutrider` | 77 | 258 | 171 k | none |
| `StriderMonowheel_*` | ~52 | ~73 | ~44 k | none |
| `SkyCityFleet` | 310 | 1694 | 1.2 M | `LODGroup`, LOD0 only (cull at 2 %) |
| `SkyFreighter` / `SkySkiff` / `SkyTug` | 10–12 | 55–65 | ~40 k | `LODGroup`, LOD0 only |

The cost is **draw calls** (renderers × material slots), not triangles. Ground exists only in the loaded
3×3 chunks (500 m chunks, `loadRadius` 1): at least ~750 m, at most ~1250 m from a player.

## Part 1 — Generated LODs (both settlements)

An editor tool, **Bake Settlement LODs** (`SettlementLodBaker`, `Tools/SpaceGame/Art/Bake Settlement LODs`),
run by each vehicle/ship builder after it builds, and runnable on its own. Per prefab:

- **LOD0** — the existing renderers, untouched and animated.
- **LOD1 (merged, self-simplifying)** — every non-particle renderer's mesh, in the prefab's rest pose (skinned meshes baked
  with `BakeMesh`), combined into **one mesh per material** under one child `LOD1_Merged`. Moving parts
  freeze; the dust hides it (Part 2).
  The merged meshes also carry **Unity 6.3 Mesh LODs** (`UnityEditor.MeshLodUtility.GenerateMeshLods`,
  verified present): the simplified levels live inside the same mesh and the `MeshRenderer` picks one by
  screen size on its own, so the merged level keeps simplifying as it recedes — no separate "LOD2" asset
  or level is needed.
- **Culled** below a screen size.

Meshes are saved as assets beside the prefab (`<Prefab>_LOD1_Merged.asset`), overwritten on every bake,
so a rebuilt prefab never keeps stale LODs. The transition height, the Mesh LOD count and the cull size
are serialized on a `SettlementLodSettings` asset (Inspector-tunable; no magic numbers). The Sky
prefabs' existing `LODGroup` is replaced by the generated one (its single LOD0 is today's behaviour).

**Particles are never in a LOD level**: `ParticleSystemRenderer`s are excluded from the merge and from
the `LODGroup`, so smoke and dust run at every level. Colliders, scripts and network components are
untouched — LODs swap renderers only, on every machine, with nothing replicated or saved.

**Rejected:** ticking `ModelImporter.generateMeshLods` per FBX (cuts triangles, keeps ~1000 draws per
house); billboard/octahedral impostors (complex bake, flat under parallax).

## Part 2 — Far dust (Strider vehicles)

Today `FootfallDust` and `RollingDust` fade **out** between `lodNear` 80 m and `lodFar` 200 m. Add a
**far-dust layer** on every Strider city vehicle (house, crawler, crab, barges, monowheels):

- A second emitter per vehicle built with the shared `DustCloudRecipe` (same sand material/tint): puffs
  ~4× larger (~8–13 m), ~⅕ the rate, longer life, emitted round/behind the hull, its rate driven by the
  vehicle's own ground speed (`GroundSpeedGauge`) — billows while moving, settles when parked.
- Crossfade: the near dust fades out over 80–200 m exactly as today while the far dust fades in over
  the same band and stays on to the vehicle's cull distance. Never both at full.
- Sky fleet: engine smoke unchanged at every distance.
- Tunables (size multiplier, rate, life, crossfade band) serialized on the component and set by the
  builders via the shared recipe. Visual only: each machine drives it from replicated motion; nothing sent
  or saved. Cost: a handful of very large particles per vehicle; overdraw is bounded because the far
  clouds are, by construction, far.

## Part 3 — The distant Strider city

- **Opt-in:** `NpcGroupTemplate.showFromAfar` (bool, default false); `WireStriderCity` sets it on
  `strider-city`.
- **Replication:** a `DistantGroups` NetworkBehaviour on the NpcWorldSim object (server-written,
  read everywhere) holds, for every opted-in group, `{ id hash, position, heading, moving, rosterSeed,
  spawned }`, written at a low rate (serialized, ~2 Hz) and interpolated on clients. Offline/host reads
  the same component, so there is one code path.
- **Silhouette:** each machine renders the folded city as the vehicles' **merged meshes** (their Mesh LODs pick the coarse levels at that range) placed in the
  city's formation slots (`FormationMath.SlotOffset` over `CityShape`) in the **same dealt order** the
  live spawn would use (`ColumnDeal` from `rosterSeed` and the template's cards — deterministic on every
  machine since the template is in the scene), each at the terrain height under it, with the Part 2 far
  dust per vehicle. Drawn with pooled `MeshRenderer`s (or `Graphics.RenderMeshInstanced`), no NetworkObjects.
- **Visible** only while the group is folded (`spawned` false), the camera is beyond `spawnRadius`, and
  there is loaded terrain under the slot (no city floating over the void); fades in/out inside its dust.
- **Hand-over:** the live city spawns into the same slots and order, at a distance where it renders its
  own merged level, so the swap is invisible apart from legs starting to move inside the dust.
- **Persistence:** nothing new — the silhouette is rebuilt from the saved group record (`rosterSeed`
  included). **Limit:** seen from at most ~1 km (the loaded ground); far-terrain rendering is out of scope.

## Testing

- EditMode: the baker produces the merged level with one submesh per material, Mesh LODs generated (`lodCount` > 1), and no particle renderers;
  every Strider city and Sky prefab has a `LODGroup` whose LOD0 is the original renderers and whose
  merged levels exist on disk; far-dust crossfade maths (pure); `DistantGroups` payload round-trip;
  silhouette slot layout equals the live spawn's for the same seed (same `ColumnDeal`).
- Renders: each prefab at LOD0, merged near, merged far, side by side; the city at 300 m and 700 m with far dust.
- Profiler counters (only with > 2.5 GB free RAM, via the low-memory counter scripts): draw calls with
  the camera 300 m from the city and from the Sky fleet, before and after.
- Manual, host + client: the distant city marches in dust and hands over without a jump; LODs and dust
  match on the client; save/reload restores the city where it was.

## Out of scope

Far terrain beyond the loaded chunks; LODs for other tribes' NPCs; impostors.
