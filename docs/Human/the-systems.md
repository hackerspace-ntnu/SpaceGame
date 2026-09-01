# Every system, briefly

The ten chapters explain the game by theme. This page goes the other way: one short entry for
every part of the codebase, so you can look up a name you heard in a meeting or a commit message
and find out what it is in a few sentences.

Each entry ends with **Worth knowing** — the one thing about that system most worth carrying
around. Often that is a constraint, sometimes it is something that is currently broken. Those are
honest, and deliberately so.

The italic name in each heading is the technical document that covers it in full, in
[../AI/systems/](../AI/systems/). Those are written for AI agents rather than people: exact,
dense, and kept current with the code. Come here first, go there when you need the detail.

## Core — the parts everything else stands on

### Startup and the shared plumbing *(CoreServices)*

This is the glue: the order the game boots in, the handful of shared lookup tables everything else asks questions of, the player's keyboard and controller bindings, and the options menu values stored on the local machine. Pressing Play in any scene quietly bounces you through a tiny boot scene first, which loads the item catalogue, the audio system and the networking object, then sends you on to where you meant to go. Input is attached to each player's own body rather than being global, so it can be switched off cleanly during death, menus and cutscenes.

**Worth knowing:** editing the input bindings asset does nothing on its own — a generated copy of those bindings is what actually runs, so a rebind that was never reimported silently does not exist.

### Playing together over the network *(Multiplayer)*

All networking runs through one small message channel instead of a bespoke sync class per feature: a feature sends a message, the server decides, and everyone else is told the result. Single-player is really a host with one player in it, so every networking rule applies even when you are alone and there is no separate offline code path to rot. Sessions, joining, player identity, chat and the catch-up snapshot a late joiner receives all live here.

**Worth knowing:** if a runtime-spawned thing is missing from the network prefab list, the host works perfectly and every client sees nothing — so testing on the host alone proves nothing.

### Saving and loading *(Persistence)*

Each world is one text file, assembled from small per-component payloads captured on the server. Everything saved is keyed by the object's own identity rather than the scene it happens to be in, because the world moves objects between map tiles as you walk around. Objects placed by hand save only what changed about them; objects created during play save a recipe that recreates them.

**Worth knowing:** this system fails silently — nothing throws, the state is just gone — so a feature is only proven saved once you have quit, reloaded, and seen the value in the file.

### Project-wide settings *(ProjectConfig)*

The engine-level dials: engine and package versions, the physics settings, the named layers objects sit on, and the version-control rules that keep Unity's binary files intact. Gravity here is -18, nearly twice Earth, so anything copied in from a tutorial falls about twice as fast as its author intended. Notably the physics layers are only labels: every layer collides with every other layer, and every "do not hit that" rule is written into the individual query instead.

**Worth knowing:** the `Player` layer is declared but nothing is on it, so at least two pieces of code that try to exclude the player exclude nothing at all.

### The scene map *(Scenes)*

75 scenes exist, 68 of them ship in the build. One small boot scene is first, the main menu is second, and one root gameplay scene holds the managers; everything else in game — map tiles, cave interiors, the arena — is layered additively on top of it. Scene names are looked up through small named assets rather than typed as raw text, so the menu and the lobby cannot drift apart.

**Worth knowing:** five personal test scenes are in the build list and ship as content today, and the deathmatch arena scene is currently empty — it was emptied by a cleanup commit and nothing rebuilds it.

### Tests and checks *(Testing)*

Roughly 1,800 automated checks, all of them running in the editor without ever entering play mode, plus a script that type-checks the code headlessly and a two-process test that actually runs a host and a client against each other. Because none of it plays the game, anything needing real frames, physics or navigation is checked by hand or by that two-process run. A useful category here is "wiring" checks that open a prefab and assert its parts are actually connected — catching the case where the code is right and the asset is not.

**Worth knowing:** whole areas have zero tests — all of terrain and settlement generation, conventional weapons, audio, cutscenes, and most of the HUD.

## The world

### Loading the world in tiles *(WorldStreaming)*

The main world is 4000 by 3000 metres cut into 48 tiles of 500 metres square, and only the tiles near a player are loaded. Players, and anything else marked as an anchor, pull tiles in around themselves plus a second batch ahead of where they are heading; tiles leave after a ten-second grace period. Only the server does any of this, and it also moves creatures and vehicles into whichever tile they are currently standing on, telling everyone else about the move.

**Worth knowing:** twelve of the 48 tiles are deliberately empty padding with no ground at all, and anything positioned outside the grid gets clamped to the nearest edge tile — which is why a place 16 km away can read as the corner of the world.

### Generated landmarks, caves and settlements *(TerrainGeneration)*

Three separate generators that all run while designers work, never during play: mesa and cliff formations grown from a footprint you drag out, caves grown from a seeded room-and-corridor graph, and tile-based settlements. Each is a pure function of one seed number, so the same seed always gives the same result, and the output is baked to a mesh asset that the game simply loads. Nothing about the base ground shape is generated — that is authored and sliced into tiles by hand.

**Worth knowing:** only two landmark types survive, mesas and cliffs; a dozen others were deleted, and because scenes store the type as a number, those numbers must never be renumbered or reused.

### Where characters can walk *(NavMeshSystem)*

One single walkable-surface map is baked for the entire world at author time and simply switched on when the game starts — nothing is calculated at runtime. All 48 tiles are opened at once to bake it, which means editing any one tile invalidates the whole thing and there is no per-tile shortcut. Caves are excluded and carry their own separate bake.

**Worth knowing:** there are no jump or gap links anywhere in the project, so characters cannot cross a gap by navigation; every leap you see is hand-simulated movement, not pathing.

### Weather, fog and sky *(Environment)*

Sandstorms, volumetric fog, clouds and the day/night sun. Almost nothing is sent over the network: a storm is a roughly 30-byte record written once when it is born, and every machine works out where it is and how hard it blows from a shared clock. One shape function decides both what the storm looks like on screen and who takes damage from it, so the picture and the gameplay cannot disagree.

**Worth knowing:** installing a new screen effect is not just adding it to a list — the render pipeline keeps a second parallel list, and an effect added to only one of them sits in the asset and never runs.

### Doorways, interiors and teleporting *(SceneTransitions)*

Cave and building interiors load alongside the outdoor world rather than replacing it, so stepping back outside is instant and everything you left out there is still alive. A doorway is assembled from three interchangeable pieces — what triggers it, where it sends you, and what the screen does while it happens — so a new kind of door is one new file. Every instant move in the whole game funnels through a single teleport function that also tells legged rigs, riders and pathing agents to rebase their world-space state.

**Worth knowing:** a late-joining player is not placed into an interior other players are already inside, and items dropped in an interior are lost when the last occupant leaves unless they were explicitly set up to save.

### Portals *(Portals)*

Sprayable pairs of openings you walk through, treated as doors rather than windows: the surface is a stylised swirl, not a live view of the other side, so nothing has to line up visually. A portal is not a networked object at all — its placement travels as a message and each machine builds its own copy, which is why they work offline, on a host and on a peer alike. The opening's shape is paint, up to 24 blobs merged together, and the shader and the physics read the exact same shape, so a lobe you can see is a lobe you can walk through.

**Worth knowing:** trigger volumes never worked here — the collider is on a child object, so Unity never delivered the messages — and the crossing is instead swept by hand once per frame; reintroducing triggers would break it again.

## Characters and creatures

### The astronaut you play *(PlayerCharacter)*

The suited figure the player drives: walking, sprinting, crouching, jumping, first-person looking, the arms that hold whatever is in your hands, suit colour and dying. Every machine in a session carries a copy of every player's body — your own is switched on, everyone else's is switched off apart from the parts other people need to see, like stance and the aiming arms. The body is 3 m tall, the eyes sit about 2.45 m above the soles, and the view is first person only; the third-person cameras belong to mounts, ragdolls and spectating.

**Worth knowing:** anything hung on the camera — a torch, an attached prop — is invisible to every other player, because the whole camera is switched off on remote copies.

### Creatures, NPCs and turrets *(AgentSystem)*

Every creature, villager, enemy and gun emplacement is a body plus a stack of small behaviour parts that bid for control each frame; the highest-priority part that wants to act wins and the rest are ignored. Three decisions have exactly one owner each — who to fight, where to go, how to move — and where the body points is a separate second channel layered on top after the winner is picked. Wandering, patrolling, fleeing, chasing, keeping distance, taking cover, herding, formations, melee and ranged attacks are all separate parts you mix per creature. Caravans of NPCs exist as lightweight records travelling in a straight line and only become real bodies when a player gets close.

**Worth knowing:** a creature with no faction is invisible to every targeting system with no error at all, and a species is peaceful precisely by having *zero* relationship rows — adding one "for completeness" makes the whole faction attack on sight.

### What makes something a thing in the world *(EntitySystem)*

There is no single entity class and no central entity manager. Something becomes a proper world object by making three independent claims: that it is part of the changeable world worth saving, that it follows the player between the streamed chunks of the map, and that AI can see it. Four authoring presets exist for stamping the standard creature, NPC, enemy or vehicle setup onto a prefab in one click; the preset deletes itself once applied.

**Worth knowing:** death here means "switch the object off", not destroy — corpses stay registered and saveable, and code that read "still registered" as "still alive" once resurrected every dead creature on each reload.

### Procedural walking *(Locomotion)*

Legged creatures and walking machines do not play walk animations; their feet are placed on the actual ground by one shared walking engine, with four small swappable policies per machine — stride length, gait pattern, how the body rides and bobs, and foot shape. Shipped that way: ostrich, crab, horse, humanoid and the six-legged desert crawler vehicle. The walking clock advances by distance travelled rather than by time, which is why the feet can never skate.

**Worth knowing:** you cannot move a walker by setting its position — it silently overwrites that next frame, so teleports, respawns, portals and save restores all have to go through the proper teleport path.

### Health, damage and death *(Combat)*

Everything that can hurt anything — guns, gadgets, creature claws, cacti, sandstorms — funnels through one damage pipeline that the server decides, so a hit is billed exactly once no matter how many players are watching. Every machine draws its own bullet, tracer, impact and sound; only the deciding copy actually applies damage. Death and ragdolls hang off the same health events, and ragdolls are built at runtime from the model's own skinning weights, so one implementation covers all ten rigs with no hand-authored joints anywhere.

**Worth knowing:** damage multiplied by the number of players in the session is the classic symptom of a missing "this copy is cosmetic only" gate.

## What you carry and touch

### Hotbar and items *(Inventory)*

Four hotbar slots, each holding an item definition; selecting a slot spawns a fresh copy of that item into the hand and unselecting destroys it, and re-selecting the slot you are already on empties your hands. One prefab is both the thing in your hand and the thing lying in the sand, so pickup and drop are a round trip through the same asset. Because the held copy is thrown away on every slot switch, whatever an item remembers is stored on the slot instead of on the object.

**Worth knowing:** an item asset saved outside the items Resources folder is never registered — absent from the dev browser and every save slot holding it comes back empty, with no error anywhere.

### Gadgets you can use *(Artifacts)*

Every gadget, spell, scanner, throwable and hand tool that occupies a hotbar slot and fires on the Use button. Each use splits in two: the part that changes the world runs on one machine only, while the part you see and hear runs everywhere, and on the user's own machine immediately so nothing waits for a round trip. Around twenty exist — lightning spell, dragon bazooka, gravel blaster, repulsor gauntlet, sucker puncher, laser staff, grappling hook, lasso, leash, net gun, rocket turret, item and ruin scanners, jumping rod, portal spray can, wing pack. Held gadgets that run continuously stream at 15 ticks a second while the button is down.

**Worth knowing:** aim is captured once on the machine that actually has a camera and travels with the message — recompute it on the receiving end and every client's shot follows the host's crosshair instead of their own.

### The backpack you lay gear on *(Backpack)*

A physical inventory rather than a list: a deployable expedition rig whose seven flat faces are grids you literally lay items onto, rummaged in from a dedicated focus camera. Everything uses one 13.5 cm cell, 255 cells across the whole pack, and each item occupies a shape mask, so oddly shaped gear can interlock. Contents belong to the pack rather than to you, so a pack you set down keeps its gear.

**Worth knowing:** there is no snapping and no refusal message — the red ghost cells *are* the refusal, and clicking on red turns the item a quarter turn, which is usually the fix.

### Ropes tied between things *(LeashSystem)*

One button ties a rope between any two things in the world: creature to post, player to crate, anything to a moving vehicle. The rope is a fixed-length limit rather than a spring, so below its length it does nothing at all, and each machine draws its own copy and only ever pulls the end it owns. Rope length is set once when you tie it, and it sags and lies over the ground it crosses.

**Worth knowing:** the AI is never told it has been leashed — a roped creature keeps trying to walk where it was going, and that visible straining against the rope is the whole effect.

### Looking at things and pressing E *(InteractionSystem)*

Doors, levers, ship consoles, repair workstations, seats and helms, pickups, cave exits and dialogue all work the same way: look at a collider, press E. One ray picks the target, one resolver turns it into the label and prompt you read, and the thing itself owns whatever it takes to get its effect onto other machines. Prompts are on by default — anything interactable gets a readable name derived from what it is unless it is given a better one.

**Worth knowing:** trading is fully written but completely unauthored — no trader profile asset exists and nothing in any prefab or scene references it, so barter has never actually run in the game.

### The torch *(Flashlight)*

Toggled with L, built in three layers: an ordinary short-range spot light of about 40 m that lights the world for everyone, a cheap shadowless long-throw glow reaching 120 m that only certain terrain and cave surfaces respond to, and a screen-space cone so you can see the beam hanging in the air. The split is what lets the near light be bright without blowing out a wall a metre in front of you. The beam's visible length comes from firing a handful of probe rays and taking the shortest hit.

**Worth knowing:** there can only ever be one long-throw torch — that far-reaching layer is a single global slot owned by the local player's lamp, so other players' torches light the world with their ordinary spot light only.

## Getting around

### Riding and driving machines *(Vehicles)*

Every machine you can operate works one of two ways. Either you *mount* it — you drop into a seat, the camera and controls become the vehicle's, and your body is held there but still shootable and ropeable — or you board a *station*, where you keep your own body and camera and walk a deck, claiming one control at a time. The catalogue spans a rideable ostrich, a piloted six-legged walker, a sand sailer with no seat at all, the flapping-wing craft, and two spacecraft.

**Worth knowing:** Seats are addressed by their position number in the prefab, so reordering a vehicle's parts between builds quietly reassigns them — before that numbering existed, one press seated a player in all four of the lander's chairs at once.

### The lander you crash in *(PlayerShip)*

A 60-tonne walkable, drivable hover vehicle with four seats, which also flies the one-time crash landing that opens a world: the crew is seated in the air, everyone launches on the same frame, and the hull is walked down a fixed 26-second arc from 2200 m. Its side panels, boarding stair and sill platform all deploy from one switch, and taking the helm closes the lot. The only hand-authored part is the Blender interior; every collider, seat, socket and marker in the finished ship is generated from it by a script.

**Worth knowing:** Anything you add to the ship by hand in the editor is destroyed, silently, the next time that generator runs — every fix has to go into the generator instead.

### The flapping-wing glider *(Ornithopter)*

A 10 m ornithopter carried folded in your inventory and thrown open in mid-air; you fly it lying prone in a cradle. It has no throttle — speed is bought with altitude or with flapping, and flapping spends a stamina bar that only refills while gliding, so you get roughly six seconds of hard climb. Pulling back does not climb directly, it raises the wing's angle and the flight path curves up a moment later; push too far and it stalls, drops its nose, and recovers on its own.

**Worth knowing:** Crash damage is measured on how fast you close on the surface, not how fast you were travelling — gliding onto sand at 20 m/s costs nothing and a scraped wingtip costs nothing, while a held dive into a cliff is instantly fatal.

## What you see and hear

### Screens, menus and the HUD *(UI)*

Every menu page, overlay, hotbar, helmet display and floating world label is built in code at runtime — there is no UI art in the project at all, and the rounded panels, discs and chevrons are drawn into textures as the game runs. Two visual languages coexist: dark navy lettering over the live 3D menu set, and a near-black panel with a blue accent for anything opening over gameplay. One shared owner hands the cursor, the input and the clock between screens, so two overlays can be open at once and control is returned cleanly; time only actually freezes when you are playing alone.

**Worth knowing:** Never tint a label to show that a row is selected — the shared button animation rewrites the label's colour on every state change, so the tint survives exactly one frame; say it on a separate object instead.

### Scripted camera moments *(Cutscenes)*

Cutscenes here are plain components running a coroutine, with no Timeline and no Cinemachine: you drop one on an object, point a trigger at it, and hook up whatever should happen afterwards, all three pieces independent of each other. Only one can play at a time, and everything is purely local — a cutscene runs on the machine that triggered it, for that machine's player, and nothing about it crosses the network. The crash landing is the worked example of splitting a moment that must look right everywhere: the ship's flight is decided on the host, the fade and rumble are presentation each player runs for themselves.

**Worth knowing:** Camera shake does nothing anywhere in the game right now — the component that performs it sits on a prefab nothing references, so damage, the bazooka, the gauntlet, the gravel blast and the sucker puncher all shake an empty screen.

### Sound *(Audio)*

Sound runs on FMOD, but nothing asks for an audio file directly: code asks for a *meaning* — 71 named sounds such as "player jump" or "portal open" — and one catalog asset decides which event plays, how loud, how often it may repeat and how far away it can be heard. Sustained sounds like engines and ambience are owned by an emitter that must be stopped on both of the two ways an object can go away. Nothing is ever sent over the network; each machine plays its own sounds off state it already has, which is why a sound placed on host-only code is silent for everyone else.

**Worth knowing:** There is no FMOD project in the repository, only the compiled banks, so the 71 named sounds share just 19 real events between them and no genuinely new sound can be authored until that source project comes back.

### Getting into a game together *(Lobby)*

Hosting, browsing, joining, the roster and the team rules, built on Unity's lobby service. The lobby itself outlives the menu deliberately, so a host can start playing and still let friends in afterwards — there is no ready check, and a late joiner is simply pulled into the running world. The roster is not a list of rows: it is a rank of actual astronauts standing in the menu scene, with names and team plates tracking them on screen.

**Worth knowing:** A crash, a timeout or a killed process leaves your player identity sitting in the old lobby, and since anonymous sign-in hands you the same identity every launch, your next join is refused until the game sweeps and retries — and two editors on one machine sign in as the same person, so the second is always refused.

### Match types *(GameModes)*

Three unrelated ways to play. *Versus* is team PvP in the full streamed world — 2 to 8 teams of up to 12, capped at 24 seats, each team arriving in its own team-coloured ship — and it has no scoring and no ending; it stops when people leave. The *arena* is a bot deathmatch with three variants (team deathmatch, free-for-all, battle royale) sharing one scorekeeper, and the *story run* is the ordinary game with a timer and a win scene.

**Worth knowing:** The deathmatch arena scene is completely empty — no spawn points, no bots, no baked navigation mesh anywhere in the project — so the entire arena code path is orphaned today and nothing warns you before you start a match into it.

## How the game gets made

### From Blender to the game *(ArtPipeline)*

Every 3D asset starts as a Blender file in a source library Unity deliberately cannot see, sitting beside the script that generated it and a note recording how it was built. Colours never come from the model: they are linked from one shared palette of 54 materials that grows only when nothing existing serves, and the export makes them local on the way out. From there an export writes the FBX, Unity imports it, and a generator script assembles the prefab, its clips and its animation controller — the finished prefab is never wired by hand.

**Worth knowing:** Never re-run a generator over a Blender file that already exists — the file, not the script, is the truth, and several of them (the lander interior, the nomad, the six-legged vrescal) carry hand edits that exist nowhere else and would be gone forever.

### The build menus *(EditorTooling)*

Roughly fifty menu commands sit behind the editor's Tools menu and cover almost everything: rebuilding a creature, vehicle, weapon or item from its model; carving the world into streamed chunks and baking navigation into it; generating item icons; registering things for multiplayer; checking that saving is wired up. Commands named Audit, Report or Validate only look; commands named Build, Wire, Apply, Fix or Bake write to disk, and the two are kept as separate twins on purpose. Two hooks run automatically whenever a model is imported; everything else you invoke deliberately.

**Worth knowing:** In some sessions the asset database quietly goes read-only and throws away what a builder writes without raising anything at all, so a build can report complete success having changed nothing — the only defence is reading every written file back off disk afterwards and checking it.

## If you want more

- Read the themed chapters instead: start at [01-the-game.md](01-the-game.md), or see the
  [chapter list](README.md).
- For the exact technical answer on any system above, open the matching document in
  [../AI/systems/](../AI/systems/).
- For the current list of things known to be broken, see [../AI/DEFECTS.md](../AI/DEFECTS.md).
