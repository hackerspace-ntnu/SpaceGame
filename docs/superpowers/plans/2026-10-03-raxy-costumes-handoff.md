# Handoff: 10 more Raxy costumes (gunslingers, toolers, and more)

**For:** the next agent continuing Raxy clothing. **Written:** 2026-10-03, after 4 batches (18 + 15 + 12 + 12 garments).
**Ask from the user:** 10 additional costumes, with the focus on **gunslingers** and **toolers** (work/craft
roles), plus room for something unexpected. They liked the bandit costumes best, rejected medieval armour as "way too
medieval", and asked for designs that "fit the world" and are "not typical outfits".

Read first, in this order: this file → [CharacterClothes.md](../../AI/systems/CharacterClothes.md) (the
system, its Gotchas) → `raxy_garment_kit/README.md` → skim one builder module (`outfits4.py`) to see the idiom.

---

## 1. Where things are

| What | Path |
|---|---|
| Source of truth (all garments live here, collection `Clothes`) | `Assets/Game/Art/Models/_Source~/models/characters/drifters/raxy.blend` |
| Exported model (body + rig + every garment) | `Assets/Game/Art/Models/Characters/Raxy/raxy.fbx` (+ `.meta` holds the material remaps) |
| Garment prefabs (one per garment, 74 now) | `Assets/Game/Prefabs/agents/Characters/Raxy/Clothes/Clothes_*.prefab` |
| Garment materials | `Assets/Game/Art/Materials/Characters/Raxy/Raxy_<Look>.mat` |
| Backups of raxy.blend before each batch | `Assets/Game/Art/Models/_backups~/raxy_before_*.blend` |
| **The generator kit** (Python for Blender + Unity snippets) | `/Users/ferdinandfremming/Documents/hackerspace/spillgruppen/raxy_garment_kit/` |

**The kit lives outside the repo on purpose:** the repo policy is "no model generator scripts — the .blend, FBX and
prefabs are the assets" (see `docs/AI/systems/ArtPipeline.md`). Do not move it into the repo without asking the user.

**Nothing from the four batches is committed.** Do not commit unless the user asks (a hook blocks it anyway; it also
false-positives on shell commands containing `$`, loops or heredocs — put such logic in a `.py` file and run that).

## 2. What already exists (don't duplicate; reuse as base layers)

| Outfit | Garments (`Clothes_` prefix omitted) |
|---|---|
| Basics | `Tunic`, `Trousers` (baggy, shin wraps), `Vest`, `Shawl`, `Backpack_Large` (has PackLeft/PackRight tool mounts) |
| Hats | `Hat_Straw` (conical), `Hat_Wanderer` (wide brim + feather), `Hat_GoggleCap`, `Hat_Turban` |
| Salvage-tech armour | `PlateArmor`, `Bracers`, `Greaves` (names kept from the rejected medieval version) |
| Dust raider (bandit) | `Bandit_Duster`, `Bandit_Mask`, `Bandit_Bandoliers` |
| Scrap marauder (bandit) | `Bandit_Mantle`, `Bandit_ScrapArmor`, `Bandit_Wraps` |
| Storm walker | `Storm_Respirator`, `Storm_Cloak` |
| Antenna oracle | `Oracle_Crown`, `Oracle_Robe` |
| Water hauler | `Hauler_Yoke`, `Hauler_Apron` |
| Scrap welder | `Welder_Mask`, `Welder_Apron`, `Welder_Gloves` |
| Sky kite rider | `Kite_Harness`, `Kite_Wing` |
| Beast herder | `Herder_Tabard`, `Herder_Chaps`, `Herder_Satchel` |
| Walking peddler | `Peddler_Rack`, `Peddler_Tray`, `Peddler_Patchcoat` |
| Colony salvager | `Salvager_Torso`, `Salvager_Tank`, `Salvager_Arm` |
| Wandering musician | `Bard_Instrument`, `Bard_Coat`, `Bard_Hat` |
| Beast hunter | `Hunter_Pelt`, `Hunter_Quiver`, `Hunter_SkullHelm` |
| Sand racer | `Racer_Jacket`, `Racer_SpineGuard`, `Racer_Scarf` |
| Miner | `Miner_Helmet`, `Miner_Vest`, `Miner_Gear` |
| Farmer | `Farmer_Overalls`, `Farmer_Basket`, `Farmer_Kerchief` |

Plus the user's own older garments (Poncho, Pants, Shorts, Armor, Belts, Sweaters, skirts…) — never rebuild those.
Reference renders: `raxy_garment_kit/reference_renders/` (Unity, batches 1–4).

No outfit/character prefab wears any of the new garments yet; the user views them by dragging garment prefabs onto
a Raxy's `Model` in Prefab Mode. Only make outfit prefabs if asked — a new character prefab must be registered in
the network prefab lists (`spacegame-multiplayer` skill) and must not duplicate a persistence prefab id.

## 3. Proposed 10 costumes (a brief, not a spec — improve on it)

Each is 2–3 garments, layered over existing basics (`Tunic`/`Trousers`). Guns, tools and gear modelled on a
garment are **static dressing** (holstered, slung, clipped); usable weapons/tools are items (`spacegame-artifact`).
A gunbelt can optionally carry a `GarmentMounts` (bone `Hips`, `HipLeft`/`HipRight` slots) so real carried tools hang
on it — see HandTools.md; the large backpack shows how the mount points were placed.

**Gunslingers**
1. **Duelist** — `Duelist_Coat` (long split-tail coat, short cape over one shoulder only), `Duelist_Gunbelt` (low-slung
   belt, two holstered salvage revolvers, cartridge loops, thigh ties), `Duelist_Hat` (flat brim, band of coins; sits
   on the crown like `Hat_Wanderer`).
2. **Sharpshooter** — `Sharpshooter_Ghillie` (cape of hanging sand-coloured cloth strips), `Sharpshooter_RifleSling`
   (long scoped rifle across the back on a sling, ammo pouch), `Sharpshooter_Spotter` (headband with a flip-down
   spotting scope over one eye).
3. **Bounty hunter** — `Bounty_Coat` (armoured long coat, plated high collar), `Bounty_Trophies` (chains of tags and
   tokens, scroll case for warrants, manacles on the hip), `Bounty_Rebreather` (half-face rebreather, hose to a chest
   canister).
4. **Caravan shotgun guard** — `Guard_Brigandine` (riveted vest of small plates), `Guard_ShellBelts` (two shell
   bandoliers, flare-pistol holster, whistle), `Guard_Pauldron` (one big shoulder guard carrying a lamp).
5. **Gunsmith** — `Gunsmith_Apron` (springs, barrels and screws in pockets), `Gunsmith_Loupe` (multi-lens loupe
   headband), `Gunsmith_PartsCase` (back case with barrels and stocks sticking out).

**Toolers**

6. **Mechanic** — `Mechanic_ToolHarness` (chest X-harness with loops of wrenches, screwdrivers, pliers),
   `Mechanic_Visor` (flip-down magnifier lenses), `Mechanic_SleeveGuards` (leather forearm guards, rag tucked in).
7. **Lineman** — `Lineman_Harness` (safety harness with lanyard and climbing spikes on the shins), `Lineman_Spools`
   (belt with cable spools and ceramic insulators), `Lineman_Gloves` (thick insulated gauntlets).
8. **Water engineer** — `Engineer_Waders` (chest-high rubber waders with straps; they end at the ankle — Raxy feet are
   clawed), `Engineer_ValveKit` (big pipe wrench on the back, valve keys, hose coil), `Engineer_Cap` (soft cap).
9. **Sapper** — `Sapper_ChargeVest` (vest of charges with wires), `Sapper_BlastVisor` (raised blast visor, like the
   welder mask's hinge trick), `Sapper_Detonator` (box plunger and fuse spools on the belt).

**Something else**

10. **Courier / sand runner** — light sprint gear: `Courier_TubeBandolier` (message tubes on a cross-strap),
    `Courier_Wraps` (arm and leg wraps with a signal mirror on one forearm), `Courier_Hood`-less head wrap (keep the
    ears free, see §6).

Before committing to designs, consider the Game Development Constitution for readability at gameplay distance
(silhouette first; one or two signature shapes per costume) — `docs/game-development-constitution/INDEX.md`.

## 4. Workflow (the loop that worked)

```bash
cd /Users/ferdinandfremming/Documents/hackerspace/spillgruppen/raxy_garment_kit
./run.sh sync                         # copy real raxy.blend -> raxy_src.blend, survey it, print its md5 (NOTE IT)
# write builders in a new module, e.g. outfits5.py, ending with GARMENTS = {...}; register it in garments.py
./run.sh build Clothes_Duelist_Coat,Clothes_Duelist_Gunbelt
./run.sh render r_duelist.png Clothes_Duelist_Coat,Clothes_Duelist_Gunbelt,Clothes_Tunic,Clothes_Trousers 160,90,20
./run.sh render r_walk.png  <same list> 150,210 walk body   # skinning check in a stride
# Read the PNGs, iterate. Then write into the REAL file:
python3 apply.py build <md5 from sync> batch5 Clothes_A,Clothes_B,...   # md5-guarded, auto-backup
/Applications/Blender.app/Contents/MacOS/Blender --background <real raxy.blend> --python validate.py   # VALIDATE OK
```

Then Unity (via `mcp__unityMCP__execute_code`; **check `EditorApplication.isPlaying` first — never in Play mode**):
1. `unity/1_materials_and_remap.cs.txt` — one row per new `raxy_<look>` material (same sRGB as `common.py`).
2. `python3 apply.py export`
3. `unity/2_prefabs_and_check.cs.txt` — expect `isHuman=True`, `embedded=0`, no `BROKEN`/`SLOTS`.
4. `unity/3_refresh_prefab_materials.cs.txt` — only if an existing garment's material list changed.
5. `unity/4_preview_render.cs.txt` — render the outfits in-engine and look at them.
6. Update `docs/AI/systems/CharacterClothes.md` (garment list in **Model**, new `.mat` looks, new Gotchas), bump
   `updated:`, run `python3 tools/docs_check.py --index` (errors in other systems' docs are not yours).

If `apply.py build` refuses (md5 changed), someone edited raxy.blend — re-run `./run.sh sync`, check what changed,
and never overwrite their work. Other agents/sessions work in this repo concurrently.

## 5. The kit, briefly

| File | Role |
|---|---|
| `cloth_lib.py` | `Body` (world-space body verts, folded bone weights, filtered BVHs), `Builder` (thick `sheet` from a point grid, `strap`, `tube_path`, `box`, `cylinder`, `sphere`, `torus`, `mesh_from`), `rig` (nearest-face weight transfer), `rig_single`, `material`, `frame`, `push_out`, `fit_grid`, `smooth_grid` |
| `fit.py` | body fitting: `torso_rows` / `torso_shell` (rings of rays at heights `('z', z)` and elevations `('phi', a)`), `skirt_rows` (hull of the legs below the hips), `limb_tube`, `hull_ring`, `dir_h`, `head_bvh` |
| `common.py` | `palette()` (every material: key → `raxy_<look>`, sRGB), `finish`, `radial`, `rolled_edge`, `mat_index` |
| `basic2.py`, `armor.py`, `pack.py`, `hats.py`, `bandit.py`, `techarmor.py`, `outfits2/3/4.py` | the builders, one function per garment, `GARMENTS` dict per module. Later modules override earlier names (`techarmor` replaces `armor`'s medieval builders; keep that order in `garments.py`) |
| `garments.py` | entry point: builds the named garments into the open .blend and saves |
| `render.py` / `run.sh` | Blender EEVEE preview renders (angles, `walk`/`nobody` pose, zoom `body/mid/head/legs`) |
| `apply.py`, `export_raxy.py` | write into the real file (guarded) / export with the exact flags that reproduce the shipped FBX (`keep_armature`, `scale_all=True`, `triangulate=True`) |
| `validate.py` | checks new garments: identity transform, all verts weighted, ≤4 bones, armature modifier, in `Clothes`, no zero-area faces, existing objects untouched (needs `inspect_src.json` from `sync`) |
| `debug/` | `dbg4.py` finds skin not covered by a garment; `dbg5.py` boundary edges; `dbg6.py` zero-area faces |

Useful builder patterns (copy from existing code): hanging panel → `outfits3.panel`; strap over the shoulders →
`pack.surface_path`; diagonal strap loop → `bandit.diagonal_loop`; dome/cap on the crown → `hats.dome` /
`hats.ell_point` with `outfits2.cap_e`; hinged visor → `outfits2.welder_mask`; lathe (jugs, cups, bottles) →
`outfits2.lathe`; ribbons → `outfits2.ribbon`; spikes/teeth/tufts → `outfits4.cone`; trousers-style legs with a
crotch seam → `outfits4.farmer_overalls`.

## 6. Numbers and conventions you will need

- **Frame (raxy.blend world):** the Raxy faces **−Y**; its **left is +X**; up +Z; height ~1.96 m in Blender (game
  scales it to 3 m). The rig object is rotated 180° about Z, so always use `body.bone(name)` (world space).
- **Unity garment mesh space = (−x, y, z) of Blender** — matters when placing `GarmentMounts` points.
- **Head:** crown top z 1.962; skull ±0.138 wide at z 1.90; eyes at z 1.84 (top 1.89), y −0.09…−0.14; snout to
  y −0.228 at z 1.68–1.75 (the `Jaw` bone moves when talking).
- **Ears (the constraint for every hat/hood/collar/back item):** they leave the skull at the back-top sides,
  |x| 0.08–0.15, y 0.05–0.15, up to **z 1.877**, then droop back/out to tips at |x| ~0.39, y ~0.2, z ~1.6. Hats sit
  on the crown above them; a pulled-up hood fails (holes so big it reads as a strip) — that is why the marauder's
  hood hangs down. Back items rising above z 1.6 must stay ≥ y 0.25 or between |x| < 0.07.
- **Torso:** very slim — waist |x| ~0.066 at z 1.0; shoulders out to |x| 0.188 at z 1.40; neck z 1.49–1.63, r ~0.05.
  Arms hang at |x| 0.25–0.35, hands down to z 0.69; legs from the crotch at z ~0.86.
- **Layer offsets (from skin) used so far:** tunic 0.024, trousers 0.03–0.054, vest 0.044, coats 0.045–0.05,
  armour/plates 0.05–0.09, straps over coats 0.07–0.1. New outer layers must clear what they are meant to go over.
- **Naming:** objects `Clothes_<Outfit>_<Piece>` (the `Clothes_` prefix is how Unity finds garments); materials
  `raxy_<look>` in Blender ↔ `Raxy_<Look>.mat` in Unity, added to `palette()` in `common.py`.
- **Rigging:** `finish(B, M, keys, body, allowed=… / allowed_fn=…)` transfers weights from the nearest body face
  restricted to the allowed bones. Below the waist always restrict to `Hips` + the matching leg (or the resting hand
  bleeds in). Rigid things (plates, boxes, canisters, buckles, props) get `B.island_weights[B.last] = {...}` right
  after creation so they move as one piece. Hats: `finish_head` (100 % `Head`). Fingers/toes fold into hand/foot.
- **Shading:** smooth with sharp edges > 60° (done in `to_object`). Flat-shaded clothes read as noise in game.

## 7. User taste (hard-won; follow it)

- **Dull, realistic, low-chroma colours**; earthy desert palette; accents muted (dull teal, oxblood, ochre).
- **No noise patterns:** no checkerboards, no camo-like patchwork (a patchwork coat was rejected in review and
  became one base colour with a few distinct sewn-on patches).
- **Not medieval.** Salvage and home-made tech fits: hull panels, bolts, hoses, cables, power cells, lamps, pipe,
  hose clamps, antennas, astronaut-suit parts from the colony. Settlement jobs and weather (sandstorms, water,
  mounts, caravans) are good sources of ideas.
- More detail than the user's originals is welcome (rims, rivets, buckles, stitched patches, straps, props), but
  each costume needs a clear silhouette.
- One garment = one prefab in the Clothes folder, each with its own colours.

## 8. Gotchas that cost time

- **Laplacian smoothing pulls a free grid boundary inward** — a dome flattened until its top row was fixed
  (`fix_rows=(0, rows-1)`).
- **Sparse rows let a limb poke between them** — the storm cloak needed rows every 4–5 cm around the shoulders and a
  `push_out` against the arm BVH. Use `debug/dbg4.py` to find uncovered skin numerically.
- **`hull_ring` over all body verts at a height** (`outfits2.all_hull`) is the way to make cloaks hang over the arms.
- **A strap whose path reverses** makes bow-tie quads (zero-area faces) — build arcs as one continuous parametric
  curve (see the welder mask's over-the-head strap).
- **Folds in a skirt can dip into a shin** — push skirt rows out from the leg BVH after smoothing.
- **`EnsurePrefabs` never updates materials of an existing garment prefab** (snippet 3 fixes it, keeping the GUID).
- **Play mode:** never export or render then; ask the user to stop Play mode and wait.
- `palette()` re-applies every colour on each build; changing a colour in `common.py` changes the .blend material but
  not the Unity `.mat` — update both.
- A `./run.sh build` that crashes part-way does not save `work.blend`: rebuild the garments that ran before the crash.

## 9. Done means

- [ ] 10 costumes (each 2–3 garments) built, reviewed in Blender renders (front/side/back) and a `walk` pose.
- [ ] Written into raxy.blend via `apply.py build` (backup made), `validate.py` → `VALIDATE OK`.
- [ ] New materials created and remapped; `raxy.fbx` exported in edit mode; snippet 2 clean; in-engine preview
      rendered and looked at.
- [ ] `CharacterClothes.md` updated, docs check clean for it; memory note added for anything that bit you.
- [ ] Report to the user with the render images and what is not done (no outfit prefabs, no play/client run, nothing
      committed).
