using NUnit.Framework;
using UnityEngine;
using SpaceGame.Vehicles.DuneFoil;

/// <summary>
/// Ride height and sand drag. The craft's deck flies 13 m up at speed and rests on the sand
/// when stopped, and that transition is both the foiling behaviour and the way aboard, so it
/// is worth pinning down.
/// </summary>
public class FoilPhysicsTests
{
    private const float Takeoff = 8f;
    private const float MaxHeight = 13.18f;

    [Test]
    public void AtRest_TheHullSitsOnTheSand()
    {
        Assert.AreEqual(0f, FoilPhysics.RideHeight(0f, Takeoff, MaxHeight), 1e-4f,
            "A stopped craft must be boardable, not hovering.");
    }

    [Test]
    public void BelowTakeoffSpeed_ItStaysDown()
    {
        Assert.AreEqual(0f, FoilPhysics.RideHeight(Takeoff * 0.99f, Takeoff, MaxHeight), 1e-4f);
    }

    [Test]
    public void RideHeight_RisesMonotonicallyWithSpeed()
    {
        float previous = -1f;
        for (float v = 0f; v <= 60f; v += 0.5f)
        {
            float h = FoilPhysics.RideHeight(v, Takeoff, MaxHeight);
            Assert.GreaterOrEqual(h, previous - 1e-5f, $"Ride height fell at {v} m/s.");
            previous = h;
        }
    }

    [Test]
    public void RideHeight_NeverExceedsTheStrut()
    {
        // Past this the foil would be out of the sand making no lift, so it is a real ceiling.
        Assert.LessOrEqual(FoilPhysics.RideHeight(500f, Takeoff, MaxHeight), MaxHeight + 1e-4f);
    }

    [Test]
    public void RideHeight_ApproachesTheStrutAtHighSpeed()
    {
        Assert.Greater(FoilPhysics.RideHeight(40f, Takeoff, MaxHeight), MaxHeight * 0.8f,
            "Well above take-off the craft should be flying near the top of its strut.");
    }

    [Test]
    public void FoilingCostsFarLessDragThanPloughing()
    {
        float ploughing = FoilPhysics.SandDrag(15f, 0f, hullDrag: 0.05f, foilDrag: 0.004f);
        float flying = FoilPhysics.SandDrag(15f, 1f, hullDrag: 0.05f, foilDrag: 0.004f);

        Assert.Greater(ploughing, flying * 5f,
            "Getting up onto the foil must be a real and rewarding transition.");
    }

    [Test]
    public void SandDrag_GrowsWithSpeed()
    {
        float slow = FoilPhysics.SandDrag(5f, 0.5f, 0.05f, 0.004f);
        float fast = FoilPhysics.SandDrag(20f, 0.5f, 0.05f, 0.004f);
        Assert.Greater(fast, slow * 8f, "Drag goes as speed squared.");
    }

    [Test]
    public void ADeeplyBuriedFoil_GripsHarderThanAFlyingOne()
    {
        float submerged = FoilPhysics.LateralGrip(0f, gripSubmerged: 0.98f, gripFlying: 0.55f);
        float flying = FoilPhysics.LateralGrip(1f, gripSubmerged: 0.98f, gripFlying: 0.55f);

        Assert.Greater(submerged, flying,
            "Resisting leeway is what lets the craft sail upwind; a buried foil must grip more.");
    }
}
