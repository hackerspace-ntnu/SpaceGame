# Leash System

A rope you can tie between any two things in the world. Tie a creature to a post, tow a
crate, rope another player, or drag a walker along behind you.

Reworked 2026-08-23 — see [the rework design](../superpowers/specs/2026-08-23-leash-rework-design.md)
for what was wrong with the version this replaced and why each piece changed.

---

## 1. Using it

**Hook, then hook.** One button — the ordinary Use press — does all of it.

| Hands | Aim | What happens |
|---|---|---|
| Empty | A thing | A rope runs from it to your hand |
| Empty | **A rope** | That rope is untied and gone |
| Empty | Nothing | Nothing |
| Holding a rope | Anything solid | Tied. The rope is now a world object and none of the artifact's business |
| Holding a rope | Nothing | You let go — the rope is gone |
| — | Unequip | The held rope is gone; tied ropes stay |

Two clicks tie anything to anything, in either order. You hold one rope at a time.

A rope tied at both ends survives the artifact, the item swap, the chunk its ends were
streamed in from, and a save/load — until somebody clicks it.

**There is deliberately no second key.** There used to be a `dropAction` bound to right-click, and
it was wrong twice over: it was read in `Update`, which runs on *every* copy of the artifact on this
machine including the ones in other players' hands, and an `InputActionReference` reads local input
— so pressing it dropped every remote player's rope on your screen and never dropped yours on
theirs. It also bypassed `Use`/`Present`, which is the only channel an item has that reaches other
machines. Clicking at nothing already means "let go" and goes through that channel.

---

## 2. Files

| File | Role |
|---|---|
| [Leash.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/Leash.cs) | One rope. Two ends, the constraint, breaking, the live registry |
| [LeashEnd.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/LeashEnd.cs) | One end: what the knot is tied to, and how that thing may be pulled |
| [LeashRope.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/LeashRope.cs) | Drawing only. No physics reaches this file |
| [LeashGround.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/LeashGround.cs) | What is underneath the rope, so it can be drawn lying on the world |
| [LeashedBody.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/LeashedBody.cs) | The half of the constraint only a player's own machine may run |
| [LeashAttachable.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/LeashAttachable.cs) | Marker on a leashed object; answers "what is tied to me" |
| [LeashArtifact.cs](../../Assets/Game/Scripts/Items/Artifacts/Leash/LeashArtifact.cs) | The equipped item. Aims, and turns clicks into ropes |
| [LeashSaveable.cs](../../Assets/Game/Scripts/Core/Persistence/Adapters/LeashSaveable.cs) | A global saver, because a rope belongs to neither of its ends |
| [LeashConstraintTests.cs](../../Assets/Game/Editor/Tests/LeashConstraintTests.cs) | Pins convergence, the mass split, the safety clamps, and that it is not a grappling hook |

Assets: `Prefabs/Items/Artifacts/Gadgets/Leash.prefab`,
`Resources/Items/Artifacts/Leash.asset`, `Art/Materials/Items/Rope_Leash.mat`,
`Art/Textures/Items/rope_braid_{albedo,normal}.png`.

---

## 3. Who resolves what

**Each machine resolves the ends it owns.** This is the organising decision and everything
else follows from it.

| End | Resolved by |
|---|---|
| A player — in their hand, or roped by someone else | That player's own machine |
| Anything else | The server, or the only machine there is offline |
| Static | Nobody. It anchors |

Every machine builds the rope (`LeashArtifact.Present` runs everywhere) and every machine
draws it. Rope length is a constant and both endpoint positions are replicated, so both
machines compute the same overshoot and each applies only its own share.

**Why not simply "the server runs it".** A player's Rigidbody is the one thing the server is
not authoritative over: their NetworkTransform is owner-authoritative, so anything the server
writes into that body is overwritten by the owner's next state update, silently, within a
tick. The previous design worked around this by banking what the rope owed each player and
shipping it at 10 Hz as `NetMsg.RopeTug` — a message whose receiver was never installed on any
player, so no rope ever pulled anyone. That whole path is gone; `RopeTug` is a burnt number.

`LeashedBody` is the player half, and its `[DefaultExecutionOrder(200)]` is load-bearing:
`PlayerMovement.FixedUpdate` assigns `rb.linearVelocity` outright — `Lerp(current, desired, 1)`
while grounded — so a pull applied before it runs is not reduced, it is deleted.

---

## 4. The constraint

A distance limit, not a spring. Below its length the rope does nothing at all.

Past it, **two separate terms**, and keeping them apart is what makes it stable:

```
share  = (1/mySelf) / (1/myMass + 1/otherMass)      // immovable ⇒ 0
arrest = max(0, separationRate) * share             // velocity is only ever REMOVED
step   = stretch * share * correction               // the error is given back as a POSITION
```

A position error corrected by *adding velocity* does not converge: the velocity it adds is
still there next step, so the ends accelerate toward each other, sail through the correct
distance and collide. So velocity is only ever taken off, and the positional error is repaid
as a position, which carries no momentum into the next step. The error then decays
geometrically — a rope 4 m overstretched closes in about a fifth of a second, with no
overshoot and no ringing. `LeashConstraintTests` pins exactly that.

Both terms are scaled by `share`. Two ends each cancelling the *full* relative speed removes
it twice over, and a pair that each over-corrects toward the other is a rope that hums.

`share` is by inverse mass — the lighter end moves further, so a player tows a barrel and is
towed by a vehicle. Mass comes off the prefab and is therefore the same number on every
machine, which is what lets the two machines resolving the two ends agree on the split
without exchanging anything.

### How each kind takes it

| End | How |
|---|---|
| dynamic Rigidbody | `AddForceAtPosition` **at the knot**, plus a direct position step. The knot is what makes a crate roped by one corner turn to face the pull instead of sliding flat |
| player | velocity and position directly, no torque — their capsule is upright by construction and spinning it would tip the camera |
| `NavMeshAgent` | `agent.Move(step)`, the documented API for external motion. **Not `Warp`**: that re-projects onto the NavMesh and resets navigation state, and doing it every physics step was the visible teleport-jitter every leashed creature had |
| plain kinematic | `MovePosition` |
| static | nothing |

### Ceilings

`maxCorrectionSpeed` (25 m/s) and `maxCorrectionStep` (0.5 m) bound what one step may do. An
end that is suddenly hundreds of metres away has been teleported, streamed in, or carried off
by a vehicle; chasing that error at full rate is how a rope slingshots things across the map.

---

## 5. Breaking

A rope snaps when it is stretched more than `breakStretch` metres past its length **and stays
that way** for `breakTime` seconds. `breakStretch = 0` is unbreakable.

Stretch rather than force, because distance is the one quantity every machine agrees on — both
ends' positions are replicated — so every machine reaches the same verdict with no message to
send. Force cannot do that; it depends on masses and velocities that differ per machine. It is
also the version a player can see coming.

---

## 6. Length

Fixed. Tying across a gap wider than the rope pays out **once**, to
`min(distance + payOutMargin, maxPaidOutLength)`, and never again.

The version this replaces rewrote its own length to whatever gap it happened to land across,
every time it was tied, and `LeashSaveable` stored the result — so an 8 m leash quietly became
a 25 m one, permanently, with no way back.

---

## 7. Drawing

`LeashRope` is a `[Serializable]` tuning class that folds into the artifact's Inspector, the
same shape as `GrappleRope`. It runs on every machine including the peers who resolve nothing,
which is the whole reason it is worth doing well — a peer watching someone drag a crate sees
this and nothing else.

- **Sag from true slack.** `h = 0.5·L·√(1 − (d/L)²)`: zero when the rope is pulled straight,
  `L/2` when the two knots meet, and within a fifth of the parabolic arc-length answer between.
  Points are laid on `4h·t(1−t)`, pinned at both knots.
- **Tension.** The rope narrows and a fine shiver runs down it as it goes taut.
- **A bite.** A crack runs back down the rope when it is tied or goes tight.
- **Rounded joints**, 32 segments, and a braid albedo + normal map tiled per metre so the
  strands stay the same size on a 2 m rope and a 20 m one. The normal map is what makes a
  camera-facing line read as a cylinder.
- **The hand end tracks the live muzzle** while the artifact is equipped, falling back to a
  baked player-root offset when it is not.
- **It lies on the ground rather than through it** — see §8.

Textures are generated, not painted — see §9.

---

## 8. Resting on the ground

A slack rope sags, and a rope between two things standing on the ground sags *into* it. So every
point between the two knots is clamped to sit `groundClearance` above whatever is underneath.

`LeashGround` does the probing, kept out of `LeashRope` so that file stays free of physics. Three
details are load-bearing:

- **The ray starts above the straight line between the knots, not at the sagged point.** A point
  that has sagged into a hillside is *inside* the mesh, and a downward ray from inside a mesh
  reports nothing — so probing from where the rope currently is goes blind in precisely the case
  this exists to fix. Starting above the chord also lets the rope ride *over* a rise between its
  ends, and keeps the ray short: a taut rope high in the air casts a 1.5 m ray that hits nothing.
- **Loose bodies are not ground.** A hit on anything with a non-kinematic Rigidbody is skipped, so
  a rope does not come to rest on a crate that is itself falling. Same rule, same reason, as
  `WalkerGround.IsLooseBody` — that one was learned by watching a machine climb into the sky on top
  of its own passenger.
- **Neither end is ground for its own rope.** Without this a rope tied to a creature's flank gets
  lifted onto its back, and one tied to the underside of anything is lifted straight through it.

The clamp runs *after* the idle sway, not before: the sway has a vertical component, and applying
it last would push the rope back down through the surface it was just lifted out of.

The two knots are never moved — they are tied to things, and lifting one would visibly detach the
rope from what it is attached to.

**Limit:** the probe looks `groundProbeAbove` (1.5 m) above the chord, so a rope strung between two
points with a whole hill in between still cuts through the hill. Raising that value widens the
search but lengthens every ray.

---

## 9. Untying — clicking a rope that has no collider

A rope is a `LineRenderer`. It has no collider and is not getting one: a chain of capsules along a
curve that moves every frame would cost more than the rope does, and would start blocking bullets,
footsteps and every other raycast in the game. So picking one is analytic —
`LeashRope.RayToSegment` against the points actually drawn, which means a rope sagging on the
ground is grabbed where it *lies* rather than where it would be if it were taut.

Two clamps in that solve are load-bearing. Solved as two infinite lines, a rope **behind** you is as
pickable as one in front — you would untie ropes by looking away from them. And past the end of a
segment the nearest point is its **endpoint**, not somewhere off along the line it happens to lie
on. Where the ray runs parallel to the rope every point is equally close and the answer is a tie;
the near end is returned, so a rope running away from you is grabbed where it starts.

The rope only wins over whatever solid thing the aim also hit if it is nearer along the ray, within
`grabRadius`. That slack is what lets a rope **lying on the ground** be clicked at all — the rope
and the ground it rests on are at very nearly the same distance, so without it the ground wins every
time.

### Naming the rope over the wire

A rope has no `NetworkObject` and therefore no id to send. But it has a *shape*, and that shape is
derived from two replicated endpoints — so the **world point where it was clicked** names the same
rope on every machine, exactly as a knot in bare geometry is addressed by its point. The click
travels as `arg.P` with the `Untie` verb, and each machine runs `Leash.Nearest` on its own copy.

Two ropes tied between the same pair of objects lie on top of each other and are genuinely ambiguous
here. They are also indistinguishable on screen, so whichever is picked looks identical; the only
cost is that two machines could drop different ones.

---

## 10. It is not a grappling hook

A leash restrains. It must never be a way to get around, and two specific things enforce that:

- **`LeashEnd.Restrain` caps a player's speed at what it already was.** The arrest term normally
  only cancels motion that is opening the gap — but the gap also opens when the *other* end leaves,
  and the same term then reads as a tow and would happily accelerate the player along the rope.
  Fired at something fast, or at the right moment on a swing, that is a launch. So a leash may take
  a player's speed away and it may drag them, but it can never hand them any.
- **Nothing in the leash calls `PlayerMovement.SetTethered`.** That flag is the grappling hook's
  swing steering: it lets a player pump an arc, preserves the speed built across it, and suppresses
  fall damage for the whole swing. An early version of this rework set it, which made the leash a
  second grappling hook with a longer reach. `LeashConstraintTests` pins its absence at the source,
  because there is no runtime state to assert against — only the temptation to add it back.

Consequences worth knowing: a player leashed to something **static** can only ever be slowed and
held at the rope's length. A player leashed to a **moving** vehicle is dragged along, because the
position correction moves them — but they carry no speed the rope gave them, so cutting it never
flings anyone. And if the vehicle outruns the correction, the rope reaches `breakStretch` and snaps
rather than teleporting them.

A leashed player takes normal fall damage and keeps normal air control.

---

## 11. Persistence

`LeashSaveable` is a **global** saver: a rope belongs to neither end, so filing the record
under either would lose it whenever that one unloaded, and filing it under both would restore
two ropes. It keeps its own retry loop because global savers get no `IDeferredSaveable` pass
and both endpoints may still be streaming in.

Format — unchanged by the rework, so worlds saved before it still load:

```
{ a: {anchor, offset, point, held}, b: {…}, maxLength }
```

An endpoint with no `SaveRef` degrades to its world point rather than losing the rope: it
comes back tied to that *place* instead of that *thing*, which is wrong in exactly the way an
unsaved prop is wrong — it was not going to be there either.

---

## 12. Regenerating the rope textures

`rope_braid_albedo.png` and `rope_braid_normal.png` are 256×64, generated. X runs along the
rope and tiles; Y is the cross-section.

The generator is `Art/Textures/Items/_Source~/rope_braid.py` — in a `~` folder so Unity does
not import it, beside the PNGs it produces. Run it from that directory; it needs `numpy` and
`Pillow`. The parameters that matter are three strands at a lay of 1.6 wraps across the width
per repeat, and a normal map whose strand curve is kept well under its rope curve: at full
strength the two saturate, the renormalise clamps, and the result is a *flatter* rope than
either shape alone.

Import settings: albedo sRGB, normal `textureType: 1` with sRGB off, both `wrapU: repeat`
(along the rope) and `wrapV: clamp` (across it).

---

## 13. Known limits

- **The rope does not wrap around corners.** It is one distance constraint, not a segmented
  rope. Deliberate — see the rework spec.
- **AI is not told it has been leashed.** A creature is pulled; its brain keeps pathing where
  it wanted. A `NavMeshAgent` will visibly lean against the rope, which is the intent.
- **A rope to an unnetworked dynamic prop stays local.** Such a prop is addressable by neither
  id nor point, so remote machines pin their copy of the rope to the hit *point*. Its physics
  already differs per machine, so a shared rope to it could not have been made to agree anyway.
- **No HUD indicator.** Hook-then-hook is meant to need none.
