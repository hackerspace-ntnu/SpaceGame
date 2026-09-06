---
system: SupplyGauge
layer: items
summary: "The fill bar on a tank or battery: built geometry, bound by name so a stripped copy reads too"
paths:
  - Assets/Game/Scripts/Items/Supplies/SupplyGauge.cs
  - Assets/Game/Scripts/Presentation/EmissiveLamp.cs
  - Assets/Game/Editor/Items/OxygenGearBuilder.cs
symptoms:
  - "the tank in my hand shows its level but the one on the pack mat always looks full"
  - "an empty battery still reads as part charged"
  - "the gauge changes colour but the bar never moves"
  - "I cannot tell how full a tank is without picking it up"
  - "the bottle docked in the plant does not show it filling"
  - "the bar is the wrong length or sits off-centre on its plate"
  - "the gauge flickers where it meets the model"
reads_with: [SupplyCharge, Oxygen, Backpack, Inventory, ArtPipeline]
updated: 2026-09-06
---

# Supply gauge

How full a reservoir is, drawn **on the object**: a fill that grows along a dark track and ramps
green → amber → red. The number it draws belongs to [SupplyCharge.md](SupplyCharge.md); this is only
how it is shown.

**Scope:** [SupplyGauge.cs](Assets/Game/Scripts/Items/Supplies/SupplyGauge.cs) ·
[OxygenGearBuilder.cs](Assets/Game/Editor/Items/OxygenGearBuilder.cs)

## Model

- **The BAR carries the reading; the colour only confirms it.** This replaced a flat tint of the
  whole gauge face, which put the entire reading in hue on the green-red axis — invisible to a
  red-green colourblind player, and what `GDC-L1-UX-0003` and `GDC-L1-UX-0006` name outright. Length
  is the channel that always works; the ramp is redundant over it. It is also why the battery's
  authored ladder was a defensible design first time round: a **count** of bars, not a hue.
- **The three stops are the palette's own indicator triad**, not colours invented here:
  `Mat_Emissive_Green_CRT`, `Mat_Emissive_Amber`, `Mat_Emissive_Red_Warn`. Two straight lerps meeting
  at `MidStop` (0.5), so the middle reads amber rather than the grey-brown a single green→red lerp
  crosses there. Half is where a phone battery turns, and borrowing the convention is free.
- **It is GEOMETRY on the prefab, not a shader and not a runtime tint.** Three boxes built by
  `OxygenGearBuilder`: a `Gauge_Track`, and a `Gauge_Anchor` whose child `Gauge_Fill` hangs off it by
  half a length. Scaling the anchor's own **+X** grows the fill from the low end — no shader, no UVs,
  no custom mesh.
- **Bound by CHILD NAME, never by component.** Two of the three things that draw a bar are display
  copies with every MonoBehaviour stripped off them. A name is the only handle they still have.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `SupplyGauge` | [Supplies/SupplyGauge.cs](Assets/Game/Scripts/Items/Supplies/SupplyGauge.cs) | The handle. `Bind` walks the hierarchy once, `Paint` does not. `Full`/`Mid`/`Low`/`MidStop`/`ColourAt` are the ramp. |
| `EmissiveLamp.Paint` | [Presentation/EmissiveLamp.cs](Assets/Game/Scripts/Presentation/EmissiveLamp.cs) | Tints the fill through a `MaterialPropertyBlock`, so the shared palette material is untouched. |
| `EmissiveLamp.Bake` | same | Writes a colour into a material **asset**. Only for materials this project generated — never a palette one. |
| `OxygenGearBuilder` | [Editor/Items/OxygenGearBuilder.cs](Assets/Game/Editor/Items/OxygenGearBuilder.cs) | Measures the gauge off the model and builds the bar. `MeasureGauge` is the geometry. |

## Flows

**Built.** `MeasureGauge` reads the emissive submesh's own vertices in the prefab root's frame. The
slab's shallowest axis is the one pointing out of the model, the longest of the other two is the one
the bar runs along. The rect is then mirrored to symmetry about the **gauge mesh's** middle, oversized
by `BarMargin`, and laid down as track and fill a fraction of a millimetre apart.

**Drawn.** Three call sites, one painter:

| Where | Who paints it | From |
| --- | --- | --- |
| In the hand, or lying in the sand | `DockableSupply.SetCharge` | its own `charge01` |
| Docked in the oxygen plant | `OxygenGenerator.RefreshTankVisual` binds, `DrawFill` climbs | `TankCharge` |
| Pack mat, ship gear wall | `PackContainer` on each copy it builds | `PackPlacement.Charge` |

A `SupplyCharge.None` is **skipped**, never painted as zero: the prefab already stands at the authored
starting charge, which is exactly what `None` means to read as.

## Multiplayer

N/A — display only. Every value it draws is already replicated by [SupplyCharge.md](SupplyCharge.md)
or by the machine's own `NetworkVariable`. The bar adds no state and no messages.

## Persistence

N/A — the bar holds nothing. Its length is a function of a charge that is saved elsewhere. The
**authored** length is serialized as the anchor's scale in the prefab, baked at `startingCharge`,
which is what makes a generated icon and an unpainted display copy read correctly.

## Gotchas

- **A `MaterialPropertyBlock` tells nobody anything outside play.** It is not serialized and `Awake`
  never runs on a prefab in the editor, so a block-only bar is right in play and wrong in every
  generated icon, on the mat and on the gear wall. The two materials are real assets, built as copies
  of the model's own gauge material so they inherit the palette's shader.
- **The TRACK is not decoration — without it an empty reservoir reads as full.** Both models light
  their gauge permanently: the bottle's contents strip is an emissive material, and the battery's
  ladder has three of five bars baked into the mesh. Nothing switches those off, so the track exists
  to *cover* them, and anything showing round its edge reads as charge that is not there.
- **The battery's bar is measured off three lit bars and MIRRORED to five** — about the middle of the
  **gauge mesh**, its housing. Mirroring about the whole MODEL was the first attempt and is wrong: a
  model's bounds centre is pulled about by everything it happens to contain, and the bottle's sits
  6 mm off its own gauge, which built a bar 36% too long and visibly off-centre — while the
  symmetric battery looked perfect. `SupplyGaugeTests.EveryShippedSupplyHasABarCoveringItsPermanentlyLitGauge`
  measures coverage off the built prefabs rather than trusting either number.
- **Nothing may meet on a plane.** The fill is twice the track's thickness so it is *buried* in it
  rather than resting on it; two parallel faces landing exactly on one plane is the flicker the model
  scripts themselves warn about. `BarProud` clears the model face by the same reasoning.
- **The bar's boxes must not keep the collider `CreatePrimitive` brings.** On an item that is picked
  up, dropped and stowed it would join the prefab's own fitted collider and change what the world,
  the pack and the interaction ray all think the shape is.
- **Build the bar LAST**, after the grip point and the fitted collider — neither should measure the
  instrument drawn on top of the model. `MeasureGauge` also scopes its `ItemBounds.Measure` to the
  model subtree, so a re-run cannot feed the previous run's bar into the next one's sizing.
- **A builder run over MCP can execute STALE code and report success.** Running `Build Oxygen Gear`
  before the Editor had recompiled rebuilt both prefabs with the *previous* geometry rule, logged
  `VERIFIED off disk`, and looked entirely correct. Only measuring the result caught it. Force a
  refresh and recompile first, then measure what was built — never trust the build log alone.

## Extending

A new reservoir needs no code here. Give its row in `OxygenGearBuilder.Roster` the mesh its gauge is
on and the emissive material to find it by — **the material, never a submesh index**, which is an
accident of the export order — and the bar is measured, built, coloured and verified by
`Verify()` with nothing further written.

To draw a bar somewhere new, `SupplyGauge.Bind(root).Paint(charge)` is the whole interface. Guard a
`SupplyCharge.None` before calling it.
