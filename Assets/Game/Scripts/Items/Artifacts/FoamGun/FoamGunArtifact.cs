// The foam gun.
//
// Hold the trigger and it lays dabs where the stream lands, fifteen a second, up to sixty-four live
// at once. Each dab swells into a sphere that welds with its neighbours into one lumpy mass you can
// stand on: a ramp up a cliff, a plug in a hole, a friend encased to the neck. Everything it makes
// has a clock.
//
// IT IS A HOSE, NOT A GUN. The landing point is traced along a BALLISTIC ARC out of the bell
// (SprayArc, shared with the portal gun) rather than along the aim ray, because the droplets have
// always fallen and the landing did not: a stream aimed anywhere but straight at a surface laid
// nothing at all, which is a hose you cannot spray into the air. Now the arc comes down, so foam
// sprayed at open sky still finds the ground some metres short of where the crosshair points. The
// reach that follows from the speed and the fall — about 7 m held level, about 20 m lobbed — is the
// range; there is no separate range knob to disagree with it.
//
// FOAM ARRIVES BEFORE IT GROWS. The lump is spawned the instant the arc is traced, but the foam a
// player can SEE is still crossing the room — so the blob and the impact are both handed the flight
// time (TravelSecondsTo) and hold until it has passed. Without it a mass swells up at the far wall
// while the jet is still leaving the bell, and the spray reads as decoration painted over something
// that had already happened.
//
// WHO DOES WHAT, AND WHY IT IS SPLIT THAT WAY.
//
//   • OnRequestUse / OnRequestHold — the OWNER, the one machine with a live camera. It reads
//     AimProvider, traces the arc to decide where this instant's dab lands, and decides whether the
//     throttle and the tank allow one at all. That verdict travels in the message; no other machine
//     re-decides it.
//   • Use / Hold — the SERVER, because a blob is a collider in the shared world and a Foamed body is
//     contested state. GameServices.World.Spawn is server-only by contract, so the item is
//     UseAuthority.Server — Owner would run Hold() on a client, where the spawn is refused outright.
//     (The design doc's "UseAuthority.Owner for the request" describes the request, which is
//     owner-side for every artifact in the game; the authority that decides a spawn cannot be.)
//   • Present / PresentHold — EVERY machine: the jet, the shutter, the sound, and the tank's gauge.
//
// WHAT MAY NOT BE WRITTEN TO. arg.A is the hotbar slot code on the press AND on every hold tick, and
// the server reads it back as its stale-slot guard, so an item that put its own flags there would be
// silently refused on the server for every slot but the matching one. The one flag this gun needs —
// "this tick lays a dab" — sits in bit 1 of B, above the low bit EquipmentController owns as the
// active flag on hold ticks.
//
// THE SERVER NEVER TAKES THE OWNER'S WORD FOR A VICTIM. The message says where the foam landed, not
// who is inside it; the server overlaps the dab itself and foams whatever bodies it finds there. A
// client that could name its target could foam a player across the map (GDC-L1-MP-0004).
//
// THE TANK IS NOT A NEW SYSTEM. The cartridge is a SupplyReservoir — the shared drain-and-refill
// reservoir every tank in the game uses, drawn by SupplyGauge on the cartridge's own gauge plate.
// Six of these artifacts carry a tank; there is one policy, and this one only tunes it on the
// prefab. Its fill is captured and replicated for free, because SupplyCharge answers "does this
// item carry a charge" by looking for exactly that component.
using SpaceGame.Core;
using SpaceGame.Gameplay;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Sprays foam that swells into solid, standable geometry, and holds whatever it lands on.
    /// </summary>
    public sealed class FoamGunArtifact : ToolItem
    {
        [Header("Spray")]
        [Tooltip("Dabs laid per second while the trigger is held. UseChannel's hold stream runs at " +
                 "15 Hz and that is a CEILING, not a starting point: a value above it lays one dab " +
                 "per tick and nothing more, so it would read as a knob that does nothing. The " +
                 "volume above 15 /s is bought with the blob's radius instead.")]
        [SerializeField, Min(0.1f)] private float dabsPerSecond = 15f;

        [Tooltip("How many of this player's blobs may stand at once. The oldest is retired to make " +
                 "room, so a long sweep dissolves behind you rather than being refused.")]
        [SerializeField, Min(1)] private int liveDabBudget = 128;

        [Tooltip("What foam sticks to. Anything else the arc passes through.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        [Tooltip("How fast foam leaves the bell, in metres per second. It is the arc's launch " +
                 "speed AND the delay before a lump swells, so the mass grows where the stream " +
                 "has actually reached. Keep it in step with the jet's own start speed in " +
                 "FoamGunSprayBuilder; the two describe the same foam and only look right when " +
                 "they agree.")]
        [SerializeField, Min(1f)] private float sprayTravelSpeed = 22f;

        [Tooltip("How hard gravity pulls the stream, as a multiple of this world's 18 m/s2. It " +
                 "is what makes this a hose rather than a gun, and it decides the reach: at 22 " +
                 "m/s and 1.4 that is about 7 m held level and 20 m lobbed at 45 degrees. Must " +
                 "match the jet's own gravityModifier in FoamGunSprayBuilder or the foam lands " +
                 "somewhere the player never saw the stream go.")]
        [SerializeField, Min(0f)] private float sprayGravity = 1.4f;

        [Tooltip("Seconds of flight before the arc is given up on and the tick lays nothing. " +
                 "Also the droplets' longest life, so the stream is watched all the way to its " +
                 "landing. 2 s covers EVERY shot the gun can make: a stream thrown straight UP " +
                 "is back on the ground after 1.81, and at 1.8 the band from 85 degrees to " +
                 "vertical laid nothing at all. That band is what makes 'spray anywhere and foam " +
                 "lands' true rather than nearly true.")]
        [SerializeField, Min(0.1f)] private float sprayFlightTime = 2f;

        [Tooltip("Corrects the landing delay for the fact that an arc is longer than the " +
                 "straight line across it. Every machine times the swell from the chord it was " +
                 "sent, so 1 makes a lobbed lump start to grow slightly early.")]
        [SerializeField, Range(1f, 1.4f)] private float flightBias = 1.08f;

        [Header("Lifetimes")]
        [Tooltip("Seconds a blob that landed on the world stands for. A ramp should still be there " +
                 "when you have climbed back down.")]
        [SerializeField, Min(1f)] private float terrainLifetime = 60f;

        [Tooltip("Seconds a blob that landed on a body stands for. Deliberately a sixth of the " +
                 "terrain figure: being stuck is a setback, not a sentence.")]
        [SerializeField, Min(1f)] private float encasementLifetime = 10f;

        [Header("Bodies")]
        [Tooltip("What the server sweeps for when a dab lands, to decide whether this blob is " +
                 "terrain or an encasement.")]
        [SerializeField] private LayerMask bodyMask = ~0;

        [Tooltip("How far outside a landed dab a body still counts as caught in it, in metres. " +
                 "The blob has not grown yet when the sweep runs, so the sweep uses its FULL " +
                 "radius plus this — a body the foam is about to swell around is already in it.")]
        [SerializeField, Min(0f)] private float catchMargin = 0.15f;

        [Header("Parts")]
        [Tooltip("The lump this lays. A spawned world object, so it MUST be a registered network " +
                 "prefab: unregistered, it exists for the host and for nobody else.")]
        [SerializeField] private FoamBlob foamBlobPrefab;

        [Tooltip("The bell: shutter, jet and loop. Found on this prefab when unset.")]
        [SerializeField] private FoamGunNozzle nozzle;

        [Tooltip("The impact half: the gob thrown back out of a landed dab, the blast off the " +
                 "muzzle on the press, and the camera kick. Found on this prefab when unset.")]
        [SerializeField] private FoamSprayFx fx;

        [Tooltip("The cartridge's reservoir — the shared SupplyReservoir, tuned on this prefab " +
                 "to the design's 0.1/s held and 0.05/s idle. Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Safety net")]
        [Tooltip("Seconds of silence after which the jet shuts itself off. Covers a release that " +
                 "never arrived — a dropped packet, or a player who disconnected mid-spray. Must " +
                 "stay comfortably above UseChannel's 0.2 s keepalive or an ordinary steady hold " +
                 "would cut itself off between two perfectly normal ticks.")]
        [SerializeField] private float holdTimeout = 0.5f;

        /// <summary>
        /// Bit 1 of <c>NetArg.B</c>: this tick lays a dab. Bit 0 is EquipmentController's active
        /// flag on hold ticks, and A is the slot code — see the file header.
        /// </summary>
        private const int DabBit = 1 << 1;

        /// <summary>Scratch for the server's catch sweep. Nothing is held between calls.</summary>
        private static readonly Collider[] Caught = new Collider[16];

        /// <summary>Owner-side throttle. The one timeline the dab rate is measured on.</summary>
        private float nextDabAt;

        /// <summary>
        /// Owner-side: did the tank agree to this pull of the trigger?
        ///
        /// <para>
        /// Asked once, at the press, and not again — which is the whole point of
        /// <see cref="SupplyReservoir.CanStart"/>. A tank that has just run dry sits below its restart
        /// threshold, and a gun that re-asked "is there anything in it" on every hold tick would
        /// lay one dab per frame's worth of trickle for as long as the trigger stayed down.
        /// </para>
        /// </summary>
        private bool armed;

        private bool spraying;
        private float lastHoldAt;

        /// <summary>
        /// Where the last tick's foam landed. The only thing a peer has to aim its stream with —
        /// see <see cref="JetDirection"/> — and it is read every frame, not only on a tick.
        /// </summary>
        private Vector3 lastLanding;

        /// <summary>
        /// Is foam actually coming out this frame — the trigger down AND the tank agreeing? Taken
        /// once a frame in <see cref="Update"/> and read by the presentation, so a held trigger on
        /// a dry tank blasts and kicks nothing. Every machine derives it from the same hold stream
        /// and its own frame time, exactly as the gauge does.
        /// </summary>
        private bool delivering;

        /// <summary>The trigger is a sweep, so the gun rides the ordinary hold stream.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed here: the jet stops when the finger comes up.
        ///
        /// An empty tank deliberately does NOT end the hold. The player keeps holding, nothing
        /// lands, and the bar on the cartridge is what tells them why — which is the whole reason
        /// the gauge is on the object in their hands rather than in a HUD.
        /// </summary>
        public override bool WantsHold => false;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            if (nozzle == null) nozzle = GetComponentInChildren<FoamGunNozzle>(true);
            if (fx == null) fx = GetComponentInChildren<FoamSprayFx>(true);
            if (tank == null) tank = SupplyReservoir.On(gameObject);
        }

        /// <summary>
        /// Hand the effects the holder's view, so the camera kick knows whose eye it is kicking.
        /// Only the local holder has a live camera, which is the whole test — see FoamSprayFx.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);

            if (fx != null) fx.SetViewer(aimProvider);
        }

        private void OnDisable()
        {
            spraying = false;
            if (nozzle != null) nozzle.SetSpraying(false);
        }

        private void Update()
        {
            // The safety net. Distinct from the release tick, which is the ordinary way a spray
            // ends: this catches a machine that simply stopped hearing from the owner.
            if (spraying && Time.time - lastHoldAt > holdTimeout) spraying = false;

            // Every machine runs the same tank off the hold stream and its own frame time, so every
            // gauge tracks with nothing on the wire. Drift near empty costs nothing, because
            // whether a dab may be laid was never this machine's decision to make.
            delivering = tank == null ? spraying : tank.Tick(Time.deltaTime, spraying);

            // The bell runs only while foam is actually coming out of it. A dry tank shuts the jet,
            // the shutter and the loop off while the trigger stays down — which, alongside the bar
            // on the cartridge, is what tells the player why nothing is landing.
            if (nozzle != null) nozzle.SetSpraying(delivering);
        }

        /// <summary>
        /// Throw the droplets the way the foam goes, every frame the trigger is down.
        ///
        /// <para>
        /// In LateUpdate because the muzzle rides the fist: aiming in Update would trail a frame
        /// behind the gun. Per FRAME rather than per hold tick because the hold stream runs at 15
        /// Hz, and a stream that only turned fifteen times a second visibly staircases behind a
        /// player who is sweeping the aim.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            if (!delivering || nozzle == null) return;

            nozzle.AimAlong(JetDirection(lastLanding));
        }

        public override void OnUnequipped(GameObject holder)
        {
            SetSpraying(false);
            if (nozzle != null) nozzle.SetSpraying(false);
            if (fx != null) { fx.Release(); fx.SetViewer(null); }

            base.OnUnequipped(holder);
        }

        // ── Owner side: the aim, the throttle and the payment ──────────────────

        /// <summary>
        /// The trigger goes down: the first dab, described by the only machine that can honestly
        /// describe it.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            // A press restarts the rhythm rather than inheriting the gap left by the last one, so
            // tapping the trigger lays a dab every time instead of swallowing every second tap.
            nextDabAt = 0f;

            // The tank's verdict on a NEW draw, taken once. See SupplyReservoir.CanStart, `armed`.
            armed = tank == null || tank.CanStart;

            DescribeDab(ref arg);
        }

        /// <summary>Owner side, fifteen times a second: where the jet is pointing now.</summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (!active) return;
            DescribeDab(ref arg);
        }

        /// <summary>
        /// Put this instant's landing place in the message, and say whether it is a dab.
        ///
        /// <para>
        /// The arc, not the aim ray. Foam falls, so where it lands is decided by
        /// <see cref="SprayArc"/> out of the bell — which is what makes spraying at open sky lay
        /// foam on the ground below the stream instead of laying nothing at all.
        /// </para>
        /// <para>
        /// It lands on whatever the arc reaches first, INCLUDING foam already laid, which is what
        /// makes a mass build up rather than spread out. Standing foam is tested by
        /// <see cref="FoamField.FirstAlong"/> rather than by its colliders — see there for why the
        /// colliders are the wrong shape for the first seconds of a lump's life.
        /// </para>
        /// <para>
        /// A miss is reported as a point with NO rotation. <see cref="NetArg.HasOrientation"/>
        /// exists precisely to tell "the sender filled this in" from "nobody did", so the miss
        /// needs no flag of its own — and every machine still gets a point to throw the jet at.
        /// </para>
        /// </summary>
        private void DescribeDab(ref NetArg arg)
        {
            arg.R = default;

            Vector3 origin = MuzzlePosition;
            Vector3 direction = AimDirection();

            // Where the stream ends up if it reaches nothing: the end of the arc, not a point
            // along the crosshair. A miss still has to look like foam falling somewhere real.
            arg.P = SprayArc.Sample(origin, direction, sprayTravelSpeed, sprayGravity,
                                    sprayFlightTime);

            // Blind to the sprayer and to whatever they are riding. The arc leaves a bell held in
            // front of the chest, so a stream thrown at your own feet — which the design wants to
            // be a way to build a step — would otherwise stop on your knees.
            Transform self = owner != null ? owner.transform : null;

            // FoamField.FirstAlong is tested alongside the colliders, and it is what makes a mass
            // build on itself: a lump's collider is a pebble until its foam has flown and stays
            // under half size for a second and a half after that, so an arc traced against the
            // colliders alone drops the whole first second of a held spray through the pile and
            // onto the ground.
            if (!SprayArc.Trace(origin, direction, sprayTravelSpeed, sprayGravity, sprayFlightTime,
                                surfaceMask, self, self != null ? self.root : null,
                                FoamField.FirstAlong,
                                out Vector3 landing, out Vector3 surface, out float _))
                return;

            arg.P = landing;
            arg.R = Quaternion.LookRotation(surface);

            // The throttle and the tank, both on the owner and nowhere else. Two machines each
            // asking their own tank "can I afford this" disagree exactly when it matters — near
            // empty — and then disagree forever about which lumps exist.
            if (!armed || Time.time < nextDabAt) return;
            if (tank != null && tank.Charge <= 0f) return;

            // A whisker under the full period. At the design's 15 /s the throttle and the hold
            // stream are the same rate, and a tick that arrives a millisecond early against an
            // exact period is a dab silently dropped — the gun would run at fourteen and change
            // for no reason a reader could see.
            nextDabAt = Time.time + 0.95f / dabsPerSecond;
            arg.B |= DabBit;
        }

        // ── Server side: the lump exists, and who is in it ─────────────────────

        /// <summary>Authority side, the press.</summary>
        protected override void Use() => LayDab(UseArg);

        /// <summary>
        /// Authority side, per tick.
        ///
        /// The spray flag is marked here as well as in <see cref="PresentHold"/> on purpose: a
        /// dedicated server never receives PresentHold, and its copy of the gun is the one whose
        /// tank reaches the save file.
        /// </summary>
        protected override void Hold(NetArg arg, bool active)
        {
            SetSpraying(active);
            if (!active) return;

            LayDab(arg);
        }

        /// <summary>
        /// Put one lump in the world, and decide whether it is terrain or an encasement.
        ///
        /// <para>
        /// The decision is a sweep the server runs itself rather than a body named in the message.
        /// It costs one overlap per dab and it means a client cannot nominate a victim
        /// (GDC-L1-MP-0004); it also catches a second player standing in the same lump, which a
        /// single named target never would.
        /// </para>
        /// </summary>
        private void LayDab(NetArg arg)
        {
            if ((arg.B & DabBit) == 0 || !arg.HasOrientation) return;

            if (foamBlobPrefab == null)
            {
                Debug.LogWarning("[FoamGun] No foam blob prefab assigned, so the trigger sprays " +
                                 "nothing. Wire it on the item prefab.", this);
                return;
            }

            bool encases = EncaseBodiesAt(arg.P);

            GameObject spawned = GameServices.World.Spawn(foamBlobPrefab.gameObject, arg.P, arg.R);
            if (spawned == null) return;

            if (!spawned.TryGetComponent(out FoamBlob blob))
            {
                Debug.LogError($"[FoamGun] '{spawned.name}' has no FoamBlob, so it will never grow " +
                               "and never expire.", spawned);
                return;
            }

            blob.Begin(owner, encases ? encasementLifetime : terrainLifetime,
                       TravelSecondsTo(arg.P));

            RetireOverBudget();
        }

        /// <summary>
        /// Foam every body standing in the dab, and say whether there was one.
        ///
        /// <para>
        /// The sprayer's own body is excluded. Encasing yourself with a jet you are holding is not
        /// a choice anybody makes on purpose, and the arc is already traced blind to your own body,
        /// so the only way to reach it is by standing in a lump laid at your feet — which the
        /// design wants to be a way to build a step, not a way to freeze solid.
        /// </para>
        /// </summary>
        private bool EncaseBodiesAt(Vector3 point)
        {
            float reach = BlobRadius + catchMargin;

            int count = Physics.OverlapSphereNonAlloc(point, reach, Caught, bodyMask,
                                                      QueryTriggerInteraction.Ignore);

            bool caught = false;
            for (int i = 0; i < count; i++)
            {
                Collider collider = Caught[i];
                if (collider == null) continue;

                // Asked of the COLLIDER's transform, not the hit's: over anything with a rigidbody
                // the latter is the body's root, so a filter written against it matches everything
                // or nothing.
                if (owner != null && collider.transform.IsChildOf(owner.transform)) continue;

                // A body, not a crate. HealthComponent is the same test the Foamed condition needs
                // for its own break-early rule, so anything this catches can also be freed.
                HealthComponent health = collider.GetComponentInParent<HealthComponent>();
                if (health == null) continue;

                // Idempotent by construction: a body already foamed has its expiry pushed back out
                // rather than being caught a second time.
                caught |= FoamBlob.Encase(health.gameObject, owner);
            }

            return caught;
        }

        /// <summary>
        /// Keep this player's live lumps inside their budget, oldest out first.
        ///
        /// Twenty-four network objects per player, several players spraying, is the first
        /// performance question this artifact raises and the budget is the answer to it
        /// (GDC-L1-PERF-0004). Enforced on the server, so the despawn reaches every machine as an
        /// ordinary spawn message.
        /// </summary>
        private void RetireOverBudget()
        {
            while (FoamField.CountFor(owner) > liveDabBudget)
            {
                FoamBlob oldest = FoamField.OldestOf(owner);
                if (oldest == null) return;

                // Despawned, never hidden: a collider that is switched off drops out of every
                // raycast and overlap in the game, so a "retired" lump would still be something the
                // aim, the ground probes and the next dab's sweep all trip over.
                oldest.Retire();
            }
        }

        // ── Every machine: the jet, the shutter and the gauge ──────────────────

        protected override void Present() => PresentSpray(UseArg, active: true, press: true);

        protected override void PresentHold(NetArg arg, bool active) =>
            PresentSpray(arg, active, press: false);

        /// <summary>
        /// One tick of spray as it looks, sounds and hits. The release — <paramref name="active"/>
        /// false — must stop it even on a machine that never saw the ticks before it.
        /// </summary>
        /// <param name="press">
        /// Is this the trigger going down? The press earns the muzzle blast and the full camera
        /// kick; a hold tick earns the small rumble. Handing both to the same call would either
        /// blast fifteen times a second or never blast at all.
        /// </param>
        private void PresentSpray(NetArg arg, bool active, bool press)
        {
            SetSpraying(active);

            if (!active)
            {
                if (fx != null) fx.Release();
                return;
            }

            lastLanding = arg.P;

            // A dry tank sprays nothing, so it blasts and kicks nothing either — the same rule the
            // bell already runs on. The press asks the tank directly rather than reading
            // `delivering`, which was last computed on a frame where the trigger was still up and
            // would therefore swallow the one blast that matters most.
            if (fx != null)
            {
                if (press)
                {
                    // The launch direction, not the line to the landing: the blast leaves the bell
                    // the way the stream does, and on a lobbed shot those are tens of degrees apart.
                    if (tank == null || tank.CanStart) fx.Press(JetDirection(arg.P));
                }
                else if (delivering)
                {
                    fx.Hold();
                }
            }

            if ((arg.B & DabBit) == 0 || !arg.HasOrientation) return;

            // arg.R is LookRotation(surface normal), so its forward IS the normal — the gob bursts
            // back out of the surface the dab stuck to rather than along the jet. It is held back
            // by the same flight time the lump waits out, so the impact, its sound and the swell
            // all happen when the foam arrives rather than when the trigger was pulled.
            if (fx != null)
                fx.Splat(arg.P, arg.R * Vector3.forward, TravelSecondsTo(arg.P));
        }

        /// <summary>
        /// The trigger is down, or it is not, and the keepalive is stamped either way.
        ///
        /// <para>
        /// Idempotent, because it is reached from the authority path, the presentation path and the
        /// timeout — and on a host from two of those inside one frame. Note that it does not touch
        /// the bell: whether foam is actually coming out is the tank's answer, taken once a frame
        /// in <see cref="Update"/>, and a trigger held on an empty tank is still a trigger held.
        /// </para>
        /// </summary>
        private void SetSpraying(bool active)
        {
            if (active) lastHoldAt = Time.time;
            spraying = active;
        }

        /// <summary>
        /// How long sprayed foam takes to reach <paramref name="point"/>.
        ///
        /// <para>
        /// Measured from the MUZZLE rather than from the player: the foam leaves the bell, and at
        /// the gun's reach the difference between the two is a visible fraction of a second. The
        /// lump and the impact both wait this out, and they must be handed the same number — a
        /// gob bursting before the mass starts to swell is the exact seam this removes.
        /// </para>
        /// <para>
        /// The chord over the speed, nudged by <see cref="flightBias"/> because an arc is longer
        /// than the straight line across it. Deliberately approximate and derived on every machine
        /// from numbers every machine already has: the owner's own trace on the wire would buy
        /// exactness that nothing here spends. What the lumps must agree on is where they are, and
        /// that IS sent.
        /// </para>
        /// </summary>
        private float TravelSecondsTo(Vector3 point) =>
            Vector3.Distance(MuzzlePosition, point) / sprayTravelSpeed * flightBias;

        /// <summary>
        /// Which way the stream is thrown. The player's look, never the bell's own forward: the
        /// gun is held in a fist rotated to the grip frame, tens of degrees off the look axis, so
        /// aiming the arc down the muzzle lands foam consistently beside the crosshair.
        /// </summary>
        private Vector3 AimDirection()
        {
            if (aimProvider != null) return aimProvider.GetAimRay().direction;
            return transform.forward;
        }

        /// <summary>
        /// Which way THIS machine throws the droplets.
        ///
        /// <para>
        /// The owner has the live aim, and it is the value the trace was made from, so its stream
        /// and its foam are the same parabola exactly. A peer has no aim to read — its copy of a
        /// remote player's AimProvider falls back to the body's forward, with no pitch in it at
        /// all — but every machine is told where the dab landed, and the launch that reaches that
        /// point is the same stream to look at. Solved rather than pointed straight at it: a
        /// straight line to a lobbed landing is not the curve the droplets fly.
        /// </para>
        /// </summary>
        private Vector3 JetDirection(Vector3 landing)
        {
            if (OwnerIsLocal()) return AimDirection();

            SprayArc.TryAimAt(MuzzlePosition, landing, sprayTravelSpeed, sprayGravity,
                              out Vector3 launch);
            return launch;
        }

        private Vector3 MuzzlePosition =>
            nozzle != null ? nozzle.MuzzlePosition : transform.position;

        /// <summary>
        /// A grown blob's radius, read off the prefab rather than copied into a field here. Two
        /// numbers that must agree are one number: the sweep looks for bodies inside the lump the
        /// blob is about to become, and a local copy would silently disagree the day the blob is
        /// retuned.
        /// </summary>
        private float BlobRadius => foamBlobPrefab != null ? foamBlobPrefab.FullRadius : 0f;

        // ── Per-instance state ─────────────────────────────────────────────────
        //
        // The tank is the only thing this gun becomes, and UsableItem already writes the sibling
        // reservoir's fill into the slot's bag for every item there is — so there is no override
        // here. maxUses stays at -1 on the prefab: the reservoir is the limit, not a charge count,
        // and a limited-use gun that refilled itself would also need OnMaxUsesReached silenced to
        // stop the inventory removing it.

        private void OnValidate()
        {
            // A timeout inside the send interval would cut a perfectly ordinary steady hold between
            // two ticks, so it is floored well clear of the keepalive rather than left to whoever
            // edits it next.
            holdTimeout = Mathf.Max(0.3f, holdTimeout);
        }
    }
}
