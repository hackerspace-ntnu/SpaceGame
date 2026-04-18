using UnityEngine;

[System.Serializable]
public class SettlementGenerationSettings
{
    [Header("Scale")]
    public int footprintRadius = 7;
    public int maxHeight = 20;
    public int minHeight = 3;

    [Header("Silhouette")]
    public bool allowMiddleBulge = true;
    [Range(0f, 1f)] public float middleBulgeChance = 0.85f;
    [Range(1, 4)] public int middleBulgeExtraWidth = 2;
    [Range(0f, 1f)] public float overhangChance = 0.7f;
    [Range(1, 3)] public int overhangMaxPush = 2;
    [Range(0f, 1f)] public float ruinedVoidChance = 0.55f;

    [Header("Arches And Features")]
    [Range(0f, 1f)] public float archDensity = 0.7f;
    [Range(0.35f, 0.9f)] public float upperFeatureCutoff = 0.62f;
    public bool enableCircularFeatures = false;
    [Range(0f, 1f)] public float circularFeatureDensity = 0.35f;
    public bool enableTriangularFeatures = true;
    [Range(0f, 1f)] public float triangularFeatureDensity = 0.28f;

    [Header("Pillars")]
    [Range(0f, 1f)] public float largePillarDensity = 0.55f;
    [Range(0f, 1f)] public float thinPillarDensity = 0.65f;

    [Header("Facade Detail")]
    [Range(0f, 1f)] public float techDetailDensity = 0.68f;
    [Range(0f, 1f)] public float ventDetailDensity = 0.42f;
    [Range(0f, 1f)] public float parapetDensity = 0.12f;
    [Range(0f, 1f)] public float roofClutterDensity = 0.58f;
}
