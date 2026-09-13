# 08 — Saving and Continuity

A world in SpaceGame is not a snapshot of a scene. It is a list of *things* and what each of them
currently is — a crate that has been looted, a creature that wandered four kilometres from where it
was authored, a door somebody left open, the position of every player who has ever played this
world. When you quit and come back, the game does not restore a photograph of the world; it rebuilds
the world from scratch out of the level as it was authored, then goes through that list and applies
every difference. Understanding that one idea explains almost everything else about how saving
behaves here, including the strange parts.

---

## Identity, not position

The oldest instinct about saving a game world is to save it by place: *this chunk of the map
contained these three creatures at these coordinates*. That approach cannot work here, for a very
concrete reason.

The world is streamed. It is a grid of 8 by 6 chunks, each 500 metres square — 4,000 by 3,000 metres
of desert — and only the chunks near a player are actually loaded. Things that move do not stay in
one chunk. A caravan crosses four chunk boundaries on its way somewhere. A creature you left near
the ridge is, ten minutes later, the property of an entirely different piece of the map. If records
were filed by chunk, that creature would be filed under a chunk that is no longer where it is, and
either lost or duplicated.

So every saved object carries **its own identity** — a stable id that belongs to the object, not to
its surroundings — and the save file is one flat list keyed by those ids. Each record does note
which scene the object was in, but only as a *routing hint*: "when this scene loads, that is where to
rebuild me." That hint is re-stamped every time the object is captured, precisely because it goes
stale by design.

If you remember nothing else: **records are addressed by identity, never by where the thing was.**

---

## Three populations

The save file holds three quite different kinds of state, and they are kept apart on purpose.

**World objects.** Everything in the world that can change: creatures, vehicles, doors, levers,
dropped items, workstations, turrets, beacons. Each has a record; each record holds a pose and a bag
of named entries.

**Players.** Filed separately, keyed by a per-installation profile rather than by anything about the
world. This is what lets four people share one world file and each get their own inventory, their
own suit colour, their own position, their own health. The world half of the save steps carefully
around the player half — it is not allowed to capture players, because they are not world objects.

**Session-wide state.** A handful of things that belong to the world as a whole rather than to any
object in it: the time of day, the current weather, the map you have uncovered, herd bookkeeping,
active ropes, story-run state. These register themselves as global savers and get their own section
of the file.

---

## Authored versus runtime — the whole storage split

There are exactly two kinds of thing in a saved world, and they are stored in fundamentally different
ways.

**Authored** things were placed by a designer in a scene file. They already exist the moment that
part of the map loads. So their record is a **delta**: the level provides the object, the save
provides the differences. Restoring one means finding it and applying changes in place. The only way
an authored object goes away is a *tombstone* — an explicit note in the file saying "this one is
gone", which is what stops a destroyed crate quietly returning on every load.

**Runtime** things did not exist until something spawned them: an item you dropped, a creature the
world simulation created, a vehicle built during play. Nothing in the level will produce them, so
their record is a **recipe**: it names the prefab to rebuild from, plus the pose and the state to
apply afterwards.

The pose is stored on the record itself rather than being one of the state entries, and there is a
good reason for that oddity: a runtime object needs to know *where it is* before it exists, so its
position cannot be something one of its own components hands over later.

---

## Savers: one key each, and never a migration

State is not written by one big serializer that knows about everything. It is contributed by small
components — the team calls them **savers** — attached to the object, each of which owns exactly one
named key inside that object's bag of state. There are 61 of them today, covering everything from
pose and velocity to an agent's current combat cadence to which colour swatch your suit is wearing.

The rule is strict and it pays for itself: **one saver owns one key, and nothing else writes it.**

The dividend is that adding a saver, removing a saver, or adding a field to one needs *no migration
whatsoever*. A record written before your saver existed simply has no entry under your key, which
your saver reads as "restore defaults". A record written by a build that had a saver you have since
deleted carries an entry nobody reads, which is harmless. Only a change to the *shape of the document
itself* needs a migration, and there has been exactly one of those — the move from per-scene records
to the flat identity-keyed list described above.

The other half of that bargain: **a saver's key is permanent.** Rename it and every record ever
written is orphaned under the old spelling — silently, because a missing key just looks like an
object at its defaults.

### Who gets saved at all

Opting in is by *component*, not by a list somebody has to remember to update. If an object carries
health, or is a pickup, or navigates, or declares itself part of the mutable world, the system
attaches the right savers to it automatically when its scene loads.

That declaration — "I am part of the mutable world" — is a deliberate, explicit marker, and it exists
because of a failure story worth telling.

---

## Cautionary tale one: every vehicle in the game was missing

The original opt-in was a heuristic: *if it has a physics body that is not kinematic, it is part of
the world, so save it.* Reasonable. Also completely wrong for this project.

Every legged rig in the game — every walker, every mount, every animal — is kinematic. It moves
itself by solving its own legs, so it does not want the physics engine pushing it around. One of the
vehicles does not even have a physics body on its root object at all.

So the heuristic quietly answered "no" for the entire cast of machines in the game. Every mount,
every walker, every vehicle was absent from the save file. Not corrupted. Not restored wrongly.
*Absent.* You would save, quit, load, and every vehicle would be sitting exactly where the designer
originally placed it, because the level put it there and nothing ever said otherwise.

The fix was to stop inferring and start declaring: a component that says nothing except "this object
is part of the mutable world". The lesson generalises. **Do not rely on inference to decide what gets
saved.** If a thing must survive, say so.

---

## Cautionary tale two: the world that would not let you walk

A player saves and quits. They load the world back up and cannot move. No error, no warning, no
console output. The character is simply immovable.

What happened: the physics body's kinematic flag had been saved. Quitting tears down the networking
layer first, which flips that flag as part of shutdown, and the quit-time autosave then faithfully
captured a body that had just been switched off. On the next load the game restored exactly what it
had recorded — a frozen player.

The rule that came out of it: **never save engine-owned flags.** The saver for physics bodies now
stores velocity only, and it returns nothing at all for a kinematic body. That state belongs to the
engine and to whatever code is currently driving the object; the save file is not the authority on
it.

---

## Cautionary tale three: the crate that refilled

You loot an authored crate. You save, quit, come back — and it is full again.

Or the other version, which is worse: a runtime object duplicates on every reload, so after three
loads there are three of it standing in the same spot.

Both come from the same mistake: creating or destroying world objects directly instead of going
through the world service that owns object lifetime. Spawning directly means the save system never
learns the object exists as *this specific object*, so on the next load it happily builds another
one from the recipe next to the one already there. Destroying directly means no tombstone is written,
so the record survives and the authored object comes back untouched.

The general fix is to route creation and removal through the one service that knows about identity.
The specific thing to burn into your memory is how you *test* for it: **duplication only shows up on
the second reload.** Load once and the world looks fine. Load twice and count the records. Anyone who
verified persistence with a single reload has not verified it.

---

## Cautionary tale four: the object that lost its name

Not everything gets a proper baked identity by hand. An authored object that has not been wired gets
a *derived* id instead — a hash of where it sits: which scene, its path through the hierarchy, its
index among its siblings.

That works right up until somebody renames the GameObject, or drags it under a different parent, or
reorders it. Then the hash changes, the object's id changes, and the record it used to own is
orphaned. The object comes back at its authored defaults and the record sits in the file forever
under an id nothing answers to.

There is an editor pass that bakes real, stable ids into scene files, and it is the right answer.
But the failure it prevents is a *design-time* failure caused by an entirely innocent action, which
is exactly why it is worth knowing about before you tidy up a scene hierarchy.

A cousin of this one: making a prefab into a **variant** rather than moving the original changes the
id the game uses to answer "what do I rebuild from", so it disagrees with every record already
written for it.

---

## Persistence fails silently — always

This is the sentence to remember about this system, because it is the opposite of how the rest of the
codebase behaves. Elsewhere the project's policy is to fail loudly. Here, by nature, it cannot.

A missing key is indistinguishable from an object that was at its defaults. A record nobody claims is
indistinguishable from an object that has not loaded yet. An object that was never opted in is
indistinguishable from an object with nothing worth saving. There is no exception, no red text, no
crash. The world just comes back slightly wrong, and usually in a way you only notice two sessions
later.

A short list of the ways it goes quiet:

- **A saver handing over a bare number, string or list** instead of a structured object has its key
  dropped. An error is logged, but the save itself succeeds — so the file is written, looks healthy,
  and is missing one entry.
- **A prefab id that cannot be resolved** produces a warning, and the record is deliberately *kept*
  rather than discarded, so it can come back if the prefab is registered later. But the object does
  not appear.
- **Not one prefab on disk currently ships a baked prefab id.** Anything spawned at runtime is
  therefore captured but not rebuildable until it is wired — registered with the networking layer,
  placed in the resources folder the registry scans, or stamped by the wiring tool.
- **Playing a world scene opened directly in the editor saves nothing.** There is no active world
  because you did not come in through the menu, and the system says so once and then stays quiet.
  Enter through the main menu, always.
- **Restoring a position by assigning it directly** puts the object back within a frame. The engine's
  automatic transform syncing is off in this project, so a raw position write to anything with a
  physics body or a character controller is undone almost immediately. There is one correct
  instant-move routine, and everything — teleports, restores, respawns, portals — goes through it.

The practical defence is to **read the file**. It is JSON, it is on disk, and it is legible. If your
object's id is not in it, it is not being saved. If your object is in it with an empty set of keys,
it is wired up and saving nothing, which is a different bug with a different fix.

---

## When saving actually happens

There are five triggers, and knowing them explains most "why did/didn't it save" questions:

- **Autosave on a 300-second timer.** If a save is refused for any reason, it retries within 15
  seconds rather than waiting out another full interval.
- **On quit.** The application closing writes synchronously — and if a write is already in flight, it
  waits that write out rather than standing down.
- **On returning to the main menu.** Leaving a world saves it.
- **Quicksave on F5**, quickload on F9.
- **Immediately on creating a new world**, so a fresh world has a file before anything can go wrong.

Each save stamps which trigger wrote it, so when you open the file you can tell an autosave from a
quicksave from a clean exit.

The write itself is atomic and paranoid: it goes to a temporary file, replaces the real one, and
keeps the previous version as a backup. A read that finds the main file unusable falls back to that
backup. There are also two refusals built into saving — it will not write a file in an older format
over a newer one, and it will not write a save that would discard every player record. Both of those
are there because both of those happened.

Serialization runs on the main thread; only the disk write is pushed off it. Quicksaving in a busy
world is not free.

### On a client

Saving is **server-only**, without exception. Every capture and restore path exits immediately on a
client. Clients get world state through normal replication, the way they get everything else.

Consequently a client pressing F5 is refused with an explanation, and a client pressing F9 is refused
too — reloading the world scene would drop that player out of the session entirely. In solo play you
are the host, so both work; that is one more place where "solo is a host of one" leaks into what a
player can actually do.

---

## Streaming and the save store's one invariant

Because chunks come and go under the player's feet, the save store maintains a single invariant that
is worth stating outright:

> An object's state is **either** live in a loaded scene **or** in the record. Never neither. Never
> both, drifting apart.

That is maintained by hooking chunk loading and unloading directly. When a chunk loads, its records
are applied and its objects become live. When a chunk is about to unload, a "will unload" event fires
*before* the unload happens — that is the last frame anything in that scene can still be read, and it
is when everything in it is captured back into records. Interiors do exactly the same thing with
their own load and unload events.

Chunk unloading is grace-timed at 10 seconds, so briefly stepping across a boundary and back does not
churn. A chunk is also held open while a tracked entity is standing in it.

One consequence people find surprising: an object that has never been near a player since the world
loaded has no live copy at all. It is *only* a record. This is fine, and it is the whole point — but
it does mean a bug that only manifests on hydrate looks like "this object is broken sometimes",
because it depends on whether you walked past it.

Two related notes:

- **The persistent scene is hydrated by hand**, because no streaming event ever fires for it. Every
  entity pinned to it lives there.
- **Chunk scene names must be globally unique across worlds.** Saved chunk deltas are keyed by scene
  name, which is one of the reasons loading a save into a different world is refused outright.

---

## Which world is this, anyway

Each save records the identity of the world configuration it belongs to, and loading refuses to open
a save into a world it was not made in. Saves written before that check existed carry an empty id and
are accepted as legacy.

Choosing a world happens in the menu, which stages the choice — the world's name, its configuration,
and whether it is new — and carries it across the scene load into the world itself, because the menu
that made the choice is destroyed by that very load. Quickload restages the same world and reloads
the scene through the networking layer's scene manager rather than the engine's, specifically so that
any connected clients follow along instead of being left behind.

One practical limitation, stated plainly because it is a real gap: **the menu can currently reach
exactly one world.** The world configuration is a fixed reference on the menu itself. A second world
exists in the project and can only be reached by opening its scene in the editor. Making worlds
selectable is a piece of indirection that does not exist yet.

---

## Things that reference each other

Some state is not about one object; it is about the relationship between two. A rider is on *that*
mount. A rope connects *these* two things. A portal is paired with *that* portal.

Those cannot be resolved at restore time, because the other end may not exist yet — its chunk might
still be loading, or the other player might not have connected. So references are stored as small
pointers, and there is a second pass that runs after the world has settled, whose job is to resolve
them.

That deferred pass runs more than once: once per world load, again every time a player is bound to
their record, and again for every chunk that hydrates after the first pass. So it is emphatically
*not* a one-shot initialisation step. Anything that runs in it has to be safe to run repeatedly over
a world that has moved on since last time. The most common bug here is code that treats it as
one-time setup and re-applies stale state over a live world.

---

## Bodies, deaths, and the things that are not gone

Two lifetime facts that catch people:

**Death is deactivation, not destruction.** A creature that dies is switched off, not deleted. It
stays in the live registry, it stays saveable, and its corpse persists. Anything treating "present in
the registry" as "alive" is wrong — that exact mistake once caused dead runtime creatures to be
re-instantiated on every chunk load.

**Removal needs a tombstone.** Genuinely deleting an authored object writes an explicit note that it
is gone, and the object is buried on the next load. Without the tombstone, the level puts it back
every time.

---

## What does not survive

Not everything is meant to. Being explicit about it:

- **Versus and arena match state is not saved, deliberately.** Both are single-session by design. The
  temporary faction and targeting changes a match makes to entities are specifically excluded from
  capture, so an arena match does not leave the world's creatures with arena allegiances.
- **The story run's session state does survive** — its timer and state — and restoring it never
  re-triggers the win.
- **A late joiner is not placed into an interior other players are inside.** Known and open.
- **Items dropped inside an interior are lost when the last occupant leaves**, unless the object is
  properly save-wired. Known and open.

---

## How to actually verify persistence

The checklist the team uses, roughly in order of cost:

1. **Read the JSON.** Is your object's id there? Are your keys there? An empty set of keys means
   wired and saving nothing.
2. **Quicksave and quickload in play mode.** Fastest signal, catches gross breakage.
3. **Quit, relaunch, and load the world through the menu.** This is the only path that exercises the
   quit-time save, which is where the frozen-player bug came from.
4. **Reload twice and count records.** Duplication does not show on the first cycle.
5. **Do it on a real client**, not just the host — because the save happens on the server and the
   client only ever sees the result through replication.

The editor also has validation tooling: passes that wire up scene objects and prefabs, one that
validates the wiring, and one that reports state which looks like it ought to be saved and is not.
They are idempotent, and none of them should be run while the game is playing.

---

## Where this lives

The dense, implementation-level versions of everything above:

- `docs/AI/systems/Persistence.md` — the document format, all 61 savers, the load and save flows, and
  the full trap table.
- `docs/AI/systems/EntitySystem.md` — how an object becomes a first-class entity, the two ids
  (which prefab, which record), and how identity is derived or baked.
- `docs/AI/systems/WorldStreaming.md` — the chunk grid, the load/unload lifecycle, and the streaming
  contract the save store depends on.
- `docs/AI/systems/SceneTransitions.md` — interiors as additive scenes, the one instant-move routine,
  and how "which interior am I in" is stored per player.
- `.claude/skills/spacegame-persistence/SKILL.md` — the working recipes for adding save support to a
  new component, prefab or spawned object.
