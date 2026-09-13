# Net Gun: Wrap and Hogtie

**Date:** 2026-09-05
**Status:** approved

## Problem

A net that lands on a body currently holds it with cloth physics: `SnareDrape` clamps the lattice
around a capsule proxy, a heavy hem (`hemWeight`) drives the skirt down past the body, and friction
(`bodyGrip`) keeps it there while `SnaredBody` / `SnareTether` apply a shuffle-radius constraint and
a speed cap. The captive stays on its feet and walks around inside the net.

Two things are wrong with that. It is expensive — a 90 Hz Verlet solver runs for the net's whole
30 s life for every live net, of which there may be three per gun. And it does not read as a
capture: a creature standing upright under a draped net looks like a creature wearing a net.

## What changes

A net that touches a body **cinches around it and puts it on the ground.** A net that touches
nothing behaves as it does today until it lands, then stops solving.

The captive is limp for as long as the net holds. It escapes by struggling, and struggling is
rate-limited so that spamming is worth nothing.

A second player may then **tie a downed captive with a leash**, which holds them past the net's own
life for up to two minutes.

## Non-goals

- No change to the flight. The closed-form arc in `NetGunFlight` and the determinism argument behind
  it (`SnareCatch.CarryAlongFlight`) are untouched.
- No new input binding. `Jump` and `Move` are already bound and both are dead while limp.
- No persistence. See *Persistence* below — this is a decision, not an omission.

---

## 1. The net's lifecycle

Five phases. Only *Flight* is unchanged.

| Phase | Solver | Ends when |
|---|---|---|
| **Flight** | alive; whole lattice carried along the closed-form arc | sphere cast reports contact |
| **Cinch** | alive, plus a shrinking radial target around the victim | `CinchSeconds` elapsed (~0.7 s) |
| **Bound** | **off**; frozen nodes skinned to ragdoll bones | integrity spent |
| **Landed-miss** | **off**; shape frozen as it lay at touchdown | idle rot spends integrity |
| **Tear** | off | `RotSeconds` dissolve completes |

`SnareCatch` gains an explicit phase enum. It currently infers its state from `landed`,
`landedElapsed` and `rotElapsed >= 0f`, and a fourth and fifth state cannot be added to that
without the flags becoming ambiguous.

### 1.1 The cinch — why it is not a snap

**Nothing ever moves a node onto a cylinder.** Each node gets a soft pull toward a target radius
about the victim's up-axis, and that pull is relaxed **inside the existing Gauss-Seidel loop**
alongside the strand, shear and bend families — a constraint competing with the others, never a
post-pass.

This is the whole design. `SnareLattice`'s history records that a Laplacian smoothing pass run
*after* the constraint loop left every substep off-constraint for the next one to yank back, which
at 90 Hz is a permanent vibration. A cinch applied as a post-pass would reintroduce exactly that.

It is also the answer to the `Cling` that was rejected on sight: *projecting cord onto a capsule
draws a capsule.* Because strands are inextensible and the target circumference shrinks, the cloth
has nowhere to put its length except into folds. The output is buckling, slack spanning the gaps
between limbs, and a hem that hangs — the solver's own behaviour, not an authored shape.

The target radius eases from the lattice's current mean radius down to `CinchRadius` over
`CinchSeconds`. The axis is the victim's up-axis sampled **once, at contact**, not tracked: the body
is about to topple, and a target frame that tumbles with it would drag the net through the ground.

`SnareLattice.PerPass` applies to the cinch stiffness like every other family, so the authored
number is the delivered number regardless of iteration count.

### 1.2 Binding

At the end of the cinch the solver stops for good. Each node takes the **nearest ragdoll bone** and
stores its offset in that bone's local space — single-bone skinning across ~15 bones, no weights to
blend. Redraw then reads bone transforms instead of solver output.

The net tumbles with the body and each limb drags its own cord. Ongoing cost is one matrix multiply
per node per frame against a solver that no longer runs.

`RagdollRig` needs one new read-only accessor for its bone list; the list is currently private.

### 1.3 The miss

A net that lands on the ground freezes its node positions and stops solving. It keeps whatever
shape the drape gave it at touchdown. `SnareDrape` and `SnareLattice.GripGround` still run during
flight and up to the freeze, and are dead afterwards.

---

## 2. Capture becomes limpness

`SnaredBody` and `SnareTether` stop being constraints and become holds.

| | Before | After |
|---|---|---|
| `SnaredBody` | shuffle-radius constraint on the player's own machine | put the local player limp; release |
| `SnareTether` | NavMesh speed cap, thrash, drag | put the creature limp via `AgentRagdoll`; release |

**No new message carries the down-state.** `NetMsg.Snared` already goes `NetTo.All`, and
`SnareCatch.Capture` already runs `Bind` on every machine — which is precisely the "both ends are
replicated, so each machine computes its own half" argument in `SnaredBody`'s existing file header.
The duration never travels because it is not known in advance.

`PlayerRagdoll` and `AgentRagdoll` grow a `HoldDown` / `ReleaseHold` pair beside the existing timed
`OnKnockdown`. An indefinite hold is a genuinely new state: `OnKnockdown` recovers on `downUntil`,
and this one recovers on an external event.

`SnareStruggle` loses `shuffleRadius`, `hobbleSpeed`, `thrashFrequency`, `thrashShare` and
`dragInfluence`. All five describe a captive on its feet and are meaningless once it is on the
floor. `SnareCatch.DragTowardCaptives` goes with them.

---

## 3. Struggling

One rate-limited accumulator, two tunings.

- A qualifying input is a **`Jump` press** or a **`Move` direction reversal**. Both count; neither
  is required.
- Each accepted input opens a cooldown of `1 / MaxUsefulRate` (0.4 s at 2.5 Hz). Inputs inside the
  cooldown are discarded outright.
- Read on the victim's own machine only. That is the one machine with their input, and it is
  legitimate here in a way it was not for `LeashArtifact`'s deleted `dropAction`: this component
  *is* the local player, not an item copy that also runs in every other player's hands.
- It reaches the authority as `NetMsg.SnareStruggle = 98` (victim → server, net id in `A`). The
  2.5 Hz cap **is** the send throttle: at most ~2.5 tiny messages per second per netted player.

### 3.1 The drain model does not change shape

`SnareIntegrity.Drain` already takes `max(IdleRotShare, load / ReferenceLoad)`. A struggling captive
simply presents more mass:

```
presentedMass = ReferenceLoad × (1 + struggleMultiplier × struggleLevel)
```

where `struggleLevel` is 0..1 from the rate-limited accumulator. At `struggleMultiplier = 2`, a
perfectly-struggling player presents `3 × ReferenceLoad` and escapes a 30 s net in **10 s**.

**Creatures need no new code at all.** `StrugglingMass` already scales with a creature's real mass
via `LassoTether.EstimateMassOf`, and that mass *is* their struggle — a heavier animal already tears
out faster. Only the player branch (the flat `ReferenceLoad` fallback) gains the multiplier.

---

## 4. The hogtie

The same machinery at a different tuning.

- A fifth verb in `LeashArtifact`'s `NetArg.B` packing, beside `Miss` / `Hit` / `HitLocal` /
  `Untie`. **Refused unless the target is already held down.** You cannot tie someone standing up.
- The tie consumes the leash item and adds a `Hogtie` component to the victim carrying its own
  `SnareIntegrity` at 120 s capacity.
- Struggle drains it at `struggleMultiplier = 1.67`: **120 s untouched, ~45 s struggling perfectly.**
- A third party frees them by clicking the ropes empty-handed — the existing `Untie` verb,
  retargeted at a body rather than a rope anchor.
- When the tie ends by any route, the rope **drops as a `Leash` pickup at the body**. The rope is
  conserved, not deleted.

Net and tie are independent pools. The net may tear out from under a tied body and leave it tied.
That is the intended reading of "so he can't get up".

### 4.1 Assumptions carried, not confirmed

Two numbers here are the author's, flagged in review and not overridden:

1. **The net's 10 s perfect-struggle escape.** The 2 min tie was specified; the net's escape was
   not. Tunable in one field.
2. **The tie consumes the leash item.** This is a reading of "add rope around him". If a tie should
   instead be free and leave the leash in hand, it is a one-line change with a real balance
   difference.

---

## 5. Defects in existing code this must handle

These were found while reading and are not hypothetical.

1. **`RagdollBudget` will stand a captive up.** It calls `Freeze()` on the oldest limp rig when over
   cap, and `PlayerRagdoll.Update` restores control on `!rig.IsLimp`. Enough corpses elsewhere in
   the world therefore free a netted player, silently. A held-down body is a *gameplay state*, not a
   corpse: it must be exempt from eviction.
2. **A mount with a rider refuses knockdown.** `AgentRagdoll.CanBeKnockedDown` is
   `isActiveAndEnabled && !HasRider`. A net on a ridden animal must fall back to the old hobble, or
   netting a mounted nomad is a no-op with a clean console.
3. **`maxLimpSeconds = 4` does not bound this**, because `IsSettled` going true is not the same as
   standing up — `PlayerRagdoll.Update` returns early while `Time.time < downUntil`, and a held body
   has no `downUntil` at all. It does mean the rig sleeps, which is the look we want. Pin it in a
   test so it is not "fixed" later.

---

## 6. Multiplayer

| Concern | Where it lives |
|---|---|
| Deciding what a net caught | server, `SnareReceiver.Decides` (unchanged) |
| Presenting cinch, bind, limpness | every machine, from `NetMsg.Snared` (unchanged) |
| Reading struggle input | the victim's own machine only |
| Applying struggle to the pool | server |
| Tying and untying | server, from the leash's `Present` |

The escape timer lives on the server and the rate cap is enforced where the drain happens, not where
the key is pressed, so a hacked client that spams struggle gains nothing (`GDC-L1-MP-0004`).

Nothing new is spawned at runtime, so there is no network prefab to register. The dropped `Leash`
pickup uses the leash's existing spawn path.

---

## 7. Persistence

**Nothing here is saved, deliberately, and this is a decision rather than an omission.**

A net and a tie are both bounded transient states measured in seconds. Persisting either would mean
a quit-time autosave capturing a limp player, and the world then reloads with a player who cannot
move and nothing in the log to say why — the exact failure recorded for `isKinematic` capture. On
load, nets are gone and everyone is standing.

---

## 8. Design principles

- `GDC-L1-FEEL-0002`, `GDC-L1-ANIM-0002` — control returns at the *start* of the recovery blend, as
  `PlayerRagdoll` already does. Being down is commitment, not latency, but it still has to be
  priced.
- `GDC-L1-UX-0006` — the rate cap is the accessibility win, and it is structural rather than an
  option buried in a menu: nobody plays this better by hurting their hands, and two input channels
  mean a player who cannot use one can use the other. The anti-macro property and the accessibility
  property are the same property — the "curb-cut effect" the principle names.
- `GDC-L1-MP-0002` — a two-minute unbreakable tie on another player is a griefing tool. The escape
  is why it is not one. Behaviour is designed, not moderated afterwards.
- `GDC-L1-FEEL-0004` — the cinch needs sound and a camera cue. A body going limp under a silent net
  is the largest feel gap in this plan and is named here rather than discovered in playtest.

---

## 9. Testing

`SnareCatch.Advance(delta)` remains the seam that drives an assembled net with no play session, and
the new phases are driven through it.

The lesson from the last round governs: every existing `NetGunTests` case measured something
invariant under translation, which is how a completely invisible net passed 28 of them. The new
tests must assert things that can actually be wrong:

- The cinch **converges to a radius** and the lattice **retains its area** (folds, not shrink-wrap).
- The cinch is applied **inside** the constraint loop — residual per-substep motion at rest stays at
  the swept 0.0003 m, not the 0.0142 m of the full-stiffness case.
- Bound nodes **follow their bone** through a synthetic bone transform.
- The struggle accumulator **saturates**: 20 inputs/sec yields the same level as 2.5/sec.
- A held body is **not evicted** by `RagdollBudget` under pressure.
- A ridden mount **falls back to the hobble**.
