using UnityEngine;

namespace SpaceGame.Items
{
    public class LightningSpell : ToolItem
    {
        [SerializeField] private GameObject lightningVFXPrefab;
        [SerializeField] private float spawnHeightOffset = 10f;
        [SerializeField] private float raycastDistance = 500f;
        Vector3 spawnPoint;



        protected override void Use()
        {     
            base.Use();
        
            // `?? Vector3.zero` used to swallow a miss, so aiming at open sky struck the world
            // origin instead of doing nothing. A miss means there is nowhere to put the bolt.
            RaycastHit? aim = aimProvider.GetRayCast(raycastDistance);
            if (aim == null) return;
            spawnPoint = aim.Value.point + Vector3.up * spawnHeightOffset;

            if (lightningVFXPrefab != null)
            {
                Instantiate(lightningVFXPrefab, spawnPoint, Quaternion.Euler(90f, 0f, 0f));
            }
            else
            {
                Debug.LogWarning("LightningSpell: No Lightning VFX prefab assigned.");
            }

        }
    }
}
