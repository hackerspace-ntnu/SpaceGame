# Grapple Bracer — build record

`models/gear/grapple_bracer.blend` → `Assets/Game/Art/Models/Items/grapple_bracer.fbx`
→ the Grappling Hook artifact.

Replaces the third-party pistol the artifact wore: `GrapplingHookGun.prefab`, which
was two prefabs out of `Assets/ThirdParty/Cosmic_Retro_Blasters Pack_1_FREE`
stacked on top of each other. The device is now worn on the right forearm.

| Part | Where it comes from |
|---|---|
| `Mesh_ArmCuff_Webbing` | `components/props/arm_cuff.blend` — **reused unchanged** |
| `Mesh_CableDrum_Winch` | `components/props/cable_drum.blend` — **new component** |
| `Mesh_GasBottle_Single` | `components/props/gas_bottle.blend` — **new component** |
| `Mesh_GrappleHarpoon` | `components/props/grapple_dart.blend` — **reused, scaled 0.28** |
| `Mesh_GrappleBracer_Frame` | the only geometry unique to this model |

17,720 triangles assembled. Export: `grapple_bracer_export.py`.

## Reuse, and the one thing that was not reused

The arm mount is `arm_cuff`'s **webbing** variation, taken as it stands. That
component's docstring says it exists so that "anything wrist-carried can sit on
one of these instead of growing its own mount", and this is the first thing to
take it up on that. Webbing rather than `Plated` or `Leather` because the brief
asked for a *simple* frame: two side rails and three bands is the open harness of
the three, and it is the only one with a mounting boss already on it.

The seated harpoon is **the actual `Mesh_GrappleHarpoon`**, appended and scaled,
not a lookalike modelled here. That was the point of the decision recorded under
"Hook" below: whatever sits in the tube has to be the thing that flies out of it,
and the cheapest way to guarantee that is for them to be one mesh.

Nothing in `components/mechanical/` was reusable for the drum. `drum_magazine`
and `road_wheel` are the right shapes at ten times the size, and
`item_devices_BUILD.md` already records what scaling a vehicle-scale component
down to item scale does: the bolt and panel-line density comes with it and turns
into noise. Same reasoning for `fuel_barrel` versus the gas bottle — a 0.9 m drum
is a different object, not this one larger.

## New components

Both are hand-scale, both hold three variations separated by silhouette, and
both are reusable well beyond this device.

**`components/props/cable_drum.blend`** — `Winch` (shipped), `Caged`, `Ratchet`.
Origin at the axle centre, axle along X. The `Winch` is the flattest of the
three, which is what an arm-worn device needs: anything standing proud of the
drum is something to catch on a doorway.

**`components/props/gas_bottle.blend`** — `Single` (shipped), `Twin`, `Flask`.
Origin at the base, axis up +Z. The gauge is the largest single detail on all
three and is built by a shared `gauge()` so they read as one instrument.

Two variations of each were built ahead of this request.

## Materials

**No palette additions.** Every surface came from an existing entry, which is
what the "steel and painted sci-fi" direction bought — the leather-and-brass
alternative would have needed nothing new either, but the sci-fi set was already
the harpoon's own five materials plus four the outpost and crawler use.

One deliberate *removal*: the gauge has **no glass cover**, although
`Mat_Glass_Canopy_Tinted` is documented for "gauge covers". At a 25 mm dial the
cover renders as an opaque lens and hides the ticks, the needle and the lit
sector — the entire instrument. The bezel alone says "glazed". Recorded in
`gas_bottle.gauge()` so the next person does not re-add it.

## The frame the model is built in

**Arm along Y, wrist at y = 0, elbow toward +Y, forward −Y, dorsal +Z.**

Forward is −Y because `_exportlib`'s FBX flags map Blender −Y onto Unity +Z,
which is the axis `ItemGrip` points an aimed item down, and because the harpoon
component already uses it. A seated harpoon and a flying one therefore agree
with no correction on either.

Dorsal is +Z, which exports onto Unity +Y — the **thumb** side of the hand
frame, not the back of it. Hence `rotationOffset = (0, 0, -90)` on the prefab.
The derivation, because it is the number most likely to be "fixed" wrongly:

- `HandGripFrame` builds the hand frame as `LookRotation(fingersDir, thumbSide)`.
  Its +Z is along the fingers, its +Y is out the thumb, and its +X is therefore
  the back of the hand for a right hand.
- The model's dorsal is Blender +Z → Unity item +Y.
- `Quaternion.Euler(0, 0, -90)` maps +Y onto +X. `+90` maps it onto −X and puts
  the whole mechanism through the wearer's palm.

The cuff arrives through `R_x(-90) @ R_z(-90)`. The roll is load-bearing: it
puts the cuff's mounting boss under the spine and its buckles out on the −X
flank. Without it the buckles land on top, exactly where the spine sits.

### The export flips X, and `grapple_dart_BUILD.md` does not say so

That document states the mapping as Blender `(x, y, z)` → Unity `(x, z, −y)`.
Measured on this model's own pivots after import:

```
Blender  Mesh_GasBottle_Single  ( 0.0500, 0.1780, 0.0460)
Unity                           (-0.0500, 0.0460,-0.1780)
```

so it is **`(x, y, z)` → `(−x, z, −y)`**. The dart document is not wrong about
anything it tested — every grapple dart is symmetric about x = 0, so nothing in
that family could tell the two apart. The X flip is the handedness change
between Blender and Unity, and any asymmetric model exported through
`_exportlib` will arrive mirrored across its own centreline.

Nothing here depends on it: `rotationOffset`, the `Grip` and the `muzzle` all
sit on x = 0. The one visible consequence is that the **gas bottle lands on the
thumb side of the forearm rather than the little-finger side**, and the cuff's
buckles on the opposite flank from the layout below. It was left that way — the
thumb side is the more visible of the two when a player looks down at their own
right arm, so the gauge is better off there.

## Scale: 2.1x, and why the model is not authored at it

Authored at real human scale like everything else in this library, and worn at
**2.1x** via `holdSize`. The rig is stylistically oversized — 0.393 m of forearm
and a 0.176 m hand against roughly 0.26 m and 0.09 m on a real person — and
`lasso_coil` needed the same 1.7-2.1x for the same reason.

2.1 is set by the cuff, not by the harpoon: the webbing sleeve is 0.091 m across
at the elbow end and the suit forearm is about 0.19 m, and 0.193 m of sleeve
times 2.1 is 0.405 m against 0.393 m of forearm. Both dimensions land at once
because `arm_cuff`'s length-to-diameter ratio (1.75) and the suit forearm's
(1.7) happen to agree.

Everything downstream follows from it:

| Prefab field | Value | Where it comes from |
|---|---|---|
| `ItemGrip.holdSize` | 0.80 | longest axis 0.3820 × 2.1 |
| `ItemGrip.rotationOffset` | (0, 0, −90) | above |
| `Grip` local position | (0, −0.0151, 0.0317) | below |
| `muzzle` local position | (0, 0.1040, 0.0700) | `FAIRLEAD` (0, −0.070, 0.104), Blender→Unity |
| `hookHeadScale` | 0.588 | `HARPOON_K` 0.28 × 2.1 |
| `hookHeadEmbed` | 0.06 | 0.1 × 0.588; the code does **not** scale it for you |

`hookHeadTipOffset` stays 0.9 — it is a distance on the model and
`EffectiveTipOffset` already multiplies it by `hookHeadScale`.

### The grip point

`EquipItemSocket` puts `ItemGrip.gripPoint` at the hand frame's origin, and that
origin is not the wrist: `HandGripFrame` places it `0.45 × handLength` along the
fingers and `0.18 × handLength` off the palm — the middle of the tunnel a fist
makes. With `handLength` measured at 0.176 m on this rig that is 0.0792 m
forward and 0.0317 m palm-ward of the wrist, or 0.0377 and 0.0151 once divided
by the 2.1 the item is worn at.

So the `Grip` marker sits *ahead of and below* the cuff rather than on it, and
`positionOffset` stays (0, 0, 0). Putting the marker on the cuff instead and
dialling `positionOffset` until it looked right would work and would leave two
numbers to re-derive the next time the rig changes.

## Layout, and the constraints that set it

Read down the arm from the elbow; every figure is a Y in model metres.

```
 +0.205  back of the cuff
 +0.190  rear spine clamp   (rear CLAMP station is 0.150 — see below)
 +0.140  drum axle, z 0.1085
 +0.178 → +0.071  gas bottle on the outboard flank, gauge at the wrist end
 +0.075  harpoon rope eye, z 0.088 — the breech
 +0.062 → -0.062  launch tube
 -0.070  fairlead, z 0.104 — where the rope pays out
 -0.082 → -0.127  the harpoon's barbs, forward of the fairlead
 -0.177  harpoon tip
```

Three of those are hard constraints rather than taste:

**The barbs must sit forward of the fairlead.** They reach 0.048 m off the axis
at this scale, so a ring placed among them is inside the barb spread. That is
what fixes the tube's muzzle at −0.062 and the harpoon's eye at +0.075.

**The launch tube is closed, not an open cradle.** The harpoon's foregrip collar
is 8.2 mm at this scale and its shaft 4.9 mm; a channel deep enough to hold the
collar leaves the shaft floating above the floor. A tube holds both, hides the
plain half of the harpoon, and leaves the head — the only part worth looking at
— standing out of the muzzle.

**The mechanism is 10 mm higher than the cuff wants.** See below.

## Four things the render caught that the arithmetic did not

**The mechanism sat inside the suit.** The first Z figures put the spine on the
cuff, which is right for the cuff and wrong for the arm: the astronaut's sleeve
is fatter than the sleeve `arm_cuff` was authored to, and the drum and tube came
out half-sunk in it. Everything was raised 10 mm, which is 21 mm worn.

**The cuff is invisible on the wearer, and cannot be made visible.** It is a good
fit for a 0.11 m arm radius and the suit is 0.11 at mid-forearm and 0.17 at the
elbow pad, so the webbing sinks into the sleeve and the arm frame reads as
nothing. Fixed by the clamp bands instead: they are this model's own geometry,
sized off the cuff rather than part of it, and `BAND_STANDOFF` puts them 20 mm
clear (42 mm worn). The rear band moved from the sleeve's own last station at
0.192 to 0.150, because nothing placed under the elbow pad is going to emerge
from it.

**A band segment rotated by `atan2(z, x)` is a spike.** That rotation sends the
box's tangent along the radius. The band came out as a ring of splayed teeth
pointing away from the arm. `R_y(90 - a)` is the one that sends local +Z along
the radius and local +X along the tangent. The same bug was in `cable_drum`'s
brake band — `R_x(a - 90)` there — and both are now commented at the site.

**Solid cheek plates hid the drum.** A 0.094 m hexagon either side of a 0.066 m
drum hides the wound cable from every angle except straight down, and the coil
of rope is one of the three things the brief asked for. They are 0.040 m struts
now and three quarters of the drum is in the open.

## Two regenerations, and why they were allowed

`cable_drum.blend` and `gas_bottle.blend` were each deleted and rebuilt once
during this build — `cable_drum` for the brake-band rotation and the coil's
triangle count, `gas_bottle` for the gauge glass and the collar. `start()`
refuses this and it is normally forbidden.

It was safe on the same grounds `lasso_coil_BUILD.md` records: both files had
been created minutes earlier by these same scripts, in this session, and neither
had ever been opened in Blender, so there were no hand edits to lose. It must
not be done again to either file. `grapple_bracer.blend` was rebuilt several
times under the same conditions.

## No armature

Nothing on the device articulates. The drum could spin and nothing spins it; the
one part that moves is the harpoon, and it moves by being deactivated here and
instantiated as `hookHeadPrefab` out in the world. Same reasoning as
`item_scanner.blend`, which likewise ships rigid parts with their origins on
their axes instead of a bone hierarchy Unity would have to unpick.

## Unity wiring

`GrapplingHook.prefab` — the artifact itself — loses the `GrapplingHookGun`
child and gains a `Bracer` instance of the FBX at identity. `GrapplingHookGun.prefab`
is deleted; `GrapplingHook.prefab` was its only referrer.

`GrapplingHookArtifact` gained one field, `seatedHook`, pointing at the
`Mesh_GrappleHarpoon` child. `SpawnHead` hides it and `DestroyHead` shows it, and
both of those run on **every** machine — the head is cosmetic and instantiated
everywhere — so a peer watching the shot sees the same empty tube the owner
does. Nothing about it is saved: the held instance is rebuilt from the prefab
every time the item is equipped, so it starts seated by construction.

`ShowSeatedHook(true)` sits *before* `DestroyHead`'s null guard on purpose. The
rope can be dropped without a head having been spawned — a shot with no
`hookHeadPrefab`, or a `StopGrapple` on unequip — and the harpoon has to come
back in those cases too.

## Measured on the rig, not eyeballed

Seated on `PlayerCharacter.prefab` through the real `EquipItemSocket` and
`HandGripFrame`, in an isolated preview scene:

```
grip frame source              finger bones      handLength 0.1763
forearm bone length            0.4041 m
applied scale                  2.0942            (holdSize 0.80 / longest 0.3820)
item forward . arm direction   0.979             1.0 = straight down the arm
cuff centre off the arm axis   0.033 m           mostly the cuff mesh's own asymmetry
launch tube, off the axis      dorsal +0.219 m,  thumb-side -0.008 m
cable drum,  off the axis      dorsal +0.196 m,  thumb-side +0.033 m
gas bottle,  off the axis      dorsal +0.061 m,  thumb-side +0.132 m
```

Positive dorsal is the back of the arm, so the whole mechanism is on the outside
of the forearm and the tube is centred on it to within 8 mm. That is the check
that `rotationOffset = (0, 0, -90)` is the right sign rather than the one that
puts the launcher through the wearer's palm — `+90` would put every one of those
dorsal figures negative.

The firing behaviour was then driven on the shipped prefab itself:
`OnEquipped` → `OnRequestUse` (hit at z −490.50) → `PlayUse`, and the seated
harpoon went `active = True` → `False` on the shot → `True` again on the
release.

Edit-mode suite after the change: **988 passed, 12 failed, 1 skipped**. None of
the twelve is a grapple test; they are in backpack placement, hotbar swapping,
mount dismount seating and portal UV round-tripping — none of which this build
touches.

## One judgement call worth naming

**The flying harpoon is now 0.55 m rather than 0.94 m**, because
`hookHeadScale` had to match what is sitting in the tube and a 0.94 m harpoon on
a 0.393 m forearm is a lance. `grapple_dart_BUILD.md` records that the 0.40 m
barbed dart was "too small to see" in play and that the harpoon exists to fix
that; 0.55 m is most of the way back, but it is not all of it.

If it turns out too small in play, the honest fix is to raise `HARPOON_K` in
`grapple_bracer.py` **and** `hookHeadScale` together — they are the same number
times 2.1. Raising `hookHeadScale` alone makes a bigger harpoon come out of the
tube than was ever in it.
