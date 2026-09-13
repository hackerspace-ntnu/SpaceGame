# Lander — build record

Built 2026-08-30. `ship_lander.blend` = the hand-built interior from
`ship_lander_blockout.blend` (copied in untouched: 73 cubes, one icosphere and
the ×30 reference hull, all still in `Coll_Ref_ExampleHull`) **plus the example
hull cut into 68 closed, individually editable components**.

Generator: `ship_lander.py`. It opens the interior file, adds the components,
and saves under the new name — the interior file is never written.

---

## Where the components come from

Nothing was remodelled. The example
(`models/example/futuristic+spacecraft+3d+model.fbx 3`) is one connected shell
(4 894 faces, 222 open edges, 10 non-manifold edges) plus 53 loose greebles.
The shell was sliced with axis-aligned planes into the pieces below, then every
cut and every pre-existing hole was capped, so each piece is a **closed solid
with zero open edges** (the build prints `open_edges=0` per object). Where two
pieces meet, their caps are coincident and face each other, so from outside the
assembled set is the original surface exactly — pull a piece away and you see a
flat cap, which is what a tweakable part should have.

Cut planes (normalised example units, hull length 1.0; ×30 in the file):

| plane | value | separates |
|---|---|---|
| x = ±0.15 | ±4.5 m | fuselage ↔ wings / forward side pods |
| x = ±0.28 | ±8.4 m | wing ↔ wingtip pod |
| y = 0.00 | 0 | forward side pod ↔ wing |
| y = −0.36 | −10.8 m | nose ↔ cockpit section |
| y = −0.14 | −4.2 m | cockpit section ↔ mid body |
| y = +0.08 | +2.4 m | mid body ↔ aft body |
| y = +0.27 | +8.1 m | aft body ↔ tail |
| z = 0.19 | 5.7 m | canopy hump ↔ cockpit hull |
| z = 0.17 | 5.1 m | nacelles ↔ aft body |
| x = 0 (nacelles only) | 0 | left ↔ right nacelle |
| z = 0.31 | 9.3 m | fin ↔ tail boom |

The planes are the same ones the interior block-out used for its rooms, so the
cockpit section is the cockpit, the mid body is the main room, and the aft body
is the ramp bay.

## Collections

```
Coll_Lander_Components
├── Coll_Lander_Fuselage   Hull_Nose, Hull_Canopy, Hull_Cockpit_Hull, Hull_MidBody, Hull_AftBody
├── Coll_Lander_Wings      Hull_Wing_L/R, Hull_Wingtip_L/R
├── Coll_Lander_Pods       Hull_SidePod_L/R  (forward side blisters)
├── Coll_Lander_Engines    Hull_Nacelle_L/R  (the two aft underslung bodies)
├── Coll_Lander_Tail       Hull_TailBoom, Hull_Fin
└── Coll_Lander_Details    Detail_<Region>_NN  — the 53 loose greebles
```

Details are named by the fuselage region their centre falls in (Nose,
Cockpit, MidBody, AftBody, TailBoom, Fin, Wing_L/R, SidePod_L/R) and numbered
largest-first. They are the example's own antennae, nozzles, lamps, hatches
and vents; rename any you identify.

## Sealing, in order of preference

1. `holes_fill` — clean loops (the cuts, most holes).
2. `triangle_fill` — branching edge nets.
3. Fan to centroid — any remaining chain, walking through the example's
   non-manifold edges; an unclosed chain is bridged end-to-end. This was
   needed on `Hull_Canopy`, `Hull_Cockpit_Hull`, `Hull_MidBody`,
   `Hull_SidePod_R`, `Hull_AftBody`, `Hull_Nose` and `Detail_Wing_R_04`,
   where the Tripo mesh had open slits running into three-face edges.

Caps are triangulated with ear clipping so concave cut loops stay planar.

## Materials

The components keep the example's own material, renamed
`Mat_Lander_Example` (fake user). It is not from the palette on purpose: the
brief was "exactly the same as the original". When the ship is textured for
real, map the pieces onto palette materials and drop it.

## Not done on purpose

- No armature — the doors and ramp that will move belong to the rebuild, not
  to a sliced reference.
- The reference hull (`Ref_ExampleHull`) is still in the file, wireframe, so
  the interior can be checked against it; delete it when the components have
  replaced it.
- Cut positions are constants at the top of `ship_lander.py`. Changing them
  and re-running into a **new** filename gives a different decomposition;
  never re-run over `ship_lander.blend` once it has hand edits.
