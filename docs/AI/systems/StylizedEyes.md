---
system: StylizedEyes
layer: pipeline
summary: "Character eyes: eight looks baked as equirect maps, plus the re-unwrap that lets a sphere wear one"
paths:
  - Assets/Game/Editor/Agents/StylizedEyeBuilder.cs
  - Assets/Game/Art/Textures/Characters/Eyes
  - Assets/Game/Art/Models/Characters/EyeMeshes
symptoms:
  - "a character's eyes are the same colour as its skin, two bare beads in the sockets"
  - "a painted eye texture comes out as two or four pupils, mirrored, on one eyeball"
  - "the pupil ends up in the side of the head instead of facing forward"
  - "an eye texture renders as scrambled checkered patches in Blender but the UV grid looks fine"
  - "every eye colour variant looks like the same pale white blob in game"
  - "a builder run over unity-mcp reports success and writes the values from the previous version of the script"
reads_with: [ArtPipeline, AgentSystem]
updated: 2026-09-22
---

# Stylized Eyes

Eight eye looks, baked into equirectangular albedo (and, for seven of them, emission) maps and wrapped in URP/Lit materials, plus the mesh repair that lets a character's eye sphere wear one.

**Scope:** [StylizedEyeBuilder.cs](Assets/Game/Editor/Agents/StylizedEyeBuilder.cs), [Textures/Characters/Eyes/](Assets/Game/Art/Textures/Characters/Eyes), [Models/Characters/EyeMeshes/](Assets/Game/Art/Models/Characters/EyeMeshes), `Materials/Characters/Eye_*.mat`. Consumed by [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs).
**Related:** [ArtPipeline.md](ArtPipeline.md) · [AgentSystem.md](AgentSystem.md)

## Model

- **The whole look is in the albedo.** The eyes on the sculpt-base characters are plain spheres, so no eye shader and no second UV set: pupil, iris gradient, limbal ring and two painted catchlights are pixels. One set of materials serves every character with spherical eyes; giving a character different eyes is pointing its eye slots at a different `.mat`.
- **The pupil is deliberately huge** — 25° of a 45° iris, leaving the colour as a ring around it. That proportion is what makes these read as cartoon eyes rather than as an eyeball with a dot on it; it is not a mis-set default. `Ivory` is the one that pulls back from it (22° of 38°), because it is the human one.
- **Every shape is a half-angle from the gaze, in degrees** — never a radius in UV. `u` covers 360° over the span `v` covers 180°, so a circle drawn in UV space arrives on the sphere as an ellipse. `Shade` works in angles and `Direction`/`Unwrap` convert.
- **The gaze sits at the middle of the map, uv(0.5, 0.5).** That parks the wrap seam at u=0/1, which is the back of the eyeball, inside the head.
- **Eight styles, a table in the builder.** `Amber Ember Acid Glacier Violet Gold` are bright irises on a near-black eyeball (the alien read: all iris, no white). `Ivory` is a pale eye with a white sclera and a dark limbal ring. `Blank` is the pupil-less white one — iris, pupil and sclera all the same near-white, shape carried by the rim and the catchlight alone.
- **Colour cannot be changed in the Inspector** — it is in the pixels. Edit the style table and re-run; the texture and the `.mat` are rewritten at the same paths so every reference survives.
- **The drifters claim a style by name** through `SculptCharacterBuilder.SculptRecipe.EyeStyle`: Human `Ivory`, Alien `Amber`, Crumpy `Ember`.

## Key types

| Type | File | Role |
|---|---|---|
| `StylizedEyeBuilder` | [StylizedEyeBuilder.cs](Assets/Game/Editor/Agents/StylizedEyeBuilder.cs) | *Tools ▸ SpaceGame ▸ Art ▸ Build Stylized Eye Materials*. Owns the style table, the bake and the materials |
| `EyeStyle` | same | One look: sclera, iris inner/outer, limbal ring, pupil, highlight, their angles, and the emission colour and strength |
| `EnsureEyeMesh` | same | Rewrites one eye sphere's UVs and saves the result as a mesh asset |
| `SculptCharacterBuilder.ApplySkin` / `UnwrapEye` | [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs) | Splits body from eye slots, measures the gaze, calls both halves |

## Flows

1. **Bake.** *Build Stylized Eye Materials* → per style, `Bake` paints a 1024×512 map (2×2 supersampled, smoothstepped edges), `WriteTexture` writes the PNG and sets its importer (sRGB, no alpha, Repeat), `Build` wires `Eye_<Style>.mat` — `_BaseMap`, `_Smoothness` 0.55, and for a glowing style `_EMISSION` + an emission map covering the iris alone.
2. **Assign.** *Build Drifter NPCs* → `ApplySkin` walks every renderer, gives a slot the eye material when the material the FBX arrived with ends in `_eyes` and the body material otherwise, and **errors** if no slot matched.
3. **Unwrap.** For each eye renderer, `UnwrapEye` measures the gaze as `eyeTransform.InverseTransformDirection(modelRoot.forward)`, checks the eye really sits in front of the root, and `EnsureEyeMesh` writes `EyeMeshes/<Character>_<Object>.asset` with a fresh equirectangular unwrap about it.

## Multiplayer

None. Materials and meshes are prefab art, identical on every machine; nothing here is spawned, owned or sent.

## Persistence

None — no runtime state exists. An eye's colour is a property of its prefab, so it reloads because the prefab does.

## Gotchas

- **A sphere with a clean-looking UV grid can still be unusable, and only a render says so.** The three drifters' eyes ship UVs whose `v` is a correct equirectangular latitude and whose **`u` is folded**: one longitude on the ball carries up to FOUR different `u`, in mirrored pairs. Plot the islands and it looks like a textbook 32×16 sphere grid; measure `longitude → u` and a single meridian answers `{0.0625, 0.4375, 0.5625, 0.9375}`. Painted, one pupil comes out as two or four facing different ways, and Unity reports nothing — the mesh imports clean and the material binds clean. `EnsureEyeMesh` is the fix, and it writes a **mesh asset** because a prefab cannot hold a mesh that only exists in memory.
- **Do not write the eye's forward down as an axis.** The importer's axis conversion decides what the eye's local space is; the authoring `.blend` (characters face +Y, eye objects unrotated) says nothing about what Unity ends up with. The gaze is measured from the model root and then checked against the geometry, which is a different source. A gaze wrong by 90° puts both pupils in the side of the head and nothing complains.
- **An iris that is a bright albedo AND an HDR emitter is a white blob.** The first pass shipped `_EmissionColor` at 1.3–1.8 and every style looked like the same pale pink under a key light. They sit at 0.3–0.6 now, and `_Smoothness` came down from 0.75 to 0.55 — the catchlight is already painted in, and a glossier ball lays a second one on top of it.
- **A white iris on a white sclera is an empty hoop.** `Ivory` started with both white and read as a dark ring with nothing in it. Its iris is a washed grey-blue now. `Blank` gets away with white on white only because nothing there is meant to have shape.
- **Blender's EEVEE lies about a textured ball; Cycles does not.** The same eye sphere and material rendered as unreadable checkered patches in EEVEE and correctly in Cycles. Verify a texture-on-geometry result in Cycles before concluding the UVs or the map are wrong.
- **A menu item run over unity-mcp will happily execute the previous build of your script.** Two rebuilds in a row wrote the old emission numbers with no error of any kind. Execute `Assets/Refresh`, poll `GetState` until `IsCompiling` has gone true and back to false, and then **read the written asset back** instead of trusting the "executed" result.

## Extending

1. **A new colour:** add an entry to `BuildStyles` — `Glowing(name, irisInner, irisOuter, limbalRing, glow, strength)` for a bright iris on a dark eyeball, or `Base(name)` plus overrides for anything else — and re-run the menu item. Keep `EmissionStrength` under about 0.6.
2. **A different shape of eye:** the angles live on `EyeStyle` (`PupilAngle`, `IrisAngle`, `LimbalWidth`, `EdgeSoftness`) and the catchlights on `HighlightAngle`/`HighlightAzimuth`/`HighlightRadius` plus the smaller `Spark*`. Azimuth 0 is straight up; both eyes share the map and share an orientation, so one value puts the highlight in the same place on both.
3. **Giving another character these eyes:** its eye mesh must be a sphere (`EnsureEyeMesh` refuses anything more than 20% off one and says so), its eye slot must be findable, and its builder must call `Load(style)` and `EnsureEyeMesh` the way `SculptCharacterBuilder.ApplySkin` does.
4. **Eyes that track a target** would need the eye transform rotated at runtime, not a new map: the pupil is at the middle of the texture, so turning the eyeball turns the gaze.
