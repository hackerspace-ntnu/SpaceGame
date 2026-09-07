---
system: Containment
status: design
layer: items
summary: "An entity folded into an item and let back out — the captive rides the item's identity"
consumers: [VacuumCanister]
updated: 2026-09-07
---

# Containment (design)

Not implemented. Design only. Read [../Artifacts.md](../Artifacts.md) first.

Putting a living thing inside a carried object and getting it back out later, intact, after a save,
a death and a rejoin. Only the Vacuum Canister uses it today, but the mechanic is general enough
that it is worth its own boundary rather than living inside one artifact.

## Model

- **A captive is a saved record, not a disabled GameObject.** Capturing serialises the entity to the
  same record the world save already writes, then despawns it. Keeping the object alive but hidden
  would leave a body in every physics query, every targeting sweep and every save — the trap the NPC
  rider work already hit once.
- **The full container is a different item.** An empty canister and a full one are distinct item
  identities, the way an empty and a charged oxygen bottle are. That is what makes the captive
  survive being dropped, looted, saved and reloaded: the record travels with the item, in the slot's
  `ItemState` bag.
- **Filling is contested, not instant.** A target resists. The resistance meter is
  `SnareStruggleMeter`, already shared by the Net Gun and the leash's hogtie — a third consumer, not
  a third implementation.
- **A contained player is never removed from play.** They keep an input path: mashing shortens the
  wait, and it ends after 5 s regardless. A player who puts the controller down still gets out.
- **Release spawns the record back.** Uncorking restores the entity at the aimed point with its
  health, its faction and its state as captured.

## Size gate

Anything up to and including a mount fits. The test is the target's collider bounds against the
container's rated volume, so a bigger canister is a prefab variant rather than new code. A rider is
released with their mount, or the capture is refused — never separated silently.

## Multiplayer

- Server-authoritative throughout. Capture despawns a networked entity and release spawns one; both
  are the server's business.
- The struggle meter's messages already exist for the net gun and are reused unchanged, including
  the rule that the captive's own struggle message is sent on the *captor's* relay.
- A contained player's camera needs somewhere to be. The simplest correct answer is the container's
  position with input suppressed, which is the same treatment the hogtie already gives.

## Persistence

- The captive's record lives in the item's `ItemState`, so it survives quit and load, and it survives
  the captor dying and dropping the canister.
- A captive whose prefab id cannot be resolved on load must fail loudly. Silently dropping the record
  is how a creature disappears with nothing in the console — the failure mode
  [../Persistence.md](../Persistence.md) exists to prevent.

## Risks

- **Containing a player across a disconnect.** If the captor leaves, the canister must still be in
  the world with its record intact. Since the record is on the item and the item drops, this works —
  but it is the first thing to test.
- **Double release.** Uncorking twice must not spawn two creatures. Clearing the record is part of
  the same server-side step that spawns.
