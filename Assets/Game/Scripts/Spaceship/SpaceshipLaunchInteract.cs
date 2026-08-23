using UnityEngine;
using SpaceGame.Gameplay;

/// <summary>
/// Interact script to trigger spaceship launch.
/// Implements IInteractable for the player interaction system.
/// Attach to a GameObject with a Collider to create an interaction point.
///
/// <para>
/// <b>Launching is shared world state.</b> This used to call <c>BeginFlight</c> straight out of
/// <see cref="Interact"/>, which runs on the machine that pressed the key and nowhere else — so
/// one player launched the ship and everybody else watched it sit on the pad. It also set its own
/// <c>hasBeenUsed</c> flag locally, which meant a second player could launch the same ship again.
/// </para>
/// <para>
/// It is now a <see cref="NetLatch"/>, the project's shared helper for exactly this shape: the
/// server decides, every machine is told, and a machine that joined afterwards can ask what it
/// missed. <c>oneWay</c>, because a launch does not come back — the latch models that as a refusal
/// rather than as a flag of its own, so the crosshair, the key and the server's re-check all read
/// the same sentence.
/// </para>
/// <para>
/// With no <c>NetworkObject</c> above it the latch degrades to a local dispatch and this behaves
/// exactly as it did before, which is the deliberate fallback the whole messaging layer is built
/// on — see NetMessaging.cs. Put the button on (or under) a networked ship and it replicates with
/// no further changes.
/// </para>
/// </summary>
public class SpaceshipLaunchInteract : MonoBehaviour, IInteractable, ILatchHost
{
    [SerializeField] private SpaceshipManager targetSpaceship;
    [SerializeField] private bool hasBeenUsed = false;

    private NetLatch latch;

    /// <summary>One button, one latch. See <see cref="ILatchHost"/> for why this has to exist.</summary>
    public int LatchCount => 1;

    private void Awake()
    {
        latch = new NetLatch(this, ApplyLaunch, canChange: () => targetSpaceship != null,
                             oneWay: true);
    }

    // Null-conditional so a latch that failed to construct costs one loud error in Awake rather
    // than one per frame forever after — the same shape DoorInteraction and LeverInteraction use.
    private void OnEnable() => latch?.Enable();

    private void OnDisable() => latch?.Disable();

    /// <summary>
    /// Delegated to the latch, so a ship that has already gone refuses here exactly as it refuses
    /// on the server. A prompt that lights up and then does nothing is the failure
    /// <c>Interactor.IsAvailable</c> exists to avoid.
    /// </summary>
    public bool CanInteract() => targetSpaceship != null && latch != null && latch.Accepts(latch.Next);

    /// <summary>
    /// Nothing launches from this call. It asks the server, which decides and tells every machine.
    /// </summary>
    public void Interact(Interactor interactor)
    {
        if (targetSpaceship == null)
        {
            Debug.LogError("SpaceshipLaunchInteract: targetSpaceship not assigned!");
            return;
        }

        if (!CanInteract()) return;

        latch.Toggle();
    }

    /// <summary>
    /// The session says the ship has gone. Called on every machine by the latch, including the one
    /// that pressed, and never twice for the same state.
    /// </summary>
    /// <param name="launched">True once the ship is away. A one-way latch never sends false.</param>
    /// <param name="instant">
    /// This machine arrived after the launch. Nothing extra to do — the ship's own state is
    /// restored by <c>SpaceshipSaveable</c> and by the replicated transform — but the flag is here
    /// because a future version may want to skip a countdown or a sound for a late joiner.
    /// </param>
    private void ApplyLaunch(bool launched, bool instant)
    {
        if (!launched) return;

        hasBeenUsed = true;

        if (targetSpaceship != null) targetSpaceship.BeginFlight();

        // Deliberately NOT SetActive(false) any more, which is what this used to do. Switching the
        // button off takes its latch down with it (OnDisable), and a player who joins afterwards
        // then asks the server what the state is and is answered by nobody — so their copy of the
        // button stays live and offers them a launch that has already happened. CanInteract is
        // what makes it unusable, and it keeps the latch around to answer.
    }

    /// <summary>
    /// Reset the interaction (for testing or respawning).
    ///
    /// Local only, and it always was. A one-way latch has no way back on the wire, so this puts
    /// this machine's button back and nobody else's — fine for the editor, not a gameplay path.
    /// </summary>
    public void ResetInteraction()
    {
        hasBeenUsed = false;
        gameObject.SetActive(true);
    }
}
