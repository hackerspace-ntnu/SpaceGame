using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Items
{
    public class LightningSpell : ToolItem
    {
        [SerializeField] private GameObject lightningVFXPrefab;
        [SerializeField] private float spawnHeightOffset = 10f;
        [SerializeField] private float raycastDistance = 500f;

        [Header("Damage")]
        [Tooltip("Dealt to everything caught in the strike. Whole points — NetDamage discards anything that rounds to zero.")]
        [SerializeField] private int damage = 120;

        [Tooltip("How wide the strike bites, in metres, measured from where the bolt earths rather than from where it was drawn.")]
        [SerializeField] private float damageRadius = 3.5f;

        [Tooltip("What the strike can hurt. Triggers are always ignored.")]
        [SerializeField] private LayerMask damageMask = ~0;

        [Tooltip("Whether the caster can be caught in their own bolt. Off by default: the spell is aimed at what you are looking at, so hitting yourself with it is nearly always a mis-click rather than a choice.")]
        [SerializeField] private bool damagesCaster;

        /// <summary>
        /// Where the bolt lands, decided by the player who cast it.
        ///
        /// Every machine has to strike the same spot, and only the caster's machine can work out
        /// which spot that is — it is the one holding their camera. So the aim travels with the
        /// use instead of each peer raycasting from its own copy of a remote player and striking
        /// somewhere slightly different.
        /// </summary>
        public override void OnRequestUse(ref NetArg arg)
        {
            RaycastHit hit = default;
            bool struck = aimProvider != null
                          && aimProvider.TryGetAimHit(raycastDistance, out hit);

            // Zero means "aimed at open sky" — see Present. `?? Vector3.zero` used to be read as a
            // position, so aiming at nothing struck the world origin.
            arg.P = struck ? hit.point + Vector3.up * spawnHeightOffset : Vector3.zero;
        }

        /// <summary>
        /// Server-run: what the strike actually does to what it lands on.
        ///
        /// <para>
        /// Damage is shared world state and exactly one machine may decide it. Applying it here
        /// rather than beside the visual in <see cref="Present"/> is the whole difference between
        /// a bolt that kills a creature and a bolt that kills it once per player watching.
        /// </para>
        /// <para>
        /// It bills the GROUND point, not <c>UseArg.P</c>. What travels on the wire is where the
        /// bolt is DRAWN from — ten metres up, so the graph has sky to fall through — and billing
        /// that would put the blast radius in the air above everything it was supposed to hit.
        /// </para>
        /// </summary>
        protected override void Use()
        {
            Vector3 strike = UseArg.P;
            if (strike == Vector3.zero || damage <= 0 || damageRadius <= 0f) return;

            Vector3 ground = strike - Vector3.up * spawnHeightOffset;

            // Colliders are not creatures: a body is several of them, and billing each would
            // multiply the damage by however many limbs happened to be inside the radius. That
            // rule now lives in RadiusDamage, which every blast in the game shares.
            RadiusDamage.Apply(ground, damageRadius, damageMask, damage,
                               owner != null ? owner.transform : transform,
                               damagesCaster ? null : owner != null ? owner.transform : null);
        }

        protected override void Present()
        {
            Vector3 strike = UseArg.P;
            if (strike == Vector3.zero) return;

            if (lightningVFXPrefab == null)
            {
                Debug.LogWarning("LightningSpell: No Lightning VFX prefab assigned.", this);
                return;
            }

            Instantiate(lightningVFXPrefab, strike, Quaternion.Euler(90f, 0f, 0f));
        }

        private void OnValidate()
        {
            damage = Mathf.Max(0, damage);
            damageRadius = Mathf.Max(0f, damageRadius);
        }
    }
}
