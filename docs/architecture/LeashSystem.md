# Leash System

A universal "rope between two GameObjects" system for the leash artifact. Lets the
player attach physics-based leashes to anything with a collider, drag objects around,
tie objects to other objects, hold multiple leashes at once, and snap ropes that take
too much force.

---

## 1. Concept

A leash has two endpoints. While the player holds a leash, one end is on a clicked
object and the other end follows the player's hand. The player can:

- **Walk around dragging** the leashed object (rope is slack until pulled taut).
- **Click another object** — but with two distinct rules:
  - Click a **fresh** GameObject → spawns a *new* held leash on that object. The
    player now holds multiple leashes at once.
  - Click an **already-leashed** GameObject → terminates the most recent held
    leash onto it. That leash is now anchored object-to-object and lives in the
    scene independently.
- **Right-click** (or whatever key is bound to the drop action) → disposes the
  most recent held leash entirely.

Multiple leashes per object, multiple leashes between the same two objects, and
attaching to colliders without rigidbodies (static walls, terrain) are all
supported.

---

## 2. Files

| File | Role |
|---|---|
| [Assets/Scripts/Artifacts/Leash/Leash.cs](Assets/Scripts/Artifacts/Leash/Leash.cs) | Runtime `MonoBehaviour`. One instance per active leash. Owns endpoints, runs the spring/damper constraint in `FixedUpdate`, draws the rope in `LateUpdate`, snaps under load, self-disposes. |
| [Assets/Scripts/Artifacts/Leash/LeashAttachable.cs](Assets/Scripts/Artifacts/Leash/LeashAttachable.cs) | Marker added at runtime to any leashed GameObject. Holds a `List<Leash>` and disposes all referencing leashes when the object is destroyed. |
| [Assets/Scripts/Artifacts/Leash/LeashArtifact.cs](Assets/Scripts/Artifacts/Leash/LeashArtifact.cs) | The `ToolItem` the player equips. Routes left-click into "create new" vs "terminate existing", handles right-click drop, and disposes held leashes when the artifact is unequipped. |
| `Assets/Prefabs/Item/Artifacts/LeashArtifact.prefab` *(create in Editor — see §6)* | Held prefab with a muzzle Transform and the `LeashArtifact` script. |
| `Assets/Resources/Items/Artifacts/Leash.asset` *(create in Editor — see §6)* | `InventoryItem` ScriptableObject pointing at the prefab + icon. |

---

## 3. Architecture

```
┌──────────────────────┐
│ Inventory hotbar key │
└──────────┬───────────┘
           ▼
┌──────────────────────┐  spawns the prefab into the player's hand socket
│ EquipmentController  ├─────────────────────────────────────────────┐
└──────────┬───────────┘                                              │
   left-click │                                                       │
           ▼                                                          ▼
┌──────────────────────┐  Use()        ┌──────────────────────────────┐
│  LeashArtifact       │──────────────▶│   "click flow" decision      │
│  (ToolItem)          │               │   ─ fresh target  → create   │
│  - List<Leash>       │               │   ─ already-leashed →        │
│    held              │               │       terminate held end     │
│  - drop input        │               └──────────────────────────────┘
└──────────┬───────────┘
   spawns/owns
           ▼
┌──────────────────────┐  references  ┌─────────────────────────────────┐
│  Leash (MonoBehav.)  │◀─────────────│   LeashAttachable (on target)   │
│  - endpoints A & B   │              │   List<Leash> referencing me    │
│  - LineRenderer      │              │   OnDestroy → dispose them all  │
│  - constraint        │              └─────────────────────────────────┘
│  - render            │
│  - snap detection    │
└──────────────────────┘
```

Each `Leash` is a standalone scene GameObject. When the artifact is unequipped,
**held** leashes (still in the artifact's `_heldLeashes` list) are disposed, but
**anchored** leashes (already terminated onto two world objects) survive.

---

## 4. Endpoint Model

A leash endpoint is one of three kinds:

| Kind | When | Constraint resolution |
|---|---|---|
| `PlayerHand` | The held end while the player has a leash in hand. Tracks the muzzle Transform on the artifact prefab. | A separately stored "reaction" Rigidbody (the player body) absorbs the constraint — via force if non-kinematic, via `MovePosition` if kinematic. |
| `Object` | World object that has a Rigidbody (NPC, prop, vehicle). | `AddForce` if non-kinematic, `MovePosition` if kinematic. |
| `Static` | World object with only a Collider (wall, terrain). | Immovable anchor — does not move. |

Each endpoint resolves *independently*, so any mix is supported: kinematic NPC ↔
non-kinematic player, two kinematic NPCs, kinematic NPC ↔ static wall, etc. See
§5 for the math.

When the player terminates a held leash onto a target, the `PlayerHand` end gets
swapped for an `Object` or `Static` end via `Leash.TerminateHandEndOnto`.

---

## 5. Constraint Math

Run every `FixedUpdate` per leash:

```
delta     = posB - posA
dist      = |delta|
if dist <= maxLength:    rope is slack → no force, no correction
n         = delta / dist                       // unit vector A → B
overshoot = dist - maxLength
vRel      = (velB - velA) · n                  // positive = separating
forceMag  = stiffness * overshoot + damping * max(0, vRel)
if forceMag > breakForce: SNAP                  // rope tension exceeded threshold

mobileSides = #endpoints with any rigidbody (primary or reaction)
positionStep = overshoot / mobileSides          // shared correction budget

resolveEndpoint(A, +forceMag*n, +n, positionStep)
resolveEndpoint(B, -forceMag*n, -n, positionStep)

// resolveEndpoint:
//   if rb is non-kinematic → AddForce(forceTowardOther)        // mass-aware physics
//   else if rb is kinematic → MovePosition(rb.pos + unit*step) // hard correction
//   else (no rb)            → no-op (static anchor)
```

This **dual-mode** resolution lets every mix of endpoint types behave correctly:

| A end | B end | Behavior |
|---|---|---|
| non-kin | non-kin | Equal & opposite force; mass-weighted natural physics. |
| non-kin | kinematic | Force on A pulls it toward B; B snaps half the overshoot toward A. |
| non-kin | static | Force on A only; B is anchored. |
| kinematic | kinematic | Each snaps half the overshoot toward the other. |
| kinematic | static | A snaps the full overshoot toward B; B is anchored. |
| static | static | No motion (purely visual). |

Damping is one-sided (`max(0, vRel)`) so the rope only resists separation, not
compression: a rope can pull, not push. The rope is fully slack below
`maxLength` — endpoints move with zero interference from the constraint until
it goes taut.

`forceMag` represents rope tension and is checked against `breakForce`
*regardless* of how each side resolves — even a kinematic-only constraint
will snap if the implied tension is too high.

---

## 6. Unity Editor Setup

### 6.1 Create the prefab

1. In the Hierarchy, **create an empty GameObject** named `LeashArtifact`.
2. Add the **`LeashArtifact`** component (drag from `Assets/Scripts/Artifacts/Leash/LeashArtifact.cs`).
3. Inside it, add a child empty named `Muzzle` — position it at the spot the rope
   should visually start (e.g. just in front of the player's hand). Drag this
   into the `Muzzle` field on the `LeashArtifact` component.
4. *(Optional)* Add a child mesh as the visible held leash (a coiled rope model,
   or just a primitive). Children of the artifact that have colliders should be
   on a layer NOT included in `Leashable Layers`, otherwise the player will
   target their own leash. Easiest: remove colliders from these children.
5. Drag the GameObject into `Assets/Prefabs/Item/Artifacts/LeashArtifact.prefab`.
6. Delete the scene instance.

### 6.2 Configure inspector fields on `LeashArtifact`

| Field | Suggested value | Notes |
|---|---|---|
| `Max Range` | 30 | Click range. |
| `Leashable Layers` | "Default" + any NPC/prop layer | Must NOT include the player's layer or the artifact's children. |
| `Max Leash Length` | 8 | Hard cap on rope length. |
| `Stiffness` | 400 | Spring force per metre of overshoot. |
| `Damping` | 30 | Resists rapid separation. |
| `Break Force` | 1500 | Newtons. Tune relative to the masses of likely targets. |
| `Rope Material` | A simple unlit material | Required — without it the LineRenderer renders magenta/invisible. URP: use an unlit/colored material. Built-in: any standard material works. |
| `Rope Color` | Tan (`0.6, 0.5, 0.35`) | LineRenderer tint. |
| `Rope Width` | 0.04 | World units. |
| `Rope Segments` | 18 | More = smoother sag. |
| `Rope Sag` | 0.6 | Max droop in world units when rope is fully slack. |
| `Muzzle` | The child Transform from §6.1 step 3 | Visual rope start. |
| `Drop Action` | An `InputActionReference` bound to e.g. RightClick | If unassigned, dropping is impossible — the player must unequip the artifact, which also disposes held leashes. |

### 6.3 Create the inventory ScriptableObject

1. In `Assets/Resources/Items/Artifacts/`, right-click → `Create → Items → Item`.
2. Name it `Leash`.
3. Set:
   - `Item Name` = "Leash"
   - `Item Prefab` = the prefab from §6.1
   - `Icon` = a sprite. Placeholder: reuse `Assets/Sprites/Items/RocketArtifact.png`.
4. Make sure the asset is loaded by the inventory registry the same way the
   other artifacts (`RocketTurret.asset`, `Lasso.asset`) are loaded. They live
   under `Resources/` so `Resources.Load`-style registry lookups will see it.

### 6.4 Drop input

Bind whatever button you want to drop held leashes. The cleanest path:

1. Open your project's Input Actions asset (the same one Lasso's `reelInAction`
   references — typically `Assets/Inputs/PlayerInput.inputactions` or similar).
2. Add an action like `LeashDrop` bound to `<Mouse>/rightButton`.
3. In the Project window, the action will show up as a child asset; drag it
   into `Drop Action` on `LeashArtifact`.

The artifact enables the action in `OnEnable` and disables it in `OnDisable`,
so it's only active while the artifact is equipped.

---

## 7. Click Flow (state machine)

```
                              ┌──────────────┐
                              │ player click │
                              └──────┬───────┘
                                     ▼
                  ┌───────────── raycast hit valid? ─────────────┐
                  │ no                                          yes │
                  ▼                                                 ▼
              do nothing                                target = root GO
                                                                ┌───┴───┐
                                                          target == player?
                                                            ┌───┴───┐
                                                          yes      no
                                                          │         │
                                                       ignore       ▼
                                                              already leashed?
                                                              ┌────┴────┐
                                                            yes          no
                                                            │             │
                                                  any held leashes?     create new
                                                    ┌────┴────┐         held leash
                                                  yes          no       (add to list)
                                                  │             │
                                       held leash already        create new
                                       references this object?  held leash
                                          ┌─────┴─────┐
                                        yes           no
                                        │              │
                                     ignore       terminate held leash
                                     (no-op)      onto target (pop from list)
```

This means: to **pair two fresh objects**, click each once (both become
held leashes), then click one of them again — the most recent held leash
terminates onto it, leaving the other still in your hand. Click the other
one to terminate the second leash.

(This matches the spec: clicking a fresh object never auto-pairs; pairing
only happens on clicks against already-leashed objects.)

---

## 8. Lifecycle Rules

| Event | What happens |
|---|---|
| Click fresh object | New `Leash` GameObject created; A on object, B in player's hand. Added to artifact's `_heldLeashes`. |
| Click already-leashed object (holding ≥1 leash) | Most recent held leash's hand-end is reconfigured onto the target. Leash leaves `_heldLeashes` and lives independently. |
| Right-click (drop) | Most recent held leash is disposed (GameObject destroyed, removed from both attachables). |
| Artifact unequipped / destroyed | All **held** leashes are disposed. Anchored leashes survive. |
| Leashed object is destroyed | Its `LeashAttachable.OnDestroy` disposes every leash referencing it; the other endpoint's attachable is cleaned up via the leash's own dispose. |
| Force per `FixedUpdate` exceeds `breakForce` | Leash snaps → disposed. |
| Both endpoints land within `maxLength` distance | Rope hangs slack with sag; no physics interference. |

---

## 9. Caveats and Tuning

### 9.1 Kinematic vs non-kinematic targets

Both are supported. Each endpoint independently picks its resolution mechanism:

- **Non-kinematic Rigidbody** → `AddForce`. Mass-aware. Natural inertia. The
  feel is "rope yanks heavy things less." Best for free physics props.
- **Kinematic Rigidbody** → `Rigidbody.MovePosition`. Hard correction toward
  the other endpoint. No inertia. Compatible with `NavMeshAgent` (the agent
  re-pathfinds from the new position next frame) and with player controllers
  built on a kinematic Rigidbody. Best for AI-driven NPCs.

Mixed pairings (kinematic ↔ non-kinematic) work — see the table in §5.

**Kinematic damping caveat.** The damping term in `forceMag` is applied via the
force path, which kinematic bodies ignore. The position-correction path uses
the raw overshoot, no velocity damping. In practice this is rarely visible:
`MovePosition` on a kinematic body produces large effective velocities that
the constraint resolves in 1–2 FixedUpdate steps, so there's no oscillation
to damp. If you do see jitter on a kinematic-kinematic pairing, lower
`stiffness` indirectly by lowering `maxLength` margin, or split the
correction over multiple frames by clamping `positionStep` (would require a
small code tweak).

**NavMeshAgent compatibility.** With kinematic Rb + `NavMeshAgent`, the leash
calls `MovePosition` while the agent calls `agent.Move` / sets `nextPosition`.
Unity reconciles these per FixedUpdate. The agent will continue trying to walk
to its destination but the leash will pull it back when taut, producing the
expected tug-of-war feel. If you see fighting or stuttering, set
`agent.updatePosition = false` and route agent velocity through your own
movement code so you control resolution order.

### 9.2 Player reaction force

The leash applies the constraint to whatever Rigidbody it finds via
`owner.GetComponentInParent<Rigidbody>()`. Both kinematic and non-kinematic
players are now towed correctly (kinematic uses `MovePosition`, non-kinematic
uses `AddForce`). If your player uses a `CharacterController` with no
Rigidbody at all, the player is treated as a static anchor — held leashes
won't tug the player, only the held target. Add a Rigidbody (kinematic is
fine) to the player root if you want the tug.

### 9.3 Tuning `stiffness` / `breakForce`

Stiffness too high + heavy targets = jitter or explosive forces. Start with
the suggested values, then:

- If rope feels stretchy beyond `maxLength`: raise `stiffness`.
- If everything snaps too easily: raise `breakForce`.
- If targets oscillate at the boundary: raise `damping`.
- If target gets slingshotted on snap: lower `stiffness`, or raise `damping`.

### 9.4 LineRenderer material

`LineRenderer` requires a material. Without one assigned, you'll see invisible
or magenta lines. Use a simple unlit/standard material. URP projects need a
URP-compatible shader.

### 9.5 Self-leashing prevention

The artifact filters out clicks that:
- Hit a collider that is a child of `owner` (the player root).
- Resolve to a target whose root GameObject *is* the player.
- Target an object whose `LeashAttachable` already contains the held leash
  being terminated (would create a self-loop).

If you have unusual rigging (player hand colliders living outside the player
hierarchy), adjust the layer mask to exclude them.

---

## 10. Public API Cheatsheet

```csharp
// LeashAttachable
public bool HasLeashes;
public List<Leash> leashes;
public static LeashAttachable GetOrAdd(GameObject go);
public void AddLeash(Leash l);
public void RemoveLeash(Leash l);

// Leash
public bool IsHeld;
public Vector3 EndAPos;
public Vector3 EndBPos;
public bool ReferencesObject(GameObject go);
public void ConfigureEndpointA_OnObject(GameObject targetRoot, Vector3 worldHitPoint);
public void ConfigureEndpointB_OnObject(GameObject targetRoot, Vector3 worldHitPoint);
public void ConfigureEndpointB_OnPlayerHand(Transform muzzle, Rigidbody playerBody);
public void TerminateHandEndOnto(GameObject targetRoot, Vector3 worldHitPoint);
public void Snap();
public void Dispose();
```

Other systems that want to react to leashing (e.g. NPC AI noticing it's been
leashed and resisting) can `GetComponent<LeashAttachable>()` and inspect
`leashes`.

---

## 11. Future Extensions (not implemented)

- **Rope segments with intermediate physics** — current rope is a single
  spring at max length; for "rope wrapping around a corner" behaviour, a
  segmented Verlet rope would be needed.
- **Snap SFX/VFX** — `Leash.Snap()` is the hook point. Currently it just
  calls `Dispose`. Add particles or an FMOD `EventReference` here.
- **Net-syncing** — leashes are entirely local right now; multiplayer would
  need state sync on creation, endpoint changes, and dispose.
- **Visual snap effect** — fade the LineRenderer over a few frames before
  destroying the GameObject for a cleaner-feeling break.
