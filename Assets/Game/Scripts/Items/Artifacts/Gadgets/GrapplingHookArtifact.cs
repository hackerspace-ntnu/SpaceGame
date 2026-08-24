using FMODUnity;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Grappling hook artifact — extends ToolItem.
    ///
    /// <para>
    /// A press throws a dart. While the dart is in the air the player keeps their own feet: nothing
    /// pulls, nothing is disabled, and a miss costs a moment of animation and nothing else. When
    /// the dart lands the rope goes taut and the player is on it.
    /// </para>
    /// <para>
    /// <b>The bite starts the winch.</b> It accelerates toward the anchor against relieved gravity,
    /// eases off as it arrives, and lets go with a boost that carries the player over the lip of
    /// whatever they climbed to. Catching something and reeling in are one gesture — a player who
    /// has to keep asking for the pull is a player wondering why their grapple did nothing.
    /// </para>
    /// <para>
    /// Letting go of the trigger <em>after</em> the rope has caught trades that climb for a swing:
    /// the rope holds its length and the player is a pendulum. Gravity does the work,
    /// <see cref="PlayerMovement.SetTethered"/> lets them steer and pump the arc, and a second
    /// press drops the rope with every bit of speed they built still on them. A release that
    /// arrives while the dart is still travelling is ignored — see <see cref="PresentHold"/>,
    /// which is where a tapped grapple used to lose its winch before it had anything to winch.
    /// </para>
    ///
    /// <para>
    /// Networking rides the same Use/Present split every other artifact uses, and nothing else:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="OnRequestUse"/> — owner-side, the one machine with the camera. It resolves
    /// the hook point, the surface normal and what was hit, because no peer can recompute an
    /// aim.</item>
    /// <item><see cref="Present"/> — every machine. Dart, rope and flight timing run here, so a peer
    /// sees a hook thrown and a rope drawn instead of a player mysteriously flying.</item>
    /// <item><see cref="PresentHold"/> — every machine. One latch, so a peer's rope reels on the
    /// same button the owner is holding.</item>
    /// </list>
    ///
    /// <para>
    /// The constraint inside <see cref="FixedUpdate"/> is the one part that stays owner-only. Their
    /// body is owner-authoritative, so the swing replicates through the transform they already own
    /// — and a peer running the constraint too would be a second authority on the same Rigidbody.
    /// </para>
    ///
    /// <para>
    /// This used to need a GrappleNetworkSync beside it: a NetworkBehaviour on the player with its
    /// own RPC triple and a replicated anchor. It carried nothing the use message does not, and the
    /// rope it existed to draw was never drawn — the LineRenderer it needed was unassigned on both
    /// player prefabs, so every remote grapple was invisible for as long as that component shipped.
    /// </para>
    /// </summary>
    public class GrapplingHookArtifact : ToolItem, IItemDeferredRestore
    {
        /// <summary>
        /// Owner-run: the swing IS the item. A round trip through the server would sit inside
        /// every jump. Present() replicates the rope so peers see what the swing hangs from.
        /// </summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        /// <summary>
        /// The press opens a hold stream, because the button is what chooses between the two
        /// grapples this item has — see the class summary. Nothing here fires per tick; the stream
        /// exists only so <see cref="PresentHold"/> can keep one latch honest on every machine.
        /// </summary>
        public override bool IsContinuous => true;

        [Header("Firing")]
        [SerializeField] private float maxRange = 60f;
        [SerializeField] private LayerMask hookableLayers = ~0;
        [SerializeField] private float shootSpeed = 60f;   // dart travel speed, m/s

        [Header("Winch — runs from the moment the dart bites")]
        [Tooltip("Acceleration toward the anchor, m/s². An acceleration and not a speed, so the " +
                 "pull has a ramp on it and adds to the swing already underway.")]
        [SerializeField] private float winchAcceleration = 55f;

        [Tooltip("Fastest the winch is allowed to close on the anchor, m/s.")]
        [SerializeField] private float maxWinchSpeed = 26f;

        [Tooltip("Fraction of gravity cancelled while the winch runs. At 0 the pull spends itself " +
                 "fighting this project's -18 m/s²; at 1 the swing turns into a rail.")]
        [SerializeField, Range(0f, 1f)] private float winchGravityRelief = 0.7f;

        [Tooltip("Upward speed given once, on a bite taken while standing, to get the capsule off " +
                 "the floor before the winch starts. Scaled by how far above the anchor is.")]
        [SerializeField] private float groundBreakBoost = 5f;

        [Header("Rope constraint")]
        [Tooltip("Fraction of the rope's overstretch corrected per physics step. Low is a rope " +
                 "with give in it, high is a steel bar.")]
        [SerializeField, Range(0.05f, 1f)] private float ropeCorrection = 0.25f;

        [Tooltip("Metres the body may sit outside the rope before it is put back by hand. A " +
                 "safety net for teleports and frame spikes, not part of the normal feel.")]
        [SerializeField] private float maxStretch = 1.5f;

        [Tooltip("Shortest the rope may be reeled to.")]
        [SerializeField] private float minRopeLength = 1.5f;

        [Header("Arrival & release")]
        [SerializeField] private float arrivalDistance = 2.5f;

        [Tooltip("Multiplier on the speed the player already had when the rope drops. Above 1 " +
                 "rewards releasing at the bottom of a fast arc.")]
        [SerializeField] private float exitMomentumScale = 1.1f;

        [Tooltip("Straight-up kick when the winch reaches the anchor, m/s.")]
        [SerializeField] private float arrivalUpBoost = 8f;

        [Tooltip("Push along the ground direction of the rope on arrival, m/s. This is what " +
                 "carries the player OVER the ledge they just climbed instead of into its face.")]
        [SerializeField] private float arrivalForwardBoost = 5f;

        [Tooltip("Ceiling on the whole exit velocity, m/s.")]
        [SerializeField] private float maxExitSpeed = 34f;

        [Tooltip("Seconds the winch may run without closing any distance before the rope is " +
                 "dropped. Without it a player jammed under an overhang is ground into it for as " +
                 "long as the button is down.")]
        [SerializeField] private float stallTimeout = 0.7f;

        [Header("Rope visual")]
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private Transform muzzle;      // gun-tip transform the rope pays out from
        [SerializeField] private GrappleRope rope = new GrappleRope();

        [Header("Hook head")]
        [Tooltip("The dart on the end of the rope. Cosmetic and spawned on every machine, so it " +
                 "must NOT be a registered network prefab.")]
        [SerializeField] private GameObject hookHeadPrefab;

        [Tooltip("Correction from the model's own forward axis to +Z, applied after aiming. Zero " +
                 "for the library's darts, which already import tip-down-+Z at scale 1.")]
        [SerializeField] private Vector3 hookHeadEuler;

        [Tooltip("Metres from the model's ORIGIN — the rope eye — to its tip. The darts put their " +
                 "origin on the eye so the rope needs no offset, which means the whole model " +
                 "extends forward of it and seating has to account for the length.")]
        [SerializeField] private float hookHeadTipOffset = 0.37f;

        [Tooltip("Metres the TIP sinks past the surface. The rest of the shaft stands proud of it — " +
                 "lower this to make the harpoon sit higher out of the wall.")]
        [SerializeField] private float hookHeadEmbed = 0.08f;

        [Tooltip("How square to the surface a shot must land before the harpoon is allowed to keep " +
                 "the angle it was fired at. Below this it is straightened toward the surface " +
                 "normal, so a glancing hit does not leave it lying flat with nothing buried. " +
                 "0.35 is about 70° off the normal.")]
        [SerializeField, Range(0f, 1f)] private float minBiteDot = 0.35f;

        [Tooltip("Multiplier on the dart's size. The head is looked at from across a canyon as " +
                 "often as from arm's length, so this is here to be tuned by eye rather than " +
                 "requiring a re-export. The tip offset is scaled with it — see EffectiveTipOffset.")]
        [SerializeField] private float hookHeadScale = 1f;

        [Header("Camera")]
        [Tooltip("Extra degrees of field of view at full speed, added on top of the player's own " +
                 "FOV setting. Zero disables the effect.")]
        [SerializeField] private float fovKick = 12f;

        [Tooltip("Speed at which the view starts opening up, m/s.")]
        [SerializeField] private float fovKickFromSpeed = 9f;

        [Tooltip("Speed at which it reaches the full kick, m/s.")]
        [SerializeField] private float fovKickAtSpeed = 30f;

        [Tooltip("Light the crosshair while the aim would actually catch. A grapple that feels " +
                 "bad in playtesting is usually a grapple you could not aim.")]
        [SerializeField] private bool showAimHint = true;

        [Header("Audio")]
        [SerializeField] private SfxId biteSoundId = SfxId.ImpactMetal;
        [SerializeField] private EventReference biteSound;

        // What the press meant, carried in NetArg.B. A is already the hotbar slot, so B it is.
        private const int Release = 0;
        private const int Attach  = 1;

        // ── Runtime state ──────────────────────────────────────────────────────
        private bool _isGrappling;
        private bool _isShooting;
        private bool _winching;

        private Vector3 _hookPoint;
        private Vector3 _hitNormal = Vector3.up;
        private Vector3 _flightDirection = Vector3.forward;
        private float _ropeLength;

        private float _flightElapsed;
        private float _flightDuration;

        private float _lastDistance;
        private float _stallTime;

        private Transform _head;
        private Rigidbody _body;
        private PlayerMovement _movement;
        private PlayerLook _look;
        private CrosshairUI _crosshair;

        /// <summary>
        /// Grab the local HUD once per equip.
        ///
        /// <para>
        /// A scene lookup, which is why it is here and not in Update. It is deliberately gated on
        /// owning the body: this item is instantiated on every machine, so without that test a
        /// remote player picking up a grapple would light up YOUR crosshair from across the map.
        /// </para>
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            if (showAimHint && OwnsMovement())
                _crosshair = FindFirstObjectByType<CrosshairUI>();
        }

        /// <summary>
        /// What the hook is stuck in, when it is stuck in something that can move.
        ///
        /// Resolved from the use message rather than from a local raycast, so a peer hangs the rope
        /// on the same object the owner did. It stays null for static geometry, where the world
        /// point in the message is already the whole answer.
        /// </summary>
        private Transform _hookAttach;

        /// <summary>The hook point in <see cref="_hookAttach"/>'s local space.</summary>
        private Vector3 _attachOffset;

        // ── Owner side: describe the press ─────────────────────────────────────

        /// <summary>
        /// Owner-side: settle what this press is and, if it is a throw, everything about where it
        /// lands that no other machine can work out.
        ///
        /// The raycast belongs here rather than in Use(). It is the only moment the aim is honest
        /// — this is the machine holding the camera — and resolving it once means every machine
        /// hangs its rope on the same point instead of each guessing its own.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            arg.B = Release;

            // Rope already out: this press lets go, and there is nothing to aim at.
            if (_isGrappling || _isShooting) return;

            if (aimProvider == null) return;

            RaycastHit? hit = aimProvider.GetRayCast(maxRange);
            if (hit == null) return;

            // Respect hookable layer mask
            if ((hookableLayers.value & (1 << hit.Value.collider.gameObject.layer)) == 0)
                return;

            arg.B = Attach;
            arg.P = hit.Value.point;

            // The surface normal, so every machine can bury the dart into the wall at the same
            // angle instead of leaving it pointing wherever it happened to be flying.
            arg.R = Quaternion.LookRotation(hit.Value.normal);

            // What it went into. Resolves to the same object on a peer when that object is
            // networked, and to nothing when it is scenery — which is the correct answer, because
            // scenery does not move and the world point already describes it completely.
            arg = arg.With(hit.Value.collider.gameObject);
        }

        /// <summary>
        /// Nothing. Both halves of this item are either the owner's own body moving or a rope being
        /// drawn, and both live in <see cref="Present"/> so that peers get them too.
        /// </summary>
        protected override void Use() { }

        // ── Every machine: the throw ───────────────────────────────────────────

        protected override void Present()
        {
            if (UseArg.B != Attach)
            {
                StopGrapple();
                return;
            }

            // A second attach with no release in between means a message arrived twice or out of
            // order. Keep the rope that is already flying rather than starting a rival throw.
            if (_isGrappling || _isShooting) return;
            if (owner == null) return;

            CacheOwner();

            _hookPoint = UseArg.P;
            _hitNormal = UseArg.HasOrientation ? UseArg.R * Vector3.forward : Vector3.up;

            BindAttach(UseArg.Resolve());

            BeginFlight();
        }

        /// <summary>
        /// Every machine: the trigger, but only once the rope has caught.
        ///
        /// <para>
        /// Ticks that arrive while the dart is still travelling are ignored, and that is the whole
        /// point. A press and release is over in about a tenth of a second; a dart thrown 50 m
        /// takes the better part of one. So a tapped grapple's release ALWAYS landed before the
        /// bite, the winch was switched off before there was anything to winch, and the hook
        /// caught and then simply hung there — which is what "it doesn't reel in" was.
        /// </para>
        /// <para>
        /// <see cref="Bite"/> now starts the winch itself. This only ever turns it off, and only
        /// for a player who is still holding the trigger when the rope goes taut and then lets go
        /// — which is the deliberate gesture for trading the climb for a swing.
        /// </para>
        /// <para>
        /// Peers get it too, because their rope reels on it: a rope that stayed at its throw
        /// length while the owner winched to the top would be drawn slack for the whole climb.
        /// </para>
        /// </summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            if (!_isGrappling) return;
            _winching = active;
        }

        // ── The throw ──────────────────────────────────────────────────────────

        private void BeginFlight()
        {
            Vector3 start = GetRopeStart();

            // Kept for the landing, not the flight. A harpoon stays at the angle it arrived at, so
            // where it was thrown FROM is what decides how it ends up sitting in the wall.
            Vector3 travel = _hookPoint - start;
            _flightDirection = travel.sqrMagnitude > 1e-6f ? travel.normalized : -_hitNormal;

            _flightElapsed = 0f;
            _flightDuration = Vector3.Distance(start, _hookPoint) / Mathf.Max(shootSpeed, 0.1f);

            _isShooting = true;
            _isGrappling = false;

            rope.Bind(lineRenderer);
            rope.Show();

            SpawnHead(start, _hookPoint - start);

            Animator animator = owner.GetComponentInChildren<Animator>();
            if (animator) animator.SetTrigger("ShootRifle");
        }

        /// <summary>
        /// The dart lands.
        ///
        /// This — not the press — is where the player stops being in charge of their own feet. The
        /// hook used to disable their movement in <see cref="Present"/>, which is why firing it felt
        /// like being dragged: control was gone the moment the trigger came down, while the rope was
        /// still travelling and had not caught anything yet.
        /// </summary>
        private void Bite()
        {
            _isShooting = false;
            _isGrappling = true;

            Vector3 anchor = CurrentAnchor();

            // Exactly the gap at the moment it caught. Anything shorter is a free teleport toward
            // the anchor; anything longer is slack the player never threw.
            _ropeLength = Mathf.Max(minRopeLength, Vector3.Distance(OwnerPosition(), anchor));
            _lastDistance = _ropeLength;
            _stallTime = 0f;

            // Catching IS the reel. Waiting for the trigger to still be down meant a tapped
            // grapple caught and then hung, because the release beat the dart to the wall. Letting
            // go from here trades the climb for a swing; see PresentHold.
            _winching = true;

            PlantHead(anchor);
            rope.Bite();
            Sfx.Play(biteSoundId, anchor, biteSound, GetInstanceID());

            if (!OwnsMovement()) return;

            _movement?.SetTethered(true);
            BreakGroundContact(anchor);
        }

        /// <summary>
        /// One hop, so a winch that starts on the floor starts in the air.
        ///
        /// <para>
        /// Freeing the horizontal was the real fix for being unable to grapple off the ground —
        /// see <see cref="PlayerMovement.SetTethered"/>. This is the other half, and it is feel
        /// rather than correctness: a capsule resting on a collider has that collider's normal
        /// force and friction to get out of before an upward pull shows, so the first moments of a
        /// climb are spent scraping along the floor instead of leaving it.
        /// </para>
        /// <para>
        /// Scaled by how far ABOVE the anchor actually is, so hooking something at eye level across
        /// a room does not toss the player upward for no reason. Taken as a floor on their vertical
        /// speed rather than added to it — a player who jumped into the shot keeps their jump
        /// instead of being given a second one on top of it.
        /// </para>
        /// </summary>
        private void BreakGroundContact(Vector3 anchor)
        {
            if (groundBreakBoost <= 0f || _body == null) return;
            if (_movement == null || !_movement.IsOnGround) return;

            Vector3 toHook = anchor - OwnerPosition();
            if (toHook.sqrMagnitude < 1e-4f) return;

            float lift = groundBreakBoost * Mathf.Clamp01(toHook.normalized.y);
            if (lift <= 0.01f) return;

            Vector3 v = _body.linearVelocity;
            v.y = Mathf.Max(v.y, lift);
            _body.linearVelocity = v;
        }

        // ── Visuals, every machine, every frame ────────────────────────────────

        private void Update()
        {
            TickAimHint();

            if (_isShooting)
            {
                TickFlight();
                return;
            }

            if (!_isGrappling) return;

            Vector3 anchor = CurrentAnchor();

            if (!OwnsMovement() && TickRemoteRope(anchor)) return;

            TickFovKick();

            rope.DrawTether(GetRopeStart(), RopeEnd(anchor), _ropeLength);
        }

        /// <summary>
        /// Light the crosshair while the aim would actually catch something.
        ///
        /// <para>
        /// The cheapest large win this item had left: almost every grapple that feels bad in
        /// playtesting is really a grapple the player could not aim, because nothing on screen
        /// distinguishes a wall 40 m away that will hold a dart from sky that will not.
        /// </para>
        /// <para>
        /// The ray is cast here rather than through <see cref="AimProvider.GetRayCast"/> on
        /// purpose — that one logs a warning on every miss, which at frame rate is a console full
        /// of them for the entirely ordinary act of pointing at the horizon.
        /// </para>
        /// </summary>
        private void TickAimHint()
        {
            if (!showAimHint || _crosshair == null || !OwnsMovement()) return;

            // A rope that is already out means the next press is a release, and a lit crosshair
            // there promises a throw that is not going to happen.
            bool idle = !_isGrappling && !_isShooting;

            _crosshair.SetAimHint(idle && AimWouldCatch());
        }

        private bool AimWouldCatch()
        {
            if (aimProvider == null || aimProvider.AimTransform == null) return false;

            if (!Physics.Raycast(aimProvider.GetAimRay(), out RaycastHit hit, maxRange,
                                 ~0, QueryTriggerInteraction.Ignore))
                return false;

            return (hookableLayers.value & (1 << hit.collider.gameObject.layer)) != 0;
        }

        /// <summary>
        /// Open the view up with speed, on the machine whose camera it is.
        ///
        /// <para>
        /// Driven by the player's whole speed rather than by the winch alone, so a fast swing gets
        /// it too — the point is to sell how fast they are going, and a pendulum at the bottom of
        /// its arc is the fastest this item ever moves anyone.
        /// </para>
        /// <para>
        /// This is worth more than it looks. When the entire view translates together, nothing in
        /// frame changes size, so there is very little for the eye to read speed from; widening the
        /// lens is what puts the periphery back in and makes the world rush past the edges.
        /// </para>
        /// </summary>
        private void TickFovKick()
        {
            if (fovKick <= 0f || _look == null || _body == null || !OwnsMovement()) return;

            float speed = _body.linearVelocity.magnitude;
            float t = Mathf.InverseLerp(fovKickFromSpeed, fovKickAtSpeed, speed);

            _look.SetFovOffset(t * fovKick);
        }

        private void TickFlight()
        {
            _flightElapsed += Time.deltaTime;

            float progress = _flightDuration <= 0f
                ? 1f
                : Mathf.Clamp01(_flightElapsed / _flightDuration);

            Vector3 start = GetRopeStart();
            rope.DrawFlight(start, _hookPoint, progress);

            if (_head != null)
            {
                Vector3 travel = _hookPoint - start;
                _head.SetPositionAndRotation(
                    Vector3.Lerp(start, _hookPoint, progress),
                    HeadRotation(travel));
            }

            if (progress >= 1f) Bite();
        }

        /// <summary>
        /// A peer's half of the swing: the rope, and only the rope. Returns true if it just ended.
        ///
        /// Everything here is derived from where the swinging player actually is, which their
        /// NetworkTransform is already delivering, and from the one latch <see cref="PresentHold"/>
        /// keeps. That is deliberate — it means the reel and the auto-release need no messages of
        /// their own, and a peer cannot end up drawing a rope the owner has already dropped.
        /// This routine must never touch that body: the owner is its only authority.
        /// </summary>
        private bool TickRemoteRope(Vector3 anchor)
        {
            float dist = Vector3.Distance(OwnerPosition(), anchor);

            if (!_winching)
            {
                _stallTime = 0f;
                _lastDistance = dist;
                return false;
            }

            Ratchet(dist);

            // The same two auto-releases the owner runs, against the same numbers.
            if (dist <= arrivalDistance || IsStalled(dist, Time.deltaTime))
            {
                StopGrapple();
                return true;
            }

            return false;
        }

        // ── The swing, owner only, on the physics clock ────────────────────────
        //
        // On the physics clock and not in a coroutine, which is where all of this used to live.
        // A constraint stepped in Update writes rb.position at the display rate into a body Unity
        // is interpolating between fixed steps, so the two disagree about where the player is every
        // single frame — the jitter that made a swing read as a stutter rather than an arc.

        private void FixedUpdate()
        {
            if (!_isGrappling || !OwnsMovement()) return;
            if (_body == null || _body.isKinematic) return;

            float dt = Time.fixedDeltaTime;

            Vector3 anchor = CurrentAnchor();
            Vector3 toHook = anchor - _body.position;
            float dist = toHook.magnitude;

            if (dist < 0.001f)
            {
                StopGrapple();
                return;
            }

            Vector3 radial = toHook / dist;

            if (_winching)
            {
                Winch(radial, dist, dt);
                Ratchet(dist);
            }

            ApplyRopeConstraint(anchor, radial, dist, dt);

            if (_winching && dist <= arrivalDistance)
            {
                ReleaseInto(radial, arrived: true);
                return;
            }

            if (IsStalled(dist, dt)) ReleaseInto(radial, arrived: false);
        }

        /// <summary>
        /// Take up the line the winch just gained, and never pay any back out.
        ///
        /// Deliberately NOT applied while swinging. A rope that shortened every time the player
        /// happened to pass inside its own length would ratchet its way up the anchor for free, and
        /// a pendulum whose length quietly shrinks every pass is not a pendulum.
        /// </summary>
        private void Ratchet(float dist) =>
            _ropeLength = Mathf.Max(minRopeLength, Mathf.Min(_ropeLength, dist));

        /// <summary>Pull toward the anchor while the trigger is down.</summary>
        private void Winch(Vector3 radial, float dist, float dt)
        {
            Vector3 v = _body.linearVelocity;

            // Cancel most of gravity along the way. Without it the winch spends its pull fighting
            // -18 m/s² and the player watches themselves crawl; with it the pull reads as a pull.
            // Not all of it, so a winch aimed sideways still arcs instead of running on rails.
            v.y -= Physics.gravity.y * winchGravityRelief * dt;

            v += radial * (winchAcceleration * dt);

            // Ease off over the last stretch, so arriving is a glide into the anchor rather than a
            // full-speed stop against it — and so the boost that follows is the only thing the
            // player feels at the top.
            float ease = Mathf.Clamp01(dist / Mathf.Max(arrivalDistance * 3f, 0.01f));
            float ceiling = Mathf.Max(maxWinchSpeed * ease, maxWinchSpeed * 0.25f);

            float closing = Vector3.Dot(v, radial);
            if (closing > ceiling) v -= radial * (closing - ceiling);

            _body.linearVelocity = v;
        }

        /// <summary>
        /// Hold the player on the sphere of radius <see cref="_ropeLength"/> around the anchor.
        ///
        /// Nothing here touches the tangential half of the motion, which is precisely what makes
        /// this a pendulum: gravity builds speed across the arc and the rope only ever refuses to
        /// let the player leave. Slack does nothing at all, so a player inside the rope's length is
        /// in free fall until it comes tight again.
        /// </summary>
        private void ApplyRopeConstraint(Vector3 anchor, Vector3 radial, float dist, float dt)
        {
            float stretch = dist - _ropeLength;
            if (stretch <= 0f) return;

            Vector3 v = _body.linearVelocity;

            // Stop whatever is still trying to leave.
            float outward = Vector3.Dot(v, -radial);
            float correction = outward > 0f ? outward : 0f;

            // Then take back a fixed FRACTION of the remaining error per step rather than all of
            // it. Resolving it in one go is a hard snap, and a hard snap is both a visible jolt and
            // — written as a position — a fight with the interpolator.
            correction += stretch / dt * ropeCorrection;

            _body.linearVelocity = v + radial * correction;

            // Safety only: a body a long way outside the rope has been teleported, spiked, or
            // carried there by its anchor. Letting the correction above chase an arbitrarily large
            // error is how a player gets slingshotted across the map.
            if (stretch > maxStretch)
                _body.position = anchor - radial * (_ropeLength + maxStretch);
        }

        /// <summary>
        /// Winching hard and getting nowhere: the player is jammed against something between them
        /// and the anchor. Left alone the winch grinds them into it for as long as the button is
        /// down, which reads as the hook being broken rather than blocked.
        /// </summary>
        private bool IsStalled(float dist, float dt)
        {
            if (!_winching)
            {
                _stallTime = 0f;
                _lastDistance = dist;
                return false;
            }

            // "Getting anywhere", not "getting closer". A player who fires the hook while running
            // and then holds the trigger swings AWAY from the anchor for a moment before the winch
            // turns them around, and billing that as a stall would drop the rope on exactly the
            // shot that needed it most. A player wedged under an overhang moves in neither
            // direction, which is the only thing this is meant to catch.
            //
            // Measured off the distance rather than the velocity on purpose: a peer's copy of a
            // remote body is kinematic and reports no velocity at all, and this same test has to
            // reach the same verdict there or the two machines drop the rope at different times.
            bool moving = Mathf.Abs(_lastDistance - dist) > 0.5f * dt;
            _stallTime = moving ? 0f : _stallTime + dt;
            _lastDistance = dist;

            return _stallTime > stallTimeout;
        }

        /// <summary>
        /// Drop the rope and hand the player back their momentum.
        ///
        /// <paramref name="arrived"/> separates the two releases this item has. A player who let go
        /// mid-arc keeps exactly what the swing earned them, which is the whole skill of a grapple.
        /// A player the winch carried to the top gets that plus a pop — and the pop is deliberately
        /// split into an upward kick and a push along the GROUND direction of the rope, because a
        /// boost aimed purely along the rope drives you into the face of whatever you just hooked.
        /// </summary>
        private void ReleaseInto(Vector3 radial, bool arrived)
        {
            Vector3 exit = _body.linearVelocity * exitMomentumScale;

            if (arrived)
            {
                exit += Vector3.up * arrivalUpBoost;

                Vector3 flat = new Vector3(radial.x, 0f, radial.z);
                if (flat.sqrMagnitude > 1e-4f) exit += flat.normalized * arrivalForwardBoost;
            }

            _body.linearVelocity = Vector3.ClampMagnitude(exit, maxExitSpeed);

            // The horizontal half of that is otherwise confiscated inside a fifth of a second by
            // the air-control lerp. The portal gun learned this the same way — see CarryMomentum.
            _movement?.CarryMomentum();

            StopGrapple();
        }

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // Quitting mid-swing used to reload the player at exactly the coordinates they were hanging
        // at, with no rope and no anchor — so the reward for saving over a canyon was falling into
        // it. The rope is restored here, at the length it had reached, and the pendulum simply picks
        // up where it left off.

        private const string HookKey = "hook";     // world point the hook is set in
        private const string RopeKey = "rope";     // rope length, mid-reel
        private const string NormKey = "nrm";      // surface normal the harpoon is buried in
        private const string DirKey  = "dir";      // the direction it was fired, which sets its pose
        private const string AttachKey = "at";     // what it is set INTO, when that can move
        private const string OffsetKey = "off";    // the hook point in that thing's local space

        private SaveRef _pendingAttachRef;
        private Vector3 _pendingAttachOffset;
        private bool _pendingRestore;

        public bool HasPendingRestore => _pendingRestore;

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // Mid-throw counts as attached: the hook point is already settled, and coming back
            // hanging from it is closer to what the player was doing than coming back falling.
            if (!_isGrappling && !_isShooting) return;

            state.Set(HookKey, _hookPoint);
            state.Set(RopeKey, _ropeLength);
            state.Set(NormKey, _hitNormal);
            state.Set(DirKey, _flightDirection);

            if (_hookAttach == null) return;

            SaveRef anchor = SaveRef.From(_hookAttach.gameObject);
            if (!anchor.IsSet) return;   // static geometry: the world point is the whole answer

            state.Set(AttachKey, anchor);
            state.Set(OffsetKey, _attachOffset);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            _pendingRestore = false;
            _pendingAttachRef = SaveRef.None;

            // No rope in the record means the player was not hanging from anything, and a fresh
            // instance already is not. StopGrapple covers the case where this instance was handed a
            // second bag after being handed a first one.
            if (state == null || !state.Has(HookKey))
            {
                StopGrapple();
                return;
            }

            // Resumed now rather than in the deferred pass, because the swing is self-contained:
            // a world point is a complete anchor on its own. The reference below only REFINES it,
            // for the case where the thing it is set into has moved since the save.
            ResumeGrapple(state.GetVector3(HookKey), state.GetFloat(RopeKey),
                          state.GetVector3(NormKey), state.GetVector3(DirKey));

            SaveRef anchor = state.GetRef(AttachKey);
            if (!anchor.IsSet) return;

            _pendingAttachRef = anchor;
            _pendingAttachOffset = state.GetVector3(OffsetKey);
            _pendingRestore = true;
        }

        /// <summary>
        /// Re-anchor the rope onto the object it was actually set into, once that object exists.
        ///
        /// Idempotent and consumed only on success, the house rule for a reference that may name
        /// something still streaming in.
        /// </summary>
        public void TryCompleteRestore()
        {
            if (!_pendingRestore) return;
            if (!_pendingAttachRef.TryResolve(out GameObject anchor)) return;

            _pendingRestore = false;

            if (!_isGrappling && !_isShooting) return;

            _hookAttach = anchor.transform;
            _attachOffset = _pendingAttachOffset;
            _hookPoint = _hookAttach.TransformPoint(_attachOffset);

            PlantHead(_hookPoint);
        }

        /// <summary>
        /// Put the player back on a rope that is already out.
        ///
        /// The throw is skipped on purpose — the player watched the dart travel before they saved,
        /// and this starts at the part they were in the middle of. The winch resumes with it, for
        /// the same reason <see cref="Bite"/> starts it: reeling is what this hook does, and it
        /// ends by itself at the anchor. Coming back hanging motionless from a rope with no way to
        /// climb it, on a save made over a canyon, is the worse of the two guesses.
        /// </summary>
        private void ResumeGrapple(Vector3 hookPoint, float ropeLength, Vector3 normal, Vector3 fired)
        {
            if (owner == null) return;
            if (_isGrappling || _isShooting) return;

            CacheOwner();

            _hookPoint = hookPoint;
            _hitNormal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up;

            // Records written before the fired direction was stored have no angle in them, and
            // square to the wall is the honest answer for a harpoon whose throw nobody recorded.
            _flightDirection = fired.sqrMagnitude > 1e-4f ? fired.normalized : -_hitNormal;

            _hookAttach = null;
            _attachOffset = Vector3.zero;
            _winching = true;

            _ropeLength = ropeLength > 0.01f
                ? ropeLength
                : Vector3.Distance(owner.transform.position, hookPoint);

            _isShooting = false;
            _isGrappling = true;
            _lastDistance = Vector3.Distance(OwnerPosition(), hookPoint);
            _stallTime = 0f;

            rope.Bind(lineRenderer);
            rope.Show();

            SpawnHead(hookPoint, _flightDirection);
            PlantHead(hookPoint);

            if (OwnsMovement()) _movement?.SetTethered(true);
        }

        // ── Teardown ───────────────────────────────────────────────────────────

        /// <summary>
        /// Unequipping, dying, or being destroyed mid-swing must give the body back.
        ///
        /// This matters more than it did: the tether the hook now sets never expires on its own,
        /// where the 999-second DisableGroundSnap it replaced eventually did. A player who switched
        /// hotbar slots mid-swing would otherwise keep rope steering and lose fall damage for the
        /// rest of the session.
        /// </summary>
        private void OnDisable() => StopGrapple();

        private void StopGrapple()
        {
            // Also the miss case: a press that hit nothing presents a Release, and there is no
            // rope to drop. Bailing keeps a miss from touching the player's movement at all.
            if (!_isGrappling && !_isShooting) return;

            _isGrappling = false;
            _isShooting = false;
            _winching = false;
            _hookAttach = null;
            _attachOffset = Vector3.zero;
            _pendingRestore = false;
            _stallTime = 0f;

            rope.Hide();
            DestroyHead();

            _movement?.SetTethered(false);

            // Hand the lens back. PlayerLook eases it closed rather than snapping, so a release at
            // full speed still gets its glide — but the request has to stop arriving or the view
            // stays wide for the rest of the session.
            _look?.SetFovOffset(0f);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        /// <summary>
        /// Resolve the owner's body and movement once per throw rather than per physics step.
        /// GetComponent inside FixedUpdate is a lookup fifty times a second for an answer that
        /// cannot change while the rope is out.
        /// </summary>
        private void CacheOwner()
        {
            _body = owner != null ? owner.GetComponent<Rigidbody>() : null;
            _movement = owner != null ? owner.GetComponent<PlayerMovement>() : null;
            _look = owner != null ? owner.GetComponent<PlayerLook>() : null;
        }

        private void BindAttach(GameObject attach)
        {
            _hookAttach = attach != null ? attach.transform : null;
            _attachOffset = _hookAttach != null
                ? _hookAttach.InverseTransformPoint(_hookPoint)
                : Vector3.zero;
        }

        /// <summary>
        /// Where the rope actually ends, this frame.
        ///
        /// Recomputed from the local offset whenever the hook is set in something that can move, so
        /// grappling a vehicle pulls the player along with it instead of leaving the rope pinned to
        /// the patch of air the vehicle used to occupy.
        /// </summary>
        private Vector3 CurrentAnchor() =>
            _hookAttach != null ? _hookAttach.TransformPoint(_attachOffset) : _hookPoint;

        /// <summary>
        /// Where the cable is DRAWN to, which is not where the constraint hangs from.
        ///
        /// The dart stands proud of the surface it is set in, and its origin is the rope eye — so
        /// the cable terminates on the eye. Ending it at the hit point instead would draw the last
        /// stretch of rope straight through the shaft of the thing it is tied to.
        /// </summary>
        private Vector3 RopeEnd(Vector3 anchor) => _head != null ? _head.position : anchor;

        private Vector3 OwnerPosition() =>
            _body != null ? _body.position : owner != null ? owner.transform.position : _hookPoint;

        /// <summary>
        /// True when this machine is allowed to move the grappling player's Rigidbody: offline, or
        /// networked and this is the owning client.
        /// </summary>
        private bool OwnsMovement()
        {
            if (!Network.IsNetworked)
                return true;

            if (owner != null && owner.TryGetComponent(out NetworkObject netObj))
                return netObj.IsOwner;

            return true;
        }

        private Vector3 GetRopeStart() =>
            muzzle != null ? muzzle.position : owner != null ? owner.transform.position : transform.position;

        // ── The dart ───────────────────────────────────────────────────────────

        private void SpawnHead(Vector3 at, Vector3 forward)
        {
            if (hookHeadPrefab == null) return;

            DestroyHead();

            // Unparented and in world space: it has to keep flying while the hand that threw it
            // moves, and stay put in the wall afterwards.
            _head = Instantiate(hookHeadPrefab, at, HeadRotation(forward)).transform;

            // Applied to the root, which the library's darts import at scale 1 for exactly this
            // reason — their mesh child sits at 100 and must not be touched.
            if (!Mathf.Approximately(hookHeadScale, 1f))
                _head.localScale *= Mathf.Max(0.01f, hookHeadScale);

            // The dart is scenery. It does not go through EquipItemSocket.Sanitize the way a held
            // item does, so anything solid on it would shove the player around on the way out and
            // then sit in the path of the next shot's aim ray — a hook that can only be fired once
            // per anchor, for reasons nothing in this file would explain.
            Collider[] solids = _head.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < solids.Length; i++) solids[i].enabled = false;
        }

        private void PlantHead(Vector3 anchor)
        {
            if (_head == null) return;

            // The angle it arrived at, which is the angle a real harpoon keeps. Planting it along
            // the surface normal instead — which is what this did — snapped every shot square to
            // the wall, so a harpoon thrown up at a ledge from below stood out of it like a nail
            // hammered in by someone standing on it.
            Vector3 into = PlantDirection();

            // Seated so the TIP is buried and the shaft is not. The model's origin is its rope eye
            // and the whole harpoon extends forward of that, so dropping the origin on the hit point
            // would put every millimetre of it inside the wall — a hook that lands and vanishes.
            // Backed off along the SAME axis it is rotated to, or the two disagree and the shaft
            // leaves the surface at one angle while pointing at another.
            float standOff = Mathf.Max(0f, EffectiveTipOffset - hookHeadEmbed);
            _head.SetPositionAndRotation(anchor - into * standOff, HeadRotation(into));

            // Rides whatever it is set in, so a dart in a moving vehicle travels with it.
            if (_hookAttach != null) _head.SetParent(_hookAttach, worldPositionStays: true);
        }

        /// <summary>
        /// Which way the planted harpoon points, into the surface.
        ///
        /// <para>
        /// The direction it was fired, straightened toward the surface normal only when the shot
        /// was glancing enough that the fired angle would leave it lying flat along the wall with
        /// nothing actually in it. Below <see cref="minBiteDot"/> the two are blended, and the
        /// blend reaches the fired direction exactly at that threshold — so there is no visible
        /// step between a shot that is corrected and one that is not.
        /// </para>
        /// </summary>
        private Vector3 PlantDirection()
        {
            Vector3 fired = _flightDirection.sqrMagnitude > 1e-6f
                ? _flightDirection.normalized
                : -_hitNormal;

            float bite = Vector3.Dot(fired, -_hitNormal);
            if (bite >= minBiteDot) return fired;

            float t = minBiteDot > 1e-4f ? Mathf.Clamp01(bite / minBiteDot) : 0f;
            return Vector3.Slerp(-_hitNormal, fired, t).normalized;
        }

        /// <summary>
        /// How far the tip actually reaches, once the size multiplier is taken into account.
        ///
        /// <para>
        /// <see cref="hookHeadTipOffset"/> is a distance measured on the model, so scaling the model
        /// scales it too. Seating with the unscaled figure would leave a doubled dart hanging half
        /// its own length out of the wall — the sort of mismatch that looks like a modelling error
        /// and is really an arithmetic one.
        /// </para>
        /// </summary>
        private float EffectiveTipOffset => hookHeadTipOffset * Mathf.Max(0.01f, hookHeadScale);

        private void DestroyHead()
        {
            if (_head == null) return;
            Destroy(_head.gameObject);
            _head = null;
        }

        /// <summary>Aim the model down <paramref name="forward"/>, then correct for its own axis.</summary>
        private Quaternion HeadRotation(Vector3 forward)
        {
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            return Quaternion.LookRotation(forward.normalized) * Quaternion.Euler(hookHeadEuler);
        }
    }
}
