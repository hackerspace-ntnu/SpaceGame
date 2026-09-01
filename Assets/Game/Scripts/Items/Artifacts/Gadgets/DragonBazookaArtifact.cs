using FirstGearGames.SmoothCameraShaker;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// A recoilless launch tube with a temple dragon cast over its muzzle. Pull the trigger and it
    /// spits a festival rocket out through the dragon's teeth — a lacquered firework that
    /// corkscrews and loops its way downrange trailing red smoke and fire, then bursts into a
    /// brood of smaller rockets that scatter from the impact.
    ///
    /// <para>
    /// <b>The wander is the item.</b> The rocket does not fly where it is pointed; it fly-swerves
    /// AROUND where it is pointed, because summed sinusoids average to zero and the aim ray is
    /// the mean of the path (<see cref="DragonRocketFlight"/>). That distinction is the whole
    /// design. A shot with no relationship to the crosshair would be spectacle the player cannot
    /// author, and every hit would read as luck rather than as aim — the point at which agency
    /// stops being felt at all (GDC-L1-DESIGN-0006). Anchoring the mean to the aim keeps the
    /// contract legible: point it at the thing, and it gets there, eventually, having taken a
    /// route of its own choosing. The 0.35 s straight run out of the muzzle
    /// (<c>settleSeconds</c>) exists for the same reason — it is the beat where the player can
    /// see their aim honoured before the rocket starts misbehaving.
    /// </para>
    /// <para>
    /// <b>Five charges and no refill</b> (<c>maxUses</c>). The launcher is a treasure, not a
    /// sidearm, and scarcity is what pays for a weapon this loud: unlimited chaos at a cooldown
    /// would make every other option a worse version of this one (GDC-L1-BAL-0002). The charge
    /// count already rides <c>ItemState</c> on the hotbar slot, so it survives a scroll, a drop
    /// and a save without anything being added here.
    /// </para>
    /// <para>
    /// <b>Networking.</b> The owner rolls one seed into the use message and every machine flies
    /// the identical rocket from it — see <see cref="DragonRocket"/>. The AUTHORITY's copy is the
    /// only one that may hurt anything, so it is created in <see cref="Use"/>; every other
    /// machine makes a cosmetic twin in <see cref="Present"/>, immediately, so the owner never
    /// waits a round trip to watch their own shot. The host would otherwise make both — it runs
    /// Present first and Use second — which is what the <c>Network.Simulates</c> guard in
    /// <see cref="Present"/> is for.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> nothing here is worth saving, and that is a decision rather than an
    /// oversight. The only runtime state is the sub-second refire gate and the jaw's animation
    /// angle, and both SHOULD come back reset — a launcher restored mid-roar, or still cooling
    /// from a shot fired before a quit, would be a bug. Charges, ownership and hotbar slot are
    /// carried by <c>UsableItem</c>/<c>PickupableItem</c>/<c>SaveableEntity</c> like every
    /// artifact.
    /// </para>
    /// </summary>
    public class DragonBazookaArtifact : ToolItem
    {
        /// <summary>
        /// Server. The rocket damages, flings and spawns a brood — all shared world state, so
        /// exactly one machine may decide any of it.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Rocket")]
        [Tooltip("The projectile. Instantiated LOCALLY by every machine, so it must NOT be in the " +
                 "network prefab list — see DragonRocket.")]
        [SerializeField] private DragonRocket rocketPrefab;

        [Tooltip("Where the rocket leaves the dragon's teeth. Placed by DragonBazookaBuilder.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Seconds before it can be fired again. Short — the real cost of this weapon is " +
                 "that it only has five rockets in it.")]
        [SerializeField] private float refireDelay = 0.9f;

        [Header("The roar")]
        [Tooltip("The dragon's lower jaw. Its pivot is on the hinge axis in the FBX, so a local " +
                 "X rotation is the whole animation.")]
        [SerializeField] private Transform jaw;

        [Tooltip("Degrees the jaw snaps open by when it fires, on top of its modelled gape.")]
        [SerializeField] private float jawOpenAngle = 22f;

        [Tooltip("Seconds the jaw takes to fall shut again.")]
        [SerializeField] private float jawCloseSeconds = 0.45f;

        [Header("Muzzle effects")]
        [Tooltip("Fire out of the mouth.")]
        [SerializeField] private ParticleSystem muzzleFire;

        [Tooltip("Red smoke off the muzzle, matching the trail the rocket lays down.")]
        [SerializeField] private ParticleSystem muzzleSmoke;

        [Tooltip("Backblast out of the venturi. A recoilless tube vents behind you, and it is " +
                 "most of what sells the weapon as one.")]
        [SerializeField] private ParticleSystem backblast;

        [Tooltip("Muzzle flash. Lit by Present, cut by Update.")]
        [SerializeField] private Light muzzleFlash;

        [SerializeField] private float flashSeconds = 0.09f;

        [Header("Recoil")]
        [Tooltip("Backward speed handed to the shooter. Small on purpose: this is a recoilless " +
                 "launcher, and a shove big enough to rocket-jump with would hand the item a " +
                 "second job the Sucker Puncher already does better (GDC-L1-BAL-0002).")]
        [SerializeField] private float recoilSpeed = 4.5f;

        [SerializeField] private float recoilUpwardTilt = 18f;

        [Tooltip("Camera shake on firing.")]
        [SerializeField] private ShakeData fireShake;

        [Tooltip("Degrees of FOV kick on the shooter's own view.")]
        [SerializeField] private float fovKick = 7f;

        [SerializeField] private float fovKickDuration = 0.22f;

        /// <summary>
        /// Did this use actually carry an aim, or is it a default struct?
        ///
        /// A magnitude test rather than <c>arg.R == default</c>, which silently never fires:
        /// Unity's Quaternion equality compares ORIENTATIONS through a dot product, and the dot
        /// of anything with the all-zero quaternion is 0, so the comparison reads false even for
        /// exactly the value it is looking for. A real rotation is unit length and an un-filled
        /// one is all zeroes, and those two are trivially told apart.
        /// </summary>
        private static bool HasAim(in NetArg arg)
        {
            Quaternion r = arg.R;
            return r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w > 0.5f;
        }

        private float authCooldownUntil = float.NegativeInfinity;
        private float cooldownUntil = float.NegativeInfinity;
        private float flashUntil = float.NegativeInfinity;
        private float jawOpenUntil = float.NegativeInfinity;
        private float fovKickUntil = float.NegativeInfinity;
        private bool fovKickArmed;
        private Quaternion jawRest = Quaternion.identity;
        private PlayerLook look;

        /// <summary>
        /// Owner-side, before the request leaves — the only machine whose aim is honest. The seed
        /// travels with it: every machine has to fly the SAME erratic rocket, and a re-roll per
        /// machine would have the server billing an explosion nobody watched.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            if (aimProvider == null) return;

            Ray ray = aimProvider.GetAimRay();

            // The aim RAY, not the point it lands on. The rocket wanders around this direction
            // for its whole flight, so a hit position would describe only where it might have
            // ended up if it had flown straight — which is the one thing this rocket never does.
            arg.P = ray.origin;
            arg.R = Quaternion.LookRotation(ray.direction);
            arg.B = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// Authority only: the rocket that is allowed to hurt people.
        ///
        /// It launches from the owner's aim ORIGIN rather than from this machine's copy of the
        /// muzzle transform, so the authoritative flight and the cosmetic ones are the same curve
        /// — a peer's copy of a remote player holds their launcher at a pose that lags the wire.
        /// </summary>
        protected override void Use()
        {
            if (rocketPrefab == null || !HasAim(UseArg) || owner == null) return;
            if (Time.time < authCooldownUntil) return;
            authCooldownUntil = Time.time + refireDelay;

            Launch(cosmetic: false);
        }

        /// <summary>
        /// Every machine, and immediately on the shooter's so the launcher never feels like it is
        /// waiting for a reply. Layered on purpose — jaw, fire, smoke, backblast, flash, sound,
        /// shake and FOV all land on the same frame (GDC-L1-FEEL-0004); any one of them alone
        /// reads as a bug rather than as a launch.
        /// </summary>
        protected override void Present()
        {
            if (!HasAim(UseArg) || owner == null) return;
            if (Time.time < cooldownUntil) return;
            cooldownUntil = Time.time + refireDelay;

            // The authority already made the real rocket. On a host both halves run — Present
            // first, Use second — so without this the host puts two rockets in the air, and the
            // spare one is the one that does no damage and never lines up.
            //
            // Asked of the OWNER, not of this item, and the distinction is load-bearing. An
            // equipped artifact is instantiated into a hand and never spawned, so its own
            // NetworkObject is dormant and `Network.Simulates(this)` answers "yes, you simulate
            // it" on EVERY machine in the session — which would suppress the cosmetic rocket
            // everywhere and leave clients watching a launcher that fires nothing at all. The
            // owner's spawned NetworkObject is the one that can actually tell, and it is the
            // same object EquipmentController tests before it runs Use. Same trap OwnerIsLocal
            // documents, reached from the other direction.
            if (!Network.Simulates(owner.transform)) Launch(cosmetic: true);

            if (muzzleFire != null) muzzleFire.Play(withChildren: true);
            if (muzzleSmoke != null) muzzleSmoke.Play(withChildren: true);
            if (backblast != null) backblast.Play(withChildren: true);

            if (muzzleFlash != null)
            {
                muzzleFlash.enabled = true;
                flashUntil = Time.time + flashSeconds;
            }

            jawOpenUntil = Time.time + jawCloseSeconds;

            if (!OwnerIsLocal()) return;

            if (fireShake != null) CameraShakerHandler.Shake(fireShake);
            ApplyRecoil(UseArg.R * Vector3.forward);

            if (look != null && fovKick > 0f)
            {
                look.SetFovOffset(fovKick);
                fovKickUntil = Time.time + fovKickDuration;
                fovKickArmed = true;
            }
        }

        /// <summary>
        /// Put a rocket in the air. One code path for both halves, so the authoritative shot and
        /// the cosmetic ones cannot be described two different ways.
        /// </summary>
        private void Launch(bool cosmetic)
        {
            Quaternion along = UseArg.R;
            Vector3 from = UseArg.P + along * Vector3.forward * MuzzleLead();

            DragonRocket rocket = Instantiate(rocketPrefab, from, along);
            rocket.Launch(owner, from, along, UseArg.B, cosmetic);
        }

        /// <summary>
        /// How far down the aim ray the rocket starts.
        ///
        /// Measured off the actual muzzle where there is one, so the rocket appears to leave the
        /// dragon's mouth rather than the player's eye. It is a DISTANCE rather than the muzzle's
        /// own position because the aim ray starts at the camera: pushing along the ray keeps the
        /// shot on the line the player drew, where launching from the muzzle transform would put
        /// it half a metre to one side and make close-range shots miss what the crosshair covers.
        /// </summary>
        private float MuzzleLead()
        {
            if (muzzle == null || owner == null) return 1.6f;
            return Mathf.Max(0.5f, Vector3.Distance(owner.transform.position, muzzle.position));
        }

        private void ApplyRecoil(Vector3 dir)
        {
            var movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            var body = owner != null ? owner.GetComponent<Rigidbody>() : null;
            if (movement == null || body == null || recoilSpeed <= 0f) return;

            movement.EnsureMovableBody();
            if (body.isKinematic) return;

            body.linearVelocity += RepulsorBlast.Launch(-dir, recoilUpwardTilt, recoilSpeed);
        }

        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            look = holder != null ? holder.GetComponentInChildren<PlayerLook>() : null;
            if (jaw != null) jawRest = jaw.localRotation;
        }

        public override void OnUnequipped(GameObject holder)
        {
            // Cut the shooter's FOV kick before the item goes away, or a launcher swapped out
            // mid-shot leaves the view zoomed with nothing left to un-zoom it.
            if (fovKickArmed && look != null) look.SetFovOffset(0f);
            fovKickArmed = false;

            base.OnUnequipped(holder);
        }

        private void Update()
        {
            if (muzzleFlash != null && muzzleFlash.enabled && Time.time >= flashUntil)
                muzzleFlash.enabled = false;

            if (fovKickArmed && Time.time >= fovKickUntil)
            {
                if (look != null) look.SetFovOffset(0f);
                fovKickArmed = false;
            }

            AnimateJaw();
        }

        /// <summary>
        /// The roar: the jaw snaps open on the shot and falls shut over
        /// <see cref="jawCloseSeconds"/>.
        ///
        /// Driven per machine off the presented use rather than replicated, because it is
        /// cosmetic and because bone and transform rotations on a held item do not travel — the
        /// same reason the muzzle flash is presented rather than sent.
        /// </summary>
        private void AnimateJaw()
        {
            if (jaw == null || jawCloseSeconds <= 0f) return;

            float remaining = jawOpenUntil - Time.time;
            if (remaining <= 0f)
            {
                jaw.localRotation = jawRest;
                return;
            }

            // Snap open, ease shut: a jaw that opens as slowly as it closes reads as a yawn.
            float t = Mathf.Clamp01(remaining / jawCloseSeconds);
            jaw.localRotation = jawRest * Quaternion.Euler(jawOpenAngle * t * t, 0f, 0f);
        }

        private void OnDisable()
        {
            // Unequipping mid-flash would otherwise leave the light on for the next equip.
            if (muzzleFlash != null) muzzleFlash.enabled = false;
            if (jaw != null) jaw.localRotation = jawRest;
        }

        private void OnValidate()
        {
            refireDelay = Mathf.Max(0f, refireDelay);
            jawCloseSeconds = Mathf.Max(0f, jawCloseSeconds);
            flashSeconds = Mathf.Max(0f, flashSeconds);
            recoilSpeed = Mathf.Max(0f, recoilSpeed);
        }
    }
}
