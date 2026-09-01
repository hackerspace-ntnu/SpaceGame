# Creatures and People

Everything alive in this world — a nomad walking to a market stall, a robot patrolling a ruin, an ostrich picking its way over a dune, a turret on a wall — runs on the same small idea. A creature is not a script with a big switch statement in it. It is a body with a pile of small opinions bolted on, and every frame those opinions bid against each other for the right to say where the body goes. This page explains how that bidding works, who decides to attack whom, how legged animals actually walk (this part is genuinely unusual and worth understanding), what lives in the world today, and what NPCs do when nobody is shooting at them.

## A mind is a stack of small opinions

Every creature carries a set of **behaviour modules**. Wander. Patrol. Flee. Chase. Keep distance. Take cover. Investigate a noise. Stay with the herd. Each one is small and does exactly one thing.

Once per frame the creature asks each module, in priority order: *do you want this frame?* A module either answers with a movement intention — go here, or stop and face that — or it declines and passes. **The first module that wants the frame wins**, and everything below it is not consulted at all.

The priorities are a fixed ladder, and knowing the numbers makes the behaviour of any creature readable at a glance:

- **Scripted (100)** — a cutscene or a designer is driving. Beats everything.
- **Override (30)** — flee. Running away outranks fighting on purpose.
- **Melee attack (23)** and **ranged attack (22)** — the attack modules, melee slightly above ranged.
- **Reactive (20)** — chase a target, take cover, investigate where a noise came from.
- **Social (15)** — flocking, herding, walking in formation behind a leader.
- **Ambient (10)** — approach someone to talk, back off to a comfortable distance.
- **Personality (5)** — idle chatter.
- **Fallback (0)** — wander, patrol. What a creature does when nothing else is happening.

Alongside that, **facing is a separate channel**. A module can say "I don't care where the body walks, but the head points *there*" — that is how a creature can strafe sideways while keeping its gun on you.

Three decisions each have exactly one owner, and this is the load-bearing rule of the whole system: **who to fight**, **where to go**, and **how to move** are three separate things, each answered in one place. A module that starts making one of those decisions for itself is precisely the bug the design exists to prevent.

Some components are not bidders at all — they are pure side effects, ticked every frame regardless: perception, hearing, taking damage, speaking. They observe and write down what they learned; they never claim the frame.

### The classic mistake

A module that is merely *waiting* must decline. If it instead answers "stand still", it has claimed the frame, and everything below it — including wander, including patrol — never gets asked. The creature stands motionless forever with a completely clean console. This is the single most common way to accidentally lobotomise a creature here.

## Seeing and hearing

Perception is a field of view plus a line-of-sight check. If a creature can see you, it writes that down: your position becomes its **last known position**, along with how long ago it was.

That memory is what makes the search behaviour work. Break line of sight and the creature does not instantly forget you — it walks to where you were, looks around, and eventually gives up.

**Sandstorms cut sight.** Perception range is multiplied by a visibility factor from the storm system, so creatures genuinely cannot see through weather; it is not a visual effect layered on top of unchanged AI.

Hearing works by broadcasting a sphere. Footsteps, alerts, hurt cries, deaths, gunshots and explosions are each a distinct kind of noise, and a creature that hears one decides per type whether to shrug, go and investigate, or go straight to aggression. There is also a deliberate **pack alert**: one creature that spots you can force its neighbours onto the same target, which is how a group wakes up together instead of trickling in one at a time.

## Choosing a target

Targeting does not simply pick the nearest thing. It scores candidates by an **effective distance** that is nudged by three things: a bias toward whatever it is already fighting (so it does not flip-flop between two equidistant enemies), a bias toward whoever last hurt it (so shooting something gets its attention), and a penalty for targets it cannot currently see.

Acquisition range is widened automatically to the longest range of the weapon the creature carries plus 5 metres, so a creature never ends up armed with a rifle it refuses to acquire targets at rifle range with.

## Factions, and who attacks whom

Every creature belongs to a **faction**, and one shared relationship table says how any two factions feel about each other. Two rules matter:

- A faction is always **allied with itself**.
- **Any pair with no row in the table is neutral.** Silence means indifference, not hostility.

That second rule is the whole basis for peaceful wildlife. A peaceful creature is one with **zero relationship rows** — nothing in the table mentions it — combined with a **provocation** component that says: I ignore everyone until someone hurts me, and then I fight back within a leash range, and then I calm down again after a while.

The trap is real and has caught people: adding a relationship row "for completeness" turns the entire faction hostile on sight, because the table has now been given an opinion where it previously had none. Today one wildlife faction appears in no rows at all and is genuinely peaceful; another is already hostile to the player.

The other silent failure is the opposite: **a creature with no faction at all is invisible to every targeting system in the game.** Nothing attacks it, it attacks nothing, and no error is printed. If a new creature stands around being ignored by the world, this is the first thing to check.

## Fighting

Melee and ranged combat are two behaviour modules, and they are opinionated about more than just the swing.

The ranged module owns the **whole engagement**, not just the trigger. It backs off to a preferred range, strafes, and keeps facing the target through the separate facing channel while it does. It does not simply stand where the chase module left it and shoot.

Both attack modules use **hysteresis**: the range at which they let go is deliberately wider than the range at which they engage, and a melee swing commits for a fixed duration once started. Without that, a creature standing exactly on the range boundary flips between "chase" and "attack" every single frame and stutters in place. Anything attack-shaped that is added later needs the same treatment.

NPC weapons come in two flavours. Some creatures carry a built-in weapon tuned by three small data assets — one for damage and the projectile, one for range and cadence and burst length, one for spread and how far it leads a moving target. Others hold **the same items the player holds** and fire them through the same code path, which is how an NPC can be given a real gun off the shelf.

Attacks replicate as **presentation only**. The machine driving the creature decides the shot and applies the damage; every other machine receives a small message saying "this creature just attacked, from here, in this direction" and draws its own tracer and impact. If the damage travelled too, the target would be billed once per player in the session.

There are also **turrets** — stationary guns that resolve their own targets rather than being handed one, including a multi-slot weapon rig that can show a different model per weapon.

Socially, creatures can flock (separation, alignment, cohesion), **herd** — where one animal's decision is rebroadcast to everyone sharing a herd id, so a group turns as one — or walk in a **formation** column behind a leader. All three sit at the same priority, above ambient behaviour and below anything reactive.

## Getting around

Underneath all the decision-making, something has to actually move the body, and there are several kinds of mover:

- **NavMesh walkers** — the majority. A standard navigation agent following a path.
- **Physics ground vehicles** and **hovercraft**.
- **Free-flying** things that move in three dimensions, paired with an air-roaming behaviour.
- The **ornithopter**, which flies on an energy model of its own.
- **Legged machines**, which have no navigation agent at all and own their own body — see the next section.

### One NavMesh for the whole world

The world's navigation mesh is baked once, at author time, over **all 48 chunk scenes at the same time** — not per chunk. There are no seams to stitch, because there are no tiles to stitch; it is one mesh loaded in a single step when the game starts, identical on every machine in the session. Nothing bakes at runtime.

The bake is more permissive than Unity's defaults: creatures will path up **60° slopes** and step over **0.8 m** ledges. Tiles are roughly 85 m across.

Three things about it are worth knowing as a designer:

**There are no off-mesh links anywhere in the game.** Not one. Creatures cannot cross a gap by navigating across it. Every jump and leap in the game is a piece of movement code simulating an arc, never a navigation feature. If you design a space with a chasm in it, the pathfinder will simply route around it or fail.

**A stale bake fails quietly in the editor.** Nothing checks freshness at runtime; edit the terrain and NPCs will happily navigate a world that no longer exists. There is a menu command to check, and the player build refuses to complete when the bake is out of date, but in the editor you get no warning at all.

**Caves are separate surfaces.** The interior layer is excluded from the world bake, so a cave adds its own navigation data when it spawns and removes it when it goes.

One consequence in the minigame arena: that scene is currently **empty**, so it has no navigation mesh at all. The spawn-point filter that is supposed to discard positions that cannot reach each other therefore does nothing and returns the authored spawns unchanged.

## How legged animals actually walk

This is the distinctive part of the project, and it is worth understanding even if you never touch the code.

**None of the legged creatures play a walk animation.** There is no walk cycle, no run cycle, no blend tree. Every footstep is computed. The creature decides where a foot should land, swings it there, plants it, and holds it fixed in world space while the body travels over it. If the terrain is uneven, each foot lands at a different height, because each one independently asked the ground where it was.

A walking machine is built from **four small policies** plugged into one shared engine:

- A **stride model** — how long a step is and how high the hips ride, per leg. Two flavours: legs splayed out to the sides (crab, crawler) and legs that come down under the hip (ostrich, horse, humanoid).
- A **gait pattern** — the phase offset of each leg, and how much of the cycle each foot spends on the ground. Ripple, alternating, trot, a crab wave, and the horse's canter.
- A **body motion** — how the torso rides: bobbing like a creature, or held level like a deck people stand on.
- A **foot style** — the shape of the swing arc and how the sole tilts. A flat pad, or a foot that rolls.

Everything else — measuring the rig, running the clock, choosing footholds, probing the ground, gravity, solving the leg joints — is shared.

### The rig measures itself

When a creature wakes up, it does not read authored numbers. It **measures its own body**: how far each leg can reach, how high the hip sits at rest, how big the footprint is, and from those, how long a stride it can take. The global step length is set by the **shortest** leg, so nothing over-extends.

Top speed then falls out of the geometry: stride length divided by how long a foot stays down. Nobody types a max speed for these creatures — if you make the legs longer in Blender, the creature gets faster, automatically and correctly. (Bipeds are the exception for turning rate, which is authored, because a two-legged thing has no long outboard leg to bound how fast it can pivot.)

### The clock runs on distance, not time

The gait clock advances by **how far the machine has travelled**, not by how many seconds have passed. Walk slowly and the legs cycle slowly. Stop and the legs stop mid-stride, exactly like a real animal.

This is elegant and has one savage failure mode: if anything ever clamps the machine's speed to zero, the clock stops, no foot's turn to step ever comes around, and the creature is frozen solid *with no error*. A whole session was lost to this once on the crawler. It is why several rules exist that look arbitrary out of context — no policy is allowed to return a value that could bring the machine to a halt.

### Order of operations, and why it looks wrong otherwise

Each frame the body is posed **first** — surveyed against the ground under its planted feet, fitted to a support plane, dropped down if any foot is over-extended — and only **then** do the feet decide where to step next. Reverse those two and every step is aimed from last frame's hip position, and the feet visibly trail the body. It reads as bad animation but it is an ordering bug.

Footholds are clamped **horizontally only**, and against where the hip will be **at touchdown** rather than where it is now. Both halves matter: clamp in three dimensions and you lift the foothold off the ground and the creature levitates; clamp against the current hip and every step gets cut short into a thrash.

### Climbing, and why the limits look conservative

Legged machines refuse ground steeper than **35°** — deliberately less than what the navigation mesh allows, so a creature will path onto a slope it then declines to climb, rather than the reverse. Refusal needs either a *sustained* grade over the whole run ahead or a single segment that is both too steep and rises more than the tallest single leg lift the machine can manage. Turning is never blocked, downhill is never blocked, and missing ground is never a refusal — an unloaded chunk is not a cliff. Those three exemptions are what keep the gate from latching shut and stranding a creature forever.

Ground probes deliberately ignore anything sitting on a loose physics body. This sounds like a technicality until you hear why: a player standing on the crawler's deck was being read as ground. The deck rose to carry the rider, the probe found the rider higher up, and the machine climbed steadily into the sky — but only when the player stood in the exact middle of the deck, where the one central ray was.

### In multiplayer, the legs run everywhere

The interesting choice: legged locomotion **simulates on every machine**. Only ownership of the body's position changes hands. On a machine that does not own the creature, the locomotion **follows** — it reads where the body has been moved to, works backwards to how fast it must be travelling, and animates the legs accordingly.

Neither obvious alternative works. Switch the locomotion off on remote copies and you get a creature sliding along with its feet perfectly still. Leave it fully on and it overwrites the network every frame — a mounted ostrich vanishes out from under its own rider on the other player's screen. Following is the only correct answer, and it means gait phase, footholds, body bob and neck motion are all re-derived locally from the motion that did replicate.

The gait is saved and restored, but purely for looks: it removes exactly one stumble per creature per load.

## The roster

**Ostrich** — a biped, and the most elaborate rig. The neck is not decoration: the spine spends the neck *undoing* the body's bob, so the head holds still in world space while the body bounces underneath it, which is what makes it read as a bird. Gaze snaps between points instead of sweeping. The neck's eleven vertebrae share the bend unevenly, and the bounce is driven by a proper second-order spring — the lag *is* the effect.

**Crab** — four to eight legs, configurable, with the leg count and swing slots all derived at startup from the rig rather than authored. It **travels across its own nose**: the wave runs sideways along the body, with front and back in antiphase, and the roles are worked out from where each leg actually sits rather than from its index. The shell hugs the ground rather than bobbing.

**Horse** — a quadruped whose gaits are one continuous function rather than separate clips. Walk becomes trot becomes canter becomes gallop as a single blend, with an asymmetric lead leg and a real suspension phase where all four feet leave the ground. Fore and hind legs are measured separately because they genuinely differ. A rider gets a second-order spring for the bounce.

**Humanoid** — the same policy set as the ostrich with every amplitude scaled down. It is the first rig with **forward-bending knees**, and the bend direction is measured from the rest pose rather than selected by a flag. The arms swing from the legs' own phase, so they can never drift out of sync with the walk.

**Desert Crawler** — a six-legged habitat vehicle. Statically stable: at least three feet are on the ground at all times, so it cannot tip. Its deck follows about **60% of the slope underneath**, capped — because people stand on it, and a deck that matched the terrain exactly would throw them off.

Beyond the legged creatures there are wheeled and hovering vehicles, flying creatures, mountable animals, and stationary turrets that resolve their own targets and never move.

## Being a first-class thing in the world

There is no `Entity` base class and no entity manager. Being an entity is a claim made by attaching parts, along three independent axes:

- **Is it part of the mutable world?** — does it deserve a save record at all.
- **Does it move between chunks?** — the world is streamed in chunks, and a creature that walks across a boundary has to be handed from one chunk to the next. Three policies exist: keep it in the always-loaded scene, migrate it to whichever chunk it is standing in, or leave it where it was authored.
- **Can AI see it?** — that is the faction component, and without it the creature is invisible as described above.

Two facts about death are worth knowing because they surprise people:

**Death is deactivation, not destruction.** A dead creature is switched off, not deleted. It stays in the world's list of live objects, and it stays saveable — that is deliberate, because otherwise corpses would vanish on every save and reload.

**Records are keyed by identity, never by location.** A creature that wandered two chunks away from where it was authored still finds its own save record, because the record is addressed to *it* and not to the place it started.

There is one other thing to know if you are building creatures: there are exactly **four** authoring profiles — a base agent, an NPC, a generic enemy and a vehicle. Each is a component you drop on a prefab, configure, and press **Generate** on; it then builds the whole component stack and removes itself. That is why no prefab in the project references one. A separate setup-guide file in the same folder is stale and still names profiles that were deleted long ago.

## Errands and caravans

NPCs have somewhere to be. A **task** names a *kind of place* and a dwell time — go to a market stall, stay a while — and never a specific position. The place is resolved when the NPC actually needs it.

The same planner drives two very different things, and that is the clever part. Live NPCs walking around in front of you use it. So do **virtual groups**: caravans that exist only as small records, sliding along a straight line across the world map, making the same task decisions as a real NPC would. When the player comes within a spawn radius, the record turns into actual prefabs standing in formation, each running its own AI. Move away and, at a larger radius, they fold back into a record. The two radii differ on purpose so a caravan does not flicker in and out at the boundary.

Idle chatter is a small behaviour that speaks the NPC's *current task* out loud when you get close, on a shared cooldown so a crowd does not all talk at once. It is one of the only behaviours that keeps ticking on machines that are not driving the creature, because talking is presentation rather than decision.

**Dialog is completely local.** Talking to an NPC involves no network traffic at all — it is your conversation. It also goes silent while that NPC is currently fighting *you*, which is a nice touch: you cannot chat with something that is trying to kill you, but a bystander of the same species will still talk.

**Trading exists in code and has never been placed in the world.** The barter system is finished — offers, a yes/no prompt, a full-screen swap panel, stock that runs out and persists — but no trader has ever been authored, so there is nobody to trade with.

## Things that reliably surprise people

- **A creature that ignores everything** is usually missing its faction, not missing a behaviour.
- **A creature that stands perfectly still with a clean console** is usually a module claiming the frame while waiting, or a legged machine whose clock has stopped.
- **Feet that skate** mean the animation *rate* was never matched to the ground speed — picking a faster clip is not the same thing as making the legs turn over faster.
- **A provoked NPC that walks toward you instead of running** has its speed authored as a walk; the chase behaviour asks for a run, and there is nothing above the authored speed to give it.
- **Every NPC swinging its barrel to follow the host's head** happens when a weapon is not told it is being aimed externally — the server owns all the NPCs, and a weapon with no external aim points where its owner's camera points.
- **Animation trigger names genuinely disagree with each other** across the codebase, misspellings included. One character's controller carries two spellings of "die" for exactly this reason.
- **Loot dropping again on every load** means a death reaction ran during a save restore. Restoring a dead creature raises the death event again by design; consequences have to check whether this is a restore.
- **Prefab-building scripts overwrite prefabs wholesale.** Components added by hand in the Inspector vanish the next time someone rebuilds. One creature lost its save component this way.

## Where this lives

- `docs/AI/systems/AgentSystem.md` — behaviour modules and priorities, targeting, factions, perception, noise, the module roster, NPC tasks and caravans.
- `docs/AI/systems/Locomotion.md` — the procedural legged system: the four policies, rig measurement, gait clock, footholds, climb gate, and per-creature notes.
- `docs/AI/systems/EntitySystem.md` — what makes something an entity, chunk migration, identity, and the four authoring profiles.
- `docs/AI/systems/NavMeshSystem.md` — the single world bake, its settings, the absence of off-mesh links, caves, and how agents get snapped onto the mesh.
- `docs/AI/systems/Combat.md` — damage, death, loot and ragdolls, which creatures share with the player.
- `docs/AI/systems/MountSystem.md` — riding the creatures that can be ridden.
