# Repository Restructure — Design

**Date:** 2026-08-12
**Branch:** `Feat/robotics-and-minigame`
**Checkpoint:** `939bb01 chore: cleanup` (clean tree before any move)

## Goal

A clear, recurring, scalable hierarchy across the whole repository, with consistent
naming and no behaviour change. Every top level is a large category; detail lives in
subcategories. Dead weight is deleted on evidence, not on suspicion.

## Non-goals

- No gameplay, tuning, or asset *content* changes.
- No API redesign. Type names, fields, and serialized data keep their meaning.
- No change to third-party package internals.

## Constraints

These are load-bearing and must not be violated:

| Constraint | Reason |
|---|---|
| `Assets/Plugins/FMOD` keeps its literal path | FMOD resolves native libs and asmdefs by that path |
| `Assets/StreamingAssets` stays at the Assets root | Unity magic folder |
| A `Resources/Items` path must survive | the one string load in the codebase is `Resources.Load("Items")` |
| Every asset moves together with its `.meta` | the `.meta` holds the GUID; separating them detaches every reference |
| Unity Editor is open during the work | moves land as live reimports; a final full reimport is required |

## Deletions

Evidence-backed. Total reclaimed ≈ 259 MB.

| Item | Size | Evidence |
|---|---|---|
| `Assets/_Recovery/` | 253 MB | 2148 crash-recovered `0 (N).unity` autosaves; already gitignored; 8 stale tracked files |
| `Assets/Imported/Generic Aircraft Models` | 2.7 MB | 111 GUIDs, 0 external references, 0 `.cs` |
| `Assets/Imported/SpaceFighter` | 2.0 MB | 22 GUIDs; sole referrer was `_Recovery`, also deleted; 0 `.cs` |
| `Assets/Imported/VertexModeler` | 928 KB | 24 GUIDs, 0 external references, 0 `.cs` |
| `mono_crash.*.json` ×4, `fmod_editor.log`, `.DS_Store` ×3 | 645 KB | already matched by `.gitignore` |
| `skills/`, `__pycache__`, 7 empty Unity dirs | — | empty |

Reference evidence comes from a GUID scan: every GUID owned by a pack, searched across
all scenes, prefabs, materials, ScriptableObjects, controllers and ProjectSettings
outside `Imported/`. None of the three removed packs contains a script, so their removal
cannot break compilation.

**Explicitly kept:** `backups/*.blend` (the hand-modelled ostrich has no other backup),
`LightRays2D` (has scripts, is referenced), and the seven third-party packs confirmed in use
(`FirstGearGames`, `Kevin Iglesias`, `Sci-Fi RTS pack`, `Bruhassets`, `Same Gev Dudios`,
`Same Gev Dudios 1`, `Cosmic_Retro_Blasters`, `TextMesh Pro`).

## Target hierarchy

### `Assets/`

```
Art/            Animations · Materials · Models · Shaders · Sprites · Textures
Audio/          (from "FMOD Banks")
Prefabs/        Agents · Characters · Environment · Items · UI · VisualEffects
                · Vehicles · Cutscenes · Camera · Multiplayer · Legacy
Scenes/         Core · World · Interiors · Minigames · Menus · Tests · Utility
Scripts/        Agents · Characters · Creatures · Locomotion · Vehicles · Weapons
                · Items · World · Gameplay · Presentation · Core · Editor
ScriptableObjects/ · Resources/ · Settings/ · Terrain/ · Tests/ · Editor/
ThirdParty/     (from Imported/ + LightRays2D)
Plugins/        UNCHANGED
StreamingAssets/ UNCHANGED
```

### Naming corrections

`agents`→`Agents`, `items`+`Item`→`Items`, `settlements`→`Settlement`,
`structures`→`Structures`, `dialoge`→`Dialogue`, `Visual Effects`→`VisualEffects`,
`Menu scenes`→`Menus`, `Test scenes`→`Tests`, `Utility scenes`→`Utility`,
`world`→`World`, `entities - legacy(still works)`→`Legacy`,
`BallLigtningController`→deleted (empty).

The nine loose files at the `Assets/` root (`ship.fbx`, four `.mat`, `MountableAnt.prefab`,
`RoverNoHirarchy.prefab`, `BallLightningWeapon.asset`, `DefaultNetworkPrefabs.asset`) are
homed into the categories above.

### Repository root

```
docs/     architecture/ (7 root .md + the .puml) · superpowers/{specs,plans}
Tools/    Blender/ (ostrich_build.py)
Archive/  backups/ + AssetSources/ blend snapshots
```

`docs/superpowers/` keeps its path because the brainstorming skill writes there by convention.

## Namespaces

Today only 44 of 487 script files declare a namespace (`SpaceGame.Locomotion`,
`SpaceGame.Vehicles.*`, `SpaceGame.EditorTools`). The remaining 443 are global.

The convention becomes `SpaceGame.<Domain>[.<Subdomain>]`, matching the folder path.

### The one silent-breakage vector

Unity links MonoBehaviours to scenes by GUID, which survives a namespace change.
**UnityEvent bindings do not** — they serialize the target type as a string
(`m_TargetAssemblyTypeName`). Five classes are bound this way:

```
DeathScreenUI · LobbyElementController · LobbyListSystem · LobbySystem · MainMenuUI
```

Each of these must have its `m_TargetAssemblyTypeName` updated in the referencing scene
and prefab files in the same change that introduces its namespace. Missing one produces
no compile error — the main-menu and lobby buttons simply stop responding.

Verified clear: zero `Type.GetType` string lookups, zero string-based `AddComponent`,
one `Resources.Load` path (`"Items"`).

## Verification

1. `.meta` parity — every asset has its `.meta`, no `.meta` is orphaned.
2. GUID stability — the set of GUIDs before and after is unchanged.
3. No unresolved GUID references in any scene or prefab.
4. Unity compiles with no errors, and the console is clean after a full reimport.
5. `Bootstrap`, `MainMenu` and `LobbyMenu` open with UI buttons still wired.

## Outcome

Executed on 2026-08-12. Unity compiles with **0 errors and 0 warnings** after a full
domain reload. GUID parity, `.meta` parity and dangling-reference checks all pass.

### The vector this design missed

`m_TargetAssemblyTypeName` was the *second* place a type name is stored as a string.
The one that actually broke was `[SerializeReference]`:

```yaml
type: {class: MesaSettings, ns: , asm: Assembly-CSharp}
```

A normal MonoBehaviour reference resolves through the script GUID and survives a
namespace change. A SerializeReference record does not - it stores class, namespace
and assembly by name, so once the class moved into `SpaceGame.World` the record stopped
resolving and Unity silently dropped the data. It surfaces only as

> A scripted class has a different serialization layout when loading.

57 records across three world chunk scenes (`Chunk_6_5`, `Chunk_7_0`, `Chunk_7_1`) held
terrain feature settings this way: `MesaSettings`, `CliffFeatureSettings`,
`BouldersSettings`, `ButteSettings`, `SpanFeatureSettings`. Rewriting `ns:` restored them.

**Before namespacing anything in this project again, grep for both string-keyed forms:**

```
grep -rn "m_TargetAssemblyTypeName" Assets/{Scenes,Prefabs,Resources}
grep -rn "ns: , asm: Assembly-CSharp" Assets
```

### Other things worth knowing

- **macOS is case-insensitive.** `agents` -> `Agents` needs a temp hop; `os.path.normcase`
  is a no-op on darwin and will not detect a case-only rename.
- **Inferring usings from an identifier scan reads comments too.** Prose naming a type
  pulled in usings for assemblies the asmdef could not see, including the Editor
  assembly, which runtime code can never reference.
- **`using (...)` statements look like using directives.** Splitting a file at the last
  using-shaped line puts the namespace opener inside a method body. Find the end of the
  header instead: the last using directive before any real code.
- **FMOD stores its bank path in `FMODStudioSettings.asset`.** Editing that file under a
  live editor does nothing - Unity holds the object in memory and writes it back. Set
  `FMODUnity.Settings.Instance.SourceBankPath` through the API instead.
- **`WorldStreamingConfig.asset` held 48 chunk scene paths** and
  `EditorBuildSettings.asset` another 249. Both are path-keyed, not GUID-keyed.

## Risks accepted

The user chose the two higher-risk options knowingly: pruning third-party packs, and
namespacing the entire codebase. Both were flagged. The residual risk is that a
reference invisible to static scanning (an editor-only tool, or a pack whose assets are
only used inside its own demo scene) is affected — recoverable from `939bb01`.
