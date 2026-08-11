using UnityEngine;

/// <summary>
/// Drop-anywhere companion for any UsableItem prefab whose hand/hold animation
/// needs a "being held" flag. Drives a boolean (default "Hold") on a target
/// Animator.
///
/// Hold = true requires ALL of:
///   1. Item is currently equipped.
///   2. Holder isn't moving — measured directly from PlayerInputManager.MoveInput
///      (raw input; zero the instant the player releases WASD) and optionally
///      Rigidbody velocity. Reading the raw input avoids races with locomotion-
///      blend-tree smoothing in the player's Animator.
///   3. After Hold drops to false, a short re-arm cooldown must elapse before
///      it can flip back on. Stops chatter from micro-pauses while moving.
///
/// Animator lookup order (first non-null wins):
///   1. The holder GameObject passed to UsableItem.OnEquipped (player rig).
///   2. Serialized `animator` field.
///   3. Any Animator in the artifact's child tree.
/// </summary>
public class HoldAnimator : MonoBehaviour
{
    [Tooltip("Optional explicit Animator. If null, the component first tries the holder's Animator (player rig), then any Animator in this object's children.")]
    [SerializeField] private Animator animator;
    [Tooltip("Bool parameter name to drive on the resolved animator.")]
    [SerializeField] private string boolParameter = "Hold";

    [Header("Movement gating")]
    [Tooltip("If true, ANY movement (input pressed or rigidbody velocity above threshold) forces Hold = false.")]
    [SerializeField] private bool requireStationary = true;
    [Tooltip("Raw move-input magnitude above which the holder counts as moving. Tiny epsilon — release WASD => 0 immediately.")]
    [SerializeField] private float inputDeadzone = 0.01f;
    [Tooltip("Horizontal Rigidbody speed (m/s) above which the holder counts as moving, even with no input. Catches sliding/knockback.")]
    [SerializeField] private float velocityThreshold = 0.15f;

    [Header("Re-arm cooldown")]
    [Tooltip("Seconds Hold must stay false after movement stops before it's allowed to flip true again. Prevents flicker.")]
    [SerializeField] private float holdReArmDelay = 0.2f;

    [Header("Debug")]
    [Tooltip("Log resolved animator on equip and Hold transitions while held.")]
    [SerializeField] private bool debugLog;

    private Animator resolvedAnimator;
    private GameObject heldByHolder;
    private PlayerInputManager holderInputs;
    private Rigidbody holderRb;
    private bool equipped;
    private int paramHash;

    private bool currentHold;
    private float earliestReArmTime;

    private void Awake()
    {
        paramHash = Animator.StringToHash(boolParameter);
    }

    /// <summary>Called by UsableItem.OnEquipped/OnUnequipped. `holder` is the player.</summary>
    public void SetHeld(GameObject holder, bool value)
    {
        equipped = value;
        if (value)
        {
            heldByHolder = holder;
            resolvedAnimator = ResolveAnimator(holder);
            holderInputs = holder != null ? holder.GetComponent<PlayerInputManager>() : null;
            holderRb     = holder != null ? holder.GetComponent<Rigidbody>()         : null;
            // Block immediate Hold = true on equip if already moving — let the
            // player settle first.
            earliestReArmTime = Time.time + holdReArmDelay;
            if (debugLog) LogResolution(holder);
        }
        else
        {
            heldByHolder = null;
            holderInputs = null;
            holderRb = null;
        }
        ApplyImmediate(false); // start clean
    }

    private void Update()
    {
        if (!equipped) return;
        Apply();
    }

    private void Apply()
    {
        if (resolvedAnimator == null) return;
        if (!HasParam(resolvedAnimator, paramHash)) return;

        bool desired = true;

        if (requireStationary && IsMoving())
        {
            desired = false;
            // Push out the re-arm window so we don't snap Hold back to true the
            // very first frame the player stops.
            earliestReArmTime = Time.time + holdReArmDelay;
        }
        else if (!currentHold && Time.time < earliestReArmTime)
        {
            // Waiting for re-arm — keep Hold false.
            desired = false;
        }

        ApplyImmediate(desired);
    }

    private void ApplyImmediate(bool desired)
    {
        if (resolvedAnimator == null) return;
        if (!HasParam(resolvedAnimator, paramHash)) return;
        if (desired == currentHold) return;

        currentHold = desired;
        resolvedAnimator.SetBool(paramHash, desired);

        if (debugLog)
        {
            Vector2 mi = holderInputs != null ? holderInputs.MoveInput : Vector2.zero;
            float vmag = holderRb != null
#if UNITY_6000_0_OR_NEWER
                ? new Vector2(holderRb.linearVelocity.x, holderRb.linearVelocity.z).magnitude
#else
                ? new Vector2(holderRb.velocity.x, holderRb.velocity.z).magnitude
#endif
                : 0f;
            Debug.Log($"[HoldAnimator] {name}: Hold={desired} (move={mi}, vel={vmag:F2})", this);
        }
    }

    private bool IsMoving()
    {
        // Raw input — true the moment WASD is pressed, false the moment released.
        if (holderInputs != null && holderInputs.MoveInput.sqrMagnitude > inputDeadzone * inputDeadzone)
            return true;

        // Velocity check catches sliding, knockback, vehicles — anything moving
        // the player without an input press.
        if (holderRb != null)
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 v = holderRb.linearVelocity;
#else
            Vector3 v = holderRb.velocity;
#endif
            v.y = 0f;
            if (v.sqrMagnitude > velocityThreshold * velocityThreshold) return true;
        }
        return false;
    }

    private Animator ResolveAnimator(GameObject holder)
    {
        if (holder != null)
        {
            var fromHolder = holder.GetComponentInChildren<Animator>(true);
            if (fromHolder != null) return fromHolder;
        }
        if (animator != null) return animator;
        return GetComponentInChildren<Animator>(true);
    }

    private static bool HasParam(Animator a, int hash)
    {
        if (a == null) return false;
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].nameHash == hash) return true;
        return false;
    }

    private void LogResolution(GameObject holder)
    {
        var msg = $"[HoldAnimator] {name}: animator='{(resolvedAnimator != null ? resolvedAnimator.name : "<null>")}' "
                + $"ctrl='{(resolvedAnimator != null && resolvedAnimator.runtimeAnimatorController != null ? resolvedAnimator.runtimeAnimatorController.name : "<null>")}' "
                + $"hasHold={HasParam(resolvedAnimator, paramHash)} "
                + $"inputs={(holderInputs != null ? "found" : "<null>")} "
                + $"rb={(holderRb != null ? "found" : "<null>")}";
        Debug.Log(msg, this);
    }
}
