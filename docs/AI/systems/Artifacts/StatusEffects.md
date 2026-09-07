---
system: StatusEffects
status: design
layer: items
summary: "Timed flags on a body — burning, frozen, slick, inflated, foamed — owned by the server, presented everywhere"
consumers: [Flamethrower, CryoSprayer, SlickCan, InflatorNozzle, FoamGun, StormFlask]
updated: 2026-09-07
---

# Status effects (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Five of the nine new artifacts leave a timed condition on whatever they hit. Without a shared
system that is five copies of the same timer, the same replication, and the same "what does a
creature do about it" question. This is that system, designed once.

## Model

- **One receiver per body.** `StatusReceiver` sits on anything that can carry a condition: the
  player character, an `AgentController`, a loose Rigidbody prop. It holds a small list of active
  statuses and nothing else.
- **A status is a kind plus an expiry.** `StatusKind` is an enum — `Burning`, `Frozen`, `Slick`,
  `Inflated`, `Foamed`. Reapplying a kind refreshes its expiry rather than stacking a second copy;
  a body is burning or it is not.
- **The server owns the flag; every machine owns the look.** The server adds, ticks and expires.
  Presentation — flames, frost, a sheen, a wobble — is rebuilt locally from the replicated flag, so
  no visual effect travels on the wire.
- **Behaviour lives with the kind, not with the receiver.** Each kind gets a small class
  (`BurningStatus`, `FrozenStatus`, …) that says what applying, ticking and clearing do. The
  receiver never grows a switch over kinds, which is what would turn it into a god class.
- **Creature reactions hook in once.** A burning or slick creature reacting is an `AgentSystem`
  concern; the receiver raises `StatusChanged` and a single `StatusReactionModule` on the agent
  translates it into a flee, a stagger or nothing. Five artifacts do not each learn about AI.

## Kinds

| Kind | Duration | Effect |
| --- | --- | --- |
| `Burning` | 5 s, refreshed | Damage over time. Creatures panic and flee. Cleared by rain or water |
| `Frozen` | 10 s | Helpless — no movement, no attacks, pose held. A hard hit shatters and kills |
| `Slick` | 20 s | No ground grip. Ropes, lassos and nets slide off |
| `Inflated` | While pumped, deflates when not | Scale and mass driven by one signed scalar |
| `Foamed` | 10 s | Held in place. Broken early by damage |

## Multiplayer

- Server-authoritative in every case: these are contested world state, not the holder's own body.
- One message carries add and remove, addressed to the affected body. Nothing per tick — a client
  that knows the kind and the expiry can draw the whole thing without further traffic.
- Damage over time is billed on the server only. Clients draw flames; they never subtract health.
- Late joiners get the active list when the body's state is first sent.

## Persistence

Statuses are seconds long and deliberately not saved. A quicksave taken while burning loads a body
that is not burning, which is the honest outcome — restoring a fire the player was already escaping
would hand back a situation they had left. `Frozen` is the one worth a second look: a creature
frozen at the moment of a save reloads thawed and free, which is a small gift rather than a defect.

## Risks

- **Frozen and a save.** A frozen agent has a suppressed motor. If the suppression is written to the
  agent rather than derived from the status every frame, a load restores a creature that never moves
  again. Derive it; do not store it.
- **Two authorities.** `Slick` on a *player* changes how their own movement resolves, and player
  movement is owner-authoritative. The server owns the flag, but the owner's movement code reads the
  flag and applies it — the server never writes the player's velocity. Same split as the rest of the
  codebase.
