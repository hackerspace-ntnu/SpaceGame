using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.Teleporting;
using UnityEngine;

namespace SpaceGame.Gameplay.Ragdoll
{
    /// <summary>
    /// Decides WHEN a body goes limp and for how long, for any body — creature or player.
    ///
    /// <para>
    /// Everything here used to exist twice, once in <see cref="AgentRagdoll"/> and once in
    /// <see cref="PlayerRagdoll"/>, and the two copies had started to drift (only one refused a
    /// knockdown on a carried body). What differs between them is WHICH layers own the transform
    /// and must be switched off — a creature's brain, motor and legs; a player's input, look and
    /// camera — and that is all the subclasses supply.
    /// </para>
    ///
    /// <para>
    /// Down-time comes from the event, not from the body: <see cref="KnockdownPolicy"/> prices it on
    /// the deciding machine and <c>NetMsg.Knockdown</c> carries it to everyone, so every machine
    /// stands the body up together. Death has no down-time: a corpse stays limp until it despawns.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(RagdollRig))]
    public abstract class RagdollController : MonoBehaviour
    {
        [SerializeField] private KnockdownTuning knockdown = new KnockdownTuning();

        [Tooltip("Speed a knockdown-worthy HIT throws the body at, m/s, away from the attacker. The " +
                 "hit carries no knockback of its own, so this stands in for it in the price too: " +
                 "KnockdownPolicy counts it as the hit's knockback, and raising it makes hits both " +
                 "knock down more easily and keep the body down longer.")]
        [SerializeField] private float hitImpulse = 3f;

        protected RagdollRig rig;
        protected HealthComponent health;

        /// <summary>
        /// Everything currently holding this body down with no end time — a net, a tie, both at
        /// once. See <see cref="HoldDown"/>.
        ///
        /// <para>
        /// A set of holders rather than a flag, the same shape <c>CarriedBody</c> uses and
        /// for the same reason: two systems can want one body down, and the one that lets go first
        /// must not stand it up. A captor hands back the token it claimed with, so forgetting is a
        /// compile error rather than a captive who gets up on their own.
        /// </para>
        /// <para>
        /// Identity only — nothing is ever read off a holder. <see cref="object"/> rather than an
        /// interface so a captor needs to implement nothing at all to take part.
        /// </para>
        /// </summary>
        private readonly HashSet<object> holders = new HashSet<object>();

        private bool suspended;
        private bool dead;

        /// <summary>Earliest this body may stand up. See <see cref="OnKnockdown"/>.</summary>
        private float standAt;

        /// <summary>
        /// When this body last got up off the ground — what hit immunity is measured from. Only a
        /// body that actually went limp stamps it: a hold the rig refused, or a standing hold let
        /// go, never put anything on the ground and earns no immunity.
        /// </summary>
        private float stoodUpAt = float.NegativeInfinity;

        public KnockdownTuning Tuning => knockdown;

        /// <summary>Is something holding this body down right now?</summary>
        public bool IsHeld => holders.Count > 0;

        /// <summary>
        /// Is this body on the ground right now, by any route — a net, a tie, or a blast?
        ///
        /// Deliberately broader than <see cref="IsHeld"/>: a body knocked flat by a repulsor blast
        /// is just as tieable as a netted one, and refusing that would make the two feel like
        /// unrelated systems.
        /// </summary>
        public bool IsHeldOrDown => IsHeld || (rig != null && rig.IsLimp);

        /// <summary>Can this body be put on the ground right now, or must it be leapt instead?</summary>
        public bool CanBeKnockedDown => isActiveAndEnabled && !RefusesToGoDown && !HeldStanding;

        /// <summary>
        /// Is something holding this body ON ITS FEET — a claim taken through
        /// <see cref="HoldStandingClaim"/>, with the collider on and the camera still in the helmet?
        ///
        /// <para>
        /// Such a body refuses knockdowns the way a rider does. Limp under a standing hold it would
        /// be a heap on the sand with its first-person camera inside a tumbling skull — the statue
        /// the freeze exists to show thrown away — and it would stay down for as long as the hold
        /// lasts, however short the knockdown, because <see cref="TickStandUp"/> waits for the last
        /// claim. A body held DOWN is different: it is already limp, and a second knockdown only
        /// adds to its motion.
        /// </para>
        /// </summary>
        private bool HeldStanding => IsHeld && !rig.IsLimp;

        // ── What the subclass supplies ────────────────────────────────────────

        /// <summary>Does THIS machine decide where the body ends up? See <see cref="RagdollRig.Drives"/>.</summary>
        protected abstract bool Drives { get; }

        /// <summary>
        /// How fast the body was already moving. Read BEFORE suspending: suspending switches off
        /// the very layer it is read from, and taken after, it is a confident zero — a body felled
        /// mid-sprint then drops as if switched off.
        /// </summary>
        protected abstract Vector3 CarriedVelocity { get; }

        /// <summary>
        /// Is the body somebody else's to move — a rider in a saddle or a seat? A rider is PARENTED
        /// to what carries it, so one that goes limp there is dragged wherever it goes, through the
        /// ground included — and on a client that is a body the server does not own and cannot put
        /// back. Refuses knockdowns and holds alike.
        /// </summary>
        protected abstract bool RefusesToGoDown { get; }

        /// <summary>The velocity a killing blow hands the body.</summary>
        protected abstract Vector3 DeathImpulse();

        /// <summary>Stop every layer that writes the transform or the bones. Called once per suspension.</summary>
        /// <param name="standing">
        /// Keep the body on its feet: nothing is going to go limp under it. See
        /// <see cref="HoldStandingClaim"/>.
        /// </param>
        protected abstract void SuspendLayers(bool standing);

        /// <summary>Hand the body back to those layers, at the place it came to rest.</summary>
        protected abstract void RestoreLayers(in TeleportMove move);

        /// <summary>Something outside this component that forbids standing up — a player's death screen.</summary>
        protected virtual bool ControlsLocked => false;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected virtual void Awake()
        {
            rig = GetComponent<RagdollRig>();
            health = GetComponent<HealthComponent>();
        }

        protected virtual void OnEnable()
        {
            this.NetOn(NetMsg.Knockdown, OnKnockdown);
            this.NetOn(NetMsg.KnockdownRequest, OnKnockdownRequest);
            if (health != null) health.OnDamage += OnDamaged;
            if (health != null) health.OnDeath += OnDeath;
            if (health != null) health.OnRevive += OnRevive;
        }

        protected virtual void OnDisable()
        {
            this.NetOff(NetMsg.Knockdown, OnKnockdown);
            this.NetOff(NetMsg.KnockdownRequest, OnKnockdownRequest);
            if (health != null) health.OnDamage -= OnDamaged;
            if (health != null) health.OnDeath -= OnDeath;
            if (health != null) health.OnRevive -= OnRevive;
        }

        private void Update() => TickStandUp(Time.time);

        /// <summary>The stand-up decision, with the clock passed in so it can be tested.</summary>
        private void TickStandUp(float now)
        {
            // Death outranks it: a knockdown that landed on the same frame as the killing blow must
            // not stand the corpse back up. A hold has no timer and no settle condition to wait
            // for, so every reason to stand up below is the wrong one — and it has to sit ABOVE the
            // rescue as well as the timer, or a captive stands up the moment their rig stops
            // being limp for any reason at all.
            if (!suspended || dead || ControlsLocked || IsHeld) return;

            // Something took the body off physics while it was suspended. RagdollBudget only
            // freezes corpses, so a living body should never be here — this is the belt to that
            // brace. Nothing will come to rest or say so, and leaving it suspended is a knockdown
            // that never ends: a creature with its brain switched off, a player unable to move.
            if (!rig.IsLimp)
            {
                Restore();
                return;
            }

            // The shared floor first, then this machine's own body, then a ceiling. A body still in
            // the air when the floor expires keeps tumbling; one that landed early lies there for
            // the rest of the beat (GDC-L1-FEEL-0007); one wedged against a rock that never comes
            // to rest still gets up (GDC-L1-FEEL-0002).
            if (KnockdownPolicy.ShouldStandUp(now, standAt, rig.IsAtRest, knockdown.settleGraceSeconds))
                Restore();
        }

        // ── What starts it ────────────────────────────────────────────────────

        private void OnDeath()
        {
            dead = true;

            // Death outranks every hold. What is dropped is the holds' CLAIM, not the limpness: the
            // corpse stays down, it just stops being a captive the budget may not reclaim. Without
            // this a body netted at the moment it dies keeps its place in RagdollBudget for the
            // rest of the session, and enough of them stop the budget bounding anything.
            //
            // Cleared directly rather than through ReleaseHold, and the difference is not cosmetic.
            // ReleaseHold releases ONE claim and would leave every other captor's standing; death
            // ends all of them at once.
            holders.Clear();
            rig.BudgetExempt = false;
            rig.IsCorpse = true;

            // A save being loaded: the body lies down where the record put it, without being
            // thrown again — a corpse relaunched on every load walks its way across the desert one
            // reload at a time. NOT IsRestoring, which is also a client learning of a fresh death
            // through RestoreHealth: that body must fall like any other. Its owner (a client's own
            // player) throws it; a watcher ignores the impulse and takes the body off the wire.
            bool loading = health != null && health.IsLoading;
            Vector3 carried = loading ? Vector3.zero : CarriedVelocity;

            Suspend(standing: false);
            rig.GoLimp(loading ? Vector3.zero : DeathImpulse() + carried,
                       settled: loading, drives: Drives);
        }

        private void OnRevive()
        {
            dead = false;

            // Belt to OnDeath's braces. Unreachable today — death always clears the claims first,
            // and HealthComponent only raises this on a dead-to-alive transition — but the failure
            // if it ever were reachable is permanent and silent: Restore calls rig.Recover, which
            // unregisters from the budget while leaving the claim set standing, and HoldDown
            // answers a stale claim rather than taking a fresh one.
            holders.Clear();
            rig.BudgetExempt = false;
            rig.IsCorpse = false;

            // Not gated on IsLimp: a corpse RagdollBudget froze is no longer limp but is still
            // suspended, and has to be handed back as much as one still lying limp. Restore
            // returns on its own when nothing was suspended.
            Restore();
        }

        /// <summary>
        /// The deciding machine only: price a knockdown and tell every machine. The single door every
        /// knockdown source goes through — a blast, a hit, a fall — so no source owns a duration of
        /// its own and a boss and a rat can price the same blast differently (their
        /// <see cref="KnockdownTuning"/>). A no-op anywhere else, and on anything with no controller.
        /// </summary>
        public static void Knock(GameObject victim, RagdollCause cause, Vector3 impulse,
                                 float damageFraction = 0f)
        {
            if (!Network.Decides || victim == null) return;

            RagdollController ragdoll = Of(victim);
            if (ragdoll != null) ragdoll.KnockHere(cause, impulse, damageFraction);
        }

        /// <summary>
        /// Would <see cref="Knock"/> find a body to put on the ground here? Asked through the same
        /// entity-scoped lookup, so a caller choosing between a knockdown and some fallback (a
        /// mount's leap) can never ask one body and knock another — a vessel's seated passenger,
        /// say, when the blast only caught the hull.
        /// </summary>
        public static bool CanKnock(GameObject victim)
        {
            if (victim == null) return false;

            RagdollController ragdoll = Of(victim);
            return ragdoll != null && ragdoll.CanBeKnockedDown;
        }

        /// <summary>
        /// The controller of the entity <paramref name="victim"/> belongs to. Up, then down: a hit
        /// hands over the body itself, but a blast hands over the root of whatever collider it
        /// caught, and a creature's controller can sit below that.
        ///
        /// <para>
        /// Down only within the victim's OWN entity. A sky vessel or a caravan mount has its seated
        /// passengers parented beneath it, each its own NetworkObject, and the first controller
        /// found below a vessel with none of its own is a passenger — who would be knocked down by
        /// a blast that only caught the hull.
        /// </para>
        /// </summary>
        private static RagdollController Of(GameObject victim)
        {
            RagdollController ragdoll = victim.GetComponentInParent<RagdollController>();
            if (ragdoll != null) return ragdoll;

            GameObject entity = NetChannel.RootOf(victim.transform);
            foreach (RagdollController below in victim.GetComponentsInChildren<RagdollController>())
                if (NetChannel.RootOf(below) == entity) return below;

            return null;
        }

        /// <summary>
        /// The owner's half of a fall knockdown: ask the server. A fall is measured only by the
        /// machine that owns the body, and only the server may broadcast the knockdown. Offline, and
        /// on a host, this dispatches locally and lands in <see cref="OnKnockdownRequest"/> at once.
        ///
        /// <para>
        /// Send it BEFORE the fall's damage: both travel on the victim's relay, reliable and in
        /// order, so the server has marked the damage as the fall's by the time it lands. See
        /// <see cref="OnDamaged"/>.
        /// </para>
        /// </summary>
        public static void RequestFallKnockdown(Component body)
        {
            if (body == null) return;

            NetMessaging.NetSendTo(body.gameObject, NetMsg.KnockdownRequest,
                                   new NetArg { A = (int)RagdollCause.Fall }, NetTo.Server);
        }

        private void OnKnockdownRequest(in NetArg arg, ulong sender)
        {
            // Only falls may be requested: a client naming any other cause is asking to knock a body
            // down on its own authority. Asked of the entity root, which holds the NetworkObject —
            // this component may sit below it.
            if ((RagdollCause)arg.A != RagdollCause.Fall) return;
            if (!Network.MayActFor(NetChannel.RootOf(this), sender)) return;

            Knock(gameObject, RagdollCause.Fall, Vector3.zero);
        }

        private void KnockHere(RagdollCause cause, Vector3 impulse, float damageFraction)
        {
            if (dead || !CanBeKnockedDown) return;

            // A hit on a body already down does not keep it down. Every round of automatic fire into
            // a downed body used to push its stand-up time out again, so under fire nobody ever got
            // up. A blast still can: it is a deliberate throw, and OnKnockdown keeps the later of
            // the two stand-up times. This is also what keeps a fall's own damage from being
            // priced as a hit — the fall's request arrives first and the body is already down.
            if (rig.IsLimp && cause == RagdollCause.Hit) return;

            if (KnockdownPolicy.Immune(cause, Time.time - stoodUpAt, knockdown)) return;

            float healthLeft = health != null && health.GetMaxHealth > 0
                ? (float)health.GetHealth / health.GetMaxHealth
                : 1f;

            float seconds = KnockdownPolicy.Seconds(
                new KnockdownEvent(cause, damageFraction, healthLeft, impulse.magnitude), knockdown);
            if (seconds <= 0f) return;

            NetMessaging.NetSendTo(gameObject, NetMsg.Knockdown, new NetArg
            {
                P = impulse,
                A = Mathf.RoundToInt(seconds * 1000f),
                B = (int)cause,
            }, NetTo.All);
        }

        /// <summary>
        /// Damage lands only where it is decided (the server, or offline), so this runs there and
        /// nowhere else — a client's copy of the health changes through <c>RestoreHealth</c>, which
        /// raises no <c>OnDamage</c>. A killing blow is death's business, and a save restoring
        /// health is not a hit.
        ///
        /// <para>
        /// Nor, in effect, is a fall's own damage: the fall's request arrives first (see
        /// <see cref="RequestFallKnockdown"/>), so its damage lands on a body already down, and a
        /// hit on a downed body does not knock it (<see cref="KnockHere"/>).
        /// </para>
        /// </summary>
        private void OnDamaged(int amount)
        {
            if (!Network.Decides || health == null || health.IsRestoring) return;
            if (health.GetHealth <= 0 || health.GetMaxHealth <= 0) return;

            Transform source = health.LastDamageSource;

            Vector3 away = source != null
                ? Vector3.ProjectOnPlane(transform.position - source.position, Vector3.up).normalized
                : -transform.forward;

            Knock(gameObject, RagdollCause.Hit, away * hitImpulse, (float)amount / health.GetMaxHealth);
        }

        /// <summary>
        /// Every machine: go limp for <c>A</c> ms, thrown at <c>P</c>. <c>B</c> is the
        /// <see cref="RagdollCause"/>, for diagnostics only — the price and the immunity were both
        /// settled by <see cref="Knock"/> before the message left the deciding machine.
        ///
        /// <para>
        /// The duration travels with the message rather than being decided locally because it is
        /// the only part of the recovery every machine can agree on. Settling cannot be: a watcher
        /// does not simulate the flight — it takes the body's position off the wire — so its
        /// ragdoll comes to rest on a different schedule from the one that does. Sharing the floor
        /// and letting each machine wait out its own body on top of it keeps them within a frame or
        /// two of each other without a second round trip to say "get up now".
        /// </para>
        /// </summary>
        private void OnKnockdown(in NetArg arg, ulong sender)
        {
            // The same refusals Knock made on the deciding machine, asked again here: a standing
            // hold is claimed on every machine, and a watcher that laid the body down anyway would
            // show a heap where everyone else sees a statue.
            if (dead || RefusesToGoDown || HeldStanding) return;

            Vector3 carried = CarriedVelocity;

            Suspend(standing: false);
            rig.GoLimp(arg.P + carried, settled: false, drives: Drives);

            float seconds = arg.A > 0 ? arg.A / 1000f : knockdown.minSeconds;
            standAt = Mathf.Max(standAt, Time.time + seconds);
        }

        // ── Holds ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Go limp and STAY limp until every holder lets go.
        ///
        /// <para>
        /// Distinct from <see cref="OnKnockdown"/>, which recovers on its own timer, because the
        /// end of this one is not known when it starts: a captive is up when they have struggled
        /// out, and how long that takes is decided on the server against a pool being drained by
        /// their own inputs. Nothing about the duration can travel, so nothing tries to.
        /// </para>
        /// <para>
        /// The callers (<c>SnaredBody.Bind</c>, <c>SnareTether.Bind</c>, <c>Hogtie</c>,
        /// <c>BodyHold</c>) reach every machine, so this runs everywhere, and the Drives split then
        /// decides which half of the ragdoll plumbing this machine gets, exactly as for a
        /// knockdown. Until <see cref="ReleaseHold"/> runs, the rig is exempt from
        /// <c>RagdollBudget</c> and this component ignores every other reason to stand up.
        /// </para>
        /// <para>
        /// The refusal has to be visible to the caller rather than silent, because a net on a
        /// ridden mount would otherwise be a no-op with a clean console. The captor is expected to
        /// fall back to whatever restraint it has instead — <c>SnareTether.Bind</c> caps a
        /// NavMeshAgent's speed when, and only when, this answers false, and has nothing to fall
        /// back on for a creature with no NavMeshAgent, a legged rig among them. This only says
        /// whether the body itself went down.
        /// </para>
        /// </summary>
        /// <returns>
        /// True once the body is actually limp and held. FALSE means the hold did not take and the
        /// caller must not treat the body as held — it is dead, something else is carrying it (see
        /// <see cref="RefusesToGoDown"/>), or the rig declined to go limp at all.
        /// <c>RagdollRig.GoLimp</c> returns without a word when the skeleton build kept no bones,
        /// and a caller that assumed otherwise would leave a body suspended with its layers switched
        /// off and nothing in the console: the <c>!rig.IsLimp</c> rescue in
        /// <see cref="TickStandUp"/> is skipped while any claim is still standing.
        /// </returns>
        public bool HoldDown(object holder)
        {
            // A corpse is already down and is not getting up. A hold that took one would set
            // BudgetExempt on a body nothing will ever release — the leak OnDeath exists to close,
            // arriving through a second door.
            if (holder == null || dead) return false;

            // Somebody else already has this body down. Take a claim on it and say so: the work
            // below has been done, and doing it twice would record the suspended state as this
            // body's normal one.
            if (IsHeld)
            {
                holders.Add(holder);
                return true;
            }

            if (!CanBeKnockedDown) return false;

            holders.Add(holder);
            Vector3 carried = CarriedVelocity;

            Suspend(standing: false);
            rig.BudgetExempt = true;
            rig.GoLimp(carried, settled: false, drives: Drives);

            // Asked of the rig afterwards rather than pre-checked, so the refusal covers every
            // reason GoLimp can decline rather than only the one we thought of. Everything this
            // method did is undone, suspend included, or the refusal is worse than the failure it
            // is reporting.
            if (rig.IsLimp) return true;

            holders.Remove(holder);
            rig.BudgetExempt = false;
            Restore();
            return false;
        }

        /// <summary>
        /// Claim the body WITHOUT laying it down — the same claim set, the same release and the
        /// same refusal as <see cref="HoldDown"/>, so a captive who is netted AND frozen is held
        /// once and stands up once. See <c>PlayerRagdoll.HoldStanding</c>.
        /// </summary>
        protected bool HoldStandingClaim(object holder)
        {
            if (holder == null || dead) return false;

            if (IsHeld)
            {
                holders.Add(holder);
                return true;
            }

            if (RefusesToGoDown) return false;

            holders.Add(holder);
            Suspend(standing: true);
            return true;
        }

        /// <summary>
        /// Give up one claim. The body gets up only once the LAST claim is given up — the rule
        /// <c>CarriedBody.Release</c> follows, so a net rotting off a hogtied captive does
        /// not untie them.
        ///
        /// Safe to call with a token that was never claimed, or after death has cleared the set.
        /// </summary>
        public void ReleaseHold(object holder)
        {
            if (holder == null || !holders.Remove(holder)) return;
            if (IsHeld) return;

            rig.BudgetExempt = false;

            // Not Restore() directly: TickStandUp owns the recovery, and it waits for the body to
            // come to rest, or for the settle grace to run out, so a body released mid-tumble does
            // not snap upright out of a roll. Now, not zero: the grace is measured from standAt,
            // and a floor of zero would have run out long ago. Never EARLIER than it already is,
            // though: a knockdown that landed during the hold set a stand-up time of its own,
            // and a release is no reason to cut it short.
            standAt = Mathf.Max(standAt, Time.time);
        }

        // ── Handing the body over and back ────────────────────────────────────

        /// <summary>
        /// Idempotent: a body knocked down twice while already down must not record the suspended
        /// state a second time, or resuming restores the values captured mid-ragdoll.
        /// </summary>
        private void Suspend(bool standing)
        {
            if (suspended) return;
            suspended = true;
            SuspendLayers(standing);
        }

        private void Restore()
        {
            if (!suspended) return;
            suspended = false;
            standAt = 0f;
            if (rig.IsLimp) stoodUpAt = Time.time;

            TeleportMove move = rig.Recover();
            RestoreLayers(move);
        }
    }
}
