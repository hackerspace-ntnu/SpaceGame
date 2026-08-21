# Asset folder casing

## The rule

**The casing git records is the truth.** If your disk disagrees, your machine is wrong.

## Why this matters

Netcode for GameObjects identifies scenes across the network as `XXHash32(full scene path)`,
and that hash is **case-sensitive**. Unity reports whatever casing your *disk* has — it resolves
the path from the scene GUID, not from the string stored in `EditorBuildSettings.asset`.

So if your checkout has `Assets/Game/Scenes/world/` and a teammate's has
`Assets/Game/Scenes/World/`, the two machines compute different hashes for the same scene and
clients cannot join. The host's log shows:

```
Exception: Scene Hash 3522805719 does not exist in the HashToBuildIndex table!
```

Nothing else in the project misbehaves, which is what makes it so confusing.

## Why `git status` will not warn you

macOS and Windows use case-insensitive filesystems, and this repo has `core.ignorecase = true`.
When someone records a case-only rename, `git pull` does **not** apply it: git writes the file
into the directory that already exists under the old casing, and then reports a clean tree.

A case-only rename therefore never propagates on its own. That is the entire problem.

## What is in place

| Piece | Role |
|---|---|
| `Tools/fix-asset-casing.sh` | Repairs on-disk casing to match git. Refuses to run while Unity is open. |
| `.githooks/post-merge`, `post-checkout`, `post-rewrite` | Run the repair automatically after pull, checkout, and rebase. |
| `Assets/Game/Editor/AssetPipeline/AssetCasingGuard.cs` | On project open: enables the hooks, then reports any drift as a Console error and a dialog. |

The hooks only work once `core.hooksPath` points at `.githooks`. The Unity guard sets that for
you the first time you open the project, which covers the one case hooks cannot: your **first**
pull, before any hook is installed.

## If you get the warning

**Pull first, then run it.** The tool makes your disk match whatever your *index* currently
says. If you run it before pulling, it will faithfully "fix" you to the old casing, and you
will have to run it again afterwards. The hooks get this right by construction, because they
only fire after the pull.

```bash
git pull
# close Unity first — renaming folders under a live Editor forces a mid-session reimport
Tools/fix-asset-casing.sh
```

Check without changing anything (this is also the CI-safe form):

```bash
Tools/fix-asset-casing.sh --check
```

On Windows the tool detects a running Unity via `tasklist`. If it cannot tell — no `pgrep` and
no `tasklist` — it refuses to rename anything and asks you to close Unity and re-run with
`--force`. It will never rename folders underneath a live Editor on a guess.

## When adding folders

Use `PascalCase`, matching the existing siblings (`Core`, `Interiors`, `Minigames`, `Tests`,
`World`). If you need to correct the casing of an existing folder, do it in **one** dedicated
change containing nothing else — a pure rename is reviewable, a rename mixed with edits is not.
