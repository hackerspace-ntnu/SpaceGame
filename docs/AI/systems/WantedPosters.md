---
system: WantedPosters
layer: world
summary: "Wanted posters baked in-editor from any character prefab as screenprints, hung as readable prefabs"
paths:
  - Assets/Game/Scripts/World/Posters/
  - Assets/Game/Editor/Posters/
  - Assets/Game/Scripts/Presentation/UI/Pages/PosterViewerUI.cs
  - Assets/Game/ScriptableObjects/Posters/
  - Assets/Game/Prefabs/Environment/Decorations/Posters/
  - Assets/Game/Art/Textures/Posters/
  - Assets/Game/Art/Fonts/Posters/
  - Assets/Game/Editor/Tests/WantedPosterTests.cs
symptoms:
  - "a baked poster's bandit has no eyes"
  - "the poster texture is square or squashed in the game"
  - "the printed bandit is nearly all dots and black"
  - "a bandit's skin prints in cream or in the poster colour instead of slate blue"
  - "a poster on the wall is blank or shows last bake's picture"
  - "right-clicking a poster does nothing, or the prompt never appears"
  - "[Posters] Font 'X' has no glyph for 'Y'"
  - "[Posters] Wanted_X: FAILED VERIFY"
reads_with: [InteractionSystem, UI, EditorTooling, CharacterClothes, StylizedEyes]
updated: 2026-10-12
---

# Wanted Posters

A wanted poster is a data asset naming a bandit, and an editor bake that renders that bandit's prefab and prints it
as a three-ink screenprint on a crumpled sheet: a banner in a dark shade of the poster's colour with WANTED, a sun backdrop, heavy corner brackets, a DEAD or
ALIVE tag, reward and name. The bake writes a PNG, a material and a placeable prefab; in game the poster hangs on a board
or wall and right-click reads it full-screen. The look was settled with the user over ten rounds of HTML mockups
(sun backdrop, banner title, bold brackets; the banner went from black to `bannerShade` x accent on 2026-10-12); the C# reproduces that design, it does not reinterpret it.

## Model

| Piece | Is |
| --- | --- |
| [`WantedPosterDefinition`](Assets/Game/Scripts/World/Posters/WantedPosterDefinition.cs) | Who and what: `subject` (any character prefab), name, reward, `WantedStatus`, accent colour, optional HT text, framing. `poster` is written by the bake. |
| [`WantedPoster`](Assets/Game/Scripts/World/Posters/WantedPoster.cs) | On the prefab root: `IInteractable` + `IInteractionReadout` ("Wanted: <name>", "RMB: read"), moment `None`. Opens the viewer. |
| [`PosterViewerUI`](Assets/Game/Scripts/Presentation/UI/Pages/PosterViewerUI.cs) | Full-screen overlay holding the sheet; Esc, left or right click puts it down. Sorting order 2030. |
| [`WantedPosterStyle`](Assets/Game/Editor/Posters/WantedPosterStyle.cs) | Every tunable (inks, tone bands, dots, lines, sheet layout in 100x125 sheet units). One asset: `ScriptableObjects/Posters/WantedPosterStyle.asset`. |
| [`WantedPosterBaker`](Assets/Game/Editor/Posters/WantedPosterBaker.cs) | Capture → stylize → layout → write PNG, material, variant prefab → `Verify()`. |

The bake, one stage per file in [Editor/Posters/](Assets/Game/Editor/Posters/):

| Stage | File | Does |
| --- | --- | --- |
| Capture | [PortraitCapture.cs](Assets/Game/Editor/Posters/PortraitCapture.cs) + [PortraitPasses.shader](Assets/Game/Editor/Posters/PortraitPasses.shader) | Preview scene, ortho camera in front (+Z), key + rim light. Three renders: lit, material ids, view normals + depth. Classifies each material into an `InkRole`. |
| Stylize | [PosterStylizer.cs](Assets/Game/Editor/Posters/PosterStylizer.cs) | Flat inks per role, tone cut into highlight / dot band / solid ink, eyes in full colour, outlines on silhouette, material borders, creases, depth steps. Fills offset (`register`) from the lines. |
| Lay out | [PosterLayout.cs](Assets/Game/Editor/Posters/PosterLayout.cs) | Sheet, sun + moon + mesas, portrait, brackets, tag, fields, coffee ring, banner (worn), creases + grain over all. Seeded by the definition's name: re-baking gives the same sheet. |
| Type | [PosterText.cs](Assets/Game/Editor/Posters/PosterText.cs) | Unity's font engine into the font atlas, read back, sized by cap height. Fonts: Bowlby One SC, Black Ops One, Chakra Petch (SIL OFL, licences beside them). |
| Paint | [PosterRaster.cs](Assets/Game/Editor/Posters/PosterRaster.cs), [PosterNoise.cs](Assets/Game/Editor/Posters/PosterNoise.cs) | `PosterMask` / `PosterImage` (row 0 at the top, sRGB, straight alpha), shapes, halftone, seeded noise. |

**Ink roles** (`PortraitCapture.MaterialSlots.Classify`): shader name containing `Eye` → **Eye**; of the materials on body
renderers (`SkinnedMeshRenderer` with no `SkinnedGarment` above it), the one covering most pixels → **Skin** (slate);
every other material whose rendered average is warm (`warmRatio`, `warmSaturation`) → **Accent**; the rest → **Neutral**
(cream with dense dots). Per material, never per pixel: in-game skins are grey (Ash, Slate, Sage), so hue cannot find skin.

## Flows

**Add a poster:** *Create ▸ SpaceGame ▸ Posters ▸ Wanted Poster* in `ScriptableObjects/Posters/`, set `subject` and the
text, then *Tools ▸ SpaceGame ▸ Posters ▸ Bake Selected Wanted Posters*. Drop `Prefabs/…/Posters/<asset name>.prefab`
anywhere; its printed face looks along its **+Z**, pivot at the back centre, 0.72 x 0.9 m.

**Bake** (`WantedPosterBaker.Bake`): capture (~1100x1210) → stylize → compose (1024x1280) → `Textures/Posters/<name>.png`
(sRGB, mips, clamp, `npotScale None`) → `definition.poster` → `Materials/Posters/<name>.mat` (copy of the base prefab's
material, `_BaseMap` set) → `Prefabs/…/Posters/<name>.prefab`, a variant of `WantedPoster.prefab`, created once, then
only its `definition` field and sheet material rewritten → `Verify()` re-reads all of it and logs `FAILED VERIFY` with the reasons.

**Read in game:** RMB on the collider → `WantedPoster.Interact` → `PosterViewerUI.Show(texture)` → `GameplayMenuScope.Enter`
(freezes time solo) → any of Esc / LMB / RMB on a later frame closes → `Exit`.

**Tune the look:** edit `WantedPosterStyle.asset`, then *Export Portrait Passes (Selected)* writes `shade`, `roles` and
`print` PNGs to `Temp/PosterPasses/<name>/` without touching an asset; *Bake Wanted Posters* when it is right.

## Multiplayer

N/A on the wire — a poster is static scenery with no `NetworkObject`, identical on every machine because it ships as a
texture. Reading is local to the presser, like `DialogInteraction`. Do not register poster prefabs in the network list.

## Persistence

N/A — nothing a poster does changes state; reading is transient. A poster hand-placed into a scene or prefab is authored
content and loads with it.

## Gotchas

- **A renderer listed in several LOD levels belongs to LOD 0.** A Raxy's eye spheres are in all three of its LODs; hiding
  "everything in LOD 1+" hid the eyes and the bandit printed eyeless with a clean console. `NearestRenderers` keeps any
  renderer that LOD 0 lists.
- **Unity's default texture import squashes the 4:5 sheet to a square** (`npotScale ToNearest`). The baker sets `None`;
  `Verify()` caught it as "texture is 1024x1024".
- **Preview-scene light is dimmer than Blender's.** At `keyLight` 1.4 the coat fell below `shadow` and printed as all dots;
  2.3 matches the mockups. Tune the light before the tone bands.
- **Id colours go through `SetVector`, not `SetColor`**: the project is Linear, `SetColor` linearises, and the id decoded
  from the linear target would be wrong.
- **The bridge cannot see `internal` editor types.** Capture, stylize and layout are internal; script against
  `WantedPosterBaker.Bake` / `BakeAll` / `ExportPasses`.
- **Fonts must stay dynamic `.ttf`**, and every printed character must exist in its face. `₡` is not in Chakra Petch;
  a missing glyph throws `[Posters] Font 'X' has no glyph`, deliberately.
- **Outlines come from the renders, not the mesh.** Creases only show where normals break between neighbouring pixels
  (hard edges); smooth-shaded folds draw no line. Material borders and silhouettes carry most of the drawing.
- **Nomad settlement prefabs allow `WantedPoster`** in [SettlementInteractableTests](Assets/Game/Scripts/agents/Residents/Editor/Tests/SettlementInteractableTests.cs):
  a sheet flat on a wall cannot stand in front of a door. Do not hang one on a door.
- **Not yet seen in play mode, on a host or a client.** Bake, prefabs, board placement and 7 EditMode tests are verified;
  the right-click → viewer round trip has not been played.

## Extending

1. **Another bandit:** a new definition + *Bake Selected*. Any prefab with renderers works; humanoid or not, it is framed
   from its bounds (`portraitSpan`, `headroom`).
2. **A different look:** change `WantedPosterStyle.asset` and re-bake everything; no code needed for colours, sizes,
   bands, dots, lines or positions. A new sheet element is a method on `PosterLayout.Sheet` plus its style fields.
3. **A board:** nest poster variants on a board prefab at the board face; `Deco_WantedBoard.prefab` hangs the three on
   `Deco_PosterBoard_Empty` (face at z = 0.04).
4. **Posters that change** (CLAIMED stamps, tearing down): that is runtime state — it needs a server-decided latch, a saver
  and a play-mode client test; none of it exists.
