---
artifact: StormFlask
status: design
authority: Server
continuous: false
uses: [StatusEffects, SurfaceCoat, LightningSpell]
updated: 2026-09-07
---

# Storm flask (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Uncork it and a cloud forms over the point you aimed at. It rains for 30 s and throws bolts at
whatever is tallest underneath. It has no idea who you are.

## What it does

Aim at a patch of ground and use. A small dark cloud forms above it and stays. Under the cloud it
rains: fires go out, ground turns `Wet` and a little slippery. Every 2.5 s or so the cloud picks the
tallest body beneath it and hits it with a bolt — the same bolt the Lightning Spell already fires.

Tallest includes you. Standing next to your own storm on high ground is a way to be struck by it,
and standing in a ditch while a creature towers over you is a way to make it useful.

## Numbers to start from

| Knob | Value |
| --- | --- |
| Cloud radius | 12 m |
| Cloud height | 15 m above the aimed point |
| Duration | 30 s |
| Bolt interval | 2.5 s |
| Bolt damage | Lightning Spell's |
| Charges | 1 per flask |

## How it is built

- `UseAuthority.Server`. The cloud is a registered network prefab: it lives for 30 s, everyone must
  see the same one, and it does damage.
- Target selection is the cloud's own job on a timer — the tallest `StatusReceiver`-carrying body
  whose position is inside the radius, by world height. Simple and readable is the point; a clever
  rule would be invisible to the player.
- Bolts are the existing Lightning Spell presentation: locally instantiated
  `Lightning.prefab` → `Lightning.vfx` on every machine, with the strike point replicated. The bolt
  is a flat plane painted by a shader graph, so it needs the same billboarding care that item does —
  see the Artifacts gotcha about effects that vanish from one half of the compass.
- Rain clears `Burning` on anything under the cloud, and lays a `Wet` [SurfaceCoat](SurfaceCoat.md).
  Both are one call each into systems that already exist.

## Multiplayer

Server-authoritative: the cloud picks targets and bills damage. Rain and bolts are drawn from the
replicated cloud, so a client sees the storm without a message per raindrop.

## Persistence

An uncorked flask is 30 s of world state and is not saved. The `Wet` coat it leaves is not saved
either. A corked flask is an ordinary item with a charge.

## Model and art

About 0.2 m, held in a fist and thrown rather than aimed. Clean issued equipment: a heavy sealed
flask with a machined collar and a ground-glass stopper, and behind the glass a churning grey core
with light moving in it. Moving parts: the stopper pops on use, and the core visibly empties as the
cloud forms. The cloud is the real art asset — a small, flat, angry disc of vapour with rain
streaking out of it, readable from the ground at 50 m so nobody wanders under one by accident. No
hold pose; it is a bottle.

## Risks

- **A cloud over a streaming boundary.** It belongs to a chunk. A cloud that unloads mid-storm while
  its rain is still drawn on another machine is the kind of split this world's streaming grid
  produces.
- **Tallest is not always sensible.** A parked vehicle, a spawned foam ramp or a dropped crate on a
  ledge can be the tallest thing under a cloud. That is either funny or broken depending on how often
  it happens — worth a playtest, not a special case up front.
- **Bolt spam.** Several flasks thrown at once is several clouds each picking a target every 2.5 s.
  Cap the number of live clouds per world rather than discovering it in a session.
