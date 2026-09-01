# What You Carry

Everything a player holds in SpaceGame goes through the same few ideas: four hotbar slots on the belt, a rack of strange gadgets we call **artifacts**, a physical backpack you actually lay objects onto, and a rope you can tie between any two things in the world. This page is about what those toys are, how they get into your hand looking right, and the handful of decisions underneath them that surprise people the first time they meet them.

---

## The hotbar

You carry **four slots**. That is deliberately small — the backpack behind you is where the rest of your life goes, and the hotbar is what you have chosen for the next two minutes.

Scrolling picks a slot. Selecting the slot you already have selected **deselects** it and empties your hands, which is how you put something away without dropping it. Picking something up off the ground fills the first free slot; if there is no free slot, the pickup falls through to the backpack instead of being refused.

Two things about slots that shape everything downstream:

- **One object is both the held thing and the thing lying in the sand.** There is no separate "world model" and "hand model". The gun you drop is the same gun you were holding, physics switched back on.
- **Equipping builds a brand new copy; unequipping destroys it.** Nothing on the object in your hand survives a slot switch. Anything that must survive — a weapon's remaining ammo, a grapple's anchor, a wing pack's aircraft — is written back into the *slot*, not left on the object. This is why swapping slots mid-fight does not silently reload your gun.

Dropping throws the item with a small impulse; it freezes and settles on first ground contact rather than rolling forever down a dune. Dropped items are real, saved objects — quit and reload and they are still lying where you left them.

There is a dev item browser on **I** in dev mode, which is how most people first see the full roster.

---

## What an artifact is

An **artifact** is any item that occupies a hotbar slot and does something when you press Use. That is the whole definition. A spell, a shotgun, a scanner, a pogo stick and a can of portal paint are all the same kind of thing to the game.

Every artifact answers the same button, and the game is careful about **which machine actually decides what happened** versus which machine merely shows it. That split is the single most important idea in the whole item system:

- The *decision* — a creature loses health, a turret exists now, a charge is spent — happens on one machine only. Usually the server. Sometimes the owner, when the entire effect is on the player's own body, because a player's position is controlled by their own machine and anything the server pushes there is quietly overwritten within a tick.
- The *presentation* — the sound, the muzzle flash, the rope, the beam, the smoke — happens on **every** machine, and on yours immediately, before anything travels over the network. Nothing you press ever waits for a round trip to feel like it fired.

Two consequences worth internalising:

**Aim travels; it is never recomputed.** The one machine with a live camera stamps where you were looking into the message. Every other machine reads that. If a peer recalculated aim from "the camera", on a server that camera is the *host's* camera — and every client's shot would follow the host's crosshair. This bug is prevented structurally rather than by discipline.

**Anything erratic gets one seed.** Shotgun spread, a firework rocket's corkscrew, a net's tumbling flight: the owner rolls a single number, that number rides along with the shot, and pure maths derives the rest. Every machine draws exactly the same chaos, and the machine that bills the damage is tracing the same path everyone else is watching.

Some artifacts are **continuous** — hold the button and they stream at 15 ticks a second until you let go. Some of those keep streaming *after* you let go, because they are self-timed: the laser staff burns for its full three seconds whether or not your finger is still down. And every continuous item needs a timeout, because a release is a single message and a disconnect sends none — without one, a dropped connection leaves a beam burning forever.

Artifacts can have **charges**. Run one dry and it is removed from your inventory. An item that refills itself has to explicitly say "do not remove me", which is a small trap that has bitten more than one gadget.

---

## The gadget roster

This is the fun part. Everything below exists and works today.

**Throwers and blasters**

- **Dragon bazooka** — fires a corkscrewing firework rocket that bursts into a litter of whelps. The reference implementation of "one seed, identical flight everywhere". Also the reference *size*: the whole item scale ladder is anchored on this thing at 1.25 m.
- **Gravel blaster** — a pipe shotgun. One roll of the dice decides both the pellet spread and whether this is the one-in-ten shot that backfires in your face.
- **Repulsor gauntlet** — an instant thundergun cone. Everything caught in it ragdolls.
- **Sucker puncher** — a steam ram. Heavy damage and a launch on whatever you actually hit, plus a shockwave shove for everything standing near it.
- **Laser staff** — a three-second arc with a ten-second recharge. The canonical continuous item; if you are writing a new hold-to-use gadget, read this one first.
- **Lightning spell** — the simplest possible aimed use: a bolt strikes the point the caster was looking at. Good first thing to read.
- **Rocket turret** — plants a rocket-launcher turret on the ground in front of you. A deployable, so unlike a projectile it genuinely has to be a registered networked object.

**Ropes and snares**

- **Grappling hook** — dart, taut rope, swing and winch. Owner-driven, because the whole effect is your own body flying.
- **Lasso** — hold to twirl (the twirl *charges*), release to throw. Its own simulated rope, its own struggle behaviour for whatever you caught.
- **Net gun** — fires a net that unfolds along a shared, closed-form arc so every machine draws the same flight, then drapes over what it catches. It refunds its own charge as it recharges.
- **Leash** — tie a rope between any two things at all. Gets its own section below.

**Tools and toys**

- **Item scanner** — a handheld CRT that finds loose salvage within 100 m. Notably it does not sweep the world with a physics query; things that want to be found register themselves, and the scanner reads the register. Entirely cosmetic — it changes nothing, it only tells you.
- **Ruin scanner** — a top-down cone of light. Anything hidden that the cone's rays touch is told to reveal itself. The "secret" marker can be dropped on anything.
- **Portal spray can** — sprays paint, and the paint *is* the portal aperture. Hold to keep spraying.
- **Jumping rod** — a pogo stick. Plant it and bounce. It was prototyped as a mount and deliberately rejected as one, because riding a pogo stick reads wrong; it stayed an item.
- **Anti-gravity potion** — drink it and float for about five seconds. The server takes the bottle; your own machine kills your gravity.
- **Ship part** — carry a hull module and fit it into its socket. Candidate holes light up as you approach with one in hand.
- **Wing pack** — worn rather than gripped, and the only item that spawns an aircraft. See the vehicles page.

**Ordinary weapons**

Alongside the gadgets there is a small conventional arsenal: a **basic gun** that spawns a straight-line bullet, an **energy rifle** that is hitscan with configurable rays per shot, spread and falloff, and **ball lightning**, the one charging weapon — the first press spawns the orb and charges it, the second press launches it, and it wanders on noise with its own light while it flies.

All of them, gadget or gun, funnel damage through a single route. Every machine draws its own bullet and plays its own impact sound and sparks; exactly one of those bullets is the one that actually bills the target. When that gate is missing, the symptom is unmistakable and is the first thing anyone checks: **damage multiplied by the number of players in the session.**

Damage numbers appear over what you hit, and nameplates over players. Getting them to appear for *everyone* took two separate signals — one that fires on the machine that decided the hit (which covers crates and test cubes and creatures that are not networked at all) and one the server announces to the others (without which a client, whose shots are resolved on the server, would see no numbers at all).

Death produces a **ragdoll**, and the ragdolls are worth a note: none of them are authored. There is no hand-built joint chain anywhere in the project. The skeleton is derived at runtime by picking bones according to how much of the mesh each one actually deforms, so one implementation covers all ten rigs. There is a global budget — too many limp bodies and the oldest *settled* one freezes. Self-collision is off and must stay off: the limb colliders are estimated from bone length, so two thighs hanging off the same hips necessarily overlap. A jittering ragdoll is that overlap being resolved forever.

---

## The flashlight

Toggled with **L**, and worth understanding because it is three layers pretending to be one lamp.

There is a real spot light with a deliberately short range — about 40 m. Distance comes from a second, cheaper layer: a long-throw contribution reaching 120 m that shaders opt into by name. It samples no shadows, which is exactly why it is affordable. The split is what makes tuning possible at all: the real light only has to cover the near field, so it can be bright without blowing out a wall a metre from your face.

The third layer is the visible shaft of light in the air. It is not ray-marched. For each pixel it finds the closest point on your view ray to the beam axis and shades by how far off-axis and how far along you are. Which means: **looking straight down your own beam does not brighten the screen.** That is intentional and correct, though it does confuse people who expect a volumetric.

One real limitation: **there is exactly one long-throw torch.** The slot it writes into is singular, so it belongs to whoever owns the local camera. Everyone else's spot light is real and lights the world correctly for all viewers, but only the local player gets the long throw and the visible shaft. Giving every player their own would mean turning that one slot into an array — a rendering change, not a networking one, and nobody has needed it yet.

---

## How things look right in your hand

This is quietly one of the most-iterated parts of the game, because an item that sits wrong in the fist ruins a gadget no matter how good its behaviour is.

**The hand has a frame, derived from the skeleton.** Not authored per character — measured. It has an origin in the middle of the fist, an axis pointing the way an item points, and an axis toward the thumb side. Held items point **along the fingers**, and the palm-normal direction depends on which hand it is. This matters because the astronaut is a 3 m character whose hand is about 1.7 times a human hand; nothing generalises if you eyeball it off mesh bounds.

**Size is a size, not a multiplier.** Each item declares how long its longest axis should be, in metres. Zero means "trust the artist's scale". These are not arbitrary — they are **brackets on a ladder**, anchored on the dragon bazooka at 1.25 m, so the whole armoury reads as one family in the hand. Four items are pinned as hand-fitted and must never be rescaled: the sucker puncher, the repulsor gauntlet, the item scanner and the wing pack.

Two related gotchas that bite people:

- **Measurement can be narrowed.** Without it, the lasso's 4.4 m of coiled rope is what gets measured, and the handle shrinks to a splinter.
- **The positional nudge is in metres, in the hand's own frame, and is not scaled by the size.** It moves the item within the fist, and it does not shrink when the item does.

**Colliders are switched off in the hand.** All of them, through the whole hierarchy, along with any physics bodies. A live collider held in your fist shoves you around the level. There is an escape hatch for the rare item that needs one, and it is an escape hatch, not a default.

**Hold poses are automatic.** An item that should be gripped gets an upper-body hold pose added for it without anyone wiring it. Worn items opt out. The one rule the pose has to respect is that it **must yield to movement** — an unmasked hold layer animates the legs too, and the character glides across the sand instead of walking.

A last piece of institutional memory: several of the more elaborate items — the laser staff, the dragon bazooka, the gravel blaster, the repulsor gauntlet, the sucker puncher, the net gun, the jumping rod, the wing pack — have their prefabs **generated by a script**. Hand-tuning one in the Inspector works beautifully until the next time anyone runs that script, at which point the tuning is gone with no warning. Tune the script.

---

## The backpack

The backpack is not a grid of icons. It is a **physical expedition rig** you wear on your spine, unshoulder onto the ground, and lay real objects onto.

Press **B** and it comes off your back with a toss and unfolds, and a dedicated camera flies in — about 2.5 m past the rig, 1.5 m up, angled 38° down. Time keeps running. You are not in a menu; you are kneeling over your kit while the world carries on around you, which is exactly the tension the design wants.

**Seven flat faces** are usable surface: a fold-out leaf, a rack, a long-goods rail, two back panels, two wings. Every face is measured in one global cell of **9 cm**, read off the rig's own webbing pitch, and every face is an exact multiple of it — 255 cells in total, with no wasted hem anywhere. Capacity is *cells occupied*. Not a count of items, not a weight, not an area: the actual squares your gear covers.

Each item fills a **mask** of cells. Most are simple rectangles derived from the item's own measured footprint, but a mask can be drawn by hand, which is what lets two awkward L-shaped things interlock instead of each reserving a bounding box.

The interaction is deliberately blunt about refusal. **There is no message and no error cursor: the red cells *are* the refusal.** You see the exact squares your item would occupy, green or red, plus the free/taken lattice of the whole face you are hovering. And clicking on red **turns the item a quarter turn** — the refusal and its most likely fix are the same click. A symmetric item with no useful rotation to offer gets a small flash instead.

There used to be a magnet that snapped your item to the nearest legal spot. It is gone, on purpose, and tests exist specifically to keep it from creeping back. What you see judged is exactly what you clicked.

The one held gesture in the whole mode is **flipping the leaf**: grab the bare board, drag through its arc, release past halfway and it commits. **R** does the same thing as a toggle for people who would rather not drag.

Other things worth knowing:

- **Contents live on the pack, not on you.** Set the pack down and it keeps its gear. Someone else can walk over to it.
- **While the pack is on your back, only the rack is reachable.** The leaf, the lash line and both wings ride the hinge and are simply not there until you deploy.
- **A full hotbar is not a refusal.** Drag something from the pack into an occupied slot and the displaced item goes back onto the pack, preferably onto the same face.
- **The same item asset can never be in the pack twice.** The layout is keyed by item identity, so a second copy is refused up front rather than dying confusingly at the drop.
- Other players see your rig, its unfold, every item lying on it, its holders and its straps. When you lift something, it disappears from *your* view of the mat immediately and stays visible to them until the server accepts the move — which is the honest thing to show, since until then it has not actually moved.
- The layout is the server's word. Clients ask; they never write. A save stores each item as a face, a position on it and a rotation — and deliberately never stores footprints, so resizing an item later does not corrupt old saves.

One known wart, recorded rather than hidden: some class-level comments in the code still describe the old design where the limit was surface *area*. That is legacy prose. The real limit is cell occupancy.

---

## The leash

The leash is one rope tied between **any two things** — a creature to a post, a player to a crate, a crate to a moving vehicle.

It works on one button and no second key. Empty hands, click a thing: a rope now runs from it to your hand. Holding a rope, click anything solid: tied, and the rope is now a world object with a life of its own. Click nothing: you let go. Empty hands and you click a *rope*: untie. One held rope at a time; unequipping drops what you are holding, and tied ropes stay tied.

The physics is the interesting bit, and it is defined by what it is *not*:

**It is a distance limit, not a spring.** Below its length it does nothing at all. There is no bungee, no wobble, no restoring force.

**It must never be a grappling hook.** The rope only ever *removes* velocity — it can arrest something running away from it, and it can repay accumulated stretch as a position correction, but it can never make anything faster than it already was. There is a test whose entire job is to assert that the launch-the-player code path does not exist. This is a design boundary, not an implementation detail: the grappling hook is a different item and gets to fling you; the leash never does.

**Rope length is fixed, and pays out exactly once.** Tie across a gap wider than the rope and it pays out to cover it, once, and that new length is what everyone uses from then on. Measured separately per machine, a tie onto anything moving settles a metre apart on different screens and stays wrong forever.

**Each machine resolves only the ends it owns.** Not "the server does it" — a ridden mount belongs to its *rider*, so a server-only rule made ropes hold host-ridden animals and pass straight through client-ridden ones. Both endpoints replicate and the length is shared, so both machines compute the same overshoot and each applies only its own half.

**Breaking is the server's verdict**, broadcast to everyone. Two machines measuring stretch from their own interpolated endpoints will sometimes land on opposite sides of the threshold, and that disagreement — one machine's rope snapped, another's did not — is permanent.

A few things people notice and ask about:

- **A creature on a leash strains against it.** A kinematic creature reports no velocity, so nothing gets arrested on its side and the correction settles into a standing overstretch of roughly half a metre for something walking off at 4 m/s. That is not a bug; that lean *is* the look.
- **The animal does not know it is leashed.** Its brain keeps pathing exactly as before. It is simply being pulled, and the visible tension between where it wants to go and where it can go is the whole effect.
- **A rope lying on a hillside can be clicked where it lies.** There is no collider on it, and it is not getting one — clicking a rope is solved analytically against the points actually drawn on screen, which is why a sagging rope resting on the ground still wins against the ground it rests on.
- **A rope tied to something un-networked stays local to your machine**, because that object's physics already differs from everyone else's.
- Ropes are saved globally rather than filed under either end — a rope belongs to neither of the things it ties. File it under one and it vanishes when that one unloads; file it under both and you get two ropes.

The **lasso** is a different system, despite looking related. It is a throwable loop with its own simulated rope and its own twirl-charge; it does not share this code.

---

## Where this lives

The dense technical versions of everything above, for when you need to change it rather than understand it:

- `docs/AI/systems/Inventory.md` — hotbar slots, equipping, pickup/drop, item state, the grip frame and the size ladder.
- `docs/AI/systems/Artifacts.md` — the Use/Present split, hold streams, charges, the full artifact catalogue and the rules for adding one.
- `docs/AI/systems/Backpack.md` — the expedition rig, cell grid, shape masks, focus mode and the placement rules.
- `docs/AI/systems/LeashSystem.md` — the rope, the constraint maths, per-end ownership and the break verdict.
- `docs/AI/systems/Combat.md` — the single damage route, weapons, projectiles, death and runtime ragdolls.
- `docs/AI/systems/Flashlight.md` — the three lighting layers, the beam shader and the one-torch limitation.
