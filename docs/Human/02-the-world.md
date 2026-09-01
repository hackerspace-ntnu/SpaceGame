# The World

The planet is four kilometres by three, and you can walk from one corner of it to the other without a
loading screen. That is a claim the game has to keep paying for, every second, in the background:
terrain arriving ahead of you and leaving behind you, a navigation mesh that was baked once and has to
still be true, weather that every machine invents independently from a shared clock, caves and
buildings that live in their own scenes, and a small number of holes in reality you sprayed onto a
wall yourself. This document is about how the world is put together, and what happens at each of the
seams.

---

## The shape of the planet

The main world is a grid: **8 by 6 tiles of 500 × 500 metres each — 4000 × 3000 metres in total.**
Each tile is its own scene file on disk. The team calls them **chunks**.

Not all of it is world. Twelve of the forty-eight chunks are authored empty on purpose — the two
westernmost columns, the first kilometre of the map — so the playable ground is closer to 3000 × 3000
metres with a padded edge. The empty chunks are explicitly flagged as "there is no terrain here, and
there never will be", which matters because otherwise the safety systems would sit there patiently
waiting for ground that nobody owes them.

There is a second, smaller world in the project — a 4 × 2 test grid — but nothing in the menus reaches
it. It exists only if you open its scene in the editor.

---

## Streaming: how a big world stays loadable

One system owns all chunk loading, and it lives in the persistent scene that never unloads. Nothing
else may load or unload a chunk.

### Anchors pull terrain in

Chunks are not loaded on a schedule; they are *required* by **anchors**. An anchor is anything that
needs ground under it: every connected player, plus anything explicitly registered as needing its
surroundings kept alive. Each anchor demands a square of chunks around itself, and a *second* square
around where it is predicted to be a couple of seconds from now based on its current velocity. Drive
fast and the terrain is already there when you arrive.

Everything else gets unloaded — but not immediately. A chunk nobody needs sits on a **ten-second grace
timer** first, so walking back and forth over a boundary does not thrash the disk. And a chunk that
still has a tracked entity standing in it will not unload at all; that entity is an anchor with a
radius of zero.

If you leave the grid entirely, the nearest edge chunk stays loaded for up to two kilometres out.
Beyond that (the deathmatch arena sits about sixteen and a half kilometres east) you hold nothing.

### Loading is a single-file queue

Netcode permits exactly one scene event at a time, so every load and unload goes through one
sequential queue, ticked twice a second. If the network layer says "busy", the *same* operation is
re-queued rather than skipped — that busy signal is normal traffic, not an error, and it is a
recurring temptation to "fix" it by advancing the queue, which breaks everything.

### Loaded is not the same as built

This is the distinction that catches people. A chunk scene finishing its load does not mean the world
is standing up in it. Terrain features defer building their meshes and their physics colliders into a
separate work queue with a **two-millisecond-per-frame budget**, because cooking collision meshes all
at once is a visible hitch. So the system tracks two different readinesses: the scene has arrived, and
the scene's contents have finished building. Player spawns wait on the second one. So does the loading
screen.

### Only the server streams

Clients never run any of this. On a client the chunk bookkeeping is permanently empty, and scenes
simply arrive from the server through the networking layer, asynchronously and later than the
server's own. There is a real window in which a client's player exists and the ground under it does
not — which is why there is an owner-side failsafe that holds a body still while it is waiting for
terrain it has been promised, rather than letting it fall forever.

### Things move between chunks

An entity standing near a boundary eventually walks over one. The streamer re-homes moving entities
into the chunk scene they are actually standing on, twice a second, and announces the move to every
client. This is opt-in per entity, and it is the same declaration as "this thing must survive a save"
— because both facts are consequences of the same thing: it *moves*, so you cannot address it by
where it was authored.

That is the reason saved state in this project is keyed by an object's identity and never by the scene
it lives in.

---

## What the ground is made of

### The base terrain is authored, not generated

There is no procedural heightmap. Somebody sculpted the desert as one large terrain, and an editor
tool sliced it into the 48 chunk scenes. Every generator described below *adds* geometry on top of
that sculpted ground.

### Terrain features: mesas and cliffs

A designer drops a feature marker, drags out a footprint polygon in the scene view, and picks a type.
The tool builds a density field over that footprint, marches a surface out of it (2-metre voxels),
blends its base into the terrain with a skirt so there is no floating seam, and then **bakes the
result to a mesh asset**. At runtime nothing generates; the baked mesh is simply instantiated.

There are exactly two feature types left: **Mesa** (a plateau) and **Cliff** (an escarpment step). An
earlier generation had a dozen more — arching caves, badlands mazes, boulder fields, dunes, spline
paths — and they are all gone. Anything you read that mentions them is stale.

Two shapes, one seed, and a lot of tuning dials: silhouette noise, jaggedness, height variation, seven
surface-detail knobs, and a "keep it walkable" constraint with a maximum slope. Features can optionally
produce true overhangs instead of a heightfield surface, at meshing cost.

Everything is a pure function of one integer seed, so two machines with the same scene get the same
rock.

### Caves

Caves are generated as a graph, not a shape. A seeded random walk lays out rooms and corridors, those
become spheres and capsules in a signed distance field, the field is smooth-unioned and noised and the
floors flattened, and the whole thing is marched into a mesh. Both the mesh and the cave's navigation
data are baked to assets.

What is *not* baked runs when the cave spawns: liquid pools found by flood-filling the low points,
cluster lighting, the entrance tube and its cover, and a scatter of decoration props placed onto the
actual triangle surface. All of it is seeded off the same number, so every machine decorates the cave
identically without a single network message.

If a cave is missing its bakes, it will generate live on load — several seconds of stall, and a
silently different result if the seed was rolled at random.

### Settlements

Settlements are tile-based. A seeded height-map of grid cells becomes block roles, which become floors,
terrace edges, roofs, corner pillars, interior slabs and stairs, and then a detail pass adds walls,
arches, colonnades, obelisks and clutter from a kit of roughly twenty-five prefab variant families.

Unlike the other two, settlements are **edit-time only**. The generator runs in the editor and its
output is plain GameObjects committed into the chunk scene. Nothing regenerates a settlement at
runtime — which is deliberate, because that generator is the one that uses a global random and would
not agree across machines.

### Sites

Layered on top of all this is a small hand-placed thing: **site markers**. A designer drops one, gives
it a kind (there are eight) and a radius, and NPCs can then ask "where is the nearest / a random site
of this kind". That is how caravans decide where to walk. Sites are authored, never generated, and
their identifiers end up inside NPC save data — so moving a marker around in the hierarchy orphans
every saved reference to it.

---

## Navigation: one mesh for the whole planet

Every walking creature in the game paths on **a single navigation mesh baked over the entire world at
once.** There are no per-chunk tiles and therefore no seams to stitch. At runtime the mesh is simply
handed to the navigation system in one call; nothing bakes, nothing rebuilds, nothing recalculates.

The bake is an editor operation that opens all 48 chunk scenes simultaneously, aligns each terrain,
spawns the baked terrain features so their geometry participates, collects the collision (not the
render meshes — colliders and terrain, skipping anything on a moving physics body), and builds one
data blob. The result is around 36 MB and is committed to the repository.

Practical consequences of that design:

- **A change to any chunk invalidates the whole bake.** There is no partial re-bake.
- **A stale bake is invisible in the editor.** A build will refuse to compile with one, but in the
  editor NPCs will happily navigate a world that no longer exists. There is a menu item that answers
  "is the navigation mesh current".
- **There is exactly one agent size** configured project-wide: half a metre wide, two metres tall,
  climbing 0.8 m steps and up to 60° slopes. Every creature in the game — regardless of how many legs
  it has — paths as that shape.
- **There are no jump links anywhere.** Nothing can navigate across a gap. Leaps and hops exist, but
  they are movement behaviours a creature performs, not routes the navigation system knows about.
- **Caves are separate.** The interior layer is excluded from the world bake, and each cave brings its
  own navigation data. A cave's mesh never merges with the world's.

---

## Weather

### Sandstorms are about thirty bytes

This is the nicest trick in the environment layer. A live sandstorm is a **single ~30-byte record**:
an id, which profile it is, a seed, an origin, a bearing, a start time and a duration. It is written
once when the storm is born and never touched again.

Everything else is *derived*. Where the storm is right now, how hard it is blowing, how far you can
see, how much of the screen it eats, what it sounds like, how much damage it does, and how badly it
blinds the AI — all of it is recomputed every frame from that record plus one shared clock reading.
There is no per-frame weather traffic in this game at all. A player who joins an hour into a session
receives the list of records and the clock anchor and lands in exactly the same weather everyone else
is standing in.

One shape function decides both the damage and the pixels. The C# version and the shader version have
to stay identical, and that is exactly the point of writing it as one function in the first place: the
wall you can see is the wall that hurts you.

You can get out of a storm. Shelters are simple boxes with doors; protective gear reduces exposure by a
single factor. Exposure is density × (1 − shelter) × (1 − protection), and only the server ticks the
damage.

### Day and night

The sun runs on its **own** clock anchor, deliberately separate from the weather clock, so that
restoring a saved storm cannot yank the sun across the sky. The sky itself is a gradient skybox with
painted dust bands that stand down when volumetric clouds are covering that part of the sky.

### Fog and clouds

Volumetric fog is authored, not simulated: a designer drops a volume (ellipsoid, box, cylinder or
ground layer), sets its look and its drift, and that is the whole workflow. There is no runtime state
and nothing to save or replicate.

Rendering them is where the engineering is. The **eight nearest** volumes and eight nearest lights are
marched in a *single* pass so that overlapping fog mixes correctly instead of stacking, at reduced
resolution, then composited back up with a depth-aware filter. Clouds are the same machinery over a
spherical shell above the world. The whole thing costs nothing when there is nothing in view — each
effect bails out early if it has no volumes, no storm density, or no cloud layer.

Two honest notes: the sandstorm shader still dithers against the screen resolution rather than its own
(half-resolution) render target, which is a stippling pattern the fog and cloud passes have already
been fixed for; and the PC renderer configuration carries two dead render-feature entries pointing at
scripts that do not exist anywhere in the project.

---

## Interiors

Caves and other enclosed spaces are **additive scenes loaded beside the world**, not instead of it.
The exterior never unloads while you are inside, which buys two things: coming back out is instant,
and everything you left standing outside stays alive and simulated.

A doorway is assembled from three independent parts, which is why adding a new kind of transition is
usually one new file:

- a **trigger** — press E on something, or walk into a volume;
- a **destination** — enter this interior, exit back out, or teleport to an anchor in the same scene;
- and a list of **effects** — fade to black, or a scripted walk-through camera move.

The server decides everything about the transition (which scenes are loaded, which scene an object
lives in, where a body ends up); the effects are one player's own screen and run only on their
machine. When you walk out, the game waits for the chunk you are returning to to be loaded again
before it moves you, with a timeout rather than an assumption.

There are two authored interiors today: an algae cave and a sandstone cave.

Three known rough edges, worth knowing before you design around interiors:

- Interiors load **at the world origin**, overlapping whatever exterior chunk sits at (0, 0, 0). It is
  harmless right now, and it is not by design.
- A player who joins late is **not** placed into an interior the others are already inside.
- Items dropped inside an interior are **lost when the last person leaves**, unless the object is
  specifically save-wired.

---

## Portals

You can spray holes in walls.

The **portal spray can** fires a jet of paint along a ballistic arc. Where it lands, the paint *is* the
aperture — the opening is the union of up to twenty-four overlapping circles, and the shader draws
exactly the same field the physics reads. If you can see a lobe, you can walk through that lobe.
Keeping spraying on your own paint and the hole grows; spray somewhere else and you place the other
end of the pair. Each player carries their own two barrels, and an aperture lives about twenty
seconds.

A few decisions shape how portals feel:

- **A portal is a door, not a window.** There used to be a second camera rendering the far side into
  the surface; it is gone, and the surface is now a stylised swirl. That means nothing has to agree
  with a rendered image, and the crossing test answers to physics alone.
- **The shape never travels over the network.** Each spray tick sends one point and a verdict, and
  every machine replays the same gesture and arrives at the same hole. Portals are consequently one of
  the few systems that need no network registration at all — they work offline, on a host, and on a
  peer identically.
- **Crossing is swept, not triggered.** Unity's trigger callbacks never worked for this (the volume
  was on a child object, so the messages went somewhere nobody was listening) and it silently did
  nothing, in every scene, forever. The door is now explicitly swept once a frame.
- **There are two ways through.** Either you cross the plane while moving, or — for creatures and
  legged machines that stop dead at a wall rather than driving their centre through it — you get
  *pulled* through on contact. Without the second mode, anything that paths carefully would simply
  stand at the rim.
- **Your momentum comes with you**, rotated by the transform rather than re-aimed, which is what lets a
  diagonal stay a diagonal. There is a specific flag for this, because normal movement would otherwise
  confiscate a fast exit in about a fifth of a second.
- **Navigation is left completely ignorant of portals.** Nothing paths *through* one. Things are
  carried through when they touch one. That is a deliberate simplification, not an oversight.

A ceiling portal composes a 180° roll, which is fine for a crate and disastrous for a walking body —
so the player's version takes only the yaw and routes the pitch through the look system instead.

---

## Teleporting, and one seam that matters

Every instant move in the game — respawn, interior transition, portal exit, save restore, arrival
seating — goes through **one function**. That is not tidiness for its own sake; it is because moving a
body correctly in this project is genuinely fiddly. It has to disable character controllers, warp
navigation agents (and check that the warp actually succeeded, because it fails silently and then lies
about it), resync every rigidbody underneath, and then announce the move to every system holding
world-space state, handing them the exact transform matrix so they can rebase footholds, path
positions and leap endpoints with one multiply.

Riders and their mounts travel as one composite. Assigning a position directly to a transform simply
does not work here — the physics body puts it back within the frame.

---

## How the scenes are laid out

Seventy-five scenes exist on disk; sixty-eight are in the build. Two indices are load-bearing: the
bootstrap scene is index 0 and the main menu must be index 1, both hardcoded, so new scenes go at the
end and never at the top.

In game, one **persistent scene** is loaded on its own and never unloads. It holds the managers, the
streamer, the save system, the interior loader, the NPC simulation, the weather, the spawn point and
the arrival director. Everything else stacks additively on top of it: world chunks, interiors, and the
arena.

Two facts about scenes that have bitten people:

- **Scenes are matched by name, not by path**, so two worlds must never share a chunk scene name.
- **The networking layer hashes scene paths case-sensitively.** If two machines have different folder
  casing, they compute different hashes and a client cannot join at all. And there is a live casing
  mismatch right now: the world's configuration records its chunk paths with a capital "World" while
  the disk (and git, and build settings) say lowercase "world". Runtime survives it because chunks are
  loaded by name — but every *editor* tool that goes through the asset database on those paths can
  silently resolve nothing, which means a navigation bake can quietly skip every chunk and report
  success. If a bake looks wrong, check that first.

---

## Where this lives

For the technical detail behind any of the above:

- `docs/AI/systems/WorldStreaming.md` — chunks, anchors, the load queue, entity migration, worlds.
- `docs/AI/systems/TerrainGeneration.md` — terrain features, caves, settlements, the site registry.
- `docs/AI/systems/NavMeshSystem.md` — the single world bake, agent settings, staleness, motors.
- `docs/AI/systems/Environment.md` — sandstorms, volumetric fog and clouds, sky, the render features.
- `docs/AI/systems/SceneTransitions.md` — interiors, thresholds, and the one teleport API.
- `docs/AI/systems/Portals.md` — apertures, the swept crossing, momentum carry, the contact pull.
- `docs/AI/systems/Scenes.md` — every scene, the build order, and the casing hazard.
