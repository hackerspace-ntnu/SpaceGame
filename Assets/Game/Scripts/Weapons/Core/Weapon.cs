using UnityEngine;
using System;
using FMODUnity;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Presentation;

namespace SpaceGame.Weapons
{
    /// <summary>
    /// Abstract base class for all weapon types.
    /// Extends UsableItem to integrate with inventory system.
    /// Handles ammo management, firing mechanics, and firing input.
    /// Subclasses define specific projectile/hit behavior.
    /// </summary>
    public abstract class Weapon : UsableItem
    {
        [Header("Weapon Configuration")]
        [SerializeField] protected Camera aimCamera;
        [SerializeField] protected Transform firePoint;
        [SerializeField] protected Transform handle1; // Primary grip point (main hand attachment)
        [SerializeField] protected Transform handle2; // Secondary grip point (support hand - for future use)
        [SerializeField] protected float fireRate = 1f; // Shots per second
        [SerializeField] protected float spawnOffset = 0.5f;
        [SerializeField] protected LayerMask aimMask = ~0;

        [Tooltip("How far the aim reaches for something to converge on, in metres. A shot at open " +
                 "sky is aimed at this distance, which is also the far point the replicated " +
                 "orientation is turned back into a target with.")]
        [SerializeField] protected float aimRange = 500f;

        [Header("Ammo")]
        [SerializeField] private Magazine magazine;
        [SerializeField] protected int ammoPerShot = 1;

        // These were plain strings fed to AudioManager.PlaySFX3d, which resolves an FMOD path at call
        // time and throws EventNotFoundException on a typo — a weapon could take the game down by
        // being fired. They also required an AudioManager in the scene, which is why every weapon
        // ran FindObjectOfType on enable. SfxId is checked by the compiler and needs no manager.
        [Header("Audio")]
        [SerializeField] protected SfxId fireSoundId = SfxId.WeaponGunFire;
        [SerializeField] protected EventReference fireSound;
        [SerializeField] protected SfxId chargeStartSoundId = SfxId.WeaponEnergyChargeLoop;
        [SerializeField] protected EventReference chargeStartSound;

        [Header("Charging")]
        [SerializeField] protected bool enableCharging = false; // Toggle charging mode on/off
        [SerializeField] protected float chargeDuration = 3f; // Time to fully charge in seconds
        [SerializeField] protected AnimationCurve chargeProgressCurve = AnimationCurve.Linear(0, 0, 1, 1); // Curve for charge progression

        protected float nextFireTime;
        protected bool canFire = true;
        protected bool isCharging = false;
        protected float chargeStartTime;
        protected IChargeable chargedProjectile; // Reference to currently charging projectile

        public event Action<int> OnAmmoChanged;

        public Magazine Magazine => magazine;
        public int CurrentAmmo => magazine != null ? magazine.CurrentAmmo : 0;
        public int MaxAmmo => magazine != null ? magazine.MaxAmmo : 0;
        public float FireRatePercent => Mathf.Clamp01((Time.time - (nextFireTime - 1f / Mathf.Max(0.01f, fireRate))) / (1f / Mathf.Max(0.01f, fireRate)));
        public bool IsReadyToFire => Time.time >= nextFireTime;
        public Transform Handle1 => handle1; // Primary grip point
        public Transform Handle2 => handle2; // Secondary grip point

        /// <summary>
        /// Someone other than a local player is pointing this weapon, so leave its rotation alone.
        ///
        /// <para>
        /// Set by <see cref="SpaceGame.Agents.EntityEquipmentController"/> when an NPC picks a
        /// weapon up. Without it an NPC's gun is aimed by <see cref="UpdateWeaponRotation"/>, whose
        /// ownership test passes on the server for every server-owned entity and whose camera is
        /// then <c>Camera.main</c> — the host's. The result is every NPC in the world swinging its
        /// barrel to follow the host's head, which looks like the NPCs are watching you and is
        /// nothing of the kind.
        /// </para>
        /// <para>
        /// Not serialized: it is a fact about who is holding the weapon right now, decided when it
        /// is equipped, and a prefab has no business having an opinion about it.
        /// </para>
        /// </summary>
        [System.NonSerialized] public bool ExternallyAimed;

        protected virtual void OnEnable()
        {
            // Auto-find camera if not assigned
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            // Ensure magazine exists
            if (magazine == null)
            {
                magazine = GetComponent<Magazine>();
            }

            if (magazine == null)
            {
                magazine = gameObject.AddComponent<Magazine>();
            }

            // Refill magazine when weapon is equipped/enabled
            if (magazine != null)
            {
                magazine.Refill();
            }

            // Subscribe to magazine changes
            if (magazine != null)
            {
                magazine.OnAmmoChanged += OnMagazineAmmoChanged;
            }

            // Reset firing state
            nextFireTime = Time.time;
            canFire = true;

            // Warn if handles aren't set (they're optional for now, but should be configured)
            if (handle1 == null)
            {
                Debug.LogWarning($"Weapon '{gameObject.name}' has no Handle1 assigned. Set this in the inspector to a child transform for proper hand attachment.", this);
            }
        }

        protected virtual void OnDisable()
        {
            if (magazine != null)
            {
                magazine.OnAmmoChanged -= OnMagazineAmmoChanged;
            }

            // Cancel charging when weapon is disabled
            CancelCharging();
        }

        protected virtual void Update()
        {
            // Update charging if active
            if (isCharging && chargedProjectile != null)
            {
                float chargeElapsed = Time.time - chargeStartTime;
                float chargeProgress = Mathf.Clamp01(chargeElapsed / chargeDuration);
            
                // Apply animation curve
                chargeProgress = chargeProgressCurve.Evaluate(chargeProgress);
            
                // Double-check projectile hasn't been destroyed
                if (chargedProjectile == null)
                {
                    isCharging = false;
                    return;
                }
            
                // Update projectile with charge progress
                chargedProjectile.UpdateCharge(chargeProgress);
            }

            // Rotate weapon to match camera pitch (up/down look direction)
            UpdateWeaponRotation();
        }

        /// <summary>
        /// Rotate weapon to point in the direction its holder is looking (pitch and yaw).
        ///
        /// <para>
        /// Two sources, because the answer lives in two different places. The machine that OWNS the
        /// holder reads its own camera, which is live and exact. Every other machine reads that
        /// player's <see cref="PlayerViewNetwork.AimPivot"/>, which carries their replicated pitch
        /// on top of the body yaw that already comes down the transform.
        /// </para>
        /// <para>
        /// Camera.main is emphatically not a fallback for the remote case: it is whatever camera
        /// THIS machine has active, which is the local player's — so reading it for somebody else's
        /// gun swung every weapon in the game to follow the local head. That is why this used to
        /// leave a remote weapon on its hand bone and do nothing. The cost of doing nothing was
        /// that a player aiming up or down looked, to everyone else, like they were aiming level.
        /// </para>
        /// </summary>
        protected virtual void UpdateWeaponRotation()
        {
            // Somebody else is aiming this — an NPC's equipment controller, a turret mount. Checked
            // first, because an NPC's weapon IS owned by the machine simulating it and would
            // otherwise pass the test below and swing to that machine's camera.
            if (ExternallyAimed)
            {
                return;
            }

            // The holder's arm is doing the aiming, so the item must not also aim itself. Two
            // things pointing the same weapon do not agree: this method writes a WORLD rotation
            // about the item's own pivot, which walks the grip out of the palm — it never calls
            // ReseatGrip, unlike the NPC path — and with the arm now in shot that shows.
            //
            // Checked on the holder rather than on a flag we set, so it stays true for a weapon
            // that changes hands. A holder with no rig (a dropped weapon, a test rig, an NPC)
            // keeps exactly the behaviour it had.
            if (owner != null && owner.GetComponent<PlayerAimRig>() != null)
            {
                return;
            }

            if (!Network.Owns(this))
            {
                AimAlongReplicatedView();
                return;
            }

            // The aim ray, not a camera's forward: the two are the same thing on foot and are not
            // while the holder is riding anything. See GetLocalAimPoint.
            if (aimProvider != null && aimProvider.AimTransform != null)
            {
                transform.rotation = Quaternion.LookRotation(aimProvider.GetAimRay().direction);
                return;
            }

            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            if (aimCamera == null)
            {
                return;
            }

            // Create a rotation that points toward the camera's forward direction
            // This includes both pitch (up/down) and yaw (left/right)
            transform.rotation = Quaternion.LookRotation(aimCamera.transform.forward,
                                                         aimCamera.transform.up);
        }

        /// <summary>
        /// A weapon in somebody else's hands: point it where they say they are pointing it.
        ///
        /// <para>
        /// Silently does nothing when the holder has no replicated view — an NPC, a weapon lying on
        /// a table, a test rig — which leaves it on its hand bone exactly as before. That is the
        /// right fallback: the alternative for a holder whose aim is unknown is to invent one.
        /// </para>
        /// </summary>
        // Cached against the holder it was looked up from, because this runs every frame for every
        // remote weapon in the session and GetComponent is not free. Re-resolved whenever the item
        // changes hands, which is the only thing that can invalidate it.
        private GameObject viewOwner;
        private PlayerViewNetwork ownerView;

        private void AimAlongReplicatedView()
        {
            if (owner == null) return;

            if (!ReferenceEquals(viewOwner, owner))
            {
                viewOwner = owner;
                ownerView = owner.GetComponent<PlayerViewNetwork>();
            }

            Transform aim = ownerView != null ? ownerView.AimPivot : null;
            if (aim == null) return;

            transform.rotation = Quaternion.LookRotation(aim.forward, aim.up);
        }

        /// <summary>
        /// Attempt to fire the weapon.
        /// If charging is enabled and no projectile is charging, spawn and start charging.
        /// If a projectile is already charging, fire it.
        /// Returns false if weapon can't fire (no ammo, fire rate not ready, etc).
        /// </summary>
        public bool TryFire()
        {
            if (!canFire || !IsReadyToFire)
            {
                return false;
            }

            // If charging is enabled and we're already charging, launch the charged projectile
            if (enableCharging && isCharging)
            {
                if (chargedProjectile != null)
                {
                    try
                    {
                        // Tell the projectile to finish charging and be ready to move
                        chargedProjectile.OnChargeComplete();
                    
                        // Launch the already-charged projectile with current aim direction
                        Fire();
                    }
                    catch (MissingReferenceException)
                    {
                        Debug.LogWarning("Charged projectile was destroyed before launch.");
                    }
                }
            
                chargedProjectile = null;
                isCharging = false;
                nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
                return true;
            }

            // Normal fire or start charging
            if (magazine == null || !magazine.ConsumeAmmo(ammoPerShot))
            {
                // No ammo
                return false;
            }

            // If charging is enabled, start charging (spawns projectile)
            if (enableCharging && !isCharging)
            {
                StartCharging();
                return true;
            }
            else
            {
                // Normal firing (no charging)
                Fire();
                nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
                return true;
            }
        }

        /// <summary>
        /// Start the charging sequence for a chargeable projectile.
        /// Spawns the projectile and begins charging it.
        /// Subclasses override SpawnChargeProjectile() to create the projectile.
        /// </summary>
        protected virtual void StartCharging()
        {
            isCharging = true;
            chargeStartTime = Time.time;
            SpawnChargeProjectile(); // Spawn the projectile for charging
        
            // Play charge start sound
            PlayChargeStartSound();
        }

        /// <summary>
        /// Spawn a projectile for charging. Override in subclasses.
        /// Should set chargedProjectile to the spawned projectile.
        /// </summary>
        protected virtual void SpawnChargeProjectile()
        {
            // Subclasses override this to spawn the chargeable projectile
        }

        /// <summary>
        /// Calculate where the weapon is aiming (center screen or raycast point).
        /// </summary>
        protected virtual Vector3 GetAimPoint()
        {
            // What the owner reported wins over anything this machine can work out for itself. On
            // the server — which is where an item's Use() runs — the local camera belongs to the
            // host, so a client's shot used to travel along the host's crosshair.
            if (UseArg.HasOrientation)
            {
                return GetSpawnPosition() + UseArg.R * Vector3.forward * aimRange;
            }

            return GetLocalAimPoint();
        }

        /// <summary>
        /// Where THIS machine's camera is pointing, ignoring anything a previous use reported.
        ///
        /// Split out from <see cref="GetAimPoint"/> because the owner has to be able to ask the
        /// question afresh: <see cref="OnRequestUse"/> runs before the new UseArg is assigned, so a
        /// version that consulted UseArg first answered with the aim of the PREVIOUS shot and every
        /// bullet after the first went where the last one did.
        /// </summary>
        protected virtual Vector3 GetLocalAimPoint()
        {
            // The holder's own aim, and only the holder's. It is the one thing that knows which
            // camera this player is actually looking through — riding anything, that is NOT the eye
            // on their head, and the mount's orbit camera is deliberately left Untagged so the
            // Camera.main fallback below cannot find it either — and it looks past the player's own
            // body and the machine they are strapped into on the way out.
            if (aimProvider != null && aimProvider.AimTransform != null)
            {
                return aimProvider.TryGetAimHit(aimRange, aimMask, out RaycastHit aimed)
                    ? aimed.point
                    : aimProvider.GetAimRay().GetPoint(aimRange);
            }

            // Nothing holding it that has a view: a weapon on a rack, an NPC, a test rig. Only a
            // player carries an AimProvider, so a holder without one keeps exactly the behaviour it
            // had — which for an NPC is also the wrong camera, and is why the agent's own combat
            // module points its barrel instead (see ExternallyAimed).
            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            if (aimCamera != null)
            {
                Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

                return Physics.Raycast(ray, out RaycastHit hit, aimRange, aimMask,
                                       QueryTriggerInteraction.Ignore)
                    ? hit.point
                    : ray.GetPoint(aimRange);
            }

            return transform.position + transform.forward * aimRange;
        }

        /// <summary>
        /// Get the direction the projectile should fire in.
        ///
        /// Every subclass routes through here, which is deliberate: fixing the aim in the base class
        /// fixes every weapon written against it, including the ones nobody has written yet.
        /// </summary>
        protected virtual Vector3 GetFireDirection()
        {
            if (UseArg.HasOrientation)
            {
                return UseArg.R * Vector3.forward;
            }

            return GetLocalFireDirection();
        }

        /// <summary>The direction this machine's own camera says. See <see cref="GetLocalAimPoint"/>.</summary>
        protected virtual Vector3 GetLocalFireDirection()
        {
            Transform origin = GetFireOrigin();
            Vector3 aimPoint = GetLocalAimPoint();
            Vector3 direction = (aimPoint - origin.position).normalized;

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = origin.forward;
            }

            return direction;
        }

        /// <summary>
        /// Get the origin point for projectile spawn.
        /// </summary>
        protected virtual Transform GetFireOrigin()
        {
            if (firePoint != null)
            {
                return firePoint;
            }

            return aimCamera != null ? aimCamera.transform : transform;
        }

        /// <summary>
        /// Get spawn position for projectile.
        /// </summary>
        protected virtual Vector3 GetSpawnPosition()
        {
            Transform origin = GetFireOrigin();
            return origin.position + origin.forward * spawnOffset;
        }

        // ─────────── Firing across the network ───────────
        //
        // A shot is two different things on two different machines, and this is where they split.
        //
        // Before this, a weapon's whole effect lived in Use(), which UsableItem runs on the SERVER
        // and nowhere else. So the projectile existed only on the server (a plain Instantiate, never
        // network-spawned), the muzzle flash and the report happened only on the server, and the
        // ammo only ever came off the server's magazine — the owner's own HUD never moved. From a
        // client, firing a gun produced no bullet, no sound and no visible change: only a health bar
        // somewhere dropping for no apparent reason.

        /// <summary>
        /// Owner side, before the request leaves: say where this shot starts and where it points.
        ///
        /// Nobody else can reconstruct it. The server has neither this player's camera nor their
        /// exact frame, and a peer has neither either.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            base.OnRequestUse(ref arg);

            // The LOCAL aim, deliberately — this is the moment the owner's own camera is the answer,
            // and GetFireDirection would hand back whatever the previous shot reported.
            Vector3 direction = GetLocalFireDirection();

            arg.P = GetSpawnPosition();
            arg.R = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction)
                : GetFireOrigin().rotation;
        }

        /// <summary>
        /// Authority side: the shot that counts. Its projectile deals damage; its hitscan registers.
        /// </summary>
        protected override void Use()
        {
            ShotDealsDamage = true;
            TryFire();
        }

        /// <summary>
        /// Every machine: the shot you can see and hear.
        ///
        /// The authority is skipped because it already fired a real one a moment ago — running both
        /// would put two bullets in the air on the host. Everybody else gets a copy that looks and
        /// sounds identical and cannot hurt anyone, because the hit was decided on the server.
        /// </summary>
        protected override void Present()
        {
            PlayFireSound();

            if (Network.Simulates(this)) return;

            // Mirror the round off this machine's own magazine. Equipment is rebuilt locally on
            // every machine from the replicated hotbar, so each has its own Magazine — and the one
            // the owner's HUD reads is theirs, not the server's.
            if (magazine != null) magazine.ConsumeAmmo(ammoPerShot);

            // A charging weapon's shot is a two-press state machine — spawn on the first press,
            // launch on the second — and a peer never saw the first press, so it has no projectile
            // to launch. Peers therefore hear a charged shot but do not draw one. Showing it would
            // mean replicating the charge itself, which is a bigger piece of work than this.
            if (enableCharging) return;

            ShotDealsDamage = false;
            Fire();
        }

        /// <summary>
        /// Whether the shot currently being produced by <see cref="Fire"/> is the real one.
        ///
        /// False on a machine that is only showing what the server already resolved. A subclass that
        /// spawns a projectile must pass this on to it, and one that resolves its own hits (a
        /// hitscan) must not apply damage when it is false — otherwise every machine in the session
        /// bills the target for the same bullet.
        /// </summary>
        protected bool ShotDealsDamage { get; private set; } = true;

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // A weapon is destroyed and rebuilt from its prefab on every equip, and OnEnable above then
        // refills the magazine and clears the cooldown. So before this, an empty gun could be made
        // full by scrolling one slot down the hotbar and back — and a save simply made that
        // permanent, because there is no reload mechanic in this game and a magazine is a resource
        // rather than a convenience.

        private const string AmmoKey = "ammo";
        private const string CooldownKey = "cd";

        /// <summary>
        /// The rounds left and the shot clock, both as the player would experience them.
        ///
        /// The cooldown is stored as time REMAINING, never as <see cref="nextFireTime"/> itself:
        /// that is a stamp on <c>Time.time</c>, which restarts at zero every session, so a stored
        /// absolute would come back either permanently expired or years in the future.
        ///
        /// Charging is deliberately not stored. Its state is a live <see cref="IChargeable"/>
        /// projectile spawned into the world, and nothing in a save file can bring that instance
        /// back — see <see cref="RestoreItemState"/>.
        /// </summary>
        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            if (magazine != null) state.Set(AmmoKey, magazine.CurrentAmmo);

            float remaining = nextFireTime - Time.time;
            if (remaining > 0.01f) state.Set(CooldownKey, remaining);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            // OnEnable has already run by now and has already called Refill(). That is the reset
            // trap this override exists to undo, and the ordering is EquipmentController's doing —
            // it restores after OnEquipped, which is after the instance has woken up.
            if (magazine != null)
                magazine.SetAmmo(state == null ? magazine.MaxAmmo : state.GetInt(AmmoKey, magazine.MaxAmmo));

            nextFireTime = Time.time + Mathf.Max(0f, state == null ? 0f : state.GetFloat(CooldownKey, 0f));
            canFire = true;

            // A weapon that was mid-charge comes back uncharged, and the round it had already spent
            // stays spent. Resuming would mean re-spawning the charging projectile at load, which
            // puts a live object in the world on a machine that may be only presenting — and the
            // player can simply charge again.
            CancelCharging();
        }

        /// <summary>
        /// Override CanUse() to check ammo.
        /// Fire rate is checked in TryFire(), not here, because CanUse() must return true
        /// for the UsableItem system to call Use() from the inventory.
        /// </summary>
        protected override bool CanUse()
        {
            // Check base class first (max uses, etc.)
            if (!base.CanUse())
            {
                return false;
            }

            // Check if we have ammo
            if (magazine == null || magazine.IsEmpty)
            {
                return false;
            }

            // Fire rate is checked in TryFire(), not here
            // This allows the inventory system to properly call Use()
            return true;
        }

        /// <summary>
        /// Cancel charging if active (e.g., when weapon is unequipped).
        /// </summary>
        protected virtual void CancelCharging()
        {
            if (isCharging && chargedProjectile != null)
            {
                chargedProjectile.OnChargeCancelled();
            }

            isCharging = false;
            chargedProjectile = null;
        }

        /// <summary>
        /// Subclasses implement actual firing behavior here.
        /// </summary>
        protected abstract void Fire();

        /// <summary>
        /// Called by magazine when ammo changes.
        /// </summary>
        protected virtual void OnMagazineAmmoChanged()
        {
            OnAmmoChanged?.Invoke(CurrentAmmo);
        }

        /// <summary>
        /// Add ammo to magazine.
        /// </summary>
        public int AddAmmo(int amount)
        {
            if (magazine == null)
            {
                return 0;
            }

            return magazine.AddAmmo(amount);
        }

        /// <summary>
        /// Refill weapon magazine to full.
        /// </summary>
        public void Refill()
        {
            if (magazine != null)
            {
                magazine.Refill();
            }
        }

        /// <summary>Fires the weapon's shot sound at the muzzle.</summary>
        protected virtual void PlayFireSound()
        {
            Sfx.Play(fireSoundId, GetFireOrigin().position, fireSound, GetInstanceID());
        }

        /// <summary>Fires the spin-up sound when a chargeable weapon starts charging.</summary>
        protected virtual void PlayChargeStartSound()
        {
            Sfx.Play(chargeStartSoundId, GetFireOrigin().position, chargeStartSound, GetInstanceID());
        }
    }
}
