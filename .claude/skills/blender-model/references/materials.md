# Materials and the palette

## Why one palette

Every model in the project draws from a single shared set of materials. This keeps the asset set visually coherent — pieces built weeks apart still look like they belong to the same world — and keeps the material count downstream small.

The palette **starts empty**. It is not seeded with a generic color set, because a generic color set belongs to no project in particular: a derelict industrial station and a sunlit market town need different palettes, and a pre-baked list would be wrong for both while looking authoritative.

## The cost of growing on demand

An empty palette that accepts every request is worse than a pre-baked one. Each model adds "just one" slightly-different grey, and within thirty models there are eleven greys nobody can tell apart, each used once. At that point the palette has stopped constraining anything.

So the discipline is: **the palette is a shared resource, and adding to it is a deliberate act with a justification.** Reaching for an existing material that is 95% right is almost always correct. Adding a new one is right when the difference is intentional and reads at the distance the asset is actually viewed from.

`scripts/palette.py` enforces this — `add` compares the requested color against everything already in the palette and refuses when something perceptually equivalent exists, naming the material to use instead.

## Structure

`Assets/Models/_Source~/palette.blend` holds the material datablocks. Models **link** from it rather than defining copies, so palette edits propagate.

`Assets/Models/_Source~/PALETTE.md` is generated from `palette.blend` and documents every material: name, hex, roughness, metallic, and what it is intended for. Read it before every build — it is the fastest way to find the material you should be reusing.

The metadata lives as custom properties on the material datablocks, so the documentation is regenerated from the file and can never drift away from it. Never hand-edit `PALETTE.md`.

## Naming

`Mat_<Category>_<Material>_<Qualifier>`

Categories emerge from the project rather than being prescribed. Useful ones tend to include `Neutral`, `Metal`, `Wood`, `Plastic`, `Stone`, `Fabric`, `Glass`, `Emissive`, `Accent`, `Organic` — but adopt what the project actually needs. Once a category exists, keep using it rather than inventing a synonym; `Metal` and `Metals` in the same palette is a defect.

Qualifiers describe finish or state: `_Worn`, `_Clean`, `_Dark`, `_Light`, `_Matte`, `_Gloss`, `_Rough`.

Examples: `Mat_Metal_Steel_Worn`, `Mat_Wood_Pine_Light`, `Mat_Emissive_Amber`.

## Commands

Create the empty palette once, at library setup:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- init
```

Before adding anything, look at what exists:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- list
blender --background --python "$SKILL_DIR"/scripts/palette.py -- check --hex 7A7D80 --metallic 1.0
```

`check` reports existing materials near that color, with a perceptual distance. Run it before every add — it usually ends the question.

Add a material:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- add \
    --category Metal --name Steel_Worn --hex 7A7D80 \
    --roughness 0.55 --metallic 1.0 \
    --note "Hull plating, structural beams, used equipment"
```

Optional: `--transmission` and `--ior` for glass, `--emission` for light sources.

`--note` is required, and it is the field that matters most. "Hull plating, structural beams, used equipment" is what makes the next person reuse this material instead of adding a twelfth grey. "grey" is not a note.

Regenerate the documentation after editing the palette by hand in Blender:

```bash
blender --background --python "$SKILL_DIR"/scripts/palette.py -- doc
```

## The duplicate guard

`add` converts the requested color to CIELAB and compares it against every existing material at a similar metallic value. Metallic is part of the comparison because the same hex as brushed metal and as matte plastic are genuinely different materials, not duplicates.

- **ΔE ≤ 5** — the same color for practical purposes. The add is refused and the existing material is named. Use it.
- **ΔE ≤ 12** — close. The add proceeds with a note. Confirm you actually need both.
- **Above that** — distinct. Proceeds silently.

`--force` overrides a refusal. Reach for it rarely, and when you do, make `--note` explain why both exist — otherwise the next person cannot tell which to use.

If a refusal seems wrong, the usual cause is that the existing material's roughness is wrong for your case rather than its color. Roughness is a property of the material, not a reason for a second one at the same color — consider whether the existing entry should be adjusted instead.

## Assignment

Assign materials at the object level for whole-object colors, at the face level only where a single mesh genuinely needs several — a control panel with a screen, a crate with metal banding.

Keep material slots minimal. If a component has seven slots, it probably wants to be several components.

Never create a material inside a model or component file. A local material does not propagate, does not appear in the documentation, and will be silently duplicated by the next person who needs the same color.
