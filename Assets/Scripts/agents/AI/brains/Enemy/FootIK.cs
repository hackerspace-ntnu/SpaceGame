using UnityEngine;
using UnityEngine.Animations.Rigging;

[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastDistance = 3f;
    [SerializeField] private float stepDistance = 0.3f;
    [SerializeField] private float stepHeight = 0.2f;
    [SerializeField] private float stepSpeed = 4f;
    [SerializeField] private float stepAnticipation = 0.35f;
    [SerializeField] private float hintOffset = 0.3f;

    private Animator animator;
    private Vector3 lastPosition;
    private Vector3 velocity;

    private Transform leftRoot, leftMid, leftTip, leftTarget, leftHint;
    private Transform rightRoot, rightMid, rightTip, rightTarget, rightHint;

    private Vector3 leftCurrent, leftOld, leftNew;
    private Vector3 rightCurrent, rightOld, rightNew;
    private float leftLerp = 1f, rightLerp = 1f;

    private void Start()
    {
        animator = GetComponent<Animator>();
        lastPosition = transform.position;

        // Search from the parent so we find constraints that live in sibling rigs
        // (e.g. LeftLegIK / RightLegIK under a Rig 1 object next to the skeleton).
        Transform searchRoot = transform.parent != null ? transform.parent : transform;
        TwoBoneIKConstraint[] constraints = searchRoot.GetComponentsInChildren<TwoBoneIKConstraint>();

        foreach (TwoBoneIKConstraint c in constraints)
        {
            Transform mid = c.data.mid;
            if (mid == null)
            {
                continue;
            }

            if (IsLeftBone(mid))
            {
                leftRoot   = c.data.root;
                leftMid    = c.data.mid;
                leftTip    = c.data.tip;
                leftTarget = c.data.target;
                leftHint   = c.data.hint;
            }
            else
            {
                rightRoot   = c.data.root;
                rightMid    = c.data.mid;
                rightTip    = c.data.tip;
                rightTarget = c.data.target;
                rightHint   = c.data.hint;
            }
        }

        if (leftTip != null)
        {
            leftCurrent = leftOld = leftNew = leftTip.position;
        }

        if (rightTip != null)
        {
            rightCurrent = rightOld = rightNew = rightTip.position;
        }
    }

    private void Update()
    {
        if (leftTarget == null || rightTarget == null)
        {
            return;
        }

        velocity = (transform.position - lastPosition) / Time.deltaTime;
        lastPosition = transform.position;

        UpdateFoot(ref leftCurrent, ref leftOld, ref leftNew, ref leftLerp, leftTip, rightLerp >= 1f);
        UpdateFoot(ref rightCurrent, ref rightOld, ref rightNew, ref rightLerp, rightTip, leftLerp >= 1f);

        leftTarget.position  = leftCurrent;
        rightTarget.position = rightCurrent;

        // Keep hints aligned with the natural knee-bend direction derived from live bone positions.
        // This makes knee bending correct regardless of character orientation or pose.
        UpdateHint(leftHint,  leftRoot,  leftMid,  leftTip);
        UpdateHint(rightHint, rightRoot, rightMid, rightTip);
    }

    private void UpdateHint(Transform hint, Transform root, Transform mid, Transform tip)
    {
        if (hint == null || root == null || mid == null || tip == null)
        {
            return;
        }

        // The direction from the root-tip midpoint toward the knee is exactly the
        // direction the leg naturally bends. Placing the hint there keeps the
        // solver bending in the right direction without any manual tweaking.
        Vector3 midpoint = (root.position + tip.position) * 0.5f;
        Vector3 bendDir  = mid.position - midpoint;

        if (bendDir.sqrMagnitude < 0.0001f)
        {
            bendDir = transform.forward;
        }

        hint.position = mid.position + bendDir.normalized * hintOffset;
    }

    private void UpdateFoot(ref Vector3 current, ref Vector3 old, ref Vector3 next, ref float lerp, Transform footBone, bool otherFootPlanted)
    {
        if (footBone == null)
        {
            return;
        }

        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 anticipatedOrigin = footBone.position;
        if (flatVelocity.sqrMagnitude > 0.01f)
        {
            anticipatedOrigin += flatVelocity.normalized * stepAnticipation;
        }

        Ray ray = new Ray(anticipatedOrigin + Vector3.up * 0.5f, Vector3.down);

        Debug.DrawRay(ray.origin, ray.direction * raycastDistance, Color.red);
        if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance, groundLayer))
        {
            if (Vector3.Distance(next, hit.point) > stepDistance && lerp >= 1f && otherFootPlanted)
            {
                lerp = 0f;
                old  = current;
                next = hit.point;
            }
        }

        if (lerp < 1f)
        {
            Vector3 pos = Vector3.Lerp(old, next, lerp);
            pos.y  += Mathf.Sin(lerp * Mathf.PI) * stepHeight;
            current = pos;
            lerp    = Mathf.Min(lerp + Time.deltaTime * stepSpeed, 1f);
        }
        else
        {
            old = next;
        }
    }

    private static bool IsLeftBone(Transform bone)
    {
        string name = bone.name.ToLower();
        return name.Contains("left") || name.Contains(".l") || name.EndsWith("_l");
    }
}
