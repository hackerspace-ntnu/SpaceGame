using System;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    /// <summary>
    /// One trigger on one slot: the whole request → present → authority → broadcast pipeline for a
    /// press, and the 15 Hz hold stream behind it, for a single <see cref="UsableItem"/>.
    ///
    /// <para>
    /// This used to be the body of <c>EquipmentController</c>, written once for the one thing a
    /// player could hold. A player now fires four things — the hand, two gauntlets and whatever is
    /// on their back — and each has its own button, its own hold state and its own place in the
    /// use messages. Extracted rather than copied, so every artifact keeps replicating because of
    /// exactly one pipeline, and a fix to it fixes all four triggers.
    /// </para>
    /// <para>
    /// The channel does not register for messages itself. Its owner receives the four
    /// <see cref="NetMsg"/>s on the player's relay and forwards the ones whose <c>NetArg.A</c>
    /// names this channel's slot (<see cref="Owns"/>), because two components on the same player
    /// cannot both own a message id on the same relay.
    /// </para>
    /// </summary>
    public sealed class UseChannel
    {
        /// <summary>
        /// How often a held item's aim goes on the wire. 15 Hz rather than every frame: the aim is
        /// the only thing in the tick that changes, peers smooth between ticks anyway, and the
        /// machine that would notice a faster rate — the owner's — does not use the messages at
        /// all. It reads its own camera live.
        /// </summary>
        public const float HoldSendInterval = 1f / 15f;

        /// <summary>
        /// How often a hold whose description has NOT changed still goes out. A steady aim used to
        /// cost the full 15 Hz, on every machine, for a message identical to the last one. It
        /// cannot be zero: LaserStaff, Lasso and GrapplingHook all extinguish a hold that has not
        /// been renewed within their <c>holdTimeout</c> (0.5 s at the tightest), on the server and
        /// on every presenting peer alike, so an unchanged hold has to keep arriving well inside
        /// that.
        /// </summary>
        public const float HoldKeepAliveInterval = 0.2f;

        private readonly Component host;
        private readonly GearArea area;
        private readonly Func<GearRef> slot;
        private readonly Func<UsableItem> item;

        /// <summary>Are hold ticks streaming? Not the same as the button being down — see <see cref="useButtonDown"/>.</summary>
        private bool useHeld;

        /// <summary>
        /// Is the trigger physically down?
        ///
        /// Tracked apart from <see cref="useHeld"/> because a self-timed item — one whose
        /// <see cref="UsableItem.WantsHold"/> is true — outlives the press, and the stream has to
        /// keep running while it does. Collapsing the two back into one flag is what makes a
        /// three-second burst freeze its aim the moment the player lets go.
        /// </summary>
        private bool useButtonDown;

        // The last hold that reached the wire, so an identical one is only re-sent as a keepalive.
        private NetArg lastSentHold;
        private bool hasSentHold;
        private float nextHoldKeepAlive;

        private float nextHoldSend;

        /// <param name="host">The player component the messages are sent through and simulated for.</param>
        /// <param name="area">Which slot list this channel fires for; decides which messages it <see cref="Owns"/>.</param>
        /// <param name="slot">The slot a press is stamped with, read at press time — the hotbar's moves with the selection.</param>
        /// <param name="item">The item currently in that slot, or null.</param>
        public UseChannel(Component host, GearArea area, Func<GearRef> slot, Func<UsableItem> item)
        {
            this.host = host;
            this.area = area;
            this.slot = slot;
            this.item = item;
        }

        public UsableItem Item => item();

        /// <summary>
        /// The item's use was just shown on THIS machine — the owner's own press, or a peer's copy
        /// of it. Raised beside <see cref="UsableItem.PlayUse"/>, so anything that dresses a use
        /// (the arm that comes up behind a gauntlet) happens on every machine that sees the item
        /// fire, and never on the server alone.
        /// </summary>
        public event Action Presented;

        /// <summary>A hold tick was shown on this machine; false is the release. See <see cref="Presented"/>.</summary>
        public event Action<bool> HoldPresented;

        /// <summary>Is this message about this channel's list? Stale slots within the list are the guards' job.</summary>
        public bool Owns(int code) => UseSlotCode.AreaOf(code) == area;

        /// <summary>
        /// The slot the owner fired must still be the slot in question. Without this a stale
        /// request that crossed a hotbar switch fires the wrong artifact on the server only.
        /// </summary>
        private bool Stale(in NetArg arg) => arg.A >= 0 && UseSlotCode.Decode(arg.A) != slot();

        private GameObject Holder => host.gameObject;

        // ── Owner side ─────────────────────────────────────────────────────────

        /// <summary>Owner pressed the trigger.</summary>
        public void Press()
        {
            UsableItem usable = item();
            if (usable == null) return;

            // The owner describes the use — chiefly where they aimed, which is knowable only here.
            var arg = new NetArg { A = UseSlotCode.Encode(slot()) };
            usable.OnRequestUse(ref arg);

            // Presented immediately, always, so no item ever feels like it is waiting for a reply.
            usable.PlayUse(Holder, arg);
            Presented?.Invoke();

            // An owner-authoritative tool is ours to run, right now — its effect is this player's
            // own body, which already replicates through the transform they own.
            if (usable.Authority == UseAuthority.Owner)
                usable.TryUse(Holder, arg);

            // Either way the server hears about it, because only the server can reach the peers.
            host.NetToServer(NetMsg.UseItem, arg);

            // A continuous item's press is also the start of its hold. The first tick goes out on
            // the next Tick rather than here, so that start and sustain travel through one code
            // path and cannot describe the aim two different ways.
            if (usable.IsContinuous)
            {
                useHeld = true;
                useButtonDown = true;
                nextHoldSend = 0f;
                hasSentHold = false;
            }
        }

        /// <summary>
        /// Owner let go.
        ///
        /// A self-timed item is not finished just because the finger came up, and cutting the
        /// stream here would strand every other machine on the aim it had at the press. Its stream
        /// ends in <see cref="Tick"/>, when the item itself says it is done.
        /// </summary>
        public void Release()
        {
            useButtonDown = false;

            UsableItem usable = item();
            if (usable != null && usable.IsContinuous && usable.WantsHold) return;

            EndHold(send: true);
        }

        /// <summary>
        /// Owner side, once per frame: keep the aim flowing while the trigger is down.
        ///
        /// Guarded on the item still being the continuous one that started the hold — swapping
        /// slots mid-beam otherwise leaves this streaming hold ticks at whatever is in the slot
        /// now, and the owner's EndHold would have nothing left to switch off.
        /// </summary>
        public void Tick(float now)
        {
            if (!useHeld) return;

            UsableItem usable = item();
            if (usable == null || !usable.IsContinuous)
            {
                EndHold(send: true);
                return;
            }

            // The button is up and the item has stopped asking. For an ordinary held item this is
            // never reached — Release already ended it — so this is the self-timed item's release,
            // arriving whenever the item decided rather than whenever the finger did.
            if (!useButtonDown && !usable.WantsHold)
            {
                EndHold(send: true);
                return;
            }

            if (now < nextHoldSend) return;
            nextHoldSend = now + HoldSendInterval;

            SendHold(usable, active: true);
        }

        /// <summary>
        /// Stop the beam. Safe to call from anywhere, including twice.
        ///
        /// <paramref name="send"/> is false only where the network cannot be trusted to still be
        /// there — a component being disabled during death or teardown.
        /// </summary>
        public void EndHold(bool send)
        {
            if (!useHeld) return;
            useHeld = false;
            useButtonDown = false;

            UsableItem usable = item();
            if (usable == null) return;

            if (send)
            {
                SendHold(usable, active: false);
                return;
            }

            usable.PlayHold(Holder, default, active: false);
            HoldPresented?.Invoke(false);
            if (usable.Authority == UseAuthority.Owner)
                usable.TryHold(Holder, default, active: false);
        }

        /// <summary>One tick of a hold, down the same three routes a press takes.</summary>
        private void SendHold(UsableItem usable, bool active)
        {
            var arg = new NetArg
            {
                A = UseSlotCode.Encode(slot()),
                B = active ? 1 : 0,
            };

            usable.OnRequestHold(ref arg, active);

            usable.PlayHold(Holder, arg, active);
            HoldPresented?.Invoke(active);

            if (usable.Authority == UseAuthority.Owner)
                usable.TryHold(Holder, arg, active);

            // The local side above runs every tick regardless; only the wire is spared. A hold
            // that describes exactly what the last one did — same slot, same state, same aim —
            // goes out only as a keepalive. A release (active false) is never held back.
            float now = Time.time;
            bool unchanged = hasSentHold && active && SameHold(arg, lastSentHold);
            if (unchanged && now < nextHoldKeepAlive) return;

            lastSentHold = arg;
            hasSentHold = active;
            nextHoldKeepAlive = now + HoldKeepAliveInterval;

            host.NetToServer(NetMsg.UseItemHold, arg);
        }

        private static bool SameHold(in NetArg a, in NetArg b) =>
            a.A == b.A && a.B == b.B && a.P == b.P && a.R == b.R;

        // ── Server and peer side ───────────────────────────────────────────────

        /// <summary>Server side: run the effect if it is the server's to run, then tell the peers.</summary>
        public void OnUseRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(host)) return;

            UsableItem usable = item();
            if (usable == null) return;
            if (Stale(arg)) return;

            if (usable.Authority == UseAuthority.Server)
                usable.TryUse(Holder, arg);

            // Everyone except the machine that already presented it locally.
            host.NetToOthers(NetMsg.ItemUsed, arg, except: sender);
        }

        /// <summary>Peer side: cosmetics only. The effect happened on the server.</summary>
        public void OnUsedElsewhere(in NetArg arg, ulong sender)
        {
            UsableItem usable = item();
            if (usable == null) return;

            usable.PlayUse(Holder, arg);
            Presented?.Invoke();
        }

        /// <summary>Server side: the same shape as <see cref="OnUseRequested"/>, per tick.</summary>
        public void OnHoldRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(host)) return;

            UsableItem usable = item();
            if (usable == null) return;

            // Same stale-slot guard as a press. A hold that crossed a slot switch would otherwise
            // keep an artifact the player is no longer holding running on the server.
            if (Stale(arg)) return;

            bool active = arg.B != 0;

            if (usable.Authority == UseAuthority.Server)
                usable.TryHold(Holder, arg, active);

            host.NetToOthers(NetMsg.ItemUseHeld, arg, except: sender);
        }

        /// <summary>Peer side: cosmetics only, exactly as with a press.</summary>
        public void OnHeldElsewhere(in NetArg arg, ulong sender)
        {
            UsableItem usable = item();
            if (usable == null) return;

            bool active = arg.B != 0;
            usable.PlayHold(Holder, arg, active);
            HoldPresented?.Invoke(active);
        }
    }
}
