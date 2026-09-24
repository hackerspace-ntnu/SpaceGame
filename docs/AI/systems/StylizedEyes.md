---
system: StylizedEyes
layer: pipeline
summary: "Character eyes: authored assets baked to equirect maps on a re-unwrapped sphere, with painted lids that blink"
paths:
  - Assets/Game/Editor/Agents/StylizedEyeBuilder.cs
  - Assets/Game/Editor/Agents/EyeStyle.cs
  - Assets/Game/Editor/Agents/EyeStyleEditor.cs
  - Assets/Game/Editor/Agents/EyeStylePresets.cs
  - Assets/Game/Editor/Agents/EyelidWiring.cs
  - Assets/Game/Art/Shaders/Characters
  - Assets/Game/Scripts/Presentation/Appearance/EyeBlink.cs
  - Assets/Game/Scripts/Presentation/Appearance/BlinkRhythm.cs
  - Assets/Game/Art/Textures/Characters/Eyes
  - Assets/Game/Art/Models/Characters/EyeMeshes
symptoms:
  - "a character's eyes are the same colour as its skin, two bare beads in the sockets"
  - "a painted eye texture comes out as two or four pupils, mirrored, on one eyeball"
  - "the pupil ends up in the side of the head instead of facing forward"
  - "an eye texture renders as scrambled checkered patches in Blender but the UV grid looks fine"
  - "every eye colour variant looks like the same pale white blob in game"
  - "a builder run over unity-mcp reports success and writes the values from the previous version of the script"
  - "a character's eyes went back to the old colour, or stopped matching its style asset"
  - "a pupil that looks right in the Inspector fills the whole eye on the character"
  - "a character never blinks, or its eyes stay open after it dies"
  - "the eyelids are darker and more saturated than the face around them"
  - "a hairline of skin colour runs down the back of an eyeball"
  - "sampling the skin around the eyes finds no vertices near either socket"
  - "one character's lids close at a slant"
  - "the pupil shows a grid of flat facets under the key light"
  - "a re-unwrapped eye still renders the old mesh although its normals and file are correct"
reads_with: [ArtPipeline, AgentSystem, PlayerCharacter]
updated: 2026-09-24
---

# Stylized Eyes

Eye looks authored as assets and baked into equirectangular albedo (and emission) maps, on materials whose shader also paints the eyelids a character blinks with, plus the mesh repair that lets a character's eye sphere wear one.

**Scope:** [EyeStyle.cs](Assets/Game/Editor/Agents/EyeStyle.cs), [EyeStyleEditor.cs](Assets/Game/Editor/Agents/EyeStyleEditor.cs), [EyeStylePresets.cs](Assets/Game/Editor/Agents/EyeStylePresets.cs), [StylizedEyeBuilder.cs](Assets/Game/Editor/Agents/StylizedEyeBuilder.cs), [EyelidWiring.cs](Assets/Game/Editor/Agents/EyelidWiring.cs), [StylizedEye.shader](Assets/Game/Art/Shaders/Characters/StylizedEye.shader) + [.hlsl](Assets/Game/Art/Shaders/Characters/StylizedEye.hlsl), [EyeBlink.cs](Assets/Game/Scripts/Presentation/Appearance/EyeBlink.cs), [BlinkRhythm.cs](Assets/Game/Scripts/Presentation/Appearance/BlinkRhythm.cs), `Assets/Game/ScriptableObjects/Eyes/` (created on first seed), [Textures/Characters/Eyes/](Assets/Game/Art/Textures/Characters/Eyes), [Models/Characters/EyeMeshes/](Assets/Game/Art/Models/Characters/EyeMeshes), `Materials/Characters/Eye_*.mat`. Consumed by [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs).
**Related:** [ArtPipeline.md](ArtPipeline.md) · [AgentSystem.md](AgentSystem.md) · [PlayerCharacter.md](PlayerCharacter.md) (owns the rest of `Presentation/Appearance/`)

## Model

- **An eye is an asset you tune, not a table you edit.** `EyeStyle` is a ScriptableObject — *Create ▸ SpaceGame ▸ Art ▸ Eye Style*, or duplicate one of the eight seeded in `Assets/Game/ScriptableObjects/Eyes/`. Its Inspector draws the eyeball live above the fields and bakes on a button. Styles are found project-wide by type, so one can be filed wherever it belongs.
- **Pupil, iris and every catchlight are the same type, `EyeShape`** — `Size`, `Aspect`, `Roundness`, `Rotation`, `OffsetUp`, `OffsetSide`. The outline is a superellipse (`|x/side|^n + |y/up|^n = 1`), which is how one type covers a round pupil, a cat's slit (`Aspect` 0.2), a goat's bar (`Aspect` 2.6), a diamond (`Roundness` 1) and a rounded square (`Roundness` 6) without a branch for each. `Size` 0 switches a shape off — that is how `Blank` has no pupil.
- **Everything is measured in degrees of eyeball, in the eye plane** — an azimuthal-equidistant projection about the gaze, so distance from the centre *is* the angle. A shape therefore arrives as the shape it was drawn as, whatever the texture's stretching does. A radius in UV would not: `u` covers 360° over the span `v` covers 180°, so a UV circle lands as an ellipse.
- **The gaze sits at the middle of the map, uv(0.5, 0.5).** That parks the wrap seam at u=0/1, which is the back of the eyeball, inside the head.
- **The pupil is deliberately huge** — 25° of a 45° iris on the seeded styles, leaving the colour as a ring around it. That proportion is what makes these read as cartoon eyes rather than as an eyeball with a dot on it; it is not a mis-set default. `Ivory` pulls back from it (22° of 38°) because it is the human one.
- **Eight styles, and the assets are the truth.** The live look (2026-09-23) puts the colour in the **sclera** — a bright coloured ball, a huge dark pupil (35.9° of a 47.1° iris) leaving the iris as a thin rim, both nudged up 5°, hot white emission on that rim. `Ivory` is the same shape in bone-white with no glow; `Blank` keeps its missing pupil. **`EyeStylePresets` still seeds the OLDER look** — bright iris on a near-black ball — because it only ever creates assets that are absent and never overwrites one. Delete an asset and re-seed and you get the old look back, not this one.
- **A character claims a style by the asset's NAME**, through `SculptCharacterBuilder.SculptRecipe.EyeStyle`: Human `Ivory`, Alien `Amber`, Crumpy `Ember`, Gary `Acid`, Raxy `Tangerine` (Amber duplicated into a truer orange ball with a pure-black 40° pupil in a 47° iris).
- **Colour cannot be changed in the Inspector at runtime** — baking puts it in the pixels. The texture and the `.mat` are rewritten at the same paths, so every reference survives a re-bake.
- **Every eye material is on `SpaceGame/Characters/StylizedEye`**, which lights the map as URP/Lit did (Lit's keyword set for a dynamic opaque; same property names, so the old URP/Lit `.mat`s moved over on one re-bake with byte-identical textures) and paints **two eyelids over the ball**. The lids are paint, not geometry: the eyes bulge out of the head, so the visible ball is the only place a lid is ever seen.
- **A lid hinges on the eye map's own horizontal axis.** Each point's angle about it (0 at the pupil, ±180° behind) comes off the UV, so the lid follows the pupil wherever the eye is turned. The upper lid covers everything above `_LidEdges.x`, the lower everything below `.y`; a dark band `.z` wide on the lid side of each edge (`.w` darker) is what makes a shut eye read as shut.
- **At rest both lids sit at ±180°, tucked behind the ball**, so an open eye looks exactly as it did before lids existed. A blink lerps both edges to `lidsMeet` (−15°: the upper lid does most of the travel). Lower `upperLidRest` for a heavy-lidded look.
- **The lid colour is per character, the material per style**, so `EyeBlink` writes the lids through a property block per eye. `EyelidWiring` samples the colour: the per-channel median of the skin texels under every body vertex 1–2 eye radii from an eye (88–225 per eye), and the skin material's `_Smoothness`. Drifters: Human `#D3B98D`, Alien `#FF7E41`, Crumpy `#78E697`, Gary `#D8BD94`, all 0.15.
- **Blink timing** (`BlinkRhythm`): a wait drawn from 1–3 s (~30 blinks a minute; raised from 2.5–6 s at the user's request), 0.08 s ease-out close, 0.05 s shut, 0.15 s ease-in open, 25% chance of a double blink. The values live on each prefab's `EyeBlink`, so changing the class defaults alone changes no existing character. The first wait starts at a random point in it, so a band spawned together does not blink together. **A dead character's eyes are shut** until `OnRevive`.

## Key types

| Type | File | Role |
|---|---|---|
| `EyeStyle` | [EyeStyle.cs](Assets/Game/Editor/Agents/EyeStyle.cs) | The asset: colours, the iris and pupil shapes, the catchlight list, edge softness, emission, smoothness |
| `EyeShape` | same | One superellipse in the eye plane. Used by the pupil, the iris and every catchlight |
| `EyeCatchlight` | same | A painted glint — colour, strength, and its own `EyeShape` |
| `EyeEmissionArea` | same | Which part the emission map lights: `Iris`, `IrisAndPupil`, `WholeEye` |
| `EyeStyleEditor` | [EyeStyleEditor.cs](Assets/Game/Editor/Agents/EyeStyleEditor.cs) | Live preview, the aperture guide, *Bake This Eye* / *Bake All Eyes* |
| `EyeStylePresets` | [EyeStylePresets.cs](Assets/Game/Editor/Agents/EyeStylePresets.cs) | Seeds the eight built-ins. `CreateMissing` only ever adds |
| `StylizedEyeBuilder` | [StylizedEyeBuilder.cs](Assets/Game/Editor/Agents/StylizedEyeBuilder.cs) | `Shade`, the bake, the materials, `EnsureEyeMesh`, `WearsEyeShader` (how an eye is recognised). Menu *Tools ▸ SpaceGame ▸ Art ▸ …* |
| `StylizedEye` shader | [StylizedEye.shader](Assets/Game/Art/Shaders/Characters/StylizedEye.shader), [.hlsl](Assets/Game/Art/Shaders/Characters/StylizedEye.hlsl) | Forward, ShadowCaster, DepthOnly, DepthNormals. `EyelidCoverage(uv)` is the lid math; `_Lid*` are `[HideInInspector]`, written per renderer |
| `EyeBlink` | [EyeBlink.cs](Assets/Game/Scripts/Presentation/Appearance/EyeBlink.cs) | On the character root. Eye list, lid colour/sheen, rest and meet angles, lash; drives the property blocks; shuts on death. Enabled = blinking |
| `BlinkRhythm` | [BlinkRhythm.cs](Assets/Game/Scripts/Presentation/Appearance/BlinkRhythm.cs) | `[Serializable]` timing tunables + clock. `Advance(dt, random)` → 0 open … 1 shut |
| `EyelidWiring` | [EyelidWiring.cs](Assets/Game/Editor/Agents/EyelidWiring.cs) | `Ensure(root)`: adds `EyeBlink`, points it at every renderer on the eye shader, samples the lids from the skin. Also *Find Eyes And Match Skin* on the component's menu |

## Flows

1. **Author.** Duplicate a style or create one, tune it against the live preview. The preview calls the same `StylizedEyeBuilder.Shade` the bake does, so there is no second description of an eye that could drift.
2. **Bake.** *Bake This Eye*, or *Tools ▸ SpaceGame ▸ Art ▸ Build Stylized Eye Materials* for all of them → `Bake` paints a 1024×512 map (2×2 supersampled), `WriteTexture` writes the PNG and sets its importer (sRGB, no alpha, Repeat), and `Eye_<name>.mat` is put on the eye shader (an existing one is moved over) and gets `_BaseMap`, the style's `Smoothness`, and for a glowing style `_EMISSION` plus an iris-only emission map. No shader → error, nothing baked.
3. **Assign.** *Build Drifter NPCs* → `ApplySkin` gives a slot the eye material when the material the FBX arrived with ends in `_eyes`, the body material otherwise, and **errors** if no slot matched.
4. **Unwrap.** For each eye renderer, `UnwrapEye` measures the gaze as `eyeTransform.InverseTransformDirection(modelRoot.forward)`, checks the eye sits in front of the root, and `EnsureEyeMesh` writes `EyeMeshes/<Character>_<Object>.asset` with a fresh equirectangular unwrap about it.
5. **Wire the lids.** `SculptCharacterBuilder.ApplyBehaviour` → `EyelidWiring.Ensure(root)` on a new build AND on *Update Drifter Behaviour* — the half that reaches existing prefabs, because it touches no eye transform or material. It owns `eyes`, `lidColour`, `lidSmoothness`; *Verify Drifter NPCs* fails a drifter whose `EyeBlink` has no eye on the eye shader.
6. **Blink.** `EyeBlink.Update` → `BlinkRhythm.Advance` → `Draw` only when the closure changed, i.e. only during a blink. `HealthComponent.OnDeath` → shut; `OnRevive` → restart the clock, open. Disabled → back to rest.

## Multiplayer

Materials and meshes are prefab art, identical on every machine; nothing here is spawned, owned or sent. `EyeStyle` is editor-only and never enters a build.

**Blinking is local presentation.** Every machine runs its own `EyeBlink` on its own seeded clock, so two players see one drifter blink at different moments; nothing is replicated and nothing needs to be. Death is the one shared state, and it arrives on a client as `HealthComponent.RestoreHealth` → `OnDeath` with `IsRestoring` true. `EyeBlink` shuts on every `OnDeath`, restores included — **do not add the `IsRestoring` guard** that loot and death sounds need, or clients' corpses keep blinking.

## Persistence

None of its own. An eye's colour and its lid settings are prefab data; the blink clock is transient and restarts on load. A dead body reloads shut because the restore fires `OnDeath` (above).

## Gotchas

- **A sphere with a clean-looking UV grid can still be unusable, and only a render says so.** The three drifters' eyes ship UVs whose `v` is a correct equirectangular latitude and whose **`u` is folded**: one longitude on the ball carries up to FOUR different `u`, in mirrored pairs. Plot the islands and it looks like a textbook 32×16 sphere grid; measure `longitude → u` and a single meridian answers `{0.0625, 0.4375, 0.5625, 0.9375}`. Painted, one pupil comes out as two or four facing different ways, and Unity reports nothing — the mesh imports clean and the material binds clean. `EnsureEyeMesh` is the fix, and it writes a **mesh asset** because a prefab cannot hold a mesh that only exists in memory.
- **Do not write the eye's forward down as an axis.** The importer's axis conversion decides what the eye's local space is; the authoring `.blend` (characters face +Y, eye objects unrotated) says nothing about what Unity ends up with. The gaze is measured from the model root and then checked against the geometry, which is a different source. A gaze wrong by 90° puts both pupils in the side of the head and nothing complains.
- **The asset's NAME is the wiring.** `Eye_<name>.mat`, `Eye_<name>_BaseColor.png` and `SculptRecipe.EyeStyle` are all keyed to it. Renaming a style asset orphans every character claiming it and leaves the old material behind still in use — rename the recipe in the same change, or duplicate rather than rename.
- **Most of the eyeball is inside the head.** The lids leave roughly the middle 50° showing, so a shape judged against the whole sphere comes out far bigger on the face than it looked. That is what the preview's aperture ring is for; it is a viewing guide and is never baked.
- **An iris that is a bright albedo AND an HDR emitter is a white blob.** The first pass shipped `_EmissionColor` at 1.3–1.8 and every style looked like the same pale pink under a key light. They sit at 0.3–0.6 now, and `Smoothness` at 0.55 rather than 0.75 — the catchlight is already painted in, and a glossier ball lays a second one on top of it.
- **A white iris on a white sclera is an empty hoop.** `Ivory` started with both white and read as a dark ring with nothing in it. Its iris is a washed grey-blue now. `Blank` gets away with white on white only because nothing there is meant to have shape.
- **Softness is scaled by a shape's NARROW half-width.** A thin slit's long sides and its ends soften by the same amount; scaling by the size alone smeared a 0.2-aspect pupil away entirely.
- **Blender's EEVEE lies about a textured ball; Cycles does not.** The same eye sphere and material rendered as unreadable checkered patches in EEVEE and correctly in Cycles. Verify a texture-on-geometry result in Cycles before concluding the UVs or the map are wrong.
- **`MaterialPropertyBlock.SetColor` linearises by itself** (Unity 6000.3, linear project: 0.5 is stored as 0.214). The lids first shipped passing `lidColour.linear`, converted twice, and every lid came out a darker, oversaturated cousin of its face — Human brown on cream, Alien red on orange — with nothing logged. Hand it the gamma colour. `PaletteRecolor`'s comment claims the opposite; see [DEFECTS.md](../DEFECTS.md). `EyeBlinkTests` pins the raw uploaded vector.
- **Skinned vertices in world space: `BakeMesh(m, useScale: true)` then `TransformPoint`.** On the drifters (model scale 1.44) `useScale: false` + `TransformPoint` came out 1.4× too big and `useScale: true` + position-and-rotation 1.4× too small; both found no skin near either socket.
- **A lid edge at ±180° lies on the `atan2` wrap.** Drawn with a soft step it paints a hairline of skin down the back of the eye, so the shader switches a lid at `LID_RETRACTED` (3.14) off outright. The edge softness is measured on the continuous (gaze, up) pair, not on the angle, and capped, because at the eye's side poles every edge meets and the angle stops meaning anything.
- **A flat-shaded eye sphere draws its pupil as a grid of facets.** The key light picks out every one of the sphere's 512 faces across the dark pupil. The sculpt-base spheres all ship flat (`use_smooth` off on every face; Gary's `EyeMeshes/` asset has every triangle flat); Raxy's were smooth-shaded in its `.blend`. The fix is in Blender, not Unity: `EnsureEyeMesh` copies the source mesh and only replaces its UVs, so it keeps whatever normals it was handed. It also reads the renderer's CURRENT mesh, which on a built prefab is the previous `EyeMeshes/` asset, so after a re-export point the renderer back at the FBX's sphere before re-unwrapping.
- **After `EnsureEyeMesh` overwrites a mesh asset that is already loaded, the running Editor keeps DRAWING the old one.** It writes in place with `CopySerialized` to keep the GUID. The file on disk and `mesh.normals` are both correct, but renders in the same session show the previous mesh. `UploadMeshData` and a forced reimport do not refresh it; `Resources.UnloadAsset` on the mesh, or an Editor restart, does. The tell: an `Object.Instantiate` copy of the same asset renders correctly while the asset itself does not. Don't chase that render; it cost an hour of "the normals are smooth and it still looks flat".
- **The hinge is the eyeball's, so a hand-rolled eye closes at a slant.** Human, Alien and Crumpy measured 0° of roll; Gary's hand-rotated eyes are ±6–8°, which reads as a slight slant. Re-roll the eye, not the lid.
- **Do not fit the rest angles to the socket from body vertices.** Tried: the skin near an eye is not a clean ring (Human's "rim" came out at 5°, Alien's had no lower edge), which is why rest is a tunable parked out of sight at ±180°.
- **Update Drifter Behaviour re-samples the lid colour** and overwrites a hand-tuned `lidColour` on a drifter; rest angles, lash and timing are left alone. Repaint the skin instead.
- **`EyeBlink` draws nothing in edit mode** — it is not `[ExecuteAlways]`, so its property blocks exist only in play mode, where `OnValidate` tunes them live. An eye whose material is not on the eye shader logs an error in `Awake`, because the shader silently ignores `_Lid*` it does not declare.
- **A menu item run over unity-mcp will happily execute the previous build of your script.** Two rebuilds in a row wrote the old emission numbers with no error of any kind. Execute `Assets/Refresh`, poll `GetState` until `IsCompiling` has gone true and back to false, and then **read the written asset back** instead of trusting the "executed" result.

## Extending

1. **A new eye:** duplicate the closest style asset, rename it, tune, *Bake This Eye*. Point a character at it by name.
2. **A non-round pupil:** `PupilShape.Aspect` below 1 for a vertical slit and above 1 for a horizontal bar; `Roundness` 1 for a diamond, 6+ for a rounded square, below 1 to pinch the ends into a lens; `Rotation` to tilt it; `OffsetUp`/`OffsetSide` to throw the glance. The iris takes all the same controls.
3. **More or fewer glints:** the `Catchlights` list, drawn in order so a later one covers an earlier one. Each carries its own `EyeShape`, so a glint can be a streak rather than a dot. An empty list gives a dead, matte eye.
4. **Giving another character these eyes:** its eye mesh must be a sphere (`EnsureEyeMesh` refuses anything more than 20% off one and says so), its eye slot must be findable, and its builder must call `Load(style)` and `EnsureEyeMesh` the way `SculptCharacterBuilder.ApplySkin` does.
5. **Making it blink:** add `EyeBlink` to its root and pick *Find Eyes And Match Skin* from the component's ⋮ menu, or call `EyelidWiring.Ensure(root)` from its builder (a hand-added component is invisible to the next rebuild). Needs its eyes on `Eye_*` materials and a PNG/JPG skin texture.
6. **Expression through the lids** — narrowed while `AggressionTelegraphModule` warns, sleepy at night — is `upperLidRest`/`lowerLidRest` driven at runtime; not built. Keep it local, like the blink.
7. **Eyes that track a target** would need the eye transform rotated at runtime, not a new map: the pupil is at the middle of the texture, so turning the eyeball turns the gaze — and the lids, which hinge on the eyeball, turn with it.
