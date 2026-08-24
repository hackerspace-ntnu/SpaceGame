# Sucker Puncher — build record

The steam-driven power fist, from the Power Fist concept sheet. An open frame the hand goes
*through*, a brass spine over the back of it, a hazard-striped guard over the mechanism, a segmented
knuckle block on a sliding carriage, and a boiler strapped to the forearm.

`models/gear/sucker_puncher.blend` → `Assets/Game/Art/Models/Items/sucker_puncher.fbx`
→ `Assets/Game/Prefabs/Items/Artifacts/Gadgets/SuckerPuncher.prefab` (built by
`Assets/Game/Editor/AssetPipeline/SuckerPuncherBuilder.cs`).

## The rebuild, and what it was fixing

The first version was a brass carcass — bulkhead, floor, two cheeks, front lip — **and a hand could
not get into it.** Two mistakes, both mine:

1. **The wrist end was sealed.** The bulkhead was a solid slab across the entire opening, carrying
   the comment "the wall the arm passes through", with no opening ever cut in it. The comment
   described an intent the geometry never had.
2. **The hand was never measured.** The cavity was sized by eye against human proportions, and the
   astronaut's hand is roughly 1.7x human — 0.176 m from wrist to knuckles against a human 0.10.
   Every dimension of that cavity was under the hand it had to hold.

Scale was **not** among the causes, despite an early diagnosis here that said it was. Comparing the
pose audit's `size` column against the model's own bounding box appears to show the item arriving
1.29x too large; that ratio is an artifact of comparing a *rotated world AABB* against an
axis-aligned local box, and `EquipItemSocket.ApplyScale` divides the rig's scale out before applying
the authored one. Both the old and the new model sit at world scale ~1.0. `holdSize` is now set
explicitly anyway — as a lock, not a fix — because a cavity sized to measured hand dimensions is
invalidated by any scaling at all.

The two real causes are now closed by construction rather than by care, because care already failed
once:

- `CAVITY` in `gauntlet_shell.py` is the hand as a volume, and `assert_clear()` refuses to save a
  component that puts a vertex in it.
- `audit_cavity()` in this model repeats the check over the **whole assembled model** at its final
  placements, and sweeps the ram through its full stroke as well as testing it at rest. The shell
  checking itself is not enough — the ram arm, the plumbing bracket and every placement in the
  assembly can put geometry back into the hand, and the original failure was exactly that shape of
  whole-model mistake.
- `holdSize` is set to the model's own longest axis, so world scale is exactly 1.0 and the numbers
  in these files are metres in the game.

The build now prints `hand cavity clear` and stops if it is not.

## The hand it is fitted to

From `Tools/SpaceGame/Items/Audit Held Item Poses`, in world metres:

| | |
|---|---|
| wrist → knuckles | 0.176 |
| knuckle span (index→pinky) | 0.113 |
| knuckle → fingertip | 0.099 |
| thumb base off the palm | 0.082 |

**Use the bone landmarks, not the mesh.** The astronaut rig has known weighting problems
(`project_astronaut_skinning`): stray vertices carry >0.5 weight on a hand bone while sitting out on
the forearm, and even a 2nd–98th-percentile box of the "hand" comes out 0.42 × 0.29 — a hand that
would be half a metre long. The bone numbers reconcile exactly with Unity (0.1763 measured in
Blender, 0.176 reported by the audit); the mesh bounds do not reconcile with anything.

## Origin: the grip point

`(0, 0, 0)` is the centre of the bar the fingers close on, which is where `HandGripFrame` seats the
item. That makes every landmark a known offset rather than a number to rediscover:

    wrist bone   y = +0.079   (handLength * GripDepthAlongFingers)
    knuckle row  y = -0.097
    back of hand z = +0.066
    spine deck   z = +0.082
    mechanism    z = +0.092   (10 mm proud of the spine — see below)

## Reused from the library

| Component | Object | Why it served |
|---|---|---|
| `components/props/arm_cuff.blend` | `Mesh_ArmCuff_Plated` | The plated bracer is the forearm mount, and its origin is already at the wrist. Scaled **1.18x** in the assembly: it is a 0.215 m component drawn for a human forearm, and this rig's forearm is 0.404 m and correspondingly thick, so at its authored size the bracer is swallowed by the arm it clamps. The scale is applied into the mesh, so the object still ships at scale 1.0. |
| `components/mechanical/panel_control.py` | `tube_path` | The library's shared swept-tube helper. |
| `RepulsorShockwave.shader` | — | The shockwave ring is the same event the repulsor draws. Shared shader, new material in the punch's own colour. |
| `RepulsorBlastRing.cs`, `FlungBody.cs`, `RepulsorBlast.Launch` | — | Unity-side reuse; see the artifact's docstring. |

## New components

### `components/mechanical/knuckle_block.blend` — the striking head

Origin at the **rear mounting face**; strikes along −Y. 0.13 × 0.115 × 0.075 m.

- `Coll_KnuckleBlock_Segmented` — **used here.** Four separate bars with a hardened cap each. Four
  bars rather than one grooved block because the gaps are real geometry: they hold a shadow line in
  silhouette and from the side, which is the angle a first-person item is actually seen from.
- `Coll_KnuckleBlock_Slab` — built ahead. One solid head with a diagonal cross rib; a breaching ram.
- `Coll_KnuckleBlock_Studded` — built ahead. Nine truncated cones; a breaker head.

### `components/mechanical/ram_slide.blend` — the linear mechanism

- `Coll_RamSlide_Rails` — **used.** Anchor plate, twin chromed rails, stop yoke.
- `Coll_RamSlide_Carriage` — **used.** Origin on the **rail axis at its centre of travel**, because
  a carriage is bolted to nothing — it is positioned by sliding.
- `Coll_RamSlide_Cylinder` — **used.** Steam cylinder, gland, rod, fittings.
- `Coll_RamSlide_SpringReturn` — **used.** A real helix, not a stack of rings: from the side a ring
  stack reads as a threaded bar, and the part's whole job is to look sprung.

Stated contract: 0.175 m of rail, a 0.062 m carriage, so **usable stroke ≈ 0.09 m**; the model uses
0.060.

**It hangs geometry below its own mounting plane** — bolt bosses and the bottom of the anchor plate,
~20 mm down. That is correct for a track bolted through a chassis and wrong for one sitting on a
hand, so the assembly stands the mechanism 10 mm proud of the spine and fills the gap with a spacer.
Bolting it flat put 128 vertices into the back of the hand.

### `components/props/gauntlet_shell.blend` — the frame

All four share the **grip point** as their origin, so they assemble by placement alone.

- `Coll_GauntletShell_Frame` — **used.** Spine, two side rails, three uprights a side, a knuckle
  bridge, a wrist yoke, and the grip bar. Five members and air; the gaps between the uprights are
  where the hand shows through, which is both what makes it fit and what makes it read as a machine.
- `Coll_GauntletShell_HazardPlate` — **used.** See below.
- `Coll_GauntletShell_WristCollar` — **used.** Sits entirely behind the wrist opening, so it never
  narrows the way in.
- `Coll_GauntletShell_Boiler` — **used.** Origin at the underside of its **saddle**, not the grip
  point, because it clamps to a forearm some way back and wants to be slid along it.

## Unique to this model

- `Mesh_SuckerPuncher_RamArm` — the short arm hanging the head off the carriage. It lives **entirely
  forward of the knuckle bridge** (y < −0.122). The first version ran two long struts down the
  outside of the hand to reach a head level with the fingers; that is moving steel either side of
  the knuckles, and it is what made the gauntlet a cage. Pushing the whole ram forward means the arm
  only ever occupies air.
- `Mesh_SuckerPuncher_Bracket` — plumbing, the cylinder's seat, and the mechanism spacer. Without it
  the boiler feeds nothing and the cylinder rests on air.
- `Marker_Grip` / `Marker_Fist` / `Marker_Vent` / `Marker_Gauge` — 4 mm cubes carrying coordinates
  across the FBX, as `portal_gun.blend` does. The builder adopts Grip and Vent and deactivates the
  rest. Fist and Gauge are exported for future use and nothing consumes them yet.

## The ram pivot — the one number Unity depends on

`Mesh_RamSlide_Carriage`, `Mesh_SuckerPuncher_RamArm` and `Mesh_KnuckleBlock_Segmented` all have
their origin at **`(0, −0.088, 0.092)`**. The prefab slides all three by one shared local offset
instead of carrying a rest pose per part; `SuckerPuncherBuilder` warns if they ever stop agreeing.

Blender −Y maps to Unity +Z under `_exportlib`'s flags, so the ram slides along the item's own
forward in both.

## Orientation: why the prefab carries a −90° roll

Blender **+Z is the back of the hand**, which in hand space is the item's **+X**, so
`ItemGrip.rotationOffset` is `(0, 0, −90)`.

`HandGripFrame`'s up is the *thumb side*. That is right for a gun — the sights sit thumb-side in a
pistol grip, which is why every other item in the project uses a zero offset — and wrong for
something worn over the hand. The frame's remaining axis is the palm normal, and for a right hand
the thumb sits on the index side of index→pinky, which puts the item's +X out the back of the hand.
Hence −90 rather than +90. If it ever reads mirrored, that is the single number to flip.

## Hazard stripes are geometry

45° bands clipped to the plate outline (Sutherland–Hodgman, in the plate's own plane) and extruded
1.2 mm proud. ~300 triangles, and it holds up in silhouette and under any lighting. A plain yellow
border frames the field — every real hazard panel has one, and without it the stripes run off the
edge and read as noise.

### The guard is bracketed from the sides, not footed underneath

A guard over a linear mechanism has nowhere to put a leg. Legs spaced along the plate — the obvious
first choice, and what this file did originally — stand inside the carriage's stroke, and the ram
drives through its own guard. Moving them to the ends does not help: the far end is exactly where the
ram arm ends up at full extension. So the load goes out to `BRACKET_X`, outboard of everything that
moves, and down onto the frame's own side uprights, leaving the plate a cantilever with nothing
beneath it. That is also how a real machine guard is built.

## Palette

One material added: **`Mat_Paint_Hazard_Yellow`** (`#C9A94E`, roughness 0.55, metallic 0.3).

Nothing served. The painted-enamel family (`Safety_Orange`, `White_Arctic`, `Coral_Faded`,
`Blue_Station`) had no yellow member at all; `Mat_Plastic_Safety_Yellow` is bright moulded plastic at
metallic 0 for trigger guards and pull rings, not a big weathered steel panel; and the only thing
within ΔE 20 was `Mat_Hide_Eye_Amber`, which is a wet eyeball.

Everything else off the shelf: `Metal_Brass_Tarnished` (spine), `Metal_Steel_Worn` (frame),
`Metal_Steel_Dark` (hardened faces, recesses), `Metal_Chrome_Scuffed` (rails, rod, fasteners),
`Plastic_Rubber_Black` (grip wrap, straps, hoses), `Paint_Warn_Red` (danger bands),
`Neutral_Black_Matte` (stripes), `Emissive_Amber` (gauge).

## No armature

Everything that moves does so as one rigid group along one straight axis, and that group already
shares an origin on it — the same capability as a single-bone rig, minus a hierarchy Unity would have
to unpick on import. Same call as `item_scanner.blend`.

## Dimensions and judgement calls

- **0.590 × 0.216 × 0.178 m**, 13.8k triangles. `holdSize` is 0.59 (the Y extent) — **re-measure it
  if the model's extents change**, or world scale silently drifts off 1.0 again.
- The gauntlet covers 87% of the arm from elbow to fingertip. That is faithful to the concept sheet,
  which shows the device running well up the forearm.
- **It is tall** — 0.216 m through, against a hand 0.104 m thick. That is structural: the mechanism
  has to clear the back of the hand, the carriage has to clear the rails, and the guard has to clear
  the carriage. Cutting it further means a lower-profile `ram_slide`, not a smaller frame.
- **Only the hero was built.** The sheet's Rebar Claws, Buzz Saw, Projectile Mine and Extender Fist
  are not modelled — but `knuckle_block.blend` is the file they belong in, and its rear mounting face
  is already the interface they would bolt to.

## Traps this build hit

All silent, all only visible in a render or an assert.

1. **`Part._absorb` claims the wrong faces.** Every `tube`/`torus`/`prism`/`loft` goes through it and
   it stamps a few faces of the *previous* part with the new part's material. The restamp pass
   corrected dozens of faces per component, so it is not theoretical. Fixed by `_tracked.py`, a new
   opt-in `TrackedPart`; `_buildlib.Part` is deliberately left alone, because a central fix would
   silently restyle shipped models.
2. **Coplanar faces z-fight.** The knuckle bars' hardened caps were flush with the bar fronts and
   rendered as a hatched mess; they now stand 0.8 mm proud. The mirror-image mistake is a detail
   buried 1 mm *inside* the surface it decorates, which renders as nothing at all — the original
   danger band was 1 mm inside the block.
3. **`matrix_world` is stale until the depsgraph ticks.** `place()` writes `obj.location`, and an
   audit that reads `matrix_world` in the same pass measures every object at its *pre-placement*
   position. The forearm cuff read as 354 vertices inside the hand while sitting 17 mm clear of it.
   `bpy.context.view_layer.update()` first.
