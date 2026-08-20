using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// A control on the dune foiler's deck. Look at it and work it: <b>E pays out / raises</b>,
    /// <b>left click hauls in / lowers</b>.
    ///
    /// Two buttons rather than one toggle, because every control here runs both ways and a toggle
    /// would make the player guess which way the next press goes.
    ///
    /// Holding either button keeps the control moving, so rope pays out continuously instead of in
    /// clicks. <see cref="Interactor"/> only fires on the press, so the hold is tracked here: a
    /// press starts it, and it runs until the button is released or the player looks away.
    ///
    /// Lives outside the DuneFoil assembly because <see cref="IInteractable"/> and
    /// <see cref="PlayerInputManager"/> are in the default assembly, the same split
    /// <c>DesertCrawlerDriver</c> uses against <c>DesertCrawlerLocomotion</c>.
    /// </summary>
    // No [RequireComponent(typeof(Collider))]: the collider is a child "Handle" placed at chest
    // height above the deck, not one wrapped around this node. Requiring it here would also break
    // AddComponent outright — Collider is abstract, so Unity cannot satisfy the requirement and
    // returns null instead of the component.
    public class DuneFoilRiggingStation : MonoBehaviour, IInteractable, ISecondaryInteractable,
                                          IInteractionReadout
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

        // --- IInteractionReadout ----------------------------------------------
        // Drawn by InteractionPromptUI whenever the crosshair is on this handle. Four winches within
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
            StationFunction.Sheet => "E: ease sheet   LMB: haul in   (hold)",
            StationFunction.Hoist => "E: raise sail   LMB: lower sail   (hold)",
            StationFunction.MastCant => "E: lean to starboard   LMB: lean to port   (hold)",
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

        public bool CanInteract() => rig != null;

        public void Interact(Interactor interactor)
        {
            easeUntil = Time.time + tapDuration;
            Apply(more: true, seconds: tapStep);   // act on the press, not on the next frame
        }

        // --- click: the "less" direction --------------------------------------

        public bool CanSecondaryInteract() => rig != null;

        public void SecondaryInteract(Interactor interactor)
        {
            trimUntil = Time.time + tapDuration;
            Apply(more: false, seconds: tapStep);
        }

        // ----------------------------------------------------------------------

        private void Update()
        {
            if (rig == null) return;

            bool easing = Time.time <= easeUntil;
            bool trimming = Time.time <= trimUntil;

            // A press of each in the same frame is a wash rather than a fight.
            if (easing == trimming) return;

            Apply(easing, Time.deltaTime);
        }

        /// <summary>
        /// Move this control. Called once on the press and then every frame the press is held.
        ///
        /// Acting on the press matters: with the effect deferred to the next <see cref="Update"/>,
        /// a tap does nothing at all if anything interrupts the frame, and the control feels dead
        /// even when it is wired up correctly.
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
