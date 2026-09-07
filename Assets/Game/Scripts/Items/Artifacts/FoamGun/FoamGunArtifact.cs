// The foam gun.
//
// Hold the trigger and it lays dabs along the aim ray, six a second, up to twenty-four live at once.
// Each dab swells into a sphere that welds with its neighbours into one lumpy mass you can stand on:
// a ramp up a cliff, a plug in a hole, a friend encased to the neck. Everything it makes has a clock.
//
// WHO DOES WHAT, AND WHY IT IS SPLIT THAT WAY.
//
//   • OnRequestUse / OnRequestHold — the OWNER, the one machine with a live camera. It reads
//     AimProvider, decides where this instant's dab lands, and decides whether the throttle and the
//     tank allow one at all. That verdict travels in the message; no other machine re-decides it.
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
using SpaceGame.Audio;
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
        [Tooltip("Dabs laid per second while the trigger is held. The hold stream runs at 15 Hz, " +
                 "so this throttles it down — a dab per tick would be a way to fill a chunk with " +
                 "colliders in under two seconds.")]
        [SerializeField, Min(0.1f)] private float dabsPerSecond = 6f;

        [Tooltip("How many of this player's blobs may stand at once. The oldest is retired to make " +
                 "room, so a long sweep dissolves behind you rather than being refused.")]
        [SerializeField, Min(1)] private int liveDabBudget = 24;

        [Tooltip("How far the gun can put foam, in metres. Beyond this the trigger sputters and " +
                 "nothing lands, which is what makes the bell's wide slow mouth an honest signal.")]
        [SerializeField, Min(1f)] private float sprayRange = 14f;

        [Tooltip("What foam sticks to. Anything else is a miss.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

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

        [Tooltip("The cartridge's reservoir — the shared SupplyReservoir, tuned on this prefab " +
                 "to the design's 0.1/s held and 0.05/s idle. Found on this prefab when unset.")]
        [SerializeField] private SupplyReservoir tank;

        [Header("Audio")]
        [Tooltip("One dab landing. The jet's own loop lives on the nozzle.")]
        [SerializeField] private SfxId dabSound = SfxId.PortalPaintSplat;

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
            if (tank == null) tank = SupplyReservoir.On(gameObject);
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
            bool delivering = tank == null ? spraying : tank.Tick(Time.deltaTime, spraying);

            // The bell runs only while foam is actually coming out of it. A dry tank shuts the jet,
            // the shutter and the loop off while the trigger stays down — which, alongside the bar
            // on the cartridge, is what tells the player why nothing is landing.
            if (nozzle != null) nozzle.SetSpraying(delivering);
        }

        public override void OnUnequipped(GameObject holder)
        {
            SetSpraying(false);
            if (nozzle != null) nozzle.SetSpraying(false);

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
        /// A miss is reported as a point with NO rotation. <see cref="NetArg.HasOrientation"/>
        /// exists precisely to tell "the sender filled this in" from "nobody did", so the miss
        /// needs no flag of its own — and every machine still gets a point to throw the jet at.
        /// </para>
        /// </summary>
        private void DescribeDab(ref NetArg arg)
        {
            arg.R = default;

            Ray aim = aimProvider != null
                ? aimProvider.GetAimRay()
                : new Ray(MuzzlePosition, transform.forward);

            // The end of the ray, so foam sprayed at open sky still throws a jet somewhere real.
            arg.P = aim.GetPoint(sprayRange);

            // AimProvider, never a hand-rolled Physics.Raycast: this ray already leaves the eye of
            // whatever view the player is actually looking through, and is already filtered of
            // their own body and of the machine they are riding.
            if (aimProvider == null ||
                !aimProvider.TryGetAimHit(sprayRange, surfaceMask, out RaycastHit hit))
                return;

            arg.P = hit.point;
            arg.R = Quaternion.LookRotation(hit.normal);

            // The throttle and the tank, both on the owner and nowhere else. Two machines each
            // asking their own tank "can I afford this" disagree exactly when it matters — near
            // empty — and then disagree forever about which lumps exist.
            if (!armed || Time.time < nextDabAt) return;
            if (tank != null && tank.Charge <= 0f) return;

            nextDabAt = Time.time + 1f / dabsPerSecond;
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

            blob.Begin(owner, encases ? encasementLifetime : terrainLifetime);

            RetireOverBudget();
        }

        /// <summary>
        /// Foam every body standing in the dab, and say whether there was one.
        ///
        /// <para>
        /// The sprayer's own body is excluded. Encasing yourself with a jet you are holding is not
        /// a choice anybody makes on purpose, and the aim ray already looks past your own body, so
        /// the only way to reach it is by spraying your feet — which the design wants to be a way
        /// to build a step, not a way to freeze solid.
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

        protected override void Present() => PresentSpray(UseArg, active: true);

        protected override void PresentHold(NetArg arg, bool active) => PresentSpray(arg, active);

        /// <summary>
        /// One tick of spray as it looks and sounds. The release — <paramref name="active"/> false
        /// — must stop it even on a machine that never saw the ticks before it.
        /// </summary>
        private void PresentSpray(NetArg arg, bool active)
        {
            SetSpraying(active);
            if (!active) return;

            if (nozzle != null) nozzle.AimAt(arg.P);

            if ((arg.B & DabBit) != 0 && arg.HasOrientation && dabSound != SfxId.None)
                Sfx.Play(dabSound, arg.P);
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
