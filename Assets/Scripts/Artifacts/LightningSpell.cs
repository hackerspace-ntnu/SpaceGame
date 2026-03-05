using UnityEngine;

public class LightningSpell : ToolItem
{
    [SerializeField] private GameObject lightningVFXPrefab;
    [SerializeField] private float spawnHeightOffset = 10f;
    [SerializeField] private float raycastDistance = 500f;
    Vector3 spawnPoint;



    protected override void Use()
    {     
        spawnPoint = aimProvider.getRayCast(raycastDistance)?.point + Vector3.up * spawnHeightOffset ?? Vector3.zero;

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