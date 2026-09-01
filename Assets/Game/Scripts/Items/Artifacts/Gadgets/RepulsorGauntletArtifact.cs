using System.Collections.Generic;
using FirstGearGames.SmoothCameraShaker;
using SpaceGame.Agents;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpaceGame.Items
{
    /// <summary>
    /// A thundergun: one press dumps a wall of compressed air out of the gauntlet at full power and
    /// ragdolls everything in a wide cone. There is no wind-up and nothing to hold — the blast
    /// resolves on the frame the button goes down.
    ///
    /// <para>
    /// That is a deliberate move along GDC-L1-FEEL-0008's responsiveness–commitment axis. The
    /// charge version put the cost in front of the shot (hold, then fire), which read as a wind-up
    /// the player had to survive and made every blast a weaker one than they had aimed. The cost
    /// now sits behind it instead: <see cref="shotCapacity"/> shots, one trickling back every
    /// <see cref="rechargeSeconds"/>. Scarcity is what prices the power once commitment no longer
    /// does (GDC-L1-ECON-0002) — the gauntlet is a panic button, not a primary, and the decision it
    /// asks for is *now or in seven seconds*, not *how long dare I hold*.
    /// </para>
    ///
    /// <para>
    /// Physics is server-authoritative (Authority=Server): loose bodies are pushed directly,
    /// players via NetMsg.Flung applied by their own machine (FlungBody), and everything with a
    /// skeleton is put on the ground by NetMsg.Knockdown, which every machine presents as a ragdoll
    /// of its own. The one exception is a creature carrying a rider — ragdolling under one would
    /// drag them through the ground — which is thrown as a leap via IMountLeapMotor instead.
    /// Cosmetics (cone, ring, dust, thunder, hurt flinches, recoil on the caster) run per machine
    /// in <see cref="Present"/>. A press the magazine refuses travels as <see cref="MissVerb"/> and
    /// presents nothing at all.
    /// </para>
    /// </summary>
    public class RepulsorGauntletArtifact : ToolItem
    {
        public override UseAuthority Authority => UseAuthority.Server;

        private const int MissVerb = 0;
        private const int FireVerb = 1;

        [Header("Ammo")]
        [Tooltip("Blasts held at once. Two is the whole panic-button economy: one to save yourself, " +
                 "one to be wrong about which way the trouble was coming from.")]
        [SerializeField] private int shotCapacity = 2;
        [Tooltip("Seconds to regain ONE shot. Long on purpose — this is the only cost the blast has " +
                 "now that it no longer has to be charged.")]
        [SerializeField] private float rechargeSeconds = 7f;
        [Tooltip("Minimum gap between two blasts, so a double-tap cannot dump the whole magazine " +
                 "into one frame and waste it on one crowd.")]
        [SerializeField] private float refireDelay = 0.45f;

        [Header("Blast")]
        [Tooltip("Blast reach in metres. Every hit inside it is a full-power hit; there is no charge to shorten it.")]
        [SerializeField] private float range = 20f;
        [Tooltip("Full cone angle, degrees. Wide enough to catch a crowd, narrow enough that the " +
                 "player can see where it went — past roughly 90 the wave has no direction left to " +
                 "read and the weapon starts feeling like a bomb you are standing on.")]
        [SerializeField, Range(10f, 180f)] private float blastAngle = 70f;
        [Tooltip("How far the push is turned from radial toward the AIM.\n\n" +
                 "0 throws every body straight away from the caster's chest, which is a detonation " +
                 "and is what this weapon used to be: the man beside you went sideways and the one " +
                 "behind your shoulder went backwards. 1 throws the whole cone down the crosshair " +
                 "and stacks the crowd into one pile. The default keeps a minority of radial so the " +
                 "fan still spreads, while the aim decides where the fan goes.")]
        [SerializeField, Range(0f, 1f)] private float aimBias = 0.8f;
        [Tooltip("Launch speed, point-blank. Read against a 9 m/s sprint and the 7 m/s jump: this is " +
                 "the number that decides whether a blast reads as a launch or a shove, so it is " +
                 "deliberately several times a player's own top speed.")]
        [SerializeField] private float flingSpeed = 48f;
        [Tooltip("Upward tilt of every fling, degrees. Load-bearing: vertical velocity is the half " +
                 "PlayerMovement never deletes, and while the victim is still RISING it holds on " +
                 "to the horizontal half too (PlayerMovement.ShouldEndCarry). Too small a tilt " +
                 "and the fling is deleted on the tick it is applied.")]
        [SerializeField] private float upwardTilt = 30f;
        [Tooltip("Blast origin height above the holder's feet.")]
        [SerializeField] private float blastOriginHeight = 1.2f;
        [Tooltip("Damage per body caught in the blast. 0 = pure force.")]
        [SerializeField] private int blastDamage = 0;
        [Tooltip("How long a victim stays on the ground before getting up, seconds.\n\n" +
                 "Travels with the blast rather than being each victim's own business, so every " +
                 "machine watching a knockdown agrees on when it ends — a watcher does not " +
                 "simulate the flight and cannot work the moment out for itself. This is the " +
                 "price of being caught, and it is the whole price: the blast does no damage.")]
        [SerializeField] private float downedSeconds = 1.2f;
        [Tooltip("Impulse scaling reference for loose items: a body this heavy takes the full fling speed.")]
        [SerializeField] private float itemMassReference = 18f;
        [Tooltip("Bounds on that mass scaling. The floor is what stops a crate from shrugging the " +
                 "blast off; the ceiling is what stops a tin can from leaving the chunk.")]
        [SerializeField] private Vector2 itemMassScaleRange = new Vector2(0.3f, 1.6f);
        [Tooltip("Fraction of the radius that takes UNDIMINISHED force. Without a core, falloff " +
                 "measured from the caster's chest makes the ordinary mid-cone hit a weak one no " +
                 "matter how high the peak speed is tuned.")]
        [SerializeField, Range(0f, 1f)] private float coreFraction = 0.55f;
        [Tooltip("Fling strength at the cone edge relative to the core — an edge hit is a puff, not a launch.")]
        [SerializeField, Range(0f, 1f)] private float edgeFalloff = 0.5f;

        [Header("Recoil (the caster)")]
        [Tooltip("Backward speed handed to the caster. Firing airborne is the repulsor-jump, and " +
                 "with no charge to modulate it that jump is now the same height every time — " +
                 "which is what makes it a movement tool you can plan around.")]
        [SerializeField] private float recoilSpeed = 26f;
        [Tooltip("Upward fraction mixed into the recoil direction. Load-bearing like upwardTilt: " +
                 "the vertical half is what PlayerMovement never deletes, and the rise it produces " +
                 "is what keeps the backward half from being deleted on the next movement tick.")]
        [SerializeField] private float recoilUpwardBias = 0.35f;

        [Header("Mount leap (creatures carrying a rider)")]
        [Tooltip("How far a RIDDEN creature is thrown, metres. Everything else goes limp instead; " +
                 "a mount with somebody on its back cannot, because the rider is parented to the " +
                 "seat and would be dragged through the ground with it. Read against flingSpeed " +
                 "rather than against a step — this is what the blast looks like on the one thing " +
                 "it cannot knock down.")]
        [SerializeField] private float leapDistance = 13f;
        [SerializeField] private float leapHeight = 3f;
        [SerializeField] private float leapDuration = 0.6f;

        [Header("Presentation")]
        [Tooltip("Ammo capacitor on the gauntlet: lit while a shot is loaded, dark while it recharges. " +
                 "This is the only readout the player gets for the magazine, so it is never merely " +
                 "decorative. Assigned by the builder.")]
        [FormerlySerializedAs("chargeGlow")]
        [SerializeField] private Transform capacitorGlow;
        // No FormerlySerializedAs on the scale: it used to be a Vector2 (min and max charge), and
        // Unity cannot carry a renamed field across a type change anyway — the authored value is
        // deliberately starting fresh at the lit size.
        [Tooltip("Uniform scale of the capacitor while it is lit.")]
        [SerializeField] private float capacitorGlowScale = 0.14f;
        [Tooltip("RepulsorShockwave-shader material for the ground ring. Assigned by the builder.")]
        [SerializeField] private Material ringMaterial;
        [SerializeField] private float ringDuration = 0.45f;
        [Tooltip("Material for the swept air cone — the shape of the blast, drawn where it actually " +
                 "reaches. Assigned by the builder.")]
        [SerializeField] private Material coneMaterial;
        [SerializeField] private float coneDuration = 0.42f;
        [Tooltip("Compressed-air dust wall leaving the gauntlet. Cosmetic; every machine plays it.")]
        [SerializeField] private ParticleSystem blastDust;
        [Tooltip("Streaked air lines down the cone axis — the direction the force went.")]
        [SerializeField] private ParticleSystem blastStreaks;
        [Tooltip("Grit and small debris torn off the ground by the blast.")]
        [SerializeField] private ParticleSystem blastDebris;
        [Tooltip("The ground wave: a wide, flat sheet of sand driven along the aim, hugging the " +
                 "floor. This is the system that makes the blast read as something that travelled " +
                 "OUT rather than something that happened at the hand.")]
        [SerializeField] private ParticleSystem blastShockSheet;
        [Tooltip("Muzzle flash at the emitter. Enabled by Present, cut by Update.")]
        [SerializeField] private Light muzzleFlash;
        [Tooltip("Seconds the muzzle flash stays lit.")]
        [SerializeField] private float flashSeconds = 0.13f;
        [SerializeField] private ShakeData blastShake;
        [Tooltip("Only cameras within this range of the blast shake.")]
        [SerializeField] private float shakeRadius = 20f;
        [Tooltip("Shake magnitude before distance attenuation. Flat, because the blast itself is " +
                 "flat now — every shot is a full-power shot, so a varying kick would be lying.")]
        [SerializeField] private float shakeMagnitude = 2.8f;
        [Tooltip("The crack of the thunderclap. Layered with blastId (GDC-L1-FEEL-0004).")]
        [SerializeField] private SfxId thunderId = SfxId.AmbThunder;
        [Tooltip("The body of the thunderclap, under the crack.")]
        [SerializeField] private SfxId blastId = SfxId.ImpactExplosion;
        [Tooltip("FOV punch on the caster, degrees. Deliberately large: this is doing the job " +
                 "GDC-L1-FEEL-0005 would give hitstop, which this codebase has ruled out on " +
                 "purpose (see SuckerPuncherArtifact) because Time.timeScale on a host stalls the " +
                 "authoritative simulation for every other player. The camera has to sell the " +
                 "impact on its own.")]
        [SerializeField] private float blastFovKick = 18f;
        [SerializeField] private float blastFovKickDuration = 0.26f;

        /// <summary>
        /// Shots in hand, the trickle that refills them, and the refire lock.
        ///
        /// <para>
        /// A gauntlet keeps TWO of these, which is the same split the cooldown it replaces had: the
        /// OWNER's copy gates the press (<see cref="OnRequestUse"/>, before the message leaves) and
        /// the AUTHORITY's copy gates the effect (<see cref="CanUse"/>, from TryUse). They cannot
        /// be one counter — on a host, EquipmentController presents the press BEFORE the request
        /// reaches the server, so a single counter would be spent by the presentation and the shot
        /// the player just watched would then be refused by its own authority.
        /// </para>
        /// </summary>
        private struct Magazine
        {
            public int Shots;
            public float RechargeProgress;
            public float NextFireTime;

            public readonly bool Ready => Shots > 0 && Time.time >= NextFireTime;

            public void Fill(int capacity)
            {
                Shots = capacity;
                RechargeProgress = 0f;
                NextFireTime = float.NegativeInfinity;
            }

            public void Spend(float refireDelay)
            {
                Shots = Mathf.Max(0, Shots - 1);
                NextFireTime = Time.time + refireDelay;
            }

            /// <summary>One shot back per <paramref name="rechargeSeconds"/>, never past capacity.</summary>
            public void Tick(float deltaTime, int capacity, float rechargeSeconds)
            {
                if (Shots >= capacity)
                {
                    RechargeProgress = 0f;
                    return;
                }

                RechargeProgress += deltaTime;
                while (RechargeProgress >= rechargeSeconds && Shots < capacity)
                {
                    RechargeProgress -= rechargeSeconds;
                    Shots++;
                }

                if (Shots >= capacity) RechargeProgress = 0f;
            }
        }

        private Magazine magazine;
        private Magazine authMagazine;

        private PlayerLook look;
        private float flashUntil = float.NegativeInfinity;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;

        /// <summary>
        /// Owner, before the press message leaves. The aim is captured here because this is the only
        /// machine whose crosshair is honest, and the verb is decided from the OWNER's magazine —
        /// a refused press still travels, so that every machine agrees it presented nothing.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            Ray aim = aimProvider != null
                ? aimProvider.GetAimRay()
                : new Ray(transform.position, transform.forward);

            arg.P = aim.origin;
            arg.R = Quaternion.LookRotation(aim.direction);
            arg.B = base.CanUse() && magazine.Ready ? FireVerb : MissVerb;
        }

        /// <summary>
        /// Authority, from TryUse. Reads the authority's own magazine and never the presentation
        /// one, which on a host has already been spent by <see cref="Present"/> this same frame.
        /// </summary>
        protected override bool CanUse() => base.CanUse() && authMagazine.Ready;

        /// <summary>Authority. The blast, at full power, on the frame of the press.</summary>
        protected override void Use()
        {
            if (UseArg.B != FireVerb || !UseArg.HasOrientation) return;

            authMagazine.Spend(refireDelay);
            FireBlast(UseArg.R * Vector3.forward);
        }

        /// <summary>Every machine. The whole thunderclap; recoil only on the caster's own machine.</summary>
        protected override void Present()
        {
            // No orientation means nobody filled the aim in — the default NetArg EquipmentController
            // sends on unequip/death, or a use with no aim provider. Blasting along a zero
            // quaternion would fire the gauntlet down the world's +Z from whoever is holding it.
            if (UseArg.B != FireVerb || !UseArg.HasOrientation) return;

            // Spent on every machine, not just the caster's: a peer's copy of this magazine drives
            // nothing but the capacitor on the gauntlet it can SEE, so a remote gauntlet dims when
            // it fires like the local one does.
            magazine.Spend(refireDelay);

            Vector3 dir = UseArg.R * Vector3.forward;
            PlayBlastFx(dir);
            if (OwnerIsLocal()) ApplyRecoil(dir);
        }

        private void FireBlast(Vector3 dir)
        {
            if (owner == null) return;
            Vector3 origin = owner.transform.position + Vector3.up * blastOriginHeight;
            // The gauntlet itself needs no exclusion of its own: while equipped it is parented into
            // the holder, so its root IS ownerRoot.
            GameObject ownerRoot = owner.transform.root.gameObject;
            var seen = new HashSet<GameObject>();

            foreach (Collider hit in Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject root = hit.transform.root.gameObject;
                if (root == ownerRoot || !seen.Add(root)) continue;

                Vector3 targetPos = hit.bounds.center;
                if (!RepulsorBlast.InCone(origin, dir, targetPos, range, blastAngle * 0.5f)) continue;

                // DirectedFling, not FlingVelocity: the plain one is pinned to aimBias 0, which
                // is right for a detonation centred on the point of contact (the rocket, the
                // punch) and wrong for a wall of air leaving a hand. This blast's origin is the
                // caster's own chest, so a radial push throws the cone in every direction it
                // happens to span — see RepulsorBlast.PushDirection.
                Vector3 fling = RepulsorBlast.DirectedFling(origin, dir, targetPos, range,
                    flingSpeed, upwardTilt, coreFraction, edgeFalloff, aimBias);

                // Three kinds of target, three routes — see BlastPush, which the Sucker Puncher
                // and the dragon bazooka's burst share. The knockdown hook is what makes this
                // gauntlet's version the rich one: a player takes the fling AND goes down, and a
                // creature that can be knocked down does that INSTEAD of leaping.
                //
                // The leap is priced off the fling this body would have taken had it been a loose
                // one, so a creature standing where a player would have been launched is thrown
                // the same distance rather than nudged. Proportional, so an edge hit fades out —
                // right for a wave centred on the caster's own chest.
                BlastPush.Apply(hit, root, fling, flingSpeed,
                                BlastPush.Leap.Proportional(leapDistance, leapHeight, leapDuration),
                                itemMassReference, itemMassScaleRange, Knock);

                if (blastDamage > 0)
                    NetDamage.Apply(root, blastDamage, owner.transform);
            }
        }

        /// <summary>
        /// Tell every machine to put this body on the ground.
        ///
        /// <para>
        /// Sent on the VICTIM's relay, like the damage and the fling: the message is about their
        /// state, and it is the attacker who happens to be sending it. <c>NetTo.All</c> because a
        /// ragdoll has to be presented everywhere — bone transforms do not replicate, so each
        /// machine runs its own and only the root converges.
        /// </para>
        /// </summary>
        private void Knock(GameObject victim, Vector3 fling)
        {
            NetMessaging.NetSendTo(victim, NetMsg.Knockdown, new NetArg
            {
                P = fling,
                A = Mathf.RoundToInt(downedSeconds * 1000f),
            }, NetTo.All);
        }

        private void PlayBlastFx(Vector3 dir)
        {
            if (owner == null) return;
            Vector3 feet = owner.transform.position + Vector3.up * 0.1f;
            Vector3 origin = owner.transform.position + Vector3.up * blastOriginHeight;

            // The ARC, not the full ring. A 360 ring under a weapon that just threw one cone of
            // people one way is the loudest thing in the frame telling the player it went
            // everywhere, and it contradicts what the physics did. Same half-angle as the sweep, so
            // the scorch covers exactly the bodies that were thrown.
            RepulsorBlastRing.SpawnArc(feet, dir, blastAngle * 0.5f, range, ringDuration, ringMaterial);
            RepulsorBlastCone.Spawn(origin, dir, range, blastAngle * 0.5f, coneDuration, coneMaterial);

            PlayBurst(blastDust, dir);
            PlayBurst(blastStreaks, dir);
            PlayBurst(blastDebris, dir);
            // Flattened onto the ground rather than fired along the aim: a wave that climbs with a
            // shot aimed slightly up stops looking like it is scouring the floor, which is the one
            // thing this system is here to say. A shot straight up or down has no ground direction
            // at all, and PlayBurst's own fallback leaves the sheet pointing wherever it last was —
            // so it is skipped outright there rather than fired sideways.
            Vector3 alongGround = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (alongGround.sqrMagnitude > 1e-4f) PlayBurst(blastShockSheet, alongGround);

            if (muzzleFlash != null)
            {
                muzzleFlash.enabled = true;
                flashUntil = Time.time + flashSeconds;
            }

            // Two sound layers on DIFFERENT rate-limiting sources. The catalog dedupes per
            // (id, sourceKey), so pointing both fields at the same event — which the catalog does
            // whenever an id has no dedicated FMOD slot — would have the second Play swallowed as a
            // repeat of the first, and the clap would collapse back to one thin layer
            // (GDC-L1-FEEL-0004). The component and its transform are two guaranteed-distinct ids.
            Sfx.Play(thunderId, origin, default, GetInstanceID());
            Sfx.Play(blastId, origin, default, transform.GetInstanceID());

            ShakeBlast(origin);

            // Cosmetic hurt flinch on agents in the cone — run per machine because animator
            // triggers do not replicate. Same cone math and exclusions as the authority sweep in
            // FireBlast, so the two provably agree.
            GameObject ownerRoot = owner.transform.root.gameObject;
            var seen = new HashSet<GameObject>();
            foreach (Collider hit in Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Ignore))
            {
                GameObject root = hit.transform.root.gameObject;
                if (root == ownerRoot || !seen.Add(root)) continue;
                if (!RepulsorBlast.InCone(origin, dir, hit.bounds.center, range, blastAngle * 0.5f)) continue;
                root.GetComponentInChildren<AgentAnimatorDriver>()?.TriggerHurt();
            }

            if (OwnerIsLocal() && look != null && blastFovKick > 0f)
            {
                look.SetFovOffset(blastFovKick);
                fovKickUntil = Time.time + blastFovKickDuration;
                fovKickArmed = true;
            }
        }

        /// <summary>
        /// Point a burst down the blast and fire it. The bursts are children of the gauntlet, so
        /// only their ROTATION is written — moving them into world space would leave them offset
        /// from the hand for every shot after the first.
        /// </summary>
        private static void PlayBurst(ParticleSystem burst, Vector3 dir)
        {
            if (burst == null) return;

            burst.transform.rotation = Quaternion.LookRotation(dir);
            burst.Play(true);
        }

        /// <summary>
        /// Camera kick, faded out with distance.
        ///
        /// <para>
        /// Distance attenuation is the dose GDC-L1-FEEL-0006 asks for — a blast at the far edge of
        /// shakeRadius should register, not punch the camera as hard as one at the caster's feet.
        /// The magnitude no longer scales with anything else, because there is nothing else left to
        /// scale with: every blast is the same blast.
        /// </para>
        /// </summary>
        private void ShakeBlast(Vector3 origin)
        {
            if (blastShake == null || Camera.main == null) return;

            float distance = Vector3.Distance(Camera.main.transform.position, origin);
            if (distance >= shakeRadius) return;

            float magnitude = shakeMagnitude * (1f - distance / Mathf.Max(shakeRadius, 0.01f));

            // A null instance means no CameraShaker is live in the scene — nothing to scale.
            ShakerInstance instance = CameraShakerHandler.Shake(blastShake);
            instance?.MultiplyMagnitude(magnitude, 0f); // 0 rate = applied on the first frame
        }

        private void ApplyRecoil(Vector3 dir)
        {
            var movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            var body = owner != null ? owner.GetComponent<Rigidbody>() : null;
            if (movement == null || body == null) return;

            Vector3 back = (-Vector3.ProjectOnPlane(dir, Vector3.up).normalized + Vector3.up * recoilUpwardBias).normalized;
            movement.EnsureMovableBody();
            if (body.isKinematic) return;
            body.linearVelocity += back * recoilSpeed;
            movement.CarryMomentum();
        }

        private void Update()
        {
            float delta = Time.deltaTime;
            magazine.Tick(delta, shotCapacity, rechargeSeconds);
            authMagazine.Tick(delta, shotCapacity, rechargeSeconds);
            UpdateCapacitor();

            if (muzzleFlash != null && muzzleFlash.enabled && Time.time >= flashUntil)
                muzzleFlash.enabled = false;

            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                fovKickArmed = false;
                if (look != null) look.SetFovOffset(0f);
            }
        }

        /// <summary>The capacitor IS the ammo readout: lit while a shot is loaded, dark while it refills.</summary>
        private void UpdateCapacitor()
        {
            if (capacitorGlow == null) return;

            bool loaded = magazine.Shots > 0;
            if (capacitorGlow.gameObject.activeSelf != loaded)
                capacitorGlow.gameObject.SetActive(loaded);
            if (loaded) capacitorGlow.localScale = Vector3.one * capacitorGlowScale;
        }

        // ── The magazine, across instances ─────────────────────────────────────
        //
        // The held object is a fresh Instantiate of the prefab, destroyed the moment the player
        // scrolls to the next hotbar slot — so a magazine living on the instance is a magazine you
        // refill by scrolling down and back up. It belongs in the slot, like the charge count.

        /// <summary>State keys for the magazine. Written into save files — never rename.</summary>
        private const string ShotsKey = "repulsorShots";
        private const string RechargeKey = "repulsorRecharge";

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // A full magazine is the default, and writing it out would put a bag on every slot that
            // has nothing worth remembering.
            if (magazine.Shots >= shotCapacity) return;

            state.Set(ShotsKey, magazine.Shots);
            if (magazine.RechargeProgress > 0.01f) state.Set(RechargeKey, magazine.RechargeProgress);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            magazine.Shots = Mathf.Clamp(state == null ? shotCapacity : state.GetInt(ShotsKey, shotCapacity),
                                         0, shotCapacity);
            magazine.RechargeProgress = state == null ? 0f : state.GetFloat(RechargeKey, 0f);
            // Seconds remaining, never a deadline: Time.time restarts at zero each session, so a
            // stored refire lock comes back either already spent or hours away. It is sub-second
            // anyway — nothing worth carrying across an unequip.
            magazine.NextFireTime = float.NegativeInfinity;

            authMagazine = magazine;
            UpdateCapacitor();
        }

        /// <summary>
        /// A gauntlet lying in the sand is the same prefab as the one in a hand, and Update runs on
        /// it either way — without this its capacitor starts dark and lights itself seven seconds
        /// later, which reads as the pickup doing something.
        /// </summary>
        private void Awake() => LoadMagazines();

        private void LoadMagazines()
        {
            magazine.Fill(shotCapacity);
            authMagazine.Fill(shotCapacity);
            UpdateCapacitor();
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            look = holder != null ? holder.GetComponent<PlayerLook>() : null;

            // RestoreItemState runs straight after this and overwrites it whenever the slot
            // remembers a spent magazine; a freshly picked-up gauntlet keeps the full one.
            LoadMagazines();
        }

        public override void OnUnequipped(GameObject holder)
        {
            ClearFlash();
            fovKickArmed = false;
            if (look != null) look.SetFovOffset(0f);
            look = null;
            base.OnUnequipped(holder);
        }

        private void OnDisable() => ClearFlash();

        /// <summary>Unequipping mid-flash would otherwise strand the light on for the next equip.</summary>
        private void ClearFlash()
        {
            if (muzzleFlash != null) muzzleFlash.enabled = false;
            flashUntil = float.NegativeInfinity;
        }

        private void OnValidate()
        {
            shotCapacity = Mathf.Max(1, shotCapacity);
            rechargeSeconds = Mathf.Max(0.1f, rechargeSeconds); // 0 would spin Magazine.Tick's refill loop
            refireDelay = Mathf.Max(0f, refireDelay);
            range = Mathf.Max(0.1f, range);
            flingSpeed = Mathf.Max(0f, flingSpeed);
            shakeMagnitude = Mathf.Max(0f, shakeMagnitude);
            itemMassScaleRange.x = Mathf.Max(0.01f, itemMassScaleRange.x);
            itemMassScaleRange.y = Mathf.Max(itemMassScaleRange.y, itemMassScaleRange.x);
        }
    }
}
