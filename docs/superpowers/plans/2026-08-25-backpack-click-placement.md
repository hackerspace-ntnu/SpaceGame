# Backpack Click Placement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the backpack's drag-and-drop-with-magnet-snap interaction with a click-to-carry
one: click an item to take it into your hand, click a grid spot to put it down, click again on a
refused spot to rotate it 90°.

**Architecture:** The focus-mode state machine (`PackDragController`, 1224 lines) becomes
`PackHandController` and loses its two drag machines — the mouse-button one and the EventSystem
one — for a single hand state with two values: empty or holding. Placement legality goes back to
`PackLayout.CanPlace` asked about the exact cell under the cursor; the magnet search
(`TryFindNearest`) and the server's first-fit stow fallback are both removed, because "no auto
placement" has to be true on both sides of the wire. `PackGridVisual` grows a green/red ghost.
Nothing about the netcode's shape changes: lifting stays purely local, and the three commits
(`RequestMove` / `RequestTake` / `RequestStow`) are the same server round trips they already were.

**Tech Stack:** Unity 6000.3.11f1, C#, Unity Netcode for GameObjects (via this project's
`NetMessaging` / `NetChannel` layer), NUnit EditMode tests.

**Spec:** `docs/superpowers/specs/2026-08-25-backpack-click-placement-design.md`

---

## Before you start

**Check the Editor is not in play mode.** Writing under `Assets/` while the user is playing can
stop play mode and throw them out of their session:

```bash
ls -la Library/EditorInstance.json Temp/UnityLockfile 2>/dev/null
```

If the user is playing, stage the work and wait. This project has no `unity-status.sh`.

**The type-check loop you will use after every code task** (no Editor, ~60 s, works while the
Editor holds the lock). Write it once now and reuse it:

```bash
mkdir -p /private/tmp/claude-501/check && cd /Users/ferdinandfremming/Documents/hackerspace/spillgruppen/SpaceGame
python3 - <<'PY'
import os, re, pathlib
ROOT = pathlib.Path('.')
DAG = ROOT/'Library/Bee/artifacts/200b0aE.dag'
OUT = pathlib.Path('/private/tmp/claude-501/check')

# Directories owned by another assembly: a .cs at or below an .asmdef is NOT Assembly-CSharp.
asmdefs = {p.parent for p in (ROOT/'Assets').rglob('*.asmdef')}

def owned_elsewhere(p):
    return any(str(p).startswith(str(d) + os.sep) or p.parent == d for d in asmdefs)

for name, editor in (('Assembly-CSharp', False), ('Assembly-CSharp-Editor', True)):
    flags = [l.rstrip('\n') for l in (DAG/f'{name}.rsp').read_text().splitlines()
             if not l.startswith('"') and not l.startswith('-refout:')]
    flags = [re.sub(r'^-out:.*', f'-out:"{OUT/(name+".dll")}"', f) for f in flags]
    if editor:
        flags = [f.replace(str(DAG/'Assembly-CSharp.ref.dll'), str(OUT/'Assembly-CSharp.dll'))
                 for f in flags]
    srcs = []
    for p in (ROOT/'Assets').rglob('*.cs'):
        if owned_elsewhere(p):
            continue
        inedit = f'{os.sep}Editor{os.sep}' in str(p) or p.parent.name == 'Editor'
        if inedit == editor:
            srcs.append(f'"{p}"')
    (OUT/f'{name}.rsp').write_text('\n'.join(flags + sorted(srcs)))
    print(name, len(srcs), 'sources')
PY
ED=/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/Resources/Scripting
$ED/NetCoreRuntime/dotnet $ED/DotNetSdkRoslyn/csc.dll "@/private/tmp/claude-501/check/Assembly-CSharp.rsp"
$ED/NetCoreRuntime/dotnet $ED/DotNetSdkRoslyn/csc.dll "@/private/tmp/claude-501/check/Assembly-CSharp-Editor.rsp"
```

Expected on a clean tree: warnings only, no `error CS`. Roughly 628 and 160 sources. **If a
`CS0246` names a type you can `grep` on disk, the source list went stale — re-run the Python
block before believing it.**

**A note on `git commit` in this repo:** a commit-blocking hook false-positives on any shell
command containing `$(...)` or a heredoc. If a commit is refused for that reason, the fix is to
re-issue it as a plain `git commit -m "..."` with no substitution — not to retry the same command.

---

## File Structure

| File | Change | Responsibility after |
| --- | --- | --- |
| `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` | Modify | `PackStow` carries yaw; `PackDrop` (77) retired in place |
| `Assets/Game/Scripts/Items/Backpack/BackpackObject.cs` | Modify | Stow places at the exact spot or refuses; `Holds` added; `TryStowAt` / `CanStow` / `TryDropToWorld` gone |
| `Assets/Game/Scripts/Items/Backpack/BackpackController.cs` | Modify | `RequestStow` packs yaw; drop request and handler gone |
| `Assets/Game/Scripts/Items/Backpack/Placement/PackGridVisual.cs` | Modify | Ghost cells draw green or red |
| `Assets/Game/Scripts/Items/Backpack/Placement/PackLayout.cs` | Modify | `TryFindNearest` (the magnet) removed |
| `Assets/Game/Scripts/Items/Backpack/Focus/PackDragVisuals.cs` | Rename → `PackHandVisuals.cs` | What is drawn while carrying |
| `Assets/Game/Scripts/Items/Backpack/Focus/PackDragController.cs` | Rename → `PackHandController.cs`, then rewrite | The hand: hover, lift, turn, put down |
| `Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs` | Modify | Attaches `PackHandController` |
| `Assets/Game/Scripts/Presentation/UI/HUD/InventoryUI.cs` | Modify | Click bridge to the hand; carried icon; no drag bridge |
| `Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs` | Modify | Left-click only; no EventSystem drag interfaces |
| `Assets/Game/Tests/Editor/PackNearestFitTests.cs` | Delete | — |
| `Assets/Game/Tests/Editor/BackpackStowTests.cs` | Modify | New `TryStowFromHotbar` signature; the no-first-fit rule |
| `Assets/Game/Tests/Editor/PackHandTests.cs` | Create | Yaw wire packing; rotate-on-refusal cycling |
| `Assets/Game/Editor/Tests/BackpackNetworkingTests.cs` | Modify | "drag onto a slot" tests renamed to clicks |

---

## Task 1: The stow places where it was pointed, and carries yaw

The hotbar → pack transfer today ignores yaw (the server always places at 0) and silently
first-fits somewhere else when the aimed spot is taken. Both are the auto-placement the redesign
removes. This task is entirely server-side and testable without any UI.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Backpack/BackpackObject.cs:1433-1492` (`TryStowAt`, `TryStowFromHotbar`), `:1642-1704` (`RequestStow`, `CanStow`)
- Modify: `Assets/Game/Scripts/Items/Backpack/BackpackController.cs:936-1000` (`RequestStow`, `OnStowRequested`)
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs:368-387` (the `PackStow` comment block)
- Test: `Assets/Game/Tests/Editor/BackpackStowTests.cs`

- [ ] **Step 1: Write the failing test — a stow lands at the exact spot, at the yaw asked for**

Add to `Assets/Game/Tests/Editor/BackpackStowTests.cs`, in the `// ------- the stow` section
after `Stow_TakesItOutOfTheHotbarAndPutsItWhereTheCursorWas`:

```csharp
        /// <summary>
        /// The whole point of removing the magnet: a stow goes where it was pointed, at the turn
        /// it was shown at, and nowhere else.
        /// </summary>
        [Test]
        public void Stow_PlacesAtTheYawItWasGiven()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            var spot = new Vector2(0.4f, 0.3f);

            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 90f));

            Assert.IsTrue(TryPlacementOf(pack, carried, out PackPlacement placed));
            Assert.AreEqual(90f, placed.Yaw, "the turn the player lined up is the turn it lands at");
        }

        /// <summary>
        /// The other half of "no auto placement". A spot that is taken is a REFUSAL — the item
        /// stays in the hotbar. It used to fall through to a first-fit search and land somewhere
        /// the player never pointed at.
        /// </summary>
        [Test]
        public void Stow_OntoATakenSpot_RefusesRatherThanFindingRoomElsewhere()
        {
            BackpackObject pack = Pack();
            var hotbar = new Hotbar(4);

            InventoryItem sitting = Item("sitting");
            var spot = new Vector2(0.4f, 0.3f);
            Assert.IsTrue(pack.TryPlace(sitting, PackSurfaceId.Leaf, spot, 0f));

            InventoryItem carried = Item("carried");
            Assert.IsTrue(hotbar.TryAddItem(carried));

            Assert.IsFalse(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f),
                           "the spot is taken, so the stow is refused");

            Assert.IsTrue(IsInSlot(hotbar, 0, carried), "the item must still be in the hotbar");
            Assert.AreEqual(1, pack.Layout.Placements.Count,
                            "and nothing may have been first-fitted onto the pack");
        }
```

Then update the two existing calls in that file, which still pass the old `aimed:` flag:

```csharp
            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot));
```
becomes
```csharp
            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f));
```

and in `Stow_ThatFitsNowhere_LeavesBothTheHotbarAndThePackAlone`:

```csharp
            Assert.IsFalse(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf,
                                                  new Vector2(0.02f, 0.02f), 0f));
```

and in `Stow_ThenTake_PutsItBackInTheHotbar`:

```csharp
            Assert.IsTrue(pack.TryStowFromHotbar(hotbar, 0, PackSurfaceId.Leaf, spot, 0f));
```

- [ ] **Step 2: Run the type-check to verify it fails**

Run the type-check block from "Before you start".
Expected: `error CS1503` / `CS1739` in `BackpackStowTests.cs` — no overload of
`TryStowFromHotbar` takes five arguments.

- [ ] **Step 3: Rewrite `TryStowFromHotbar` and delete `TryStowAt`**

In `BackpackObject.cs`, delete this method entirely:

```csharp
        public bool TryStowAt(InventoryItem item, PackSurfaceId surfaceId, Vector2 uv, float yaw) =>
            (Reaches(surfaceId) && TryPlace(item, surfaceId, uv, yaw)) || TryStow(item);
```

(also delete the `<summary>` block directly above it, which describes the fallback).

Replace `TryStowFromHotbar` with:

```csharp
        /// <summary>
        /// Move a hotbar slot's item onto the pack, at the exact spot and turn given.
        /// <b>Server side only</b> — callers want <see cref="RequestStow"/>.
        ///
        /// <para>
        /// The way in, and the mirror of <see cref="TryTakeToHotbar"/>. Both halves of the transfer
        /// replicate themselves and neither of them from here: the hotbar is
        /// <see cref="PlayerInventoryNetwork"/>'s, which is server-authoritative, and the pack's
        /// half is <see cref="BackpackNetwork"/>'s, which is watching this layout.
        /// </para>
        /// <para>
        /// <b>The pack is filled before the hotbar is emptied</b>, the same order
        /// <see cref="TryTakeToHotbar"/> uses and for the same reason: a placement that is going to
        /// be refused must be refused while the item is still safely somewhere. Doing it the other
        /// way round means every "the pack is full" is an item deleted out of the world.
        /// </para>
        /// <para>
        /// <b>There is no fallback.</b> A spot that is taken by the time this runs — another player
        /// got there first — is a refusal, and the item stays in the hotbar. It used to fall
        /// through to a first-fit search, which put gear somewhere nobody pointed at; the player
        /// only ever sends this for cells they watched turn green, so anything else is a lie about
        /// what they asked for.
        /// </para>
        /// </summary>
        public bool TryStowFromHotbar(IPlayerInventory hotbar, int slotIndex,
                                      PackSurfaceId surfaceId, Vector2 uv, float yaw)
        {
            if (hotbar == null) return false;
            if (slotIndex < 0 || slotIndex >= hotbar.GetInventorySize()) return false;

            InventorySlot slot = hotbar.GetSlot(slotIndex);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null || string.IsNullOrEmpty(item.ID)) return false;

            // The layout is keyed by id, so an item already on the pack cannot be placed a second
            // time — TryPlace answers false for it. Caught here instead, because reaching the
            // hotbar removal below on a refused placement is exactly the item-deleting path this
            // method is ordered to avoid, and because the same asset really can be in both places
            // at once: the hotbar holds items by reference, not by instance.
            if (TryFindPlacement(item.ID, out _)) return false;

            if (!Reaches(surfaceId) || !TryPlace(item, surfaceId, uv, yaw)) return false;

            // Cannot fail: the index was bounds-checked against this very hotbar and the slot was
            // read out of it. Undone rather than trusted anyway, because the failure would be one
            // item in two places, which nothing downstream would ever notice.
            if (!hotbar.TryRemoveItem(slotIndex))
            {
                Layout.Remove(item.ID);
                return false;
            }

            return true;
        }

        /// <summary>Is this asset already lying on the pack? The layout is keyed by id, so a
        /// second copy of one can never be placed — see <see cref="TryStowFromHotbar"/>.</summary>
        public bool Holds(string itemId) => TryFindPlacement(itemId, out _);
```

- [ ] **Step 4: Rewrite `BackpackObject.RequestStow` and delete `CanStow`**

Replace `BackpackObject.RequestStow` with:

```csharp
        public void RequestStow(int slotIndex, PackSurfaceId surfaceId, Vector2 uv, float yaw,
                                Interactor interactor)
        {
            if (interactor == null) return;

            if (owner != null)
            {
                owner.RequestStow(slotIndex, surfaceId, uv, yaw, interactor);
                return;
            }

            // Same unowned-pack degradation RequestTake documents, and unreachable for the same
            // reason: every pack is bound to a wearer in BackpackController.Awake.
            IPlayerInventory hotbar = interactor.GetComponentInParent<IPlayerInventory>();
            if (hotbar != null) TryStowFromHotbar(hotbar, slotIndex, surfaceId, uv, yaw);
        }
```

Delete `CanStow` and its `<summary>` block entirely. It predicted the server's first-fit, which no
longer happens; the hand asks `PackLayout.CanPlace` about the cells it is already drawing, which is
the same question the server will answer.

- [ ] **Step 5: Pack yaw into the wire in `BackpackController`**

Replace `BackpackController.RequestStow` and `OnStowRequested` with:

```csharp
        /// <summary>
        /// The way IN, and the mirror of <see cref="RequestTake"/>: an item held in the player's
        /// hand goes onto this pack, at the spot and turn they lined it up at.
        ///
        /// <para>
        /// Same channel, same direction, same rule that nothing happens locally — two people can be
        /// reaching into one pack, so which of them gets the space under the cursor is the server's
        /// to decide, exactly as which of them gets the last water cell is.
        /// </para>
        /// <para>
        /// The hotbar slot travels as an INDEX where the other messages are positional, and that
        /// difference is deliberate: a hotbar slot is a numbered box, and it is not a thing anybody
        /// else is rearranging underneath them. See <see cref="NetMsg.PackStow"/> for the field
        /// layout.
        /// </para>
        /// </summary>
        public void RequestStow(int slotIndex, PackSurfaceId surface, Vector2 uv, float yaw,
                                Interactor interactor)
        {
            if (interactor == null) return;

            // The low byte of A is the slot and the next one up is the turn, so a slot that will
            // not fit in a byte would silently corrupt the yaw beside it.
            if (slotIndex < 0 || slotIndex > byte.MaxValue) return;

            // The stower's BODY, resolved the way the messaging layer resolves it — see the note
            // in RequestTake, which this is the other half of.
            GameObject stower = NetChannel.RootOf(interactor);
            if (stower == null) return;

            var arg = new NetArg
            {
                A = slotIndex | (PackGrid.QuarterTurns(yaw) << 8),
                B = (int)surface,
                P = new Vector3(uv.x, 0f, uv.y),
            };

            this.NetToServer(NetMsg.PackStow, arg.With(stower));
        }

        /// <summary>Server side: put it on the pack, if it is still in that slot and the spot is
        /// still free.</summary>
        private void OnStowRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;
            if (Pack == null || CurrentState != State.Open) return;

            GameObject stower = arg.Resolve();
            if (stower == null) return;

            // GetComponentInChildren rather than GetComponent, for the reason written out in
            // OnTakeRequested: a body may keep its hotbar on a child.
            var hotbar = stower.GetComponentInChildren<IPlayerInventory>(true);
            if (hotbar == null) return;

            // Every stow is aimed now — there is no first-fit sentinel to fall back to — so a
            // surface that does not decode is a malformed request and is refused outright.
            if (!TryDecodeSurface(arg.B, out PackSurfaceId surface)) return;

            int slot = arg.A & byte.MaxValue;
            float yaw = ((arg.A >> 8) & 3) * 90f;

            // Idempotent the way the take is: the second request finds the slot already empty and
            // answers false rather than placing a second copy.
            Pack.TryStowFromHotbar(hotbar, slot, surface, new Vector2(arg.P.x, arg.P.z), yaw);
        }
```

- [ ] **Step 6: Update the `PackStow` wire documentation**

In `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs`, replace the field-layout lines of the
`PackStow` comment block (the four lines beginning `//   Target`) with:

```csharp
        //   Target  the player reaching in, as for PackTake. Their hotbar is the source.
        //   A       the hotbar slot index in the low byte, the placement's quarter turns (0-3) in
        //           the next one up — the same byte packing PackMove uses for its two surfaces.
        //           Yaw has to travel: an item is turned in the player's hand before it is put
        //           down, and a server that placed everything at zero would land it on cells the
        //           player never saw highlighted.
        //   B       the surface being placed on. There is no "the cursor was nowhere" sentinel any
        //           more — a stow is only ever sent for a spot the player pointed at and watched
        //           go green, and a spot the server finds taken is refused rather than first-fitted.
        //   P       where on that surface, in its uv: X and Z, Y unused.
```

- [ ] **Step 7: Run the type-check to verify it passes**

Run the type-check block.
Expected: no `error CS` in `BackpackObject.cs`, `BackpackController.cs`, `NetMessaging.cs` or
`BackpackStowTests.cs`. `PackDragController.cs` will still error on `pack.CanStow(...)` and
`TryStowAt` — that is expected and is fixed in Task 4. **Note the exact list of remaining errors
so Task 4 can be checked against it.**

- [ ] **Step 8: Run the backpack EditMode tests**

Delete the stale results file first, then run
`Tools/Tests/Run EditMode Tests (headless)` from the Unity menu (or via
`HeadlessTestRunner.RunEditModeDeferred("BackpackStowTests")` over the bridge), and poll:

```bash
rm -f Temp/headless_tests.txt
# ... trigger the run ...
until grep -q DONE Temp/headless_tests.txt 2>/dev/null; do sleep 5; done
cat Temp/headless_tests.txt
```

Expected: `BackpackStowTests` all pass, including the two new ones. A result that comes back in
seconds is a truncated run — re-run it.

- [ ] **Step 9: Commit**

```bash
git add Assets/Game/Scripts/Items/Backpack/BackpackObject.cs Assets/Game/Scripts/Items/Backpack/BackpackController.cs Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs Assets/Game/Tests/Editor/BackpackStowTests.cs
git commit -m "feat(backpack): stow places at the exact spot and carries yaw"
```

---

## Task 2: Green and red ghost cells

`PackGridVisual.Show` draws the ghost's cells in one colour, because the magnet guaranteed they
were always legal. They now carry the verdict.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Backpack/Placement/PackGridVisual.cs:56-60` (tints), `:143-148` (fields), `:178-183` (constructor), `:199-250` (`Show`), `:379-416` (`CommitTo`), `:333-345` (`HideGhost`), `:355-378` (`Dispose`)

- [ ] **Step 1: Replace the single ghost tint with two**

Replace this:

```csharp
        /// <summary>A cell the drop would legally use.</summary>
        private static readonly Color ClearTint = new(0.45f, 0.85f, 1f, 0.55f);
```

with:

```csharp
        /// <summary>A cell the placement would legally use.</summary>
        private static readonly Color LegalTint = new(0.38f, 0.92f, 0.45f, 0.55f);

        /// <summary>
        /// A cell the placement is refused on — clashing with placed gear, or hanging off an edge
        /// this face does not allow overhang on.
        ///
        /// Drawn on the WHOLE footprint rather than only the offending cells. The question the
        /// player is asking is "can this go here", which has one answer for the whole item; a
        /// footprint that was part green and part red would read as a partial placement, which is
        /// not a thing that can happen.
        /// </summary>
        private static readonly Color RefusedTint = new(1f, 0.30f, 0.28f, 0.55f);
```

- [ ] **Step 2: Two materials instead of one**

Replace:

```csharp
        private readonly Material clearMaterial;

        private GameObject clearObject;
        private Mesh clearMesh;
```

with:

```csharp
        private readonly Material legalMaterial;
        private readonly Material refusedMaterial;

        private GameObject ghostObject;
        private Mesh ghostMesh;
```

Add `ghostLegal` beside the other ghost cache fields:

```csharp
        private PackSurface ghostSurface;
        private Vector2Int ghostOrigin;
        private PackShape ghostOriented;

        /// <summary>Which of the two tints the cached geometry is currently painted with. Part of
        /// the early-out key, because legality can flip without the ghost moving a pixel — another
        /// player placing something under it does exactly that.</summary>
        private bool ghostLegal;
```

In the constructor, replace:

```csharp
            clearMaterial = BuildMaterial("PackGridClear");
            clearMaterial.SetColor(ColorId, ClearTint);
```

with:

```csharp
            legalMaterial = BuildMaterial("PackGridLegal");
            legalMaterial.SetColor(ColorId, LegalTint);

            refusedMaterial = BuildMaterial("PackGridRefused");
            refusedMaterial.SetColor(ColorId, RefusedTint);
```

- [ ] **Step 3: `Show` takes a verdict**

Replace the `Show` method's signature, doc comment and early-out with:

```csharp
        /// <summary>
        /// Draw the cells the held item would occupy, green when the placement is legal and red
        /// when it is not.
        ///
        /// <para>
        /// This is the whole refusal readout. There is no message and no cursor change: the cells
        /// the player is aiming at say yes or no, and a click on red turns the item rather than
        /// doing nothing.
        /// </para>
        /// </summary>
        public void Show(PackSurface surface, Vector2Int origin, PackShape oriented, bool legal)
        {
            if (surface == null || oriented.IsEmpty)
            {
                HideGhost();
                return;
            }

            // Rebuilt only when the ghost actually moved or changed verdict. Compared field-by-field
            // rather than via oriented.Equals(ghostOriented): PackShape has no Equals override, so
            // that call would box through ValueType.Equals every single frame. A masked
            // (non-rectangular) shape is excluded from the early-out outright and always rebuilds —
            // Rotated allocates a fresh backing array on every call, so a same-Width/Height mask
            // could still be a DIFFERENT pattern (a rotated L keeps its bounding box but not its
            // cells), and Width/Height alone cannot tell them apart. Rectangles, the common case,
            // have no such array to distinguish and cache cleanly.
            if (surface == ghostSurface && origin == ghostOrigin && legal == ghostLegal &&
                oriented.IsRectangular && ghostOriented.IsRectangular &&
                oriented.Width == ghostOriented.Width && oriented.Height == ghostOriented.Height)
                return;

            ghostSurface = surface;
            ghostOrigin = origin;
            ghostOriented = oriented;
            ghostLegal = legal;
```

The cell loop that follows is unchanged. Replace only its final line:

```csharp
            CommitTo(ref clearObject, ref clearMesh, "PackGridClearCells", clearMaterial, surface);
```

with:

```csharp
            CommitTo(ref ghostObject, ref ghostMesh, "PackGridGhostCells",
                     legal ? legalMaterial : refusedMaterial, surface);
        }
```

**Important:** the cell loop currently skips cells with `if (!PackGrid.OnGrid(surface.Size, cell)) continue;`.
Leave that as it is — an overhanging cell has nowhere to be drawn, and the refusal is already
carried by every other cell of the footprint being red.

- [ ] **Step 4: Make `CommitTo` re-assign the material**

In `CommitTo`, the renderer's material is only set on first build, so a green→red swap would
never take. Move the assignment out of the `if (go == null)` block:

```csharp
            if (go == null)
            {
                go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };

                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                var created = go.AddComponent<MeshRenderer>();
                created.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                created.receiveShadows = false;

                int layer = BackpackItemVisual.ItemLayer;
                if (layer >= 0) go.layer = layer;
            }

            // Assigned on every commit, not only on the first: the ghost swaps between the legal
            // and refused materials on the same object, and a material set once at construction
            // would leave it stuck on whichever verdict happened to be first.
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
```

- [ ] **Step 5: Rename the ghost fields in `HideGhost` and `Dispose`**

In `HideGhost`:

```csharp
            ghostSurface = null;

            if (ghostObject != null) ghostObject.SetActive(false);
```

In `Dispose`, replace the three `clear*` lines in each of the destroy and null-out groups:

```csharp
            Destroy(ghostObject);
            Destroy(latticeFreeObject);
            Destroy(latticeTakenObject);
            Destroy(ghostMesh);
            Destroy(latticeFreeMesh);
            Destroy(latticeTakenMesh);
            Destroy(legalMaterial);
            Destroy(refusedMaterial);
            Destroy(latticeFreeMaterial);
            Destroy(latticeTakenMaterial);

            ghostObject = null;
            latticeFreeObject = null;
            latticeTakenObject = null;
            ghostMesh = null;
            latticeFreeMesh = null;
            latticeTakenMesh = null;
```

Also update the class doc comment's second paragraph, which still describes the magnet:

```csharp
    /// Three callers, one geometry. <see cref="BuildPlaced"/> makes a permanent child of the
    /// surface for an item already on the mat — the ring of cells the player asked to see around
    /// attached gear. The instance form has two passes while an item is in hand:
    /// <see cref="Show"/> draws the held item's own cells, green where the placement is legal and
    /// red where it is not; <see cref="ShowLattice"/> draws the WHOLE hovered face underneath it,
    /// free cells barely-there and occupied ones filled in the rig's webbing ochre, so free space
    /// reads at a glance through the gear sitting on it.
```

- [ ] **Step 6: Run the type-check**

Run the type-check block.
Expected: `PackGridVisual.cs` clean. `PackDragController.cs` now also errors on `cellGrid.Show(...)`
taking three arguments — expected, fixed in Task 4.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Scripts/Items/Backpack/Placement/PackGridVisual.cs
git commit -m "feat(backpack): ghost cells draw green when legal and red when refused"
```

---

## Task 3: Mechanical rename — drag becomes hand

No behaviour change at all. Doing it on its own keeps the rewrite in Task 4 readable as a diff of
logic rather than a diff of names.

**Files:**
- Rename: `Assets/Game/Scripts/Items/Backpack/Focus/PackDragController.cs` → `PackHandController.cs` (and its `.meta`)
- Rename: `Assets/Game/Scripts/Items/Backpack/Focus/PackDragVisuals.cs` → `PackHandVisuals.cs` (and its `.meta`)
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs:44,165`
- Modify: `Assets/Game/Scripts/Presentation/UI/HUD/InventoryUI.cs`
- Modify: `Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs:30`
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackPointer.cs:12`, `PackLeafDrag.cs:10`
- Modify: `Assets/Game/Scripts/Core/Input/PlayerInputManager.cs:149`

- [ ] **Step 1: Move the files, keeping their meta files**

`git mv` keeps the `.meta` beside its file, which is what preserves the GUID — without it Unity
mints a new one and every serialized reference to the type breaks silently.

```bash
git mv Assets/Game/Scripts/Items/Backpack/Focus/PackDragController.cs Assets/Game/Scripts/Items/Backpack/Focus/PackHandController.cs
git mv Assets/Game/Scripts/Items/Backpack/Focus/PackDragController.cs.meta Assets/Game/Scripts/Items/Backpack/Focus/PackHandController.cs.meta
git mv Assets/Game/Scripts/Items/Backpack/Focus/PackDragVisuals.cs Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs
git mv Assets/Game/Scripts/Items/Backpack/Focus/PackDragVisuals.cs.meta Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs.meta
```

Neither type is referenced from a prefab or scene — `PackHandController` is added by
`AddComponent` at runtime and `PackHandVisuals` is a plain C# class — so no asset needs touching.

- [ ] **Step 2: Rename the types and their methods across the project**

```bash
python3 - <<'PY'
import pathlib, re
subs = [
    ('PackDragController', 'PackHandController'),
    ('PackDragVisuals',    'PackHandVisuals'),
    ('ClearDragFeedback',  'ClearPackFeedback'),
    ('SetDragTint',        'SetCarryDenied'),
    ('BeginDrag(',         'BeginCarry('),
    ('MoveDrag(',          'MoveCarry('),
    ('EndDrag(',           'EndCarry('),
]
for p in pathlib.Path('Assets').rglob('*.cs'):
    t = p.read_text()
    n = t
    for a, b in subs:
        n = n.replace(a, b)
    if n != t:
        p.write_text(n)
        print(p)
PY
```

**Watch out:** `BeginDrag(` / `EndDrag(` also appear as the EventSystem's `OnBeginDrag(` /
`OnEndDrag(` in `InventorySlotUI.cs`. The replacements above are anchored on the bare names, so
`OnBeginDrag(` becomes `OnBeginCarry(` — which is wrong, those are interface methods. Fix them
back:

```bash
python3 - <<'PY'
import pathlib
p = pathlib.Path('Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs')
t = p.read_text().replace('OnBeginCarry(', 'OnBeginDrag(').replace('OnEndCarry(', 'OnEndDrag(')
p.write_text(t)
PY
grep -n "OnBeginDrag\|OnEndDrag\|OnBeginCarry\|OnEndCarry" Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs
```

Expected: only `OnBeginDrag` and `OnEndDrag` appear.

- [ ] **Step 3: Rename the file-local field in `PackFocusSession`**

In `PackFocusSession.cs`, the field is still called `drag`:

```bash
python3 - <<'PY'
import pathlib, re
p = pathlib.Path('Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs')
t = p.read_text()
t = t.replace('private PackHandController drag;', 'private PackHandController hand;')
t = re.sub(r'\bdrag\b', 'hand', t)
p.write_text(t)
PY
grep -n "hand" Assets/Game/Scripts/Items/Backpack/Focus/PackFocusSession.cs
```

Expected: `private PackHandController hand;`, `hand = PackHandController.Attach(...)`,
`if (hand != null) hand.Cancel();`, `hand = null;`.

- [ ] **Step 4: Run the type-check**

Run the type-check block.
Expected: exactly the same errors Task 1 Step 7 and Task 2 Step 6 left behind (`CanStow`,
`TryStowAt`, `cellGrid.Show` arity) — all in `PackHandController.cs`, all fixed in Task 4. No
*new* errors, and nothing about a missing `PackDragController` or `PackDragVisuals`.

- [ ] **Step 5: Commit**

```bash
git add -A Assets/Game/Scripts
git commit -m "refactor(backpack): rename PackDrag* to PackHand*, no behaviour change"
```

---

## Task 4: The click state machine

The task the whole plan is for. `PackHandController` loses both drag machines, `InventoryUI` and
`InventorySlotUI` lose their EventSystem plumbing, and all three change in one commit because they
reference each other by name.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackHandController.cs` (substantial rewrite)
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs:217-222` (`SetCarryDenied`)
- Modify: `Assets/Game/Scripts/Presentation/UI/HUD/InventoryUI.cs`
- Modify: `Assets/Game/Scripts/Presentation/UI/HUD/InventorySlotUI.cs`

- [ ] **Step 1: Repurpose the proxy tint as a timed refusal flash**

In `PackHandVisuals.cs`, replace `SetCarryDenied` (the old `SetDragTint`) doc comment:

```csharp
        /// <summary>
        /// Flashes the held copy refusal-red, or clears it.
        ///
        /// Not the ordinary "this spot is taken" readout any more — the ghost cells carry that, in
        /// green and red, on every frame. This is the one click that can do literally nothing: red
        /// cells under an item whose authored row forbids turning, where the rotate-on-refusal
        /// answer is not available. A click that changes nothing at all has to say so, or the
        /// button reads as broken.
        /// </summary>
        public void SetCarryDenied(bool denied)
        {
            dragMaterial.SetColor(ColorId, denied ? ConflictBody : DragBody);
            dragMaterial.SetColor(OutlineColorId, denied ? ConflictOutline : DragOutline);
        }
```

Also update the constructor's call `SetCarryDenied(conflict: false);` to `SetCarryDenied(false);`
(the parameter is renamed).

- [ ] **Step 2: Rewrite the hand state in `PackHandController`**

Replace everything from the `// ── Drag state ─────` field block down to (but not including)
`// ── Flipping the leaf ─────`'s `private bool draggingLeaf;` with:

```csharp
        // ── The hand ─────────────────────────────────────────────────────────
        //
        // Two states and nothing else: empty, or holding one item. Every verb in focus mode is a
        // left click resolved against which of the two it is, which is why there is no gesture
        // state here at all — no button-down origin, no drag threshold, no source enum for "which
        // machine started this". A lift is local and costs nothing; only putting it down is a
        // request.

        private bool carrying;
        private InventoryItem heldItem;

        /// <summary>Where the held item came from, and therefore what putting it down means.</summary>
        private enum HandSource
        {
            /// <summary>Lifted off the mat. Putting it down is a move; putting it in a slot is a take.</summary>
            Pack,

            /// <summary>Lifted out of a hotbar slot. Putting it down is a stow.</summary>
            Hotbar,
        }

        private HandSource heldFrom;

        /// <summary>The display copy the held item was lifted off, left rim-ghosted where it was.</summary>
        private GameObject originVisual;

        private PackSurfaceId originSurface;

        /// <summary>
        /// A cell the held item really filled, as opposed to its placement uv, which is where its
        /// block is centred. This is what names it to the server — see
        /// <see cref="PackLayout.TryAnchorUv"/> for why the two had to come apart.
        /// </summary>
        private Vector2 originGrab;

        /// <summary>The hotbar slot a <see cref="HandSource.Hotbar"/> item came out of.</summary>
        private int originSlot = -1;

        /// <summary>
        /// The turn the held item is being shown at. Written by the wheel and by a click on a
        /// refused spot, and read by the preview, the cells and the request alike — there is no
        /// second "the yaw it will actually use" any more, because nothing turns the item on the
        /// player's behalf.
        /// </summary>
        private float yaw;

        /// <summary>
        /// Has the carried copy been built yet? An item lifted off the mat has one from the first
        /// frame. One lifted out of a hotbar slot starts with the cursor over the HUD, which is
        /// nowhere near the rig and has no face to seat a true-size proxy against — so the copy
        /// appears the moment the cursor reaches the mat, and there is nothing floating in the
        /// corner of the screen before that.
        /// </summary>
        private bool proxyBuilt;

        /// <summary>The hotbar slot under the cursor this frame, or -1.</summary>
        private int hoveredSlot = -1;

        private PackSurface targetSurface;
        private Vector2 targetUv;

        /// <summary>Is the cursor on one of the rig's faces at all this frame?</summary>
        private bool overSurface;

        /// <summary>
        /// Would a click put the held item down where it is being shown?
        ///
        /// <para>
        /// Asked of <see cref="PackLayout.CanPlace"/> about the exact cells being drawn, at the
        /// exact turn being drawn — not about a nearby spot the item could be moved to. That is
        /// the whole difference from the magnet this replaced: the answer describes the player's
        /// aim rather than correcting it, so a red readout is information about where they are
        /// pointing instead of a spot that quietly moved out from under them.
        /// </para>
        /// </summary>
        private bool placementLegal;

        public bool IsCarrying => carrying;
```

Delete the now-unused fields: `dragging`, `dragItem`, `dragFrom`, `DragSource`, `originUv`,
`originYaw`, `targetYaw`, `dropIsLegal`, `springBack`, `SpringBackSeconds`, and the entire
"Caching the magnet search" field group (`nearestSurfaceKey`, `nearestUvKey`, `nearestYawKey`,
`nearestSearchDirty`, `nearestFound`, `nearestUv`, `nearestYaw`).

- [ ] **Step 3: Rewrite `OnLayoutChanged`, `AbandonDrag` and `Cancel`**

```csharp
        /// <summary>The lattice shows other items' cells, so any layout change stales it.</summary>
        private void OnLayoutChanged()
        {
            if (cellGrid != null) cellGrid.MarkLatticeDirty();
        }
```

Replace `AbandonDrag` with:

```csharp
        /// <summary>
        /// Puts whatever is in hand back where it came from, and lets go of the leaf. The
        /// component stays alive and usable.
        ///
        /// <para>
        /// There is nothing to undo. A lift is local — no request went out and no layout changed —
        /// so the item has been sitting where it always was for the whole time it looked like it
        /// was in the player's hand, and letting go is just no longer drawing the copy.
        /// </para>
        /// <para>
        /// The rack key needs this: the surface the ghost is tracking is about to swing through
        /// ninety degrees. Calling <see cref="Cancel"/> for that destroyed the controller for the
        /// rest of the session, so one press of R silently stopped the player picking anything up.
        /// </para>
        /// </summary>
        public void ReturnToOrigin()
        {
            if (draggingLeaf) ReleaseLeaf(commit: false);

            if (!carrying) return;

            carrying = false;

            if (visuals != null) visuals.EndCarry();
            if (cellGrid != null) cellGrid.Hide();

            ClearHand();
        }

        /// <summary>The per-carry state every exit from the hand shares.</summary>
        private void ClearHand()
        {
            heldItem = null;
            originVisual = null;
            originSlot = -1;
            proxyBuilt = false;
            hoveredSlot = -1;
            placementLegal = false;

            InventoryUI.ClearPackFeedback();
        }
```

In `Cancel`, replace the `springBack` line and the `dragging`/`dragItem`/`proxyBuilt` lines with a
call to the above, keeping everything else:

```csharp
        public void Cancel()
        {
            // Before the fields are cleared: the leaf is the pack's own state, not this
            // component's, and leaving focus mid-flip must not leave it stranded halfway.
            if (draggingLeaf) ReleaseLeaf(commit: false);

            carrying = false;
            ClearHand();

            if (visuals != null) visuals.Dispose();
            visuals = null;

            if (cellGrid != null) cellGrid.Dispose();
            cellGrid = null;

            if (Active == this) Active = null;

            Destroy(this);
        }
```

In `OnDestroy`, delete nothing except the `springBack` reference if one remains; `ClearDragFeedback`
was already renamed to `ClearPackFeedback` in Task 3.

- [ ] **Step 4: Rewrite `Update` and `UpdateHover`**

```csharp
        private void Update()
        {
            if (visuals == null) return;

            // Unconditional, ahead of every other early return below: "the flash always ends" is
            // a promise about the WALL CLOCK, not about whichever state the rest of Update happens
            // to be in.
            if (deniedUntil > 0f && Time.unscaledTime >= deniedUntil)
            {
                deniedUntil = 0f;
                visuals.SetHoverDenied(false);
                visuals.SetCarryDenied(false);
            }

            Camera cam = focusCamera != null ? focusCamera.Camera : null;
            BackpackObject pack = controller != null ? controller.Pack : null;

            if (cam == null || pack == null) return;

            Mouse mouse = Mouse.current;

            if (draggingLeaf) UpdateLeafDrag(cam, pack, mouse);
            else if (carrying) UpdateCarry(cam, pack, mouse);
            else UpdateHover(cam, pack, mouse);
        }
```

Replace `UpdateHover`'s body's second half — from `InventoryItem item = pack.ItemFor(...)` to the
end — with the lift branch, and drop the right-click branch entirely:

```csharp
            InventoryItem item = pack.ItemFor(placement.ItemId);

            SetHovered(visual);

            visuals.ShowName(item != null ? item.itemName + "   (click to pick up)" : null,
                             PackPointer.CursorPosition);

            if (mouse == null || overBar) return;

            if (mouse.leftButton.wasPressedThisFrame) Lift(pack, item, placement, visual);
```

And update the bare-mat hint, which still names the drag verbs:

```csharp
                visuals.ShowName(onBoard ? LeafHint(pack.IsRacked)
                                         : overSurface ? "Click a hotbar item to take it in hand — or press 1-4"
                                         : null,
                                 PackPointer.CursorPosition);
```

Delete `SendToHotbar` entirely, and update `LeafHint`:

```csharp
        private static string LeafHint(bool racked) =>
            racked ? "Click to lay the board flat"
                   : "Click to stand the board up";
```

- [ ] **Step 5: Write `Lift`, `TryLiftFromSlot` and `OnHotbarKey`**

Replace `BeginDrag` with:

```csharp
        /// <summary>Take a placed item off the mat and into the hand. Local only — nothing is
        /// sent, and the copy on the mat stays exactly where it is until a placement moves it.</summary>
        private void Lift(BackpackObject pack, InventoryItem item, PackPlacement placement,
                          GameObject visual)
        {
            if (item == null || item.itemPrefab == null) return;

            PackSurface surface = pack.SurfaceFor(placement.Surface);
            if (surface == null) return;

            carrying = true;
            heldItem = item;
            heldFrom = HandSource.Pack;
            originSlot = -1;

            originVisual = visual;
            originSurface = placement.Surface;

            // Kept apart from the placement's own uv on purpose. That is where the item VISUALLY
            // sits; this is a cell it actually fills, which is the only point the server can
            // resolve it from. For a rectangle they are the same; for a mask with a hole in the
            // middle of its block they are not.
            originGrab = pack.AnchorUv(placement);

            yaw = placement.Yaw;

            targetSurface = surface;
            targetUv = placement.Uv;

            // The lift happened ON a face, so that is the state until the first UpdateCarry says
            // otherwise — and the item is trivially legal where it already is.
            overSurface = true;
            placementLegal = true;

            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            // The hover rim comes off first: hover and the carried copy both append a material to
            // the same renderers, and the one that gets there first is the one that can be taken
            // back off.
            SetHovered(null);
            visuals.ShowName(null, Vector2.zero);

            // An outline stays where the item was, so the player can see what they are about to
            // leave empty.
            visuals.SetGhost(originVisual);
            visuals.BeginCarry(item.itemPrefab, surface, placement.Uv, placement.Yaw);
            proxyBuilt = true;
        }
```

Replace `BeginHotbarDrag`, `EndHotbarDrag`, `CancelHotbarDrag`, `EndHotbarDragVisuals`,
`OnStowKey` and `StowSlot` with:

```csharp
        // ── Lifting out of the hotbar ────────────────────────────────────────

        /// <summary>
        /// Take a hotbar slot's item into the hand. Called by the HUD when a slot is clicked, and
        /// by the 1-4 keys, which are the same verb on a key.
        ///
        /// <para>
        /// Answers false when it will not take the item, so the slot can shake rather than start a
        /// carry that can only ever end in nothing happening.
        /// </para>
        /// </summary>
        public bool TryLiftFromSlot(int slotIndex, InventoryItem item)
        {
            if (carrying || draggingLeaf) return false;
            if (visuals == null || item == null || item.itemPrefab == null) return false;

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null || interactor == null) return false;

            // The layout is keyed by id, so this asset already lying on the mat is going to be
            // refused wherever the player tries to put it down. Refusing the lift is honest about
            // that up front; the alternative is an item in hand with no legal cell anywhere.
            if (pack.Holds(item.ID)) return false;

            carrying = true;
            heldItem = item;
            heldFrom = HandSource.Hotbar;
            originSlot = slotIndex;

            originVisual = null;
            originSurface = default;
            originGrab = Vector2.zero;

            yaw = 0f;

            if (cellGrid != null) cellGrid.MarkLatticeDirty();

            // No proxy and no target yet. The cursor is over the HUD at the bottom of the screen,
            // which is not a face, and TryHitSurface is what will say otherwise on some later
            // frame — see proxyBuilt.
            targetSurface = null;
            overSurface = false;
            placementLegal = false;
            proxyBuilt = false;

            SetHovered(null);
            visuals.ShowName(null, Vector2.zero);

            InventoryUI.SetHeldOrigin(slotIndex);

            return true;
        }

        /// <summary>
        /// A hotbar key while the pack is open, with an empty hand: that slot's item comes into it.
        ///
        /// The click verb on a key, and nothing more. It used to be an aimed stow that leaned on
        /// the magnet to find a spot, which is exactly the auto-placement this interaction removed.
        /// </summary>
        private void OnHotbarKey(int slotIndex)
        {
            if (carrying || draggingLeaf) return;

            IPlayerInventory hotbar = Hotbar();
            if (hotbar == null) return;
            if (slotIndex < 0 || slotIndex >= hotbar.GetInventorySize()) return;

            InventorySlot slot = hotbar.GetSlot(slotIndex);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null) return;

            if (!TryLiftFromSlot(slotIndex, item)) InventoryUI.ShakeSlot(slotIndex);
        }
```

Update the two `Subscribe` / `OnDisable` hookups, which still name `OnStowKey`:

```csharp
            input.OnPackStowPressed -= OnHotbarKey;
            input.OnPackStowPressed += OnHotbarKey;
```

- [ ] **Step 6: Write `UpdateCarry` and the click resolution**

Replace `UpdateDrag` and `Release` with:

```csharp
        // ── Carrying ─────────────────────────────────────────────────────────

        private void UpdateCarry(Camera cam, BackpackObject pack, Mouse mouse)
        {
            overSurface = PackPointer.TryHitSurface(cam, pack.Surfaces,
                                                    out PackSurface surface, out Vector2 uv);

            PackShape shape = pack.ShapeFor(heldItem);

            if (overSurface)
            {
                targetSurface = surface;

                // Grid-snapped and NOTHING else. The item goes exactly where the cursor is
                // pointing, at exactly the turn it is being shown at, and the only question left
                // is whether that is legal — which is what the cells answer.
                targetUv = PackLayout.Snap(surface.Id, surface.Size, shape, uv, yaw);

                placementLegal = pack.Layout.CanPlace(surface.Id, surface.Size, shape,
                                                      targetUv, yaw, HeldItemId());
            }
            else
            {
                placementLegal = false;
            }

            // Where the hotbar is under the cursor. Asked every frame rather than only on the
            // click, because the slot has to light up while the player is still deciding.
            hoveredSlot = InventoryUI.SlotIndexUnder(PackPointer.CursorPosition);

            bool overHotbar = hoveredSlot >= 0;

            InventoryUI.SetDropTarget(hoveredSlot);

            // One rule for either source: the flat icon over the bar, the true-size copy over the
            // mat. What is in the hand is visible at every moment, wherever the cursor is.
            if (overHotbar) InventoryUI.ShowCarriedIcon(heldItem, PackPointer.CursorPosition);
            else InventoryUI.HideCarriedIcon();

            // The proxy is built the first time the cursor reaches a face. For a pack lift that is
            // the frame it was lifted; for a hotbar lift it is whenever the player gets there.
            if (!proxyBuilt && overSurface && targetSurface != null)
            {
                visuals.BeginCarry(heldItem.itemPrefab, targetSurface, targetUv, yaw);
                proxyBuilt = true;
            }

            if (proxyBuilt) visuals.MoveCarry(heldItem.itemPrefab, targetSurface, targetUv, yaw);

            // The bar is drawn over the same screen the rig is. While the cursor is over a slot a
            // click puts the item in THAT SLOT, not on the face behind the bar — but the raycast
            // against the rig keeps hitting whatever is back there regardless, so without this the
            // cells would promise a landing the click was never going to honour.
            bool showCells = overSurface && targetSurface != null && !overHotbar;

            if (showCells)
            {
                cellGrid.ShowLattice(targetSurface, pack.Layout, HeldItemId());

                PackShape oriented = PackOverhang.Clamp(targetSurface.Id, targetSurface.Size,
                                                        shape.Rotated(PackGrid.QuarterTurns(yaw)));

                cellGrid.Show(targetSurface,
                              PackGrid.BlockOrigin(targetSurface.Size, targetUv, oriented.Size),
                              oriented, placementLegal);
            }
            else
            {
                cellGrid.Hide();
            }

            visuals.ShowName(overHotbar ? $"Click to put it in slot {hoveredSlot + 1}" : null,
                             PackPointer.CursorPosition);

            if (mouse != null && mouse.leftButton.wasPressedThisFrame) ClickWhileCarrying(pack);
        }

        /// <summary>
        /// The click that resolves a carry. Four outcomes, and which one it is was settled by
        /// where the cursor is standing.
        ///
        /// <para>
        /// <b>Over a hotbar slot</b> — it goes in that slot, swapping with whatever was there.
        /// Tested first, because the hotbar is drawn over the same screen the mat is.
        /// </para>
        /// <para>
        /// <b>Over a face, cells green</b> — put down there, at the turn shown.
        /// </para>
        /// <para>
        /// <b>Over a face, cells red</b> — the item turns a quarter and stays in hand. This is the
        /// refusal, and it is a useful one: the commonest reason a spot is refused is that the item
        /// is the wrong way round for it, so the refusal and the fix are the same click.
        /// </para>
        /// <para>
        /// <b>Off the mat and off the bar</b> — nothing at all. There is no third place for an item
        /// to be, so there is nothing for a click on the sand to mean.
        /// </para>
        /// </summary>
        private void ClickWhileCarrying(BackpackObject pack)
        {
            if (hoveredSlot >= 0)
            {
                PutIntoSlot(hoveredSlot);
                return;
            }

            if (!overSurface || targetSurface == null) return;

            if (placementLegal)
            {
                PutDown();
                return;
            }

            Turn(pack);
        }

        /// <summary>
        /// A quarter turn, in the player's hand. The answer to a refused click.
        ///
        /// <para>
        /// Two shapes have no answer to give, and both must say so out loud rather than turning:
        /// an item whose authored row forbids turning at all, and a SYMMETRIC one — a 1x1, a 2x2,
        /// any square — whose quarter turn occupies the very same cells. The second is the subtler
        /// of the two and it is not rare: rotating it succeeds, changes the yaw, and changes
        /// nothing the player can see, so the click reads as a button that does not work. A refusal
        /// flash is the honest answer to "this does not go here and turning it will not help".
        /// </para>
        /// </summary>
        private void Turn(BackpackObject pack)
        {
            if (!PackShapes.AllowsRotation(heldItem, pack.Shapes) || !TurningWouldChangeAnything(pack))
            {
                deniedUntil = Time.unscaledTime + DeniedFlashSeconds;
                visuals.SetCarryDenied(true);
                return;
            }

            yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + YawPerNotch, 360f));
        }

        /// <summary>
        /// Would a quarter turn land the held item on a different set of cells?
        ///
        /// <para>
        /// <see cref="PackShape"/> has no equality operator, and adding one for this would be a
        /// public API for a private question — so the two orientations are compared cell by cell
        /// here. Cheap: this runs once per refused click, not per frame.
        /// </para>
        /// </summary>
        private bool TurningWouldChangeAnything(BackpackObject pack)
        {
            PackShape shape = pack.ShapeFor(heldItem);

            int turns = PackGrid.QuarterTurns(yaw);

            PackShape now = shape.Rotated(turns);
            PackShape next = shape.Rotated(turns + 1);

            if (now.Width != next.Width || now.Height != next.Height) return true;

            for (int y = 0; y < now.Height; y++)
                for (int x = 0; x < now.Width; x++)
                    if (now[x, y] != next[x, y]) return true;

            return false;
        }

        /// <summary>
        /// Put the held item down on the spot being shown.
        ///
        /// <para>
        /// A REQUEST and nothing else — no local move, no optimistic visual. The placed copy stays
        /// exactly where it is until the layout changes underneath it, which is what a server that
        /// allowed the action publishes; a server that refuses publishes nothing, and the item was
        /// never anywhere else.
        /// </para>
        /// </summary>
        private void PutDown()
        {
            if (heldFrom == HandSource.Pack)
                controller.RequestMove(originSurface, originGrab, targetSurface.Id, targetUv, yaw);
            else
                controller.RequestStow(originSlot, targetSurface.Id, targetUv, yaw, interactor);

            carrying = false;

            visuals.EndCarry();
            if (cellGrid != null) cellGrid.Hide();

            ClearHand();
        }

        /// <summary>
        /// Put the held item into a hotbar slot. Called by the click poll above and by the HUD,
        /// which resolves a click on a slot through <c>InventoryUI.ClickSlot</c>.
        /// </summary>
        public void PutIntoSlot(int slotIndex)
        {
            if (!carrying) return;

            BackpackObject pack = controller != null ? controller.Pack : null;
            if (pack == null) return;

            if (heldFrom == HandSource.Hotbar)
            {
                // Hotbar reordering is not this feature — IPlayerInventory has no move — so the
                // only slot a hotbar item may go back into is its own, and that is a cancel.
                if (slotIndex != originSlot)
                {
                    InventoryUI.ShakeSlot(slotIndex);
                    return;
                }

                ReturnToOrigin();
                return;
            }

            // Asked before the request goes out, and asked LOCALLY. A full hotbar is not a refusal
            // — TryTakeToHotbar swaps — but a swap with nowhere to put the displaced item is, and
            // the server refuses it by changing nothing, which on this screen is indistinguishable
            // from a lost packet.
            if (!pack.CanTakeToHotbar(originSurface, originGrab, Hotbar(), out bool refused, slotIndex)
                && refused)
            {
                InventoryUI.ShakeSlot(slotIndex);
                return;
            }

            controller.RequestTake(originSurface, originGrab, interactor, slotIndex);

            carrying = false;

            visuals.EndCarry();
            if (cellGrid != null) cellGrid.Hide();

            ClearHand();
        }

        /// <summary>
        /// The id a legality test should ignore, which is the one currently in the hand.
        ///
        /// Null for a hotbar lift, and that is the whole point of the distinction: an item lifted
        /// off the mat is not in its own way, but one still sitting in the hotbar has never
        /// occupied anything, so there is no placement of its own to ignore.
        /// </summary>
        private string HeldItemId() =>
            heldFrom == HandSource.Pack && heldItem != null ? heldItem.ID : null;
```

- [ ] **Step 7: Fix the wheel, and the leaf drag's guard**

```csharp
        /// <summary>
        /// The wheel turns what is in hand, in either direction — the click's rotate only goes one
        /// way, and three clicks to get back one quarter is a worse deal than a notch of scroll.
        /// </summary>
        private void OnYawScrolled(int notches)
        {
            if (!carrying) return;

            BackpackObject pack = controller != null ? controller.Pack : null;
            PackShapeLibrary shapes = pack != null ? pack.Shapes : null;

            if (!PackShapes.AllowsRotation(heldItem, shapes)) return;

            yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + notches * YawPerNotch, 360f));
        }
```

Delete `SpringBack` and the `SpringBackSeconds` constant. In `BeginLeafDrag`'s caller (`UpdateHover`)
nothing changes; the leaf is only ever reachable with an empty hand, which the `carrying` branch in
`Update` already guarantees.

- [ ] **Step 8: Replace the drag bridge in `InventoryUI` with a click bridge**

Rename the field `dragOriginIndex` to `heldOriginIndex` throughout the file (the doc comment above
it, which says "mid-drag", becomes "in the player's hand"). Then replace `BeginSlotDrag`,
`DragSlot`, `RequestSlotStow` and `EndSlotDrag` with:

```csharp
        /// <summary>
        /// A hotbar slot was clicked while the pack is open. The pack's hand resolves it: an empty
        /// hand lifts the slot's item, a full one puts what it is holding into the slot.
        ///
        /// <para>
        /// Outside focus mode there is no hand and this does nothing, which is correct — the cursor
        /// is locked out there and the bar is not clickable at all.
        /// </para>
        /// </summary>
        public void ClickSlot(int index)
        {
            PackHandController hand = PackHandController.Active;
            if (hand == null) return;

            if (hand.IsCarrying)
            {
                hand.PutIntoSlot(index);
                return;
            }

            if (playerInventory == null) return;

            InventorySlot slot = playerInventory.GetSlot(index);
            InventoryItem item = slot != null && !slot.IsEmpty ? slot.Item : null;
            if (item == null) return;

            // The hand declines when the same asset is already on the mat, where it could never be
            // put down. TryLiftFromSlot marks the slot reserved itself on success — see
            // SetHeldOrigin — so there is nothing to do on the way out.
            if (!hand.TryLiftFromSlot(index, item)) ShakeLocal(index);
        }
```

Add these three statics beside `SetDropTarget` and `ShakeSlot`:

```csharp
        /// <summary>The slot whose item is in the player's hand. Its tile reads as empty, but as
        /// an empty tile that is spoken for. -1 clears it.</summary>
        public static void SetHeldOrigin(int index)
        {
            if (local == null || local.heldOriginIndex == index) return;

            local.heldOriginIndex = index;
            local.RefreshAll();
        }

        /// <summary>The held item's icon, following the cursor while it is over the bar. The
        /// true-size copy on the mat is the other half of the same rule — see
        /// <c>PackHandController.UpdateCarry</c>.</summary>
        public static void ShowCarriedIcon(InventoryItem item, Vector2 screenPosition)
        {
            if (local == null) return;

            local.ShowGhost(item, screenPosition);
        }

        public static void HideCarriedIcon()
        {
            if (local == null) return;

            local.HideGhost();
        }
```

`ClearPackFeedback` needs no change beyond the field rename it already got in Task 3.

- [ ] **Step 9: Strip the EventSystem drag plumbing from `InventorySlotUI`**

Change the class declaration:

```csharp
    [DisallowMultipleComponent]
    public sealed class InventorySlotUI : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerClickHandler
    {
```

Delete `OnBeginDrag`, `OnDrag`, `OnEndDrag` and `OnDrop` and their doc comments, and replace
`OnPointerClick`:

```csharp
        /// <summary>
        /// Left click: this slot's item goes into the player's hand, or whatever is already in
        /// their hand goes into this slot. The same verb the mat uses, on the same button — the
        /// bar and the pack are one surface as far as the player is concerned, and a slot that
        /// needed a different gesture would break that.
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (parentUI != null) parentUI.ClickSlot(slotIndex);
        }
```

Update the class doc comment's fourth paragraph:

```csharp
    /// <para>
    /// <b>Clicking.</b> The pointer handlers here resolve nothing themselves — they hand the click
    /// to <c>PackHandController</c> through <see cref="InventoryUI"/>, which is already hit-testing
    /// the pack every frame and already knows what a legal placement is. This project has no
    /// EventSystem drag plumbing at all any more.
    /// </para>
```

- [ ] **Step 10: Run the type-check**

Run the type-check block.
Expected: **no `error CS` anywhere.** The `CanStow` / `TryStowAt` / `cellGrid.Show` errors carried
from Tasks 1 and 2 are now gone.

If a `CS0246` names a type you can grep on disk, regenerate the source list before debugging.

- [ ] **Step 11: Run the full EditMode suite**

```bash
rm -f Temp/headless_tests.txt
# trigger Tools/Tests/Run EditMode Tests (headless)
until grep -q DONE Temp/headless_tests.txt 2>/dev/null; do sleep 5; done
cat Temp/headless_tests.txt
```

Expected: `PackNearestFitTests` still passes (the magnet is not removed until Task 5). Against the
**documented baseline of 15 standing failures** — `MountRiderComponentRestoreTests` ×2,
`WingPackLaunchTests` ×2, `NpcPassengerTests` ×2 (all the `Time.time == 0` edit-mode artefact),
Backpack ×5, Lasso ×1, GrappleSwing ×1 — there must be **no new failure**. A result returning in
seconds is truncated; re-run.

- [ ] **Step 12: Commit**

```bash
git add Assets/Game/Scripts/Items/Backpack/Focus Assets/Game/Scripts/Presentation/UI/HUD
git commit -m "feat(backpack): click to lift, click to place, click on red to rotate"
```

---

## Task 5: Delete the magnet and the ground drop

Both are now unreachable. Removing them is what makes "no auto placement" and "items live only in
the hotbar or the pack" true rather than merely unused.

**Files:**
- Modify: `Assets/Game/Scripts/Items/Backpack/Placement/PackLayout.cs:390` (`TryFindNearest`)
- Modify: `Assets/Game/Scripts/Items/Backpack/BackpackObject.cs:1885` (`TryDropToWorld`)
- Modify: `Assets/Game/Scripts/Items/Backpack/BackpackController.cs:226,244,918-936` (drop wiring)
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs:355-366` (`PackDrop`)
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackHandVisuals.cs` (`SetHoverDenied`, `DeniedRim`)
- Modify: `Assets/Game/Scripts/Items/Backpack/Focus/PackHandController.cs` (`ClearDeniedFlash`)
- Delete: `Assets/Game/Tests/Editor/PackNearestFitTests.cs` and its `.meta`
- Test: `Assets/Game/Tests/Editor/PackHandTests.cs` (create)

### The hover refusal died with the right-click take

`PackHandVisuals.SetHoverDenied(true)` has no caller after Task 4, and cannot get one: the only
thing that ever turned the hover rim red was a refused right-click-straight-to-hotbar, and that
shortcut is gone. With an empty hand there is now no refusable hover at all — hovering a placed item
only ever offers "click to pick up".

Delete `SetHoverDenied` and the `DeniedRim` colour, and drop the `SetHoverDenied(false)` call from
`PackHandController.ClearDeniedFlash`, which then only has the carried copy's flash to clear. Leave
`HoverRim` and `SetHovered` alone — the ordinary hover rim is still the whole point of that method.

Task 4's implementer flagged this rather than doing it, because removing it reaches into the hover
material's setup and would have collided with this task's file list.

- [ ] **Step 1: Confirm both are unreachable**

```bash
grep -rn --include="*.cs" "TryFindNearest\|RequestDrop\|TryDropToWorld\|PackDrop" Assets/Game/Scripts
```

Expected: only the definitions themselves and `WorldSiteRegistry.TryFindNearest` (an unrelated
method on a different type — do **not** touch it). If anything under `Items/Backpack/Focus` still
appears, Task 4 is incomplete.

- [ ] **Step 2: Write the failing test — the yaw packing round-trips**

Create `Assets/Game/Tests/Editor/PackHandTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Tests
{
    /// <summary>
    /// The two pieces of arithmetic the click interaction rests on: the byte packing that gets a
    /// turn across the wire beside a hotbar slot, and the quarter-turn cycle a refused click walks.
    ///
    /// <para>
    /// The state machine itself is a MonoBehaviour on a runtime-spawned focus camera and needs a
    /// live NetworkManager to say anything about, so what is tested here is the arithmetic it
    /// uses — which is where a silent wrong answer would come from.
    /// </para>
    /// </summary>
    public class PackHandTests
    {
        /// <summary>
        /// Every hotbar slot survives the trip beside every surface. Calls the REAL encode/decode
        /// pair rather than mirroring the formula — a test that reimplements the arithmetic it is
        /// checking passes happily while the shipped code says something else.
        /// </summary>
        [Test]
        public void TheStowWireCarriesBothTheSlotAndTheSurface()
        {
            foreach (int slot in new[] { 0, 1, 3, 9, 255 })
            {
                foreach (PackSurfaceId surface in System.Enum.GetValues(typeof(PackSurfaceId)))
                {
                    int a = BackpackController.EncodeStowTarget(slot, surface);

                    Assert.IsTrue(BackpackController.TryDecodeStowTarget(a, out int back,
                                                                        out PackSurfaceId face),
                                  $"slot {slot} on {surface} must decode at all");

                    Assert.AreEqual(slot, back, $"slot {slot} on {surface}");
                    Assert.AreEqual(surface, face, $"surface {surface} in slot {slot}");
                }
            }
        }

        /// <summary>
        /// The decode is the trust boundary: <c>A</c> arrives from another machine. It must refuse
        /// anything that cannot have come from the encoder rather than resolving to a real slot on
        /// a surface nobody named.
        /// </summary>
        [Test]
        public void AMalformedStowTargetIsRefusedRatherThanGuessedAt()
        {
            Assert.IsFalse(BackpackController.TryDecodeStowTarget(-1, out _, out _),
                           "a negative A is not something the encoder can produce");

            // A surface byte that is not a defined PackSurfaceId. 0xFE is far past the enum.
            Assert.IsFalse(BackpackController.TryDecodeStowTarget(0 | (0xFE << 8), out _, out _),
                           "an undefined surface must not decode");
        }

        /// <summary>
        /// The refused click's answer. Four of them return the item to the turn it started at, so
        /// a player who over-clicks is never stuck with an orientation they cannot get back to.
        /// </summary>
        [Test]
        public void FourRefusedClicksReturnTheItemToItsStartingTurn()
        {
            float yaw = 0f;

            for (int i = 0; i < 4; i++)
                yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + 90f, 360f));

            Assert.AreEqual(0f, yaw);
        }

        [Test]
        public void EachRefusedClickIsExactlyOneQuarterTurn()
        {
            float yaw = 0f;

            foreach (float expected in new[] { 90f, 180f, 270f, 0f })
            {
                yaw = PackGrid.SnapYaw(Mathf.Repeat(yaw + 90f, 360f));
                Assert.AreEqual(expected, yaw);
            }
        }
    }
}
```

- [ ] **Step 3: Run the type-check to verify the new test file compiles**

Run the type-check block.
Expected: clean. (These tests pass immediately — they assert arithmetic that already exists. They
are regression fences on the encoding, not a red-green cycle; the red-green cycle for this feature
was Task 1.)

- [ ] **Step 4: Delete `PackLayout.TryFindNearest`**

In `PackLayout.cs`, delete the whole `TryFindNearest` method and its `<summary>` block, along with
any private helper it alone uses. Check for orphans afterwards:

```bash
grep -n "private.*TryAnchorUv\|TryFindSpot\|TryFindNearest" Assets/Game/Scripts/Items/Backpack/Placement/PackLayout.cs
```

`TryFindSpot` and `TryAnchorUv` are still used (`TryStow`, `AdoptPlacements`, `BackpackObject.AnchorUv`)
— keep both.

- [ ] **Step 5: Delete the ground-drop path**

In `BackpackController.cs`, delete:
- the two wiring lines `this.NetOn(NetMsg.PackDrop, OnDropRequested);` and
  `this.NetOff(NetMsg.PackDrop, OnDropRequested);`
- `RequestDrop` and its `<summary>` block
- `OnDropRequested`

In `BackpackObject.cs`, delete `TryDropToWorld` and its `<summary>` block.

In `NetMessaging.cs`, replace the `PackDrop` block with a retirement note. **The id is not reused**
— ids travel between builds and a reused number routes a message to the wrong handler:

```csharp
        // 77 was PackDrop, retired 2026-08-25: an item dragged clean off the mat left the pack and
        // landed on the ground. The click interaction has no such verb — gear lives in the hotbar
        // or on the pack and there is no third place for it to be — so there is nothing left to
        // send. The number is not reused.
```

- [ ] **Step 6: Delete the magnet's tests**

```bash
git rm Assets/Game/Tests/Editor/PackNearestFitTests.cs Assets/Game/Tests/Editor/PackNearestFitTests.cs.meta
```

- [ ] **Step 7: Rename the drag-named tests in `BackpackNetworkingTests`**

Four tests in `Assets/Game/Editor/Tests/BackpackNetworkingTests.cs` are named for a gesture that no
longer exists. The behaviour they assert is unchanged — a take into a named slot — so only the
names and doc comments move:

```bash
python3 - <<'PY'
import pathlib
p = pathlib.Path('Assets/Game/Editor/Tests/BackpackNetworkingTests.cs')
t = p.read_text()
for a, b in [
    ('ADragOntoAnEmptySlotLandsInThatSlotAndDisturbsNoOther',
     'AClickOntoAnEmptySlotLandsInThatSlotAndDisturbsNoOther'),
    ('ADragOntoAnOccupiedSlotSwapsWithWhatWasInIt',
     'AClickOntoAnOccupiedSlotSwapsWithWhatWasInIt'),
    ('ADragOntoASlotIsIdempotentLikeEveryOtherTake',
     'AClickOntoASlotIsIdempotentLikeEveryOtherTake'),
    ('ADragOntoASlotOutsideTheHotbarChangesNothing',
     'AClickOntoASlotOutsideTheHotbarChangesNothing'),
]:
    t = t.replace(a, b)
p.write_text(t)
PY
grep -n "AClickOnto" Assets/Game/Editor/Tests/BackpackNetworkingTests.cs
```

Expected: four matches. Then read the doc comments above each and reword any that say "dragged" or
"released" to "clicked".

- [ ] **Step 8: Run the type-check**

Run the type-check block.
Expected: no `error CS`. Roughly one fewer source in the Assembly-CSharp-Editor count than before
(`PackNearestFitTests` deleted, `PackHandTests` added — net zero, actually; check the counts print).

- [ ] **Step 9: Run the full EditMode suite**

```bash
rm -f Temp/headless_tests.txt
# trigger Tools/Tests/Run EditMode Tests (headless)
until grep -q DONE Temp/headless_tests.txt 2>/dev/null; do sleep 5; done
cat Temp/headless_tests.txt
```

Expected: `PackHandTests` 4/4 pass. No failure outside the documented baseline of 15.

- [ ] **Step 10: Commit**

```bash
git add -A Assets/Game
git commit -m "refactor(backpack): remove the magnet search and the ground-drop verb"
```

---

## Task 6: Verify it in the game, on a client

The non-negotiables in `CLAUDE.md`: a feature seen working only on the host is not finished, and
one that has not been reloaded is not persisted. Nothing here is optional.

**Files:** none — this is verification.

- [ ] **Step 1: Confirm the Editor actually rebuilt before testing anything**

A stale domain silently runs the old code and reports a plausible false result:

```bash
python3 -c "print(open('Library/ScriptAssemblies/Assembly-CSharp.dll','rb').read().count('Click to put it in slot'.encode('utf-16-le')))"
```

Expected: `1` or more. `0` means the Editor has not compiled the new code — bring it to the
foreground and wait, then re-check. **Do not interpret any behavioural result until this prints
non-zero.**

- [ ] **Step 2: Host-side pass**

Enter play mode, open the pack (B), and walk the whole interaction:

| Check | Expected |
| --- | --- |
| Hover a placed item | Rim lights, label reads "…(click to pick up)" |
| Click it | It lifts into hand; an outline stays on its old cells |
| Move over a free area | Cells under it are **green** |
| Move over placed gear | Cells are **red**, and the item does not jump anywhere |
| Click on red | Item turns 90°; still in hand |
| Scroll the wheel | Item turns, both directions |
| Click on green | It lands exactly on the green cells, at the shown turn |
| With an empty hand, click a hotbar slot | Its item comes into hand; the slot reads reserved |
| Carry it over the bar | The flat icon follows the cursor |
| Click a green spot on the mat | It leaves the hotbar and lands there, at the shown turn |
| Carry a pack item onto an occupied slot | It swaps |
| Click the sand while holding | Nothing happens; still in hand |
| Press Esc while holding | The pack stows; the item is back where it came from |

- [ ] **Step 3: Client-side pass — the one that actually proves it**

Start a host and join with a second client (an MPPM clone or the multiplayer test player). On the
**client**, repeat: lift, rotate, place, hotbar-swap, and lift-from-hotbar-onto-the-pack.

Watch the client's console for `[Net] '<name>' handled message N locally` and
`[WorldService] Prefab 'X' has no NetworkObject` — either means the request never reached the
server.

Then, with both machines in the same pack, have both put an item on the same cell within a second
of each other. Expected: one lands, the other is refused and **its item stays in the hotbar / on its
old cell** — it must not appear somewhere neither player pointed at. That is the whole point of
removing the server's first-fit.

- [ ] **Step 4: Persistence pass**

Place three items at three different rotations (including at least one at 90° and one at 270°),
save, quit to the menu, and reload the world.

Expected: all three come back on the same cells at the same turns. Then confirm the yaws are
actually in the file:

```bash
python3 - <<'PY'
import json, glob, os
newest = max(glob.glob(os.path.expanduser('~/Library/Application Support/*/SpaceGame/**/*.json'), recursive=True), key=os.path.getmtime)
print(newest)
d = open(newest).read()
i = d.find('placements')
print(d[i-40:i+600] if i >= 0 else 'no placements key — the pack saved nothing')
PY
```

Expected: a `placements` array whose entries carry the non-zero yaws just set. If the save path
above finds nothing, locate it from `SaveSlots` instead — the assertion is the same.

- [ ] **Step 5: Run the validation pass and the full suite once more**

```bash
rm -f Temp/headless_tests.txt
# trigger Tools/Tests/Run EditMode Tests (headless)
until grep -q DONE Temp/headless_tests.txt 2>/dev/null; do sleep 5; done
grep -E "PASSED|FAILED" Temp/headless_tests.txt
```

Expected: no failure outside the documented baseline of 15. Compare the total against ground truth
so a truncated run cannot pass for a clean one:

```bash
grep -rho '\[Test\]' Assets/Game | wc -l
```

- [ ] **Step 6: Report honestly**

If any step above did not run — no second machine available, save path not found — say so
explicitly rather than reporting the feature complete. A host-only verification is not a
verification.

---

## Self-review notes

- **Spec coverage.** Hand-empty verbs → Task 4 Step 4/5. Hand-full verbs and green/red → Tasks 2
  and 4 Step 6. Rotate-on-refusal → Task 4 Step 6 (`Turn`). Yaw on the wire → Task 1. No
  auto-placement, both sides → Task 1 (server) and Task 5 (`TryFindNearest`). Ground drop removed →
  Task 5. Renames → Task 3. Keys 1–4 re-pointed → Task 4 Step 5 (`OnHotbarKey`). Hotbar-reordering
  non-goal → Task 4 Step 6 (`PutIntoSlot`). Exit returns the item → Task 4 Step 3
  (`ReturnToOrigin`). Icon-over-bar / proxy-over-mat rule → Task 4 Step 6 and Step 8. Multiplayer
  and persistence verification → Task 6.
- **Naming consistency.** `TryStowFromHotbar(hotbar, slot, surfaceId, uv, yaw)`,
  `RequestStow(slot, surface, uv, yaw, interactor)`, `Show(surface, origin, oriented, legal)`,
  `TryLiftFromSlot(slot, item)`, `PutIntoSlot(slot)`, `IsCarrying`, `ReturnToOrigin()`,
  `SetHeldOrigin`, `ShowCarriedIcon` / `HideCarriedIcon`, `ClearPackFeedback`,
  `BeginCarry` / `MoveCarry` / `EndCarry`, `SetCarryDenied`, `Holds(itemId)` — each is used with
  the same signature everywhere it appears above.
- **Known ordering hazard.** The tree does not compile between Task 1 Step 3 and Task 4 Step 10.
  That is deliberate and bounded: `PackHandController` is the only file carrying the errors, the
  expected error list is written down at Task 1 Step 7, and Task 4 Step 10 is where it must come
  back clean. Do not commit a red type-check as if it were green.
