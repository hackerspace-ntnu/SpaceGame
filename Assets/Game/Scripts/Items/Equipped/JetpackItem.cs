// The jetpack: two vectoring motors worn on the back that fly the player's own body.
//
// The item is a switch and a pair of pods. All of the flying is JetpackFlight on the player's own
// body; all of the looking-like-flying is JetpackNozzles and JetpackPose. What is here is the
// gesture, the rule about when it is allowed, putting the three of them on and off the wearer, and
// the one stream of numbers a peer cannot work out for itself.
using SpaceGame.Characters;
using SpaceGame.Gear.Jetpack;
using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Worn in the torso slot, fired by a double tap of Space — the third <c>EquipKind.Back</c>
    /// item, so it is mutually exclusive with the wing pack and the wingsuit for free: there is
    /// one torso slot and all three want it.
    ///
    /// <para>
    /// Owner-authoritative. The whole effect is the holder's own body, and the player's
    /// <c>NetworkTransform</c> is owner-authoritative: a flight begun on the server would be
    /// overwritten by the owner's next state update, silently, which is the mistake
    /// <c>UsableItem</c> warns about for exactly this shape of item.
    /// </para>
    /// <para>
    /// <b>It is continuous, and that is what puts it on the wire.</b> A double tap presses and
    /// releases in the same breath, so there is no button held down to stream behind — but
    /// <c>UseChannel.Release</c> steps aside for an item that is <see cref="IsContinuous"/> and
    /// still <see cref="WantsHold"/>, and the stream then runs until the item says it is done.
    /// The jetpack says so when the flight ends. That gives it a 15 Hz channel for the whole of a
    /// flight and exactly no new message.
    /// </para>
    /// </summary>
    public class JetpackItem : UsableItem, IItemDeferredRestore
    {
        [Tooltip("The pods: the vectoring parts, the four flames, the heat glow and the smoke. On " +
                 "this prefab.")]
        [SerializeField] private JetpackNozzles nozzles;

        [Tooltip("How fast the shown nozzle angle catches up to the streamed one, per second. It " +
                 "is here rather than on the flight because it is a DRAWING rate: a peer hears " +
                 "the angle 15 times a second and the pods must not step between the ticks.")]
        [SerializeField, Min(0.01f)] private float showResponse = 14f;

        private JetpackFlight flight;
        private JetpackPose pose;

        // What the pods are being drawn at, and what they are heading for. The owner sets the
        // target from its live flight every frame; a peer sets it from each hold tick.
        private JetNozzle shownNozzle;
        private JetNozzle targetNozzle;
        private float shownThrottle;
        private float targetThrottle;
        private float shownHeat;
        private float targetHeat;
        private bool showing;

        /// <summary>Owner-authoritative: the effect is the holder's own body and nothing else.</summary>
        public override UseAuthority Authority => UseAuthority.Owner;

        /// <summary>Worn, not gripped — the hands stay free.</summary>
        protected override bool UsesHoldPose => false;

        /// <summary>Always, so a press can open a stream. See the class summary.</summary>
        public override bool IsContinuous => true;

        /// <summary>
        /// Keep streaming for as long as the motors are lit. This is the whole of the jetpack's
        /// netcode: it is what makes the stream outlive the double tap that started it, and what
        /// ends it when the pack is stowed or lands.
        /// </summary>
        public override bool WantsHold => flight != null && flight.IsFlying;

        // ── Wearing ────────────────────────────────────────────────────────────

        /// <summary>
        /// Strap the pack on. Runs on every machine, because a worn instance is derived from
        /// replicated slot state rather than sent — so the pods and the pose exist for a peer
        /// watching somebody else fly, and only the flight itself is owner-only.
        /// </summary>
        public override void OnEquipped(GameObject holder)
        {
            base.OnEquipped(holder);
            if (holder == null) return;

            pose = holder.GetComponent<JetpackPose>();
            if (pose == null) pose = holder.AddComponent<JetpackPose>();

            // The flight writes velocity onto a body only its owner may move. On a peer it would
            // be a second simulation of somebody else's flight, fighting the replicated one.
            if (OwnerIsLocal())
            {
                flight = holder.GetComponent<JetpackFlight>();
                if (flight == null) flight = holder.AddComponent<JetpackFlight>();

                // Heat captured before this component existed. SetHeat rather than Resume:
                // Resume LAUNCHES, and a pack restored merely hot must not take off on its own.
                flight.SetHeat(pendingHeat);
            }

            if (nozzles != null)
            {
                nozzles.SetWearer(holder.transform);
                nozzles.WarnFraction = WarnFraction;
            }
        }

        /// <summary>
        /// Take it off, and put back everything it changed. Reached from a slot swap, a drop, a
        /// death and a despawn alike, so it has to be safe from all four.
        /// </summary>
        public override void OnUnequipped(GameObject holder)
        {
            // Before anything is destroyed: End hands the body back to PlayerMovement, and a pack
            // taken off in mid-air must not leave it switched off.
            if (flight != null)
            {
                flight.End();
                Destroy(flight);
                flight = null;
            }

            if (pose != null)
            {
                Destroy(pose);
                pose = null;
            }

            if (nozzles != null) nozzles.SetWearer(null);

            base.OnUnequipped(holder);
        }

        /// <summary>The heat fraction the pods start glowing and smoking at. One number, shared
        /// with the visor gauge, so what the player sees and what the gauge draws agree.</summary>
        private float WarnFraction =>
            flight != null ? flight.Config.WarnFraction : 0.7f;

        // ── The gesture ────────────────────────────────────────────────────────

        /// <summary>
        /// A double tap of Space lights the motors. It does NOT put them out again.
        ///
        /// <para>
        /// <b>Legal from standing</b>, unlike the other two back items. The wing pack and the
        /// wingsuit refuse on the ground because they need air to work in; a vertical takeoff is
        /// the jetpack's whole point, so there is no ground rule here at all.
        /// </para>
        /// <para>
        /// The one refusal is an overheated pack, and it is deliberately not silent: "nothing
        /// happened" is indistinguishable from a broken item, and this is the one state in which
        /// the pack legitimately does nothing.
        /// </para>
        /// </summary>
        protected override bool CanUse()
        {
            if (!base.CanUse()) return false;
            if (owner == null || flight == null) return false;

            // A press mid-flight is legal but does nothing (see Use): the same key is the
            // throttle, so refusing it here would fight the pilot's own hand rather than say
            // anything.
            if (flight.IsFlying) return true;

            if (flight.Heat.Overheated)
            {
                Debug.Log("Jetpack: overheated — let it cool before relighting.", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Owner side, because <see cref="Authority"/> is Owner. It lights the pack, and that is
        /// all it does.
        ///
        /// <para>
        /// <b>Deliberately not a toggle.</b> Space is the throttle: a flight is climb, let go,
        /// climb, so a pilot working the pack presses Space twice inside the 0.3 s double-tap
        /// window constantly, and a toggle here would read that as "shut the motors off" and drop
        /// them out of the sky. The one gesture cannot mean both "fly" and "stop flying" once it
        /// is also the throttle. Landing ends a flight; letting go of Space is how the pilot gets
        /// there.
        /// </para>
        /// </summary>
        protected override void Use()
        {
            if (flight == null || flight.IsFlying) return;

            flight.Begin();
        }

        // ── The stream ─────────────────────────────────────────────────────────

        /// <summary>
        /// Owner side, before each hold tick leaves: the three things a peer cannot derive.
        ///
        /// <para>
        /// Throttle and heat go in <c>P</c>; the nozzle deflection goes in <c>R</c>, where it
        /// belongs, because it literally is a rotation. None of it can be measured on the other
        /// end — a remote body is kinematic and reads zero velocity, and heat is not a fact about
        /// motion at all.
        /// </para>
        /// </summary>
        public override void OnRequestHold(ref NetArg arg, bool active)
        {
            if (flight == null) return;

            arg.P = new Vector3(flight.ThrottleFraction, flight.HeatFraction, 0f);
            arg.R = flight.Nozzle.Rotation;
        }

        /// <summary>
        /// Every machine: point the pods where the tick says they are.
        ///
        /// The owner overwrites all of this from its own live flight in <see cref="Update"/>, so
        /// this is what a PEER runs on. The release tick (<c>active</c> false) is the flames going
        /// out and must be handled even on a machine that saw none of the ticks before it.
        /// </summary>
        protected override void PresentHold(NetArg arg, bool active)
        {
            if (!active)
            {
                showing = false;
                targetThrottle = 0f;
                targetNozzle = JetNozzle.Vertical;
                return;
            }

            showing = true;
            targetThrottle = arg.P.x;
            targetHeat = arg.P.y;
            targetNozzle = JetNozzle.FromRotation(arg.R);
        }

        /// <summary>
        /// Drive the pods and the pose, on every machine.
        ///
        /// <para>
        /// The owner reads its live flight here rather than waiting for its own hold tick, so its
        /// pods move at frame rate rather than at 15 Hz; a peer keeps whatever the last tick left
        /// and eases toward it. Both go through the same smoothing, so the machine looks the same
        /// on both sides and there is no "owner path" to diverge.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (flight != null)
            {
                showing = flight.IsFlying;
                targetNozzle = flight.Nozzle;
                targetThrottle = flight.ThrottleFraction;
                targetHeat = flight.HeatFraction;
            }

            float t = 1f - Mathf.Exp(-showResponse * Time.deltaTime);

            shownNozzle = new JetNozzle
            {
                Pitch = Mathf.Lerp(shownNozzle.Pitch, targetNozzle.Pitch, t),
                Roll = Mathf.Lerp(shownNozzle.Roll, targetNozzle.Roll, t),
            };
            shownThrottle = Mathf.Lerp(shownThrottle, targetThrottle, t);

            // Heat is NOT smoothed toward zero when the stream stops: an unworn or stowed pack
            // still has whatever heat it has, and fading the glow out would say it had cooled.
            shownHeat = Mathf.Lerp(shownHeat, targetHeat, t);

            if (nozzles != null)
            {
                nozzles.Nozzle = shownNozzle;
                nozzles.Throttle = shownThrottle;
                nozzles.Heat = shownHeat;
                nozzles.WarnFraction = WarnFraction;
            }

            if (pose != null)
            {
                pose.Active = showing;
                pose.Nozzle = shownNozzle;
                pose.Throttle = shownThrottle;
            }
        }

        // ── Persistence ────────────────────────────────────────────────────────
        //
        // The pack itself is saved by the body slot it sits in. Two things here need a bag: the
        // heat, because otherwise a quicksave is free coolant and the whole budget is optional,
        // and a flight in progress, because without it a mid-air save reloads standing still in
        // the sky — the mistake OrnithopterSaveable made first and the wingsuit answers in the
        // deferred pass.

        private const string HeatKey = "heat";
        private const string OverheatKey = "hot";
        private const string FlightKey = "jet";
        private const string NozzleKey = "noz";

        private JetpackHeat pendingHeat = JetpackHeat.Cold;
        private bool pendingFlight;
        private Vector3 pendingVelocity;
        private JetNozzle pendingNozzle;

        public bool HasPendingRestore => pendingFlight;

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // Runs BEFORE OnUnequipped, so the flight is still here to be asked. A pack that is
            // merely being carried has no flight and reports the heat it was restored with.
            JetpackHeat heat = flight != null ? flight.Heat : pendingHeat;

            state.Set(HeatKey, heat.Value);
            if (heat.Overheated) state.Set(OverheatKey, true);

            if (flight == null || !flight.IsFlying) return;

            var body = flight.GetComponent<Rigidbody>();
            if (body != null) state.Set(FlightKey, body.linearVelocity);

            state.Set(NozzleKey, new Vector3(flight.Nozzle.Pitch, flight.Nozzle.Roll, 0f));
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            pendingHeat = JetpackHeat.Cold;
            pendingFlight = false;
            pendingVelocity = Vector3.zero;
            pendingNozzle = JetNozzle.Vertical;

            if (state == null) return;

            pendingHeat = new JetpackHeat
            {
                Value = state.GetFloat(HeatKey),
                Overheated = state.GetBool(OverheatKey),
            };

            if (!state.Has(FlightKey)) return;

            pendingVelocity = state.GetVector3(FlightKey);
            Vector3 saved = state.GetVector3(NozzleKey);
            pendingNozzle = new JetNozzle { Pitch = saved.x, Roll = saved.y };
            pendingFlight = true;
        }

        /// <summary>
        /// Runs after every saver has restored, and more than once — so it holds its pending state
        /// until the flight component actually exists, and clears it the moment it is spent.
        ///
        /// The heat goes on in <c>OnEquipped</c> rather than here, because a pack that was merely
        /// hot has no deferred work to do and would otherwise stay cold until this happened to run.
        /// </summary>
        public void TryCompleteRestore()
        {
            if (!pendingFlight || flight == null) return;

            flight.Resume(pendingVelocity, pendingHeat, pendingNozzle);
            pendingFlight = false;
        }
    }
}
