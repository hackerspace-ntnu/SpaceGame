# Flame Gauntlet — build record

A burner tube on the forearm's deck, fed from a drum on the outer flank, that throws one whole
burst of fire per press. The gauntlet family's fifth combat device, beside the puncher, the
repulsor and the blade.

`models/gear/gauntlet_flame.blend` → `Assets/Game/Art/Models/Items/gauntlet_flame.fbx`
→ `Assets/Game/Prefabs/Items/Artifacts/Gadgets/FlameGauntlet.prefab` (built by
`Assets/Game/Editor/AssetPipeline/FlameGauntletBuilder.cs`, fired by the lance's own
`FlamethrowerArtifact` with `commitToBurst` on).

## Reused from the library

| Component | Why it served |
|---|---|
| `_gauntlet.py` | `BASE_DECK_Z`, `BASE_WRIST_EDGE`, the deck contract and the envelope every device stands inside |
| `panel_control.tube_path` | the hose from the tank's outlet to the burner's rear union |
| `_buildlib` `cyl`/`loft`/`torus`/`rivets` | tube, flare, hoops, bolts — nothing bespoke |

No new component file: nothing here rides a second device. The burner is a tube with a flare,
and a tube with a flare is `cyl` + `loft`, not a part worth a `.blend` of its own.

## Decomposition

| Object | Separate because |
|---|---|
| `Mesh_FlameGauntlet_Cradle`  | the two saddles bolting the burner to the deck; the mount, not the gun |
| `Mesh_FlameGauntlet_Burner`  | the tube itself, 24 mm bore, elbow end to mouth |
| `Mesh_FlameGauntlet_Nozzle`  | the worn-steel flare at the mouth; the part that scorches and gets replaced |
| `Mesh_FlameGauntlet_Shroud`  | the vented heat shroud round the front third: six ribs, two rings |
| `Mesh_FlameGauntlet_Tank`    | the drum on the outer flank with hoops, gauge and valve |
| `Mesh_FlameGauntlet_Bracket` | the outriggers carrying the tank off the deck margin |
| `Mesh_FlameGauntlet_Hose`    | tank outlet to burner union |
| `Mesh_FlameGauntlet_Cover`   | the suit's safety-orange plate with the red arming stripe |
| `Mesh_FlameGauntlet_Lamps`   | two amber ready lamps beside the burner, emissive |

Nothing is merged; nothing moves; no armature. The fire is Unity's (the lance's particle rig,
attached under the `Jet` empty the builder creates at the muzzle).

## Numbers the Unity side consumes

| | Blender | Unity |
|---|---|---|
| `Marker_Grip` | origin | GripPoint |
| `Marker_Muzzle` (bore axis at the mouth plane) | (0, −0.070, 0.290) | (0, 0.290, 0.070) — `Muzzle`, the jet root's parent |
| `Marker_Pilot` | (0.020, −0.074, 0.320) | unused by the builder; the lance's `Pilot` system sits under the jet |
| `Marker_Gauge` | (−0.150, 0.200, 0.405) | unused for now; where a `SupplyGauge` readout would go |
| Bounds | 0.252 × 0.432 × 0.174 | `holdSize` 0.432 at 1.0× wear |

The nozzle's mouth is 70 mm forward of the wrist, inside the family's reach limit (y ≥ −0.240);
the tank's outer face is at x −0.198, inside |x| ≤ 0.210. `audit()` asserts the envelope and
the collar clearance and refuses to save otherwise. 2 420 triangles.

## Materials

All from the palette: `Mat_Metal_Steel_Worn`, `Mat_Metal_Steel_Dark`, `Mat_Metal_Chrome_Scuffed`,
`Mat_Metal_Brass_Tarnished`, `Mat_Plastic_Rubber_Black`, `Mat_Paint_Safety_Orange`,
`Mat_Paint_Warn_Red`, `Mat_Emissive_Amber`. Nothing added.

## Decisions a reader might want made differently

- The tank rides the **outer** flank (−X in the model, the thumb side once worn on the right
  arm mirrored), so the inner forearm stays clear of the body when the arm swings. It puts the
  gauge where the wearer cannot read it; a `SupplyGauge` on `Marker_Gauge` would need the
  camera, not the eye.
- The burst is sized by the tank, not by a timer on the artifact: 3 s of drain, must be full to
  light, 5 s to refill. Change the burst by changing `FlameGauntletBuilder.BurstSeconds`; the
  artifact code has no number for it.
- Range 8 m, half-angle 22°: longer and narrower than the lance's 6 m / 25°. A committed burst
  the wearer cannot cut short wants reach more than spread.
