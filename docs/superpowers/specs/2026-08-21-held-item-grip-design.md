# Held-Item Grip System

**Date:** 2026-08-21
**Status:** Complete. All five parts implemented, all eight prefabs tuned and verified.

## Problem

Artifacts held in a player's hand sit at the wrong angle, in the wrong place, at the wrong
size, and their colliders stay live on the hand while the player walks.

All four symptoms come from one method — `EquipItemSocket.Equip`:

```csharp
currentObject = Object.Instantiate(prefab, socket.position, socket.rotation, socket);
```

1. **Angle.** `socket.rotation` is the *hand bone's* world rotation. The rig is Mixamo
   (`mixamorig:RightHand`) on a humanoid avatar, so that rotation carries the FBX's own
   arbitrary axes — nothing to do with how a hand grips. Each artifact prefab's model
   orientation then stacks on top of it.
2. **Position.** The item's pivot lands on the bone origin. Pivots are wherever the artist
   left them (`RuinScanner` root at `y=0.14`, `RocketArtifact` at `y=0.1987`), so items float
   beside the hand. Only `Weapon` has a grip point (`Handle1`) and artifacts are not weapons.
3. **Size.** Nothing normalizes scale. Prefab `localScale` is inherited raw (`RuinScanner` is
   `0.5`, the rest `1`) and multiplied by the bone's `lossyScale`.
4. **Collision.** `Setup()` disables the **root** collider and root Rigidbody through
   `GetComponent` — not `GetComponentsInChildren`. `GrapplingHook`'s `muzzle` BoxCollider and
   every nested model prefab's colliders stay enabled on layer `Default`, sweeping the world
   on the end of the player's arm.

`EquipItemSocket` is the single seam both `EquipmentController` (player) and
`EntityEquipmentController` (NPCs) go through, so fixing it fixes both.

## Measured rig facts

Taken from `PlayerCharacter.prefab` in the editor, not assumed:

| Fact | Value |
| --- | --- |
| Hand bone | `mixamorig:RightHand` |
| `hand.lossyScale` | `(1, 1, 1)` |
| Head height | 1.396 m — sizes in metres are meaningful |
| `RightHandMiddle1` local pos | `(-0.020, 0.175, 0.004)` |
| `RightHandIndex1` local pos | `(-0.056, 0.177, 0.018)` |
| `RightHandPinky1` local pos | `(+0.053, 0.181, -0.007)` |
| `RightHandThumb1` local pos | `(-0.026, 0.077, -0.007)` |

Those finger offsets are **constant in hand-local space**, so they define the hand's frame
pose-independently:

- fingers point along hand-local **+Y** — and this, not the back-of-hand normal, is where a
  held item points; see "The forward-axis bug" below
- the fist's grip axis (index→pinky) is hand-local **X**, thumb side at **−X**
- the palm faces **+Z** (the sign comes from handedness, not the thumb — see below)

## Design

### 1. `ItemGrip` — authored on the artifact prefab root

| Field | Meaning |
| --- | --- |
| `gripPoint` | Child transform the hand closes around. Defaults to the root. |
| `rotationOffset` | Euler fine-tune, applied in socket space. |
| `positionOffset` | Metres fine-tune, applied in socket space. |
| `holdSize` | Longest-axis size in metres. `0` keeps the prefab's own scale. |
| `hand` | Right or Left. |
| `keepColliders` | Escape hatch; default off. |

Data lives next to the model it describes, which is the pattern the rest of this repo uses.

### 2. `HandGripFrame` — derives the socket frame from the rig

Builds a **canonical** frame from the finger bones rather than trusting bone axes:

- `fingersDir` — from the middle-finger proximal offset
- `gripAxis` — index→pinky
- `palmNormal` — `cross(gripAxis, fingersDir)`, sign from **handedness** (see the bug below)
- socket **up** = thumb side (where a torch's flame points)
- socket **forward** = along the fingers (where an aimed item points)
- socket **origin** = pushed out along the fingers into the middle of the fist, not the wrist

Because the frame is canonical, an `ItemGrip` tuned once transfers to every character. Baking
one rig's bone axes into each item would not.

Fallbacks, in order: finger bones → forearm→hand direction → serialized override transform →
raw bone axes.

### 3. `EquipItemSocket` rewritten

- instantiate as a child, then solve local TRS so the **grip point** lands on the socket with
  the item's axes aligned to the socket frame — angle and position solved together
- normalize against `socket.lossyScale` so `holdSize` is true world metres on any rig
- **sanitize recursively**: every `Collider` disabled, every `Rigidbody` kinematic with
  gravity off

### 4. Fallback for artifacts with no `ItemGrip`

Measure combined renderer bounds, scale the longest axis to 0.30 m, seat the bounds centre in
the palm. Every untuned artifact becomes immediately plausible; adding an `ItemGrip` makes it
exact. `Weapon.Handle1` folds in as an implicit grip point, so weapons keep working and the
special-case block in `EquipItemSocket` disappears.

### 5. NPC aim re-seat

`EntityEquipmentController.UpdateHeldItemAim` re-seats via `Handle1` every frame. That becomes
a generic grip re-seat so aimed artifacts — not just weapons — rotate about the hand rather
than about their own pivot.

## Scope

In scope: the five parts above, plus authoring `ItemGrip` values on the eight existing artifact
prefabs and verifying each in-editor.

Out of scope: a dedicated held-item physics layer (colliders are disabled instead), a custom
editor window, and two-handed grips (`Handle2` is untouched).

## Verified

Measured in the editor against `PlayerCharacter.prefab`, not assumed:

- The grip frame derives from **finger bones** (`source = "finger bones"`), is orthonormal
  (`dot(up, fwd) = 0.000`), and seats 0.085 m out from the wrist with the knuckles at 0.176 m —
  the middle of the fist.
- `dot(up, pinky->thumb) = 0.678`. That is the maximum achievable, not a shortfall: 72% of the
  raw pinky→thumb vector runs *down the fingers* because the thumb sits lower on the hand, so
  no axis perpendicular to the fingers can score above 0.693.
- All eight artifacts equip with **0 enabled colliders and 0 non-kinematic Rigidbodies**.
- Auto-fit scale factors ranged from 0.067 (`Lasso`, 15x too big) to 1.873 (`Leash`, nearly 2x
  too small), which is the size bug quantified.


## The palm-normal bug

Worth recording, because the first version looked correct by every numeric check and was wrong.

The palm normal has two possible signs and something has to choose. The first version asked the
thumb: a thumb folds across the palm, so it should lean the way the palm faces.

It does — but only just. In a rest pose the thumb lies very nearly *in* the plane of the hand.
On the player's rig its out-of-plane component is `0.009` against `0.017` sideways, so the sign
was being decided by the smaller number, and it decided wrong. The frame flipped: every held
item moved to the back of the hand, and every gun pointed into the palm.

Nothing in the orthonormality checks catches this. A flipped frame is still perfectly
orthonormal. It took a render to see it, and then arithmetic to confirm it — `cross(sideDir,
fingersDir)` in character space is `(-0.53, -0.64, +0.55)`, pointing **down**, which for this
A-pose arm is the palm. The unflipped cross had been right all along.

Handedness has no such ambiguity: fingers forward with index→pinky rightwards is a palm-down
right hand, and a left hand is its mirror. So the sign comes from `isRightHand` now. The thumb
is still used for the one thing it reports unambiguously — which *end* of the knuckle row is
the top of a held object.

Post-fix checks: back of hand faces up (`forward.y = +0.640`), grip sits below the hand plane
on the palm side (`off-plane y = -0.020`), thumb-side check unchanged at `0.678`.

## Final tuned values

| Prefab | Grip point | Rotation | Hold size | Held size measured |
| --- | --- | --- | --- | --- |
| AntiGravityPotion | (0, 0.055, 0) | — | 0.30 | 0.32 m |
| LightningSpell | (0, 0.050, 0) | — | 0.30 | 0.37 m |
| GrapplingHook | (0, −0.055, 0.030) | — | 0.45 | 0.42 m |
| GrapplingHookGun | (0, 0.057, 0.030) | — | 0.45 | 0.42 m |
| Leash | (0.009, 0.030, 0.012) | — | 0.26 | 0.41 m |
| RocketArtifact | (0.175, 0.135, 0) | 0, **180**, 0 | 0.40 | 0.51 m |
| RuinScanner | (0, −0.014, −0.010) | 0, **90**, 0 | 0.42 | 0.40 m |
| Lasso | `Cylinder` child | — | 0.35 | 0.32 m |

("Held size measured" is the world AABB of a rotated object, so it reads slightly above
`holdSize` — that is the diagonal, not an error.)

The two rotations: `RocketArtifact`'s launch tubes face −Z and would otherwise fire backwards
out of the hand; `RuinScanner`'s body, screen and antenna run out along −X from a handle at the
root origin.

All eight equip with **0 enabled colliders and 0 non-kinematic Rigidbodies**.

## Resolved: the Lasso

Originally a known content issue. `LassoArtifact.lassoModel` pointed at the whole
nested model prefab, which held a 4.4 m `BezierCurve` rope beside a 0.12 m
`Cylinder` handle - a 36x mismatch inside one held object that no grip setting
could reconcile.

Fixed by modelling the missing object: `components/props/lasso_coil.blend` ->
`Items/lasso_coil.fbx`, a hand-scale hemp coil with a whipped tie and a tucked
honda. `lassoModel` now points at it; the old 4.4 m visual is deactivated rather
than deleted, so the change is reversible. See `lasso_coil_BUILD.md`.

The Lasso's row in the table above is therefore superseded: `gripPoint` is now a
`Grip` marker at the tie, `holdSize` 0.45, `rotationOffset` (0, 0, 90).

## Follow-up worth doing

These values were judged against the rig's **bind pose**, where the fingers are splayed open and
the arm hangs at the side. In game the character plays a `Hold` animation (`HoldAnimator` drives
a `Hold` bool), so the final read will differ. Every value is a field on `ItemGrip` in the
Inspector, so adjusting one is seconds of work with the game running.

## The forward-axis bug

The second thing this system got wrong, found the same way as the first — by looking at it, then
measuring.

`forward` was the back-of-hand normal, reasoning that a pistol's barrel exits over the curled
index finger. Every artifact therefore pointed across the body instead of down it.

Reasoning about anatomy is what produced the error, so the fix came from the rig instead. The
project has a pose the artists authored specifically for holding a gun — `HumanM@Gun_Aim01`,
the state the `Hold` bool transitions to. Sampling it with `AnimationMode` and asking where a
barrel would end up is a direct measurement of the right answer:

| | forward error | up error |
| --- | --- | --- |
| back-of-hand normal (before) | **94.0°** | 33.1° |
| along the fingers (after) | **4.0°** | 33.1° |

A brute-force sweep of all 64 axis-aligned corrections picked `euler (0, 270, 0)` — a quarter
turn about the frame's own up. Worked through, that maps forward onto `cross(thumbSide,
palmNormal)`, which *is* `fingersDir`. So the correction has a plain meaning: **an item points
the way the fingers point.** The fingers curl around a grip, but the barrel carries on down the
line of the hand and forearm; nothing useful points out through the knuckles.

Taken as `fingersDir` directly rather than as `cross(thumbSide, palmNormal)` — the two are the
same vector on a right hand and negatives on a left, and the cross form would have left a
left-handed item pointing backwards.

**Why this class of bug survives self-checks.** A frame rotated a quarter turn is still
perfectly orthonormal, its origin still lands in the palm, and nothing throws. Every internal
consistency check passes. The only thing that catches it is agreement with something external —
here, the rig's own authored pose. That is what `GripFrameTests` pins.

The residual 33° is wrist cant in the authored pose, not an axis error; it is left alone rather
than fitted away against a single clip.

## Two bugs, one shape

Both defects in this system were sign-or-axis conventions derived by reasoning about anatomy,
and both looked correct until rendered:

1. the palm normal's **sign**, taken from the thumb's out-of-plane lean, which is the smallest
   and least reliable component in a rest pose
2. the forward axis, taken as the back-of-hand normal

The lesson is recorded because it generalises: for anything that has to agree with an artist's
rig, measure against the rig. Orthonormality proves nothing about which way is forward.
