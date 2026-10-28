using System.Collections.Generic;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Status;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The Raxy hunter's electric harpoon gun, made to fire. Press Use: the loaded harpoon leaves
    /// the barrel on a wire, and whatever living thing it bites goes down shocked for a few seconds
    /// while the current crackles over it. Then the reel winds the harpoon home and it can fire again.
    ///
    /// <para>
    /// <b>Authority: server.</b> The shock is a <see cref="StatusKind.Shocked"/> condition on the
    /// victim's <see cref="StatusReceiver"/> — contested world state, decided once, replicated by
    /// the status system to every machine, which is what puts the body on the floor everywhere.
    /// Everything this class draws (the dart, the wire, the arcs) is local to each machine and
    /// rebuilt from the press, so nothing visual travels: the owner resolves the aim once in
    /// <see cref="OnRequestUse"/> and every machine flies the same dart to the same body.
    /// </para>
    /// <para>
    /// The shock lands with the dart, not with the press. The authority waits out the same flight
    /// time each machine draws, so the body drops on the frame the harpoon is seen to bite.
    /// </para>
    /// <para>
    /// <b>What it costs</b> (GDC-L1-BAL-0004): one dart, visibly in flight, on a short line; a miss
    /// still costs the whole trip out and back on the reel; and the victim cannot be kept down by alternating shooters,
    /// because <see cref="ShockedStatus"/> refuses a second shock until the first has worn off and a
    /// breather has passed. It is a control tool, not an execution — the harpoon's own bite is small.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> nothing, as a decision. The only runtime state is a dart a few seconds
    /// out; a gun restored with its harpoon half way to a target would be a bug.
    /// The shock itself is a status, which is deliberately not saved either (StatusEffects.md).
    /// </para>
    /// </summary>
    public class ShockHarpoonArtifact : ToolItem
    {
        public override UseAuthority Authority => UseAuthority.Server;

        // NetArg.B on a press. A is the slot code, reserved by EquipmentController.
        private const int Refused = 0;
        private const int FiredAtAir = 1;
        private const int FiredAtSurface = 2;

        private enum Phase { Loaded, Flying, Stuck, Reeling }

        [Header("Shot")]
        [Tooltip("How far the wire reaches, in metres. A press aimed past it still fires, and the " +
                 "harpoon falls short at the end of the line.")]
        [SerializeField] private float range = 22f;
        [Tooltip("Harpoon speed, metres per second. Slow enough to be seen in flight — the wire " +
                 "paying out behind it is half of what the item looks like.")]
        [SerializeField] private float flightSpeed = 45f;
        [Tooltip("Speed the reel winds the harpoon home, metres per second. There is no reload " +
                 "beyond it: the gun fires again the moment the harpoon is back in the barrel.")]
        [SerializeField] private float reelSpeed = 30f;

        [Header("Shock")]
        [Tooltip("Seconds the victim stays down, then gets up. Named here rather than left to the " +
                 "Shocked condition's own default: that default is serialized into every body's " +
                 "StatusReceiver, so a prefab saved earlier would keep its old number. Zero uses it anyway.")]
        [SerializeField] private float shockSeconds = 7.5f;
        [Tooltip("Seconds the harpoon stays in a body before the reel pulls it out. The shock outlasts " +
                 "it: the body stays down and crackling after the harpoon is home.")]
        [SerializeField] private float bodyHoldSeconds = 1.5f;
        [Tooltip("Seconds the harpoon waits in a body for the shock to take before giving up and " +
                 "reeling in — the body was immune, or already dead. Covers the round trip to the server.")]
        [SerializeField] private float shockGrace = 0.6f;
        [Tooltip("The harpoon's own bite, in health. Small on purpose: the shock is the point.")]
        [SerializeField] private int damage = 8;
        [Tooltip("Seconds the harpoon stays in a wall or a crate before the reel pulls it out.")]
        [SerializeField] private float surfaceHoldSeconds = 0.35f;
        [Tooltip("Metres the tip sinks into what it hits.")]
        [SerializeField] private float embedDepth = 0.06f;
        [Tooltip("Metres of slack in the wire while the harpoon is set, so it hangs like a cable " +
                 "rather than standing like a rod.")]
        [SerializeField] private float wireSlack = 0.6f;

        [Header("Parts (assigned by the builder)")]
        [Tooltip("The harpoon loaded in the gun. Hidden while it is out; the flying copy is made from it.")]
        [SerializeField] private Transform loadedHarpoon;
        [Tooltip("The harpoon's tip and tail, in the loaded harpoon's own space.")]
        [SerializeField] private Vector3 harpoonTip;
        [SerializeField] private Vector3 harpoonTail;
        [Tooltip("The barrel's mouth: where the wire leaves the gun.")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private LineRenderer wireLine;
        [SerializeField] private GrappleRope wire = new GrappleRope();
        [Tooltip("The current crackling down the wire while the body on the end is shocked.")]
        [SerializeField] private WireCurrent current;

        [Header("Current")]
        [Tooltip("One short arc from the harpoon into the body. Instantiated locally, never networked.")]
        [SerializeField] private LightningBoltEffect arcPrefab;
        [Tooltip("Seconds between arcs while the current runs.")]
        [SerializeField] private float arcInterval = 0.07f;

        [Header("Sound")]
        [SerializeField] private SfxId fireSoundId = SfxId.WeaponProjectileWhoosh;
        [SerializeField] private SfxId bodyBiteSoundId = SfxId.ImpactFlesh;
        [SerializeField] private SfxId surfaceBiteSoundId = SfxId.ImpactProjectile;
        [SerializeField] private SfxId shockSoundId = SfxId.WeaponBallLightningArc;
        [Tooltip("Seconds between crackles while the current runs.")]
        [SerializeField] private float crackleInterval = 0.6f;
        [SerializeField] private SfxId reeledSoundId = SfxId.RopeCoil;

        // ── Presentation, per machine ──
        private Phase phase = Phase.Loaded;
        private HarpoonDart dart;
        private float phaseStart;
        private float phaseLength;
        private Vector3 heading;
        private Vector3 tailFrom;
        private Vector3 aimPoint;
        private Transform aimHost;
        private Vector3 aimLocal;
        private bool shocking;
        private bool currentSeen;
        private StatusReceiver victimStatus;
        private bool struckSurface;
        private float wireLength;
        private float nextArc;
        private float nextCrackle;
        private readonly List<Transform> arcTargets = new List<Transform>();

        // ── Authority. A second clock on purpose — see WristBladeArtifact: on a host Present runs
        // first, and a shared clock would read the host's own press as still cooling down. ──
        private float authCooldownUntil;
        private float authShockAt = float.PositiveInfinity;
        private GameObject authVictim;

        /// <summary>
        /// Owner, before the press leaves: the one machine with a live camera, or the server for an
        /// NPC holder. The hit point and the thing hit travel, so every machine flies its dart to the
        /// same place on the same body.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            // An NPC's press arrives with the shot already aimed (NpcItemFire.AimedArg): from its hand
            // in P, towards its target in R. A press with neither aims nowhere.
            bool npc = aimProvider == null;
            Ray aim = npc ? new Ray(arg.P, arg.R * Vector3.forward) : aimProvider.GetAimRay();

            arg.B = Refused;
            if (phase != Phase.Loaded || (npc && arg.P == Vector3.zero)) return;

            arg.R = Quaternion.LookRotation(aim.direction);

            if (npc ? TryNpcAimHit(aim, out RaycastHit hit) : aimProvider.TryGetAimHit(range, out hit))
            {
                arg.B = FiredAtSurface;
                arg.P = hit.point;
                arg = arg.With(hit.collider.gameObject);
                return;
            }

            arg.B = FiredAtAir;
            arg.P = aim.origin + aim.direction * range;
        }

        protected override bool CanUse() => base.CanUse() && Time.time >= authCooldownUntil;

        /// <summary>Authority: arm the shock to land when the dart does.</summary>
        protected override void Use()
        {
            if (UseArg.B == Refused || owner == null)
            {
                CancelUse();
                return;
            }

            // The gun is ready when its harpoon is home, which only the machines drawing the dart
            // see. The authority refuses only what no real shot could do: fire again before the
            // harpoon has had time to go out to the hit and be reeled back.
            authCooldownUntil = Time.time + FlightSeconds(UseArg.P) + ReelSeconds(UseArg.P);

            authVictim = UseArg.B == FiredAtSurface ? LivingBody(UseArg.Resolve()) : null;
            if (authVictim == null) return;

            // Ensured on the authority too: a dedicated server never runs Present.
            StatusReceiver.EnsureOnBody(authVictim);
            authShockAt = Time.time + FlightSeconds(UseArg.P);
        }

        /// <summary>Every machine, at once on the shooter's: the harpoon leaves the barrel.</summary>
        protected override void Present()
        {
            if (UseArg.B == Refused || owner == null || loadedHarpoon == null || muzzle == null) return;

            // A press that arrives while a dart is still out (a reordered message) starts over
            // rather than flying two harpoons off one barrel.
            EndShot();

            struckSurface = UseArg.B == FiredAtSurface;

            GameObject hit = struckSurface ? UseArg.Resolve() : null;
            GameObject victim = LivingBody(hit);
            shocking = victim != null;

            // Every machine, before the server's status message can arrive: a body with no
            // receiver drops that message without a word (StatusEffects.md).
            victimStatus = shocking ? StatusReceiver.EnsureOnBody(victim) : null;
            // Now, while the body still stands: the X-ray measures which way its face points.
            ShockedVisual.EnsureOn(victimStatus);

            aimPoint = UseArg.P;
            aimHost = shocking ? NearestBone(victim.transform, aimPoint) : hit != null ? hit.transform : null;
            if (aimHost != null) aimLocal = aimHost.InverseTransformPoint(aimPoint);

            dart = new HarpoonDart(loadedHarpoon, harpoonTip, harpoonTail);
            loadedHarpoon.gameObject.SetActive(false);

            // Along the dart's own path, muzzle to target. The aim ray leaves the EYE, so pointing the
            // dart along it would fly the harpoon visibly sideways at anything close.
            Vector3 path = aimPoint - muzzle.position;
            heading = path.sqrMagnitude > 1e-4f ? path.normalized
                    : UseArg.HasOrientation ? UseArg.R * Vector3.forward : muzzle.forward;
            BeginPhase(Phase.Flying, FlightSeconds(aimPoint));

            wire.Bind(wireLine);
            wire.Show();
            // The gun fires the harpoon live: the wire crackles all the way out, whatever it is
            // aimed at. Only once it bites does the current follow the victim's condition.
            if (current != null) current.Live = true;

            Sfx.Play(fireSoundId, muzzle.position, GetInstanceID());
        }

        private void Update()
        {
            TickAuthority();

            switch (phase)
            {
                case Phase.Flying: TickFlight(); break;
                case Phase.Stuck: TickStuck(); break;
                case Phase.Reeling: TickReel(); break;
            }
        }

        private void TickAuthority()
        {
            if (Time.time < authShockAt) return;
            authShockAt = float.PositiveInfinity;

            GameObject victim = authVictim;
            authVictim = null;
            if (victim == null) return;

            HealthComponent health = victim.GetComponent<HealthComponent>();
            if (health != null && !health.Alive) return;

            Transform source = owner != null ? owner.transform : transform;
            StatusReceiver.EnsureOnBody(victim)?.Apply(StatusKind.Shocked, shockSeconds, source: source);
            if (damage > 0) NetDamage.Apply(victim, damage, source);
        }

        // ── The shot, every machine ─────────────────────────────────────────────

        private void TickFlight()
        {
            float t = Progress();
            Vector3 tailTo = AimPoint() - heading * dart.Length;
            Vector3 tail = Vector3.Lerp(tailFrom, tailTo, t);

            dart.PlaceTail(tail, heading);
            // The wire runs from the barrel to the dart's tail, which travels exactly this lerp.
            wire.DrawFlight(muzzle.position, tailTo, t);

            if (t >= 1f) Bite();
        }

        private void Bite()
        {
            Vector3 tip = AimPoint() + heading * embedDepth;
            dart.PlaceTail(tip - heading * dart.Length, heading);

            if (!struckSurface)
            {
                // Open air: nothing to bite, the line simply runs out and the reel takes it back.
                StartReel();
                return;
            }

            dart.StickTo(aimHost);
            wire.Bite();
            wireLength = Vector3.Distance(muzzle.position, dart.Tail) + wireSlack;

            if (shocking)
            {
                CollectArcTargets(aimHost);
                currentSeen = false;
                Sfx.Play(bodyBiteSoundId, tip, GetInstanceID() + 1);
                BeginPhase(Phase.Stuck, bodyHoldSeconds);
            }
            else
            {
                // Earthed in a wall: the charge it flew out with is spent.
                if (current != null) current.Live = false;
                Sfx.Play(surfaceBiteSoundId, tip, GetInstanceID() + 1);
                BeginPhase(Phase.Stuck, surfaceHoldSeconds);
            }
        }

        private void TickStuck()
        {
            // The body it was in is gone: despawned, streamed out. The reel takes the dart back.
            if (shocking && (aimHost == null || victimStatus == null))
            {
                StartReel();
                return;
            }

            wire.DrawTether(muzzle.position, dart.Tail, wireLength);

            if (!shocking)
            {
                if (Progress() >= 1f) StartReel();
                return;
            }

            // Drawn off the condition itself, never off a timer of this item's own: the server may
            // have refused the shock (the body is immune, or dead), cleared it early, or be a
            // round trip behind, and arcs on a body that is not shocked would be a lie.
            bool live = victimStatus.Has(StatusKind.Shocked);
            if (current != null) current.Live = live;

            if (live)
            {
                currentSeen = true;
                TickCurrent();
            }

            // Out after its hold whatever the shock is doing, or early once the shock has gone or
            // never came.
            if (Progress() >= 1f || (!live && (currentSeen || Time.time - phaseStart > shockGrace)))
                StartReel();
        }

        /// <summary>The current running: arcs from the harpoon into the body, and its crackle.</summary>
        private void TickCurrent()
        {
            if (arcPrefab != null && Time.time >= nextArc && arcTargets.Count > 0)
            {
                nextArc = Time.time + arcInterval;

                Transform into = arcTargets[Random.Range(0, arcTargets.Count)];
                if (into != null)
                    Instantiate(arcPrefab).Strike(dart.Tip, into.position);
            }

            if (Time.time >= nextCrackle)
            {
                nextCrackle = Time.time + crackleInterval;
                Sfx.Play(shockSoundId, dart.Tip, GetInstanceID() + 2);
            }
        }

        private void StartReel()
        {
            if (current != null) current.Live = false;
            dart.Free();
            tailFrom = dart.Tail;
            BeginPhase(Phase.Reeling, ReelSeconds(tailFrom));
        }

        private void TickReel()
        {
            float t = Progress();
            Vector3 mouth = muzzle.position;
            Vector3 tail = Vector3.Lerp(tailFrom, mouth, t);

            // Still pointing away from the gun, so it comes home tail first, the way it went out.
            Vector3 away = tail - mouth;
            dart.PlaceTail(tail, away.sqrMagnitude > 1e-4f ? away : heading);
            wire.DrawFlight(mouth, tailFrom, 1f - t);

            if (t < 1f) return;

            Sfx.Play(reeledSoundId, mouth, GetInstanceID() + 3);
            EndShot();
        }

        /// <summary>Put the harpoon back in the gun, whatever it was doing.</summary>
        private void EndShot()
        {
            dart?.Destroy();
            dart = null;
            wire.Hide();
            if (current != null) current.Live = false;
            arcTargets.Clear();
            aimHost = null;
            victimStatus = null;
            phase = Phase.Loaded;
            if (loadedHarpoon != null) loadedHarpoon.gameObject.SetActive(true);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        private void BeginPhase(Phase next, float seconds)
        {
            // Out of the barrel's mouth, where the wire is paid out from, so the wire's end and the
            // dart's tail are the same point for the whole flight.
            if (next == Phase.Flying) tailFrom = muzzle.position;
            phase = next;
            phaseStart = Time.time;
            phaseLength = seconds;
        }

        private float Progress() =>
            phaseLength <= 0f ? 1f : Mathf.Clamp01((Time.time - phaseStart) / phaseLength);

        /// <summary>Where the dart is going: the hit point, carried along by whatever it was on.</summary>
        private Vector3 AimPoint() => aimHost != null ? aimHost.TransformPoint(aimLocal) : aimPoint;

        /// <summary>What an NPC's shot meets first along the wire, its own body and the gun skipped.</summary>
        private bool TryNpcAimHit(Ray aim, out RaycastHit hit)
        {
            int count = Physics.RaycastNonAlloc(aim, NpcHits, range, ~0, QueryTriggerInteraction.Ignore);
            Transform holder = owner != null ? owner.transform : transform;
            return AimProvider.NearestOutside(NpcHits, count, holder, AimProvider.RiddenBy(holder), out hit);
        }

        private static readonly RaycastHit[] NpcHits = new RaycastHit[16];

        private float FlightSeconds(Vector3 to) =>
            muzzle == null ? 0f : Vector3.Distance(muzzle.position, to) / Mathf.Max(flightSpeed, 0.1f);

        private float ReelSeconds(Vector3 from) =>
            muzzle == null ? 0f : Vector3.Distance(from, muzzle.position) / Mathf.Max(reelSpeed, 0.1f);

        /// <summary>
        /// The living thing <paramref name="hit"/> belongs to — the object carrying its health — or
        /// null for a wall, a rock, a dropped crate. Asked identically on every machine: the
        /// shooter passes the collider it hit, a peer the networked root it resolved, and both land
        /// on the same body.
        /// </summary>
        private static GameObject LivingBody(GameObject hit)
        {
            if (hit == null) return null;

            HealthComponent health = hit.GetComponentInParent<HealthComponent>();
            if (health == null) health = hit.GetComponentInChildren<HealthComponent>();
            return health != null ? health.gameObject : null;
        }

        /// <summary>
        /// The skeleton bone whose segment (the bone to its child) passes nearest the hit, so the dart
        /// rides the limb it went into when the body goes limp. Bones, not colliders: the standing
        /// body's root capsule encloses every hit point, and the root stays where the body stood
        /// while the rig falls — a dart pinned to it hangs in the air over a body on the floor.
        /// </summary>
        private static Transform NearestBone(Transform body, Vector3 point)
        {
            HashSet<Transform> bones = BodyFrame.BonesOf(body);
            Transform best = body;
            float bestDistance = float.MaxValue;

            foreach (Transform bone in bones)
            {
                Transform parent = bone.parent;
                if (parent == null || !bones.Contains(parent)) continue;

                Vector3 closest = ClosestOnSegment(parent.position, bone.position, point);
                float d = (closest - point).sqrMagnitude;
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = parent;
            }

            return best;
        }

        private static Vector3 ClosestOnSegment(Vector3 from, Vector3 to, Vector3 point)
        {
            Vector3 along = to - from;
            float lengthSq = along.sqrMagnitude;
            if (lengthSq < 1e-8f) return from;
            return from + along * Mathf.Clamp01(Vector3.Dot(point - from, along) / lengthSq);
        }

        /// <summary>Every bone of the body the current can jump to — the skeleton, which falls with it.</summary>
        private void CollectArcTargets(Transform host)
        {
            arcTargets.Clear();
            if (host == null) return;

            GameObject body = LivingBody(host.gameObject);
            if (body != null) arcTargets.AddRange(BodyFrame.BonesOf(body.transform));

            if (arcTargets.Count == 0) arcTargets.Add(host);
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public override void OnUnequipped(GameObject holder)
        {
            EndShot();
            authShockAt = float.PositiveInfinity;
            authVictim = null;
            base.OnUnequipped(holder);
        }

        private void OnDestroy() => dart?.Destroy();

        private void OnValidate()
        {
            range = Mathf.Max(1f, range);
            flightSpeed = Mathf.Max(1f, flightSpeed);
            reelSpeed = Mathf.Max(1f, reelSpeed);
            shockSeconds = Mathf.Max(0f, shockSeconds);
            shockGrace = Mathf.Max(0.1f, shockGrace);
            bodyHoldSeconds = Mathf.Max(shockGrace, bodyHoldSeconds);
            damage = Mathf.Max(0, damage);
            arcInterval = Mathf.Max(0.02f, arcInterval);
            crackleInterval = Mathf.Max(0.1f, crackleInterval);
        }
    }
}
