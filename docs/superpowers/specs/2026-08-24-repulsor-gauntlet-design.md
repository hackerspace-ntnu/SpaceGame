# Repulsor Gauntlet — design

**Date:** 2026-08-24 · **Revised:** 2026-08-25 (v2 — see §1) · **Status:** built; awaiting
in-editor and multiplayer playtest

A hand-worn artifact: press Use to fire an instant directional force blast that flings
players, loose items, and (bounded, v1-scoped) creatures away from the caster, with
layered VFX on every machine. The first deliberate knockback system in the game — prior
systems (lasso, leash) were explicitly forbidden from giving players speed; this one
exists to do exactly that, on purpose, priced by scarce ammunition.

Design principles applied: GDC-L1-FEEL-0008 (deliberately placed at the *responsive* end
of the responsiveness–commitment axis — see §1), GDC-L1-FEEL-0004 (layered multi-sensory
feedback), GDC-L1-FEEL-0006 (directional, dosed, capped camera kick), GDC-L1-FEEL-0005
(hitstop — considered and rejected, see §5), GDC-L1-SYS-0007 (bound the griefing loop
with limited ammo), GDC-L1-MP-0001 (the point of the item is player-to-player
interaction).

---

## 1. Interaction model

**v2, after playtest.** v1 was hold-to-charge with a cooldown. Playtesting judged it "way
too weak", and the chosen direction was an instant-fire wonder weapon in the mould of the
Thundergun — so the charge came out entirely. This is a deliberate move along
FEEL-0008's axis: v1 bought its risk/reward with *commitment* (a wind-up you could be
punished during), v2 buys it with *scarcity* (two shots, then a wait). Both are valid
positions on that axis; the choice is now recorded rather than assumed.

- **Press Use** → blast fires immediately along the owner's aim. No wind-up, no release
  tick. Acknowledged on frame one everywhere (`Present()` runs locally before the round
  trip).
- **Ammunition** is the price: `shotCapacity` (2) shots held, one regained every
  `rechargeSeconds` (7 s), with a `refireDelay` (0.45 s) so a double-tap can't dump both
  in a frame. This is the primary anti-spam bound (SYS-0007), replacing v1's cooldown.
- **Legibility** (SYS-0006): the gauntlet's capacitor glow is the ammo readout — lit when
  a shot is ready, dark while recharging. The magazine is persisted per hotbar slot, so
  scrolling away and back does not refill it.
- **Safety**: a `NetArg` carrying no orientation (the default one `EquipmentController`
  sends on unequip/death) never fires — inherited from v1's cancel semantics.
- **Recoil**: the caster is pushed backward hard enough that an airborne shot is a
  movement tech (repulsor-jump) — a feature, priced by the ammo.

All tunables are serialized fields — no magic numbers.

## 2. Class and item plumbing (standard artifact pipeline)

`RepulsorGauntletArtifact : ToolItem`, namespace `SpaceGame.Items`, at
`Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs`.

- `Authority => UseAuthority.Server` — the blast changes shared world state.
- `IsContinuous => true`, **no** `WantsHold` override — the button-up release IS the
  fire (Lasso pattern, `LassoArtifact.cs`).
- Prefab `Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab` via an
  editor builder script; `ItemGrip` with `holdStyle = OneHanded`, grip point in the
  palm cavity of the mesh (there is no "worn" mode; the grip-point placement is how a
  gauntlet straps on). `UsesHoldPose` stays true so the arm is posed.
- Item asset `Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset`,
  back-reference on `PickupableItem.item`, icon via `Tools/Generate All Item Icons`,
  network prefab via `Tools/SpaceGame/Multiplayer/Sync Network Prefabs`.
- Mesh via the blender-model skill; greybox first (GDC-L1-PROTO-0003) — the mechanic is
  provable with a placeholder cylinder.

## 3. What travels

v2 has no hold stream at all, so the entire release-tick encoding of v1 is gone — one
press message carries everything:

- `NetArg.B` carries a verb: `MissVerb = 0` (refused — out of ammo or inside the refire
  delay), `FireVerb = 1`.
- `arg.P` = aim ray origin and `arg.R` = aim rotation, captured in `OnRequestUse` on the
  **owner** — the only machine where the aim is honest, since a peer's copy of a player
  has an `AimProvider` with no live camera behind it.
- **A NetArg with no orientation never fires.** Inherited from v1's cancel semantics:
  `EquipmentController` delivers a default NetArg on unequip/death, and a zero quaternion
  would otherwise blast along an arbitrary axis. Both `Use()` and `Present()` guard on
  `HasOrientation`.
- **Ammo state never travels either.** The owner and the authority each run their own
  magazine, exactly as v1's cooldown was tracked per machine — the owner's copy gates the
  press, the authority's gates the effect. They must be separate objects: on a host
  `Present()` runs *before* `Use()`, so one shared counter would be spent by the
  presentation and then refused by its own authority. A remote peer's copy is cosmetic
  (it drives that peer's view of the capacitor glow) and self-corrects.

## 4. The blast: three target classes, three mechanisms

Fired by the **server** (or the only machine offline) in `ApplyRelease`. Target
gathering: `Physics.OverlapSphere` at the hand, radius `Lerp(minRange, maxRange,
charge)` (defaults 6→13 m), filtered to a cone `blastAngle` (75°) around the aim
direction, with a `HashSet<GameObject>` keyed on root so multi-collider bodies are
billed once (LightningSpell precedent, `LightningSpell.cs:60-90`). Fling magnitude
falls off with distance and off-axis angle; direction is (target − hand) tilted upward
by `upwardTilt` (25–30°). The caster is excluded (recoil is separate).

**Class 1 — loose Rigidbody items** (dropped artifacts, scraps, debug cubes):
`if (body.isKinematic && Network.Simulates(body)) body.isKinematic = false;` (the
LassoTether guard — a kinematic replica is kinematic on purpose), then
`AddForce(dir * speed, ForceMode.VelocityChange)` attenuated by LassoTether's
bounds-density mass estimate so a crate flies less than a can. Server-simulated;
replication is free via NetworkTransform. This also papers over the inconsistent
authored `isKinematic` flags across item prefabs.

**Class 2 — players.** The core of the item, and the one genuinely new network
mechanism. The player body is owner-authoritative and `PlayerMovement.FixedUpdate`
*assigns* horizontal velocity every tick, so a server-side push is deleted silently.
The retired `NetMsg.RopeTug` (id 62, burnt — do not reuse) documents the correct
shape: **the server decides, the victim's own machine applies.**

- New message `NetMsg.Flung` (appended after the last id), broadcast `NetTo.All` on
  the **victim's** channel with `P = velocity delta (m/s)` — the layer has no unicast;
  every machine receives and only the owner acts.
- New component `FlungBody` on the player root — `static Ensure(GameObject)` pattern
  (LeashedBody / SceneTransitionViewer), `[DefaultExecutionOrder(200)]` (the
  load-bearing ordering both rope systems use so `PlayerMovement` runs first). Handler
  gates on `Network.Owns(this)`, latches the pending velocity, and drains it in
  `FixedUpdate`:
  1. `movement.EnsureMovableBody()`
  2. `rb.linearVelocity += V`
  3. `movement.CarryMomentum()` — mandatory or air control (0.3 lerp toward walk speed,
     50×/s) erases the fling in ~0.2 s.
  Never `SetTethered` (suppresses fall damage — fall damage after a fling is intended
  counterweight) and never `DisableGroundSnap` (kills all steering/animation).
- Handler is idempotent-by-latch (host re-entrancy: inline local delivery + the
  broadcast's host leg would otherwise double-apply).
- Tuning floor: `CarryMomentum` self-cancels at or below `CurrentMoveSpeed` (up to
  9 m/s sprinting), so `minFlingSpeed ≥ 12 m/s`; `maxFlingSpeed ≈ 22 m/s` at full
  charge. **Corrected during implementation:** the upward tilt does *not* un-ground the
  victim in one tick — `IsGrounded` sphere-casts generously and keeps reporting ground
  for ~0.6 m of clearance, while a 10 m/s rise lifts only ~0.2 m per FixedUpdate. So
  the original plan (rely on going airborne) would have let `SteerWithoutBraking` clear
  the latch and delete the horizontal half before the victim ever left the floor. What
  actually protects it is `PlayerMovement.ShouldEndCarry`, which refuses to treat a
  *rising* body as landed; the upward tilt's real job is to make that rise unambiguous.
  Vertical velocity itself is never touched by `PlayerMovement`.
- Recoil is the degenerate same-machine case: the **owner** applies it locally in
  `PresentHold` (no message needed — portal/grapple precedent), gated on owning the
  movement.

**Class 3 — agents and mounts (v1-bounded).** Every creature/walker body is kinematic
and its motor rewrites transform or velocity every tick — no force can ever land, and
no knockback/stagger seam exists anywhere. v1 scope, honest about the bound:

- Motors implementing `IMountLeapMotor` (`RigidbodyMotor`, `NavMeshAgentMotor`):
  `RequestLeap(awayDir, distance ∝ charge / mass estimate, height, duration)` — a
  ready-made scripted displacement arc that already survives save/load via
  `MotorStateSaveable`. This covers most mounts and NavMesh creatures.
- Everything else (LeggedDriver walkers, ornithopter): **hit reaction only** — wire
  `AgentAnimatorDriver.TriggerHurt()` (an authored animator trigger currently called
  by nothing) plus the shared VFX. A full "shoved" displacement state machine
  (suspend self-drive → displace → resume, per LassoTether) is explicitly deferred;
  logged as follow-up, not silently skipped.
- Optional `blastDamage` (default small or 0) through `NetDamage.Apply` with the same
  once-per-root billing.

## 5. Feel and VFX (all in `Present()`, every machine)

Layered per FEEL-0004 — each channel on a different sense. v1 shipped only a flat
additive ground ring, a shake and an FOV kick, which playtest called "very boring"; v2
adds the volume of air itself, particle mass, light and a thunderclap.

**The air wall (the signature effect):** `RepulsorBlastCone` builds a procedural cone
shell along the aim at the *same half-angle the authority swept* — the cone you see is
the cone that threw you — rendered with a new `RepulsorAirWarp.shader`. That shader is
the first in this project to sample `_CameraOpaqueTexture` (already enabled in
`PC_RPAsset.asset`), refracting the world behind the blast front so the air visibly
bends, with the distortion banded at the leading edge and a faint fresnel rim so it still
reads against an empty sky. The half-res opaque texture makes the refraction soft, which
happens to read as hazy compressed air.

**Ground ring:** the existing `RepulsorShockwave.shader` annulus, for where the blast
meets the ground (a shot aimed up or down still gets the cone).

**Particle mass:** three one-shot bursts authored in the builder from the GravelBlaster
recipes — billowing billboard **dust** (45° cone), high-speed stretched **streaks** (40°,
reading as thrown air), and **debris** that bounces off the sand via the collision module.

**Light:** a point `MuzzleFlash` armed off and enabled for `flashSeconds` on the shot —
which matters most at night, where the blast now lights the terrain.

**Audio:** two layered ids on distinct source keys so the catalog's per-(id, sourceKey)
cooldown can't swallow either — `SfxId.AmbThunder` (→ `event:/SFX/Thunder`, mapped from
exactly one id, so a genuine thunderclap) over `SfxId.ImpactExplosion`
(→ `event:/SFX/Explosion`).

**Camera:** a +14° FOV kick, and shake through `CameraShakerHandler` with
`RepulsorBlastShake.asset` **actually authored** this time (v1 shipped it as a byte-copy
of `DamageShake` — a soft 0.15 s tick, so a blast felt exactly like taking a hit): 0.55 s,
magnitude 3.5, roughness 13, with real Z positional and stronger rotational influence.
Distance-attenuated per-viewer via `ShakerInstance.MultiplyMagnitude` — dosed and capped
per FEEL-0006, inheriting the shaker's user setting.

**Hitstop (FEEL-0005) — considered and rejected.** A time-freeze would sell the impact,
but `Time.timeScale` is global and on a host would stall the authoritative simulation for
every other player. This codebase has already made and documented that decision
(`SuckerPuncherArtifact`, `GameplayMenuScope` freezes only in a solo session), so the
FOV kick and shake deliberately do the work hitstop would — this is FEEL-0005's own
"does not apply" clause, not an oversight.

**Victims:** the flung player's machine plays its own shake + short FOV kick when
`FlungBody` drains (the hit you feel, not just see).

**Not in v1:** screen-space shockwave distortion — no such shader exists and
`GlassDistortionRenderFeature` is not per-event drivable. Stretch goal.

No new FMOD events are possible (the .fspro is lost); all audio maps onto the 18
shipped events.

## 6. Persistence

**v2 has state worth persisting: the magazine.** It is captured into the hotbar slot's
item state (`CaptureItemState`/`RestoreItemState`), because without it the player refills
by scrolling off the hotbar and back — the held instance is destroyed and rebuilt on every
equip, so anything held only in fields is silently free ammo. Everything else is
unchanged: item-in-inventory persistence rides the existing pipeline, and mid-arc leap
displacement on shoved mounts already saves via `MotorStateSaveable`.

## 7. Multiplayer verification (definition of done)

Host-only testing proves nothing here. On an actual client:

1. Client fires → host player is flung on the host's screen (and sees cone/ring/particles/
   flash/shake/audio).
2. Host fires → client is flung *on the client's machine* (the FlungBody path).
3. Both machines see identical VFX and audio for a third party's blast.
4. Dropped items scatter identically on both machines.
5. Death or a hotbar scroll never produces a phantom blast (the `HasOrientation` guard),
   and the magazine does not refill by scrolling away and back (the persistence above).
6. Ammo: two shots, then the capacitor goes dark and a shot returns after
   `rechargeSeconds`; a client's readout matches what it can actually fire.
7. Drop the gauntlet, client picks it up — network prefab registration proof.
8. `Tools/Tests/Run EditMode Tests (headless)` — `NetworkPrefabRegistrationTests` and
   `HoldPoseTests` green, plus the feature's own suites (cone/falloff math including the
   edge-hit launch floor, `FlungBody`, `CarryMomentum`).

## 8. Balance bounds (SYS-0007)

v2 is deliberately a wonder weapon, so the bounds moved from telegraph to scarcity:
two shots and a 7 s-per-shot recharge, plus fall damage staying live on victims, plus
`flingSpeed` capping stacked flings implicitly via `+=` against drag. What v2 *loses* is
v1's wind-up counterplay (BAL-0004) — an instant blast cannot be reacted to, only
positioned against, which is the accepted cost of the chosen fantasy. If chain-flinging
turns out to grief in practice, the lever is ammo and recharge, not a reinstated charge.
Numbers are serialized and expected to move after playtests — tune with play, not math
alone (BAL-0005).

## 9. Build order

1. `NetMsg.Flung` id + `FlungBody` component (smallest testable core — can be proven
   with a debug key before the item exists).
2. `RepulsorGauntletArtifact` charge/release skeleton (Lasso template) with greybox
   mesh; blast resolution for items + players.
3. Agent/mount handling (leap + TriggerHurt wiring).
4. VFX/audio/shake layer.
5. Real mesh (blender-model skill), prefab builder, item asset, icon, network prefab
   sync, route into `startingItems`/dev browser.
6. Verification checklist above.
