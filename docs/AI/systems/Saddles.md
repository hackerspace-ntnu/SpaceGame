---
system: Saddles
layer: items
summary: "Fitting a saddle to an animal: what makes it rideable, what it carries, and what happens when it comes off"
paths:
  - Assets/Game/Scripts/agents/Modules/Riding/SaddleSocket.cs
  - Assets/Game/Scripts/agents/Modules/Riding/SaddleRemover.cs
  - Assets/Game/Scripts/Items/Artifacts/Gadgets/SaddleArtifact.cs
  - Assets/Game/Editor/Creatures/SaddleBuilder.cs
  - "Assets/Game/Art/Models/_Source~/models/gear"
symptoms:
  - "the animal cannot be ridden, or can be ridden with no saddle on it"
  - "gear stowed on an animal vanished when the saddle came off"
  - "the saddle arrives a hundred times too big, or floating beside the animal"
  - "a saddled animal comes back bare after a save and reload"
  - "using the saddle saddles whatever the host is looking at"
  - "the rider is sunk into the animal up to the chest, or stands on top of the saddle"
  - "a saddled animal can be ridden but not driven — the rider has no control"
  - "the saddle's straps, skirts or stirrups are sunk inside the animal"
  - "the saddle stands off the animal like a shelf instead of lying on it"
  - "stirrup irons or buckles float on the flank with nothing joining them to a strap"
  - "looking at the saddle always offers to ride, never to take it off"
  - "the saddle is the right size for the world but too small for the animal wearing it"
reads_with: [AgentSystem, Backpack, Artifacts, Vehicles]
updated: 2026-09-05
---

# Saddles

A saddle turns an animal into something you can ride and something that carries gear. It is an assembly of three systems rather than a new one, which is why it has its own page: no single one of them owns it.

**Scope:** `SaddleSocket` / `SaddleRemover` ([Riding/](Assets/Game/Scripts/agents/Modules/Riding)), [`SaddleArtifact`](Assets/Game/Scripts/Items/Artifacts/Gadgets/SaddleArtifact.cs), [`SaddleBuilder`](Assets/Game/Editor/Creatures/SaddleBuilder.cs), and the model under `_Source~/models/gear/`.
**Related:** [AgentSystem.md](AgentSystem.md) (the animal), [Backpack.md](Backpack.md) (the container), [Artifacts.md](Artifacts.md) (the item), [Vehicles.md](Vehicles.md) (`MountModule`).

## Model

Three pieces, each living where it belongs:

| Piece | Lives on | Is |
| --- | --- | --- |
| `SaddleSocket` | the **animal** | Whether a saddle is on. The only replicated state — one bool. |
| `AppaSaddle.prefab` | instantiated onto a bone | The visual, the `PackContainer`, the removal trigger. |
| `SaddleArtifact` | the **item** | Aimed use that asks a socket to fit one, and is spent when it does. |
| `SteerModule` | the **animal** | Rider input to the motor. Always on; it self-gates on `IsMounted`. |
| `SaddleQuickRelease` | the **animal** | `Q` while standing beside it. Same `Request(false)` as the grips. |

- **The saddle is a plain `Instantiate`, not a spawned `NetworkObject`** — the same call `BackpackController` makes for the pack. A `NetworkObject` parented into a bone hierarchy needs that reparenting replicated and re-applied after every spawn and every load, and what is actually replicated here is one bool. `NetMsg.SaddleFit` (100) asks the server, `SaddleSet` (101) tells everyone, and each machine builds its own copy in `ApplySaddled`.
- **Riding and steering are two components.** `MountModule` puts a body in the seat; `SteerModule` is what makes the animal go. A mount with only the first is a chair — you climb on and it carries on with whatever its AI was doing. `SteerModule` needs a motor that implements `IRiderControllable`, which `NavMeshAgentMotor` does, so on a NavMesh creature it is one component and no other work.
- **"You cannot ride it bareback" is the `MountModule` being disabled.** `AppaBuilder` adds it with `enabled = false`; the socket turns it on with the saddle. A disabled `Behaviour` is one the `Interactor` skips outright, so a bare animal offers no verb at all rather than one that appears and refuses.
- **Fitting and removing are deliberately different verbs.** The item fits. The saddle carries `SaddleRemover` — a trigger with its own `IInteractable`, so looking at the saddle offers "take saddle off" while the animal's solid collider goes on offering "ride". You need no saddle in hand to remove one, and a removed saddle has to go somewhere anyway.
- **The faces are ordinary `PackSurface`s.** `WallInventory` is reused verbatim: it is documented as "a `PackContainer` bolted to something, with no fold, no deploy and no owner", which is a saddle exactly. Three faces, `SaddleLeft`/`SaddleRight`/`SaddleRear` (ids 8–10), 42 cells against the expedition rig's 255.

## Flows

**The loop conserves.** Fitting spends the item; removing returns it through `SpillAndReturn`. So
there is exactly one saddle at any moment — in a pack, or on an animal — and no place/remove cycle
multiplies it. `SaddleSocket.Fit()` returns whether the saddle actually went on, and only then does
the artifact call `Deplete()`: a click at an animal already wearing one must not eat the saddle.
That answer cannot come back through `Request` (a message has no return value), which is why an
already-server caller asks directly.

**Fitting.** `SaddleArtifact.OnRequestUse` raycasts on the holder's machine, resolves a `SaddleSocket` with `GetComponentInParent` and puts it in the `NetArg` — the aim is read there because the server's `Camera.main` is the *host's* camera. `Use()` (server) resolves it and calls `Request(true)`. The server flips the bool and broadcasts; every machine instantiates.

**Removing.** Two ways in, one decision: `SaddleRemover.Interact` (E, aimed at a grip) and
`SaddleQuickRelease` (Q, standing near) both call `Request(false)`, so "is there a saddle" and
"is someone in the seat" are answered once, on the server. Q exists because aiming at a strap
three metres up and a metre out is real work for something you do constantly.

`SaddleRemover.Interact` → `Request(false)` → the server runs `SpillAndReturn` **before** flipping the bool, so the container still exists to be emptied.

**Spilling.** `SpillAndReturn` walks the container's placements, `TakeOut`s each and spawns it in a ring around the animal, then spawns the saddle item itself. Gear stowed on an animal is not a second inventory to lose track of — it is on a thing that walks away, so removal must put everything back within reach. The ids are copied into a list first, because `TakeOut` mutates the layout being walked.

## Multiplayer

Two ids, both on the **animal's** relay: the saddle has no channel of its own. `OnFitRequested` is gated on `Network.Owns(this)`. `ApplySaddled` runs everywhere and is idempotent, so the broadcast, `Awake` and the save restore can all call it.

## Persistence

`SaddleSaveable` stores the bool and nothing else. It cannot be recomputed — the saddle is not a spawned entity, so a load finds nothing in the world to infer it from, and without it every saddled animal reloads bare, un-rideable, and having silently eaten its cargo. The **contents** belong to the container's own saver, which is right: that instance does not exist until this one has restored.

## Gotchas

- **A collider or prefab parented to a bone is 100x too big.** Every transform in an imported FBX carries `lossyScale` 100 (the centimetre convention), bones included. `compensateBoneScale` divides it out. The same trap ate the pet target.
- **Divide out the bone's scale, not the animal's.** The bone carries the centimetre factor *times* whatever the creature's root is scaled to, so cancelling the whole thing pins the saddle to world scale: on an Appa built at 1.5x it would sit on his back at two thirds size. `ApplySaddled` divides the animal's own scale back in, leaving the saddle in his scale. Anything else authored onto a bone wants the same treatment.
- **Scaling a creature is one number on the root, and a short list of exceptions.** Colliders, bones, the saddle and both mount offsets are authored in the creature's own space and follow it for free. What does not: `NavMeshAgent` radius/height/speed/stopping distance, `mountedJumpHeight`, the pack's spill radius, and any reach or eye height held as a bare float. Angles — turn rates, clip sweeps — are scale-free and must be left alone.
- **Author the offset in the ANIMAL ROOT's space, not the bone's**, and apply it as a *world* position after parenting. A bone's axes are whatever the rig export left them as, and an offset expressed in them can be derived from nothing; the root-space figure is read straight off the model — Appa's `(0, 2.089, -0.18)` is `appa.blend (1.62, 0, 0.329)` through `appa_export.py`'s mapping `(x,y,z) -> (-y, z+1.76, -(x-1.44))`.
- **A saddle with a rider on it will not come off**, from either path. The seat would vanish mid-ride and `MountModule` would be disabled while it still held a player.
- **Spill before you clear.** `SpillAndReturn` runs while `saddled` is still true, because it reads the live container. Flipping the flag first destroys the instance and the cargo with it.
- **`MountModule.seatOffset` is measured to the rider's FEET, but the chair convention does not survive an animal.** Vehicle seats drop the rider a whole leg (`NpcPassenger` defaults to -0.85 m) so a standing pose lines its pelvis up with the cushion — nothing is under a chair. A metre of barrel *is* under a saddle, so the same drop buries the rider in the animal to the chest. Nothing plays a straddle pose, so the choice is between a rider inside the animal and one sitting high on it: Appa uses `SeatRise - 0.10`, just under the seat's own surface (0.15 m once his 1.5x scale is applied, since the offset rides in his space).
- **A grip on the animal's centreline can never be reached.** `Interactor` resolves the nearest
  thing the ray hits and a body collider is solid, so a ray aimed at anything inside the torso box
  answers with the animal's own `MountModule` — "ride" — and the saddle's verb never appears. Appa's
  box reaches x ±1.26 m and 3.45 m up; a grip at the seat (centreline, 3.50 m) was blocked from
  every standing position. The grips that work sit **outboard of the body**, on the saddle's side
  furniture, which is where a person reaches to unstrap one anyway. Put one on each side.
- **Wrap the leather onto the animal; do not compute where it should go.** Two offset schemes were
  tried and both failed visibly — a fixed drop from the panel's edge buried everything, and pushing
  parts outboard of the widest point made them stand off him like shelves. What works is building
  each strap flat and projecting its vertices sideways onto a measured surface (`wrap` in
  `saddle.py`), which keeps the part's own shape and gives it his curve.
- **A fitting must be read off the wrapped geometry, never recomputed.** Once leather has been
  projected it is no longer where the arithmetic that placed it said it would be, so a stirrup iron
  or buckle positioned from a second, independent lookup ends up floating on the flank with nothing
  joining the two. `hanging_end` returns where a strap actually ended up.
- **Fitting the top of an animal is not fitting its sides.** A downward raycast measures only the
  back; everything that hangs — skirts, girth, panniers, stirrups — needs a *horizontal* measurement
  of the flank, because a barrel is far wider below the spine than at it. Appa goes from 0.66 m
  half-width at the spine to 1.12 m half a metre down, so parts hung a fixed distance from the
  panel's edge were up to 0.9 m inside him. `saddle.py` carries a `SIDE` table and `flank_out`
  beside `BACK` and `back_height` for this.
- **Place a hanging part against the widest point it spans, not the width at its top.** On a barrel
  a strap set flush at its upper edge is buried by its lower one.
- **The pannier boards are modelled horizontal**, not flush to the flank — a `PackSurface` maps a uv onto a plane, and a board following the barrel would slope every item.

## Extending

**A saddle for another animal** — 1) Add a collection to `saddle.blend` fitted to that animal's measured back (`Coll_Saddle_Pack` is a started cargo pad). 2) Export it. 3) Copy `SaddleBuilder.BuildWorn` with the new FBX and face sizes; append new `PackSurfaceId` values and **never renumber** — they are persisted bytes and travel on the wire. 4) In that creature's builder add a disabled `MountModule`, a `SteerModule` pointing at it, a `SaddleSocket` pointing at the new prefab, and `SaddleSaveable`. Turn `jumpEnabled`/`leapEnabled` off unless that creature's controller actually plays them — the motor will happily leap 8 m in a walk cycle. 5) Verify it survives a reload and appears on a joining client.
