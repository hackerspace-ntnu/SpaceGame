using UnityEngine;

/// <summary>
/// Rocket turret artifact — extends ToolItem.
///
/// On Use(), spawns a rocket launcher prefab in front of the player, facing
/// away from them, snapped to the ground if a surface is found below the
/// spawn point.
/// </summary>
public class RocketTurretArtifact : ToolItem
{
    [Header("Spawn")]
    [Tooltip("The launcher prefab to spawn (should have a RocketLauncherTurret component).")]
    [SerializeField] private GameObject launcherPrefab;
    [Tooltip("Distance in front of the player to spawn the launcher.")]
    [SerializeField] private float spawnDistance = 3f;
    [Tooltip("Vertical offset above the player's feet to start the ground raycast from.")]
    [SerializeField] private float groundProbeHeight = 2f;
    [Tooltip("How far down to raycast looking for ground to snap to.")]
    [SerializeField] private float groundProbeDistance = 10f;
    [Tooltip("Layers considered ground for snapping.")]
    [SerializeField] private LayerMask groundMask = ~0;

    protected override void Use()
    {
        base.Use();

        if (launcherPrefab == null)
        {
            Debug.LogWarning("[RocketTurretArtifact] launcherPrefab not assigned.");
            return;
        }

        Transform player = owner.transform;

        Vector3 forwardFlat = player.forward;
        forwardFlat.y = 0f;
        if (forwardFlat.sqrMagnitude < 0.0001f)
            forwardFlat = Vector3.forward;
        forwardFlat.Normalize();

        Vector3 spawnPos = player.position + forwardFlat * spawnDistance;

        Vector3 probeOrigin = spawnPos + Vector3.up * groundProbeHeight;
        if (Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit hit,
                groundProbeHeight + groundProbeDistance, groundMask, QueryTriggerInteraction.Ignore))
        {
            spawnPos = hit.point;
        }

        Quaternion spawnRot = Quaternion.LookRotation(forwardFlat, Vector3.up);
        GameObject launcher = Instantiate(launcherPrefab, spawnPos, spawnRot);

        // Freeze on landing so the launcher doesn't drift after spawning.
        if (launcher.GetComponent<GroundAnchorOnLand>() == null)
        {
            if (launcher.GetComponent<Rigidbody>() == null)
                launcher.AddComponent<Rigidbody>();
            launcher.AddComponent<GroundAnchorOnLand>();
        }

        // Don't let the launcher push the player around.
        Collider[] playerCols = owner.GetComponentsInChildren<Collider>();
        Collider[] launcherCols = launcher.GetComponentsInChildren<Collider>();
        foreach (Collider lc in launcherCols)
        {
            if (lc == null) continue;
            foreach (Collider pc in playerCols)
            {
                if (pc == null) continue;
                Physics.IgnoreCollision(lc, pc, true);
            }
        }
    }
}
