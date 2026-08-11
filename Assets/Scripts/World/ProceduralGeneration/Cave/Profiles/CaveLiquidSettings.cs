using UnityEngine;

/// <summary>
/// Liquid sub-config. Per-pool detection: <see cref="LiquidPoolFinder"/> flood-fills the voxel grid
/// and discovers each disconnected low-air pocket, then <see cref="LiquidPoolSpawner"/> builds a
/// flat water plane mesh for each pool sized to its individual rim.
/// </summary>
[System.Serializable]
public class CaveLiquidSettings
{
    [Tooltip("Master toggle. Off = no liquid generated.")]
    public bool enabled = false;

    [Header("Detection")]
    [Tooltip("Pool surface height as a fraction of the cave's vertical extent. 0 = at the very bottom, 0.5 = halfway up. The flood-fill takes everything below this Y as candidate liquid.")]
    [Range(0f, 1f)] public float fillFraction = 0.18f;

    [Tooltip("Minimum pool volume (cubic metres) for a detected pool to be kept. Filters out tiny accidental pockets from voxel-grid noise.")]
    public float minPoolVolume = 4f;

    [Tooltip("Maximum number of pools to keep (largest first). 0 = unlimited.")]
    public int maxPoolCount = 16;

    [Header("Visual")]
    [Tooltip("Material applied to the water plane meshes. URP/Lit transparent water works fine; for lava use an emissive material.")]
    public Material liquidMaterial;

    [Tooltip("Tag for the liquid GameObjects (e.g. 'Water' / 'Lava' so gameplay code can detect entering).")]
    public string liquidTag = "Untagged";

    [Tooltip("Set true to attach a trigger collider matching each pool — handy for damage / swim volumes.")]
    public bool spawnTriggerVolume = true;

    [Tooltip("Vertical thickness of the trigger volume below the waterline (metres). Only used when spawnTriggerVolume is true.")]
    public float triggerDepth = 3f;
}
