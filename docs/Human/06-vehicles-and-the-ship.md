# Vehicles and the Ship

SpaceGame's machines fall into three shapes, and knowing which shape a thing is tells you almost everything about how it will feel to use. Either you **mount** it and take it over completely, or you **board** it and keep your own body while claiming one control on a walkable deck, or it drives itself and you are a passenger. On top of that sit two special cases worth their own attention: the ornithopter, which is a real flight model rather than a vehicle with a fly button, and the lander, which is both the ship you live out of and the thing that flies the crash landing that opens a world.

---

## Three ways to operate a machine

### Mounting — you become the vehicle

Right-click an ostrich and you *are* the ostrich. Your body is put in the saddle, your camera is taken over, and your movement keys go to the machine instead of your legs. This is the model for anything you ride: creatures, walkers, aircraft, the lander's helm.

Three moving parts make a mount: something that owns the **seat and camera**, something that reads your **input**, and a **motor** that knows how this particular machine moves. New vehicles are assembled from those, not written from scratch.

A few decisions worth knowing:

- **Your body is parented into the seat and made kinematic**, but its colliders are *never* disabled. They are ignored pairwise against the hull instead. Turning them off would remove the rider from every raycast, every overlap, every interaction probe — you would become unshootable and unropeable the moment you sat down.
- **Ownership of the vehicle moves to the rider.** Your local input moves the machine, and that motion replicates outward. Without this the rider is steering a body they do not control, and the authoritative copy overwrites their input every tick.
- **Only the local rider gets a view.** Cameras, the audio listener, look input, the visor: all gated on "is this my rider". Everyone else on every machine still replays the seating, so you can see other players sitting in their saddles.
- **The third-person camera is spawned unparented.** Parent it to the vehicle and the vehicle's motion is applied to it twice — measured at 48% frame-to-frame variance versus 2.6%. It jitters, badly, and the cause is not obvious from looking at it.
- **One seat is one mount point.** A hull can have several — the lander has four — and every message about seating carries which seat it means. Before that was true, one press seated a player in all four chairs at once.
- **Escape gets out.** That is read by the seat itself, so even a passenger chair with no controls at all can be left.

A big hull needs one more piece: if every collider on a 20 m machine is a mount point, you board it by brushing against it anywhere. Large vehicles switch off direct interaction and put a single boarding control in the cockpit instead.

### Stations — you keep your body

The opposite trade. No seat, no camera takeover; you stand on a walkable deck and **claim one control**. Several crew can each hold a different one. This is how the sand sailer works, and the pitch for it is that the controls *are* the deck: you walk from the helm to a rigging line and back while the thing is moving.

Claiming is arbitrated by the server, which owns the table of who has what and the control's actual value. The person holding a control drives it locally and ignores the echo of their own input coming back, so it feels direct.

Boarding a deck is deliberately **not** a mount: look at the hull, right-click, and you are placed on the deck as yourself.

Walkable decks need one non-obvious support. A hull that moves by having its transform written imparts no friction to anything standing on it — you would slide straight off the back. So the hull's per-frame movement, including rotation about its pivot, is re-applied to every body inside its carry volume.

And the matching trap, which has caught this project more than once: **a machine's ground probes must ignore loose bodies.** Otherwise a player standing mid-deck is read as ground, the walker steps onto its own passenger, and it climbs into the sky. (A seated rider is safe by construction, being both kinematic and parented.)

### Passengers — the machine is the driver

An NPC riding a mount is a different model entirely and shares almost no code with the above: the *mount* is the intelligent thing and the rider is switched-off cargo along for the ride. They share only the ability to be evicted from a seat and the collision handling.

---

## The vehicle roster

- **Ostrich** — a rideable creature, and the simplest complete example of the mount stack. Legged, direct-interaction, with a built saddle pose.
- **RigWalker** — a piloted six-legged walker with a cockpit and a walkable platform. You board it at the cockpit control, not by touching a leg.
- **DesertCrawler** — the same legs with no seat at all: an AI-driven walking station carrying a dig, claw and collector rig. You ride along and work it.
- **DuneFoil** — the sand sailer, and the flagship for stations. No mount anywhere on it. A boarding ramp, a helm, four rigging stations, and a mooring that holds it steady while nobody is aboard.
- **DuneOrnithopter** — the flapping-wing aircraft. Its own section below.
- **ShipRV** — a hovering spacecraft with a cockpit control, eight articulated moving panels and a swappable hull shell.
- **PlayerShip** — the lander. Its own section below.
- **Rover** — an autonomous explorer with suspension IK. Not rideable, and currently only in a test scene.
- **DuneRider** — a self-contained rigidbody mount driver with its own input handling. It exists in code but is **on no prefab today**.

Underneath, motors differ in what they can even be asked to do, and this is the thing most likely to make a new vehicle feel wrong: a navmesh-driven or plain rigidbody mount steers like a tank and can jump and leap; a hovering machine holds a ride height and simply ignores the vertical axis entirely; a flying machine has throttle, yaw and a real altitude axis; a legged driver is bound to its gait. A motor that does not implement jumping does not error — it just ignores the button. If a new vehicle feels dead on a key, that is usually why.

Vehicles also have **moving parts** as a first-class concept: hinged and sliding panels that rotate or slide about their own origin, some of which the player can operate directly, some of which open and close automatically when someone takes the controls.

---

## The ornithopter

A 10 m flapping-wing aircraft you carry **folded in your inventory** as a wing pack, deploy in mid-air, and fly prone from a cradle. It is the most physically-modelled thing in the game and the one that rewards learning it.

### How it actually flies

The flight model is a point mass on a path, and it keeps **two angles apart**: where the craft is *moving*, and where it is *pointing*. The difference between them is the angle of attack, and angle of attack is what makes lift.

That single fact is the whole feel. **Pulling back on the stick does not climb.** It raises the angle of attack, which generates more lift, which curves your flight path upward a moment later. There is a lag, and it is real, and it is the difference between flying this thing and driving it.

**There is no throttle.** Speed is bought with altitude or with flapping, and flapping costs stamina. Roughly six seconds of full-effort climbing empties you; about four and a half seconds of gliding fills you back up. Exhaustion fades in rather than cutting off — as stamina drains, your beats get weaker. Thrust pulses on the downstroke rather than applying smoothly, so the craft surges with each beat.

**Control authority scales with airspeed.** Slow down and the stick goes soft, proportionally. Stall and you keep only about 30% of your authority. Roll self-centres.

**Tucking is how you go fast.** Pulling the wings in sheds about 65% of the wing area — less lift, but also much less drag. That is the dive.

At the shipped numbers — 150 kg, 14 m² of wing — it stalls at about **11.3 m/s** and glides at about **9:1**. It is a good glider.

### The stall

The stall has no special-case code at all, and that is the point. Past 15° of angle of attack the wing starts letting go and lift fades toward a fraction of its peak over the next 12°. The nose drops, the flight path steepens, the angle of attack falls back below the threshold, and the wing flies again. The recovery is emergent.

One number holds this together: post-stall lift **must stay above zero**. Set it to nothing and the nose never falls, and the stall never ends.

### Getting into the air

The wing pack refuses to open on the ground, and the check is worth understanding because it is what makes cliff edges work. You may launch if you are **already falling** — no ground within 0.6 m — **or** if there is no ground within 6 m from a point 1.5 m in front of you. That forward ray is the ledge test. Refusals are logged deliberately, so "it will not launch" is answerable.

You carry your run-up: the flat speed you had going off the edge comes with you, and there is a floor on launch speed so a standing jump still works.

### Coming down

Landing and crashing are the same code path with different numbers, which is why both feel consistent.

A downward probe finds ground within 1.4 m and reports a touchdown — so **a wing flown gently onto sand simply lands**, and never has to wait for a collision to notice. A collision reports one too, because a cliff face is never *underneath* you; without that, the machine grinds along the rock forever.

Damage is computed from **closing speed** — how fast you are moving *into* the surface — not from raw speed. The effect on play:

- Gliding onto sand at 20 m/s at a shallow angle: closing speed 2.1 m/s, **no damage**.
- Flying level into a cliff at 20 m/s: **35 damage**.
- A held dive at 42 m/s into the ground: **100 damage** — a full player's health.
- Scraping a wingtip along a wall: **nothing**.

That is the behaviour you want: skill in the approach angle matters more than bravery about speed.

### Making the wings look alive

The animation detail that matters most is **wing twist**. The wings bite on the downstroke and feather on the upstroke — 20° down, 12° up. Without it the wings just wave and the craft looks like it is being carried rather than flying. Gliding wings still breathe slightly rather than locking rigid.

Nothing happening at beat frequency is smoothed — the flap, the twist, the gear spin all come straight off the beat phase. Only the slow channels (bank, pitch trim, the tail fan) get any damping. Smooth the fast ones and the whole thing goes rubbery.

### Multiplayer and its one rough edge

The pilot's machine simulates and its position is the truth, so stick and wings live on one machine with no round trip. Other machines keep the flight component running but switch it to *measuring* speed and attitude from the replicated position rather than running a second, divergent simulation.

**Known limitation, recorded honestly:** a player joining while an ornithopter is already airborne sees it flying with its wings shut, until it lands. It is purely cosmetic, and closing it means the deployment state needs to be replicated as ongoing state rather than as a one-time event.

A mid-air save reloads correctly and relaunches the craft, rather than dropping it out of the sky — but stamina and flap phase restart, which nobody has ever noticed.

---

## The lander

The lander is the ship you arrive in: a **60-tonne, walkable, drivable hover vehicle** with four seats, deployable sides, a boarding stair, and eleven salvage sockets you strip parts out of over the course of a run. It is also the machine that flies the crash landing.

### Where it comes from

Almost uniquely in the project, **the lander's prefab is entirely generated**. One person hand-built the interior in Blender; that file is the only authored thing, and tooling opens it read-only. A script exports it, and a builder generates the whole prefab from what comes out: pivots, colliders, seats, cockpit, sockets, networking, savers, everything.

The practical consequence is blunt and worth putting on a poster: **any component or Inspector tweak added to the lander prefab by hand is destroyed by the next build, silently.** Every fix belongs in the builder. There is a verification pass whose entire purpose is to fail loudly when a part goes missing, precisely because those losses are otherwise invisible — a ship with its seating component quietly removed will fly its whole descent with nobody aboard.

Its collision is a **baked convex decomposition**, not one collider per mesh. That is what makes the interior genuinely walkable. Note that the canopy dome deliberately gets no collider at all, because a 3 m character's head sits inside the glass.

### Living on it

**Four chairs.** The front-left is the helm and it drives the ship; the other three are passenger seats you enter through a trigger volume around each chair. Seat poses are measured off each chair's actual cushion geometry rather than taken from the chair's transform, because the imported chairs arrive at roughly 150× scale with baked rotation that reads as 180° whichever way they are facing. Get that wrong and your feet are buried a metre through the deck.

**One switch opens the whole side.** Four telescoping leaves, the boarding stair and the sill platform all run off a single press, and if they are in mixed states the switch resolves toward closing everything. An invisible 32° ramp does the actual carrying — the player capsule has no step offset and physically cannot climb 0.7 m stair treads. The rear door is its own switch and droops 40° into its own walkable ramp.

Taking the helm closes all seven moving parts; getting up reopens them.

**Salvage sockets** track what has been pulled out, saved and replicated as a single bitmask.

**A repair station.** On the deck opposite the gear wall, just aft of the map projector, stands a bench with a scrap hopper on it, a lamp on top and a gauge above the hopper. Walk up with ship scrap in your selected hotbar slot and right-click to feed it one piece; anything else in your hand is refused with a buzz. Five pieces bring it online — the lamp goes from red to green, the gauge reads ONLINE and the grinder on the bench starts turning. The count is saved with the ship and every crewmate sees the same gauge.

One naming trap that catches people reading the code: there is an unrelated scenery rocket in the project with a launch button and a full idle/flight/crash state machine. That is not this ship. **The lander has no take-off.**

### The crash landing

Starting a new story world plays a one-time arrival, and it is a proper set-piece.

The spawn point is resolved **once**, terrain is streamed around it, and that same point becomes the impact site. The ship spawns at the top of a closed-form descending spiral — **2200 m up, with a 900 m lateral budget** — and every player is spawned at the hull and seated in it a frame later.

The launch is then **held until every connected player is actually seated**, up to a 12-second timeout, and then all hulls launch on the same frame. The descent takes **26 seconds**, with the hull's own drivers switched off for the duration (leaving them running while the descent is teleporting the same body is what "the screen came apart" looked like). At the bottom it snaps to the exact landing pose and hands control back.

About 1.6 seconds after touchdown the crew are released and stand up on Escape at their own pace — nobody is ejected. A three-minute backstop force-empties the seats if someone has wandered off from their keyboard.

Some deliberate design decisions inside that:

- **Riders are never reparented into the ship during the descent.** A player's position is controlled by their own machine, so each machine holds only the bodies it owns in place, and it does so late in the frame. Doing it earlier lags by exactly one frame, which reads as the cabin shaking.
- **The cutscene is triggered by "my player just got seated", not by the descent.** The descent runs on one machine only; driving the cutscene from it would have played the whole arrival for the host alone.
- **A loaded world never re-crashes.** A restored save short-circuits before any of this.
- **The world is marked as arrived-in even mid-descent.** Saving halfway down and resuming into a half-finished crash is worse than a crash that got cut short.
- If the arrival cannot happen at all — no ship, no room — everyone simply spawns normally. It degrades rather than blocking.

In versus modes the same machinery builds a **whole per-team formation at once**: one team-coloured ship per team, launched together on mirrored arcs with staggered altitudes. All landing sites are measured before any hull spawns — it is all-or-nothing, so nobody ends up buried in a hillside while the other team lands cleanly.

The most common way for the arrival to fail is also the least obvious: **if the scene has no spawn point, nobody spawns at all**, the arrival never runs, and the console shows a single error.

---

## What survives a save

Worth a short note, because it shapes what these machines are allowed to be.

A mount saves only **who is sitting in it** — a reference to the rider, nothing else. Seat position, camera offset and perspective come from the prefab, and the rider's pose comes from being parented into the seat. Storing a world position too would be a second, competing answer to the same question.

Restoring seats is deliberately patient: players arrive one at a time, so a rider reference that cannot be resolved yet is kept and retried rather than thrown away. It refuses to seat a corpse, and it goes through the ordinary seating path so that ownership moves correctly and every machine sees it.

The ornithopter stores whether it was flying and how fast, and relaunches after everything else has loaded. The lander stores its wreck pose, its hover state, the position of every door and stair, and its salvage progress. Seat occupancy during the arrival is not saved, because the descent is over by the time any save is legitimate.

---

## Where this lives

The dense technical versions, for when you need to change these rather than understand them:

- `docs/AI/systems/Vehicles.md` — the mount stack, stations, decks, moving parts, the full vehicle catalogue and the recipe for a new rideable.
- `docs/AI/systems/MountSystem.md` — a short redirect that keeps the motor capability matrix and the mount setup dials.
- `docs/AI/systems/Ornithopter.md` — the flight model, every tunable, launch/landing/crash and the wing rig.
- `docs/AI/systems/PlayerShip.md` — the generated lander, its builder, the four seats, and the arrival sequence end to end.
- `docs/AI/systems/Multiplayer.md` — ownership, authority and the messaging these all sit on.
- `docs/AI/systems/Persistence.md` — how vehicles and riders survive quit and reload.
