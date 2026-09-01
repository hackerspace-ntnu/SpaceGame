# The Player

You are an astronaut, three metres tall, seen from inside your own helmet. You walk, you look, you crouch, you sprint, and you press a single key at whatever your crosshair happens to be resting on. That is the whole verb set, and almost everything else in the game — vehicles, items, doors, trading, dying — is reached through it. This page explains what that body can and cannot do, how the world answers when you look at it, and what happens to you when you run out of health.

## The body

The player is a physics body with a capsule around it, not a character controller walking a script. That distinction matters more than it sounds like it should.

The capsule stands **3 metres tall** in world scale. The astronaut is genuinely a big person — this is a suit with a life-support pack on it, not a human silhouette. The eyes sit **2.45 m above the soles**, tucked just inside the visor, and the camera never leaves that spot while you are on foot. Every third-person view in the game — the orbit camera on a mount, the camera that follows your corpse, the spectator view — belongs to some other system that borrows control of the screen. The player itself is first-person, always.

One number causes more confusion than any other: the player's own origin point is **about a metre above the soles**, not at the feet. If you are eyeballing clearance, seat heights or spawn positions, measure from the feet, not from the pivot. Everything in the codebase that gets this wrong produces a character floating a metre in the air or buried to the waist.

## Moving

Every physics step, movement takes your input, works out how fast you should be going, and **assigns** your horizontal velocity. It does not push you, it does not add a force — it writes the number. The vertical axis is deliberately left alone, which is why jumping, pogo-sticking and being launched by an explosion all work: they own up-and-down, movement owns side-to-side.

Speeds come in a fixed order of precedence: **crouching** is slowest, then **aiming**, then **sprinting**, with plain walking in between. Whichever condition is currently true and highest in that order sets the target speed; the actual speed eases toward it rather than snapping, so starting and stopping have weight.

In the air you keep about **30% of your usual steering authority**. You can nudge a jump, you cannot fly it.

The ground check is generous: you are still considered "grounded" with roughly **0.6 m of clearance** under your feet. This is on purpose — it keeps small bumps, stairs and slope crests from making you stutter between grounded and airborne states — but it means "am I grounded" is a slightly softer question here than it looks.

Falling hurts. Fall damage is computed by your own machine and then handed to the server to make official, like every other damage source in the game.

### The thing that surprises everyone: momentum gets deleted

Because movement *assigns* horizontal velocity every step, anything that gives you sideways speed is erased on the next physics tick unless it explicitly says otherwise. Get flung by a blast, hooked by a grapple, or thrown by a piece of terrain, and your momentum is confiscated in around **two tenths of a second** unless the system doing the flinging tells movement to carry the momentum.

This is not a bug so much as a design choice with sharp edges: it means the player is extremely responsive and never slides around on ice, and it means every knockback feature in the game has had to be taught to survive it. If you are designing something that pushes the player, assume the push will be eaten unless it says otherwise.

## Looking

Look is split across two clocks, which is invisible in play but explains a couple of oddities.

**Yaw** — turning left and right — rotates the whole body, and it is banked up frame by frame and spent as a single rotation on the physics step, so the body turns smoothly with the physics rather than fighting it. **Pitch** — looking up and down — is only the camera, and it happens every frame so that mouse aim feels immediate regardless of physics rate.

Sensitivity comes from the settings menu and is scaled down while aiming down sights, so the slow, precise feel while aiming is a deliberate multiplier and not just a lower field of view. Field of view itself is a base value from settings plus offsets that other systems can push (sprinting, grappling, vehicle speed) — nothing writes the FOV directly, it adds to the offset and lets go.

The cursor is re-locked to the centre of the screen **every single frame** while you have control. Any panel that wants a visible mouse pointer has to formally take the player out of gameplay control; it cannot just unhide the cursor and hope. This is the single most common cause of "my UI works but the mouse keeps snapping away."

## Crouching and sprinting

Crouch lowers the capsule and drops your eye height by **0.6 m**. It also refuses to let you stand back up if there is a ceiling in the way — the game checks for headroom before allowing the stand, so crawl spaces work correctly and you cannot clip yourself into a floor above.

Sprint is a **double-tap** rather than a held modifier, and it draws from a charge tank rather than running forever.

Both of these live in a part of the player that runs on **every machine in the session**, not just yours. That is the whole reason they were built the way they were: if crouching only existed on your own computer, other players would watch you glide around at full height while you crawled through a duct. Anything about the player that other people must see has to live outside the components that get switched off on remote copies.

## Hands, aiming and holding things

When you hold an item, an upper-body animation layer takes over your arms while your legs keep walking normally. Two values blend independently: how strongly you are in the **hold pose** for the current item, and how far you have raised it into an **aim**. Aiming can never exceed holding, so you cannot aim an item you are not really holding yet.

On top of that, inverse kinematics pulls your right hand onto a shared pivot point, so the weapon and the hand agree with each other rather than the hand floating near an animated pose.

There is exactly one camera-derived aim ray in the game, and everything that needs to know "where is this player pointing" asks for it rather than reading a camera itself. When you fire, the direction of the shot **travels with the shot** as data — it is never recalculated on another machine. If it were, every other player's copy of your bullet would be aimed down the host's line of sight instead of yours.

## What other players see of you

Your body exists on every machine in the session, but only *your* copy is switched on. On everyone else's machine your camera object and your HUD are deactivated, and your input, movement and look components are turned off. Your body is still there, still animating, still replicating its position — it is a puppet driven by the network rather than by a person.

The consequence is a rule worth knowing before you attach anything to the player: **anything parented under the camera is invisible to other players.** The camera object itself is off on their machines. The flashlight is the worked example — it is authored under the camera for the local player's benefit, and on remote copies it is moved onto a neutral pivot that stays active so other people can actually see the beam.

What does replicate: your position and rotation, your animation state (which carries crouching, what kind of hold pose you are in, and whether you are aiming), your head pitch, your torch, your name, your suit colour and your team, your health, and your inventory and backpack contents.

Your suit colour is a single swatch index. It recolours seven materials on the astronaut by name — including the visor — so a colour change is one small replicated number rather than a stream of material data.

## Interacting: look at it, press E

Interaction is a raycast, not a list of nearby things. Every frame, a ray goes out of the camera up to **20 metres**, and whatever it lands on becomes what you are looking at. There is no proximity registry, no trigger volumes you have to stand inside, and no held-to-charge interaction: interaction is a single press.

The arbitration rules along that ray are worth understanding, because they explain most "why can't I press E on this" reports:

- Your own body is skipped entirely.
- A **trigger** collider only answers if the interactable thing is on that exact object. Otherwise the ray passes straight through it. This is what stops a large "you are standing on the deck" volume from swallowing every control on a vehicle.
- A **solid** collider answers with its own interactable, or one on its parents. If it has none, it **blocks the ray**. A stray hull box in front of a lever silently kills the lever's prompt.

Availability is checked once and used for both the prompt and the press, so the crosshair can never light up on something that will then refuse you. Some things refuse *per player* — you cannot board a seat you are already sitting in, and an NPC currently fighting you will not chat — and that refusal hides the prompt too.

Prompts are on by default and derived rather than authored. If nobody wrote a label, the game humanises the name of the thing itself, so a door reads "Door" without anyone typing that. The default line is **"E: interact"**, and things with an opposite action add **"LMB: use"** — that is how "haul the rigging in / let it out" reads as one control instead of two.

Things you can press E on today include doors, levers, a repair workstation that eats a specific item from your hotbar, NPCs you can talk to, items and backpacks on the ground, mounts and their seats, vehicle stations like the helm and the rigging, articulated parts that fold and unfold, the ship, cave exits, and interior entrances.

**Execution is local to whoever pressed.** The press only ever runs on the machine of the player who pressed it — everyone else's copy never fires. Getting the *consequence* onto other machines is the job of the thing you pressed, and different things do it differently: doors and levers flip a single replicated bit, vehicle stations run a claim table on the server, the workstation counts progress on the server and broadcasts feedback, and dialog does nothing at all on the network because dialog is genuinely private to you.

**Trading is built but unauthored.** The barter system — N of one item for M of another, offered through a yes/no dialog and then a full-screen panel — is code-complete and has never been placed in the world. There is no trader in the game because nobody has authored one yet, not because it is broken.

## The screens over the world

Nothing in the UI is an art asset. Every panel, button, bar and rounded rectangle is drawn in code at runtime. There are two visual languages: the **navy over the live 3D set** used by the main menu, which sits in front of an actual 3D scene, and a **near-black panel with a blue accent** used by anything that opens over gameplay.

While you are playing you have:

- A **health bar** with numbers, driven by damage and heal events rather than polled.
- A **crosshair**. Its hover-brightening half has never run — it was never wired up, and that is a deliberate open question about the look rather than a bug waiting to be fixed.
- The **interaction prompt** — what you are looking at, what the buttons will do, and a progress bar when the thing has one.
- A **four-slot hotbar**.
- The **helmet overlay** — a visor layer with AR markers for nearby entities coloured by faction, points of interest, and two curved arcs that flare when you take a hit from a direction and decay afterwards. **H** toggles the helmet layer on its own.
- Floating **damage numbers** for hits you personally caused, and **nameplates** over other players that fade with distance and hide behind geometry.

Full-screen things open over the top: pause on **M** (not Escape), chat on **T**, and in dev mode an artifact browser on **I**.

Two rules govern all of them. First, only one thing owns the cursor, your input and the clock at any moment, and it is reference-counted — two panels can be open at once, and control only comes back when the last one closes. Second, **time only freezes if you are alone**. In a session with other players, opening the pause menu, chat or a trade panel keeps the world running for everyone including you. Chat does not freeze time even solo.

## Carrying and firing things

You have four hotbar slots, and the selected one is what your hands are holding. Using an item is a held stream rather than a single press — the Use button reports pressed, held and released, which is what lets a weapon charge, a spray can keep spraying, and a lasso keep twirling.

Weapons follow a strict split that is worth internalising because every usable item in the game copies it: there is a **decide** step and a **present** step. The decide step runs only on the machine with authority, and it is the one that spends ammunition officially and deals damage. The present step runs on **every** machine, plays the report, mirrors the local ammo count and draws its own copy of the bullet — with the damage suppressed. That is why the sound and the muzzle flash and the impact sparks appear for everybody while the target only loses health once.

The weapons that exist today are a basic projectile gun, a hitscan energy rifle that fires several rays with spread and falloff, and a ball-lightning weapon that is the only **charging** weapon in the game: the first press spawns and charges the orb, the second press launches it. There is also a growing set of gadget-shaped weapons — a laser staff, a gravel blaster, a rocket, a lightning spell, a repulsor gauntlet, a net gun — which live in their own documentation.

Ammunition and cooldowns travel with the item itself, not with you. Put a half-empty gun in your backpack and it comes back out half-empty; a cooldown is stored as the time **remaining**, so it does not expire while the world is loaded.

## Losing and regaining control

A surprising number of systems take your controls away temporarily: mounting a creature or a vehicle, a cutscene, opening a menu, dying, and switching to the spectator camera. They all work the same way — capture what was on, switch things off, restore afterwards.

**Cutscene mode** is the shared form of "you are still standing here but you are not driving": input, look and movement stop, the camera keeps rendering, and the cursor is freed. Every full-screen panel that opens over gameplay enters it. It is reference-counted, so two panels can be open at once and control only returns when the last one closes — and if a panel is ever destroyed without letting go, the game is left frozen with nothing able to thaw it.

The other side of this is the shared "may I react to a hotkey right now" check. The flashlight, the helmet overlay toggle, the map and the seat controls all consult the same question: is there a local player, and is its input currently enabled? The keys that *open* menus deliberately cannot use it, because by the time a menu is open the player's input has already been switched off — so the pause menu and chat each listen on their own private input channel.

## Dying

Death is a state, not the absence of controls. When your health reaches zero, a flag is set that says you are dead, and *that flag* is what everything else consults. Input, movement and look are switched off, the cursor is released, and the death screen appears.

That distinction exists because so many systems temporarily take your controls away and give them back — mounting, cutscenes, spectating, opening a menu. Each of them captures what was enabled and restores it afterwards. Without an authoritative "this player is dead", dismounting or leaving a cutscene would cheerfully hand a corpse its controls back.

Your body goes limp on **every** machine, not just yours. The ragdoll listens to the health event directly rather than waiting for a message, so other players see you fall over at the same moment you do. The camera moves out of your head when this happens.

Ragdolls here are not authored. There is no hand-built joint skeleton on any character; the bones are chosen at runtime by how much of the mesh each one actually influences, which is why one implementation covers all ten rigs in the game. Self-collision is off, and has to be — the collider shapes are estimated from bone lengths, so sibling limbs necessarily overlap (measured at 15 cm on one character). A ragdoll with self-collision on does not look solid, it vibrates.

**Respawning does not replace your body.** You click, the server finds you a position, moves you there, and *then* heals you back to full. The same body stands up; nothing is despawned and re-created. The order matters — heal first and you would briefly be alive at the place you died.

Death also survives a save. If you quit while dead and load back in, the save restores zero health, which raises the death event again — with a marker saying "this is a restore, not a fresh kill", so that loot does not drop twice and death sounds do not replay.

## Things that hurt you

Damage of every kind — bullets, melee, cactus, sandstorms, falling — goes through one pipeline, and the machine that has authority over the *victim* decides the result. Nothing calls damage directly. This is why a hit does not multiply by the number of players in the session: every machine draws its own bullet and its own impact effects, and exactly one of them bills the target.

Damage numbers need two separate signals to work, which is a nice illustration of the authority split: hits your own machine resolved (a crate, an un-networked creature) announce themselves locally, and hits the *server* resolved on your behalf come back as a message. Without the second, a client would shoot things all day and never see a single number.

## What survives quitting

Your state is keyed to your **profile**, not to the scene you were standing in. Quit in a cave and load back into the world, and you are still you.

What is written down: where you are and which way you are facing, your look pitch (your yaw rides along with the body rotation), your display name, your health, your inventory and backpack contents, your suit colour, whether your torch was on, the status effects on you, which interiors you have visited, and your portal pairing.

The world around you keeps its own notes: whether each door is open, whether a lever has been pulled and whether a one-shot lever has been spent, how far a repair workstation got, and what a trader still has in stock. Mounts you were riding, station seats and dialog progress are deliberately **not** saved.

Two failure modes here are worth knowing because they are both completely silent. Restores happen by going through the same replicated switch the interaction would have flipped — never by posing a transform — because a transform written directly is undone within the frame. And a save taken while your body was frozen can capture that frozen state; loading it back gives you a player who cannot move, with no error anywhere.

## Where this lives

- `docs/AI/systems/PlayerCharacter.md` — the body, movement, look, stances, aim rig, suit colour, and everything about what replicates.
- `docs/AI/systems/InteractionSystem.md` — the interaction raycast, arbitration rules, the full list of interactables, and trading.
- `docs/AI/systems/Combat.md` — health, the damage pipeline, weapons, projectiles, death and ragdolls.
- `docs/AI/systems/UI.md` — HUD, overlays, the menu families, and how cursor/input/time handover works.
- `docs/AI/systems/Inventory.md` and `docs/AI/systems/Artifacts.md` — what goes in the four hotbar slots and how using it works.
- `docs/AI/systems/Persistence.md` — what of the player survives quit and load, and why it fails silently when it doesn't.
