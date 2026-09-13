# Resizer remote — build record

A handset that shrinks or enlarges whatever it is pointed at. Design:
[`docs/AI/systems/ResizerRemote.md`](../../../../../../docs/AI/systems/ResizerRemote.md).

## Reused from the library

Three of this model's eight parts are appended whole, and the two biggest are among them. Both
donors were variations **built ahead and never shipped** — exactly the case the library exists for.

| Reused | From | Role |
| --- | --- | --- |
| `Mesh_Terminal_Rugged_Case` | `components/props/handheld_terminal.blend` | the control case. The armoured clamshell with the lid propped open — 0.137 × 0.108 × 0.143, already the device family's cream shell, already carrying a toggle, a rotary selector and a knob on its deck. |
| `Mesh_Terminal_Rugged_Screen` | same | the display plate, already UV'd for a 0..1 shader. |
| `Mesh_WeaponGrip_Pistol` | `components/mechanical/weapon_grip.blend` | the grip. Its origin is on its mount face by that file's convention, so bolting it under the case is a translation and a rake, nothing else. |

Both are **renamed on append** to `Mesh_ResizerRemote_*`. `Mesh_Terminal_Rugged_Case` sitting in a
Unity prefab called `ResizerRemote` is a name that sends the next reader to the wrong source file.

`Coll_Terminal_Scanner` was considered first and rejected. It is a better handset shape and it is
already the item scanner, twice — shipping a second item on the same case would put two devices in
the hotbar that a player has to read twice to tell apart (`GDC-L1-UX-0004`). `Rugged` is the same
family and a different silhouette, which is what was wanted.

## New geometry

Five parts, and each is only true of a transmitter:

| Object | Why it exists here rather than in a component |
| --- | --- |
| `Mesh_ResizerRemote_Whip` | The antenna, and the read at a distance. Three telescoping sections, origin at the collar, so Unity extends it by scaling one transform along Z. |
| `Mesh_ResizerRemote_Dial` | The polarity selector. Its own object with its origin on its own spindle, because the game turns it between the two settings. |
| `Mesh_ResizerRemote_Lamp` | One lens Unity tints per instance — a `MaterialPropertyBlock` addresses a renderer, not a face on somebody else's mesh. |
| `Mesh_ResizerRemote_Yoke` | The chin block, the antenna collar and guard hoop, and every part carrying the blue. |
| `Mesh_ResizerRemote_Gauge` | The charge bar plate. Flat, unbevelled, planar-UV'd — a bevel folds new faces into the UV island and drags the fill's edge pixels round the rim. |

## How it assembles

Case faces −Y, up is +Z, origin at the case's own — `handheld_terminal`'s frame, so every appended
part is already in it.

```
  z +0.286  ● blue bead                     ── Marker_Emitter
  z +0.170  ┃ whip section 3
  z +0.108  ┃ whip section 2
  z +0.106  ┃ whip section 1     Mesh_ResizerRemote_Whip, origin z +0.086
  z +0.116  ╱ propped lid (part of the case)
  z +0.090  ▭ charge bar on the TOP face, x −0.056…−0.034   ── Marker_Gauge
  z +0.086  ⊙ antenna collar, blue ring, guard hoop
  z +0.070  ▌ blue flank plates, x ±0.062
  z +0.031…0.071   ▭ screen
  z +0.006…0.026   ▭ the case's own deck: toggle, selector, knob
  z  0.000  ═ case underside ── grip mounts here, raked −14° about X
  z −0.011  ◉ polarity dial at x −0.038   ── Marker_Dial
            ○ polarity lamp at x +0.040   ── Marker_Lamp
  z −0.024  ▁ chin block, blue lip
  z −0.056  ┬ grip                          ── Marker_Grip
  z −0.158  ╵ heel of the grip
```

Longest axis 0.446 m (Unity **Y**). `ItemGrip.holdSize` 0.36 brings it to a one-handed device.

## Where the readouts went, and why none of them is on the chin together

The grip covers the middle 50 mm of the chin — its top station is 38 mm across and the wooden
cheeks stand 6 mm proud of that either side. The first build put the dial, the lamp **and** the
charge bar on the chin; the render showed the bar squarely behind the trigger guard. The measure is
`GRIP_SHADOW` in the build script now, taken off `weapon_grip.pistol`'s own stations rather than
eyeballed, and the three readings ended up in three places:

- **polarity dial** — chin, x −0.038, outboard of the grip on the left.
- **polarity lamp** — chin, x +0.040, outboard on the right. Colour only, and only a repeat: the
  dial's pointer angle is the reading a player who cannot separate the two colours takes
  (`GDC-L1-UX-0003`).
- **charge bar** — the case's **top** face. There is no span of chin left that a hand does not
  cover, and the top is the face that turns toward the holder at exactly the moment the reading is
  wanted, the handset being held up to aim it.

## Two things that were changed after looking at a render

- **The grip's rake.** `pistol` rakes 0.34 backward per unit of drop, which over its 0.14 m puts
  the heel 48 mm behind the case. Right for a rifle levelled at the shoulder; the first render of
  this handset read as a power drill. −14° about X carries 34 mm of that back forward.
- **The blue.** One saturated colour per device is the family's rule, and the first build put it
  only on a 4 mm collar ring nobody could see. It is now on four parts with four shapes — collar
  ring, chin lip, grip throat band, flank plates — and the flank plates are the ones that do the
  work in profile, which is the angle another player sees the handset from while it is pointed at
  somebody.

## Export

```
blender --background --python models/gear/resizer_remote_export.py
```

13 objects, 8844 tris, no armature (three rigid parts each on one axis do not earn one), markers as
4 mm meshes. Out to `Assets/Game/Art/Models/Items/resizer_remote.fbx`.
