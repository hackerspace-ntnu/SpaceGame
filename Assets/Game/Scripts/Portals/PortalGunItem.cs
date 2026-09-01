// The spray can.
//
// It used to throw one blob and open one fixed ellipse where the blob landed. It is now held down
// and swept: a jet of coloured paint comes out of the nozzle, the aperture opens on the first blob
// to land, and it grows for as long as the trigger is down and there is paint in the barrel. The
// hole is the paint — see PortalStencil.
//
// It is an ordinary artifact and rides the ordinary Use/Present split, plus the ordinary hold
// stream, so it replicates for the same reason every other artifact does and needs no sync
// component of its own:
//
//   • OnRequestUse — owner-side, the one machine holding a camera. Picks the barrel and describes
//     the first blob. No peer could recompute that: their copy of a remote player has an
//     AimProvider with no live camera behind it.
//   • OnRequestHold — owner-side, fifteen times a second, describing where the jet is pointing now.
//   • Present / PresentHold — every machine. Runs the jet and lays the paint from the message.
//     Both apertures therefore exist on every machine with the same outline, which is what lets a
//     peer see somebody else's portals and walk through them.
//
// THE SHAPE NEVER GOES ON THE WIRE. Each tick carries one point, and every machine builds the same
// stencil by laying the same blobs in the same order off a reliable, ordered stream. Sending an
// outline would be both larger and less reliable than sending the gesture that produced it.
//
// WHAT MAY NOT BE WRITTEN TO. arg.A is the hotbar slot index on the press AND on every hold tick,
// and the server reads it back as its stale-slot guard — a gun that used it for its own flags
// would be silently refused for every slot but the first two, on the server only. The gun's own
// state is packed into the upper bits of arg.B (the barrel, the grow flag, and the dab count the
// owner paid for), whose low bit EquipmentController owns on hold ticks as the active flag.
//
// THE OWNER PAYS FOR THE PAINT, alone. The tank is simulated per machine off local frame times —
// gun instances are created at different moments, a late joiner's copy spawns full — so two
// machines asking their own tank "can I afford this dab" disagree exactly when it matters, near
// empty, and then disagree forever about which dabs exist. The owner's verdict travels in the
// message and every machine applies it; the local tanks are debited identically and serve only
// the gauge on the gun.
//
// Authority is Owner because a portal shot changes nothing the server arbitrates — no damage, no
// spawn, no contested resource — and routing it through the server would put a round trip inside
// the feel of the trigger.
//
// ONE TRIGGER, TWO BARRELS. Which barrel a spray comes out of is decided by where it STARTS: on
// your own paint it tops that aperture up in its own colour, on bare wall it opens the next
// barrel. The cursor that decides "next" lives on PortalPair, with the portals, not here — see
// there for why, and note that the spray session lives there for exactly the same reason.
using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    using SpaceGame.Portals;

    public sealed class PortalGunItem : ToolItem
    {
        [Header("Jet")]
        [Tooltip("Metres per second the paint leaves the nozzle. With gravity on, this is what sets the reach: about 17 m lobbed at 45 degrees, nearer 9 held level.")]
        [SerializeField] private float jetSpeed = 13f;

        [Tooltip("How hard gravity pulls the stream. 1 is real gravity, which is what makes it a hose rather than a gun.")]
        [SerializeField] private float jetGravity = 1f;

        [Tooltip("Seconds of flight before the stream is given up on. Also the droplets' lifetime.")]
        [SerializeField] private float jetFlightTime = 1.6f;

        [Tooltip("Corrects the landing delay for the fact that an arc is longer than the straight line across it. 1 lands paint slightly before the droplets arrive.")]
        [SerializeField, Range(1f, 1.4f)] private float flightBias = 1.08f;

        [Tooltip("Radius of one blob of paint on the wall, in metres. A single tap is a hole twice this across; sweeping is how you get one you can run through.")]
        [SerializeField] private float dabRadius = 0.62f;

        [Tooltip("Layers paint sticks to. Anything else splashes and is not part of an aperture.")]
        [SerializeField] private LayerMask surfaceMask = ~0;

        [Tooltip("How far outside one of your own apertures the jet may start and still top it up rather than opening the other barrel.")]
        [SerializeField] private float growMargin = 0.5f;

        [Tooltip("Seconds an aperture stays open before it irises shut. Both barrels. 0 would mean forever, which this gun does not offer — a portal you cannot get rid of is a hole in the level.")]
        [SerializeField] private float portalLifetime = 20f;

        [Header("Parts")]
        [Tooltip("Where the jet leaves the horn. Falls back to this transform.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("The aperture prefab, spawned once per barrel and then moved.")]
        [SerializeField] private Portal portalPrefab;

        [Tooltip("The jet of paint. Emits while the trigger is held, on every machine.")]
        [SerializeField] private ParticleSystem jet;

        [Tooltip("Lit while spraying, tinted to the barrel. Optional.")]
        [SerializeField] private Light muzzleLight;

        [Tooltip("The coat of paint left where each blob lands. Optional.")]
        [SerializeField] private PortalSplat splat;

        [Tooltip("Radius of a splat on the wall, in metres.")]
        [SerializeField] private float splatRadius = 0.75f;

        [Tooltip("How many splats may be on the world at once from this gun. The oldest goes first.")]
        [SerializeField] private int maxSplats = 28;

        [Tooltip("Shaken while paint is coming out of it. Optional.")]
        [SerializeField] private Transform nozzle;

        [SerializeField] private float nozzleShudder = 0.012f;
        [SerializeField] private float nozzleShudderSpeed = 26f;

        [Header("Reservoirs")]
        [Tooltip("The renderer carrying the fluid materials. On the shipped model that is the whole gun body, and the two fluids are submeshes of it.")]
        [SerializeField] private Renderer bodyRenderer;

        [SerializeField] private string primaryMaterialName = "Mat_Emissive_Portal_Orange";
        [SerializeField] private string secondaryMaterialName = "Mat_Emissive_Portal_Blue";

        [Tooltip("Tank spent per blob of paint. A full tank is 1, so 0.045 buys about twenty-two blobs — a second and a half of sweeping.")]
        [SerializeField, Range(0.005f, 0.25f)] private float paintPerDab = 0.045f;

        [Tooltip("Tank refilled per second, per barrel.")]
        [SerializeField] private float rechargePerSecond = 0.30f;

        [Header("Colour")]
        [Tooltip("The two apertures, told apart by hue so a player can identify either end from across a room — including through the other one.")]
        [SerializeField] private Color primaryColour = new Color(1.00f, 0.54f, 0.12f);
        [SerializeField] private Color secondaryColour = new Color(0.18f, 0.72f, 1.00f);

        [Header("Audio")]
        [SerializeField] private SfxId spraySound = SfxId.PortalSprayLoop;
        [SerializeField] private SfxId splatSound = SfxId.PortalPaintSplat;

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int AgitationId = Shader.PropertyToID("_Agitation");

        /// <summary>The swing is the player's own; nothing here is the server's to arbitrate.</summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        /// <summary>The trigger is a spray, so the item rides the hold stream. See UsableItem.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Nothing self-timed here: the jet stops when the finger comes up.
        ///
        /// A dry barrel deliberately does NOT end the hold. The player keeps holding, the jet
        /// sputters, and the gauge on the gun is what tells them why — which is the whole reason
        /// the reservoir is on the object in their hands rather than in a HUD.
        /// </summary>
        public override bool WantsHold => false;

        /// <summary>
        /// The aperture prefab this gun spawns.
        ///
        /// Exposed for the save system, which has to re-open a player's portals on load and has no
        /// gun to ask: the item is not equipped when a world is loaded, and may never be again in
        /// that session. Reading it off the gun's own prefab is what keeps "which aperture prefab is
        /// this game's" in exactly one place — the inspector field above — rather than duplicating
        /// the answer into a saver that would then silently disagree the day somebody re-authors it.
        /// </summary>
        public Portal PortalPrefab => portalPrefab;

        /// <summary>One tick's worth of paint, in the air.</summary>
        private struct Landing
        {
            public float At;
            public Vector3 Point;
            public Quaternion Rotation;

            /// <summary>How many blobs this tick is laid as, or 0 for paint that stuck to nothing.</summary>
            public int Steps;
        }

        private readonly float[] charge = { 1f, 1f };
        private readonly float[] agitation = { 0f, 0f };
        private readonly int[] fluidSlot = { -1, -1 };

        private readonly List<Landing> pending = new List<Landing>();

        /// <summary>Splats this gun has left on the world, oldest first, so the count can be capped.</summary>
        private readonly Queue<PortalSplat> splats = new Queue<PortalSplat>();

        private readonly LoopingEmitter jetSound = new LoopingEmitter();

        private MaterialPropertyBlock fluidBlock;
        private Vector3 nozzleRest;
        private bool spraying;
        private int sprayBarrel = PortalPair.Primary;
        private Vector3 lastAim;

        /// <summary>Set when a tick's paint stuck to nothing, so the next one does not bridge the gap.</summary>
        private bool strokeBroken;

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            fluidBlock = new MaterialPropertyBlock();
            if (nozzle != null) nozzleRest = nozzle.localPosition;
            ResolveFluidSlots();
        }

        private void OnDisable()
        {
            // A gun destroyed mid-spray — a hotbar change is the ordinary way — must not leave the
            // player's pair thinking a stroke is still in progress, or the next press would be
            // treated as a continuation of a session no object is driving any more.
            StopSpraying();

            // Reachable from OnDisable and OnDestroy both: a loop cleaned up in only one of them
            // leaks whenever the game exits through the other.
            jetSound.Stop(false);
        }

        private void OnDestroy() => jetSound.Stop(false);

        /// <summary>
        /// Find which submesh each fluid is, by material NAME.
        ///
        /// Not by index. The shipped FBX carries twelve materials in the order
        /// the Blender script declared them, but Unity's importer is free to
        /// reorder or drop unused slots, and a hard-coded index that silently
        /// becomes "the chrome bottle" would make the whole gun pulse orange
        /// with no error anywhere. Names are what the model actually promises.
        /// </summary>
        private void ResolveFluidSlots()
        {
            fluidSlot[PortalPair.Primary] = -1;
            fluidSlot[PortalPair.Secondary] = -1;

            if (bodyRenderer == null) return;

            Material[] materials = bodyRenderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;

                string materialName = materials[i].name;
                if (materialName.Contains(primaryMaterialName)) fluidSlot[PortalPair.Primary] = i;
                else if (materialName.Contains(secondaryMaterialName)) fluidSlot[PortalPair.Secondary] = i;
            }
        }

        private void Update()
        {
            for (int i = 0; i < 2; i++)
            {
                charge[i] = Mathf.Min(1f, charge[i] + rechargePerSecond * Time.deltaTime);

                // Fast attack, slow release: the boil should hit on the frame
                // the trigger goes and then subside, which a symmetric lerp
                // turns into a gentle throb.
                agitation[i] = Mathf.Max(0f, agitation[i] - Time.deltaTime * 2.2f);
            }

            PushFluidState();
            LandDuePaint();
            ShudderNozzle();
        }

        // ── The reservoirs ─────────────────────────────────────────────────────

        /// <summary>How full <paramref name="barrel"/> is, 0 to 1.</summary>
        public float ChargeOf(int barrel) =>
            barrel >= 0 && barrel < charge.Length ? charge[barrel] : 0f;

        /// <summary>
        /// Could <paramref name="barrel"/> pay for <paramref name="dabs"/> blobs right now?
        ///
        /// All or nothing. Half a stroke is no easier to reason about than none of it, and a
        /// partial payment would let a fast sweep — which is laid as several interpolated blobs —
        /// buy area at a discount.
        ///
        /// Asked on the OWNER only, from OnRequestUse/OnRequestHold, and the answer travels in the
        /// message — see the file header for why no other machine's tank may be consulted.
        /// </summary>
        public bool CanSpend(int barrel, int dabs)
        {
            if (barrel < 0 || barrel >= charge.Length) return false;
            return charge[barrel] >= paintPerDab * Mathf.Max(dabs, 1);
        }

        /// <summary>
        /// Deduct <paramref name="dabs"/> blobs from <paramref name="barrel"/>, clamping at empty.
        ///
        /// Unconditional: it runs on every machine for every dab the owner paid for, so the gauges
        /// track. The clamp is what absorbs the drift a peer's tank accumulates from its own frame
        /// times — the shape is already settled by the owner's verdict, so a peer's gauge reading
        /// a hair low costs nothing.
        /// </summary>
        public void Spend(int barrel, int dabs)
        {
            if (barrel < 0 || barrel >= charge.Length) return;

            charge[barrel] = Mathf.Max(0f, charge[barrel] - paintPerDab * Mathf.Max(dabs, 1));
            agitation[barrel] = 1.6f;
        }

        // ── Owner side: aim ────────────────────────────────────────────────────

        /// <summary>
        /// The trigger goes down. Picks the barrel and describes the first blob of paint.
        ///
        /// The barrel is chosen here, on the shooter's machine, and travels in B along with the
        /// grow flag. Every other machine reads them back out rather than deciding for itself:
        /// two machines each keeping their own idea of "which barrel is next" would drift apart
        /// the first time one of them dropped a message, and then one player's orange portal would
        /// be another player's blue.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            PortalPair pair = PortalPair.Of(owner);

            bool grow = false;
            int barrel = pair != null ? pair.PeekBarrel() : PortalPair.Primary;

            // Along the ARC, not the crosshair. Asking where the player is pointing would top up a
            // portal across the room that the stream cannot actually reach.
            if (pair != null &&
                PortalJet.Trace(MuzzlePosition(), AimDirection(), jetSpeed, jetGravity,
                                jetFlightTime, ~0, out RaycastHit look, out float _))
                barrel = pair.ChooseSprayBarrel(look.point, growMargin, out grow);

            AimPaint(ref arg);

            // The press is always a single blob — Present seeds the stroke at its own landing
            // point, so there is no distance to interpolate over. Whether that blob is AFFORDABLE
            // is decided here and travels with the message; see the file header.
            int paid = arg.HasOrientation && CanSpend(barrel, 1) ? 1 : 0;

            arg.B = PackBarrel(barrel, grow, paid);

            // Only a spray that opens a NEW aperture moves the cursor on. Topping up the one you
            // are already looking at must not burn the other barrel — see PortalPair.PeekBarrel.
            if (!grow) pair?.CommitBarrel(barrel);
        }

        /// <summary>
        /// Owner side, every tick: where this instant's paint is going, and how many dabs of it
        /// this machine's tank — the only tank with any authority — is paying for.
        /// </summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (!active) return;
            AimPaint(ref arg);

            // Paint that stuck to nothing costs nothing; the miss is the message.
            if (!arg.HasOrientation) return;

            // The same measurement SchedulePaint takes, against the presentation state as it
            // stood BEFORE this tick — which is exactly the state every peer holds when this
            // message reaches them, the stream being reliable and ordered.
            int steps = strokeBroken || !spraying
                ? 1
                : PortalStencil.StrokeSteps(Vector3.Distance(lastAim, arg.P), dabRadius);

            // The low bit of B is EquipmentController's active flag on hold ticks; the paid dab
            // count lives above it, in the same bits PackBarrel uses on a press.
            if (CanSpend(sprayBarrel, steps)) arg.B |= steps << 2;
        }

        /// <summary>
        /// Put one blob's landing place in the message, or leave the rotation empty for a miss.
        ///
        /// A miss is reported as a point with NO rotation. NetArg.R is all-zero in a default
        /// message and <see cref="NetArg.HasOrientation"/> exists precisely to tell "the sender
        /// filled this in" from "nobody did", so the miss case needs no flag of its own — and
        /// every machine still gets the point, and still splashes paint on whatever is there.
        /// </summary>
        private void AimPaint(ref NetArg arg)
        {
            arg.R = default;

            Vector3 origin = MuzzlePosition();
            Vector3 direction = AimDirection();

            // Where the stream ends up if it hits nothing: the end of the arc, not a point along
            // the crosshair. A miss still has to look like paint falling somewhere real.
            arg.P = PortalJet.Sample(origin, direction, jetSpeed, jetGravity, jetFlightTime);

            if (!PortalJet.Trace(origin, direction, jetSpeed, jetGravity, jetFlightTime, ~0,
                                 out RaycastHit hit, out float _))
                return;

            arg.P = hit.point;

            // The LOOK direction, not the arc's, and not the surface normal. It only matters on
            // floors and ceilings, where "up" on the surface is undefined and the aperture is
            // rolled to face the shooter — which is what makes a portal in the floor drop you out
            // the way you were already facing. Handing it the normal there leaves the roll
            // undefined and every floor portal snapped to world forward.
            if (!PortalPlacement.FitDab(hit, surfaceMask, direction,
                                        out Vector3 position, out Quaternion rotation))
                return;

            arg.P = position;
            arg.R = rotation;
        }

        // ── Every machine: the jet, and the paint landing ──────────────────────

        protected override void Present()
        {
            sprayBarrel = UnpackBarrel(UseArg.B);
            spraying = true;
            strokeBroken = false;

            // The impact point, NOT the muzzle. The stroke length between two ticks is what decides
            // how many blobs a tick is laid as, and seeding this with the muzzle made the FIRST
            // blob of every spray measure the whole distance to the wall — so a press at anything
            // past arm's length was billed for eight blobs and drained a third of the tank before
            // any paint had landed.
            lastAim = UseArg.P;

            PortalPair.Of(owner)?.BeginSpray(sprayBarrel, UnpackGrow(UseArg.B));

            SetJet(true);
            SchedulePaint(UseArg);
        }

        protected override void PresentHold(NetArg arg, bool active)
        {
            if (!active)
            {
                StopSpraying();
                return;
            }

            SchedulePaint(arg);
        }

        /// <summary>
        /// Queue one tick's paint to land when it gets there.
        ///
        /// The delay is distance over speed, timed locally on every machine from numbers every
        /// machine already has — the same trick the thrown blob used before this gun became a
        /// spray can. It is what makes the jet look like it is doing the painting rather than
        /// trailing behind a hole that has already opened.
        /// </summary>
        private void SchedulePaint(NetArg arg)
        {
            if (!spraying) return;

            // Timed locally on each machine rather than sent, and deliberately approximate: the
            // chord over the speed, nudged by flightBias because the arc is longer than the line
            // across it. Exactness would need the owner's own trace on the wire, and buys nothing —
            // the dabs land in the same ORDER everywhere off a reliable stream, and their positions
            // are identical. All this decides is when each machine's own droplets appear to arrive.
            float flight = Vector3.Distance(MuzzlePosition(), arg.P)
                         / Mathf.Max(jetSpeed, 1f) * flightBias;

            // Paint that stuck to nothing still splashes. It simply never becomes an aperture — and
            // it breaks the stroke, so whatever the jet finds next starts a fresh one.
            if (!arg.HasOrientation)
            {
                strokeBroken = true;

                pending.Add(new Landing
                {
                    At = Time.time + flight,
                    Point = arg.P,
                    Rotation = Quaternion.identity,
                    Steps = 0,
                });

                return;
            }

            // How many dabs this tick is worth — and whether the tank covered them — arrived in
            // the message. The OWNER measured and paid; a peer consulting its own tank here is the
            // bug this replaces, two machines disagreeing near empty about which dabs exist. The
            // stroke state is still advanced on every machine so the owner's next measurement and
            // the peers' stay in step.
            int paid = UnpackPaid(arg.B);

            lastAim = arg.P;
            strokeBroken = false;

            if (paid <= 0) return;

            Spend(sprayBarrel, paid);

            pending.Add(new Landing
            {
                At = Time.time + flight,
                Point = arg.P,
                Rotation = arg.R,
                Steps = paid,
            });
        }

        /// <summary>
        /// Lay whatever paint has arrived. Runs on every machine, from the same queue.
        ///
        /// Strictly in the order it was sprayed. Flight times are not monotonic — a sweep towards
        /// the wall schedules a later dab with a SHORTER flight — and the stencil's merging is
        /// order-dependent, so releasing dabs by arrival time let two machines whose frames cut
        /// the queue differently lay the same dabs in different orders and drift apart. A dab that
        /// would land early simply waits for the one sprayed before it.
        /// </summary>
        private void LandDuePaint()
        {
            while (pending.Count > 0)
            {
                Landing landing = pending[0];
                if (Time.time < landing.At) break;

                pending.RemoveAt(0);

                // The rotation's forward IS the surface normal — PortalPlacement.FitDab builds it
                // that way — so a landing that stuck already knows which way the wall faces. One
                // that did not stick has no wall, and the splat is laid facing the shooter.
                Vector3 normal = landing.Steps > 0
                    ? landing.Rotation * Vector3.forward
                    : (MuzzlePosition() - landing.Point).normalized;

                Splash(landing.Point, normal, landing.Steps > 0);

                if (landing.Steps <= 0) continue;

                PortalPair pair = PortalPair.Of(owner);
                if (pair == null || portalPrefab == null) continue;

                pair.LayDab(portalPrefab, landing.Point, landing.Rotation, dabRadius, landing.Steps,
                            sprayBarrel == PortalPair.Primary ? primaryColour : secondaryColour,
                            portalLifetime, HostBehind(landing.Point, landing.Rotation));
            }
        }

        /// <summary>
        /// The wall a blob landed on.
        ///
        /// Re-resolved on each machine rather than sent, because a Collider cannot travel in a
        /// message. Probing behind the paint finds the same wall the shooter aimed at, and a null
        /// answer only costs traversal its collision pass-through.
        /// </summary>
        private Collider HostBehind(Vector3 point, Quaternion rotation)
        {
            Vector3 normal = rotation * Vector3.forward;

            return Physics.Raycast(point + normal * 0.2f, -normal, out RaycastHit hit, 0.6f,
                                   surfaceMask, QueryTriggerInteraction.Ignore)
                ? hit.collider
                : null;
        }

        /// <summary>Shut the jet down and let go of the session. Safe to call twice.</summary>
        private void StopSpraying()
        {
            if (!spraying) return;

            spraying = false;
            SetJet(false);

            // GetComponent, never PortalPair.Of: this runs during teardown, and Of() would add a
            // fresh PortalPair to a holder that is on its way out.
            if (owner != null && owner.TryGetComponent(out PortalPair pair)) pair.EndSpray();
        }

        // ── The barrel and the payment, packed into one int ────────────────────
        //
        // Bit 0 is the barrel on a press (EquipmentController's active flag on a hold tick),
        // bit 1 says this spray tops up an aperture that is already open rather than placing a
        // new one, and bits 2–5 are how many dabs the owner's tank paid for — 0 for a dry tick.
        // All in B because A is the hotbar slot index and the server reads it back — see the
        // file header.

        private static int PackBarrel(int barrel, bool grow, int paidSteps) =>
            (barrel & 1) | (grow ? 2 : 0) | (Mathf.Clamp(paidSteps, 0, 15) << 2);

        private static int UnpackBarrel(int packed) => packed & 1;

        private static bool UnpackGrow(int packed) => (packed & 2) != 0;

        /// <summary>How many dabs the owner paid for this tick, or 0 for a refused one.</summary>
        private static int UnpackPaid(int packed) => (packed >> 2) & 15;

        // ── Presentation ───────────────────────────────────────────────────────

        private void PushFluidState()
        {
            if (bodyRenderer == null) return;

            for (int i = 0; i < 2; i++)
            {
                int slot = fluidSlot[i];
                if (slot < 0) continue;

                bodyRenderer.GetPropertyBlock(fluidBlock, slot);
                fluidBlock.SetFloat(FillId, charge[i]);
                fluidBlock.SetFloat(AgitationId, agitation[i]);
                bodyRenderer.SetPropertyBlock(fluidBlock, slot);
            }
        }

        /// <summary>Start or stop the jet, on every machine. Purely presentation.</summary>
        private void SetJet(bool on)
        {
            Color colour = sprayBarrel == PortalPair.Primary ? primaryColour : secondaryColour;

            if (jet != null)
            {
                ParticleSystem.MainModule main = jet.main;
                main.startColor = colour;

                if (on) jet.Play(true);
                else jet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (muzzleLight != null)
            {
                muzzleLight.color = colour;
                muzzleLight.enabled = on;
            }

            // A LOOP, not a one-shot. The catalogue points the jet at a sustained event, and
            // starting one of those through Sfx.Play would leave it running for the rest of the
            // session with nothing holding a handle to stop it.
            if (on) { if (spraySound != SfxId.None) jetSound.Play(spraySound, gameObject); }
            else jetSound.Stop();
        }

        /// <summary>
        /// Paint landing on a surface. Smaller and quieter when it stuck to nothing an aperture
        /// could be cut into, which is how a miss reads without needing to be announced.
        /// </summary>
        private void Splash(Vector3 point, Vector3 normal, bool stuck)
        {
            if (splatSound != SfxId.None) Sfx.Play(splatSound, point);

            if (splat == null) return;

            // Oldest first. A hose lays fifteen of these a second, and without a ceiling a long
            // spray leaves a hundred quads on the wall waiting to dry.
            while (splats.Count >= Mathf.Max(maxSplats, 1))
            {
                PortalSplat oldest = splats.Dequeue();
                if (oldest != null) Destroy(oldest.gameObject);
            }

            PortalSplat spawned = Instantiate(splat);
            spawned.Place(point, normal,
                          sprayBarrel == PortalPair.Primary ? primaryColour : secondaryColour,
                          splatRadius * (stuck ? 1f : 0.6f),
                          PortalSplat.SeedFor(point));

            splats.Enqueue(spawned);
        }

        /// <summary>
        /// Shake the nozzle while paint is coming out of it.
        ///
        /// A local offset rather than an animation clip, because the gun's Animator belongs to the
        /// hold pose, and driving a second layer for a shudder would put a rig dependency on an
        /// item that several artifacts do not have one for at all.
        /// </summary>
        private void ShudderNozzle()
        {
            if (nozzle == null) return;

            if (!spraying)
            {
                nozzle.localPosition =
                    Vector3.Lerp(nozzle.localPosition, nozzleRest, Time.deltaTime * 12f);
                return;
            }

            float t = Time.time * nozzleShudderSpeed;
            var offset = new Vector3(Mathf.PerlinNoise(t, 0f) - 0.5f,
                                     Mathf.PerlinNoise(0f, t) - 0.5f,
                                     0f) * nozzleShudder;

            nozzle.localPosition = nozzleRest + offset;
        }

        private Vector3 MuzzlePosition() =>
            muzzle != null ? muzzle.position : transform.position;

        /// <summary>
        /// Where the jet is thrown.
        ///
        /// The muzzle's own forward, not the aim ray, so the paint leaves the horn rather than the
        /// player's eye — but only when a muzzle is wired. The landing point is already decided
        /// either way, so this only affects the look of the jet, which is exactly what it should
        /// affect.
        /// </summary>
        private Vector3 AimDirection()
        {
            // The player's look direction, NOT the muzzle's forward. A hose is aimed by where you
            // point, and the muzzle's own axis on the shipped model is off the look axis by enough
            // that using it made the stream land consistently beside the crosshair.
            if (aimProvider != null) return aimProvider.GetAimRay().direction;
            if (muzzle != null) return muzzle.forward;
            return transform.forward;
        }
    }
}
