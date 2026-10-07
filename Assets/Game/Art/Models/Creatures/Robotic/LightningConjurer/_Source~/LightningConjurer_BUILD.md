# Lightning Conjurer — rig build record

Source: `../ConjuringRobot1 (2) (1) (1).blend`
Pre-rig backup: `../_Backup~/ConjuringRobot1.pre-rig.blend`
Export: `../LightningConjurer.fbx`

## What was wrong

The model had never been assembled into a rig. Specifically:

| Symptom | Cause |
|---|---|
| Parts don't follow the joints | No body mesh had a vertex group or an armature modifier. Only the finger meshes were attached to anything. |
| "Rotation is messed up" | The `Legs` armature object carried `rot Y = 180°` and `scale 1.8385`. Anything parented under it inherited a flipped, 1.84× transform. |
| No single skeleton | Three partial armatures — `Armature` (18 bones) and `Armature.001`/`Armature.002` (14) were two *hand* rigs; `Legs` (9) was a leg rig. No spine, no root, no connection between them. |
| Flipped shading | Negative scale on `RightLowerLeg` (y −0.74), `Cube.007`/`Cube.013` (z −0.12), `Icosphere`. |

## The rig

One armature, `ConjurerRig`, **at the world origin with an identity transform** — 19 bones
as built by `rig.py`, later 58 (walkerize.py's leg pins and hinges, then
hands_rebuild.py's thirty finger bones and the cast sockets, then staff.py's three):

```
Root
└─ Hips
   ├─ Spine ─ Head ─ Halo
   ├─ Thigh.L ─ Shin.L ─ Foot.L
   ├─ Thigh.R ─ Shin.R ─ Foot.R
   └─ ArmRoot.{L,R} ─ UpperArm ─ Forearm ─ Hand     (the free-floating arms)
                                            └─ Staff ─┬─ StaffRotor   (right hand only)
                                                      └─ StaffTip
```

Binding is **rigid bone-parenting**, not skinning: every one of the 52 parts is
100% attached to exactly one bone. That is the right answer for a mech — it needs
no weight painting and it never smears a hard edge — and it means *no mesh data
was touched*. All 73 meshes are bit-identical to the pre-rig backup (verified by
comparing world-space bounds of every vertex, max drift 1e-5).

The floating arms are rigged as floating. They sit at y = ±6.4, exactly symmetric
about the body centre line, so they are detached by design rather than broken.

### Two traps worth remembering

**Bone parenting anchors at the bone TAIL.** The parent matrix therefore carries a
`+Y` translation of `bone.length`. To keep an object exactly where it was:

```python
o.matrix_parent_inverse = P.inverted()   # P = bone.matrix_local @ Translation((0, len, 0))
o.matrix_basis = original_world_matrix   # NOT "leave it alone"
```

Leaving `matrix_basis` alone only works for objects that had no parent before.

**Detaching an action does not reset the pose.** Pose bones keep whatever the last
keyframe left behind — `Walk`'s final frame parks `Shin.R` at −35.7° — and the FBX
bind pose is captured from the *current evaluated state*. Zero `pb.matrix_basis`
explicitly before exporting or that bent knee becomes the rest pose.

## The hands

**Both hands were replaced.** The model shipped with two salvaged hand rigs and no
thumb between them: `Armature` (right, 18 bones, one skinned mesh `Hand.001`) and
`Armature.001` (left, 14 bones, thirteen loose meshes, missing every metacarpal).
The old `hands.py` could only ever give the right one working fingers, so the left
stayed rigid and just aimed.

They are now two copies of `components/mechanical/robot_hand.blend`
(`Coll_RobotHand_Five`): an opposable thumb plus four fingers, three phalanges each,
sixteen objects per hand. `restore_parts.py` seats them; `hands_rebuild.py` copies
their joints into `ConjurerRig` as `Thumb{1..3}.{L,R}`, `Index/Middle/Ring/
Pinky{1..3}.{L,R}` — thirty bones — plus `CastSocket.R`.

Nothing is deleted. Both legacy rigs and all their meshes sit in `WIP_Spares`.

### Bone-parented, not skinned — and that is a change

The old hand *had* to be skinned: it was ONE mesh, so weighting its vertices was the
only way to bend it. Every phalanx of the new hand is its own object with its origin
on its own hinge pin, and for that, rigid bone-parenting is strictly better — no
weights, no smearing at a knuckle, and the same treatment every other part of this
mech already gets. It also removes the double-transform trap the old file kept
tripping over, because nothing is now both parented to a wrist and skinned to bones
underneath it. The FBX contains no skinned meshes at all.

### Where the palm sockets sit

`CastSocket.R` and `CastSocket.L` are placed by `hands_rebuild.py` from each hand's
own geometry rather than typed: half a finger-length off the palm, along the palm
normal, level with the digits. The fingertips are splayed at rest and converge only
once Attack closes them, so a socket cannot simply be their centroid.

**There are two of them, and only one still does anything.** The right one used to be
the only one; the chest cast then made the left load-bearing too, because it arced to
both palms and fired from the point between them.

Neither is a muzzle any more. The staff cast fires nothing from the hands at all — the
bolt falls from the sky, and if `skyStrike` is turned off the line leaves `StaffTip`
instead. Both survive only because `hands_rebuild.py` builds them and nothing is
gained by tearing them out — `anim.py` briefly solved the free left arm's position on
`CastSocket.L`, and does not any more now that arm is held down. They cost one
transform each in the FBX. `CastSocket.R` rides the right hand's subtree, so
`pose_carry_arm` carries it along with everything else.

### Placement and scale

The component is authored 0.95 m long at library scale. It is grafted at **3.28
conjurer units per metre**, which is NOT the model's own 3.835: it is sized so the
new hand occupies the 3.12 units the legacy hand did. An anatomically-scaled hand
would come out 17% larger than the arms were drawn for, and matching the model beats
matching the anatomy.

Both hands are *authored* with the fingers pointing down (-Z) and the palm facing
forward (+X) — the orientation the original hands had, being flat in X and spread
in Y. The LEFT one still rests that way. The right one does not: `staff.py` poses
the whole arm into a carry so the fist can close round a vertical shaft without
breaking the wrist, which moves the hand's rest frame and the fist's bore with it.
See "The staff / where it sits" below, and the curl-axis note under Animation.

## Animation

30 fps. Idle and Walk are cycles whose last frame duplicates the first, so they loop
seamlessly. Attack is a one-shot, neutral to neutral.

- **Idle** — frames 1–120 (4.0 s). Hover: body breathes, arms drift out of phase,
  halo turns 90°.
- **Walk** — frames 1–53 (1.733 s), **foot-locked**, authored at exactly 8.99 m/s.
  See "The walk is solved from the foot" below; this is the one clip in the file that
  is not posed by hand.
- **Attack** — frames 1–135 (4.5 s). Four beats: the raise (f1–35), the charge
  (f35–105), the strike (f105–120) and the recovery (f120–135). The shoulder
  swings forward and up to −60°, the elbow stays flexed at about 38°, and the arm
  PRESENTS the staff forward and up rather than hoisting it overhead — the emitter
  goes from 4.5 units clear of the crown at rest to 11.2. It holds and trembles for
  two and a third seconds while the fan spins up; then the arm EXTENDS, the wrist
  straightens, and the staff, braced 20° back through the charge, drives toward
  upright as the bolt falls out of the sky at frame 120.

  **The bolt no longer leaves the creature, and that rewrote the clip.** Every pose
  in the old Attack existed to serve a muzzle: both arms came up to the chest ring,
  held either side of it, then threw forward into a steeple whose gap the bolt flew
  out of. There is no ring and no muzzle now — the lightning comes *down*, somewhere
  else — so what the animation has to sell is summoning rather than aiming, and the
  two look nothing alike.

  **It is a ONE-ARMED gesture.** The staff hand goes up and the left arm stays at the
  creature's side. An intermediate version had the free hand sweep out through the
  charge and snap down to point at the victim on the strike; it is gone. The left arm
  is still *keyed*, flat at neutral, rather than left out of the action — a bone with
  no curves in a clip holds its bind pose, which looks the same standing still but is
  not the same thing while the animator blends out of Idle or Walk, where the arm
  would drift from wherever the previous clip's swing left it.

  That puts the whole burden of the commit on the staff, which is why the
  straightening matters more than it used to: the shaft going from 11° off vertical
  to 2° is now the only thing that changes shape on the strike beat.

  The charge beat is shorter than the old 3.0 s only because the raise is longer.
  Hoisting a staff overhead cannot be done in the 0.5 s the old reach took without
  looking weightless.

  **The clip is 4.5 s and the module's `castSeconds` is 4.0 s.** Those used to be the
  same number, because the old clip landed its bolt on its final frame. They are not
  any more: `FireFrame` (120) is when the bolt leaves and `AttackFrames` (135) is
  when the arms are back at rest, with the recoil in between.
  `LightningConjurerBuilder` derives the module's timing from the first and the
  importer's clip length from the second. Conflating them fires the bolt half a
  second after the hands have already dropped.

  **The shoulder does not move, and the arm is posed by ANGLE.** Two earlier
  approaches are worth keeping, because both shipped.

  The first placed the hand by TRANSLATING `ArmRoot.R`. That works — the arms
  float, so ArmRoot is a free 3-DOF translation of the whole limb and the
  correction is exact rather than iterated — and it slides the entire arm bodily
  through space. Nothing else on the creature moves with it, so at full extension
  the shoulder ended up a couple of units out and up from where the torso expects
  it and the limb visibly tore off the body. `ArmRoot.R` is pinned at zero for the
  whole clip now.

  The second solved two-link IK for a target GRIP POSITION with the shoulder
  fixed. That is the right shape of answer for an arm that is *reaching* for
  something, and this one is not — it is holding a staff, and what has to stay
  true is a relationship between the joints rather than a point in space. The
  wrist is the whole of it. The staff is bone-parented to `Hand.R`, so the shaft's
  direction *is* the hand's; ask for a grip position and the hand's angle is
  already spoken for, so whatever is left over lands in the wrist. What landed
  there was 30–45° on top of a rest pose that was itself a 90° sideways crank, and
  the arm came out thrust straight forward with the fist turned out on the end of
  it. That is what the reference pass was asked to fix.

  So a pose is three ANGLES — `upper`, `fore` and `lean` — and the *position* is
  what gets checked rather than asked for.

  **The wrist is arithmetic, not taste, and this is the load-bearing idea in the
  clip.** Every rotation here is about world Y — the convention this file has
  always run on — and rotations about a common axis commute and add. The staff
  stands vertical at rest, so the shaft's lean off vertical is just the sum:

  ```
  lean = upper + fore + wrist      =>      wrist = lean − upper − fore
  ```

  Getting it wrong is not subtle: an arm swung to −150° with the wrist left alone
  lays the whole staff over at 150°, turbine pointing at the floor, in an attack
  whose entire premise is that it points at the sky.

  The useful consequence is that the wrist's **bend** — the angle between the hand
  and the forearm, which is what reads as a limb or as a break — depends on
  `wrist` **alone**. Everything above the wrist turns the hand and the forearm
  together and cannot change the angle between them. So the clip is free to move
  the arm as far as it likes, provided the three numbers sum to a small lean.

  | | upper | fore | wrist | lean | elbow | wrist |
  |---|---|---|---|---|---|---|
  | rest (the carry) | 0 | 0 | 0 | 0 | −48° | −42° |
  | hold | −60 | 10 | 30 | −20 | −38° | −12° |
  | strike | −72 | 22 | 40 | −10 | −26° | −2° |

  Elbow and wrist are **signed**, and negative is the direction they actually
  bend — flexion at the elbow, extension at the wrist. Both stay on the correct
  side of zero the whole way through, and the wrist only ever straightens from the
  carry. `build_pose` asserts both against the posed rig.

  **AN ELBOW ONLY BENDS ONE WAY, and that limit prices the whole clip.** An
  earlier version of this pass swung the shoulder through 150° to stand the elbow
  above the head, and it hyperextended the elbow to do it: +64° at the hold and
  +91° at the strike, which is **244°** and **271°** measured the way a person
  measures an arm. Snapped backwards.

  It shipped because the guard could not see it. The assertion measured
  `ud.angle(fd)` — the *unsigned* angle between the two bones, which is 0–180 by
  construction and reads the same for a joint folded 70° the right way and one
  folded 70° the wrong way. An unsigned angle cannot express the thing that was
  wrong, so it said 65 and 92 and passed. `sagittal()` and `joint()` now carry the
  sign, measured about world Y — the axis every joint here actually hinges on.

  The constraint is one line:

  ```
  elbow = FORE_REST + fore        (FORE_REST ≈ −48°, the carry)
  ```

  because `fore` **is** the elbow's rotation, on top of the shoulder's, about the
  same world Y everything else turns about. The shoulder cancels out entirely. So
  keeping the elbow flexed means keeping `fore` well under +48, and *that* is what
  caps the raise — not taste, and not the shoulder.

  **What is reachable once the elbow is honest.** The hand has to stay level for
  the shaft to stand up, so the forearm stays near horizontal, so the fist sits
  about eight units forward of the elbow whatever else happens; and the elbow
  cannot get far above the shoulder without `fore` going past the limit. Between
  them:

  - **The fist cannot get above the head.** A raise that puts it there needs
    either a hyperextended elbow or a forearm pointing up — and a forearm pointing
    up folds the *wrist* to 90° or more, because the hand is anti-parallel to the
    shaft by construction. Both were built and both are in the history as broken
    poses.
  - So the cast **presents** the staff forward and up. The grip travels from
    (6.4, 23.3) to (11.9, 31.1), and the emitter from 4.5 units clear of the crown
    to 11.2 at the hold and 12.5 at the strike — nearly triple, on an arm that
    reads as an arm.

  The strike then **extends** rather than folding further: elbow −38 → −26, wrist
  −12 → −2. That is a drive, and it is the right way round — the hyperextended
  version folded the elbow *tighter* on the commit, which is what a limb does
  recoiling, not striking.

  **The staff is braced BACK and comes toward upright on the strike** — lean −20°
  to −10°, so the turbine rises and sweeps forward through the commit. That is a
  change from the previous clip's 11° → 2° forward, and it is forced the same way
  everything else here is: leaning a twenty-unit shaft *forward* off a fist that is
  already in front of the body throws the turbine further out and a metre and a
  half DOWN, so the beat that is supposed to read as the commit dropped the only
  thing the eye is tracking.

  **The staff swings while the arm moves, and that is the lag tables, not a bug.**
  `lean = upper + fore + wrist` is an identity, so with the wrist on its own
  schedule the three cannot all be held: staging them apart is exactly what tips
  the shaft mid-move. It peaks around 24° back through the raise, which is what a
  twenty-unit staff being lifted looks like and is wanted. It is *not* wanted on
  the drop, where a wide spread threw the turbine forward past vertical in the
  five frames the recovery has — so `DROP_LAG`'s windows are tight and
  `RAISE_LAG`'s deliberately are not. An audit over every frame of all three clips
  puts the shaft's worst clearance against the body at 2.36 units, the elbow's
  whole range at −60°…−26° and the wrist's at −46°…−2° — both on the correct side
  of zero for every frame of every clip, which is the property that was missing.

  **The turbine spins in 120° steps, and that is a quaternion constraint rather than
  a style choice.** Rotation is keyed as a quaternion, which cannot represent more
  than half a turn between two keys: a key at 900° is indistinguishable from one at
  180° and Blender interpolates the short way round regardless. Multi-turn spin has
  to be spelled out one sub-180° step at a time, and the *spacing* of those keys is
  the speed — they close up through the charge and open out after the strike. 120°
  also happens to be seamless on a three-bladed fan. Same constraint that kept the
  old chest rotor on 60° and the halo on 90°.

  It is keyed about world **Z**, the shaft's axis at rest. `world_rot` expresses that
  in the bone's own rest frame, so the spin stays about the shaft however far the arm
  has swung — it does not become a wobble the moment the staff tilts.

  **What makes the motion read as organic** is not interpolation settings — the clip
  is sampled onto linear keys like the walk. It is two things in the *values*: a
  smoothstep, so every joint accelerates out of rest and decelerates into its target;
  and the per-phase lag tables (`RAISE_LAG`, `PEAK_LAG`, `DROP_LAG`), which start each
  joint later than the one above it and let the forearm and wrist overshoot and
  settle. A shoulder, elbow and wrist moving in lockstep read as one rigid lever. The
  raise windows are more than twice the old reach's, because they are lifting a staff
  the height of the creature rather than moving an empty hand a few units.

  **The right hand grips in every clip, Idle and Walk included.** The staff is
  bone-parented to `Hand.R` permanently, not just during a cast, so a hand that opens
  between casts is a hand the staff hangs off with the fingers splayed around
  nothing. Two keys per action is enough, since the value never changes. `anim.py`
  writes them at the end of the Attack section rather than in the Idle and Walk
  blocks only because `CUP_CURL` and the digit helpers are defined there; moving them
  above IDLE would be tidier and a much larger diff.

  `GRIP_CURL` is 0.85 of a full cup, and `staff.py` holds the same pair of numbers —
  it poses the hand with them to place the shaft on the circle the closed fingers
  make. The two files are separate Blender runs, so the table is duplicated rather
  than shared; if they drift apart the check simply measures a hand the creature
  never makes, so keep them in step.

  **The curl axes are measured, not written down.** A finger closes about the
  knuckle line — the *thumb* axis — and the thumb closes about the *finger* axis;
  both are facts about a hand, so they hold whatever pose the rest of the rig is
  in. The world axis *letters* they line up with are not, and both `anim.py` and
  `staff.py` used to write down the letters, with a note warning they had moved
  once already when the right hand was first turned and would have to be kept in
  step by hand if it were ever turned again. It was: the right arm now rests in a
  carry, and the letters are different again. `hand_frame()` reads the two axes off
  the rig in both files, and the sign flip between them is just chirality. Curling
  on a stale axis barely moves the fingers at all, which is a failure that reports
  nothing — hence measuring it.

  **The left hand is not posed at all.** It used to open through the charge and close
  to a point at the strike. With the arm held down, a hand gesturing on the end of a
  limb that is not moving reads as a twitch.

  `ConjurerCastModule` claims `IFacingModule` for the whole cast, so the *body* keeps
  turning to face whatever is about to be struck. A baked clip cannot aim.

The halo turns 90° per loop, not 120°, because the cube has 4-fold symmetry — 120°
would visibly pop at the seam.

### The walk is solved from the foot

The creature used to glide, and the glide was in the parameterisation rather than in
the tuning. The old cycle drove the JOINTS — `thigh = 24°·sin(t)`,
`knee = 34°·max(0, sin(t − 1.2))` — and then dropped the body onto whichever ankle
came out lower, hoping the feet would follow. Two things went wrong, and neither is
fixable by moving those numbers:

* **A sinusoidal thigh has zero angular velocity at each extreme.** Right where the
  foot is planted hardest, it stops, and the body keeps going.
* **Riding on the lower ANKLE is not riding on the stance leg.** The ankle is lowest
  when the leg is nearest vertical, so the tie-break handed the ground to the wrong
  leg for the first third of every stance. The planted foot slid *forward* 4.6 units —
  about 2.4 m — and then reversed.

So the foot is the input now. Each leg follows a trajectory in the ground's own frame
and the joints are whatever two-link IK says puts the ankle on it:

| | |
|---|---|
| stance (`DUTY` = 0.58 of the cycle) | a straight line from `+HALF` to `−HALF` at **constant velocity**, sole flat on the floor, toe pitch zero |
| swing (the other 0.42) | a lifted arc forward, `LIFT` = 3.5 units at its peak, with the toe pitching down out of toe-off and up into the landing |

The swing's horizontal curve is a cubic Hermite whose end slopes are both
`−(1−DUTY)/DUTY` — the stance velocity expressed in swing-normalised time. That one
condition is what makes the cycle C1 across the contact: the foot is *already*
travelling backward at the body's speed when it lands, so there is no catch-up frame.
It also produces the small overshoot at each end that real feet have, reaching a
little past the landing point and drawing back before touching down.

**The creature crouches now, and it has to.** The rig stands at full leg extension —
hip 20.32 units above the ankle against a 20.33-unit leg — so at its authored height
the foot cannot reach forward at all, which is exactly why the old cycle could only
fake a stride. `hip_ride()` computes the highest the hip may sit for the feet
currently out, capped at `H_CAP` = 19.7. That is the vault of a real walk: highest
over the stance leg mid-step, lowest during double support. The knee swings between
about 8° and 29°, and the body drops at most ~2 units (1 m on an 18 m creature) below
where Idle stands it — small enough that the 0.25 s Idle→Walk crossfade absorbs it.

The IK is solved against the POSED rig, not against a model of it: the hip position
is read back off the armature each frame (so the pelvis roll and the ride height are
exact) and the residual is fed back into the goal until it converges. `anim.py`
asserts the feet land within 0.02 units, because the IK CLAMPS an out-of-reach target
rather than throwing, and a clamp is a skate that reports nothing.

**Ground speed is now a property of the clip.** `2·HALF / (DUTY · WALK_FRAMES/30)`,
which for the shipped numbers is 17.238 blender units/s = **8.990 m/s** — the value
`LightningConjurerBuilder.StrideSpeed` has always claimed. It was not true before:
`stride.py` used to report the *mean* of a sixteen-frame window on a cycle whose foot
speed swung from 6.6 to 11.5 m/s, and the honest average was nearer 7.2, so everything
downstream played a 7.2 m/s walk as though it were a 9 m/s one. `stride.py` now
measures a rigid point on the foot across the whole stance and **asserts** the answer;
it reports 8.990 m/s with the worst frame 0.04% off it.

The two touchdowns land on whole frames on purpose (`PHASE0` is a quarter cycle), at
frames 14 and 40 — which is what `FootPlantFrames` in the builder carries, and
`contacts.py` confirms.

## The staff

`staff.py`, step 4b, between `hands_rebuild.py` and `rustify.py`. Four meshes and
three bones. **It supersedes `charger.py`** and removes that script's four meshes and
two bones if it finds them, so running the pipeline over an already-chargered file
converts it rather than leaving both. `charger.py` is kept beside it as a record, the
way `hands.py` and `style.py` are, and is no longer in the sequence.

| Part | Bone | What it is |
|---|---|---|
| `Staff_Shaft` | `Staff` | the rod, z 12.5 to 42.0, radius 0.60, through the fist |
| `Staff_Mount` | `Staff` | the stationary bearing collar under the hub |
| `Staff_Fan` | `StaffRotor` | three swept blades and their hub, one mesh |
| `Staff_Core` | `Staff` | the emitter above the blades - the only part that glows |

`Staff` hangs off `Hand.R`, so the staff goes wherever the arm goes and `anim.py`
never keys it directly: raising the staff is raising the *arm*. `StaffRotor` exists
so the fan can spin. `StaffTip` is deliberately **not** under `StaffRotor` - the
charge effect and the bolt hang off it, and an emitter that spun with the fan would
drag the lightning round with it.

### The turbine

Three blades, swept, after Cochrane - figure 6(g) of the reference: curved blades
splaying off one hub, all sweeping the same way round. Built as a ribbon of
cross-sections along a swept curve and copied round the axis, written out as vertices
rather than grown from a primitive because all five things that shape it - radius,
rise, sweep, chord taper and lean - vary along the same parameter, and no modifier
stack does that legibly.

**`FAN_LEAN` is the part that reads.** At the root the chord is axial, so the blade
stands parallel to the shaft; by the tip it has leaned 52 degrees toward radial, so
the blade lies over and the three splay outward like a hand rather than standing in a
cage. That lean is the whole difference between figure 6(g) and figure 6(a).

Three blades and not six, for the same reason the old chest rotor had six teeth and
not twelve: this is read as a **silhouette** at the 25 m the creature casts from,
where a dense fan closes up into a disc and stops reading as blades at all. Three
stays open, and open is what lets the emitter glow through it.

### Where it sits

Vertical, gripped in the right hand with the fingers closed round it, turbine above
the head, butt hanging to about knee height. Two constraints put it there and they
pull against each other, which is the whole difficulty.

**The ARM CARRIES it, and the geometry that forces that is the whole story.**

A fist's *bore* — the hole a held pole passes through — is perpendicular to the
plane the fingers curl in, which means it is perpendicular to the **fingers**. A
vertical bore therefore needs horizontal fingers, and horizontal fingers on a
vertical forearm are a wrist snapped 90° out to the side.

Three versions have now paid for that sentence. The first two stood the shaft
*beside* the hand — outboard of the whole arm, then in front of the palm with the
fingertips pressing its near face — because the unrolled fist's bore is horizontal
and a vertical staff cannot pass through it. Both were pinches and both looked
like one. The third rolled `Hand.R` 90° about world X and left the arm hanging
straight: it got the bore vertical, and the hand jutted sideways off the wrist in
every clip, Idle and Walk included, with the staff floating 2.4 units outboard of
a limb it did not look attached to.

**Bending the elbow is what removes the break**, because it is what a person does
— nobody holds a staff with a straight arm. `pose_carry_arm()` puts the right arm
into a carry in the **rest pose**:

| | |
|---|---|
| `CARRY_ELBOW` | −50°, the forearm's rest angle off vertical, forward |
| `CARRY_SPLAY` | 20°, the forearm turned outboard about the vertical |
| hand | fingers along the splay and **level**, palm inboard, thumb up |

The hand is level because the bore is perpendicular to it, so the rest wrist sits
at 40° of extension against a forward forearm — the pose in the reference art,
where the forearm comes forward and the fist cocks up onto the shaft.

**The splay is a silhouette fix and it costs nothing in the joint.** A staff
carried straight forward stands directly in front of the creature, and from the
three-quarter angle a player actually fights it from, *forward* and *to the right*
project onto the same place: the shaft came out drawn across the middle of the
body and over the head, cancelling the separation the whole design depends on.
Turning the forearm 20° about the vertical puts the column back outside the body's
edge without moving it forward at all, because the forearm's length is what sets
the reach. It takes the hand with it, so there is no deviation at the wrist —
yawing the *hand* alone buys the same clearance and pays for it with exactly the
sideways break this change exists to remove.

**Palm inboard is not a free choice.** For a right hand thumb = fingers × palm, so
palm-inboard is what makes it a thumb-up grip; palm-outboard puts the thumb
underneath, which is the reversed grip a torch is carried in.

Two things about how the pose is applied:

- It lives in the **rest pose**, not in a keyed rotation. Put it in the clips and
  the bind pose keeps the authored orientation, which means the staff lies
  horizontal through the creature's own torso whenever no animation is playing —
  in the prefab view and in every editor scene not in play mode.
- **Connected bones have to be disconnected first.** Moving an edit bone's tail
  drags the head of any child connected to it, so transforming a parent and then
  that child applies the rotation to the child *twice*. Eleven of the twenty bones
  here are connected, and the result was phalanges four units long and a hand
  turned inside out. `turn_subtree` clears `use_connect`, transforms, and restores
  it.

Idempotency is by **delta**, not by a flag: both steps measure what the rig
currently holds and apply the difference, so the function converges on the same
pose from the hand as authored, from the old wrist-roll build, and from its own
output. Run it twice and the second pass moves nothing.

**The column is MEASURED, not typed.** A closed finger's four joints lie on a
circle, so the bore is that circle's centre rather than a guess between the palm
and the fingertips. `measure_bore()` fits one — Kasa least squares, through the
index finger's three joints and its tip, in the plane the finger sweeps. On the
hand as the old roll left it, it lands within 0.02 of the figures the first
version of this file arrived at by hand: centre (0.500, −8.810), radius 0.760.

That matters because the carry pose *moves* the bore, to (6.43, −8.05, 23.27). A
hard-coded column would have stood the shaft next to a fist that is no longer
there, and nothing downstream would have said so.

The **radius** is what the curl controls; the centre barely moves — 0.76 at curl
0.85, 0.651 at 1.00. A full curl looks like the better match against a 0.60 shaft
and is not: those are the *joints*, and the finger mesh is thick, so at 1.0 its
inner face drives through and a fingertip pokes out the far side, plainly visible
in a top-down render. 0.85 keeps the phalanges against the shaft with the
intersection buried. The fingers wrap 270° of it and the palm closes the last
quadrant.

**The clearances all improved rather than shifting.** Grip in the palm of a
properly carried arm, the shaft clears the body by 1.87 units against the rolled
version's 0.40 and the pinched version's 0.11, and the fan clears by 5.64 against
3.94.

**The turbine cannot be beside the head.** Measured off the meshes: the head reaches
radius 4.67 about (0.33, -0.06, 32.4) and the body stops at z 37.48. A 3.45-radius
fan on this column cuts 1.84 into the skull at hub z 34.4 and 0.73 at z 36, and
finally clears at 37. `HUB_Z` is 38.0, which clears by 2.51.

An earlier version made the opposite mistake and tucked the hub *below* the head, at
z 25 beside the narrow Hips column. That buries the turbine against the torso and the
arm; it rendered as a pale smudge at the shoulder.

**Those two together decide the layout.** Scaling the staff about its grip - keeping
the hand a third of the way up, as the full-size version had it - lands the hub at
exactly the 34.4 that fails. A shorter staff on a creature that has not shrunk simply
has to be gripped further down, and the tail stops at the knee rather than near
the floor. How far down is not authored: `GRIP` is measured off the carried arm,
so the collar lands wherever the fist is — currently z 23.27 on a rod running
12.50 to 41.97, a little over a third of the way up.

All three placements are **asserted**, not eyeballed. `staff.py` compares the fan
against a star profile of the body per z band and per azimuth; it tests the shaft as
a *column* against the body's vertices; and it poses the hand closed before checking
that the fingers reach it.

Two of those needed correcting when the grip moved into the palm, and both had been
passing for the wrong reason. The fan test compared on y alone, which was right while
the staff was carried out to the side and says nothing once it is on the body's own
line. The shaft test sampled the *shaft's* vertices - and the rod carries vertex
rings only at its fourteen profile heights, with the arm sitting in the fifteen-unit
gap between the ring above the grip and the ring under the turbine, so it was looking
at nothing at all in the region that matters. It is the same sparse-rod trap that
made the hand-reach check pass by doing nothing an earlier time round; the answer
both times was to walk the *other* object's vertices.

### Size

`SIZE` is 0.75, and everything below it is authored at the staff's original
dimensions and multiplied through - length, radii, chords, the hub and the emitter.
The staff is 29.5 units long against the original 39.3.

Three angles are deliberately **not** scaled: `FAN_SWEEP`, `FAN_LEAN` and `BLADES`.
Those are what make the turbine read as figure 6(g) rather than as a cage, and they
are shape rather than size - scaling them would redesign the turbine as a side effect
of resizing it.

The rod's profile is given in **absolute z**, derived from the things its collars sit
against, rather than as fractions of the length. The two that matter are the grip
collar, which has to land in the fist so the fingers close on a swell instead of a
bare pole, and the bearing band under the turbine mount. As fractions those were 0.32
and 0.72 for the full-size staff and would both be in the wrong place now, because
the shrink moved the grip down the shaft and the carry pose moved it back up. The
two entries that matter ride `GRIP.z`, so they follow the fist.

### The shaft is generated, and reusing the model's own was tried first

The .blend has always carried a staff. `Weapon` - 7250 verts, 23 units tall - sits in
`WIP_Spares` at y = -44 and is the only thing in the file drawn for this creature and
never used. Lifting its shaft, cutting the blade head off and stretching it to length
is the obvious move, and it does not work.

**Its shaft is not a tube.** Cut below the head it comes to 120 verts in about sixty
faces: a 32-vert ferrule disc at the bottom, a handful of 4- and 8-vert collar rings
near the top, and *nothing between them* - no faces spanning the fifteen units in the
middle. Whatever it was drawn as, it is not closed geometry, and a bounding box says
nothing about that: every check passed on the reused version, the placement
assertions passed, and it rendered as a turbine floating over a stub with a tiny disc
hovering near the floor.

So the rod is generated from a profile of (height, radius) pairs. That also buys what
the donor could not: the collars land where they are wanted - one inside the hand,
one under the turbine - instead of wherever a 4.2x vertical stretch dragged them.

`Weapon` is left untouched, and `staff.py` asserts its vertex count at the end to
make sure it stays that way. `components/props/walking_staff.blend` was the other
candidate and is not usable either: its four variants are 1.0-1.6 m wooden hiking
canes wearing `Mat_Wood_Ply_Worn` - the wrong size by a factor of fifteen and the
wrong material for a nine-metre rusted mech.

### Two traps, both already paid for

**`transform_apply` works on the SELECTION, not the active object.** `charger.py` got
away without an explicit select because every one of its parts came out of a
`bpy.ops` primitive, and those leave the new object selected. The parts here are
built with `bpy.data.objects.new` and arrive selected by nothing. For the generated
pieces that is invisible - their matrix is already identity, so the apply has nothing
to bake - but the shaft carried its whole scale-and-place matrix, and skipping the
apply threw it away and left the staff buried under the floor at a third of its
length.

**The donor's local space is not the world's.** `Weapon` is parked at z 37.54
carrying a 0.85 z scale, so a cut threshold read off the viewport means nothing in
its local coordinates. Cutting at a local 36.4 removed *nothing at all* - the whole
mesh is below it - and the script ran happily to the end, kept all 7250 verts, and
then measured the blade as the shaft's radius. A cut that removes nothing is not
self-announcing; it needs an assertion, which that version did not have.

## Materials

`rustify.py` paints the creature in a **four-shade rust ramp**, superseding
`style.py`'s nine-material scheme. One flat rust over 48 parts read as a repaint
rather than as corrosion, and flattened the whole silhouette into a single mass.

| | Hex | Rough | Metal | Where |
|---|---|---|---|---|
| `Mat_Metal_Rust_Pale` | #C6884A | 0.85 | 0.35 | sun-bleached powdered oxide, up top |
| `Mat_Metal_Rust_Heavy` | #9A5D1D | 1.00 | 0.50 | the mid tone, and the most common |
| `Mat_Metal_HullRust_Orange` | #764E2A | 0.72 | 0.15 | browner, barely metallic |
| `Mat_Metal_Rust_Deep` | #4E3418 | 1.00 | 0.40 | near-black pitting, at the feet |

Two already existed (`Rust_Heavy`, and `HullRust_Orange` off the RV ship's hull);
`Rust_Pale` and `Rust_Deep` were added for this ramp. Metallic falls as the rust
gets lighter and powderier — corrosion is an oxide, not a conductor — and
`HullRust_Orange` sits at 0.15 on purpose: that difference in *surface response* is
what separates neighbouring plates in motion even where their base colours are close.

**Distribution is height plus a name hash, not random.** Height drives the base —
deep pitting where water sits at the feet, pale bleach up at the halo — which is
what makes the variation read as weather rather than as a paint scheme. But height
alone bands the model into four horizontal stripes, which is arguably worse than one
flat colour because it draws a line across the silhouette, so a CRC of each object's
name jitters it a step either way. The CRC rather than a random number means
rebuilding produces the same creature every time.

Height is taken as a **rank** over the parts, not scaled between the model's lowest
and highest point: the halo floats ten metres clear above the body, and raw height
compresses every real part into the middle of the range, leaving the pale end almost
unused. Current spread — Pale 14, Heavy 29, Orange 24, Deep 13.

### The colour is in the MESH, not in the materials

**This supersedes everything below in this section.** Three versions assigned
palette *materials* — one per object, then one per face from a warped noise field.
Both are discrete by construction: a face gets exactly one material, so every
transition is a hard step at a polygon edge. The brief was soft blends, one tone
bleeding into the next, and no amount of tuning a noise field makes a step
function do that.

So `rustify.py` now evaluates the field at every **vertex**, keeps the result as a
continuous position along the ramp instead of rounding it to an index, and
interpolates between the two palette colours it falls between. That colour is
baked into a `FLOAT_COLOR` attribute on the POINT domain, and the GPU interpolating
it across each triangle is where the gradient comes from.

`SpaceGame/ConjurerWeathered` reads it. A custom shader is not optional here —
URP/Lit ignores vertex colour entirely. It also adds triplanar world-space grunge
and downward streaks generated in the fragment shader, because vertex colours are
bounded by vertex density (~0.09 m here) and the speckle in the reference photos is
finer than that. Generated rather than sampled from a texture, so it needs no UVs,
which matters because none of these 68 parts are unwrapped.

Vertex **alpha** carries how corroded each point is and drives metallic and
smoothness together: corrosion is an oxide, so the more weathered a spot, the
rougher and less conductive.

The creature is back to **one material** over the body — the per-face version
needed 70 submeshes — so this costs fewer draw calls than what it replaced, not
more.

Two traps this cost me, both worth remembering:

- **Vertex colours must be exported as sRGB.** `export.py` sets
  `colors_type='SRGB'`. Unity converts incoming vertex colours from sRGB to linear;
  exporting as LINEAR applies that conversion to values that were never encoded and
  the whole creature imports about 30% too dark.
- **Metallic must stay well below 1.0.** A fully metallic surface has no diffuse
  response, so with only a sun and a dim sky to reflect, it renders near-black.
  Steel_Worn's palette value of 1.0 is right for a flat-colour material and wrong
  here, hence the material's own 0.45.
- **Blooms only push one way**, so they drag the whole distribution toward the damp
  end — roughly half the surface sits inside one. `BLOOM_BIAS` compensates; without
  it the creature comes out overwhelmingly verdigris and the grey it is mostly
  supposed to be never appears.

### Historical: variation within a part, by material (superseded)

A shade per object leaves every individual surface flat, and the flattest surfaces
are the biggest ones — the head dome at 2638 polys, the two upper legs at ~2000
each. Those are what the eye reads first.

So any body mesh of **24 polys or more** picks its shade **per face**, from a noise
field sampled at each face's WORLD centre and added to that part's height-derived
base tone. World-space sampling means the field is continuous across part
boundaries: a patch running off a thigh carries onto the knee below it instead of
stopping at the seam. 19 panels qualify.

### Making it flow rather than tile

Three things, and the first attempt had none of them — it looked like squares of
flat colour dropped here and there, which is exactly what it was.

**Domain warping.** Plain fractal noise gives round blobs; corrosion creeps along
seams, necks, and branches. So each sample position is offset by a *second* noise
field before the first is sampled. Straight features come out as tendrils and blob
edges come out ragged. This is the single change that stopped it reading as
geometric.

**Blooms.** A warped field on its own drifts smoothly between neighbouring shades,
which reads as a gradient, not as rust. A second warped field is thresholded, and
above the threshold the shade jumps two whole steps darker rather than drifting one
— an isolated patch with a hard-ish edge. Because the thresholded field is itself
warped, that edge necks and strands off instead of being a circle.

**Small parts sample the same field.** They are too small to patch face-by-face, so
they take one shade — but from the field at their centre, not from a hash of their
name. That is the difference between a wrist block that continues the weathering of
the forearm it is bolted to and one that pops out as a pale rectangle. The pale
rectangles were most of what read as "geometric"; a hash cannot know what its
neighbours look like, a continuous field does by construction.

The patch boundaries still follow polygon edges, which is fine here: faces are a
median 0.09 m in game and no worse than 0.18 m on the coarsest panel, so an edge has
enough faces to wander along. It works because the mesh is dense enough — on a
coarse mesh the answer would be baking a texture instead.

**Where it is subtle:** the head dome. It is smooth-shaded and large, so its
lighting gradient swamps the difference between two rust shades that are close in
value. The faceted parts carry the effect much better. Widening the ramp's contrast
(`Rust_Pale` lighter, `Rust_Deep` darker) is the dial if it needs to read harder —
both were added for this creature, so changing them affects nothing else.

**Only the shades a mesh actually uses become slots.** A slot with no faces on it is
still a submesh and still a material on Unity's renderer, so appending all four to
all seventeen panels would buy empty draw calls. In practice most panels use two:
17 panels come to **36 submeshes**, not 68.

Per-face spread over the patched panels — Pale 36%, Heavy 34%, Deep 17%, Orange 13%.

All four must also be listed in `LightningConjurerBuilder.Palette` or the missing
ones import as default grey however good the .blend looks: the FBX remaps materials
by NAME onto those generated assets, and a name with no entry matches nothing.

Four things keep their glow, and the exemption is not decoration: the attack is
survivable because the player gets a four-second telegraph, and **the telegraph is
the glow**. The eye, the two palm emitter plates and now the staff's emitter
(`Staff_Core`) stay `Mat_Emissive_Portal_Blue`. Rust those over and the wind-up is
invisible until the bolt is already falling.

The staff's shaft, bearing collar and fan are deliberately *not* exempt. They are
structure — a rusted rod, a collar and three steel blades — and weathering them is
what makes the emitter above them read as lit rather than as one uniformly bright
object on a stick. Same call the old chest housing got.

The grafted hands keep the materials they were authored with — rust plating over
dark steel joints and chrome pins. The plating is the same rust as the body; the
joints are the only thing that reads the fingers as separate segments at 25 m.

**The file's procedural `Rust` material is deliberately not used on any exported
part.** It is a much better rust — noise into voronoi into two colour ramps, mixed
against a hot orange, metallic off an inverted ramp, with a bump — and it cannot
reach Unity: FBX carries a base colour and texture references and nothing else, so a
node graph does not survive the trip. It is kept with a fake user. Baking it to an
albedo/roughness/metallic/normal set is the upgrade path and needs UVs on all 45
parts first.

## The charge effect, and the warning on the ground

Two prefabs, both generated by `LightningConjurerBuilder` and both assigned to
`ConjurerCastModule` by it, because a slot filled by hand is a slot that is empty
again after the next rebuild.

### `ConjurerStaffCharge.prefab`

Parented to `StaffTip` when a cast begins, destroyed when the bolt lands. A point
light and ten arcs — and deliberately **no glowing core**.

An emissive sphere that swelled at the emitter used to be the centrepiece, carried
over from the chest charge before it, where a ball growing inside a ring *was* the
picture. On the end of a staff it read as a blue balloon stuck to a stick, and it hid
the turbine — which is the part that actually tells the player what is happening,
because it spins up. The light stays: a `Light` has no geometry, so it brightens the
blades and the arcs without drawing a shape of its own, which is the effect the
sphere was really there for.

The arcs are `LightningBoltEffect` — the same component the strike uses — with
**`duration` set to 0**, which that component reads as "do not destroy yourself".
`ConjurerStaffCharge` re-points them a few times a second. Re-aiming produces exactly
the same snap-to-an-unrelated-shape that respawning would, without churning hundreds
of instances through Instantiate over a cast.

**It replaces `ConjurerChestCharge`, and it runs the other way.** The old effect's
arcs ran *inward*, from the chest ring to the two palms hovering either side of it,
then converged on the gap between them — because that gap was the muzzle. Nothing
about this attack is like that. These start on the turbine and, as the charge builds,
more and more of them stop reaching back to the blades and shoot straight **up**
instead. That progression is the whole idea: before anything is falling, the effect
has to tell the player the answer is coming from above and not from the creature.

**The fan is a radius, not a bone.** The three blades are one mesh on one bone — they
never move independently — so there is no per-blade-tip transform to hang an arc off,
and adding three empty bones purely so a cosmetic could find them would put geometry
in the FBX to serve nothing. The turbine is a circle of known radius a known distance
below the emitter, and the endpoints are sampled off that circle, which also lets
them land anywhere along a blade rather than only at its tip. Both numbers are
derived in the builder from `staff.py`'s `FAN_R1` and `TOP_Z − HUB_Z`, converted by
the model's import scale.

It samples that circle in the staff's **own** frame (`transform.up`, not
`Vector3.up`): the shaft leans 11° while it charges and straightens at the strike,
and the turbine is perpendicular to the shaft, not to the world.

### `ConjurerStrikeWarning.prefab`

A ring on the ground at the blast radius, a column of glow descending onto it, and a
light. Spawned unparented — the mark belongs to the ground, not to the caster, and
parenting it to the creature would drag the warning around whenever the body turned.

**This one is new, and it is not decoration.** The old attack fired down a line and
could be blocked by putting something solid in the way. This one falls out of the
sky: no cover, no angle, no block. The only counterplay it can have is moving off the
spot, and a player can only move off a spot they can see. Without the ring the attack
is unavoidable damage on a timer, which is why the builder logs an **error** rather
than a warning if the prefab is missing.

It draws the two things the player has to read. **Where** is the ring, at the real
`damageRadius` — not smaller, because a ring that does not mean "everything inside
this is hit" teaches the wrong lesson the first time somebody stands just outside it
and dies anyway. **When** is the column, descending; a ring that only brightens says
"something is coming", while a mark falling out of the sky says how long is left,
which is what decides whether you walk or sprint.

The ring is a generated annulus mesh (`Art/Models/Generated/StrikeWarningRing.asset`,
authored at radius 1 and scaled at runtime) rather than a scaled primitive, because
Unity has no ring: a flat cylinder gives a filled disc, and a filled disc under the
player's feet hides the ground they are trying to run across. It is double-sided —
the camera can end up under it on a slope, and a warning that vanishes from some
angles is the one thing this must never do. The mesh is rewritten **in place** on a
rebuild; `CreateAsset` over a live asset mints a new object and would leave the saved
prefab pointing at nothing.

### The lock, and why nothing on the wire carries it

The ring **follows** its victim for three of the four seconds and then **locks**. That
is the fight: standing still is punished, a late move beats it. The freeze is loud on
purpose — the ring snaps wider, the light turns white, the pulse doubles — because a
lock the player cannot see is a lock they cannot play against.

Every machine runs its own copy and reaches the lock on its own clock, from the same
cast start and the same authored delay, so no message carries it. What the
`ConjurerCast` message *did* change is its subject: it used to point at the conjurer,
which restated who was sending it, and now points at the **victim**. `NetMessaging`
routes by the sending component's own relay, so that field was never doing routing
work — pointed at the target, `Resolve()` answers on every machine and offline both,
and each peer's ring can follow the victim itself instead of being told where it is
every frame.

## Sleeping, and why waking is just the eyelid

The creature spawns standing, in the open, doing nothing until something hostile comes within
`wakeRadius` (20 m on this build — four fifths of `CastRange`). `DormantModule`
(`Scripts/Agents/Modules/Movement/`) owns *when*; the animator owns *what it looks like*.

**This used to also bury the creature and play a 90-frame rise back out of the sand**, with a
`Dormant` squat action and a `Wake` unfold action both baked in Blender, a matching pair of
`IsDormant`/`Wake` animator parameters, and a module that sank and lifted the transform on its
own clock synced to the clip length. All of that is gone. It bought a striking entrance and cost
a second animation state machine's worth of things that had to be kept in lockstep — the clip
length, the module's timer, the smoothstep on both sides, the burial depth measured off the
folded pose, the `NavMeshAgent` suspend/resume around the sink. Waking is now *only* the eyelid,
on a body that never leaves its authored pose. Both actions have since been deleted from
`anim.py` as well, so the FBX no longer carries them.

### Sleep and Awakening are STATES, and they are generated Unity-side

The animator's graph is five states and it is deliberately one-way at the front:

```
[entry] → Sleep → Awakening → Idle ⇄ Walk
                               ↑      ↑
                               └ Attack ┘   (from Any State, gated on Awake)
```

Nothing anywhere targets `Sleep` or `Awakening` a second time. That is enforced in the graph
rather than in the module, because the module could be re-enabled, or added twice, or dropped on
an instance by hand — and none of that can put a woken creature back to sleep if there is no edge
for it. `Sleep` is the ENTRY state, so every conjurer in the world is asleep until something
walks up to it.

- `Sleep` — 1 s, looping. Idle's first frame on every bone, held flat, plus both lid shape keys
  at 0. Literally "stands completely still with its eye shut".
- `Awakening` — 1.2 s, one-shot. The same held pose, with the lid opening 0→100: the top half
  leads and the bottom follows a third of the way in, so it reads as an eye rather than as one
  object splitting. It falls through to `Idle` on exit time.
- `Sleep → Awakening` fires on the `Wake` trigger, with a **zero-length** transition — the two
  clips hold the same pose, so there is nothing to cross-fade.
- `Awake` is a BOOL LATCH, set once by the module when the eye finishes opening and never
  cleared. The Any State → Attack edge requires it, which is what stops a sleeping creature
  being startled straight into a cast by a trigger arriving through Any State.

**Both clips are generated by `LightningConjurerBuilder.BuildEyeClips`, not exported.** Blender
cannot put shape-key animation in the same FBX take as the armature: with
`bake_anim_use_all_actions` the exporter emits one `AnimationStack` per *(object, action)* pair,
so a shape-key action comes out as a separate take named `Key|ConjurerRig|<action>` that Unity's
clip slicer never looks at. Generating the two clips sidesteps that, and it buys the thing that
makes the hand-off invisible: the sleeping pose is sampled from Idle's own frame 0, so
`Awakening → Idle` is a no-op on all 58 bones.

Every bone curve is held FLAT rather than left out. A clip with no curve for a bone does not hold
that bone still — it hands it to whatever the animator's write-defaults policy says, and a
creature that fell asleep would sleep in its last walking pose.

### The imported clips carry the eyelid too, and they carry it OPEN

This is the trap in the whole arrangement, and it is not visible from Unity. Blender bakes each
shape key's **export-time value** into every animation stack as a constant channel, and Unity
reads those onto the armature take — so `Idle`, `Walk` and `Attack` all animate the eyelid
whether anyone meant them to or not. With the keys exported at 0, the frame after `Awakening`
finishes opening the eye is the frame `Idle` shuts it again, and no amount of write-defaults
fiddling on the Unity side outvotes a curve.

So `export.py` exports the two lid keys at **1.0** (`EXPORT_OPEN`). Every clip in the FBX then
holds the eye open, which is what all three of them should hold — the creature is awake in all of
them. `BuildEyeClips` checks it and logs an error if the imported `Idle` clip leaves `Top open`
at anything but 100, including at nothing at all.

The mesh's own resting weights stay at 0, so a conjurer in the project window or dropped in a
scene shows a shut eye — which is exactly how it looks when it spawns.

### Waking is one-way

`DormantModule` sits at `Scripted` (100), the top of the ladder, and returns `MoveIntent.Idle()`
every frame it is asleep or waking. That starves chase, cast and wander for as long as it keeps
doing so — the documented use of `Idle`, since standing still *is* the behaviour — so no other
module on the prefab had to learn the word "dormant". It fires the `Wake` trigger, counts out
`awakenSeconds` (which the builder writes from the clip's own length rather than leaving two
copies to drift), latches `Awake`, and sets its own `enabled = false`; `AgentController` re-reads
`IsActive` the very next tick, and whichever module is next on the ladder — `ChaseModule`/the
cast if a target exists by then, `WanderModule` if not — wins the frame.

**Removing the component no longer gets you an awake conjurer.** The graph's entry state is
`Sleep` and this module is the only thing that fires the trigger out of it, so one without the
component stands asleep forever. Leave it on and widen `wakeRadius`, or call `WakeNow()`.

## The eyelid, and why it exports as a blend shape

The eye is covered by a two-piece lid — `Eyelid`, mesh `Sphere.005`, a 546-vertex dome
bone-parented rigidly to `Head` like everything else on this rig. It carries three
relative shape keys on top of the Basis:

| Key | Verts moved | Affected local Z | Motion |
|---|---|---|---|
| `Top open` | 268 / 546 | −0.20 … 1.00 | upper half retracts back (−X) and up |
| `Bottom Open` | 193 / 546 | −1.00 … −0.20 | lower half retracts back (−X) and down |
| `Key 3` | 461 / 546 | −1.00 … 1.00 | both at once |

**The Basis is the CLOSED lid**, which is the whole reason blend shapes are the right
mechanism here rather than a hinge bone: a blend shape's rest state in Unity is the
Basis at weight 0, so the creature imports with its eye shut for free and opening is a
0–100 slider. The halves are disjoint, so they drive independently — the lid can crack
open one half at a time.

The keys' *export-time values* are a separate matter from the Basis, and a load-bearing
one — see "The imported clips carry the eyelid too" above. `export.py` writes the two
lid keys at 1.0 and `Key 3` at 0. That does not move the exported geometry, which is
still the Basis; it sets the constant each FBX animation stack carries for that
channel.

`Key 3` is **exactly** `Top open` + `Bottom Open`, verified to the last decimal
(`max|Key3 − (Top+Bottom)| = 0.0000`). Relative shape keys are additive, so it is
redundant: driving both halves to 100 reproduces it. It is exported anyway rather than
quietly dropped, because it is authored data — but drive the two halves, not this, or a
`Key 3` set alongside them double-opens the lid.

### The exporter silently ate the keys, and `Solidify` is why

For a long time the FBX carried **zero** `BlendShape` deformers and Unity had nothing to
drive. The lid shipped as one frozen mesh, stuck in whatever pose the sliders happened to
read — which was wide open, because `Key 3` sat at 1.0.

Blender's FBX exporter drops every shape key off any mesh it has to *evaluate*. The
evaluation goes through `bpy.data.meshes.new_from_object()`, which strips them; the
exporter says so in its own source, in the comment on the branch it takes when it does
**not** have to evaluate (`export_fbx_bin.py`, "removes shape keys (see #104714)"). With
`use_mesh_modifiers=True` it evaluates every mesh carrying an enabled non-armature
modifier — and `Eyelid` carries a `Solidify`.

Armature modifiers are exempt and must be left alone: the exporter parks those at the
REST pose instead of evaluating, so they never trip its `do_evaluate`. Only the others
have to go, and Blender will not let you simply *apply* a modifier to a mesh that has
shape keys.

So `export.py` does it itself, in `bake_modifiers_into_shape_keys()`: every key block is
pushed through the modifier stack as its own temporary object, the Basis result becomes
the new base mesh, and the keys are rebuilt from the remaining results. Each key needs
its own evaluation because a modifier reads the **base** vertex positions — evaluating
once and offsetting afterwards would solidify the closed lid and then slide the result,
which is a different shape.

Two things it is careful about, both load-bearing:

- **`preserve_all_data_layers=True`**, or the `Weathering` colour attribute
  `rustify.py` writes is lost and the lid imports flat grey. See *The colour is in the
  MESH, not in the materials*.
- **Every key's evaluated vertex count must match**, and it asserts so. A modifier whose
  topology depends on vertex positions cannot be baked this way at all, and failing loudly
  beats shipping a mesh whose keys are silently misaligned.

It runs **in memory only** — the `.blend` is never saved, so the live `Solidify` stays on
the object for whoever opens the file next. It is generic, not special-cased to the
eyelid: any mesh in the export set with shape keys and a live non-armature modifier gets
the same treatment.

The result, verified by reimporting the FBX in Unity:

```
SkinnedMeshRenderer 'Eyelid' mesh 'Eyelid' verts=1382 blendShapes=3
  [0:'Top open', 1:'Bottom Open', 2:'Key 3']  colors=1382
```

Note that `Eyelid` is now a **`SkinnedMeshRenderer`**, not a `MeshRenderer` — Unity
promotes any mesh with blend shapes. Nothing in the project looked the old renderer up by
type, so this cost nothing, but a future sweep over the model's `MeshRenderer`s will miss
the lid.

### It moves the creature's top by 0.014 units, and that is fine

`LightningConjurerBuilder.BlenderTop = 37.49f` is the top of the eyelid and it sizes the
whole creature. That number was measured with the lid frozen open. Closed, the top is
**37.477**; fully open it is **37.491**. The 0.014-unit difference across a 34.7-unit body
is 0.04%, so the constant is left alone rather than re-measured to a value that is only
correct while the eye is shut.

## Scale and orientation

Height **18.11 m** — `PlayerHeight * 6f`, where the player (`AstronautArmature`)
measures 3.019 m to the top of the head (confirmed two ways: skeleton
`HeadTop_End` at y 3.019, mesh bounds 3.024). Scale factor 0.5216 — that is
18.114 / (`BlenderTop` 37.49 − `BlenderFloor` 2.757) — applied via
`ModelImporter.globalScale`.

*This paragraph used to say 9.06 m and "3× the player", from when `TargetHeight` was
`PlayerHeight * 3f`. The multiplier was changed to 6 in `LightningConjurerBuilder`
and this was not; the prefab's own bounds in the test scene read 18.114 m. Worth
knowing because the staff is sized in blender units against the body, and anything
that converts those to metres — `ChargeFanRadius` and `ChargeFanDrop` do — goes
through this scale.*

The model faces Blender **+X**, which lands on Unity **+X**. The prefab's model
child is yawed −90° so the prefab *root's* forward is the creature's forward.

**The armature is deliberately left at identity in the FBX.** For a bone-parented
(non-skinned) rig Unity discards the armature node's own transform, so a rotation
or scale parked there survives in the animation curves but vanishes from the bind
pose — the creature stands correctly only while a clip plays. `GolemBuilder.cs`
documents hitting exactly this. So: export Blender's native Z-up axes untouched,
let `bakeAxisConversion` do the conversion, put metre scale on `globalScale`, and
put the yaw on the prefab child.

## Nothing was deleted

36 loose work-in-progress parts — a spare head dome, an older arm set parked at
y = 38/−21, the `Weapon` staff at y = −44, and the three legacy armatures — were
**moved** into a `WIP_Spares` collection so they stay out of the export. They are all
still in the file. `Cylinder` was left where it was, in an unlinked collection.

`Weapon` was evaluated as the shaft for the new staff and rejected; see **The staff**
above. It is still whole, still parked, and `staff.py` asserts its vertex count so a
future edit cannot quietly consume it.

**The chest charger is the exception, and it was deleted rather than parked.** Unlike
the halo it has no hand-made mesh worth keeping: `charger.py` *generated* all four of
its parts, and can generate them again from the constants still in it. `staff.py`
removes `Charger_Housing`, `Charger_Rotor`, `Charger_Teeth`, `Charger_Core` and the
`Charger` and `ChargerRotor` bones.

The legacy hand rigs (`Armature`, `Armature.001`) and every one of their finger
meshes (`Hand.001`, `.003`, `.004`, `.011`, `.016`, `.035`) are kept intact in
`WIP_Spares`, parked by `restore_parts.py`. The hands the creature now wears are
additions beside them, not edits to them.

The working file had also drifted off the committed lineage and lost the halo
(`Cube`, 392 verts) and eight of the twelve forearm cable curves.
`restore_parts.py` appends the cables back from the committed .blend.

**The halo has since been retired on request.** It was restored, then taken off
again: `restore_parts.RETIRE` parks it in `WIP_Spares`, and it is no longer on the
`RESTORE` list so a cold rebuild does not bring it back. Putting it back is a
one-line change there.

Its BONE stays. `Halo` is still in `rig.py`'s table and `anim.py` still turns it
through Idle and spins it up through Attack; it simply drives nothing now — the
state the model was in before the restore. Leaving it costs one transform in the
FBX and keeps the restore trivial, where tearing it out would mean edits to the
bone table, both clips and this file.

Exported height is therefore 35.45 blender units (top of the body at z 38.21)
rather than the commit's 53.70. Nothing downstream depends on that: the builder
sizes the creature off `BlenderTop = 37.49`, which is the top of the *body*,
deliberately not the halo.

## Rebuilding

Cold start, from an unrigged .blend. `restore_parts.py` needs the committed model as
a donor for the halo and cables:

```bash
BLENDER="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"
BLEND="../ConjuringRobot1 (2) (1) (1).blend"
git show "HEAD:<path to the .blend>" > /tmp/donor.blend
"$BLENDER" -b "$BLEND" -P restore_parts.py -- --donor /tmp/donor.blend
"$BLENDER" -b "$BLEND" -P rig.py            # armature + binding    (refuses to run twice)
"$BLENDER" -b "$BLEND" -P walkerize.py -- --save   # leg naming, pins, hinges
"$BLENDER" -b "$BLEND" -P hands_rebuild.py  # 30 finger bones + cast sockets
"$BLENDER" -b "$BLEND" -P staff.py          # staff + turbine, 4 meshes and 3 bones
"$BLENDER" -b "$BLEND" -P rustify.py        # one corroded metal, glow kept
"$BLENDER" -b "$BLEND" -P anim.py           # Idle + Walk + Attack
"$BLENDER" -b "$BLEND" -P export.py -- ../LightningConjurer.fbx
```

Everything except `rig.py` is safe to re-run. `staff.py` must come before `rustify.py`
(which paints what it builds) and before `anim.py` (which keys `StaffRotor` and solves
the right arm on the `Staff` bone, so it fails on a missing bone otherwise).

Three scripts are superseded and kept only as record: `charger.py` (the chest ring the
staff replaced — `staff.py` deletes its output), `hands.py` (lifted the legacy
right-hand rig, which no longer exists on the creature) and `style.py` (the
nine-material scheme rust replaced — go back to it if the flat read is too flat).

Backups, newest first: `ConjuringRobot1.pre-carry.blend` is the file as it stood with
the 90-degree wrist roll and the position-solved Attack, immediately before
`pose_carry_arm` replaced both; `ConjuringRobot1.pre-staff.blend` is the file as it stood with
the chest charger and the old two-handed Attack; `.pre-chestcast` is older still;
`.pre-rerig.blend` is the file as hand-edited before the hand rebuild;
`.rig-4finger.blend` is the intermediate rig over the old four-finger hand;
`.pre-hands`, `.pre-rig`, `.pre-style` are older.

Then in Unity: **Tools > Creatures > Build Lightning Conjurer**. Note it silently
does nothing useful in play mode — asset writes are discarded — so exit play first.
It ends by asking for **Tools > Save System > Wire Saveable Prefabs**, which
re-stamps the rebuilt prefab's save id; skip it and `SaveWiringOnDiskTests` fails and
the creature is dropped on load in a build.
