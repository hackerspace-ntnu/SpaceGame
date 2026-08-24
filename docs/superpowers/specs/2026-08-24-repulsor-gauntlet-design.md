# Repulsor Gauntlet — design

**Date:** 2026-08-24 · **Status:** draft, awaiting review

A hand-worn artifact: hold Use to charge, release to fire a directional force blast that
flings players, loose items, and (bounded, v1-scoped) creatures away from the caster,
with layered VFX on every machine. The first deliberate knockback system in the game —
prior systems (lasso, leash) were explicitly forbidden from giving players speed; this
one exists to do exactly that, on purpose, behind a charge commitment and a cooldown.

Design principles applied: GDC-L1-FEEL-0008 (charge = commitment, input acknowledged
instantly, action resolves on its own schedule), GDC-L1-FEEL-0004 (layered multi-sensory
feedback on charge and blast), GDC-L1-FEEL-0006 (directional, dosed, capped camera
kick), GDC-L1-SYS-0007 (bound the griefing loop with cooldown + charge time),
GDC-L1-MP-0001 (the point of the item is player-to-player interaction).

---

## 1. Interaction model

- **Press Use** → charge begins. Acknowledged on frame one everywhere (`Present()` runs
  locally before the round trip): gauntlet emissive ramps, charge loop starts.
- **Hold** → charge accumulates over `chargeTime` (default 1.2 s), clamped 0–1, with a
  floor `minCharge` (0.25) so a tap still puffs.
- **Release** → blast fires along the owner's aim. Charge scales radius, fling speed,
  and recoil.
- **Cancel** paths (no blast, charge refunded visually): hotbar switch, unequip, death,
  disconnect, hold-stream timeout.
- **Cooldown** `cooldownTime` (2.5 s) after a blast — the commitment half of
  FEEL-0008 and the primary anti-spam bound (SYS-0007).
- **Recoil**: the caster is pushed backward, scaled by charge. Full-charge airborne
  recoil is deliberately strong enough to be a movement tech (repulsor-jump) — a
  feature, priced by the cooldown.

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

## 3. Charge: what travels and what doesn't

Copying the Lasso's proven encoding (`LassoArtifact.cs:210-330`):

- On the **press** message, `NetArg.B` carries a verb: `MissVerb = 0` (CanUse refused —
  cooldown), `ChargeVerb = 1`. `Present()` on every machine starts the local charge
  clock on `ChargeVerb`.
- **The charge level itself never travels.** `A` is the hotbar slot and `B` is the
  active flag on the hold stream, so there is no free scalar — and there doesn't need
  to be. Every machine saw the press, so every machine runs its own charge clock:
  cosmetics use the local value, and the *authority* computes blast strength from its
  own elapsed time between receiving press and release. Each machine is
  self-consistent; cross-machine drift is ≤ one network latency of charge (~4 % at
  50 ms RTT), the same tolerance the Lasso shipped with.
- On the **release tick** (`OnRequestHold` with `active == false`, owner only):
  `arg.P` = aim ray origin, `arg.R` = aim rotation. **`!arg.HasOrientation` on a
  release means cancel, never fire** — `EquipmentController.EndHold(send:false)`
  delivers a default NetArg on unequip/death, and treating it as a fire launches a
  blast on every hotbar scroll (the documented Lasso trap).
- `Hold()` (authority) and `PresentHold()` (every machine) both route to one
  **idempotent** `ApplyRelease` — on a host both run for the same tick
  (LaserStaff `ApplyHold` precedent).
- `holdTimeout = 0.5f` safety: if charging and no hold tick arrives for longer, cancel
  (dropped release packet, disconnect mid-charge) — LaserStaff pattern.

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

## 5. Feel and VFX (all in `Present`/`PresentHold`, every machine)

Layered per FEEL-0004 — each channel on a different sense, intensity scaled to charge:

**Charging:** gauntlet emissive ramp + inward-streaming particles; charge loop
`SfxId.WeaponEnergyChargeLoop` (→ `event:/SFX/ElectricHum`, a real loop) via
`LoopingEmitter`; owner-only slow FOV **pull-in** via `PlayerLook.SetFovOffset`
(−4° at full charge — anticipation; always reset to 0 on cancel/unequip, the grapple's
documented reset trap).

**Blast:** expanding ground ring with a **new dedicated shader**
(`RepulsorShockwave.shader` — additive, hot leading edge at the outer rim, fades as the
wave completes; the RuinScanner pulse shader is explicitly NOT reused, per review) + a
dust/grit burst;
`SfxId.ImpactExplosion` (→ `event:/SFX/Explosion`, the one distinct blast in the bank —
deliberately *not* an ElectricHum-mapped id, or charge and release sound identical);
FOV snaps from −4° to a brief +6° kick and eases out (asymmetric in/out speeds are
built into PlayerLook). Camera shake through the existing
`CameraShakerHandler.Shake(ShakeData)` with a new authored `RepulsorBlastShake.asset`
(only `DamageShake` exists today): full strength for caster and victims, distance-
attenuated for bystanders, capped — dosed and directional per FEEL-0006, and it
inherits the shaker's user setting.

**Victims:** the flung player's machine plays its own shake + short FOV kick when
`FlungBody` drains (the hit you feel, not just see).

**Not in v1:** screen-space shockwave distortion — no such shader exists and
`GlassDistortionRenderFeature` is not per-event drivable. Stretch goal.

No new FMOD events are possible (the .fspro is lost); all audio maps onto the 18
shipped events.

## 6. Persistence

The gauntlet holds **no runtime state worth persisting** — charge and cooldown are
transient by design (a reload mid-charge simply cancels, matching every other cancel
path). Item-in-inventory persistence is already handled by the existing item pipeline;
mid-arc leap displacement on shoved mounts already saves via `MotorStateSaveable`.
Stated explicitly per the repo rule: nothing new to save.

## 7. Multiplayer verification (definition of done)

Host-only testing proves nothing here. On an actual client:

1. Client charges and fires → host player is flung on the host's screen (and sees the
   ring/shake/audio).
2. Host fires → client is flung *on the client's machine* (the FlungBody path).
3. Both machines see identical ring/particles/audio for a third party's blast.
4. Dropped items scatter identically on both machines.
5. Hotbar-scroll and death mid-charge cancel cleanly on all machines (no phantom blast
   — the `HasOrientation` guard).
6. Drop the gauntlet, client picks it up — network prefab registration proof.
7. `Tools/Tests/Run EditMode Tests (headless)` — `NetworkPrefabRegistrationTests` and
   `HoldPoseTests` green, plus new edit-mode tests: release-tick cancel discriminator,
   cone/falloff math, fling-speed floor.

## 8. Balance bounds (SYS-0007)

Griefing loop (chain-flinging a teammate) is bounded by: charge time (a full-power
fling costs 1.2 s of visible, audible telegraph — counterplay per BAL-0004), cooldown,
fall damage staying live, and `maxFlingSpeed` capping stacked flings implicitly via
`+=` against drag. Numbers are serialized and expected to move after playtests —
tune with play, not math alone (BAL-0005).

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
