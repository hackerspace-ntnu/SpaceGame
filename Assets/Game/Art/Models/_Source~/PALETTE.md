# Material Palette

Generated from `palette.blend` by `scripts/palette.py`. Do not edit by hand —
edit the palette and regenerate, or the two will disagree.

Every model and component in this repository links its materials from here.
Before adding anything, search this table for something that would serve.

**54 material(s)** across 10 categor(ies).

## Emissive

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Emissive_Amber` | `#FFB347` | 0.3 | 0.0 | Warning lamps, gauge backlighting, engine glow, indicator strips. |
| `Mat_Emissive_Cabin_Warm` | `#FFF2D8` | 0.4 | 0.0 | Interior lighting strips and dome lamps - the warm liveable cabin light. |
| `Mat_Emissive_Green_CRT` | `#6BFF9E` | 0.2 | 0.0 | Console screens, holographic helm display, diagnostic readouts. |
| `Mat_Emissive_Portal_Blue` | `#2FB8FF` | 0.15 | 0.0 | Cold cyan-blue portal light: the blue portal aperture, the blue reservoir in the portal gun and its muzzle ring. The palette had no emissive blue at all - Mat_Emissive_Green_CRT is a readout and Mat_Glass_Canopy_Tinted is glazing, not a light source. |
| `Mat_Emissive_Portal_Orange` | `#FF8A1E` | 0.15 | 0.0 | Hot orange portal light: the orange portal aperture and the orange reservoir in the portal gun. Deliberately separate from Mat_Emissive_Amber, which is a warm indicator lamp at #FFB347 - the two portal colours have to read as a matched pair against each other, and amber is too pale to hold its own beside Portal_Blue. |
| `Mat_Emissive_Red_Warn` | `#FF4436` | 0.3 | 0.0 | Alarm strips, door-open beacons, reactor fault lamps. |

## Fabric

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Fabric_Canvas_Faded` | `#6E6A5A` | 0.92 | 0.0 | Bunk bedding, cargo netting, curtains, awning cloth, strapping. |
| `Mat_Fabric_Canvas_Sand` | `#F4BD62` | 0.88 | 0.0 | Sun-soaked golden-sand pack canvas: the expedition rig's boards, pouches and soft luggage. Warmer and brighter than Wing_Beige; fabric counterpart to Mat_Hide_Sand_Pale, which is creature skin, not cloth. |
| `Mat_Fabric_Flag_Bleached` | `#D8D2C2` | 0.9 | 0.0 | Off-white sun-bleached flag and pennant cloth, awnings, sun shades. Much lighter than Canvas_Faded, which is dirty webbing. |
| `Mat_Fabric_Rope_Hemp` | `#B89968` | 0.92 | 0.0 | Twisted natural-fibre rope: lariat coils, lashings, tow lines, rigging. Warmer and lighter than Mat_Fabric_Canvas_Faded, which is dirty grey webbing rather than laid rope. |
| `Mat_Fabric_Seat_Ochre` | `#8A5A2B` | 0.85 | 0.0 | Crew seat upholstery - cracked ochre vinyl, matches the hull family. |
| `Mat_Fabric_Tarp_Azure` | `#3E9AD0` | 0.9 | 0.0 | Saturated azure tarpaulin: shade sails and awnings pitched over field workspaces. The only strong colour note on a desert outpost, and nothing in the palette was within deltaE 20. Mat_Fabric_Flag_Bleached is its sun-killed counterpart. |
| `Mat_Fabric_Wing_Beige` | `#CBB68E` | 0.88 | 0.0 | Sun-cured beige sailcloth stretched over wing blade frames. The ornithopter's primary wing surface - warmer and dirtier than Flag_Bleached, lighter than Canvas_Faded. |
| `Mat_Fabric_Wing_Ochre` | `#C98551` | 0.88 | 0.0 | Sun-cured orange sailcloth stretched over the ornithopter's wing spars. The warmer, dustier counterpart to Wing_Beige - reads as canvas soaked in desert iron rather than bleached out by it. |

## Foliage

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Foliage_Leaf_Pale` | `#7E9B55` | 0.88 | 0.0 | Sunlit yellow-green leaf: the lit upper surfaces and hanging tips of the workshop's overgrowth, read against Mat_Foliage_Moss_Deep. Two tones are the minimum for foliage to have any form at all. |
| `Mat_Foliage_Moss_Deep` | `#4E6B3A` | 0.9 | 0.0 | Deep shaded plant green: the mat of moss and creeper massed on the workshop tank roof, and the shadowed underside of vine drapes. The palette had no living-plant material at all - Mat_Paint_Roof_Green is enamel on a hatch cover and Mat_Metal_Copper_Oxide is verdigris on pipework. |

## Glass

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Glass_Canopy_Tinted` | `#AEC4CC` | 0.05 | 0.0 | Cockpit canopy, side viewports, gauge covers. Lightly tinted, scratched. |

## Hide

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Hide_Claw_Horn` | `#4A3D2E` | 0.34 | 0.0 | Dark polished keratin: digging claws, tooth enamel, beak edges and horn tips on organic creatures. Lower roughness than the hide family because worn claw takes a shine. Distinct from Mat_Paint_Olive_Deep, which is enamel on steel. |
| `Mat_Hide_Dune_Tan` | `#C9BC9A` | 0.74 | 0.0 | Mid dune-tan creature hide: the Vrescal flanks, humps, neck and the kept head sculpt. Deliberately close in value to Mat_Hide_Scute_Umber so the armour mosaic reads as one hide. Started life as a much paler Bone_Cream, which against the dark plates turned the animal into cow markings and made the head look like a bare skull. |
| `Mat_Hide_Eye_Amber` | `#B8912F` | 0.12 | 0.0 | Wet gold-amber iris with a slit pupil: the Vrescal eye. Low roughness because an eye is the only glossy thing on an otherwise matte animal. Distinct from Mat_Emissive_Amber, which is a lamp. |
| `Mat_Hide_Ivory_Spine` | `#E2D8C0` | 0.38 | 0.0 | Pale ivory keratin: the Vrescal jaw spines, cheek horns and teeth. The Hide family had dark keratin (Mat_Hide_Claw_Horn) but no pale keratin, and the spines must read brighter than the hide they grow out of or they vanish into the flank. Forced past Mat_Fabric_Flag_Bleached (deltaE 5.6), which is cloth, and Mat_Hide_Dune_Tan (deltaE 8.0), which is the body colour the spines have to contrast against. |
| `Mat_Hide_Plate_Tan` | `#987340` | 0.62 | 0.0 | Darker tan keratin: the Vrescal's overlapping dorsal armour plates and scutes. Reads as hardened shell against the softer Mat_Hide_Sand_Pale skin. Distinct from Mat_Wood_Ply_Worn, which is scavenged plywood, not a body surface. |
| `Mat_Hide_Sand_Pale` | `#E7B345` | 0.72 | 0.0 | Pale sand-yellow creature hide: the Vrescal's head, belly, limbs and tail keel. The soft skin tone the darker keratin plates sit on. Distinct from Mat_Emissive_Amber, which is a lamp, not a surface. |
| `Mat_Hide_Scute_Umber` | `#6E5B47` | 0.58 | 0.0 | Dark desaturated umber keratin: the Vrescal's cracked armour mosaic. Deliberately close in value to Mat_Hide_Bone_Cream so the plate field reads as one hide rather than as cow markings - Mat_Hide_Plate_Tan is a mid orange-brown that blew out against the pale body. |
| `Mat_Hide_Slate_Teal` | `#5E7B7A` | 0.68 | 0.0 | Cold blue-green creature hide: the Vrescal's throat, belly, inner limbs and foot pads. The desaturated counter-tone the warm Mat_Hide_Sand_Pale flank reads against, and the only cool note on the animal. Distinct from Mat_Metal_Copper_Oxide, which is verdigris on metal rather than skin. |

## Metal

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Metal_Brass_Tarnished` | `#9C7B3F` | 0.45 | 1.0 | Scavenged brass: gear teeth, bearing collars, crank pins, linkage bushings. The homemade machined-from-scrap look on the ornithopter's wing drive. |
| `Mat_Metal_Chrome_Scuffed` | `#C9CDD2` | 0.22 | 1.0 | Bright trim: handles, grab rails, wheel rim, galley fittings, RV chrome mouldings. |
| `Mat_Metal_Copper_Oxide` | `#4E8C7A` | 0.6 | 0.8 | Verdigris pipework and coil windings - coolant runs, reactor plumbing, old wiring conduit. |
| `Mat_Metal_Gold_Leaf` | `#E0B33A` | 0.26 | 1.0 | Bright polished gold leaf: the dragon head's horns, fangs, brow ridge, whisker wire and the tube's ceremonial banding. Mat_Metal_Brass_Tarnished (#9C7B3F, roughness 0.45) is scavenged machine brass - dull, dark and deliberately cheap-looking - and using it here made the dragon read as plumbing. Gold has to out-shine the vermilion it sits on or the ornament disappears at arm's length. |
| `Mat_Metal_HullRust_Orange` | `#764E2A` | 0.72 | 0.15 | (existing) Primary hull skin of the RV ship - oxidised orange-brown steel. Main exterior body colour. |
| `Mat_Metal_Rust_Heavy` | `#9A5D1D` | 1.0 | 0.5 | (existing) Deep corrosion: weld-on repair patches, streak damage, exhaust scorching, rotted panel edges. |
| `Mat_Metal_Steel_Dark` | `#3A3E42` | 0.45 | 1.0 | Machined mechanism metal: brackets, bolts, hydraulic rams, engine internals, tool bodies. |
| `Mat_Metal_Steel_Worn` | `#7A7D80` | 0.55 | 1.0 | Bare structural steel: frames, ribs, beams, hinge barrels, exposed load-bearing parts. |

## Neutral

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Neutral_Black_Matte` | `#272727` | 0.55 | 0.0 | (existing) Seals, gaskets, shadow gaps, recessed backing behind grilles and vents. |
| `Mat_Neutral_Panel_Grey` | `#606060` | 0.5 | 0.0 | (existing) Interior wall and ceiling panelling, deck plates, generic cabin surfaces. |
| `Mat_Neutral_Slate_Dark` | `#1F2736` | 0.7 | 0.0 | (existing) Dark blue-black trim: engine nacelle shells, wings, underside, contrast panels. |

## Paint

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Paint_Blue_Station` | `#9FB8CE` | 0.6 | 0.35 | Pale powder-blue enamel over steel: the desert outpost's prefab hull skin, tower shaft and sensor cupola. The cool blue member of the painted-hull family alongside Mat_Paint_White_Arctic (arctic off-white) and Mat_Paint_Hull_Bleached (warm desert sun-bleach). Distinct from Mat_Glass_Canopy_Tinted despite a close hue - that is glazing at roughness 0.05, this is chalky paint. |
| `Mat_Paint_Butter_Pastel` | `#E8CE8C` | 0.6 | 0.25 | Soft warm butter-yellow pastel enamel: workshop settlement cottage walls and shutter panels. Much lighter and creamier than Mat_Paint_Hazard_Yellow, which is a sun-dulled warning colour, and unlike Mat_Plastic_Safety_Yellow it is paint on a surface rather than moulded plastic. |
| `Mat_Paint_Coral_Faded` | `#D9705E` | 0.62 | 0.25 | Sun-faded coral enamel over steel: the outpost tower's habitat blocks, control cab roof band and machine module skins. The warm mass colour that reads against the grey-blue steelwork of the lattice, matching the reference print's duotone. Distinct from Mat_Paint_Safety_Orange, which is fresh high-vis construction paint, and from Mat_Metal_HullRust_Orange, which is oxidised bare steel rather than a painted surface. |
| `Mat_Paint_Hazard_Yellow` | `#C9A94E` | 0.55 | 0.3 | Sun-dulled hazard-yellow enamel over steel: the Sucker Puncher's striped shield plate, warning chevrons on machine guards and lifting gear. The painted-hull family (Safety_Orange, White_Arctic, Coral_Faded, Blue_Station) had no yellow member at all. Distinct from Mat_Plastic_Safety_Yellow, which is bright moulded plastic at metallic 0 for trigger guards and pull rings, and from Mat_Hide_Eye_Amber, which is a wet eyeball. |
| `Mat_Paint_Hull_Bleached` | `#AAA499` | 0.68 | 0.6 | Sun-bleached olive-white paint over steel. The desert crawler's body panels, leg shrouds and container modules. |
| `Mat_Paint_Lacquer_Vermilion` | `#C1272D` | 0.28 | 0.0 | Deep glossy vermilion lacquer: the dragon bazooka's tube body, the dragon head's scaled crown and the rocket casings. The Paint family's reds were Mat_Paint_Warn_Red (#8E2B22), a matte stencilled hazard band at roughness 0.55, and Mat_Paint_Coral_Faded, a sun-killed hull tone - neither reads as the wet ceremonial lacquer a dragon is finished in. Low roughness is the point: this is the only glossy painted surface in the palette. |
| `Mat_Paint_Mint_Pastel` | `#B9D2BE` | 0.6 | 0.25 | Pale sage-mint pastel enamel over steel and board: the workshop settlement's outbuilding walls. The painted-hull family (Safety_Orange, White_Arctic, Coral_Faded, Blue_Station, Hazard_Yellow) had no green member and no pastel at all. Forced past Mat_Fabric_Flag_Bleached (deltaE 11.8), which is an off-white cloth awning at metallic 0 rather than a green painted wall. |
| `Mat_Paint_Olive_Deep` | `#3F4A3A` | 0.62 | 0.4 | Deep olive shadow panels and recesses - the contrast tone that keeps large bleached surfaces from flattening out. |
| `Mat_Paint_Roof_Green` | `#6E7A5E` | 0.6 | 0.4 | Faded military green: roof caps, banded accent panels, hatch covers. Reads as the older paint layer under the bleached topcoat. |
| `Mat_Paint_Rose_Dusty` | `#D6A79C` | 0.62 | 0.25 | Chalky dusty-rose pastel enamel: workshop settlement cottage walls. The desaturated pale cousin of Mat_Paint_Coral_Faded, which is a much stronger sun-faded coral used as a mass colour on the outpost tower. |
| `Mat_Paint_Safety_Orange` | `#D9541F` | 0.52 | 0.2 | High-visibility construction orange: the refinery tower's landing legs, cantilever spine, conveyor ramp and accent modules. Reads as fresh paint against Mat_Metal_HullRust_Orange, which is the weathered oxidised version. |
| `Mat_Paint_Warn_Red` | `#8E2B22` | 0.55 | 0.2 | Matte hazard red: stencilled roundels, danger bands, lifting-point marks. The non-glowing counterpart to Mat_Emissive_Red_Warn. |
| `Mat_Paint_White_Arctic` | `#D6DAD9` | 0.58 | 0.35 | Cool off-white enamel over steel: the refinery tower's slab cladding and module skins. The arctic counterpart to Mat_Paint_Hull_Bleached, which is warm desert sun-bleach. |

## Plastic

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Plastic_Cream_Aged` | `#B8AD94` | 0.6 | 0.0 | Yellowed RV interior plastic: cabinet fronts, switch panels, light diffusers, trim mouldings. |
| `Mat_Plastic_Rubber_Black` | `#1A1A1A` | 0.88 | 0.0 | Hoses, cable sheathing, hand grips, floor matting, door weather seals. |
| `Mat_Plastic_Safety_Yellow` | `#F2B01E` | 0.45 | 0.0 | Injection-moulded high-vis yellow plastic: safety pins, pull rings, trigger guards, lever grips. The moulded-plastic counterpart to Mat_Paint_Safety_Orange, which is enamel sprayed onto steel. |

## Wood

| Name | Hex | Roughness | Metallic | Intended for |
|---|---|---|---|---|
| `Mat_Wood_Ply_Worn` | `#8C6A44` | 0.75 | 0.0 | Scavenged plywood: galley counter, shelf boards, patched-in cabinetry. The RV domestic touch. |
| `Mat_Wood_Timber_Silvered` | `#9A9186` | 0.85 | 0.0 | Weathered grey-silvered softwood: scaffold planks, lashed stilt poles, toe boards and shanty cladding. The Wood family had only Mat_Wood_Ply_Worn, a warm brown scavenged plywood - bare timber left out in the sun goes grey, and the two read as different ages of the same settlement. |
