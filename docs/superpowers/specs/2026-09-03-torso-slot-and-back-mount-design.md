# Torso gear: the lash rail, the swing, and a chest place — design

2026-09-03. Extends [2026-09-02-body-equipment-design.md](2026-09-02-body-equipment-design.md) and
[2026-09-02-body-screen-in-world-design.md](2026-09-02-body-screen-in-world-design.md). Reference:
[docs/AI/systems/BodyEquipment.md](../../AI/systems/BodyEquipment.md).

## The problem

On the gear screen the back slot was represented by `GhostBack`, a mount frame seated on the spine
and read from the front — so from the only camera the screen had, back gear looked like it went on
the chest. Nothing on screen said where it actually clips, the back held exactly one item kind, and
there was no chest place at all.

## What it becomes

**Back gear clips to the expedition rig's lash rail** — `Mesh_Rig_LashRail`, the bar across the
folded pack, 1.70 m wide on a pack body 0.86 m wide, so its two ends stick out past each flank with
the folded wing panels beside them. Picking up a back item on the gear screen swings the lens round
behind the player, and the rail lights up as the thing you click.

**The chest is a second place for the same slot.** An item's `EquipKind` says which place it takes;
you can wear one or the other, never both.

## Decisions

| Decision | Why | Rejected |
| --- | --- | --- |
| `BodySlot.Back` → `Torso`, one slot, two places | "One or the other" then needs no rule at all — a chest item displaces a back item through the swap every slot already does. Zero save migration: saves are positional and the wire carries the index, so only the name moved. | Two slots plus a mutual-exclusion rule in `GearMoves`; two slots with auto-swap. Both add a rule that can be forgotten. |
| One `Back` slot, wider catalogue | What was asked for: more items of the kind, not more slots. | Two independent slots, one per rail end; a strip of mount points along the rail. |
| Gear parents to the **spine bone**, positioned off the **rail** | The pack leaves — deploy it and anything parented to its rail is flung onto the sand. The bone is what the gear belongs to; the rail only says where on the bone to sit. | Parenting to the rail; hand-authoring an offset from the spine. |
| Position **measured** off the rail, never authored | No magic number, and it cannot drift: move the pack's worn pose or rescale the rig and the gear follows. A typed offset would keep the screen's ghost promising the rail while the gear slid off it, silently. | `WornFit.localPosition` tuned in play — which is what the wing pack had, and it is now only the fallback for a back with no pack on it. |
| Outline the **real rail**, not a ghost of one | The thing that lights up is the thing gear goes on (`GDC-L1-UX-0004`). A translucent stand-in over geometry that is already there would z-fight. | A new rail-stub ghost model. `GhostBack` survives as the fallback for a deployed pack. |
| Orbit as an **angle about world up** | Front and back are exact opposites, so lerping the two forward vectors passes through the zero vector — undefined yaw, snapped lens. An angle also keeps the horizon level for free. | Slerp; a second authored shot with a cross-fade. |
| Nothing schedules the swing | `FocusCamera` re-asks for `LensPosition()`/`LensYaw()` every `LateUpdate` and never samples them, so moving the heading *is* moving the camera — and the wall probe follows, because it sweeps along the same heading. | A flight coroutine, as `FlyIn`/`FlyOut` use. |
| Forearm sites go **unclickable** while orbited | A hit rect is projected bounds with no notion of occlusion, so both gauntlet boxes still sit on screen, over the pack. | Depth-testing the rects; colliders on the sites. |
| **No chest placeholder model** | The only way to reach an empty chest is to be carrying a chest item, and a translucent copy of that item on the sternum says more than a generic plate would. | Modelling a `GhostChest`. |
| No accessibility toggle for the swing | 0.35 s, eased, same class of motion as the 0.4 s fly-in that already ships. `GDC-L1-FEEL-0006`'s dosing clause is satisfied; its slider clause is aimed at stacking shake, which this is not. | A slider; an instant cut. Revisit if playtests report discomfort. |

## Shape

- `EquipKind` gains `Chest` (append-only, on the asset).
- `BodySlotRules.Accepts(Torso, ·)` takes `Back` **or** `Chest` — the whole exclusion mechanism.
- `BackSeat` → `WornSeat`: `BoneFor(kind, spine, chest)` picks the place, `Apply(…, mount)` takes
  the position off the mount when there is one. Shared by the wear and the screen's ghost, so the
  two cannot disagree.
- `BackpackController.GearMount` — the worn pack's lash rail, or null. One seam, read by both.
- `BodyFocusCamera.OrbitTo` / `FacingBack` / pure `Heading`.
- `BodySite` gains a borrowed **mount** it outlines but never owns, and a *place* derived from the
  carry first, then what is worn, then the back.

## Known gaps

- **No shipped item is `EquipKind.Chest`.** The mechanism landed ahead of its content: retagging an
  existing gauntlet would have taken it off a player's arm without being asked. Making one is two
  fields — see BodyEquipment.md's *Extending*.
- **Gear worn while the pack was deployed keeps the fallback offset** until any slot write re-seats
  it. The two positions are centimetres apart; live re-seating would have to run on every machine
  for every player.
