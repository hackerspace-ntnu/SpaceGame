using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FootIK : MonoBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastDistance = 3f;
    [SerializeField] private float stepDistance = 0.3f;
    [SerializeField] private float stepHeight = 0.2f;
    [SerializeField] private float stepSpeed = 4f;

    [Header("IK Targets (assign from Animation Rigging rig)")]
    [SerializeField] private Transform leftFootTarget;
    [SerializeField] private Transform rightFootTarget;

    private Animator animator;

    private Vector3 leftCurrent, leftOld, leftNew;
    private Vector3 rightCurrent, rightOld, rightNew;
    private float leftLerp = 1f, rightLerp = 1f;

    void Start()
    {
        animator = GetComponent<Animator>();
        leftCurrent = leftOld = leftNew = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
        rightCurrent = rightOld = rightNew = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
    }

    void Update()
    {
        UpdateFoot(ref leftCurrent, ref leftOld, ref leftNew, ref leftLerp, HumanBodyBones.LeftFoot);
        UpdateFoot(ref rightCurrent, ref rightOld, ref rightNew, ref rightLerp, HumanBodyBones.RightFoot);

        leftFootTarget.position = leftCurrent;
        rightFootTarget.position = rightCurrent;
    }

    void UpdateFoot(ref Vector3 current, ref Vector3 old, ref Vector3 next, ref float lerp, HumanBodyBones bone)
    {
        Transform foot = animator.GetBoneTransform(bone);
        Ray ray = new Ray(foot.position + Vector3.up * 0.5f, Vector3.down);

        Debug.DrawRay(ray.origin, ray.direction * raycastDistance, Color.red);
        if (Physics.Raycast(ray, out RaycastHit hit, raycastDistance, groundLayer))
        {
            if (Vector3.Distance(next, hit.point) > stepDistance && lerp >= 1f)
            {
                lerp = 0f;
                old = current;
                next = hit.point;
            }
        }

        if (lerp < 1f)
        {
            Vector3 pos = Vector3.Lerp(old, next, lerp);
            pos.y += Mathf.Sin(lerp * Mathf.PI) * stepHeight;
            current = pos;
            lerp += Time.deltaTime * stepSpeed;
        }
        else
        {
            old = next;
        }
    }
}
