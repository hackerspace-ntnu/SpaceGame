// The wing pack: a folded ornithopter carried in the inventory. Using it in mid-air spawns the
// craft, straps the player into the prone cradle and hands them the controls.
//
// The pack is a launcher, not the aircraft. Everything about flying lives on the spawned prefab
// (OrnithopterFlightMotor + MountModule + SteerModule); this class owns the moment of transition
// and the teardown, which is the part that has three ways to fire and has to survive all of them.
using SpaceGame.Vehicles.Ornithopter;
using UnityEngine;

public class WingPackItem : UsableItem
{
    [Header("Craft")]
    [Tooltip("The ornithopter prefab spawned and mounted when the pack is used.")]
    [SerializeField] private GameObject ornithopterPrefab;

    [Header("Launch window")]
    [Tooltip("The pack is air-only. Ground within this distance straight down counts as standing " +
             "on it, and the pack refuses.")]
    [SerializeField, Min(0.1f)] private float groundClearance = 0.6f;

    [Tooltip("How far below to look before a drop counts as big enough to launch into.")]
    [SerializeField, Min(1f)] private float minLaunchClearance = 6f;

    [Tooltip("How far ahead of the player to probe for that drop. This is what makes standing on " +
             "a cliff EDGE work: the ray straight down hits the ledge the player is standing on, " +
             "so the one that matters is cast out over the drop they are looking at.")]
    [SerializeField, Min(0f)] private float ledgeProbeForward = 1.5f;

    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Launch")]
    [Tooltip("How much of the player's speed at the moment of use carries into the launch. A run " +
             "and a jump should be worth something.")]
    [SerializeField, Range(0f, 1f)] private float speedCarry = 1f;

    [Tooltip("Metres above the player to place the craft, so the wings do not open through the " +
             "ledge that was just stepped off.")]
    [SerializeField] private float launchLift = 1.2f;

    // Live flight. All three teardown paths funnel through ReleaseCraft.
    private GameObject craft;
    private MountModule craftMount;
    private OrnithopterFlightMotor craftMotor;
    private Renderer[] heldRenderers;

    protected override bool CanUse()
    {
        if (!base.CanUse())
            return false;

        if (ornithopterPrefab == null)
        {
            Debug.LogError("WingPackItem: no ornithopter prefab assigned.", this);
            return false;
        }

        if (craft != null)
            return false;               // already flying

        if (owner == null)
            return false;

        if (!HasLaunchRoom(owner.transform))
        {
            // Deliberately not silent. "Nothing happened" is indistinguishable from a broken item,
            // and this is the one item in the game that refuses on the ground by design.
            Debug.Log("WingPack: need air under you — jump, or step off something.", this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Air-only, in the two senses that matter: already falling, or standing at the top of a drop
    /// worth jumping into.
    /// </summary>
    private bool HasLaunchRoom(Transform player)
    {
        Vector3 origin = player.position + Vector3.up * 0.1f;

        bool airborne = !Physics.Raycast(origin, Vector3.down, groundClearance,
                                         groundMask, QueryTriggerInteraction.Ignore);
        if (airborne)
            return true;

        Vector3 ahead = origin + player.forward * ledgeProbeForward;
        bool dropAhead = !Physics.Raycast(ahead, Vector3.down, minLaunchClearance,
                                          groundMask, QueryTriggerInteraction.Ignore);
        return dropAhead;
    }

    protected override void Use()
    {
        Transform player = owner.transform;

        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();

        Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
        craft = Instantiate(ornithopterPrefab, player.position, facing);

        craftMount = craft.GetComponent<MountModule>();
        craftMotor = craft.GetComponent<OrnithopterFlightMotor>();
        if (craftMount == null || craftMotor == null)
        {
            Debug.LogError("WingPackItem: prefab needs both MountModule and OrnithopterFlightMotor.",
                           this);
            Destroy(craft);
            craft = null;
            return;
        }

        // Place the craft so its SEAT lands where the player already is, rather than its origin.
        // Mounting parents the player to the seat at identity, so without this the player is
        // teleported by however far the cradle sits from the prefab root.
        Transform seat = craftMount.ActiveSeatPoint;
        if (seat != null)
        {
            Vector3 seatOffset = seat.position - craft.transform.position;
            craft.transform.position = player.position + Vector3.up * launchLift - seatOffset;
        }
        else
        {
            craft.transform.position = player.position + Vector3.up * launchLift;
        }

        float carriedSpeed = 0f;
        if (owner.TryGetComponent(out Rigidbody playerBody))
        {
            Vector3 flat = playerBody.linearVelocity;
            flat.y = 0f;
            carriedSpeed = flat.magnitude * speedCarry;
        }

        craftMotor.Landed += HandleLanded;
        craftMount.Dismounted += HandleDismounted;

        Interactor interactor = owner.GetComponentInChildren<Interactor>(true);
        if (interactor == null || !craftMount.TryMount(interactor, null))
        {
            Debug.LogError("WingPackItem: could not mount the player onto the craft.", this);
            ReleaseCraft(dismountFirst: false);
            return;
        }

        craftMotor.Launch(forward, carriedSpeed);
        SetHeldVisible(false);
    }

    private void HandleLanded() => ReleaseCraft(dismountFirst: true);

    // The rider bailed out (Escape). The craft goes with them — and because the pack has unlimited
    // uses and is usable while falling, bailing out and redeploying is a legitimate move rather
    // than a dead end.
    private void HandleDismounted(PlayerMovement _) => ReleaseCraft(dismountFirst: false);

    /// <summary>
    /// The single teardown path. Reached from landing, from dismounting, from switching hotbar
    /// slot mid-flight, and from the item being destroyed — so it has to be safe to call twice and
    /// safe to call while the player is still parented into the craft.
    /// </summary>
    private void ReleaseCraft(bool dismountFirst)
    {
        if (craft == null)
            return;

        // Unsubscribe BEFORE dismounting: Dismount raises Dismounted, which re-enters here.
        if (craftMotor != null) craftMotor.Landed -= HandleLanded;
        if (craftMount != null) craftMount.Dismounted -= HandleDismounted;

        // Get the player out from under the craft before destroying it — they are parented to the
        // seat, and destroying the parent takes the player with it.
        if (dismountFirst && craftMount != null && craftMount.IsMounted)
            craftMount.Dismount();

        GameObject doomed = craft;
        craft = null;
        craftMount = null;
        craftMotor = null;

        Destroy(doomed);
        SetHeldVisible(true);
    }

    /// <summary>Hide the folded pack in the player's hand while the real thing is deployed.</summary>
    private void SetHeldVisible(bool visible)
    {
        heldRenderers ??= GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in heldRenderers)
            if (r) r.enabled = visible;
    }

    public override void OnUnequipped(GameObject holder)
    {
        base.OnUnequipped(holder);
        ReleaseCraft(dismountFirst: true);
    }

    private void OnDestroy() => ReleaseCraft(dismountFirst: true);
}
