// How a body in stasis looks, on one machine.
//
// It owns nothing about being in stasis except the LOOK. Helplessness is StasisStatus, read off
// StatusReceiver.Suppressed by AgentController and by BodyHold. This is the shell and the held pose.
//
// EVERY MACHINE HAS ITS OWN, put there by StasisStatus.OnApplied, which runs wherever the status
// message lands — the host, every peer and a late joiner told about it on connect. It sends nothing.
using UnityEngine;

namespace SpaceGame.Gameplay.Status
{
    /// <summary>
    /// The shimmering shell round one body in stasis, and the pose it holds while inside.
    ///
    /// <para>
    /// <b>Stasis is read every frame and never stored.</b> The shell asks the receiver whether the
    /// condition is still running and removes itself the first frame it is not, for the reason
    /// <c>FrozenBody</c> gives: a machine that missed the end of a condition must not draw it forever.
    /// </para>
    /// <para>
    /// The shell is an ellipsoid fitted to the body's renderers at the moment it is applied, so a
    /// knee-high sprite and a witch both read as "inside a bubble" rather than one of them wearing a
    /// sphere sized for the other. It flickers as the time runs out — the thaw is telegraphed, so
    /// the player who froze something can see when it will move again (GDC-L1-UX-0003).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StasisShell : MonoBehaviour
    {
        /// <summary>
        /// The shell material, under a Resources folder because this component is added at runtime
        /// to whatever was frozen and has no prefab to be handed one by.
        /// </summary>
        private const string MaterialPath = "Status/StasisShell";

        /// <summary>How much larger than the body's bounds the shell is, so it never clips a limb.</summary>
        private const float Padding = 1.25f;

        /// <summary>Seconds the shell takes to swell into place.</summary>
        private const float AppearSeconds = 0.2f;

        private static readonly int AppearId = Shader.PropertyToID("_Appear");
        private static readonly int RemainingId = Shader.PropertyToID("_Remaining");

        private static Material sharedMaterial;
        private static bool materialMissingReported;

        private StatusReceiver receiver;
        private Animator animator;
        private GameObject shell;
        private Renderer shellRenderer;
        private MaterialPropertyBlock properties;
        private float restSpeed = 1f;
        private bool posed;
        private float age;

        /// <summary>The shell on <paramref name="body"/>, making it if it has none.</summary>
        public static StasisShell Ensure(StatusReceiver body)
        {
            if (body == null) return null;

            StasisShell existing = body.GetComponent<StasisShell>();
            return existing != null ? existing : body.gameObject.AddComponent<StasisShell>();
        }

        private void LateUpdate()
        {
            if (Receiver == null || !Receiver.Has(StatusKind.Stasis))
            {
                Destroy(this);
                return;
            }

            HoldPose();
            Build();

            age += Time.deltaTime;
            if (shellRenderer == null) return;

            properties ??= new MaterialPropertyBlock();
            shellRenderer.GetPropertyBlock(properties);
            properties.SetFloat(AppearId, Mathf.Clamp01(age / AppearSeconds));
            properties.SetFloat(RemainingId, Receiver.RemainingShare(StatusKind.Stasis));
            shellRenderer.SetPropertyBlock(properties);
        }

        private void OnDestroy()
        {
            // A body torn down mid-stasis — killed, streamed out — must not leave its animator
            // stopped for good or a shell floating where it stood.
            if (posed && animator != null) animator.speed = restSpeed;
            posed = false;

            if (shell != null) Destroy(shell);
        }

        /// <summary>
        /// Stop the clip where it stands. The speed is captured and given back rather than assumed
        /// to be one, and never written anywhere a save reads.
        /// </summary>
        private void HoldPose()
        {
            if (posed) return;

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;

            restSpeed = animator.speed;
            animator.speed = 0f;
            posed = true;
        }

        private void Build()
        {
            if (shell != null) return;

            Material material = LoadMaterial();
            if (material == null) return;

            Bounds bounds = BodyBounds();

            shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "StasisShell";

            // A shell that could be shot or stood on would be a second body; it is a picture.
            Destroy(shell.GetComponent<Collider>());

            shell.transform.SetParent(transform, worldPositionStays: true);
            shell.transform.position = bounds.center;
            shell.transform.rotation = Quaternion.identity;
            SetWorldScale(shell.transform, bounds.size * Padding);

            shellRenderer = shell.GetComponent<Renderer>();
            shellRenderer.sharedMaterial = material;
            shellRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellRenderer.receiveShadows = false;
        }

        /// <summary>Everything the body draws, the shell excepted. A unit box at the pivot for a body that draws nothing.</summary>
        private Bounds BodyBounds()
        {
            bool any = false;
            Bounds bounds = new Bounds(transform.position, Vector3.one);

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled || part is ParticleSystemRenderer || part is TrailRenderer || part is LineRenderer)
                    continue;

                if (!any) bounds = part.bounds;
                else bounds.Encapsulate(part.bounds);
                any = true;
            }

            return bounds;
        }

        /// <summary>Scale a child so its WORLD size is <paramref name="size"/>, whatever its parents are scaled by.</summary>
        private static void SetWorldScale(Transform child, Vector3 size)
        {
            Vector3 parent = child.parent != null ? child.parent.lossyScale : Vector3.one;
            child.localScale = new Vector3(
                parent.x != 0f ? size.x / parent.x : size.x,
                parent.y != 0f ? size.y / parent.y : size.y,
                parent.z != 0f ? size.z / parent.z : size.z);
        }

        private static Material LoadMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;

            sharedMaterial = Resources.Load<Material>(MaterialPath);
            if (sharedMaterial == null && !materialMissingReported)
            {
                materialMissingReported = true;
                Debug.LogError($"[Stasis] No material at Resources/{MaterialPath} — bodies in stasis are helpless but draw no shell.");
            }

            return sharedMaterial;
        }

        private StatusReceiver Receiver => receiver != null ? receiver : receiver = GetComponent<StatusReceiver>();
    }
}
