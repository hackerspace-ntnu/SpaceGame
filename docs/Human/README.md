# SpaceGame, explained

Ten chapters about how this game works and why it is built the way it is. Written for people:
a new teammate finding their feet, an artist wondering how their model reaches the game, a
designer deciding what to build next, or anyone coming back after months away.

You can read straight through — it is ordered so each chapter builds on the last — or jump to
whatever you need.

**Looking up one specific thing?** [Every system, briefly](the-systems.md) has a few sentences on
each of the 33 parts of the codebase, so you can resolve a name you heard without reading a whole
chapter.

## The chapters

**Start here**

1. [The game](01-the-game.md) — the premise, the core loop, what a session actually feels like.
2. [The world](02-the-world.md) — the planet, how it is built, and how it holds together while
   you cross it. Terrain, caves, settlements, storms, interiors, portals.

**Living in it**

3. [The player](03-the-player.md) — your body: moving, looking, interacting, holding things,
   dying.
4. [Creatures and people](04-creatures-and-people.md) — how the wildlife and NPCs think, who
   fights whom, and how legged animals actually walk.
5. [What you carry](05-what-you-carry.md) — the hotbar, the gadgets, and the backpack you lay
   real objects onto.
6. [Vehicles and the ship](06-vehicles-and-the-ship.md) — mounts, machines, the ornithopter,
   and the lander you crash in.

**The things that shape every feature**

7. [Playing together](07-playing-together.md) — sessions, lobbies, and the rule that decides
   how every feature in this game is written.
8. [Saving and continuity](08-saving-and-continuity.md) — what survives a quit, and the quiet
   ways that goes wrong.

**Making it**

9. [How it looks and sounds](09-how-it-looks-and-sounds.md) — the interface, cutscenes, the
   atmosphere, and audio.
10. [How the game is built](10-how-the-game-is-built.md) — from a Blender file to something in
    the game, the tools, and how it is tested.

## A note on honesty

These chapters say plainly where things are unfinished or broken. That is deliberate. If a
chapter tells you the deathmatch arena is currently an empty scene, or that trading works but
has no trader in it, that is not an oversight in the writing — it is the state of the game, and
knowing it saves you an afternoon.

## Going deeper

Each chapter ends with **Where this lives** — pointers into [../AI/systems/](../AI/systems/),
the technical reference. Those documents are dense and written for AI agents rather than
people, but they are the exact, current, source-verified answer when you need one.

If you are here to change code, read [../AI/INDEX.md](../AI/INDEX.md) instead.
