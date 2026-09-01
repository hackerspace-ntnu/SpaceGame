using NUnit.Framework;
using SpaceGame.Gear.JumpingRod;

/// <summary>
/// The jumping rod's bounce arithmetic. Pure functions, so none of this needs a scene, a Rigidbody
/// or a clock — which is the whole reason the model was split out of the item.
/// </summary>
public class JumpingRodHopTests
{
    private static JumpingRodConfig Config() => new();

    // ── Take-off ───────────────────────────────────────────────────────────────

    [Test]
    public void StandingOnItAndDoingNothingStillHopsHigh()
    {
        JumpingRodConfig cfg = Config();

        // No arrival speed at all. This is the promise the whole item is built on: plant it, do
        // nothing, and it throws you the cruise height every time.
        Assert.AreEqual(cfg.MinHopSpeed, JumpingRodHopModel.TakeoffSpeed(0f, cfg), 1e-4f);
    }

    [Test]
    public void TheCruiseHopIsWellAboveAnOrdinaryJump()
    {
        JumpingRodConfig cfg = Config();

        // PlayerMovement.jumpForce is 7 m/s at this project's -18 gravity. A rod that hopped the
        // same height as the jump button would be a gadget with no reason to exist.
        Assert.Greater(cfg.MinHopSpeed, 7f * 1.4f);
    }

    [Test]
    public void AVeryHardLandingIsCapped()
    {
        JumpingRodConfig cfg = Config();

        Assert.AreEqual(cfg.MaxHopSpeed, JumpingRodHopModel.TakeoffSpeed(400f, cfg), 1e-4f);
    }

    [Test]
    public void ArrivingHarderLeavesHigher()
    {
        JumpingRodConfig cfg = Config();

        // Both above the floor and below the ceiling, so neither clamp is what is being measured.
        float gentle = JumpingRodHopModel.TakeoffSpeed(13f, cfg);
        float hard = JumpingRodHopModel.TakeoffSpeed(15f, cfg);

        Assert.Greater(hard, gentle);
    }

    [Test]
    public void ABigFallSettlesBackToTheCruiseHeight()
    {
        JumpingRodConfig cfg = Config();

        float speed = cfg.MaxHopSpeed;
        for (int i = 0; i < 200; i++)
            speed = JumpingRodHopModel.TakeoffSpeed(speed, cfg);

        // Settles ON the cruise hop, never below it: the rod keeps working forever, it just stops
        // handing back the extra height a cliff gave it.
        Assert.AreEqual(cfg.MinHopSpeed, speed, 1e-3f);
    }

    [Test]
    public void ArrivalSpeedSignIsIgnored()
    {
        JumpingRodConfig cfg = Config();

        Assert.AreEqual(JumpingRodHopModel.TakeoffSpeed(13f, cfg),
                        JumpingRodHopModel.TakeoffSpeed(-13f, cfg), 1e-4f);
    }

    // ── Touchdown ──────────────────────────────────────────────────────────────

    [Test]
    public void TheTipReachingTheGroundWhileFallingIsALanding()
    {
        Assert.IsTrue(JumpingRodHopModel.HasTouchedDown(0.05f, -11f, 0.12f));
        Assert.IsTrue(JumpingRodHopModel.HasTouchedDown(-0.3f, -11f, 0.12f), "already sunk in");
    }

    [Test]
    public void RisingThroughTheContactBandIsNotALanding()
    {
        // The step after a bounce: the player is still inside the band. Without the descending
        // test the hop is caught and spent before it ever leaves the ground.
        Assert.IsFalse(JumpingRodHopModel.HasTouchedDown(0.05f, 11f, 0.12f));
    }

    [Test]
    public void GroundOutOfReachIsNotALanding()
    {
        Assert.IsFalse(JumpingRodHopModel.HasTouchedDown(2f, -11f, 0.12f));
    }

    // ── Clearance ──────────────────────────────────────────────────────────────
    //
    // The seam the rod shipped broken on, so it is pinned here first. Every height in the config
    // — the contact band, the squash — is clearance under the holder's FEET, and this project's
    // player is not authored with their pivot at their soles: the capsule hangs about a metre
    // below it. Measured from the pivot, a player standing flat on the ground reads a metre of
    // air, every gate in the item stays shut, and the rod does nothing at all without throwing
    // anything at all.

    [Test]
    public void ClearanceIsMeasuredFromTheSolesNotFromThePivot()
    {
        JumpingRodConfig cfg = Config();

        // A player standing flat on ground at y = 0, pivot a metre above their soles.
        float clearance = JumpingRodHopModel.Clearance(1f, 1f, 0f);

        Assert.AreEqual(0f, clearance, 1e-4f);
        Assert.IsTrue(JumpingRodHopModel.HasTouchedDown(clearance, 0f, cfg.ContactHeight),
                      "a player standing on the ground has touched down");

        // The bug, stated: forget the drop and the same player reads a metre of air.
        Assert.IsFalse(JumpingRodHopModel.HasTouchedDown(
                           JumpingRodHopModel.Clearance(1f, 0f, 0f), 0f, cfg.ContactHeight),
                       "measuring from the pivot puts the contact band out of reach forever");
    }

    [Test]
    public void ClearanceFollowsThePlayerUp()
    {
        // Same player, three metres into a hop.
        Assert.AreEqual(3f, JumpingRodHopModel.Clearance(4f, 1f, 0f), 1e-4f);

        // And onto higher ground: what is under them is what counts, not their altitude.
        Assert.AreEqual(0f, JumpingRodHopModel.Clearance(21f, 1f, 20f), 1e-4f);
    }

    // ── Where the rod hangs ────────────────────────────────────────────────────
    //
    // The same measurement seen from the other side. The tip is hung one contact band below the
    // soles, so it meets the ground on exactly the frame the bounce fires — one number rather
    // than two that have to be kept in step by hand.

    [Test]
    public void TheTipHangsOneContactBandBelowTheSoles()
    {
        JumpingRodConfig cfg = Config();

        // The same player as the clearance tests above: pivot a metre over their soles.
        const float PivotAboveSoles = 1f;

        // This model's own pivot is at its tip, so its lowest point is 0 below it.
        const float PrefabBottom = 0f;
        float y = JumpingRodHopModel.TipOffset(PivotAboveSoles, cfg.ContactHeight, PrefabBottom);

        Assert.AreEqual(-(PivotAboveSoles + cfg.ContactHeight), y, 1e-4f);

        // The point of it: the tip touches down exactly when the bounce does. Stand the rod's tip
        // on the ground — the holder's pivot is then TipOffset above it — and the clearance under
        // their soles is the contact band, so the descending player is landing on the same frame.
        //
        // Asserted as a DISTANCE, to the millimetre this file measures everything else in, rather
        // than by handing HasTouchedDown a clearance built as (soles + band) - soles: 1f + 0.12f
        // comes back as 0.12000001 once the 1 is taken off again, one ulp outside a band the model
        // tests with <=, so that spelling failed on IEEE rounding and said nothing about the rod.
        float pivot = -(y + PrefabBottom);

        Assert.AreEqual(cfg.ContactHeight,
                        JumpingRodHopModel.Clearance(pivot, PivotAboveSoles, 0f), 1e-4f,
                        "the tip reaches the ground at a different clearance from the one the " +
                        "bounce fires at, so the rod either sinks into the sand before it fires " +
                        "or fires with the tip still in the air");

        // And the band is inclusive, which is the half of it that is a decision rather than
        // geometry: a descending player exactly one band up is landing, not about to.
        Assert.IsTrue(JumpingRodHopModel.HasTouchedDown(cfg.ContactHeight, -1f, cfg.ContactHeight));
    }

    [Test]
    public void ARodWhosePivotIsNotItsTipStillHangsByItsTip()
    {
        // A model authored around its middle reports its lowest point half a rod below its own
        // pivot. Ignored, it would be planted half a rod into the ground.
        Assert.AreEqual(-1.12f + 0.725f,
                        JumpingRodHopModel.TipOffset(1f, 0.12f, -0.725f), 1e-4f);
    }

    // ── The squash ─────────────────────────────────────────────────────────────

    [Test]
    public void TheCoilLoadsAsThePlayerComesDownAndReleasesAsTheyRise()
    {
        const float over = 0.5f;

        Assert.AreEqual(0f, JumpingRodHopModel.Compression(over, over), 1e-4f);
        Assert.AreEqual(1f, JumpingRodHopModel.Compression(0f, over), 1e-4f);

        float previous = 1f;
        for (int i = 0; i <= 20; i++)
        {
            float value = JumpingRodHopModel.Compression(i * 0.025f, over);
            Assert.LessOrEqual(value, previous + 1e-5f, "the coil should only relax as clearance grows");
            Assert.That(value, Is.InRange(0f, 1f));
            previous = value;
        }
    }

    [Test]
    public void HighAboveTheGroundTheCoilIsFullyExtended()
    {
        Assert.AreEqual(0f, JumpingRodHopModel.Compression(40f, 0.5f), 1e-4f);
    }

    [Test]
    public void SunkBelowTheSurfaceTheCoilIsSolidRatherThanOverdriven()
    {
        Assert.AreEqual(1f, JumpingRodHopModel.Compression(-5f, 0.5f), 1e-4f);
    }

    [Test]
    public void AZeroCompressHeightIsExtendedRatherThanDividingByZero()
    {
        Assert.AreEqual(0f, JumpingRodHopModel.Compression(0f, 0f), 1e-4f);
    }
}
