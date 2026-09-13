using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// A control on the dune foiler's deck. Look at it and work it: <b>E pays out / raises</b>,
    /// <b>left click hauls in / lowers</b>.
    ///
    /// Two buttons rather than one toggle, because every control here runs both ways and a toggle
    /// would make the player guess which way the next press goes.
    ///
    /// A press keeps the control moving for a moment after it, so rope pays out in a smooth run
    /// rather than a click. <see cref="Interactor"/> only fires on the press, so the run is tracked
    /// here: a press starts it and it stops itself.
    ///
    /// Lives outside the DuneFoil assembly because <see cref="IInteractable"/> and
    /// <see cref="PlayerInputManager"/> are in the default assembly, the same split
    /// <c>DesertCrawlerDriver</c> uses against <c>DesertCrawlerLocomotion</c>.
    ///
    /// ── Crewed by more than one person ──
    /// A winch is a <see cref="VehicleStation"/>, so a press is a momentary CLAIM: the server runs
    /// the winch for the length of one press and publishes where it ended up, and every machine puts
    /// the sail there. That is what makes the trim shared — before it, easing the main moved the
    /// boom on the easer's screen and nowhere else, and each machine then sailed a differently
    /// trimmed boat.
    ///
    /// Two things about that are deliberate. The claim EXPIRES rather than needing a stand-down, so
    /// a player who drops mid-haul cannot hold a winch for the rest of the voyage. And the winch is
    /// not exclusive: two crew both hauling on the same sheet is a crew, not a conflict, and
    /// refusing the second press would silently eat half of them.
    /// </summary>
    // No [RequireComponent(typeof(Collider))]: the collider is a child "Handle" placed at chest
    // height above the deck, not one wrapped around this node. Requiring it here would also break
    // AddComponent outright — Collider is abstract, so Unity cannot satisfy the requirement and
    // returns null instead of the component.
    public class DuneFoilRiggingStation : VehicleStation, ISecondaryInteractable, IInteractionReadout
    {
        /// <summary>What this station does.</summary>
        public enum StationFunction
        {
            /// <summary>E pays out sheet, click hauls it in. Sets how hard the sail drives.</summary>
            Sheet,

            /// <summary>E works every halyard up, click works them down. Held, not tapped.</summary>
            Hoist,

            /// <summary>
            /// E leans the post to starboard, click leans it to port.
            ///
            /// The post leans ACROSS the hull, not fore and aft. Leaning it into the wind stands
            /// the craft up under a press of sail that would otherwise have it on its ear;
            /// leaning it to leeward lies the craft down and bears the bow away. Either way it
            /// costs drive, because a leaning sail presents less of itself to the wind — which is
            /// the trade the control exists to offer.
            /// </summary>
            MastCant,
        }

        [Header("Station")]
        [SerializeField] private StationFunction function = StationFunction.Sheet;

        [Tooltip("The rig this station belongs to. Found in the parents when empty.")]
        [SerializeField] private SailRig rig;

        [Tooltip("Sail this station works. Ignored by the Hoist station, which works all of them.")]
        [SerializeField] private SailSurface sail;

        [Header("Feel")]
        [Tooltip("Seconds a press keeps the control moving after the button goes up. Small: it is " +
                 "there so a tap does something, not to add lag.")]
        [SerializeField, Min(0.01f)] private float tapDuration = 0.18f;

        [Tooltip("How much a single tap moves a continuous control, in seconds of travel. Applied " +
                 "the instant the button goes down.")]
        [SerializeField, Min(0.0f)] private float tapStep = 0.08f;

        // The local prediction of the run the server is about to make. Only the machine that pressed
        // ever runs one; everybody else takes the published value.
        private float easeUntil;
        private float trimUntil;

        /// <summary>True while a press is still driving this control in the "more" direction.</summary>
        public bool IsEasing => Time.time <= easeUntil;

        /// <summary>True while a press is still driving it the other way.</summary>
        public bool IsTrimming => Time.time <= trimUntil;

        /// <summary>What this station does, for prompts and the builder.</summary>
        public StationFunction Function => function;

        /// <summary>The sail it works, if it works one.</summary>
        public SailSurface Sail => sail;

        // --- What kind of station this is -------------------------------------

        /// <summary>Two crew on one sheet is a crew, not a fight over a wheel.</summary>
        protected override bool Exclusive => false;

        /// <summary>
        /// The claim lasts exactly as long as the run one press makes, then lapses. No stand-down
        /// message exists for a winch, which is also why a dropped connection cannot leave one
        /// turning.
        /// </summary>
        protected override float ClaimTimeout => tapDuration;

        /// <summary>The part of a tap that lands the instant the button goes down.</summary>
        protected override float ImmediateStep => tapStep;

        // --- IInteractionReadout ----------------------------------------------
        // Drawn by VisorReticle's info box whenever the crosshair is on this handle. Four winches within
        // a couple of metres of each other, all answering the same two buttons, are unusable
        // without this — which is how the rig shipped.

        /// <summary>Which control this is.</summary>
        public string Label => function switch
        {
            StationFunction.Sheet => sail != null && rig != null && sail == rig.Jib
                ? "Jib sheet"
                : "Main sheet",
            StationFunction.Hoist => "Halyards",
            StationFunction.MastCant => "Mast cant",
            _ => "Rigging",
        };

        /// <summary>What the two buttons do here.</summary>
        public string Prompt => function switch
        {
            StationFunction.Sheet => "RMB: ease sheet   LMB: haul in   (hold)",
            StationFunction.Hoist => "RMB: raise sail   LMB: lower sail   (hold)",
            StationFunction.MastCant => "RMB: lean to starboard   LMB: lean to port   (hold)",
            _ => string.Empty,
        };

        /// <summary>Where this control currently sits, for the HUD's fill bar.</summary>
        public float? Value01 => function switch
        {
            StationFunction.Sheet => sail != null ? sail.SheetOut : (float?)null,
            StationFunction.Hoist => rig != null ? rig.Hoist01 : (float?)null,
            // Upright reads as half, so the bar sits in the middle and both directions are
            // visible on it — the same convention the rudder gauge uses.
            StationFunction.MastCant => sail != null ? sail.Cant01 : (float?)null,
            _ => null,
        };

        /// <summary>The same, in units the player thinks in.</summary>
        public string ValueText => function switch
        {
            StationFunction.Sheet => sail == null ? string.Empty
                : sail.SheetOut < 0.02f ? "hard in"
                : sail.SheetOut > 0.98f ? "right out"
                : $"{sail.SheetOut * 100f:F0}% out ({sail.BoomAngle:F0}°)",
            StationFunction.Hoist => rig == null ? string.Empty : $"{rig.Hoist01 * 100f:F0}% set",
            StationFunction.MastCant => sail == null ? string.Empty
                : Mathf.Abs(sail.CantAngle) < 1f ? "upright"
                : sail.CantAngle < 0f ? $"{-sail.CantAngle:F0}° to port"
                                      : $"{sail.CantAngle:F0}° to starboard",
            _ => string.Empty,
        };

        private void Awake()
        {
            if (rig == null) rig = GetComponentInParent<SailRig>();
            if (rig == null)
            {
                Debug.LogWarning($"[{nameof(DuneFoilRiggingStation)}] {name} has no SailRig above " +
                                 "it; this control will do nothing.", this);
            }
        }

        // --- E: the "more" direction ------------------------------------------

        public override bool CanInteract() => rig != null;

        public override void Interact(Interactor interactor) => Work(interactor, more: true);

        // --- click: the "less" direction --------------------------------------

        public bool CanSecondaryInteract() => rig != null;

        public void SecondaryInteract(Interactor interactor) => Work(interactor, more: false);

        /// <summary>
        /// One press: predict it here, and ask the server for it.
        ///
        /// The prediction is not a nicety. Acting on the press is what the control has always done,
        /// and deferring it — to the next frame before, to a round trip now — is what makes a winch
        /// feel dead even when it is wired up correctly. The server's answer arrives a moment later
        /// carrying an absolute position, so the correction is a few centimetres of sheet on a boom
        /// that is already swinging toward it: invisible, and self-limiting, because the value on
        /// the wire never depends on how many messages this machine did or did not see.
        /// </summary>
        private void Work(Interactor interactor, bool more)
        {
            if (rig == null) return;

            if (more) easeUntil = Time.time + tapDuration;
            else trimUntil = Time.time + tapDuration;

            // Act on the press, not on the next frame — but only where somebody else is the
            // authority. On the host and offline the send below is delivered inline on this very
            // frame and the server path applies the same step, so predicting here as well would
            // move the winch twice per press and run it at double rate for as long as it is held.
            if (Predicting) Apply(more, tapStep);

            RequestClaim(interactor, more ? 1f : -1f);
        }

        /// <summary>
        /// Is this machine guessing rather than deciding?
        ///
        /// The authority — the server, or the only machine there is offline — runs this winch in
        /// <see cref="AdvanceOnServer"/> and must not run it a second time as a prediction.
        /// Everybody else predicts, and is corrected by the position the authority publishes.
        /// </summary>
        private bool Predicting => !Network.Simulates(this);

        // ----------------------------------------------------------------------

        protected override void Update()
        {
            // Not optional: the base's Update is what runs the winch on the server and publishes it.
            base.Update();

            if (rig == null) return;

            bool easing = Time.time <= easeUntil;
            bool trimming = Time.time <= trimUntil;

            // A press of each in the same frame is a wash rather than a fight.
            if (easing == trimming) return;

            // Prediction only, and only ours to predict. The authority is running the same run for
            // the same length of time and its answer is the one that lands everywhere, including
            // here once this run has lapsed — so a machine that IS the authority must not also run
            // it here, and a machine watching somebody else work the winch must not run it at all.
            if (!Predicting || !IsMannedByLocalPlayer) return;

            Apply(easing, Time.deltaTime);
        }

        /// <summary>
        /// Server side: run the winch for one frame and report where it ended up.
        ///
        /// <paramref name="wanted"/> is the direction the presser asked for, +1 or -1, so this is
        /// the same call the presser is making locally with the same rate — the two only differ by
        /// however much of the run each machine has got through, which is why the value that travels
        /// is the position rather than the movement.
        /// </summary>
        protected override float AdvanceOnServer(float wanted, float deltaTime)
        {
            if (rig != null && Mathf.Abs(wanted) > 0.01f) Apply(wanted > 0f, deltaTime);

            return ReadValue();
        }

        /// <summary>
        /// Where this control sits, read off the rig rather than off a number this station is
        /// holding. It is the same figure <see cref="Value01"/> puts on the HUD bar, so what
        /// travels is what the player is looking at — and, more importantly, a winch nobody has
        /// touched all session still answers a joiner's question with where its sail actually is
        /// rather than with zero.
        /// </summary>
        protected override float ReadValue() => Value01 ?? 0f;

        /// <summary>
        /// Every machine but the presser's: put the control exactly here.
        ///
        /// Absolute, so applying it twice is applying it once and a dropped message costs nothing
        /// but the next tenth of a second. The hoist case sets every sail to the same figure, which
        /// is true of this rig because the halyard station is the only thing that ever moves a
        /// hoist and it always moves all of them together.
        /// </summary>
        protected override void ApplyValue(float position)
        {
            switch (function)
            {
                case StationFunction.Sheet:
                    if (sail != null) sail.SetSheet(position);
                    break;

                case StationFunction.Hoist:
                    if (rig == null) break;
                    foreach (SailSurface s in rig.Sails)
                        if (s != null) s.SetHoist(position);
                    break;

                case StationFunction.MastCant:
                    // Cant01 is the readout's 0..1 form of a -1..1 lean, and it is what travels, so
                    // the wire carries the same number the HUD bar shows.
                    if (sail != null) sail.SetCant(position * 2f - 1f);
                    break;
            }
        }

        /// <summary>Stop predicting. The station is somebody else's now, or nobody's.</summary>
        protected override void OnUnmanned(GameObject player)
        {
            easeUntil = 0f;
            trimUntil = 0f;
        }

        /// <summary>
        /// Move this control. Called once on the press and then every frame the run continues, on
        /// the presser's machine as a prediction and on the server as the truth.
        /// </summary>
        private void Apply(bool more, float seconds)
        {
            if (rig == null) return;

            switch (function)
            {
                case StationFunction.Sheet:
                    if (sail == null) return;
                    if (more) sail.EaseSheet(seconds);
                    else sail.TrimSheet(seconds);
                    break;

                case StationFunction.Hoist:
                    // Continuous, and deliberately so. This was a one-press toggle whose "down"
                    // half called rig.FurlAll() — every sail struck instantly, from a winch 1.5 m
                    // from the jib sheet winch, on the same mouse button, with 0.6 m interaction
                    // spheres. One stray click mid-passage and the cloth was simply gone. Now a
                    // mis-click costs a few centimetres of hoist and you watch it happen.
                    if (more) rig.RaiseHalyards(seconds);
                    else rig.LowerHalyards(seconds);
                    break;

                case StationFunction.MastCant:
                    if (sail == null) return;
                    if (more) sail.CantToStarboard(seconds);
                    else sail.CantToPort(seconds);
                    break;
            }
        }

        // There is deliberately no distance check here.
        //
        // An earlier version measured from Camera.main and refused beyond a few metres. That is both
        // redundant and actively harmful: Interactor already limits its raycast to 5 m *from the
        // player who is interacting*, whereas Camera.main is whatever camera happens to be tagged —
        // a spectator camera, a map camera, an editor preview camera — and when it is not the
        // player's, every control on the craft silently refuses. Range belongs to the interactor,
        // not to the thing being interacted with.

        /// <summary>Wire the station up. Used by the prefab builder.</summary>
        public void Bind(StationFunction stationFunction, SailRig sailRig, SailSurface target)
        {
            function = stationFunction;
            rig = sailRig;
            sail = target;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.9f, 0.8f);
            foreach (Collider c in GetComponentsInChildren<Collider>())
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
