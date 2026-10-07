# Desert monowheel, double: build record

`desert_monowheel_double.blend` is hand-built by the user from two copies of the Runner in
[`desert_monowheel.blend`](desert_monowheel_BUILD.md). Its rig was cleaned up and it was centred on
2026-09-24. **No script generates it. The `.blend` is the only source, so never regenerate it.**

## Layout

- The vehicle lives in `Coll_Monowheel_Double`. It's centred on x = 0 and faces −Y, with the ski at the front.
- **Left and right follow the rider.** They face −Y, so **L is +X**. The generated `desert_monowheel.blend` uses the opposite convention for its own L/R names, so don't copy names across the two files.
- There are two wheels, with hubs at (±1.1747, 0.5428, 1.8729). Each wheel has its own ring, 8 paddles and mounts, 3 rollers with arms, lower and rear tubes, seat posts and a footrest.
- **Centre parts, on x = 0:**
  - seat pan, seat cushion, backrest and back cushion
  - tiller and grips
  - front cowl and rear deck
  - tail panel and tail post with its caps
  - the ski (deck, rails, straps, runners, scrolls)
- **Mirrored L/R pairs:**
  - cheek frames
  - rear rails
  - side hoops (inner and outer)
  - belly pans
  - foot pedals
  - side seats (pan and cushion, one inside each wheel)
- **Passenger seat** (added by the user): `Mesh_PassengerSeatPan`, `Mesh_PassengerSeatCushion`, `Mesh_PassengerBackrest` and `Mesh_PassengerBackCushion`, behind the rider on the centre line.
- **Leg rest** (added by the user): `Mesh_LegRestPan` and `Mesh_LegRestCushion`, in front of the seat, centred.
- Every structural pair is mirrored across x = 0 to within 0.5 mm. To keep it that way, edit one side and mirror it. The luggage is deliberately *not* mirrored (see below).

## Luggage

Added by [`desert_monowheel_double_luggage.py`](desert_monowheel_double_luggage.py). That script is a record of how the luggage was made; don't re-run it, because it refuses once the luggage exists.

- **On the ski:**
  - The Hauler's rug-wrapped bundle, bedroll and lashing (`Mesh_Cargo*`). They're built by the single-wheel generator's `build_cargo`, so they aren't a second copy of that code.
  - A trade-goods sack on each side (`Mesh_CargoSackL` and `Mesh_CargoSackR`).
  - Everything is set on a plane fitted to the deck, which slopes about 13° nose-down in world space.
- **On the sides:** a saddlebag slung from each outer side hoop by loops, with body, flap and fittings (straps, brass buckles, hanger loops).
  - **Left:** a canvas bag with `Mesh_SideBedrollL` tied on top.
  - **Right:** a leather bag, with two trade-goods canisters roped to the hoop behind it (`Mesh_Canister0R` and `Mesh_Canister1R`).
- **Reused parts:** the sacks and canisters are appended from `components/props/trade_goods.blend`.
- **Clearance:** the side luggage now rides on its wheel pod and tilts with it. None of it enters a paddle sweep. The user has since moved the left saddlebag and bedroll inside the wheel, next to the hub, which is still clear of the paddles.
- **Gotcha:** a freshly created object reports an identity `matrix_world` until `view_layer.update()` runs. Composing a placement onto it silently drops the object's own origin offset. This is what first put the rug under the ski.

## Rig

- `Arm_Monowheel_Double` sits on the centre line. The armature object is tilted about −16° around X, as in the Runner.
- It has five bones:
  - `Bone_Chassis` at the armature origin.
  - `Bone_PodL` and `Bone_PodR`: one wheel pod per side, children of the chassis. They're static, and their head sits on the hub.
  - `Bone_RingL` and `Bone_RingR`: children of their pod bone. Rotating one about its own Y axis spins that wheel only.
- **Camber.** Each pod is tilted 7° (the user's saved choice) about the vehicle's long axis through its hub, with the tops of the wheels leaning **out**, as the user asked. It's baked into the pod and ring bones' rest pose, and stored as `camber_out_deg` on the armature.
  - To change it, rotate both of a side's bones (`Bone_Pod*` and `Bone_Ring*`) in Edit Mode about the armature's Y axis through the hub. Everything on the pod follows.
- **What's in a pod** (parented to `Bone_Pod*`):
  - rollers and roller arms
  - lower and rear cradle tubes
  - seat posts, footrest and foot pedal
  - side seat pan and cushion
  - belly pan and outer side hoop
  - that side's luggage (saddlebag, flap, fittings, bedroll or canisters)
- **What stays on the chassis:** the inner side hoops. They're welded into the cheek frames, ski and rear rails, so they can't tilt with a pod.
- **Clearance, measured at 4° out** (the file is now at 7°, which clips more; see below), checked with a full turn of each wheel in 15° steps (BVH triangle overlap). Each wheel touches two spots, and both already existed before the camber:
  - The cheek frame's front-bottom bar (y −1.6 to −0.5 m, z 0.5–0.9 m) is grazed by each paddle's inner end.
  - The rear rail's tip (y ≈ 2.0 m, z ≈ 1.15 m) sits inside the paddle path.
- **Tops-out tilt swings each pod's lower half inward.** Past 4° the wheel cuts deeper into the cheek frame (46 triangles at 5°, 66 at 6°). To tilt further, move those two frame pieces first.
- Each ring (`Mesh_RingL_Double` or `Mesh_RingR_Double`) is parented to its ring bone, with its origin on the hub. That ring's paddles and mounts are parented to the ring itself.
- Everything else is parented to `Bone_Chassis`. `Socket_Rider_Double` sits on the centre seat cushion, and `Socket_Passenger_Double` on the passenger cushion.
- **If a ring moves, its bone head must move with it.** A bone left behind makes the wheel orbit instead of spin, and nothing warns you.
- **The in-game spinner must turn the `Bone_Ring*` transform about its own axis, not about world X.** On a cambered wheel, world X would wobble the wheel.

## Left as the user made it

- `Mesh_FrontCowl_Double` and `Mesh_SkiScrolls_Double` use a local material called `Material`, not the palette.
- The hidden `Coll_Monowheel_Hauler` and `Coll_Monowheel_Patched` copies that came along when the file was copied were deleted on 2026-09-24.

## Variant: `Coll_Monowheel_DoubleWide`

This is a copy of the Double with more distance between the wheels and extreme camber. It has its own rig, `Arm_Monowheel_DoubleWide`, and every name ends in `_DoubleWide`. The base Double collection is hidden in the viewport, so use the eye icon to switch between them.

- **Separation:** each pod is moved 0.7 m further out (`pod_separation_m`), so the hubs sit at ±1.875 m.
- **Camber:** 20° out (`camber_out_deg`). Both are baked into the `Bone_Pod*` and `Bone_Ring*` rest pose, as in the base.
- **Clearance:** the combination was chosen by a full-turn scan.
  - At 20° with 0.5 m of separation, the pods still touch the cheek frames. From 0.7 m up, nothing touches.
  - Neither wheel touches anything while spinning.
- **Outriggers:** once the pods move out, their welds to the centre frame are gone. `Mesh_OutriggersL_DoubleWide` and `Mesh_OutriggersR_DoubleWide` bridge every joint the pod used to have, 7 per side:
  - seat posts to the rear rail and the inner hoop
  - footrest to the cheek frame and the inner hoop
  - belly pan to the rear rail
  - outer hoop to the ski rails
  - rear cradle tube to the rear rail
  - Each strut runs from the joint's point on the chassis to where that point went on the moved pod.
- **Foot pedals** are on `Bone_Chassis` in this variant, because they belong to the rider, not the wheels.
- **The right sack** (`Mesh_CargoSackR_*`), which the user moved inside the right wheel, rides on `Bone_PodR` in both variants.

## Export to Unity

Run [`desert_monowheel_export.py`](desert_monowheel_export.py) after any change. It's meant to be re-run, and it only reads the `.blend` files.

```
blender --background --python models/vehicles/desert_monowheel_export.py
```

It writes five files to `Assets/Game/Art/Models/Vehicles/Monowheel/`:
- `desert_monowheel_runner.fbx`
- `desert_monowheel_double.fbx`
- `desert_monowheel_double_wide.fbx`
- `desert_monowheel_hauler.fbx`
- `desert_monowheel_patched.fbx`

- **What's kept:** the rig (the ring bones are what spins in game), and the socket empties where players attach.
- **Mirrored parts:** `fix_inverted` repairs, at export time only, the parts that hand edits left mirrored. Three per double variant needed it: `RearRailR`, `SideHoopInnerL` and `SideHoopOuterR`.
- **Verified (2026-09-24):** re-importing each FBX puts every part within 0.1 mm of the source. Each ring is still parented to its `Bone_Ring*`, with its 16 paddles and mounts under it.
