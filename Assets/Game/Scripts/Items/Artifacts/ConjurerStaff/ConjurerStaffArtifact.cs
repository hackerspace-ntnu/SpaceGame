// The lightning conjurer's staff, in a player's hands.
//
// Dropped by the creature on death (see EntityLootTable on its prefab) and it casts the same
// attack the creature does: a bolt out of the sky onto a chosen patch of ground, after a wind-up.
// The damage and the drawing are LightningStrike's, exactly as they are for the creature and for
// LightningSpell -- this class owns the wind-up, the aim, and the ring.
//
// ---- the ring is not decoration -----------------------------------------------
//
// A falling bolt cannot be blocked and cannot be dodged by angle. It arrives on the point that was
// picked, so the only counterplay anyone has -- the caster reading their own aim, and the victim
// reading the mark -- is seeing WHERE and WHEN. That is the ring's whole job, and it is why it has
// to lie on the ground rather than hover over it: a marker that floats a metre above a dune is
// describing a circle nobody is standing in. GroundRing resamples per vertex for that reason.
//
// The ring's radius is damageRadius. Not a fraction of it, not a bit less to be safe: a ring that
// does not mean "everything inside this is hit" teaches the wrong lesson the first time somebody
// stands just outside it and dies anyway.
//
// ---- what runs where -----------------------------------------------------------
//
// The press commits an aim, and then three machines each run their own copy of the same wind-up
// clock off the same authored castSeconds:
//
//   OWNER    picked the point. Nobody else can -- a peer's copy of this player has an AimProvider
//            with no live camera behind it -- so the point travels in NetArg from OnRequestUse.
//   SERVER   Use(): starts its clock, and bills the damage when it runs out. Once, for everyone.
//   EVERY    Present(): starts the same clock, lights the charge on the staff, and locks a ring on
//            the ground so the people standing in it can leave. Draws the bolt when it runs out.
//
// Nothing about the wind-up is sent per frame. Two machines counting the same authored duration
// from the same press land within a frame of each other, and the server owns the damage, so a peer
// whose bolt is drawn a frame early is not a problem worth a message.
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    public class ConjurerStaffArtifact : ToolItem
    {
        /// <summary>
        /// Server. The bolt hurts things other players can see hurt, so exactly one machine may
        /// decide it — UseAuthority.Owner is only for tools whose whole effect is the holder's own
        /// body.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("The cast")]
        [Tooltip("Seconds from the press to the bolt landing. The creature takes four; a player " +
                 "who cannot move while a fight happens around them takes rather less.")]
        [SerializeField] private float castSeconds = 1.2f;

        [Tooltip("Seconds after the bolt lands before it can be cast again.")]
        [SerializeField] private float cooldownSeconds = 4f;

        [Tooltip("How far the aim reaches. Beyond this the staff has nothing to aim at and the " +
                 "ring goes out.")]
        [SerializeField] private float aimDistance = 500f;

        [Tooltip("What the aim ray can land on. Triggers are always ignored.")]
        [SerializeField] private LayerMask aimMask = ~0;

        [Header("Damage")]
        [Tooltip("Dealt to everything caught in the strike. Whole points — NetDamage discards " +
                 "anything that rounds to zero.")]
        [SerializeField] private int damage = 90;

        [Tooltip("How wide the strike bites, in metres, measured where the bolt earths. This is " +
                 "also the ring's radius: change one and you have changed both, on purpose.")]
        [SerializeField] private float damageRadius = 3.5f;

        [Tooltip("What the strike can hurt.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Tooltip("Whether the caster can be caught in their own bolt. Off: the staff is aimed at " +
                 "what you are looking at, so hitting yourself with it is a mis-click, not a plan.")]
        [SerializeField] private bool damagesCaster;

        [Header("Presentation")]
        [Tooltip("The bolt. ConjurerLightningBolt, not Lightning.prefab — the latter's length is " +
                 "baked into its graph, so at this draw height it hangs in the sky with nothing " +
                 "under it.")]
        [SerializeField] private GameObject boltPrefab;

        [Tooltip("How high the bolt is drawn from, in metres. It is falling out of the clouds, so " +
                 "it needs sky to fall through.")]
        [SerializeField] private float drawHeight = 100f;

        [Tooltip("Seconds before the spawned bolt destroys itself. Never 0 — nothing else cleans " +
                 "it up and this fires on a loop.")]
        [SerializeField] private float vfxLifetime = 5f;

        [Tooltip("The charge that gathers on the turbine during the wind-up. Parented to the tip, " +
                 "so it rides the staff.")]
        [SerializeField] private GameObject chargePrefab;

        [Tooltip("Where the charge hangs — the emitter above the blades.")]
        [SerializeField] private Transform tip;

        [Tooltip("The mark on the ground. Spawned unparented: it belongs to the GROUND, not to " +
                 "the hand holding the staff.")]
        [SerializeField] private GroundRing aimRingPrefab;

        [Tooltip("Played on every machine when the bolt lands, as the press sound is played by " +
                 "PlayUse when it is cast.")]
        [SerializeField] private SfxId strikeSoundId = SfxId.WeaponBallLightningFire;

        /// <summary>State key for the recharge. Written into save files — never rename.</summary>
        private const string CooldownKey = "conjurerCooldown";

        /// The owner's own ring, alive for as long as the staff is held. Kept rather than spawned
        /// per cast: it is the aim marker between casts and the strike marker during one, and those
        /// are the same circle in two moods.
        private GroundRing _ring;

        /// A peer's ring. Peers have no aim to draw, so they only ever see the locked one, and it
        /// lasts exactly as long as the wind-up.
        private GroundRing _remoteRing;

        private GameObject _charge;

        private bool _casting;
        private float _castElapsed;
        private Vector3 _castPoint;

        /// Set when Present accepts a press, cleared when the authority half of that same press has
        /// been let through. It exists only to bridge those two calls on a host, where Present runs
        /// first — see CanUse.
        private bool _presentedPress;

        /// Runs on the authority only, and is what actually bills the damage. Separate from
        /// `_casting` because the two clocks belong to different machines and a host runs both.
        private bool _billing;
        private float _billElapsed;
        private Vector3 _billPoint;

        private float _cooldownEndsAt;

        // ── The recharge, across instances ─────────────────────────────────────
        //
        // The held object is a fresh Instantiate of the prefab, destroyed the moment the player
        // scrolls to the next hotbar slot — so a cooldown living on the instance is a cooldown you
        // can skip by scrolling down and back up. It belongs in the slot.

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // Seconds remaining, never a deadline: Time.time restarts at zero each session, so a
            // stored deadline comes back either already spent or hours away.
            float remaining = Mathf.Max(0f, _cooldownEndsAt - Time.time);
            if (remaining > 0.01f) state.Set(CooldownKey, remaining);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            float remaining = state == null ? 0f : state.GetFloat(CooldownKey, 0f);
            _cooldownEndsAt = remaining > 0f ? Time.time + remaining : 0f;
        }

        // ── Equip ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The ring is spawned for the local holder only. A remote player's staff has no aim to
        /// show — and drawing one from a peer's own camera would paint a circle in a place nobody
        /// is aiming at.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            if (!OwnerIsLocal() || aimRingPrefab == null) return;

            if (_ring == null)
            {
                _ring = Instantiate(aimRingPrefab);
                _ring.name = "ConjurerStaffAimRing";
            }

            _ring.SetLocked(false);
            _ring.SetProgress(0f);
            _ring.Hide();
        }

        // ── The press ──────────────────────────────────────────────────────────

        /// <summary>
        /// Owner-side, before the request leaves: where the bolt lands.
        ///
        /// <para>
        /// The only machine where an aim is honest. A raycast rather than
        /// <see cref="Characters.AimProvider.GetRayCast"/>, which logs a warning on a miss — aiming
        /// at open sky is an ordinary thing to do and this is also how the ring is placed every
        /// frame, so that warning would bury the console.
        /// </para>
        /// <para>
        /// Zero means "aimed at open sky", and every reader below checks for it. Without the
        /// sentinel a miss reads as a position and the bolt lands on the world origin.
        /// </para>
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.P = TryAim(out Vector3 point) ? point : Vector3.zero;
        }

        /// <summary>
        /// Authority-side gate. Charges are spent in <see cref="UsableItem.TryUse"/> whether or not
        /// the item does anything, so a staff whose button was mashed through its own wind-up would
        /// burn charges without ever firing.
        ///
        /// <para>
        /// The <c>_presentedPress</c> escape is load-bearing rather than a nicety. On a host,
        /// <see cref="EquipmentController.OnUse"/> calls <see cref="Present"/> FIRST and only then
        /// routes the same press to the server half — so by the time this is asked, Present has
        /// already started the cast and the cooldown, and a gate that just re-read them would
        /// refuse every press the host ever made. The staff would deal no damage at all in
        /// single-player while looking perfectly correct on screen, which is the exact shape of
        /// bug <c>LaserStaffArtifact._pressLitTheArc</c> exists to prevent.
        /// </para>
        /// <para>
        /// It is not a hole for a mashed button, because <see cref="Present"/> sets it only when it
        /// ACCEPTS a press — a second press during the wind-up is turned away there, so the flag
        /// stays false and the full gate below applies.
        /// </para>
        /// </summary>
        protected override bool CanUse() =>
            base.CanUse() &&
            (_presentedPress || (!_billing && !_casting && Time.time >= _cooldownEndsAt));

        /// <summary>
        /// Authority-side: start the clock. Nothing is billed here — the bolt has not landed yet,
        /// and the whole point of a wind-up is that the ground it covers can be walked out of.
        ///
        /// The server runs its own clock rather than waiting to be told, because a dedicated server
        /// never receives <see cref="Present"/>.
        /// </summary>
        protected override void Use()
        {
            if (UseArg.P == Vector3.zero) return;

            _billing = true;
            _billElapsed = 0f;
            _billPoint = UseArg.P;

            // Spent. The next press has to earn its way through the full gate.
            _presentedPress = false;

            // Started HERE as well as in Present, and that is not belt-and-braces.
            //
            // A dedicated server never receives Present, so a cooldown set only there is a cooldown
            // the deciding machine does not have. CanUse would then gate on `_billing` alone, which
            // goes false the instant the bolt lands — and the recharge on a dedicated server would
            // be the wind-up, with the whole cooldown missing.
            //
            // The host runs both halves and writes the same number twice, a frame apart at most.
            BeginCooldown();
        }

        /// <summary>
        /// Every machine: light the staff and mark the ground, then draw the bolt when the same
        /// authored wind-up runs out here too.
        /// </summary>
        protected override void Present()
        {
            if (UseArg.P == Vector3.zero) return;

            // PlayUse is deliberately NOT gated on CanUse — the authority already decided the use
            // happened, and re-deciding from a peer's copy of the charge count is how one machine
            // silently skips an effect everyone else saw. But a PRESS is not that: it arrives here
            // before anyone has decided anything, so a mashed button during the wind-up would
            // restart the cast and, through _presentedPress, wave the second one past the gate too.
            if (_casting || Time.time < _cooldownEndsAt) return;

            _casting = true;
            _castElapsed = 0f;
            _castPoint = UseArg.P;

            // This press is accepted, so the authority half of it may skip a gate that this line
            // has just closed behind it. See CanUse.
            _presentedPress = true;

            // Also on this side, because a CLIENT has no Use() — without it a peer's ring would
            // come back the moment the bolt landed, saying the staff was ready when it was not.
            BeginCooldown();

            if (chargePrefab != null && _charge == null)
            {
                // Parented to the tip so it rides the staff through the raise — the same trick the
                // creature plays with its StaffTip bone.
                Transform socket = tip != null ? tip : transform;
                _charge = Instantiate(chargePrefab, socket.position, socket.rotation, socket);
            }

            GroundRing ring = LockRing(_castPoint);
            if (ring != null)
            {
                ring.Show(_castPoint, damageRadius);
                ring.SetLocked(true);
                ring.SetProgress(0f);
            }
        }

        // ── Per frame ──────────────────────────────────────────────────────────

        private void Update()
        {
            TickAim();
            TickPresentation();
            TickBilling();
        }

        /// The ring following the crosshair. Owner-local, and only when nothing is in flight — once
        /// the cast is committed the ring belongs to the committed point and must stop following,
        /// which is the difference between a marker you can trust and one that lies about where the
        /// bolt is going.
        private void TickAim()
        {
            if (_ring == null) return;

            if (_casting || !OwnerIsLocal())
                return;

            if (Time.time < _cooldownEndsAt || !TryAim(out Vector3 point))
            {
                _ring.Hide();
                return;
            }

            _ring.SetLocked(false);
            _ring.SetProgress(0f);
            _ring.Show(point, damageRadius);
        }

        /// The wind-up everyone can see, and the bolt at the end of it.
        private void TickPresentation()
        {
            if (!_casting) return;

            _castElapsed += Time.deltaTime;

            GroundRing ring = _remoteRing != null ? _remoteRing : _ring;
            if (ring != null)
                ring.SetProgress(castSeconds > 0f ? _castElapsed / castSeconds : 1f);

            if (_castElapsed < castSeconds) return;

            _casting = false;

            LightningStrike.Present(boltPrefab, _castPoint + Vector3.up * drawHeight, _castPoint,
                                    vfxLifetime);
            Sfx.Play(strikeSoundId, _castPoint, GetInstanceID());

            ClearCharge();
            ReleaseRings();
        }

        /// The wind-up only the authority runs, and the damage at the end of it.
        private void TickBilling()
        {
            if (!_billing) return;

            _billElapsed += Time.deltaTime;
            if (_billElapsed < castSeconds) return;

            _billing = false;

            LightningStrike.Damage(_billPoint, damage, damageRadius, damageMask,
                                   owner != null ? owner : gameObject, damagesCaster);
        }

        // ── Teardown ───────────────────────────────────────────────────────────

        /// <summary>
        /// A cast dies with the staff that started it. Scrolling to the next hotbar slot destroys
        /// this instance, and this is also what a staff being thrown on the ground runs — a
        /// discarded staff must not finish calling down a bolt, and a ring left lying in the sand
        /// marking nothing is the other half of the same failure.
        /// </summary>
        public override void OnUnequipped(GameObject holder)
        {
            base.OnUnequipped(holder);
            Cancel();
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            // The rings are unparented, so nothing else takes them with it.
            if (_ring != null) Destroy(_ring.gameObject);
            if (_remoteRing != null) Destroy(_remoteRing.gameObject);
        }

        private void Cancel()
        {
            _casting = false;
            _billing = false;

            // Or an accepted-but-unspent press would wave the NEXT one straight past the cooldown.
            _presentedPress = false;

            ClearCharge();

            if (_ring != null)
            {
                _ring.SetLocked(false);
                _ring.SetProgress(0f);
                _ring.Hide();
            }

            if (_remoteRing != null)
            {
                Destroy(_remoteRing.gameObject);
                _remoteRing = null;
            }
        }

        /// <summary>
        /// The staff is spent until the wind-up has run and the recharge after it. Counted from the
        /// press rather than from the landing, so the two machines that start a cast reach the same
        /// deadline without either of them being told.
        /// </summary>
        private void BeginCooldown() =>
            _cooldownEndsAt = Time.time + castSeconds + cooldownSeconds;

        private void ClearCharge()
        {
            if (_charge == null) return;
            Destroy(_charge);
            _charge = null;
        }

        /// After the bolt: the owner keeps their ring for the next shot, a peer's is thrown away.
        private void ReleaseRings()
        {
            if (_remoteRing != null)
            {
                Destroy(_remoteRing.gameObject);
                _remoteRing = null;
            }

            if (_ring == null) return;

            _ring.SetLocked(false);
            _ring.SetProgress(0f);
            _ring.Hide();
        }

        /// <summary>
        /// The ring the locked mark goes on. The owner already has one; everybody else needs one
        /// spawned, because the people standing in the circle are the ones who most need to see it.
        /// </summary>
        private GroundRing LockRing(Vector3 at)
        {
            if (_ring != null) return _ring;
            if (aimRingPrefab == null) return null;

            if (_remoteRing == null)
            {
                _remoteRing = Instantiate(aimRingPrefab, at, Quaternion.identity);
                _remoteRing.name = "ConjurerStaffStrikeRing";
            }

            return _remoteRing;
        }

        // ── Aim ────────────────────────────────────────────────────────────────

        /// <summary>Where the holder is pointing, on the ground. False when that is open sky.</summary>
        private bool TryAim(out Vector3 point)
        {
            point = Vector3.zero;

            Transform aim = aimProvider != null ? aimProvider.AimTransform : null;
            if (aim == null) return false;

            if (!Physics.Raycast(aim.position, aim.forward, out RaycastHit hit, aimDistance,
                                 aimMask, QueryTriggerInteraction.Ignore))
                return false;

            point = hit.point;
            return true;
        }

        // ── Who is who ─────────────────────────────────────────────────────────

        // There is deliberately no IsAuthority() here, unlike LaserStaffArtifact.
        //
        // That staff needs one because it bills damage from Update, every frame it is burning, and
        // an equipped artifact is instantiated into a hand and never network-spawned — so its own
        // NetworkObject is dormant and Network.Simulates would answer "yes" on every machine in the
        // session, billing each victim once per player watching.
        //
        // This one never has to ask. `_billing` is set in Use() and nowhere else, and TryUse calls
        // Use() only where the item has authority; so the flag IS the answer, and TickBilling runs
        // on exactly the machine that set it.

        /// <summary>True when the local player is the one holding this staff.</summary>
        private bool OwnerIsLocal()
        {
            if (!Network.IsNetworked) return true;

            if (owner != null && owner.TryGetComponent(out Unity.Netcode.NetworkObject netObj) &&
                netObj.IsSpawned)
                return netObj.IsOwner;

            return true;
        }

        private void OnValidate()
        {
            castSeconds = Mathf.Max(0.05f, castSeconds);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
            aimDistance = Mathf.Max(1f, aimDistance);
            damage = Mathf.Max(0, damage);
            damageRadius = Mathf.Max(0.05f, damageRadius);
            drawHeight = Mathf.Max(1f, drawHeight);

            // Never 0. ConjurerLightningBolt has nothing on it that cleans it up, so a zero here
            // leaks one GameObject per cast for the life of the session.
            vfxLifetime = Mathf.Max(0.1f, vfxLifetime);
        }
    }
}
