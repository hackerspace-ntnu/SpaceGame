using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;

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
public class DuneFoilRiggingStation : MonoBehaviour, IInteractable, ISecondaryInteractable
{
    /// <summary>What this station does.</summary>
    public enum StationFunction
    {
        /// <summary>E pays out sheet, click hauls it in. The steering controls.</summary>
        Sheet,

        /// <summary>E sets every sail, click furls the lot.</summary>
        Hoist,

        /// <summary>E rakes the post aft, click rakes it forward.</summary>
        MastRake,
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

    /// <summary>Prompt text, if anything ever wants to show one.</summary>
    public string Prompt => function switch
    {
        StationFunction.Sheet => "E: ease sheet   LMB: haul in",
        StationFunction.Hoist => "E: set sail   LMB: furl",
        StationFunction.MastRake => "E: rake aft   LMB: rake forward",
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
                // Discrete, so the hold adds nothing; the press is the whole action.
                if (more) rig.HoistAll();
                else rig.FurlAll();
                break;

            case StationFunction.MastRake:
                if (sail == null) return;
                if (more) sail.RakeAft(seconds);
                else sail.RakeForward(seconds);
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
