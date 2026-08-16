using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Items
{
    public class LightningSpell : ToolItem
    {
        [SerializeField] private GameObject lightningVFXPrefab;
        [SerializeField] private float spawnHeightOffset = 10f;
        [SerializeField] private float raycastDistance = 500f;

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
            RaycastHit? hit = aimProvider != null ? aimProvider.GetRayCast(raycastDistance) : null;

            // Zero means "aimed at open sky" — see Present. `?? Vector3.zero` used to be read as a
            // position, so aiming at nothing struck the world origin.
            arg.P = hit.HasValue ? hit.Value.point + Vector3.up * spawnHeightOffset : Vector3.zero;
        }

        // The bolt is a visual, drawn by every machine from the caster's aim point, so there is
        // nothing here for the server alone to do. ToolItem.Use is already empty.

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
    }
}
