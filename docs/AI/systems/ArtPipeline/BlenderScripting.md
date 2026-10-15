---
system: BlenderScripting
status: implemented
layer: presentation
summary: "Traps between Blender and Unity: FBX import, headless or MCP scripting, placing models from editor scripts"
updated: 2026-10-15
---

# Blender to Unity gotchas

Read [../ArtPipeline.md](../ArtPipeline.md) first.

## Gotchas

- **Metallic does not survive the FBX.** Every palette metal arrives in Unity as URP Lit with `_Metallic` **0** and a smoothness the importer derived itself (`Mat_Metal_Gold_Leaf`, metallic 1 / roughness 0.26 in Blender, imports at metallic 0 / smoothness 0.74). A surface that has to read as real metal or a mirror needs a hand-authored `.mat` assigned on the prefab's renderer — the sun shrine's panes use `Materials/Environment/SunMirrorSilver.mat` ([SunShrine.md](SunShrine.md)).
- **A single-mesh FBX carries its axis conversion on the ROOT, and a script that sets the rotation erases it.** Unity collapses an FBX whose only node is one mesh, so the model prefab's root transform itself holds the `(-90, 0, 0)` rotation and scale 100. `PrefabUtility.InstantiatePrefab(...)` then `transform.rotation = yaw` overwrites that rotation, and the model lies on its side. This happened to every single-collection `export_collections` FBX (all `sh_*` dwellings and props). An FBX with an armature keeps an unrotated root and stands up fine, which hides the pattern. **Compose, never assign:** `rotation = yaw * prefab.transform.localRotation`.
- **In a Blender script, `mesh.materials.clear()` zeroes every face's `material_index`.** Rebuilding a mesh's slots (for example, dropping unused ones before a contact-sheet export) and writing the remapped indices *before* the clear leaves the whole mesh on slot 0, one flat colour. Read the indices out first, rebuild the slots, then `polygons.foreach_set("material_index", ...)`.
- **A Blender older than the file's writer warns "expect loss of data" — never save the library file from it.** `raxy.blend` is written by 5.1.30; a 5.1.0 session opened it with that warning on 2026-10-07, so the witch's garments went into a new `morwen.blend` instead of being saved over the irreplaceable original. Check `bpy.app.version_string` against the warning before any save.
- **Driven remotely (MCP), `export_collections` fails: `'Context' object has no attribute 'selected_objects'`.** It re-opens the file, which leaves the remote session with no window context, and the FBX exporter reads `context.selected_objects`. Open the file in one call, then run its body (`_unparent_meshes`, `_localise_materials`, per collection: offset, select, `_write_fbx(use_selection=True)`) inside `bpy.context.temp_override(window=…, area=…, screen=…)` taken from `window_manager.windows[0]`. Never save the file afterwards — the export localised its materials in memory.
- **An unwelded fan bakes as streaks.** A disc built as a lathe from a radius-0 ring has every centre vertex duplicated, so each triangle is its own UV island; a baked atlas then shows a seam along every spoke (the witch hut's floor). Merge by distance before unwrapping. A boolean cut on a mesh UV'd afterwards needs no care — `smart_project` re-islands it.
- **Blender 5.1 also opens the library.** `palette.blend` was last saved by 5.1.29, and the sprite hamlet files were built with `C:/Program Files/Blender Foundation/Blender 5.1/blender.exe`. The portable 5.2.1 named above is not on every machine.
