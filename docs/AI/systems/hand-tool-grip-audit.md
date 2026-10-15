# Hand tool grip audit

Generated from `HandToolAudit` and `HandToolWorkAudit` on 2026-10-03 -- regenerate rather than hand-edit (**Items > Hand Tools > Audit Grips** / **Audit Work Clips** write `Temp/HandToolPreview/grip_audit.csv` and `work_audit.csv`). Companion to [HandTools.md](HandTools.md).

Measured by `HandToolAudit` (**Items ▸ Hand Tools ▸ Audit Grips**) on `Raxy_handyman`, every built prefab seated by the real `EquipItemSocket` in the pose its stance names. Columns: **along** / **face** = degrees the tool's two reference axes are from its stance (along its length, and about it: the rotation the along axis cannot see); **fist→root** = centimetres from where the roster put the root (the middle of the closed fist plus the row's nudge) to where it is; **root→surface** = to the nearest vertex of the tool; **off-axis** = how far the handle's centre line is from the root; **grip** = how far along the tool's length the root is (0 = butt, 1 = business end); **floor** = lowest point of the tool above the soles, metres; **wrist** = degrees between the tool's axis and the line a fist closes on (across the knuckles for shafts, along the fingers for aimed tools; meaningless for a vessel, whose grip axis is its bail, so shown for `Wield` and `Staff` only). Rules: right = along and face within 1 degree, fist→root within 3 cm, nothing through the floor, wrist within 50 degrees, root not within 5 percent of either end. Every row below passes (the audit is in `HandToolGripTests`); the notes say what each group was before.

| Tool | Stance / pose | along | face | fist→root cm | root→surface cm | off-axis cm | grip | floor m | wrist | Grade |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Carry_Basket_Open | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.95 | 0.86 | - | right (was wrong) |
| Carry_Basket_Wicker | Hang / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.97 | 0.58 | - | right (was wrong) |
| Carry_Bucket_Metal | Hang / Carry | 0.0 | 0.0 | 0 | 0 | 0 | 0.96 | 0.73 | - | right (was wrong) |
| Carry_Bucket_Wood | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.96 | 0.70 | - | right (was wrong) |
| Carry_Cart_Hand | Push / Push | 0.0 | 0.0 | 0 | 3 | 0 | 0.02 | 1.68 | - | wrong: the Push pose carries the hands at head height in the faithful body and the cart on the shoulder line; replaced by the pushable carts in phase 5 (another session) |
| Carry_Cart_Hover | Push / Push | 0.0 | 0.0 | 0 | 2 | 4 | 0.01 | 1.83 | - | wrong: as Carry_Cart_Hand |
| Carry_Tank_Oil | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 2 | 0.97 | 0.57 | - | right: handle 2 cm off axis |
| Carry_Tank_Water | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.97 | 0.57 | - | right (was wrong) |
| Carry_Yoke | Hang / Carry | 0.0 | 0.0 | 0 | 4 | 0 | 0.96 | 0.24 | - | right (was wrong) |
| Tool_Adze | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.58 | 0.96 | 20 | right (was wrong) |
| Tool_Awl | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.41 | 1.24 | 20 | right (was wrong) |
| Tool_BaitPouch | Hang / Carry | 0.0 | 0.0 | 0 | 2 | 1 | 0.46 | 1.11 | - | right (was wrong) |
| Tool_BandageRoll | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.37 | 1.26 | - | right (was wrong) |
| Tool_BoneSaw | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.19 | 1.25 | 20 | right (was wrong) |
| Tool_BowDrill | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 2 | 0.46 | 1.13 | 20 | right (was wrong) |
| Tool_BrickMould | Hang / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.93 | 1.03 | - | right (was wrong) |
| Tool_Broom | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.45 | 0.61 | 36 | right (was wrong) |
| Tool_Chisel | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.51 | 1.11 | 20 | right (was wrong) |
| Tool_Cleaver | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 0 | 0.36 | 1.20 | 20 | right (was wrong) |
| Tool_Club_Knotted | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 1 | 0.43 | 1.05 | 20 | right (was wrong) |
| Tool_Club_Studded | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.38 | 1.02 | 20 | right (was wrong) |
| Tool_CookingPot | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.94 | 0.93 | - | right (was wrong) |
| Tool_Crowbar | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.50 | 0.97 | 20 | right (was wrong) |
| Tool_Dibber | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.56 | 1.11 | 20 | right (was wrong) |
| Tool_ElectricHarpoon | Staff / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.63 | 0.45 | 36 | right (was wrong) |
| Tool_ElectricHarpoonGun | Aim / TwoHanded | 0.0 | 0.0 | 0 | 1 | 13 | 0.41 | 1.42 | - | right: root is the pistol grip under the body, so it is 13 cm off the body axis; the off hand is open, not on a foregrip |
| Tool_FilterCloth | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 0 | 0.34 | 1.19 | 20 | right (was wrong) |
| Tool_FishingNet | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.35 | 0.57 | 36 | right (was wrong) |
| Tool_FishingRod | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.12 | 1.05 | 36 | right (was wrong) |
| Tool_Flask | Hang / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.78 | 1.07 | - | right (was wrong) |
| Tool_FleshingScraper | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 1 | 0.94 | 0.93 | 20 | right (was wrong) |
| Tool_Hammer | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 1 | 0.52 | 1.04 | 20 | right (was wrong) |
| Tool_HandAxe | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.57 | 0.98 | 20 | right (was wrong) |
| Tool_HandDrill | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.23 | 1.20 | 20 | right (was wrong) |
| Tool_HandPump | Aim / Carry | 0.0 | 0.0 | 0 | 4 | 0 | 0.14 | 0.98 | - | right (was up to 6 degrees off) |
| Tool_HandSaw | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.26 | 1.20 | 20 | right (was wrong) |
| Tool_HandScales | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.70 | 0.90 | - | right (was wrong) |
| Tool_Harpoon_Bone | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.40 | 0.43 | 36 | right (was wrong) |
| Tool_Harpoon_Float | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.39 | 0.46 | 36 | right (was wrong) |
| Tool_Harpoon_Iron | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.42 | 0.38 | 36 | right (was wrong) |
| Tool_HerbKnife | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 0 | 0.50 | 1.19 | 20 | right (was wrong) |
| Tool_HerdingCrook | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.51 | 0.53 | 36 | right (was wrong) |
| Tool_Hoe | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.38 | 0.77 | 36 | right (was wrong) |
| Tool_HookPole | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.36 | 0.57 | 36 | right (was wrong) |
| Tool_HuntingKnife | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.28 | 1.25 | 20 | right (was wrong) |
| Tool_Ladle | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.43 | 1.03 | 20 | right (was wrong) |
| Tool_Lantern | Hang / Carry | 0.0 | 0.0 | 0 | 0 | 0 | 0.95 | 0.98 | - | fixed: pose was Torch (free arm raised) |
| Tool_Lasso | Hang / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.93 | 0.66 | - | right (was wrong) |
| Tool_MagnetPole | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.42 | 0.62 | 36 | right (was wrong) |
| Tool_Mallet | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.50 | 0.93 | 20 | right (was wrong) |
| Tool_MeasuringStaff | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.50 | 0.47 | 36 | right (was wrong) |
| Tool_MortarPestle | Hang / Carry | 0.0 | 0.0 | 0 | 0 | 2 | 0.94 | 1.07 | - | right (was wrong) |
| Tool_OilCan | Aim / Carry | 0.0 | 0.0 | 0 | 1 | 1 | 0.05 | 1.22 | - | right (was up to 6 degrees off) |
| Tool_OilRag | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 1 | 0.73 | 1.10 | - | right (was wrong) |
| Tool_PatchKit | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 1 | 0.45 | 1.25 | 20 | right (was wrong) |
| Tool_Pickaxe | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.53 | 0.63 | 36 | right (was wrong) |
| Tool_Pickaxe_Rust | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.53 | 0.63 | 36 | right (was wrong) |
| Tool_PipeWrench | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.49 | 1.14 | 20 | right (was wrong) |
| Tool_Pliers | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 0 | 0.32 | 1.26 | 20 | right (was wrong) |
| Tool_PlumbLine | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.37 | 1.13 | 20 | right (was wrong) |
| Tool_PoulticeBowl | Hang / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.91 | 1.11 | - | right (was wrong) |
| Tool_Rake | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.43 | 0.69 | 36 | right (was wrong) |
| Tool_RockHammer | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.70 | 0.98 | 20 | right (was wrong) |
| Tool_SandBrush | Wield / Ready | 0.1 | 0.0 | 0 | 4 | 0 | 0.55 | 0.97 | 20 | right (was wrong) |
| Tool_ScrapCutter | Staff / Carry | 0.0 | 0.0 | 0 | 3 | 0 | 0.35 | 0.86 | 36 | right (was wrong) |
| Tool_Shield | Aim / OneHanded | 0.0 | 0.0 | 0 | 2 | 4 | 0.24 | 1.38 | - | right: held by the arm strap, 4 cm off axis |
| Tool_ShortBow | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 4 | 0.50 | 0.76 | 36 | right: root is the riser grip, 4 cm off the limb line |
| Tool_Shovel | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.42 | 0.67 | 36 | right (was wrong) |
| Tool_Sickle | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.44 | 1.11 | 20 | right (was wrong) |
| Tool_SiftingPan | Aim / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.20 | 1.28 | - | right (was up to 6 degrees off) |
| Tool_SignalFlag | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.39 | 0.72 | 36 | fixed: pose was Torch (free arm raised) |
| Tool_SignalHorn | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 4 | 0.41 | 1.14 | 20 | right: held by the cord loop beside the horn |
| Tool_SkinningKnife | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.29 | 1.30 | 20 | right (was wrong) |
| Tool_Sling | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.93 | 0.69 | - | right (was wrong) |
| Tool_Snare | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.30 | 1.17 | 20 | right (was wrong) |
| Tool_Spear_Bone | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.43 | 0.35 | 36 | right (was wrong) |
| Tool_Spear_Stone | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.42 | 0.42 | 36 | right (was wrong) |
| Tool_Spindle | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 0 | 0.43 | 1.21 | 20 | right (was wrong) |
| Tool_Splint | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 1 | 0.48 | 1.14 | 20 | right (was wrong) |
| Tool_Spyglass | Aim / OneHanded | 0.0 | 0.0 | 0 | 2 | 1 | 0.58 | 1.81 | - | right (was up to 6 degrees off) |
| Tool_TanningPaddle | Staff / Carry | 0.0 | 0.0 | 0 | 2 | 0 | 0.40 | 0.69 | 36 | right (was wrong) |
| Tool_ThrowingNet | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 0.91 | 0.44 | - | right (was wrong) |
| Tool_Trowel | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.41 | 1.11 | 20 | right (was wrong) |
| Tool_WaterSkin | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 0 | 1.00 | 0.89 | - | right (was wrong) |
| Tool_WateringCan | Hang / Carry | 0.0 | 0.0 | 0 | 1 | 4 | 0.94 | 0.89 | - | right: held by the arc handle, 4 cm off the body axis |
| Tool_Whetstone | Wield / Ready | 0.1 | 0.0 | 0 | 1 | 1 | 0.43 | 1.26 | 20 | right (was wrong) |
| Tool_WireCutters | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 0 | 0.36 | 1.26 | 20 | right (was wrong) |
| Tool_Wrench_Open | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 1 | 0.52 | 1.14 | 20 | right (was wrong) |
| Tool_Wrench_Ring | Wield / Ready | 0.1 | 0.0 | 0 | 2 | 1 | 0.51 | 1.15 | 20 | right (was wrong) |

**Counts.** 89 tools: 87 right, 2 wrong (the carts, `Push`). Before this work all 89 had their root about 15 cm short of the fist; the 37 `Wield` tools were also 10 degrees off in the game body, the 22 `Staff` tools needed 49 degrees of wrist, the shield, spyglass and harpoon gun were up to 6 degrees off, and the 2 `Lantern`/`SignalFlag` tools raised the free arm. The shared groups (the audit's first question): `Wield` 37 and `Staff` 22 and `Hang` 22 were each wrong by one shared value (the fit offset), and the same fix corrected all of them; no per-tool rotation was needed, because the models agree on their axes (+Y along the tool, +Z the working face: hammer face, blade edge, hoe blade, pickaxe heads along Z).

## Work clips

`HandToolWorkAudit` (**Audit Work Clips**) holds each tool in the Carry/Ready pose it is built for, then plays every variant of the work clips the job cues give it and samples 24 times over one cycle. Rule used, because the work clips carry no `Contact` mark: **lowest tip** = the lowest the tool's business end (the farthest vertex along +Y) gets above the soles, metres, over the cycle; **shaft angle** = the acute angle between the tool's axis and the line between the two hands at that moment (a two-handed clip says where the shaft was; 0 is aligned); **hands** = how far apart they are (a clip with one hand on the tool has them far apart). A ground clip should bring the tip to about 0 and a two-handed one should have a small shaft angle. Neither is a contact point, so a number here is evidence, not a verdict.

| Tool | Clip | Variant | lowest tip m | shaft angle | hands m |
| --- | --- | --- | --- | --- | --- |
| Tool_Hoe | Farm Plough | 0 | 1.24 | 15 | 0.39 |
| Tool_Hoe | Farm Plough | 1 | 1.25 | 17 | 0.44 |
| Tool_Hoe | Dig | 0 | 1.65 | 60 | 0.50 |
| Tool_Shovel | Dig | 0 | 1.62 | 87 | 0.35 |
| Tool_Pickaxe | Mine Ground | 0 | 1.15 | 55 | 1.10 |
| Tool_Pickaxe | Mine Ground | 1 | 1.10 | 50 | 1.26 |
| Tool_Pickaxe | Mine Ground | 2 | 1.10 | 54 | 1.13 |
| Tool_Pickaxe | Mine Ground | 3 | 1.08 | 49 | 1.29 |
| Tool_Pickaxe | Mine Wall | 0 | 2.14 | 79 | 1.13 |
| Tool_Pickaxe | Mine Wall | 1 | 1.52 | 53 | 1.13 |
| Tool_Pickaxe | Mine Wall | 2 | 2.14 | 80 | 1.17 |
| Tool_Pickaxe | Mine Wall | 3 | 1.52 | 52 | 1.16 |
| Tool_Pickaxe | Dig | 0 | 1.55 | 60 | 0.50 |
| Tool_Pickaxe_Rust | Mine Ground | 0 | 1.15 | 55 | 1.10 |
| Tool_Pickaxe_Rust | Mine Ground | 1 | 1.10 | 50 | 1.26 |
| Tool_Pickaxe_Rust | Mine Ground | 2 | 1.10 | 54 | 1.13 |
| Tool_Pickaxe_Rust | Mine Ground | 3 | 1.08 | 49 | 1.29 |
| Tool_Pickaxe_Rust | Mine Wall | 0 | 2.14 | 79 | 1.13 |
| Tool_Pickaxe_Rust | Mine Wall | 1 | 1.52 | 53 | 1.13 |
| Tool_Pickaxe_Rust | Mine Wall | 2 | 2.14 | 80 | 1.17 |
| Tool_Pickaxe_Rust | Mine Wall | 3 | 1.52 | 52 | 1.16 |
| Tool_RockHammer | Mine Ground | 0 | 0.85 | 60 | 1.10 |
| Tool_RockHammer | Mine Ground | 1 | 0.98 | 35 | 1.26 |
| Tool_RockHammer | Mine Ground | 2 | 0.80 | 59 | 1.13 |
| Tool_RockHammer | Mine Ground | 3 | 0.96 | 33 | 1.29 |
| Tool_RockHammer | Hammer | 0 | 1.50 | 40 | 0.87 |
| Tool_ScrapCutter | Mine Wall | 0 | 1.99 | 76 | 1.54 |
| Tool_ScrapCutter | Mine Wall | 1 | 1.58 | 53 | 1.13 |
| Tool_ScrapCutter | Mine Wall | 2 | 1.99 | 76 | 1.56 |
| Tool_ScrapCutter | Mine Wall | 3 | 1.59 | 52 | 1.16 |
| Tool_Hammer | Hammer Ground | 0 | 0.70 | 58 | 1.01 |
| Tool_Hammer | Hammer Ground | 1 | 1.11 | 20 | 1.51 |
| Tool_Hammer | Hammer Ground | 2 | 0.72 | 60 | 1.07 |
| Tool_Hammer | Hammer Ground | 3 | 1.14 | 12 | 1.59 |
| Tool_Hammer | Hammer | 0 | 1.55 | 37 | 0.87 |
| Tool_Mallet | Hammer Ground | 0 | 0.77 | 77 | 0.78 |
| Tool_Mallet | Hammer Ground | 1 | 1.22 | 34 | 0.77 |
| Tool_Mallet | Hammer Ground | 2 | 0.80 | 77 | 0.86 |
| Tool_Mallet | Hammer Ground | 3 | 1.24 | 63 | 0.79 |
| Tool_Mallet | Hammer | 0 | 1.67 | 28 | 0.92 |
| Tool_HandSaw | Saw | 0 | 2.01 | 85 | 0.44 |
| Tool_BoneSaw | Saw | 0 | 2.07 | 80 | 0.44 |
| Tool_FishingRod | Fish Rod | 0 | 3.53 | 28 | 0.21 |
| Tool_FishingRod | Fish Rod | 1 | 3.59 | 36 | 0.28 |
| Tool_Broom | Sweep | 0 | 1.96 | 50 | 0.66 |
| Tool_SandBrush | Sweep | 0 | 1.56 | 87 | 0.54 |
| Tool_Wrench_Ring | Wrench Tighten | 0 | 1.59 | 30 | 0.41 |
| Tool_Wrench_Open | Wrench Tighten | 0 | 1.60 | 30 | 0.41 |
| Tool_PipeWrench | Wrench Tighten | 0 | 1.56 | 30 | 0.41 |
| Tool_Ladle | Stir Pot | 0 | 1.61 | 89 | 0.60 |
| Tool_Cleaver | Chop Food | 0 | 1.76 | 59 | 0.63 |
| Tool_Dibber | Plant Seedling | 0 | 1.46 | 43 | 0.72 |
| Tool_Trowel | Poke Ground | 0 | 0.67 | 87 | 1.13 |
| Tool_Spear_Stone | Stab | 0 | 2.27 | 35 | 1.21 |
| Tool_Spear_Bone | Stab | 0 | 2.27 | 35 | 1.21 |
| Tool_HuntingKnife | Stab | 0 | 1.96 | 45 | 1.05 |
| Tool_SkinningKnife | Stab | 0 | 1.89 | 45 | 1.05 |
| Tool_Club_Knotted | Sword Strike | 0 | 0.80 | 35 | 1.63 |
| Tool_Club_Studded | Sword Strike | 0 | 0.81 | 35 | 1.63 |
| Tool_HandAxe | Sword Strike | 0 | 0.71 | 35 | 1.63 |
| Tool_Harpoon_Iron | Spear Throw | 0 | 0.67 | 41 | 1.34 |
| Tool_Lasso | Lasso Throw | 0 | 1.36 | 69 | 1.33 |

**Reading it.** The staff tools' shaft angle in the plough fell from 35 degrees to 15 (hoe) with the fist fix and the leaning staff; the hammers and mallets range from 12 to 77 degrees by variant, the bench variants nearest. Nothing reaches the ground in the plough, dig, mine and ground-hammer clips (lowest tip 0.7 to 1.65 m): the tools are longer than the props those clips were captured with and a Raxy's hands are 1.5 times a human's height, so the contact point is a station problem (the stand point, [the plan](../../superpowers/plans/2026-10-03-activity-contract-animation.md) phase 4), which a grip cannot solve. Grips were NOT re-fitted per work clip: a natural grip (the tool along the knuckle line, working face along the fingers) was tried against all 62 rows and was better for the hoe (tip 0.82 against 1.71 at the old fit) and the pickaxe, worse for the spear and a few hammer variants, so one grip per tool cannot serve every clip; the leaning staff and the fist fix were kept as the compromise.
