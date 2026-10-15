using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The Oathbranch: the staff grown from the sprite hamlet's Tree of Life that held up the dome
    /// over the village. Lifted out of its socket it is a weapon of one verb — a bolt that holds
    /// whatever it strikes in stasis for a few seconds.
    ///
    /// <para>
    /// <b>Fire whenever it is ready.</b> No charges and no ammunition: the only cost is the
    /// recharge, and the recharge is deliberately LONGER than the stasis it lays. A staff that could
    /// re-freeze its target the moment it thawed would lock anything it hit forever, and the witch
    /// fight it was made for would be over in one volley; with the gap, every shot is a commitment
    /// and the question is when and where to spend it (GDC-L1-DESIGN-0002).
    /// </para>
    /// <para>
    /// <b>Networking.</b> The owner reports its aim ray; every machine flies the same
    /// <see cref="StasisBolt"/> from it, immediately, and only the authority's copy applies the
    /// status — the bazooka's split (<see cref="DragonBazookaArtifact"/>). Stasis itself then
    /// travels as the ordinary status message.
    /// </para>
    /// <para>
    /// <b>Persistence.</b> The recharge rides the hotbar slot (<see cref="CaptureItemState"/>), like
    /// the laser staff's: the held object is destroyed on every slot change, and a recharge living on
    /// the instance is one the player skips by scrolling away and back. Everything else is carried by
    /// <c>UsableItem</c>/<c>PickupableItem</c>/<c>SaveableEntity</c> like every artifact.
    /// </para>
    /// </summary>
    public class OathbranchArtifact : ToolItem
    {
        /// <summary>Server: stasis is laid on other bodies, which is shared world state.</summary>
        public override UseAuthority Authority => UseAuthority.Server;

        [Header("Bolt")]
        [Tooltip("The shot. Instantiated LOCALLY by every machine, so it must NOT be in the network " +
                 "prefab list — see StasisBolt.")]
        [SerializeField] private StasisBolt boltPrefab;

        [Tooltip("Where the bolt leaves the staff — the orb at its head.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Seconds of stasis a bolt lays. Zero or less: the status's own duration.")]
        [SerializeField] private float stasisSeconds;

        [Tooltip("Seconds before it can be fired again. Keep it longer than the stasis, or a target " +
                 "can be held forever.")]
        [SerializeField, Min(0.1f)] private float rechargeSeconds = 10f;

        [Header("Readiness")]
        [Tooltip("The orb. Drawn with the StasisShell shader, whose _Appear swells it from dim to " +
                 "full as the staff recharges, so the player can read the recharge off the staff itself.")]
        [SerializeField] private Renderer orb;

        [Tooltip("Lit while the staff is ready, dark while it recharges.")]
        [SerializeField] private Light orbLight;

        [Tooltip("Orb light intensity when fully charged.")]
        [SerializeField, Min(0f)] private float orbLightIntensity = 2f;

        private static readonly int AppearId = Shader.PropertyToID("_Appear");

        /// <summary>State key for the recharge. Written into save files — never rename.</summary>
        private const string RechargeKey = "oathbranchRecharge";

        private float authorityReadyAt = float.NegativeInfinity;
        private float presentedReadyAt = float.NegativeInfinity;
        private MaterialPropertyBlock orbProperties;

        /// <summary>Owner-side: the aim ray, the only honest one being here.</summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            if (aimProvider == null) return;

            Ray ray = aimProvider.GetAimRay();
            arg.P = ray.origin;
            arg.R = Quaternion.LookRotation(ray.direction);
        }

        /// <summary>Authority: the bolt that is allowed to lay stasis.</summary>
        protected override void Use()
        {
            if (boltPrefab == null || owner == null || !UseArg.HasOrientation || Time.time < authorityReadyAt)
            {
                CancelUse();
                return;
            }

            authorityReadyAt = Time.time + rechargeSeconds;
            Launch(cosmetic: false);
        }

        /// <summary>
        /// Every machine: the bolt everyone else sees, and the orb going dark. Asked of the OWNER's
        /// network object, not this one — an equipped item's own NetworkObject is dormant, see
        /// <see cref="DragonBazookaArtifact"/>.
        /// </summary>
        protected override void Present()
        {
            if (owner == null || !UseArg.HasOrientation || Time.time < presentedReadyAt) return;

            presentedReadyAt = Time.time + rechargeSeconds;
            if (!Network.Simulates(owner.transform)) Launch(cosmetic: true);
        }

        private void Launch(bool cosmetic)
        {
            Quaternion along = UseArg.R;
            Vector3 from = UseArg.P + along * Vector3.forward * MuzzleLead();

            StasisBolt bolt = Instantiate(boltPrefab, from, along);
            bolt.Launch(owner, cosmetic, stasisSeconds);
        }

        /// <summary>
        /// How far down the aim ray the bolt starts: out to the orb, so it leaves the staff rather
        /// than the player's eye, while staying on the line the crosshair drew.
        /// </summary>
        private float MuzzleLead()
        {
            if (muzzle == null || owner == null) return 1.2f;
            return Mathf.Max(0.5f, Vector3.Distance(owner.transform.position, muzzle.position));
        }

        public override void CaptureItemState(ItemState state)
        {
            base.CaptureItemState(state);
            if (state == null) return;

            // Seconds remaining, never a deadline: Time.time restarts at zero each session.
            float remaining = Mathf.Max(0f, Mathf.Max(authorityReadyAt, presentedReadyAt) - Time.time);
            if (remaining > 0.01f) state.Set(RechargeKey, remaining);
        }

        public override void RestoreItemState(ItemState state)
        {
            base.RestoreItemState(state);

            float remaining = state == null ? 0f : state.GetFloat(RechargeKey, 0f);
            float readyAt = remaining > 0f ? Time.time + remaining : float.NegativeInfinity;
            authorityReadyAt = readyAt;
            presentedReadyAt = readyAt;
        }

        private void Update()
        {
            float remaining = Mathf.Max(0f, presentedReadyAt - Time.time);
            float readiness = 1f - Mathf.Clamp01(remaining / rechargeSeconds);

            if (orb != null)
            {
                orbProperties ??= new MaterialPropertyBlock();
                orb.GetPropertyBlock(orbProperties);
                orbProperties.SetFloat(AppearId, readiness);
                orb.SetPropertyBlock(orbProperties);
            }

            if (orbLight != null) orbLight.intensity = orbLightIntensity * readiness * readiness;
        }

        private void OnValidate() => rechargeSeconds = Mathf.Max(0.1f, rechargeSeconds);
    }
}
