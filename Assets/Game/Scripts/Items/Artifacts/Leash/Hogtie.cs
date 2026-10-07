// Rope round a body that is already on the ground, so it cannot get back up.
//
// The user asked for this in one sentence — "i want to be able to use the leash to tie him up, max
// duration 2 mins" — and then chose what ends it early: struggling out, far slower than a net, plus
// another player cutting you loose. Those three sentences are the whole design, and the shape they
// produce is the net's, with three deliberate differences:
//
//   * the pool is four times as deep (120 s against the net's 30) and is the thing that makes a tie
//     read as slow rather than a smaller struggle multiplier — see the derivation below;
//   * a tie is a FOLLOW-UP, not an opener: it refuses anybody who is still on their feet, so it
//     costs the tier a second action and a rope on top of whatever put the target down;
//   * the rope is spent. The leash item is consumed by the tie and given back as a pickup at the
//     body when the tie ends, by any route.
//
// It does NOT reuse SnaredBody, and the reason is the pool. A net's pool is SHARED between every
// captive under one net — SnareIntegrity's class summary says why — while a tie is one rope round
// one body and its pool belongs to that body alone. Folding them together would make the net's
// wide-shot trade-off apply to a thing that cannot be wide.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// One tied body: the hold that keeps it down, its own pool, and the struggle that spends it.
    ///
    /// <para>
    /// Added on demand rather than authored on a prefab, because any body can be tied at any time —
    /// the same shape and reasoning as <see cref="SnaredBody.Ensure"/>. It has no Awake and must
    /// not grow one: Unity does not raise Awake for an AddComponent outside play mode, so anything
    /// cached there would be null in an EditMode test and in any editor tooling that lands a tie.
    /// </para>
    ///
    /// <para><b>Authority.</b> Split exactly the way <c>SnareCatch</c> splits it. Every machine
    /// holds the body — a peer that skipped the hold would watch a tied player stand up and walk
    /// about while every other machine sees them on the floor — and one machine decides when it
    /// ends. What is server-side is the pool, the meter that drains it and the announcement; what
    /// is everywhere is the claim on the ragdoll.</para>
    ///
    /// <para><b>The multiplier is derived, and the naive figure is 10% out.</b>
    /// <see cref="SnareIntegrity.Drain"/> spends the pool at
    /// <c>max(IdleRotShare, mass / ReferenceLoad)</c> per second. A tie presents
    /// <c>ReferenceLoad · (1 + multiplier · level)</c>, so the rate is <c>1 + multiplier · level</c>
    /// — always above the 0.25 idle floor, which is therefore never the binding term for a tie.
    /// Time to empty is <c>HoldSeconds / (1 + multiplier · L̄)</c>, where <c>L̄</c> is the
    /// TIME-AVERAGE level, not the peak.
    /// <br/>
    /// The trap is assuming <c>L̄ = 1</c> for a perfect struggle. It is not:
    /// <see cref="SnareStruggleMeter.Push"/> clamps the level with <c>Mathf.Min(1, …)</c>, so at
    /// the 2.5 Hz cap the level is a sawtooth that touches 1 at each accepted push and decays to
    /// <c>e^(-cooldown/decay) = e^(-1/3) = 0.7165</c> before the next. Its average over one
    /// cooldown is <c>decay · (1 - e^(-cooldown/decay)) / cooldown = 1.2 · 0.28347 / 0.4 =
    /// 0.8504</c>.
    /// <br/>
    /// Solving <c>120 / (1 + m · 0.8504) = 45</c> gives <c>m = (120/45 - 1) / 0.8504 = 1.96</c>.
    /// Assuming <c>L̄ = 1</c> instead gives 1.667, which really delivers 49.7 s — ten per cent long,
    /// silently. Measured against the real classes stepped at 1/60 s, 1.96 lands on 45.2 s (the
    /// remainder is the meter climbing from zero over the first few seconds, which no steady-state
    /// algebra can see). The same arithmetic explains the net: its authored multiplier of 2 is
    /// documented as "about 10 s" against a 30 s pool and actually measures 11.45 s.
    /// <br/>
    /// Note what that arithmetic also says: at the net's own multiplier of 2 a 120 s tie would
    /// already break in 44.6 s. <b>A tie is slow because its pool is four times deeper, not because
    /// its multiplier is smaller</b> — 1.96 against 2.00 is a rounding difference and nothing to
    /// lean a design on. Anyone retuning this should move <c>HoldSeconds</c>, not the multiplier.
    /// </para>
    ///
    /// <para><b>Nothing here is persisted, deliberately.</b> This component implements no
    /// <c>ISaveable</c> and is never on a prefab, so <c>SaveableEntity.CollectSavers</c> — a
    /// <c>GetComponents&lt;ISaveable&gt;()</c> walk — cannot see it and no key of its own can reach
    /// a save file. That is the whole design and not an omission: a quit-time autosave that
    /// captured a tie would reload a world in which a player cannot move, with nothing in the log
    /// to say why. Untying by loading is a far better failure than that.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class Hogtie : MonoBehaviour
    {
        private HogtieSettings settings;
        private InventoryItem rope;

        /// <summary>The pool this tie spends. One rope, one body, one pool — see the file header.</summary>
        private SnareIntegrity integrity;

        /// <summary>
        /// The AUTHORITY's meter: what the struggle costs the ropes.
        ///
        /// Never the same object as <see cref="input"/>'s throttle below, and the duplication is
        /// the design — the tied player's own meter runs on THEIR machine and decides what they
        /// may send, this one runs on the server and decides what it is worth. Sharing one would
        /// mean putting a level on the wire, which is handing the client the escape
        /// (GDC-L1-MP-0004).
        /// </summary>
        private SnareStruggleMeter meter;

        /// <summary>The tied player's own send throttle, on their machine only.</summary>
        private SnareStruggleMeter sendMeter;

        /// <summary>Their keys, and the memory of which way they last pushed. Shared with the net.</summary>
        private readonly SnareStruggleReader input = new SnareStruggleReader();

        private PlayerRagdoll player;
        private AgentRagdoll creature;
        private HealthComponent health;

        private bool bound;
        private bool authoritative;

        /// <summary>Is this body tied right now?</summary>
        public bool IsBound => bound;

        /// <summary>1 when the ropes are fresh, 0 when they are about to give. For the HUD and tests.</summary>
        public float HoldFraction => integrity?.Fraction ?? 0f;

        /// <summary>How hard this body is fighting, 0-1, as the AUTHORITY scores it.</summary>
        public float StruggleLevel => meter?.Level ?? 0f;

        public static Hogtie Ensure(GameObject body)
        {
            if (body == null) return null;

            return body.TryGetComponent(out Hogtie existing) ? existing : body.AddComponent<Hogtie>();
        }

        /// <summary>
        /// The body a hit belongs to — the object carrying the ragdoll adapter, not the collider.
        ///
        /// <para>
        /// <b>This is not <c>GetComponentInParent&lt;Rigidbody&gt;()</c>, which is what the leash's
        /// ordinary anchor path uses, and the difference is the whole feature.</b> A tie may only
        /// land on a body that is already limp — and a limp body is a built ragdoll, whose every
        /// bone carries a Rigidbody and a BoxCollider of its own (<c>RagdollRig.BuildBone</c>). So
        /// the nearest Rigidbody to an aim ray that hit a downed player is that player's FOREARM.
        /// Tying it would put this component on a bone, address the network message to an object
        /// with no NetworkObject, and hold nothing.
        /// </para>
        /// </summary>
        public static GameObject BodyOf(Component hit)
        {
            if (hit == null) return null;

            PlayerRagdoll asPlayer = hit.GetComponentInParent<PlayerRagdoll>();
            if (asPlayer != null) return asPlayer.gameObject;

            AgentRagdoll asCreature = hit.GetComponentInParent<AgentRagdoll>();
            return asCreature != null ? asCreature.gameObject : null;
        }

        /// <summary>
        /// May this body be tied at all?
        ///
        /// <para>
        /// <b>Only a body that is already down.</b> You cannot tie somebody standing: a tie is a
        /// follow-up to whatever put them there, which is what stops the leash being a one-click
        /// two-minute stun.
        /// </para>
        /// <para>
        /// Deliberately broader than "netted", and this is the reason the ragdoll adapters expose
        /// <c>IsHeldOrDown</c> rather than only their claim sets: a player knocked flat by a
        /// repulsor blast is just as tieable as a netted one, and refusing that would make the two
        /// feel like unrelated systems.
        /// </para>
        /// <para>
        /// A body already tied is refused, so a second rope cannot be spent to no effect — and the
        /// leash then falls through to its ordinary anchor path, because a tied body is still a
        /// perfectly good thing to tie a rope TO.
        /// </para>
        /// </summary>
        public static bool CanTie(GameObject body)
        {
            if (body == null) return false;
            if (body.TryGetComponent(out Hogtie already) && already.IsBound) return false;

            PlayerRagdoll asPlayer = body.GetComponentInParent<PlayerRagdoll>();
            if (asPlayer != null) return asPlayer.IsHeldOrDown;

            AgentRagdoll asCreature = body.GetComponentInParent<AgentRagdoll>();
            return asCreature != null && asCreature.IsHeldOrDown;
        }

        /// <summary>
        /// Put the ropes on. Runs on EVERY machine; only <paramref name="authority"/> decides when
        /// they come off.
        /// </summary>
        /// <param name="rope">
        /// The item the tie is spending, given back as a pickup when the tie ends. Null is legal
        /// and means "no rope comes back" — an EditMode fixture, or a build where the leash item
        /// has been removed from the registry.
        /// </param>
        /// <returns>
        /// False when the tie did not take, and the caller must then not spend the rope. Either the
        /// body is not down (<see cref="CanTie"/>), or the ragdoll refused the hold outright —
        /// <c>PlayerRagdoll.HoldDown</c> lists what those are: a corpse, a body a seat or a saddle
        /// is already placing, or a rig whose skeleton build kept no bones. Recording a tie over a
        /// body that never went down is worse than no tie: the rope is spent holding somebody who
        /// is walking about.
        /// </returns>
        public bool Bind(HogtieSettings tieSettings, InventoryItem rope, bool authority)
        {
            if (bound) return false;
            if (!CanTie(gameObject)) return false;

            if (!Claim()) return false;

            settings = tieSettings ?? new HogtieSettings();
            this.rope = rope;
            authoritative = authority;

            integrity = new SnareIntegrity();
            integrity.Reset(settings.HoldSeconds);

            meter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate,
                                           settings.StruggleDecaySeconds);
            sendMeter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate,
                                               settings.StruggleDecaySeconds);
            input.ForgetHeading();

            // Registered here rather than in an OnEnable this component deliberately does not have,
            // and dropped in Release, so the subscription lasts exactly as long as the tie. The
            // same reason SnaredBody subscribes to OnDeath in Bind.
            this.NetOn(NetMsg.HogtieStruggled, OnStruggleReported);
            this.NetOn(NetMsg.HogtieUntied, OnUntieAnnounced);

            HealthComponent vitals = Vitals;
            if (vitals != null) vitals.OnDeath += OnBodyDied;

            bound = true;
            return true;
        }

        /// <summary>
        /// Cut the ropes. Safe from anywhere, safe twice, and the ONE way a tie ends.
        ///
        /// <para>
        /// Every route out funnels here — the ceiling running out, a body struggling free, the body
        /// dying, and a third party clicking the ropes empty-handed. That is what makes "the rope
        /// comes back at the body" one line in one place rather than four that drift.
        /// </para>
        /// <para>
        /// On the deciding machine it also tells everyone else, so a machine that did not see the
        /// local gesture — and a dedicated server, which is not among <c>SendTo.ClientsAndHost</c>
        /// — still lets go. That broadcast re-enters this method on the host, which is exactly why
        /// the first line is a guard rather than an assertion: the second pass finds
        /// <see cref="bound"/> already false and returns, so the rope is dropped once.
        /// </para>
        /// </summary>
        public void Untie()
        {
            if (!bound) return;

            bool wasDeciding = authoritative;
            Release();

            if (!wasDeciding) return;

            DropRope();
            this.NetToAll(NetMsg.HogtieUntied);
        }

        /// <summary>
        /// Advance one step. The seam the EditMode tests use, and the reason the input arrives as
        /// arguments rather than being read in here: a test can supply presses, and an
        /// <c>InputControls</c> cannot be driven from one.
        ///
        /// <para>
        /// Both meters advance on every machine that runs one, so a peer's decays rather than
        /// latching at whatever it last heard. Only the tied player's own machine may ADD to the
        /// send meter, because only it knows what they are pressing; only the deciding machine
        /// spends the pool.
        /// </para>
        /// </summary>
        public void Step(float delta, bool jumpPressed, Vector2 move)
        {
            if (!bound) return;

            ReportStruggle(delta, jumpPressed, move);
            Spend(delta);
        }

        /// <summary>
        /// The tied player's half: decide whether that input was a struggle, and if so say so once.
        ///
        /// <para>
        /// Offered to the meter rather than added, because <see cref="SnareStruggleMeter.Push"/>
        /// answering false is the throttle on the WIRE as much as on the meter — a rejected input
        /// must not be reported either, or a held key becomes a message per frame.
        /// </para>
        /// </summary>
        private void ReportStruggle(float delta, bool jumpPressed, Vector2 move)
        {
            sendMeter.Advance(delta);

            if (!Network.Owns(this)) return;

            bool struggled = input.Counts(jumpPressed, move,
                                          settings.StruggleMoveDeadzone,
                                          settings.StruggleReversalDot);
            if (!struggled || !sendMeter.Push()) return;

            // On this body's OWN relay, unlike the net's report — which has to cross to the
            // shooter's, because that is where SnareReceiver lives. Here the listener is this
            // component, on this body, so the channel the message arrives on already names the
            // subject and there is nothing to put in the payload.
            this.NetToServer(NetMsg.HogtieStruggled);
        }

        /// <summary>The deciding machine's half: bill the struggle to the pool, and end it when spent.</summary>
        private void Spend(float delta)
        {
            if (!authoritative) return;

            meter.Advance(delta);

            // Weighed the way SnareIntegrity.Drain reads it: a body fighting flat out arrives as
            // 1 + multiplier captives' worth of mass. Nothing about SnareIntegrity changes for a
            // tie; it already takes the greater of the idle rot and load/ReferenceLoad, and a
            // struggling body simply arrives as more mass.
            integrity.Drain(SnareIntegrity.ReferenceLoad
                            * (1f + settings.StruggleMultiplier * meter.Level), delta);

            if (integrity.IsSpent) Untie();
        }

        /// <summary>
        /// One report that this body fought the ropes, once. The authority's side of
        /// <c>NetMsg.HogtieStruggled</c>.
        ///
        /// <para>
        /// Not idempotent, and must not be: the whole content of the message is that one more input
        /// happened, so acting on it twice would count it twice. That is why it is a
        /// <c>NetTo.Server</c> message rather than a broadcast — delivered exactly once, to the one
        /// machine that spends the pool.
        /// </para>
        /// <para>
        /// <see cref="Network.MayActFor"/> asked of THIS body is what stops one player reporting
        /// struggles on another's behalf. <c>NetRelay</c>'s server RPC is
        /// <c>InvokePermission.Everyone</c>, so anybody in the session may send on this relay;
        /// without the check, a player who was never tied could struggle somebody else out of their
        /// ropes from across the map, or hold their own ropes open by never sending at all while a
        /// confederate spammed the channel.
        /// </para>
        /// </summary>
        private void OnStruggleReported(in NetArg arg, ulong sender)
        {
            if (!bound || !authoritative) return;
            if (!Network.MayActFor(gameObject, sender)) return;

            // Offered, not added: this meter carries the same authored cooldown the sender's does,
            // so a client sending a hundred a second is discarded here exactly as it would be
            // there. That is what makes it safe for the wire to carry no magnitude at all.
            meter.Push();
        }

        /// <summary>A machine elsewhere ended this tie. Idempotent — see <see cref="Untie"/>.</summary>
        private void OnUntieAnnounced(in NetArg arg, ulong sender) => Untie();

        /// <summary>
        /// The tied body died. Let go at that moment rather than at the ropes'.
        ///
        /// <para>
        /// A corpse is not a captive. The ragdoll adapters already drop every claim on death — the
        /// leak that fix closed was a permanently un-evictable <c>RagdollBudget</c> slot, and the
        /// claim set handles this tie's share of it with nothing added here — but nothing there
        /// knows about the ropes, so without this the tie outlives the player: the pool goes on
        /// draining a corpse, the tied player's own machine goes on reading a dead player's keys
        /// (the menu gate is open for a corpse, there being no menu), and the rope does not come
        /// back until the full two minutes are up. Ending here also puts the rope on the ground at
        /// the body, which is where a rope that was round somebody who has just died belongs.
        /// </para>
        /// </summary>
        private void OnBodyDied() => Untie();

        /// <summary>
        /// Read this body's keys and hand them to <see cref="Step"/>.
        ///
        /// The gate is <see cref="SnareStruggleReader.MayRead"/>, which is NOT the shared
        /// <c>AcceptsGameplayInput</c> hotkey gate — that one is false for every restrained player
        /// by construction. See there for the full reason.
        /// </summary>
        private void Update()
        {
            if (!bound) return;

            input.Poll(SnareStruggleReader.MayRead(Network.Owns(this)),
                       out bool jumpPressed, out Vector2 move);

            Step(Time.deltaTime, jumpPressed, move);
        }

        /// <summary>
        /// Teardown: let go LOCALLY, and say nothing.
        ///
        /// <para>
        /// Not <see cref="Untie"/>. This is reached when the body is destroyed or its chunk
        /// unloads, and at that moment the two things Untie does besides releasing are both wrong:
        /// a broadcast has no relay left to leave from, and a rope spawned at an object that is
        /// going away lands in a chunk nobody is loading. Releasing is the half that can be done
        /// cleanly, and it is the half that matters — a body left limp with nothing alive to stand
        /// it up is a player who cannot move for the rest of the session.
        /// </para>
        /// <para>
        /// The consequence, stated rather than hidden: a rope on a body that is destroyed while
        /// tied is lost. That is the same trade <c>SnareReceiver.OnDisable</c> documents for a net
        /// whose shooter despawns.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            Release();
            input.Release();
        }

        // ── The claim ──────────────────────────────────────────────────────────

        /// <summary>
        /// Give the ragdoll back its claim and stop everything this tie was running.
        ///
        /// <para>
        /// <b>One claim, not the limpness.</b> Both adapters hold a SET of claims, so a body that a
        /// net and a tie both have hold of stays down until the last one lets go — which is what
        /// makes the two compose with no bookkeeping here at all. A tie cut off a netted player
        /// leaves them netted; a net rotting off a tied player leaves them tied.
        /// </para>
        /// </summary>
        private void Release()
        {
            if (!bound) return;

            bound = false;

            this.NetOff(NetMsg.HogtieStruggled, OnStruggleReported);
            this.NetOff(NetMsg.HogtieUntied, OnUntieAnnounced);

            HealthComponent vitals = Vitals;
            if (vitals != null) vitals.OnDeath -= OnBodyDied;

            integrity = null;
            meter = null;
            sendMeter = null;
            settings = null;
            input.ForgetHeading();
            input.Release();

            Unclaim();
        }

        /// <summary>Take the hold, whichever kind of body this is. False means it refused.</summary>
        private bool Claim()
        {
            if (Body != null) return Body.HoldDown(this);
            return Beast != null && Beast.HoldDown(this);
        }

        private void Unclaim()
        {
            if (Body != null) Body.ReleaseHold(this);
            if (Beast != null) Beast.ReleaseHold(this);
        }

        /// <summary>
        /// Put the rope back in the world at the body. Deciding machine only.
        ///
        /// <para>
        /// Through <c>GameServices.ItemDropService</c> rather than a bare Instantiate, because that
        /// is the one path in this project that spawns an item into the world correctly: it uses
        /// <c>GameServices.World.Spawn</c> (which a client is refused outright, rather than quietly
        /// creating a ghost), sizes the spawn point off the item's own world scale, and stamps a
        /// runtime <c>SaveableEntity</c> so the dropped rope survives a reload. Every
        /// <c>InventoryItem.itemPrefab</c> is already a registered network prefab — the net gun
        /// pipeline's <c>NetworkPrefabRegistrationTests</c> asserts exactly that — so no new
        /// registration is needed for this.
        /// </para>
        /// </summary>
        private void DropRope()
        {
            if (rope == null || GameServices.ItemDropService == null) return;

            GameServices.ItemDropService.DropItem(transform, rope);
        }

        // ── Resolved on demand ─────────────────────────────────────────────────
        //
        // From the PARENT and never cached in an Awake this component does not have. Both reasons
        // are SnaredBody's: AddComponent raises no Awake outside play mode, and a body whose
        // collider is on a child is not the object the adapter sits on.

        private PlayerRagdoll Body =>
            player != null ? player : player = GetComponentInParent<PlayerRagdoll>();

        private AgentRagdoll Beast =>
            creature != null ? creature : creature = GetComponentInParent<AgentRagdoll>();

        private HealthComponent Vitals =>
            health != null ? health : health = GetComponentInParent<HealthComponent>();
    }
}
