---
system: GrassInteraction
layer: presentation
summary: Grass and bushes reacting to what is in them — trampled trails, rustling bushes, plant bases, sheen
paths:
  - Assets/Game/Scripts/World/Environment/Foliage/GrassBender.cs
  - Assets/Game/Scripts/World/Environment/Foliage/GrassTrample.cs
  - Assets/Game/Scripts/World/Environment/Scatter/GroundContacts.cs
symptoms:
  - "the grass never parts and leaves no trail when the player walks through it"
  - "bushes never move when the player walks through them"
  - "grass grows straight through tree trunks and bush stems"
  - "trails stop updating or vanish while I work in the Scene view"
  - "a trail disappears as soon as I walk a little way from it"
  - "Compute shader (GrassCull): Property (_Trample) at kernel index (1) is not set"
  - "[GroundContacts] mesh is not Read/Write enabled, so the grass cannot find its base"
reads_with: [Grass, Foliage]
updated: 2026-10-27
---

# GrassInteraction

The [Grass](Grass.md) field and [Foliage](Foliage.md) react to what stands and moves in them: bodies
part grass and bushes and leave trails, painted plants root themselves in the meadow, and the wind
shows the blades' paler backs. All of it is picture only — nothing is networked or saved.

## Model

- **Bender** = a [GrassBender](Assets/Game/Scripts/World/Environment/Foliage/GrassBender.cs) on a
  body's root (0.9 m; player 1.3 m — 0.9 barely shows on 1 m tufts). The 8 nearest the camera part the grass live (`_BendStrength`), push
  bushes aside (foliage `_BendStrength`) and shake their leaves while moving (`_BendRustle`, full at
  2 m/s). **Only `PlayerCharacterNetworked` carries one**; before 2026-10-27 no gameplay body did.
- **Trail** = what benders leave behind: grass flattened (`_TrampleStrength` 1.2; > 1 lays it further over, no lower) and laid over in
  the direction walked, standing back up over `trampleRecoverSeconds` (40 s). Remembered only inside
  a `trampleWindow` (64 m) square that follows the viewer; grass outside it stands straight back up.
- **Plant base** = where a tree or bush painted with *Paint Trees* meets the ground: its LOD0 mesh
  below `plantBaseHeight` (0.6 m), radius = the 80th percentile of those vertices' distance, × the
  instance's width scale. The inner `plantBaseClearing` (60 %) grows no grass; out to
  `plantContactReach` (1.5 m) beyond it the grass is up to `contactGrowth` (profile, 0.3) taller and
  `_ContactShade` (material, 0.4) darker toward its roots. Decided at scatter time: free per frame,
  and fewer tufts where bases are cleared.
- **Sheen**: a blade bent away from the eye by a gust, or pressed flat by a trail, shows its paler,
  duller back: `lerp(colour, grey(colour) × _SheenLift, _SheenDesaturate)` by `_SheenStrength`
  (0.45) × height — so gusts roll across a field as a silvery wave. Per vertex, forward pass only.

## Key types

| Type | Role |
| --- | --- |
| [GrassBender](Assets/Game/Scripts/World/Environment/Foliage/GrassBender.cs) | `Gather(eye, time, positions, motion)`: nearest 8, and their measured ground heading + speed (smoothed by `speedResponse`) → `_GrassBenderMotion` / `_FoliageBenderMotion` (xy heading, z m/s). |
| [GrassTrample](Assets/Game/Scripts/World/Environment/Foliage/GrassTrample.cs) | The trail map: `ARGBHalf`, power-of-two side (256² over 64 m = 25 cm), **wrap-around window** — texel (i, j) holds the ground cell congruent to it, so following the viewer clears only the strip that came into view. Texel: x flatness, y foot height × x, zw heading. `Update` (one camera) and `Bind` (every camera). |
| `Trample` kernel ([GrassCull.compute](Assets/Game/Art/Shaders/Environment/GrassCull.compute)) | Per texel: forget cells new to the window, recover by `elapsed / recoverSeconds`, stamp each bender (smoothstep over its radius; turns the heading only above 0.3 m/s). `Trampled()` in `Cull` reads it bilinear per kept tuft. |
| [GroundContacts](Assets/Game/Scripts/World/Environment/Scatter/GroundContacts.cs) | Every painted plant's base per terrain in 16 m buckets; each prototype measured once. Optional on [PaintedGround](Assets/Game/Scripts/World/Environment/Scatter/PaintedGround.cs) (rocks pass none), which copies the nearby ones into `PaintedGroundPatch.Contacts` for the worker. |
| `GrassScatter.TryContact` | Per tuft: false inside a base's cleared core; else contact 1 at its edge easing to 0 at its reach (nearest plant wins). |
| `GrassPackHalves` / `GrassUnpackHalves` ([GrassShared.hlsl](Assets/Game/Art/Shaders/Environment/GrassShared.hlsl)) | Contact rides as the high half beside tint in `GrassInstance.y`, and beside patch in `GrassVisible.look.y` — neither struct grew. |

## Flows

1. **Each camera** (GrassField): gather benders for it → if it is the driving camera (`Camera.main`
   in play; `SceneView.lastActiveSceneView` while authoring) `GrassTrample.Update` dispatches
   `Trample` centred on it → `Bind` → every biome's `Cull`.
2. **Cull, per kept tuft**: `Trampled(root)` — outside the window → nothing; else flatness ×
   (1 − |root.y − foot| / `trampleHeightTolerance`) — → `sway += heading × flat`, `motion.y =
   max(bend, flat)`. The vertex shader lowers the tuft by `motion.y` and shows the sheen.
3. **Scatter, per tuft**: after paint and clearing noise, `TryContact` → skip, or grow and pack contact.
4. **Paint Trees stroke**: `GroundContacts.Refresh` sees the terrain's plant count change, waits
   0.3 s of stillness, re-reads, and reports the ground whose plants differ (± reach) as changed.

## Multiplayer

N/A — presentation only. Each client stamps trails from the bodies it sees; the player's bender rides
its replicated transform, so trails agree as closely as positions do. Plant bases come from the
`TerrainData`, identical everywhere. Nothing is spawned or networked.

## Persistence

N/A — deliberately: trails recover in 40 s and a reload starts with the grass upright; plant bases
are re-derived from the `TerrainData` on load.

## Gotchas

- **`Time.frameCount` does not advance in edit mode.** A once-per-frame guard on it froze the trails
  while authoring; the driving camera is chosen by identity instead.
- **Only one camera may move the window.** Following each frame's first camera let a Scene view
  beside the Game view drag it back and forth, clearing the trails every frame.
- **`_Trample` is bound for every camera's `Cull`**: unbound, Unity skips the dispatch and that camera
  draws no grass. The "not set" warning right after editing GrassCull.compute is the re-imported
  shader meeting C# that has not reloaded yet; it stops after the domain reload.
- **Foot height is stored × flatness**, so bilinear filtering averages heights instead of pulling
  them toward 0 at a trail's edge. Half precision: ~0.25–0.5 m at world heights of 512–1024 m.
- **The map side must be a power of two**: negative cells wrap with `& (size − 1)`; `%` on ints is
  slow on Metal and wrong for negatives.
- **Plants are re-read only when a terrain's plant count changes.** Moving one without adding or
  removing one leaves the old clearing until the field rebuilds.
- **A plant mesh without Read/Write** gets no base (warned once per prefab). gameScene's 6 are readable.
- **Contact must not draw random numbers**: a cell's sequence must stay identical, or a sparse storage
  level stops being a subset of a dense one and chunks reshuffle.
- **Bilinear headings can cancel** where trails cross; below 0.2 length the tuft lies along its own yaw.

## Extending

- **A body that leaves trails**: add `GrassBender` to its prefab root, radius ≈ its footprint. Creature,
  NPC and vehicle prefabs (131 agent bases) do not have one yet. Cost: only the 8 nearest are used;
  more only sort on the CPU.
- **A bush that reacts**: `_BendStrength` (push, m) and `_BendRustle` (shake, m; bushes use 0.08 ×
  push) on its foliage material.
- **Tuning**: material `_SheenStrength/Lift/Desaturate`, `_ContactShade`, `_TrampleStrength`; profile
  `contactGrowth`; field `plantBaseHeight/Clearing`, `plantContactReach`, `trample*`.
