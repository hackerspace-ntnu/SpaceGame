# Carried artifact devices — build record

Covers three components built together as one family, because they share a
problem: each is a gadget an artifact spawns, each is seen mostly as a 256 px
inventory icon, and each was previously a raw Unity primitive.

| Component | Ships to | Artifact |
|---|---|---|
| `weather_station_device.blend` | `Coll_WeatherStation_Field` → `Items/weather_station.fbx` | LightningSpell (was a Sphere) |
| `antigrav_device.blend` | `Coll_AntiGrav_Ring` → `Items/antigrav_emitter.fbx` | AntiGravityPotion (was a Capsule) |
| `leash_device.blend` | `Coll_Leash_Spool` → `Items/leash_emitter.fbx` | Leash (was a Cylinder) |

Export: `item_devices_export.py`. One combined record rather than three
near-identical ones, since the three were designed against the same constraint
and share an export script.

## Reuse

**Nothing existing was reused, and that was not the first choice.** The library's
components are authored for vehicles and buildings — `console_panel` variants run
0.6–2.7 m, `vent_grille` is 0.6 m square, `floodlight_bank` is 1.5 m. These
devices are 0.15–0.27 m on their longest axis. Scaling a 1 m console panel down
by 5× keeps its bolt and panel-line density, which at icon size turns into noise.
The closest precedent in spirit is `expedition_backpack`, which is likewise a
carried item living in `components/props/`, and the layout convention follows it.

`Mat_Metal_Copper_Oxide` was added to the weather station's palette list on the
second pass; everything else came from the existing palette unchanged. No new
palette entries were created — `PALETTE.md` already had a documented material for
every surface these needed, including "coil windings" for the verdigris copper.

## Decomposition

Each file holds three variations in separate collections, differing in
silhouette rather than colour, because silhouette is the only axis that survives
the thumbnail:

- **weather_station**: `Field` (cup anemometer on a slab body), `Vane` (wind vane
  and tail fin on a drum), `Beacon` (caged lightning rod on a coiled can)
- **antigrav**: `Ring` (split hoop, core floating in the gap), `Pylon` (stacked
  emitter plates), `Orb` (three-axis gimbal cage)
- **leash**: `Spool` (pistol-grip tether gun), `Gauntlet` (wrist cuff),
  `Winch` (bolt-down deck winch)

Only the first of each is wired to an artifact. The other six were built ahead —
the structure and material choices were already paid for, so a second and third
variation cost little, and a camp or workshop scene now has spares that do not
look copy-pasted.

Shared helpers within each file (`_base_plate`, `_readout`, `_grip`, `_drum`,
`_hook`, `_arc`) exist so the variations read as one manufacturer's product line
rather than three unrelated props.

## Four traps hit while building these

Recorded because all four are silent — the build reports success and the model
looks plausible until it is rendered.

1. **`bmesh.ops.bevel` stamps every face it creates with material index 0.**
   Verified directly: a cube built entirely on index 1 comes back as 6 faces on
   index 1 and 48 on index 0. So the material at index 0 is the colour of every
   chamfered edge in the file. The first weather-station build put the accent
   blue first and turned every edge in all three meshes pale blue. Every script
   here now puts a structural metal at index 0 deliberately, which is also why
   `light_fixture.py` and the rest of the library lead with `STEEL`.

2. **A whole-part `p.bevel()` destroys thin swept geometry at this scale.** A
   2.6 mm bevel on a 5.5 mm cage tube is over half its radius, and where several
   tubes converged on one shared point `finish()`'s `remove_doubles` then welded
   the over-bevelled ends into a solid dome capping the model. Fix: accumulate
   the boxy faces in a `hard` list and bevel only those. Side effect — the
   weather station dropped from 12,766 to 5,238 triangles. (Face references do
   survive `_absorb`; that was checked and is not the hazard here.)

3. **`loft` section offsets are absolute along the axis, not relative.** Writing
   `(0.000, ...)` after building a grip that already occupies 0–0.07 generates the
   housing *inside* the handle. The offsets have to carry the running height
   themselves.

4. **`prism`'s `(u, v, w)` mapping is not intuitive** — for `axis='X'` it is
   `(w, u, v)`. Guessing it put the wind vane's fin 4 cm clear of its own boom.
   Anything positional here is now built from `box`/`loft` with the mapping
   written down in a comment.

## Unity side

The FBXs import at root scale 100 with a −90° X rotation on the root; that
rotation is what converts Blender's Z-up to Unity's Y-up. Forcing the child's
`localRotation` to identity when parenting the model into a prefab lays the
device on its back — the bounds come back with Y and Z swapped, which is the
quickest way to spot it. Leave the model prefab's own rotation alone.

Origins sit at the bottom of the base plate or grip, so each device stands on a
surface without a Z nudge.
