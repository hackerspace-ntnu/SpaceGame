// A player inside a container, which is a temporary state and nothing more.
//
// This is the whole griefing answer, and it is a design decision rather than a limitation: aimed at
// a creature a container is permanent, aimed at a person it is a few seconds' inconvenience. A
// player is never turned into a record, never despawned, and never left with nothing to do —
// mashing shortens the wait, and the wait ends on its own whether they mash or not, so somebody who
// puts the controller down still gets out (GDC-L1-MP-0002).
//
// The clock runs on the PLAYER's own object, not on the container's. A container is an item
// instance destroyed on every hotbar switch, and a bottled player whose captor changed slot must
// not be left limp for the rest of the session.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// One bottled player: the hold that keeps them there, the clock that lets them out, and the
    /// mashing that hurries it.
    ///
    /// <para>
    /// Added on demand rather than authored, because any player can be bottled at any time — the
    /// same shape and reasoning as <see cref="SnaredBody.Ensure"/>. It has no Awake and must not
    /// grow one: Unity does not raise Awake for an AddComponent outside play mode, so anything
    /// cached there would be null in an EditMode test and the hold would silently hold nothing.
    /// </para>
    /// <para>
    /// <b>Authority.</b> Split the way <c>Hogtie</c> splits it. Every machine takes the hold — a
    /// peer that skipped it would watch a bottled player walk about while every other machine sees
    /// them gone — and one machine runs the clock and announces the end.
    /// </para>
    /// <para>
    /// <b>The player is not moved.</b> The hold suppresses their input, which is the treatment the
    /// hogtie already gives and the same one the design asks for; where their camera sits is then
    /// wherever their body already was. Writing a position instead would be a server write to an
    /// owner-authoritative body, undone within a tick and silently — the one rule this project
    /// breaks most often.
    /// </para>
    /// <para>
    /// <b>Nothing here is persisted, deliberately.</b> This component implements no
    /// <c>ISaveable</c> and is never on a prefab, so <c>SaveableEntity.CollectSavers</c> cannot see
    /// it and no key of its own can reach a save file. A quit-time autosave that captured a bottled
    /// player would reload a world in which somebody cannot move, with nothing in the log to say
    /// why. Getting out by loading is a far better failure than that.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class BottledPlayer : MonoBehaviour
    {
        private ContainerHold container;
        private ContainmentSettings settings;
        private PlayerRagdoll body;
        private HealthComponent health;

        /// <summary>
        /// The AUTHORITY's meter: what the mashing is worth.
        ///
        /// Never the same object as <see cref="sendMeter"/>, and the duplication is the design —
        /// the bottled player's own meter runs on THEIR machine and decides what they may send,
        /// this one runs on the authority and decides what it buys. Sharing one would mean putting
        /// a level on the wire, which is handing the client the escape (GDC-L1-MP-0004).
        /// </summary>
        private SnareStruggleMeter meter;

        /// <summary>The bottled player's own send throttle, on their machine only.</summary>
        private SnareStruggleMeter sendMeter;

        /// <summary>Their keys, and the memory of which way they last pushed. Shared with the net.</summary>
        private readonly SnareStruggleReader input = new SnareStruggleReader();

        private bool bottled;
        private bool authoritative;
        private float remaining;

        /// <summary>Is this player inside a container right now?</summary>
        public bool IsBottled => bottled;

        /// <summary>Seconds left on the ceiling, as the authority counts them. For the HUD and tests.</summary>
        public float Remaining => remaining;

        /// <summary>How hard they are mashing, 0-1, as the AUTHORITY scores it.</summary>
        public float StruggleLevel => meter?.Level ?? 0f;

        /// <summary>They are out. Every machine, however it ended.</summary>
        public event System.Action<BottledPlayer> Freed;

        public static BottledPlayer Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out BottledPlayer existing)
                ? existing
                : player.AddComponent<BottledPlayer>();
        }

        /// <summary>
        /// Put this player inside. Runs on EVERY machine; only <paramref name="authority"/> runs
        /// the clock.
        /// </summary>
        /// <returns>
        /// False when the hold did not take, and the caller must then record no capture at all.
        /// <c>PlayerRagdoll.HoldDown</c> lists what those are: a corpse, a body a seat or a saddle
        /// is already placing, or a rig whose skeleton build kept no bones. Recording a capture
        /// over a body that never went down is worse than none: the container reads as full while
        /// the player it claims to hold is walking about.
        /// </returns>
        public bool Bottle(ContainerHold hold, ContainmentSettings bottleSettings, bool authority)
        {
            if (hold == null) return false;

            // The same container bottling again is not a second capture. Answering true without
            // rebuilding is what stops it resetting a ceiling the player has already burned down.
            if (bottled && container == hold) return true;
            if (bottled) return false;

            PlayerRagdoll ragdoll = Body;
            if (ragdoll == null || !ragdoll.HoldDown(this)) return false;

            container = hold;
            settings = bottleSettings ?? new ContainmentSettings();
            authoritative = authority;
            remaining = settings.ContainedPlayerSeconds;

            meter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate, settings.StruggleDecaySeconds);
            sendMeter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate, settings.StruggleDecaySeconds);
            input.ForgetHeading();

            // Registered here rather than in an OnEnable this component deliberately does not have,
            // and dropped in Release, so the subscriptions last exactly as long as the hold.
            //
            // On THIS body's own relay, unlike the struggle a captive sends while being DRAWN in —
            // which has to cross to the captor's, because that is where ContainmentPull lives. Here
            // the listener is this component, on this body, and it has to be: a bottled player's
            // captor may have switched hotbar slot, dropped the container or left the session
            // entirely, and the player still has to get out. The channel a message arrives on is
            // already the subject, so there is nothing to put in the payload.
            this.NetOn(NetMsg.SnareStruggled, OnStruggleReported);
            this.NetOn(NetMsg.Released, OnReleaseAnnounced);

            HealthComponent vitals = Vitals;
            if (vitals != null) vitals.OnDeath += OnBottledPlayerDied;

            bottled = true;
            return true;
        }

        /// <summary>
        /// Let them out. Safe from anywhere, safe twice, and the ONE way a bottling ends.
        ///
        /// <para>
        /// Every route out funnels here — the ceiling running out, mashing it down, the player
        /// dying — which is what makes "the container is empty again" one line in one place rather
        /// than three that drift. On the deciding machine it also tells everyone else, so a machine
        /// that ran no clock still lets go; that broadcast re-enters this method on the host, which
        /// is why the first line is a guard rather than an assertion.
        /// </para>
        /// </summary>
        public void Free()
        {
            if (!bottled) return;

            bool wasDeciding = authoritative;
            Vector3 where = transform.position;

            Release();

            if (!wasDeciding) return;

            NetArg arg = new NetArg().With(container != null ? container.gameObject : null);
            arg.P = where;

            this.NetToAll(NetMsg.Released, arg);
        }

        /// <summary>
        /// Advance one step. The seam the EditMode tests use, and the reason the input arrives as
        /// arguments rather than being read in here: a test can supply presses, and an
        /// <c>InputControls</c> cannot be driven from one.
        /// </summary>
        public void Step(float delta, bool jumpPressed, Vector2 move)
        {
            if (!bottled) return;

            ReportStruggle(delta, jumpPressed, move);
            Spend(delta);
        }

        /// <summary>
        /// The bottled player's half: decide whether that input was a struggle, and if so say so
        /// once.
        ///
        /// Offered to the meter rather than added, because <see cref="SnareStruggleMeter.Push"/>
        /// answering false is the throttle on the WIRE as much as on the meter — a rejected input
        /// must not be reported either, or a held key becomes a message per frame.
        /// </summary>
        private void ReportStruggle(float delta, bool jumpPressed, Vector2 move)
        {
            sendMeter.Advance(delta);

            if (!Network.Owns(this)) return;

            bool struggled = input.Counts(jumpPressed, move,
                                          settings.StruggleMoveDeadzone,
                                          settings.StruggleReversalDot);
            if (!struggled || !sendMeter.Push()) return;

            this.NetToServer(NetMsg.SnareStruggled);
        }

        /// <summary>
        /// The deciding machine's half: run the clock down, faster while they fight it.
        ///
        /// <para>
        /// The ceiling is spent at <c>1 + gain * level</c> seconds per second, so a player mashing
        /// flat out at the authored gain of 1 gets out in half the time and a player doing nothing
        /// gets out in all of it. Both ends of that matter: the mashing has to be worth something
        /// or the input path is a lie, and the clock has to finish on its own or the container
        /// becomes a way to remove somebody from the game.
        /// </para>
        /// </summary>
        private void Spend(float delta)
        {
            if (!authoritative) return;

            meter.Advance(delta);

            remaining -= delta * (1f + settings.ContainedPlayerStruggleGain * meter.Level);

            if (remaining <= 0f) Free();
        }

        /// <summary>
        /// One report that this player mashed, once. The authority's side.
        ///
        /// <para>
        /// Not idempotent, and must not be: the whole content of the message is that one more input
        /// happened, so acting on it twice would count it twice. That is why it is a
        /// <c>NetTo.Server</c> message rather than a broadcast — delivered exactly once, to the one
        /// machine running the clock.
        /// </para>
        /// <para>
        /// <see cref="Network.MayActFor"/> asked of THIS body is what stops one player mashing on
        /// another's behalf: <c>NetRelay</c>'s server RPC is <c>InvokePermission.Everyone</c>, so
        /// anybody in the session may send on this relay.
        /// </para>
        /// </summary>
        private void OnStruggleReported(in NetArg arg, ulong sender)
        {
            if (!bottled || !authoritative) return;
            if (!Network.MayActFor(gameObject, sender)) return;

            // Offered, not added: this meter carries the same authored cooldown the sender's does,
            // so a client sending a hundred a second is discarded here exactly as it would be
            // there. That is what makes it safe for the wire to carry no magnitude at all.
            meter.Push();
        }

        /// <summary>A machine elsewhere ended this. Idempotent — see <see cref="Free"/>.</summary>
        private void OnReleaseAnnounced(in NetArg arg, ulong sender) => Free();

        /// <summary>
        /// The bottled player died. Let go at that moment rather than at the clock's.
        ///
        /// A corpse is not a captive. <c>PlayerRagdoll</c> drops every claim on death on its own,
        /// but nothing there knows about the container — so without this the container goes on
        /// reading as full, this machine goes on reading a dead player's keys (the menu gate is
        /// open for a corpse, there being no menu), and their respawn happens under a hold that is
        /// still counting down.
        /// </summary>
        private void OnBottledPlayerDied() => Free();

        /// <summary>
        /// Read this player's keys and hand them to <see cref="Step"/>.
        ///
        /// The gate is <see cref="SnareStruggleReader.MayRead"/>, which is NOT the shared
        /// <c>AcceptsGameplayInput</c> hotkey gate — that one is false for every restrained player
        /// by construction, because going limp is what disables their input. See there.
        /// </summary>
        private void Update()
        {
            if (!bottled) return;

            input.Poll(SnareStruggleReader.MayRead(Network.Owns(this)),
                       out bool jumpPressed, out Vector2 move);

            Step(Time.deltaTime, jumpPressed, move);
        }

        /// <summary>
        /// Teardown: let go LOCALLY, and say nothing.
        ///
        /// Not <see cref="Free"/>. This is reached when the body is destroyed or its chunk unloads,
        /// and at that moment a broadcast has no relay left to leave from. Releasing is the half
        /// that can be done cleanly, and it is the half that matters — a body left limp with
        /// nothing alive to stand it up is a player who cannot move for the rest of the session.
        /// </summary>
        private void OnDisable()
        {
            Release();
            input.Release();
        }

        /// <summary>
        /// Give the ragdoll its claim back and stop everything this bottling was running.
        ///
        /// <para>
        /// <b>One claim, not the limpness.</b> <c>PlayerRagdoll</c> holds a SET of claims, so a
        /// player a net and a container both have hold of stays down until the last one lets go —
        /// which is what makes the three restraints compose with no bookkeeping here at all.
        /// </para>
        /// </summary>
        private void Release()
        {
            if (!bottled) return;

            bottled = false;

            this.NetOff(NetMsg.SnareStruggled, OnStruggleReported);
            this.NetOff(NetMsg.Released, OnReleaseAnnounced);

            HealthComponent vitals = Vitals;
            if (vitals != null) vitals.OnDeath -= OnBottledPlayerDied;

            if (container != null) container.MarkToldFull(false);

            PlayerRagdoll ragdoll = Body;
            if (ragdoll != null) ragdoll.ReleaseHold(this);

            container = null;
            settings = null;
            meter = null;
            sendMeter = null;
            remaining = 0f;
            input.ForgetHeading();
            input.Release();

            Freed?.Invoke(this);
        }

        // ── Resolved on demand ─────────────────────────────────────────────────
        //
        // From the PARENT and never cached in an Awake this component does not have. Both reasons
        // are SnaredBody's: AddComponent raises no Awake outside play mode, and a body whose
        // collider is on a child is not the object the adapter sits on.

        private PlayerRagdoll Body =>
            body != null ? body : body = GetComponentInParent<PlayerRagdoll>();

        private HealthComponent Vitals =>
            health != null ? health : health = GetComponentInParent<HealthComponent>();
    }
}
