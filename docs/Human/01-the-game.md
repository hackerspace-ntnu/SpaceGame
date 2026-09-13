# The Game

SpaceGame is a first-person co-op survival-and-salvage game set on a desert planet. You arrive by
crashing into it: a heavy lander comes down out of the sky with the whole crew strapped into it, hits
the sand, and that impact site becomes the place you live out of. From there the game is walking,
driving and improvising your way across a large open desert with a small set of very strange tools,
in the company of up to three other people, and coming back to the same world tomorrow with
everything where you left it. This document is about what the game *is* — the premise, the loop, and
the handful of decisions that make it feel like itself.

---

## The premise

You are an astronaut on a planet you did not choose to land on. The **lander** — the ship that
brought you — is not a menu or a loading screen. It is a real sixty-tonne hover vehicle with a
walkable interior, four crew chairs, a boarding stair, a rear ramp and a cockpit, and it is the first
thing you ever see. It flies exactly once, on the way in, and then it is a wreck you live next to.

That wreck is also the game's long-term goal object. Eleven sockets around its hull are empty, and
eleven matching hull modules are out there somewhere in the sand. Salvage is the spine of the loop:
go out, find a part, carry it home, fit it back into the ship.

The world is a desert. Sandstorms cross it. The sun rises and sets. There are caves under it,
settlements built on it, creatures and NPCs living in it, and vehicles scattered across it for people
who would rather not walk.

---

## Arrival: the first two minutes

Most games start you standing somewhere. This one starts you falling.

When a brand-new world is created, the server picks the spawn site first and streams the terrain in
around it, then spawns the lander at the top of a descending spiral — about **2200 metres up** and
**900 metres out** — and seats every connected player in a chair inside it. It waits (up to twelve
seconds) until everyone is actually aboard, then launches every hull on the same frame, so nobody
watches from outside while their friends fly.

The descent takes **twenty-six seconds**. During it the ship is not being simulated — it is being
walked along a precomputed arc, with its own hover motor and AI switched off so nothing fights the
camera. What you see is a fade, a rising shake, the impact, and a hold in the dark. About a second
and a half after the ship stops, the seats unlock and you can stand up on your own schedule. (If
somebody wanders off to make tea, a three-minute backstop empties the seats anyway.)

Two consequences worth knowing:

- **This happens once per world.** Load a saved world and there is no crash — you simply appear where
  you were. The "this world has been arrived in" flag is written as soon as the descent starts, on
  purpose: a *resumed* crash landing is worse than a crash landing you missed the end of.
- **The impact site is the spawn point.** They are the same resolved position, and it is resolved
  exactly once. That coupling is load-bearing; if a world has no spawn point authored in it, nobody
  spawns at all and the arrival never runs.

---

## What you actually do in a session

### You walk around in first person

The camera lives inside the helmet. There is no third-person view of your own body during normal
play — the third-person cameras in the game belong to other systems (riding a vehicle, going ragdoll,
spectating after death). You walk, sprint, crouch, jump, dash, and take fall damage. Crouching drops
your eye height by about half a metre and refuses to stand you up under a low ceiling.

One thing that surprises everyone: **the astronaut is three metres tall.** The capsule is scaled up,
the eye sits 2.45 m above the soles, and the character's own pivot floats a metre above its feet.
Doorways, seats, stair treads and hand poses across the whole project were all measured against that,
so it is not a bug to "fix" — but it does mean that anything you eyeball against a human-sized
reference will be wrong by half.

Air control is deliberately weak (about 30% authority once your feet leave the ground), which is what
makes the mobility gadgets below feel like they matter.

### You carry four things, and a backpack

The hotbar has **four slots**. That is the whole of what is in your hands' reach. Selecting the slot
you already have selected puts your hands away.

Everything else goes in the **backpack**, which is not a grid UI — it is a physical deployable
expedition rig you set down and rummage through from a dedicated camera. Seven flat faces on the rig
are cell grids (255 cells in total, one global cell size of 13.5 cm read off the rig's own webbing), and
you place real objects onto them at a real position and yaw. Items interlock by their footprint
shape, so packing is a small spatial puzzle rather than an inventory count. The contents belong to the
pack, not to you: set it down and walk away and the gear stays with it.

### You use artifacts

**Artifacts** are the game's verbs. They are the usable items that occupy a hotbar slot and fire on
one shared Use button, and the striking thing about the catalogue is how few of them are guns:

- **Grappling hook** — a dart, a rope that goes taut, then swing or winch yourself in.
- **Lasso** — hold to twirl, release to throw; a real simulated rope that you can put around a
  creature and then fight against.
- **Net gun** — fires a woven lattice along a fixed arc, catching whatever it lands on.
- **Leash** — tie a rope between literally any two things and see what happens.
- **Portal spray can** — sprays paint onto a wall; the paint *is* the hole. See the world doc.
- **Jumping rod** — a pogo stick. It was prototyped as a vehicle and deliberately rejected as one,
  because reading like a vehicle made it feel wrong; it is an item you hold and bounce on.
- **Wing pack** — worn rather than gripped.
- **Anti-gravity potion** — drink it and float for about five seconds.
- **Gravel blaster** — a home-made pipe shotgun that backfires roughly one shot in ten.
- **Repulsor gauntlet** — an instant cone that ragdolls everything caught in it.
- **Sucker puncher** — a steam ram: heavy damage and a launch on whatever it hits, plus a shove for
  everything nearby.
- **Laser staff** — burns for three seconds, then recharges for ten. Once lit it finishes its burn
  whether you keep holding the button or not.
- **Dragon bazooka** — a corkscrewing firework rocket that bursts into whelps.
- **Lightning spell** — the simplest one: a bolt lands where you were pointing.
- **Rocket turret** — a deployable that plants itself in front of you and starts shooting.
- **Item scanner** — a handheld CRT that finds loose salvage within 100 metres.
- **Ruin scanner** — a downward cone of light that reveals hidden things it sweeps over.
- **Ship part** — carry a hull module and fit it into its socket on the lander.

The through-line is mobility and mischief rather than damage output. Most of these are ways to move,
to move something else, or to change the shape of the space between you and where you want to be.

Two design rules hold the whole catalogue together, and both exist for multiplayer reasons:

- **Every use is split in two.** The half that changes the world runs on exactly one machine; the half
  you see and hear runs on every machine, including yours *immediately*, before any round trip. That
  is why gadgets feel responsive rather than laggy, and why nobody's client ever waits on the host to
  find out that they fired.
- **Anything erratic is seeded, not synced.** When a shotgun scatters pellets or a rocket wanders, the
  player who fired rolls one number, that number travels, and every machine derives the identical
  spray from it. Nobody streams positions for a firework.

Continuous items — the laser, the lasso twirl, the portal spray — stream at 15 ticks a second while
held, which is the budget the whole "hold to do something" family lives inside.

### You go somewhere and come back

Sessions tend to be expeditions. You leave the wreck, cross terrain that is being streamed in around
you as you move, find a cave or a settlement or a site, and come home. The world is 4000 × 3000 metres
and it does not load all at once — the second document covers how it holds together while you cross
it.

---

## Multiplayer is not a mode

The most important structural decision in the project: **single-player is a host of one.** There is no
offline code path that gets to be simpler. When you play alone you are running the same server the
other three players would be connecting to, which means a feature that only works "on your machine"
is not proof of anything, and the classic failure is a thing that works perfectly for whoever pressed
Host and does nothing at all for everyone else.

Consequences you will feel as a player:

- Your body is authoritative on **your** machine. Other machines cannot shove your character around
  directly; they ask you to move. That is why teleports, respawns and portal exits are all routed
  through one specific mechanism rather than someone writing your position.
- Damage is decided in one place, by the server, no matter what caused it — a gun, a gadget, a
  creature, a sandstorm, a fall.
- Sound is never sent over the network. Every machine plays its own.

---

## It has to survive quitting

The other structural rule: a world you leave is a world you come back to. Saved state is keyed by the
*identity* of a thing, never by which chunk of terrain it happened to be standing in, because the
streaming system moves entities between chunks all the time.

What persists: where the wreck lies and what condition it is in, which hull sockets you have filled,
door and stair positions, your position and rotation, your health, your hotbar and what each item has
become (a lasso remembers what it is tied to), your backpack contents, your suit colour, your look
pitch, the weather clock and every live storm, the time of day, which interior you were standing in,
and every creature and vehicle in the world.

Persistence here fails *silently* — nothing throws, the state is just quietly gone — which is why the
team treats "does it survive a reload" as a separate acceptance question from "does it work".

---

## Three ways to play

**The story run** is the default: one world, one crew, the arrival, the desert, the salvage. It has a
session timer and a win state that loads a win scene.

**Versus** is team PvP in the same streamed world. The lobby picks 2–8 teams of 1–12 players (24 seats
maximum), and instead of one lander the arrival builds a whole formation — one team-coloured ship per
team, launched together on different arcs, landing on a ring around a shared centre. It is honest to
say that Versus is currently a *setup* rather than a *match*: there is no scoring, no win condition
and no end. The mode finishes when people leave.

**The minigame arena** is a separate bot deathmatch with three gamemodes driven by one match
orchestrator — Team Deathmatch (two sides, up to four bots each, host picks kill target / lives /
last-standing), Free-For-All (everyone their own team, up to fifteen bots), and Battle Royale (FFA
with exactly one life and no respawns). The code is complete: bots, factions, kills and deaths, a
leaderboard pushed on every death, spectator mode for the eliminated, a result screen that shows
Victory or Defeat per side.

**But the arena scene itself is empty.** It was reduced to a bare scene by a cleanup commit and never
restored, so the entire deathmatch route currently loads a void additively over the world. Nothing
warns you. Anyone picking up minigame work should restore the scene from history first.

One more thing that is built but not turned on: NPC trading is code-complete and has no authored
trader anywhere in the project, so no trader exists to talk to.

---

## What makes it distinctive

If you had to name the character of the project in four points:

1. **The opening is diegetic.** You arrive by flying and crashing, with your crew, into a spot that
   then becomes home. The wreck is a place, a vehicle, and a progress bar all at once.

2. **The toolset is mobility, not firepower.** Grapple, lasso, net, leash, pogo, portals, wing pack,
   anti-gravity. The interesting question in most encounters is *how do I get there* or *how do I move
   that*, not *how much damage per second*.

3. **Everything is a simulation you can grab.** Ropes are real Verlet ropes. The backpack is real
   surfaces with real footprints. The portal opening is literally the paint you sprayed. Ragdolls are
   built from the skinning weights at runtime. Very little is faked with a UI.

4. **Multiplayer and persistence are entry requirements, not features.** Every system in the project
   was designed against "does this work for a client who joined late" and "is this still true
   tomorrow" from the first line, because retrofitting either one has repeatedly cost more than
   building it in.

---

## Where this lives

For the technical detail behind any of the above, the dense agent-facing docs are:

- `docs/AI/systems/README.md` — index of every subsystem doc, plus a live list of known defects.
- `docs/AI/systems/PlayerShip.md` — the lander, the arrival sequence, seating, salvage sockets.
- `docs/AI/systems/PlayerCharacter.md` — movement, look, stances, death, geometry, what replicates.
- `docs/AI/systems/Artifacts.md` — the full artifact catalogue and the use/present authority split.
- `docs/AI/systems/GameModes.md` — Versus, the arena gamemodes, spawning and scoring.
- `docs/AI/systems/Inventory.md` and `docs/AI/systems/Backpack.md` — hotbar, equipping, the physical pack.
- `docs/AI/systems/Multiplayer.md` and `docs/AI/systems/Persistence.md` — the two non-negotiables.
