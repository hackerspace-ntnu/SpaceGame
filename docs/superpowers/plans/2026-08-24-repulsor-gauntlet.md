# Repulsor Gauntlet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A hand-worn charge-and-release artifact that fires a cone force blast, flinging players (via a new owner-applied `Flung` network message), loose items (server-side impulse), and leap-capable mounts, with layered VFX on every machine.

**Architecture:** `RepulsorGauntletArtifact : ToolItem` uses the Lasso's charge-on-hold pattern (charge never travels; release fires on the final hold tick; default NetArg = cancel). Physics is split by target class: players get a new `NetMsg.Flung` broadcast applied by a new `FlungBody` component on the victim's own machine (execution order 200, `CarryMomentum` latch); items get a server-side `AddForce`; agents get `RequestLeap` or a cosmetic hurt trigger. Pure blast math lives in a static `RepulsorBlast` class so it is unit-testable.

**Tech Stack:** Unity (Assembly-CSharp — NO asmdef under Scripts/Items), NGO + the repo's NetMessaging layer, FMOD via `Sfx`/`LoopingEmitter`, FirstGearGames camera shaker, NUnit EditMode tests in `Assets/Game/Editor/Tests/`.

**Spec:** `docs/superpowers/specs/2026-08-24-repulsor-gauntlet-design.md`

**Ground rules for the executor:**
- Compile-check after every code task: ask the Unity editor via MCP (`Unity_GetConsoleLogs` after a script refresh, or `Unity_ValidateScript`). If MCP is attached, first verify it is NOT a read-only Multiplayer Play Mode clone (check `Application.dataPath` points at this repo, not a clone path) — a clone silently imports nothing.
- A repo hook may block `git commit` — if a commit step is refused, ask the user to run/allow it rather than skipping or working around it.
- Where a step says "verify against `<file>:<lines>`", those line numbers were confirmed 2026-08-24; re-grep if drifted.

---

### Task 1: `NetMsg.Flung` message id

**Files:**
- Modify: `Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs` (the `NetMsg` id block; current last id is `PackStow = 78` at line ~387)

- [ ] **Step 1: Read the tail of the NetMsg id block**

Run: `grep -n "public const ushort" Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs | tail -5`
Expected: last id is `PackStow = 78`. If a higher id now exists, use `highest + 1` below instead of 79. Never reuse 3, 30, or 62 (retired, burnt — ids travel between builds).

- [ ] **Step 2: Append the new id after PackStow, matching the file's comment style**

```csharp
        /// <summary>
        /// Server → everyone, on the VICTIM player's relay: this player has been flung and their
        /// owning machine must apply the velocity in <see cref="NetArg.P"/> (m/s, world space).
        /// Broadcast because this layer has no unicast — every machine receives it and only the
        /// machine that owns the victim acts (see FlungBody). The server cannot apply it itself:
        /// the player body is owner-authoritative, so a server-side push is overwritten within a
        /// tick. Successor to the retired RopeTug (62) shape.
        /// </summary>
        public const ushort Flung = 79; // server → everyone, on the VICTIM's relay
```

- [ ] **Step 3: Compile check**

Refresh scripts and read the Unity console (MCP `Unity_GetConsoleLogs`). Expected: no errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Scripts/Core/Multiplayer/NetMessaging.cs
git commit -m "feat: add NetMsg.Flung for owner-applied player knockback"
```

---

### Task 2: `RepulsorBlast` pure math (TDD)

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlast.cs`
- Test: `Assets/Game/Editor/Tests/RepulsorBlastMathTests.cs` (Editor/Tests, NOT the asmdef'd test folder — these types live in Assembly-CSharp)

- [ ] **Step 1: Write the failing tests**

```csharp
// Assets/Game/Editor/Tests/RepulsorBlastMathTests.cs
using NUnit.Framework;
using SpaceGame.Items;
using UnityEngine;

namespace SpaceGame.EditorTests
{
    public class RepulsorBlastMathTests
    {
        [Test]
        public void Charge_ClampsToOne_AndHasFloor()
        {
            Assert.AreEqual(1f, RepulsorBlast.ChargeFrom(5f, 1.2f, 0.25f), 1e-4f);
            Assert.AreEqual(0.25f, RepulsorBlast.ChargeFrom(0f, 1.2f, 0.25f), 1e-4f);
            Assert.AreEqual(0.5f, RepulsorBlast.ChargeFrom(0.6f, 1.2f, 0.25f), 1e-4f);
        }

        [Test]
        public void InCone_AcceptsFront_RejectsBehind_RejectsFar_AcceptsPointBlank()
        {
            Vector3 origin = Vector3.zero, aim = Vector3.forward;
            Assert.IsTrue(RepulsorBlast.InCone(origin, aim, new Vector3(0, 0, 5f), 10f, 40f));
            Assert.IsFalse(RepulsorBlast.InCone(origin, aim, new Vector3(0, 0, -5f), 10f, 40f));
            Assert.IsFalse(RepulsorBlast.InCone(origin, aim, new Vector3(0, 0, 11f), 10f, 40f));
            Assert.IsTrue(RepulsorBlast.InCone(origin, aim, origin, 10f, 40f));
            // 45° off-axis at halfAngle 40° is outside the cone
            Assert.IsFalse(RepulsorBlast.InCone(origin, aim, new Vector3(5f, 0, 5f), 10f, 40f));
        }

        [Test]
        public void FlingVelocity_AlwaysHasUpwardComponent()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 3f), 0.5f, 10f, 12f, 22f, 27f);
            Assert.Greater(v.y, 0f);
        }

        [Test]
        public void FlingVelocity_PointBlankFullCharge_HitsMaxSpeed()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 0.01f), 1f, 10f, 12f, 22f, 27f);
            Assert.AreEqual(22f, v.magnitude, 0.05f);
        }

        [Test]
        public void FlingVelocity_EdgeHit_IsWeakerThanCloseHit()
        {
            Vector3 close = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 1f), 1f, 10f, 12f, 22f, 27f);
            Vector3 edge = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 0, 10f), 1f, 10f, 12f, 22f, 27f);
            Assert.Greater(close.magnitude, edge.magnitude);
        }

        [Test]
        public void FlingVelocity_PushesHorizontallyAwayFromOrigin()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(3f, 0, 4f), 1f, 10f, 12f, 22f, 27f);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, new Vector3(0.6f, 0, 0.8f)), 0.99f);
        }

        [Test]
        public void FlingVelocity_TargetDirectlyOverOrigin_FallsBackToAimDirection()
        {
            Vector3 v = RepulsorBlast.FlingVelocity(Vector3.zero, Vector3.forward,
                new Vector3(0, 2f, 0), 1f, 10f, 12f, 22f, 27f);
            Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
            Assert.Greater(Vector3.Dot(flat.normalized, Vector3.forward), 0.99f);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run `Tools/Tests/Run EditMode Tests (headless)` (or the MCP test runner). Expected: compile error — `RepulsorBlast` does not exist. (In Unity, a missing type is a compile failure, not a red test; that counts as the failing state.)

- [ ] **Step 3: Implement the math**

```csharp
// Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlast.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Pure math for the repulsor gauntlet's blast — kept free of scene and network state so it is
    /// unit-testable and so the authority and the cosmetic sweep provably agree on the same cone.
    /// </summary>
    public static class RepulsorBlast
    {
        /// <summary>Charge in [minCharge, 1] from seconds held. A tap still puffs.</summary>
        public static float ChargeFrom(float heldSeconds, float chargeTime, float minCharge)
            => Mathf.Max(minCharge, Mathf.Clamp01(heldSeconds / Mathf.Max(chargeTime, 0.01f)));

        /// <summary>Is the point inside the blast cone? Point-blank always counts.</summary>
        public static bool InCone(Vector3 origin, Vector3 aimDir, Vector3 point,
                                  float radius, float halfAngleDeg)
        {
            Vector3 to = point - origin;
            if (to.sqrMagnitude > radius * radius) return false;
            if (to.sqrMagnitude < 1e-4f) return true;
            return Vector3.Angle(aimDir, to) <= halfAngleDeg;
        }

        /// <summary>
        /// Velocity to hand a flung body: horizontally away from the blast origin, tilted upward.
        /// The tilt is load-bearing, not flavour — PlayerMovement never touches vertical velocity,
        /// so the up-component both survives unconditionally and un-grounds the victim, which is
        /// what lets CarryMomentum protect the horizontal half (see FlungBody).
        /// Speed falls off toward the cone edge; edge hits may dip under the CarryMomentum floor
        /// (~sprint speed) by design — an edge hit is a puff, not a launch.
        /// </summary>
        public static Vector3 FlingVelocity(Vector3 origin, Vector3 aimDir, Vector3 targetPos,
                                            float charge, float radius,
                                            float minSpeed, float maxSpeed, float upwardTiltDeg,
                                            float edgeFalloff = 0.35f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(targetPos - origin, Vector3.up);
            Vector3 dir = flat.sqrMagnitude > 1e-4f
                ? flat.normalized
                : Vector3.ProjectOnPlane(aimDir, Vector3.up).normalized;

            float t = Mathf.Clamp01((targetPos - origin).magnitude / Mathf.Max(radius, 0.01f));
            float speed = Mathf.Lerp(minSpeed, maxSpeed, charge) * Mathf.Lerp(1f, edgeFalloff, t);

            float rad = upwardTiltDeg * Mathf.Deg2Rad;
            return (dir * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad)) * speed;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run `Tools/Tests/Run EditMode Tests (headless)`. Expected: all `RepulsorBlastMathTests` PASS, no other suite broken.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlast.cs Assets/Game/Editor/Tests/RepulsorBlastMathTests.cs
git commit -m "feat: repulsor blast cone/charge/fling math with tests"
```

---

### Task 3: `FlungBody` — the owner-applied fling receiver

**Files:**
- Create: `Assets/Game/Scripts/Characters/Player/Movement/FlungBody.cs`
- Test: append to `Assets/Game/Editor/Tests/RepulsorBlastMathTests.cs` — no; create `Assets/Game/Editor/Tests/FlungBodyTests.cs`

Before writing: skim the two exemplars this copies — handler registration `NetworkedHealthComponent.cs:44-56` (`this.NetOn/NetOff` in OnEnable/OnDisable, handler `(in NetArg, ulong)`), and the execution-order + ownership pattern `LeashedBody.cs` (order 200 is load-bearing; the header comments there explain why).

- [ ] **Step 1: Write the failing structural test**

`AddComponent` raises no Awake/OnEnable outside play mode, so an EditMode test can safely check structure:

```csharp
// Assets/Game/Editor/Tests/FlungBodyTests.cs
using NUnit.Framework;
using SpaceGame.Characters;
using UnityEngine;

namespace SpaceGame.EditorTests
{
    public class FlungBodyTests
    {
        [Test]
        public void FlungBody_RunsAfterPlayerMovement()
        {
            var attrs = typeof(FlungBody)
                .GetCustomAttributes(typeof(DefaultExecutionOrder), false);
            Assert.AreEqual(1, attrs.Length,
                "FlungBody must declare DefaultExecutionOrder — PlayerMovement deletes velocity written before it runs.");
            Assert.GreaterOrEqual(((DefaultExecutionOrder)attrs[0]).order, 200);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails** (compile error: `FlungBody` missing)

- [ ] **Step 3: Implement**

```csharp
// Assets/Game/Scripts/Characters/Player/Movement/FlungBody.cs
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Applies NetMsg.Flung to this player — on the owning machine only. The player body is
    /// owner-authoritative, so the server cannot push it; it broadcasts the velocity on this
    /// player's relay and this component, on the one machine that owns the body, applies it.
    ///
    /// Execution order 200 (the value LeashedBody uses, for the same reason): PlayerMovement's
    /// FixedUpdate ASSIGNS horizontal velocity, so anything written before it runs is deleted the
    /// same tick. Latch here, drain after.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class FlungBody : MonoBehaviour
    {
        [Tooltip("Shake played on the victim's own machine when the fling lands. Optional.")]
        [SerializeField] private ShakeData flungShake;

        [Tooltip("Brief FOV kick (degrees) on the victim when flung. 0 disables.")]
        [SerializeField] private float fovKick = 5f;

        [Tooltip("Seconds the FOV kick holds before easing back.")]
        [SerializeField] private float fovKickDuration = 0.25f;

        private Rigidbody body;
        private PlayerMovement movement;
        private PlayerLook look;
        private Vector3 pending;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            movement = GetComponent<PlayerMovement>();
            look = GetComponent<PlayerLook>();
        }

        private void OnEnable() => this.NetOn(NetMsg.Flung, OnFlung);
        private void OnDisable() => this.NetOff(NetMsg.Flung, OnFlung);

        private void OnFlung(in NetArg arg, ulong sender)
        {
            // Broadcast — every machine hears it, exactly one owns this body and acts.
            if (!Network.Owns(this)) return;
            pending += arg.P;
        }

        private void FixedUpdate()
        {
            if (pending == Vector3.zero) return;
            Vector3 impulse = pending;
            pending = Vector3.zero;

            if (movement != null) movement.EnsureMovableBody();
            if (body == null || body.isKinematic) return;

            body.linearVelocity += impulse;
            // Without the latch, air control lerps the horizontal half back to walk speed in ~0.2 s.
            if (movement != null) movement.CarryMomentum();

            if (flungShake != null) CameraShakerHandler.Shake(flungShake);
            if (look != null && fovKick > 0f)
            {
                look.SetFovOffset(fovKick);
                fovKickUntil = Time.time + fovKickDuration;
                fovKickArmed = true;
            }
        }

        private void Update()
        {
            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                fovKickArmed = false;
                if (look != null) look.SetFovOffset(0f);
            }
        }
    }
}
```

- [ ] **Step 4: Run tests** — `FlungBodyTests` PASS, console clean.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Characters/Player/Movement/FlungBody.cs Assets/Game/Editor/Tests/FlungBodyTests.cs
git commit -m "feat: FlungBody applies Flung knockback on the owning machine"
```

---

### Task 4: `RepulsorBlastRing` — procedural shockwave ring VFX + dedicated shader

**Files:**
- Create: `Assets/Game/Art/Shaders/Artifacts/RepulsorShockwave.shader`
- Create: `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlastRing.cs`

Purely cosmetic and machine-local (spawned from `PresentHold` on every machine) — it must NEVER carry a NetworkObject and never goes in the network prefab list.

The ring gets its **own shader** — do NOT reuse `RuinScannerPulse.shader` (explicit review decision). A hand-written unlit additive shader renders fine under this project's URP setup (the other artifact shaders in `Art/Shaders/Artifacts/` are the precedent).

- [ ] **Step 1: Write the shader**

The annulus mesh maps V across the ring width (0 = inner edge, 1 = outer rim). The look: a hot leading edge at the outer rim, a faint trailing skirt behind it, the whole wave dying out as `_Progress` reaches 1.

```shaderlab
// Assets/Game/Art/Shaders/Artifacts/RepulsorShockwave.shader
Shader "SpaceGame/Artifacts/RepulsorShockwave"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.8, 1.0, 1.0)
        _Intensity ("Intensity", Range(0, 8)) = 3
        _Progress ("Progress", Range(0, 1)) = 0
        _TrailStrength ("Trail Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One   // additive
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _Intensity;
            float _Progress;
            float _TrailStrength;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float across = saturate(i.uv.y);          // 0 inner edge → 1 outer rim
                float lead = pow(across, 4.0);            // hot leading edge
                float trail = across * _TrailStrength;    // faint skirt behind it
                float fade = pow(saturate(1.0 - _Progress), 1.5); // wave dies as it lands
                float a = saturate(lead + trail) * fade;
                return fixed4(_Color.rgb * _Intensity * a, a * _Color.a);
            }
            ENDCG
        }
    }
}
```

- [ ] **Step 2: Implement the ring script**

```csharp
// Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlastRing.cs
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One expanding ground ring for a repulsor blast. Built procedurally (a flat annulus) so no
    /// prefab or FBX is needed; the material is expected to be the RepulsorShockwave shader, whose
    /// _Progress drives the wave's die-off. Local-only cosmetic — spawned by every machine from
    /// PresentHold, self-destroys, never networked.
    /// </summary>
    public class RepulsorBlastRing : MonoBehaviour
    {
        private const int Segments = 48;
        private const float InnerFraction = 0.7f;
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private float maxRadius;
        private float duration;
        private float startTime;
        private Material material;

        public static void Spawn(Vector3 position, float maxRadius, float duration, Material source)
        {
            if (source == null) return;
            var go = new GameObject("RepulsorBlastRing");
            go.transform.position = position;
            var ring = go.AddComponent<RepulsorBlastRing>();
            ring.maxRadius = Mathf.Max(0.5f, maxRadius);
            ring.duration = Mathf.Max(0.05f, duration);
            ring.material = new Material(source); // instance — _Progress is animated per ring
            ring.Build();
        }

        private void Build()
        {
            var mesh = new Mesh { name = "RepulsorRing" };
            var verts = new Vector3[Segments * 2];
            var uvs = new Vector2[Segments * 2];
            var tris = new int[Segments * 6];

            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = dir * InnerFraction; // unit mesh; transform scale animates size
                verts[i * 2 + 1] = dir;
                uvs[i * 2] = new Vector2(i / (float)Segments, 0f);
                uvs[i * 2 + 1] = new Vector2(i / (float)Segments, 1f);

                int next = (i + 1) % Segments;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = next * 2; tris[t + 2] = i * 2 + 1;
                tris[t + 3] = i * 2 + 1; tris[t + 4] = next * 2; tris[t + 5] = next * 2 + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            startTime = Time.time;
        }

        private void Update()
        {
            float t = (Time.time - startTime) / duration;
            if (t >= 1f) { Destroy(gameObject); return; }

            float eased = 1f - (1f - t) * (1f - t); // fast out, soft stop
            transform.localScale = Vector3.one * (maxRadius * eased);
            material.SetFloat(ProgressId, t);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
```

- [ ] **Step 3: Compile check** (console clean via MCP; shader compiles without errors in the console).

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Art/Shaders/Artifacts/RepulsorShockwave.shader* \
        Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorBlastRing.cs
git commit -m "feat: blast ring VFX with dedicated shockwave shader"
```

---

### Task 5: `RepulsorGauntletArtifact` — the item itself

**Files:**
- Create: `Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs`

Before writing, open the three exemplars this composes (read the cited regions, they carry the traps):
- `LassoArtifact.cs:210-330` — verbs in press-`B`, release-tick fire, `!HasOrientation` = cancel.
- `LaserStaffArtifact.cs:404-460` and `:851-871` — idempotent Hold/PresentHold, hold timeout, and the `IsAuthority`/`OwnerIsLocal` helpers (an equipped item's dormant NetworkObject makes `Network.Simulates` lie — copy the helpers, do not "improve" them).
- `LightningSpell.cs:60-104` — OverlapSphere + HashSet root billing, `NetDamage.Apply`.

- [ ] **Step 1: Implement**

```csharp
// Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs
using System.Collections.Generic;
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Hold Use to charge, release to fire a cone force blast. Charge follows the Lasso pattern:
    /// it never travels — every machine saw the press and runs its own clock; the authority prices
    /// the blast off its own elapsed time. The release rides the final hold tick with the aim in
    /// P/R; a release with no orientation (the default NetArg EquipmentController sends on
    /// unequip/death) is a CANCEL, never a fire.
    ///
    /// Physics is server-authoritative (Authority=Server): loose bodies are pushed directly,
    /// players via NetMsg.Flung applied by their own machine (FlungBody), leap-capable mounts via
    /// IMountLeapMotor. Cosmetics (ring, sound, hurt flinches, recoil on the caster) run per
    /// machine in Present/PresentHold.
    /// </summary>
    public class RepulsorGauntletArtifact : ToolItem
    {
        public override UseAuthority Authority => UseAuthority.Server;
        public override bool IsContinuous => true; // press opens the hold stream; release fires

        // Verbs in NetArg.B of the PRESS message only (B is the active flag on the hold stream,
        // A is the hotbar slot on both — same layout the Lasso and Grapple document).
        private const int MissVerb = 0;
        private const int ChargeVerb = 1;

        [Header("Charge")]
        [Tooltip("Seconds of hold for a full-power blast.")]
        [SerializeField] private float chargeTime = 1.2f;
        [Tooltip("Charge floor — a tap still fires this fraction of full power.")]
        [SerializeField, Range(0f, 1f)] private float minCharge = 0.25f;
        [Tooltip("Seconds after a blast before the gauntlet can charge again.")]
        [SerializeField] private float cooldownTime = 2.5f;
        [Tooltip("Cancel a charge if no hold tick arrives for this long (dropped release, disconnect). Must exceed the 1/15 s hold send interval.")]
        [SerializeField] private float holdTimeout = 0.5f;

        [Header("Blast")]
        [SerializeField] private float minRange = 6f;
        [SerializeField] private float maxRange = 13f;
        [Tooltip("Full cone angle, degrees.")]
        [SerializeField, Range(10f, 180f)] private float blastAngle = 75f;
        [Tooltip("Fling speed at min charge. Below ~9 m/s CarryMomentum self-cancels, so keep ≥ 12.")]
        [SerializeField] private float minFlingSpeed = 12f;
        [SerializeField] private float maxFlingSpeed = 22f;
        [Tooltip("Upward tilt of every fling, degrees. Load-bearing: vertical velocity is the half PlayerMovement never deletes.")]
        [SerializeField] private float upwardTilt = 27f;
        [Tooltip("Blast origin height above the holder's feet.")]
        [SerializeField] private float blastOriginHeight = 1.2f;
        [Tooltip("Damage per body caught in the blast. 0 = pure force.")]
        [SerializeField] private int blastDamage = 0;
        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full fling speed.")]
        [SerializeField] private float itemMassReference = 10f;

        [Header("Recoil (the caster)")]
        [Tooltip("Backward speed handed to the caster at full charge. Full-charge airborne recoil is the repulsor-jump.")]
        [SerializeField] private float recoilSpeed = 12f;

        [Header("Mount leap (kinematic agents that support it)")]
        [SerializeField] private float leapDistanceMin = 2f;
        [SerializeField] private float leapDistanceMax = 6f;
        [SerializeField] private float leapHeight = 1.2f;
        [SerializeField] private float leapDuration = 0.45f;

        [Header("Presentation")]
        [Tooltip("Child scaled up while charging. Assigned by the builder.")]
        [SerializeField] private Transform chargeGlow;
        [Tooltip("RepulsorShockwave-shader material for the ground ring. Assigned by the builder.")]
        [SerializeField] private Material ringMaterial;
        [SerializeField] private float ringDuration = 0.35f;
        [SerializeField] private ShakeData blastShake;
        [Tooltip("Only cameras within this range of the blast shake.")]
        [SerializeField] private float shakeRadius = 20f;
        [SerializeField] private SfxId chargeLoopId = SfxId.WeaponEnergyChargeLoop;
        [SerializeField] private SfxId blastId = SfxId.ImpactExplosion;
        [Tooltip("FOV pull-in (degrees) at full charge — anticipation. Positive number, applied negative.")]
        [SerializeField] private float chargeFovPull = 4f;
        [SerializeField] private float blastFovKick = 6f;
        [SerializeField] private float blastFovKickDuration = 0.2f;

        // Presentation state — per machine, driven by Present/PresentHold.
        private bool charging;
        private float chargeStart;
        private float lastHoldTime;
        private float cooldownUntil;

        // Authority state — only meaningful on the server (or the single machine offline).
        private bool authCharging;
        private float authChargeStart;
        private float authLastHoldTime;

        private readonly LoopingEmitter chargeLoop = new LoopingEmitter();
        private PlayerLook look;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;

        private float LocalCharge
            => RepulsorBlast.ChargeFrom(Time.time - chargeStart, chargeTime, minCharge);

        // ── Press ─────────────────────────────────────────────────────────────────────

        /// <summary>Owner, before the press message leaves. CanUse here is the cooldown refusing.</summary>
        public override void OnRequestUse(ref NetArg arg)
            => arg.B = CanUse() && Time.time >= cooldownUntil ? ChargeVerb : MissVerb;

        protected override bool CanUse() => base.CanUse() && Time.time >= cooldownUntil;

        /// <summary>Authority. Starts the pricing clock for this blast.</summary>
        protected override void Use()
        {
            if (UseArg.B != ChargeVerb) return;
            authCharging = true;
            authChargeStart = Time.time;
            authLastHoldTime = Time.time;
        }

        /// <summary>Every machine. Starts the cosmetic charge — glow, loop, FOV pull.</summary>
        protected override void Present()
        {
            if (UseArg.B != ChargeVerb || charging) return;
            charging = true;
            chargeStart = Time.time;
            lastHoldTime = Time.time;
            chargeLoop.Play(chargeLoopId, gameObject);
            if (chargeGlow != null) chargeGlow.gameObject.SetActive(true);
        }

        // ── Hold stream ───────────────────────────────────────────────────────────────

        /// <summary>Owner, every tick incl. the release tick. Aim only matters on release.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (active || !charging) return;
            Ray aim = aimProvider != null ? aimProvider.GetAimRay() : new Ray(transform.position, transform.forward);
            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
        }

        /// <summary>Authority. Keep-alive while held; the blast physics on release.</summary>
        protected override void Hold(NetArg arg, bool active)
        {
            if (active) { authLastHoldTime = Time.time; return; }
            if (!authCharging) return;
            authCharging = false;
            if (!arg.HasOrientation) return; // default NetArg = unequip/death = cancel

            float charge = RepulsorBlast.ChargeFrom(Time.time - authChargeStart, chargeTime, minCharge);
            FireBlast(arg.R * Vector3.forward, charge);
        }

        /// <summary>Every machine. Cosmetic release — or cancel.</summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            if (active) { lastHoldTime = Time.time; return; }
            if (!charging) return;

            float charge = LocalCharge;
            EndChargePresentation();

            if (!arg.HasOrientation) return; // cancel: glow and loop already stopped, no blast

            cooldownUntil = Time.time + cooldownTime;
            PlayBlastFx(arg.R * Vector3.forward, charge);
            if (OwnerIsLocal()) ApplyRecoil(arg.R * Vector3.forward, charge);
        }

        // ── The blast (authority only) ────────────────────────────────────────────────

        private void FireBlast(Vector3 dir, float charge)
        {
            if (owner == null) return;
            Vector3 origin = owner.transform.position + Vector3.up * blastOriginHeight;
            float radius = Mathf.Lerp(minRange, maxRange, charge);
            GameObject ownerRoot = owner.transform.root.gameObject;
            var seen = new HashSet<GameObject>();

            foreach (Collider hit in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject root = hit.transform.root.gameObject;
                if (root == ownerRoot || root == gameObject || !seen.Add(root)) continue;

                Vector3 targetPos = hit.bounds.center;
                if (!RepulsorBlast.InCone(origin, dir, targetPos, radius, blastAngle * 0.5f)) continue;

                Vector3 fling = RepulsorBlast.FlingVelocity(origin, dir, targetPos, charge,
                    radius, minFlingSpeed, maxFlingSpeed, upwardTilt);

                if (root.GetComponent<PlayerMovement>() != null)
                {
                    // Owner-authoritative body: the victim's own machine applies it (FlungBody).
                    NetMessaging.NetSendTo(root, NetMsg.Flung, new NetArg { P = fling }, NetTo.All);
                }
                else if (root.GetComponentInChildren<AgentController>() != null)
                {
                    // Kinematic, motor-owned transform — forces never land. Leap if the motor can;
                    // otherwise the cosmetic sweep's hurt flinch is all v1 gives (deferred by spec).
                    var leaper = root.GetComponentInChildren<IMountLeapMotor>();
                    if (leaper != null && leaper.IsLeapAvailable)
                    {
                        float falloff = fling.magnitude / Mathf.Max(maxFlingSpeed, 0.01f);
                        Vector3 away = Vector3.ProjectOnPlane(fling, Vector3.up).normalized;
                        leaper.RequestLeap(away,
                            Mathf.Lerp(leapDistanceMin, leapDistanceMax, charge) * falloff,
                            leapHeight, leapDuration);
                    }
                }
                else
                {
                    Rigidbody body = hit.attachedRigidbody;
                    if (body == null) continue;
                    // Only a real, simulated body — a kinematic replica is kinematic on purpose
                    // (the LassoTether guard).
                    if (body.isKinematic)
                    {
                        if (!Network.Simulates(body)) continue;
                        body.isKinematic = false;
                    }
                    float massScale = Mathf.Clamp(itemMassReference / Mathf.Max(body.mass, 0.1f), 0.2f, 1.5f);
                    body.AddForce(fling * massScale, ForceMode.VelocityChange);
                }

                if (blastDamage > 0)
                    NetDamage.Apply(root, blastDamage, owner.transform);
            }
        }

        // ── Cosmetics (every machine) ─────────────────────────────────────────────────

        private void PlayBlastFx(Vector3 dir, float charge)
        {
            if (owner == null) return;
            Vector3 feet = owner.transform.position + Vector3.up * 0.1f;
            Vector3 origin = owner.transform.position + Vector3.up * blastOriginHeight;
            float radius = Mathf.Lerp(minRange, maxRange, charge);

            RepulsorBlastRing.Spawn(feet, radius, ringDuration, ringMaterial);
            Sfx.Play(blastId, origin, default, GetInstanceID());

            // Proximity-dosed shake: full for anyone near the blast (caster and victims included).
            if (blastShake != null && Camera.main != null &&
                (Camera.main.transform.position - origin).sqrMagnitude < shakeRadius * shakeRadius)
                CameraShakerHandler.Shake(blastShake);

            // Cosmetic hurt flinch on agents in the cone — run per machine because animator
            // triggers do not replicate. Same cone math as the authority, so they agree.
            var seen = new HashSet<GameObject>();
            foreach (Collider hit in Physics.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject root = hit.transform.root.gameObject;
                if (root == owner.transform.root.gameObject || !seen.Add(root)) continue;
                if (!RepulsorBlast.InCone(origin, dir, hit.bounds.center, radius, blastAngle * 0.5f)) continue;
                root.GetComponentInChildren<AgentAnimatorDriver>()?.TriggerHurt();
            }

            if (OwnerIsLocal() && look != null && blastFovKick > 0f)
            {
                look.SetFovOffset(blastFovKick);
                fovKickUntil = Time.time + blastFovKickDuration;
                fovKickArmed = true;
            }
        }

        private void ApplyRecoil(Vector3 dir, float charge)
        {
            var movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            var body = owner != null ? owner.GetComponent<Rigidbody>() : null;
            if (movement == null || body == null) return;

            Vector3 back = (-Vector3.ProjectOnPlane(dir, Vector3.up).normalized + Vector3.up * 0.35f).normalized;
            movement.EnsureMovableBody();
            if (body.isKinematic) return;
            body.linearVelocity += back * (recoilSpeed * charge);
            movement.CarryMomentum();
        }

        private void EndChargePresentation()
        {
            charging = false;
            chargeLoop.Stop();
            if (chargeGlow != null) chargeGlow.gameObject.SetActive(false);
            if (OwnerIsLocal() && look != null && !fovKickArmed) look.SetFovOffset(0f);
        }

        // ── Housekeeping ──────────────────────────────────────────────────────────────

        private void Update()
        {
            // Timeout: a release that never arrives (dropped packet, holder disconnect).
            if (charging && Time.time - lastHoldTime > holdTimeout) EndChargePresentation();
            if (authCharging && Time.time - authLastHoldTime > holdTimeout) authCharging = false;

            if (charging)
            {
                if (chargeGlow != null)
                    chargeGlow.localScale = Vector3.one * Mathf.Lerp(0.03f, 0.14f, LocalCharge);
                if (OwnerIsLocal() && look != null)
                    look.SetFovOffset(-chargeFovPull * LocalCharge);
            }

            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                fovKickArmed = false;
                if (look != null) look.SetFovOffset(0f);
            }
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            look = holder != null ? holder.GetComponent<PlayerLook>() : null;
            if (chargeGlow != null) chargeGlow.gameObject.SetActive(false);
        }

        public override void OnUnequipped(GameObject holder)
        {
            // EndHold(send:false) has already delivered the default-NetArg cancel; this is the
            // belt-and-braces sweep for the FOV and the loop (the grapple's documented reset trap).
            EndChargePresentation();
            authCharging = false;
            if (look != null) look.SetFovOffset(0f);
            look = null;
            base.OnUnequipped(holder);
        }

        private void OnDisable()
        {
            chargeLoop.Stop(false);
        }

        // Copied from LaserStaffArtifact (:851-871): an equipped artifact is instantiated into a
        // hand, never spawned, so its dormant NetworkObject makes Network.Simulates answer true on
        // every machine. Do not replace these with Simulates.
        private bool OwnerIsLocal()
        {
            if (!Network.IsNetworked) return true;
            var netObj = owner != null ? owner.GetComponentInParent<NetworkObject>() : null;
            return netObj != null && netObj.IsOwner;
        }

        private void OnValidate()
        {
            holdTimeout = Mathf.Max(0.2f, holdTimeout); // must exceed the 1/15 s hold interval
            maxRange = Mathf.Max(maxRange, minRange);
            maxFlingSpeed = Mathf.Max(maxFlingSpeed, minFlingSpeed);
        }
    }
}
```

- [ ] **Step 2: Cross-check the five borrowed APIs before compiling**

Grep and confirm exact signatures (they were verified 2026-08-24, but this file is the integration point):
- `aimProvider.GetAimRay()` — `LassoArtifact.cs` uses it in `OnRequestHold`.
- `NetMessaging.NetSendTo(<target>, ushort, NetArg, NetTo)` — `SceneTransition.cs:423`. If the first parameter is typed `Component`, pass `root.transform` instead of `root`.
- `owner` and `OnEquipped/OnUnequipped` signatures on `UsableItem.cs:248-284`.
- `Sfx.Play(SfxId, Vector3, EventReference, int)` — `Sfx.cs:37-82`.
- `SfxId.WeaponEnergyChargeLoop` (204) and `SfxId.ImpactExplosion` (304) exist in `SfxId.cs`.

- [ ] **Step 3: Compile check** (console clean).

- [ ] **Step 4: Run the full EditMode suite** — nothing broken.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Scripts/Items/Artifacts/Gadgets/RepulsorGauntletArtifact.cs
git commit -m "feat: repulsor gauntlet artifact — charge, blast, recoil, cosmetics"
```

---

### Task 6: Builder — prefab, item asset, network registration, player wiring, shake assets

**Files:**
- Create: `Assets/Game/Editor/AssetPipeline/RepulsorGauntletBuilder.cs`

Follows the template in `.claude/skills/spacegame-artifact/references/prefab-and-builder.md` exactly (LaserStaffBuilder pattern). v1 is a greybox mesh built from primitives — the real model is a follow-up via the blender-model skill; the builder is re-runnable and will pick up the FBX later by swapping `BuildModel()`. Builders replace the prefab wholesale: ALL tuning lives in this script or in the artifact's serialized defaults.

- [ ] **Step 1: Implement the builder**

```csharp
// Assets/Game/Editor/AssetPipeline/RepulsorGauntletBuilder.cs
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Characters;
using SpaceGame.Items;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class RepulsorGauntletBuilder
    {
        private const string PrefabPath = "Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab";
        private const string ItemPath = "Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset";
        private const string RingMatPath = "Assets/Game/Art/Materials/Artifacts/RepulsorBlastRing.mat";
        private const string ShockwaveShaderPath = "Assets/Game/Art/Shaders/Artifacts/RepulsorShockwave.shader";
        private const string ShakeSourcePath = "Assets/Game/ScriptableObjects/Shake/DamageShake.asset";
        private const string BlastShakePath = "Assets/Game/ScriptableObjects/Shake/RepulsorBlastShake.asset";
        private const string FlungShakePath = "Assets/Game/ScriptableObjects/Shake/RepulsorFlungShake.asset";
        private const string PlayerPrefabPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab";
        private const string NetworkPrefabsPath = "Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset";
        private const int GroundLayerMask = 128;

        [MenuItem("Tools/SpaceGame/Items/Build Repulsor Gauntlet")]
        public static void Build()
        {
            Material ringMat = EnsureRingMaterial();
            ShakeData blastShake = EnsureShake(BlastShakePath);

            var root = new GameObject("RepulsorGauntlet");

            GameObject model = BuildModel(root.transform);

            var grip = new GameObject("GripPoint");
            grip.transform.SetParent(root.transform, false);
            grip.transform.localPosition = new Vector3(0f, 0f, -0.05f); // inside the cuff

            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "ChargeGlow";
            UnityEngine.Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.12f); // over the knuckles
            glow.transform.localScale = Vector3.one * 0.03f;
            glow.GetComponent<MeshRenderer>().sharedMaterial = ringMat;
            glow.SetActive(false);

            // ── Pickup / world presence (per the prefab reference table) ──
            var netObject = root.AddComponent<NetworkObject>();
            netObject.SynchronizeTransform = true;

            SphereCollider sphere = root.AddComponent<SphereCollider>();
            sphere.radius = 0.14f;

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = true;

            AddInternal(root, "SpaceGame.Items.PickupableItem");

            var drop = root.AddComponent<DropItemPhysics>();
            SetPrivate(drop, "rb", body);
            SetPrivateLayerMask(drop, "groundLayer", GroundLayerMask);

            root.AddComponent<SpaceGame.Core.NetRelay>();
            root.AddComponent<SpaceGame.Core.Persistence.SaveableEntity>();
            root.AddComponent<SpaceGame.Core.Persistence.TransformSaveable>();

            // ── Grip: worn on the hand — grip point sits in the palm cavity ──
            var itemGrip = root.AddComponent<ItemGrip>();
            SetPrivate(itemGrip, "gripPoint", grip.transform);
            SetPrivate(itemGrip, "holdSize", 0.3f);
            SetPrivate(itemGrip, "sizeReference", model.transform);

            // ── The artifact ──
            var artifact = root.AddComponent<RepulsorGauntletArtifact>();
            SetPrivate(artifact, "chargeGlow", glow.transform);
            SetPrivate(artifact, "ringMaterial", ringMat);
            SetPrivate(artifact, "blastShake", blastShake);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath) ?? ".");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            if (prefab == null) { Debug.LogError("[Repulsor] Prefab save failed."); return; }

            InventoryItem item = EnsureItem(prefab);
            WireItemIntoPickup(prefab, item);
            RegisterNetworkPrefab(prefab);
            WireFlungBodyIntoPlayer();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Repulsor] Built {PrefabPath} and {ItemPath}. " +
                      "Run Tools/Generate All Item Icons for the inventory icon.");
        }

        /// <summary>Greybox: a forearm cuff + fist plate from primitives. Replaced by an FBX later.</summary>
        private static GameObject BuildModel(Transform parent)
        {
            var model = new GameObject("Model");
            model.transform.SetParent(parent, false);

            var cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cuff.name = "Cuff";
            UnityEngine.Object.DestroyImmediate(cuff.GetComponent<Collider>());
            cuff.transform.SetParent(model.transform, false);
            cuff.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // long axis along +Z
            cuff.transform.localScale = new Vector3(0.09f, 0.11f, 0.09f);

            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(model.transform, false);
            plate.transform.localPosition = new Vector3(0f, 0.02f, 0.13f);
            plate.transform.localScale = new Vector3(0.1f, 0.04f, 0.08f);

            return model;
        }

        private static Material EnsureRingMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(RingMatPath);
            if (mat != null) return mat;

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShockwaveShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[Repulsor] Shockwave shader missing at {ShockwaveShaderPath} — run Task 4 first.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            mat = new Material(shader);
            Directory.CreateDirectory(Path.GetDirectoryName(RingMatPath) ?? ".");
            AssetDatabase.CreateAsset(mat, RingMatPath);
            return mat;
        }

        private static ShakeData EnsureShake(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<ShakeData>(path);
            if (existing != null) return existing;
            if (!AssetDatabase.CopyAsset(ShakeSourcePath, path))
                Debug.LogError($"[Repulsor] Could not copy {ShakeSourcePath} → {path}.");
            return AssetDatabase.LoadAssetAtPath<ShakeData>(path);
        }

        /// <summary>Idempotently adds FlungBody to the real player prefab and wires its shake.</summary>
        [MenuItem("Tools/SpaceGame/Items/Wire FlungBody Into Player")]
        public static void WireFlungBodyIntoPlayer()
        {
            ShakeData flungShake = EnsureShake(FlungShakePath);

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var flung = root.GetComponent<FlungBody>();
                if (flung == null) flung = root.AddComponent<FlungBody>();
                var so = new SerializedObject(flung);
                so.FindProperty("flungShake").objectReferenceValue = flungShake;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[Repulsor] FlungBody wired into the player prefab.");
        }

        private static InventoryItem EnsureItem(GameObject prefab)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ItemPath) ?? ".");
            var item = AssetDatabase.LoadAssetAtPath<InventoryItem>(ItemPath);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<InventoryItem>();
                AssetDatabase.CreateAsset(item, ItemPath);
            }
            item.itemName = "Repulsor Gauntlet";
            item.itemPrefab = prefab;
            EditorUtility.SetDirty(item);
            return item;
        }

        private static void WireItemIntoPickup(GameObject prefab, InventoryItem item)
        {
            Component pickup = prefab.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().FullName == "SpaceGame.Items.PickupableItem");
            if (pickup == null) { Debug.LogError("[Repulsor] PickupableItem missing."); return; }
            var so = new SerializedObject(pickup);
            so.FindProperty("item").objectReferenceValue = item;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(prefab);
        }

        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null) { Debug.LogError($"[Repulsor] No list at {NetworkPrefabsPath}."); return; }
            if (list.Contains(prefab)) return;
            list.Add(new NetworkPrefab { Prefab = prefab });
            EditorUtility.SetDirty(list);
        }

        private static void AddInternal(GameObject go, string typeName)
        {
            Type type = typeof(ItemGrip).Assembly.GetType(typeName);
            if (type == null) { Debug.LogError($"No type {typeName}."); return; }
            go.AddComponent(type);
        }

        private static FieldInfo Field(object target, string name) =>
            target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static void SetPrivate(object target, string name, object value) =>
            Field(target, name)?.SetValue(target, value);

        private static void SetPrivateLayerMask(object target, string name, int mask) =>
            Field(target, name)?.SetValue(target, (LayerMask)mask);
    }
}
```

- [ ] **Step 2: Compile check** (console clean).

- [ ] **Step 3: Run the builder** — menu `Tools/SpaceGame/Items/Build Repulsor Gauntlet` (via MCP `Unity_ManageMenuItem` or in the editor).

Expected console: `[Repulsor] Built … Run Tools/Generate All Item Icons …` and `[Repulsor] FlungBody wired into the player prefab.`

- [ ] **Step 4: Verify the writes actually landed** (the AssetDatabase-read-only trap: saves can be discarded silently)

```bash
test -f Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab && echo prefab-ok
test -f Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset && echo item-ok
grep -c "GlobalObjectIdHash: 0$" Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab || echo hash-nonzero-ok
GUID=$(grep guid Assets/Game/Scripts/Characters/Player/Movement/FlungBody.cs.meta | awk '{print $2}')
grep -q "$GUID" Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab && echo flungbody-wired-ok
GGUID=$(grep guid Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab.meta | awk '{print $2}')
grep -q "$GGUID" Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset && echo netprefab-ok
```

Expected: `prefab-ok`, `item-ok`, hash check shows **no** `GlobalObjectIdHash: 0` (a zero hash can never spawn on a client), `flungbody-wired-ok`, `netprefab-ok`. If any fails, check for a read-only AssetDatabase / MPPM clone and re-run in the real editor.

- [ ] **Step 5: Generate the icon** — run `Tools/Generate All Item Icons`. Verify `Assets/Game/Art/Sprites/Items/RepulsorGauntlet.png` exists (name may derive from the item; check the tool's output).

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Editor/AssetPipeline/RepulsorGauntletBuilder.cs \
        Assets/Game/Prefabs/Items/Artifacts/Gadgets/RepulsorGauntlet.prefab* \
        Assets/Game/Resources/Items/Artifacts/RepulsorGauntlet.asset* \
        Assets/Game/Art/Materials/Artifacts/RepulsorBlastRing.mat* \
        Assets/Game/ScriptableObjects/Shake/RepulsorBlastShake.asset* \
        Assets/Game/ScriptableObjects/Shake/RepulsorFlungShake.asset* \
        Assets/Game/Prefabs/Characters/Player/PlayerCharacterNetworked.prefab \
        Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset \
        Assets/Game/Art/Sprites/Items/RepulsorGauntlet.png*
git commit -m "feat: repulsor gauntlet prefab, item asset, player FlungBody wiring"
```

---

### Task 7: Guard-rail test suites

**Files:** none new — this task runs the existing pipeline guards.

- [ ] **Step 1: Run the full headless EditMode suite** — `Tools/Tests/Run EditMode Tests (headless)`.

Expected green, specifically:
- `NetworkPrefabRegistrationTests` — the gauntlet's root NetworkObject is registered.
- `HoldPoseTests` — `OnEquipped` adds a HoldAnimator (the gauntlet keeps `UsesHoldPose = true`).
- `RepulsorBlastMathTests`, `FlungBodyTests` from Tasks 2–3.

- [ ] **Step 2: If `NetworkPrefabRegistrationTests` fails**, the builder's registration didn't land — re-run Task 6 Step 4's checks before touching code.

- [ ] **Step 3: Commit any stragglers** (e.g. regenerated meta files):

```bash
git status --short
git add <only files this feature created/modified>
git commit -m "chore: repulsor gauntlet pipeline artifacts"
```

---

### Task 8: In-editor smoke test (single machine)

Manual, in the editor. Enter Play Mode with `GameSettings.DevMode`, press `I`, spawn the Repulsor Gauntlet from the dev browser (the registry lists it automatically — nothing to wire).

- [ ] Equip it: it sits on/near the hand (tune `ItemGrip` offsets in the **builder script** if not — hand edits are destroyed on the next builder run; use `Tools/SpaceGame/Items/Audit Held Item Poses` to measure).
- [ ] Hold Use: glow grows, charge loop audible, FOV pulls in slightly. Release: ring expands, explosion sound, FOV kicks out and recovers, shake fires.
- [ ] Drop a few loose items (scanner, lasso) in front; blast: they scatter, heavier ones less.
- [ ] Blast a creature with a leap-capable motor (Ostrich): it leaps away. A legged walker: hurt flinch only (expected v1).
- [ ] Fire at nothing at max charge while jumping: recoil is a usable repulsor-hop.
- [ ] Scroll the hotbar mid-charge: charge cancels silently — NO blast (the `HasOrientation` guard).
- [ ] Wait out the cooldown message: pressing Use during cooldown does nothing (MissVerb).
- [ ] Save, quit, reload: gauntlet still in inventory, no console errors (no new persisted state exists to check — by design, spec §6).

Fix and re-commit as needed; keep fixes inside the artifact/builder scripts.

---

### Task 9: Multiplayer verification (definition of done — spec §7)

Host + at least one real client (Multiplayer Play Mode or two editors). **Host-only proof is not proof.**

- [ ] Client picks up a dropped gauntlet (network prefab registration proof — this fails ONLY on clients).
- [ ] Client charges and fires at the host player: host is flung on the host's screen; both machines saw ring/sound.
- [ ] Host fires at the client: the client is flung **on the client's machine** (the `FlungBody` path — the core of the feature).
- [ ] A third machine (or the other of the pair) watching someone else's blast sees identical ring/particles/audio and any agent flinches.
- [ ] Loose items scatter to the same resting places on both machines (server-simulated, NetworkTransform).
- [ ] Client death / hotbar-scroll mid-charge: no phantom blast on any machine.
- [ ] Client disconnects mid-charge: host's gauntlet stops charging within `holdTimeout` (watch the glow).
- [ ] Note: if testing under MPPM, remember the UDP port-leak gotcha — bump the port in `NetworkManager.prefab` if joins fail after repeated Play sessions.

Record results in the PR/summary. Any red item = the feature is not done.

---

## Deferred (explicitly out of v1 — do not silently start)

- Real gauntlet mesh via the blender-model skill (builder's `BuildModel()` swaps to the FBX).
- Full "shoved" displacement for non-leap agents (suspend self-drive → displace → resume, per LassoTether).
- Screen-space shockwave distortion (no per-event drivable distortion feature exists).
- `startingItems` / loot-table / trade routing — dev browser access is enough for playtesting.
