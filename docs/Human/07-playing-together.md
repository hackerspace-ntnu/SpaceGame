# 07 — Playing Together

SpaceGame is a multiplayer game that happens to be playable alone. That sentence is not marketing;
it is the single most consequential engineering decision on the project, and it explains most of the
rules in this document. When you launch a solo world, you are not running a special offline mode —
you are running a multiplayer session with exactly one player in it, and you are the host. Every
piece of netcode is live. Every authority check fires. The upside is that the awkward multiplayer
code paths get exercised constantly instead of rotting in a corner. The downside is the thing that
bites people, over and over: because solo play is a real session, a broken feature can look
completely healthy on your machine and be totally invisible to everyone else.

---

## The shape of a session

There is one player-visible entry point and, underneath it, three ways a session can actually start.

**Solo.** From the main menu you pick a world and enter it. The game quietly starts hosting a
session for one. There is no matchmaking, no network traffic to speak of, no waiting. It feels like
single-player because nothing is happening on the wire — but the machinery is all running.

**Hosted with friends.** You go through the lobby instead. The game signs you in, allocates a
relay — a server run by Unity that both machines connect out to, so nobody has to forward a port —
and publishes a lobby entry describing your session. Other players find it in the browser, or you
give them the join code. They connect to the same relay, and from that moment your machine is
**the host**: it runs the world, and everyone else is a **client** watching the host's version of
reality.

**Direct connection.** Machine-to-machine with no relay. This exists only for automated testing and
should never be used for actual play; it takes over network settings that the normal path relies on.

Whichever route you took, the exit is the same: one teardown path back to the main menu. If the host
disappears mid-session, a watchdog notices from inside the world and gets everyone out to a
"session ended" screen rather than leaving them standing in a world nobody is simulating any more.

One deliberate omission: there is **no connection approval**. Anyone who has the join code and can
reach the relay gets in. That is a choice, not an oversight, and it is worth remembering before
anyone builds a feature that assumes the server got a chance to vet a joiner.

---

## The lobby

The lobby is split down the middle, and the split is worth understanding because it explains why the
lobby stays sane when the network does not.

On one side there is the **lobby session** — the thing that actually knows what lobby you are in,
who is in it, and what the rules are. It lives for as long as the application runs. It survives the
scene load into the world. It never touches a single button or label.

On the other side there is the **lobby screen** — the page you look at. It is disposable. It reads
the session's current picture and draws it. It never talks to the online service directly.

The seam between them has one absolute rule: **nothing throws across it**. When the service refuses
a request, the failure arrives at the screen as a sentence fit to show a player, not as an exception
somebody has to catch. That is why lobby errors read like "couldn't join that session" instead of
dumping a stack trace over the UI.

Some smaller things that shape how the lobby feels:

- **The browser refreshes on a clock with a budget.** The lobby service allows roughly one query per
  second, and the browser spends that budget in exactly one place. It backs off when nothing is
  changing. Rows are reconciled one at a time rather than the whole list being thrown away and
  rebuilt, so the list does not flicker under you while you are aiming at a row.
- **One heavy operation at a time.** Creating, joining and starting are mutually exclusive; you
  cannot double-press your way into two lobbies. Lighter controls — flipping privacy, nudging team
  rules, changing your suit colour — deliberately bypass that lock so the page never feels stuck.
- **Colour and team choices publish on their own debounce clocks.** Dragging through a colour picker
  does not fire twenty network writes.
- **The rank.** The lobby shows the actual astronaut figures lined up, in their real suit colours,
  with nameplates and team plates floating over them. It borrows the menu camera for the shot and
  fits it to however many people are standing there. It is the first time in the game you see who
  you are playing with, and it is doing real work: the colour you pick here is the colour you wear
  in the world.

### The ghost membership

The lobby's most memorable failure is worth telling as a story, because everyone hits it eventually.

You run two copies of the game on one machine to test something. The first one hosts fine. The
second one tries to join and the service refuses with "you are already a member of this lobby."
Which is nonsense, because that second copy has never joined anything.

Except it has. Both copies share the same saved preferences on that machine, so anonymous sign-in
hands them **the same player identity**. As far as the lobby service is concerned, one player is
trying to join a lobby they are already in. There is now a recovery sweep that finds the stale
membership and retries, but the real fix is to launch the second instance under its own profile via
a launch flag. If you take one thing away: *two instances on one machine are the same person unless
you tell them otherwise.*

---

## What "the host decides" actually means

This is the part that shapes every feature in the game, so it is worth being precise.

**The server is the referee.** It decides what happened. Everyone else is told. A client does not
apply damage, does not open a door, does not consume an item charge, does not decide who won. It
asks, and it waits to be told.

But there is a second question, and conflating the two is the source of a large fraction of the
bugs in this repository. The two questions are:

1. **May I decide this?** — *Am I the authority for this object?* True on the host. Also true when
   there is no session at all, and true for objects that are not networked, because refusing there
   would freeze every prop in the world during solo play.
2. **Is this mine to drive from input?** — *Do I own this thing?* True for your own player on your
   own machine. True for the host. True for a vehicle that has been handed to its rider.

Almost nothing in the codebase asks "am I the server" directly. It asks one of those two questions,
because both of them answer sensibly when there is no network at all — and that is what lets a
feature written for multiplayer degrade cleanly into solo play instead of throwing.

### The one big exception: your body is yours

There is a rule that catches people constantly, and it is the inverse of everything above.

**The player's position is owned by the player's own machine, not by the host.** If the server tries
to move a remote player — teleport them, shove them, place them at a spawn — that write is
overwritten within a single network tick. Silently. Nothing logs. The player just does not move.

So placing a player is never "set their position on the server". It is always "ask the owner to move
themselves, and let the owner do it." Every teleport in the game goes through that one asking route.
Anything that repositions a player — a failsafe that lifts you out of the ground, a respawn, an
arrival cutscene — has to run on the machine that owns that body.

### How a feature is shaped by all this

The standard shape of an action in this game is:

1. The owner does the *visible* part immediately, locally, so the game feels responsive.
2. The owner asks the server what actually happened.
3. The server re-checks the preconditions itself — it does not trust the request — mutates the
   authoritative state, and tells everyone else.
4. Everyone else plays the visible part.

That "visible part" is strictly cosmetic and must be safe to run twice, because on the host it
genuinely does run twice: the host's own broadcast comes straight back to itself in the same call.
A handler that toggles something rather than setting it will flip it back. The rule is: act only
when the new state actually differs from the current one.

Two more consequences worth internalising:

- **A remote copy of an object has its brain switched off.** When you see another player's mount, or
  an NPC on a client, its movement code is not running — it is being positioned by the network. So
  anything that code *would have drawn* — a muzzle flash, a footstep puff, a rope, a light — has to
  be broadcast explicitly. If you only wrote it into the simulation, the host sees it and nobody
  else does.
- **A value that changes has no memory.** A replicated value fires its "it changed" notification
  when it changes and never again. Someone who joins later missed it. Late joiners must *read* the
  current value when they arrive, not wait to be notified. For state that is purely an event and has
  no current value at all — a rope currently strung between two things, an open portal — the server
  builds a catch-up snapshot and hands it to the joiner, retrying each entry for up to 30 seconds
  until the objects it names actually exist on that machine.

---

## The cultural rule: single-player is a host of one

Here is the rule the whole team is expected to have memorised:

> Solo play is a session with one player. So "it works on my machine" proves nothing. Every feature
> is designed for host **and** client from the start — never retrofitted.

The reason this is stated so aggressively is that the failure mode is *silent and asymmetric*. The
host is always fine. The host is fine by construction, because the host is where everything actually
happens. Bugs of this class only exist on the other machines, which are the machines you are not
looking at.

These are the specific ways it bites, and each of them has cost this project real time:

**The unregistered prefab.** Anything spawned during play has to be listed in the network prefab
list. If it is not, the host spawns it perfectly and clients construct *nothing*. No error on the
host. Nothing thrown on the client. The object simply does not exist for anyone but you. There is a
menu command that syncs the list; there is also a decoy list at the root of the project that
regenerates itself and is *not* the one the game reads. And a prefab whose networking component was
added by script rather than by hand can ship with an empty identity hash — two of those collide, and
the engine silently drops all but one.

**The remote-procedure call that does nothing.** The attribute that turns a method into a network
call only works on components that are actually network components. Put it on a plain script and it
compiles, runs, and is completely inert. It ran on your machine, locally, which is exactly what a
working call looks like from the host's seat. This bug shipped once already: it made every client
run the *server's* half of an interior transition on itself.

**The spawn pose that becomes the truth.** A client builds a newly spawned object at whatever pose
the prefab was saved with, then writes the real position over it. If that object has physics, the
physics undoes the write within the frame. And if that object is owner-authoritative — like a
player — the wrong pose is then *published to everyone as the truth*. The symptom is a player
standing hundreds of metres from where they should be, or inside the ship they were supposed to
spawn beside.

**Folder casing.** The engine matches scenes between machines by hashing the scene's path,
case-sensitively. Git on macOS does not care about case. So a folder renamed from `World` to `world`
on one machine and not another produces a client join that fails with a hash-not-found error and no
hint whatsoever about what it means.

**The leaked socket.** Running two sessions from the editor leaks the underlying network socket per
play session, so the second run fails to bind. There is a mitigation; the fallback is to bump the
port.

The practical conclusion is the same every time: **verify on an actual client.** There is a menu
command that builds a test player, and it can be launched in host mode or client mode from the
command line, printing structured lines you can assert against. Two processes, both watched. A
feature that has only ever been seen working on the host is not finished.

---

## Chat

Chat is the one system that does not ride the general-purpose message channel, and the reason is
mundane: the shared message format has no room for a string, and it has no way to address a single
player. Chat needs both. So it owns its own small set of network calls.

Beyond that it is what you would expect. Messages are rate-limited by a token bucket, so nobody can
flood the log. The log itself is static and survives scene loads, so returning from an interior does
not wipe the conversation. There are commands as well as plain messages.

The one thing worth knowing if you touch it: player-typed text is rendered by a rich-text system,
which means a player can type markup and have it interpreted. Both the sanitiser and the
no-parse wrapper are there for a reason. Do not remove either.

---

## Versus

**Versus** is team PvP in the real streamed world, not in an arena. Its defining image is the
arrival: every team gets one identical ship, placed on a ring, and the whole team launches and lands
together in a formation. You start the match inside your ship with your teammates.

The shape:

- **2 to 8 teams, 1 to 12 players each, up to 24 seats total.** Those two limits are coupled — each
  one is clamped against the other, which is the only thing stopping a host configuring eight teams
  of twelve for a 24-seat session.
- **Team identity is a single number** used everywhere: which name you get, which colour your ship
  wears, which spawn point on the ring is yours.
- **The host stages the team rules in the lobby.** Every machine then reads that setup as the world
  loads, and each client tells the server which team it picked. The server validates the index and
  records it — a client can *claim* a team, but the server decides.
- **Team colour is an opinion, not a fact.** Each player publishes what they think their team's
  colour is, timestamped, and the colour picker skips swatches other teams are already wearing.

The important design fact about Versus, and it surprises everyone: **there is no scoring, no win
condition, and no end.** The match ends when people stop playing. Versus is a mode for fighting in
the world, not a match with a result screen. If you are designing something that assumes a Versus
match concludes, it does not.

Two practical notes for anyone working near it. The ship placement ring comes from an authored
configuration asset — there is a runtime override for it, but that override is only ever used by
tests, so do not assume it is live. And ground height under a spawn is probed from the terrain
heightmap first and only then by raycast; a "no ground here" answer means *the chunk has not loaded
yet*, and the correct response is to retry, never to guess a height.

---

## The arena deathmatch

Separately from Versus there is a bot deathmatch in a dedicated arena, with three gamemodes driven
by one match orchestrator:

- **Team Deathmatch.** Two teams. Up to four bots per side. The host chooses the ending — a kill
  target, a number of lives each, or last team standing. The host is always on the ally team, and
  players joining later fill whichever side is thinner.
- **Free-For-All.** Every participant is its own team with its own faction, up to fifteen bots.
  Lives run from 1 to 10. With one life it collapses into last-standing.
- **Battle Royale.** Free-for-all with lives forced to one, last-standing forced on, and no
  respawns. The lives control is hidden because there is nothing to choose.

Everything decisive is the server's: bots, teams, kills, the leaderboard, and the call on who won.
The leaderboard is rebuilt whole and pushed out on each death or join rather than being incrementally
patched. Friendly fire and suicides score nothing. When you are eliminated you go into spectator
mode rather than being removed. At the end, the winning side sees Victory and everyone else sees
Defeat, decided by comparing the winning team against your own.

One structural detail worth knowing because it is unusual: **respawning does not destroy and recreate
you.** Your body is deactivated, then reactivated, healed, and moved. This matters for anything
holding a reference to a player — that reference stays valid across a death.

And a related trap that was found the hard way: a dead player's body stays in the world, so it has to
be explicitly pulled out of the AI targeting registry when it dies. Without that, every survivor
keeps aiming at a corpse and a last-standing match never ends.

### The arena is currently not playable

State this plainly, because nothing in the game will tell you: **the arena scene is empty.** No match
orchestrator, no spawn points, and there is no baked navigation mesh anywhere in the project for it.
The entire deathmatch code path — all three gamemodes, the leaderboard, the result screen — is
written and orphaned, waiting for someone to author that scene. Nothing warns you; you just launch a
minigame and nothing happens.

A related known hazard for whoever does author it: if the arena's navigation mesh ends up split into
disconnected islands, spawn points on the minority islands are dropped with a warning telling you to
rebake. Without that guard a match can hang forever because two survivors literally cannot reach
each other.

---

## The story run

The plain story run is the third family, and it is the smallest: a session timer and a win condition
that loads a win scene. It belongs to solo and co-op play, not to Versus and not to the arena, and
the three do not share machinery beyond the spawn plumbing.

---

## What survives a session, and what does not

Short version, expanded in the next document:

- **Versus and arena match state is deliberately not saved.** Both are single-session. The values
  that carry a mode across the scene load are explicitly cleared on the way out — and one of those
  clears is the only thing stopping the next match starting on the previous match's spawn ring.
- **Story-run session state is saved** — the timer and the game state — and restoring it never
  re-triggers the win.
- **The day/night cycle is not replicated as a value.** Only an anchor point is; the actual time of
  day is computed from a shared clock on every machine. That is why it stays in sync for free and
  why there is nothing to catch a late joiner up on.

---

## Where this lives

The dense, implementation-level versions of everything above:

- `docs/AI/systems/Multiplayer.md` — the message channel, the two authority questions, session
  startup, how a client gets a body, the full list of silent-failure traps.
- `docs/AI/systems/Lobby.md` — the session/screen split, the pure readers, the rate budget, the
  ghost-membership recovery.
- `docs/AI/systems/GameModes.md` — Versus, all three arena gamemodes, spawn and score flows, and the
  note that the arena scene is unauthored.
- `docs/AI/systems/WorldStreaming.md` — why client joins fail on folder casing, and how chunks reach
  clients.
- `.claude/skills/spacegame-multiplayer/SKILL.md` — the working recipes for adding netcode to an
  existing system.
