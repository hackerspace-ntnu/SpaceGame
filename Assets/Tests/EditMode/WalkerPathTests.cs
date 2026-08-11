using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

public class WalkerPathTests
{
    /// The walker's deck sits this far above the NavMesh the corners were sampled on.
    private const float RideHeight = 11.5f;
    private const float ArriveRadius = 4f;

    private static Vector3[] Corners(params Vector3[] points) => points;

    private static WalkerPath PathAlong(params Vector3[] points)
    {
        var path = new WalkerPath();
        path.Set(points, points.Length);
        return path;
    }

    // ─────────── the cursor ───────────

    [Test]
    public void EmptyPath_HasNoSteerTarget()
    {
        var path = new WalkerPath();

        Assert.IsFalse(path.HasPath);
        Assert.IsFalse(path.TryGetSteerTarget(Vector3.zero, ArriveRadius, out _));
    }

    [Test]
    public void FirstCornerIsDropped_BecauseItIsTheAgentsOwnPosition()
    {
        // NavMesh.CalculatePath always returns the start position as corner 0. Steering at it
        // gives a zero-length heading vector, so the walker would never turn at all.
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 50f));

        Assert.IsTrue(path.TryGetSteerTarget(Vector3.zero, ArriveRadius, out Vector3 target));
        Assert.AreEqual(50f, target.z, 1e-3f);
    }

    [Test]
    public void SingleCornerPath_HasNothingToSteerAt()
    {
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f));

        Assert.IsFalse(path.HasPath);
        Assert.IsFalse(path.TryGetSteerTarget(Vector3.zero, ArriveRadius, out _));
    }

    [Test]
    public void CornerCountShorterThanBuffer_IgnoresTheStaleTail()
    {
        // GetCornersNonAlloc fills a reusable buffer and reports how much of it is real. Reading
        // past that would steer the walker at a corner from a path it finished two orders ago.
        Vector3[] buffer = Corners(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 20f),
            new Vector3(999f, 0f, 999f));

        var path = new WalkerPath();
        path.Set(buffer, 2);

        Assert.AreEqual(1, path.RemainingCorners);
        Assert.IsTrue(path.TryGetSteerTarget(Vector3.zero, ArriveRadius, out Vector3 target));
        Assert.AreEqual(20f, target.z, 1e-3f);

        // Reaching that corner spends the path; the stale third corner is never offered.
        Assert.IsFalse(path.TryGetSteerTarget(new Vector3(0f, 0f, 20f), ArriveRadius, out _));
    }

    // ─────────── arrival ───────────

    [Test]
    public void ArrivalIsMeasuredFlat_SoTheRideHeightDoesNotBlockProgress()
    {
        // The hull is 11.5 m above the corner it is standing on. In 3D it is never within the
        // arrive radius, so a 3D test leaves the walker grinding at that corner forever.
        WalkerPath path = PathAlong(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 30f),
            new Vector3(40f, 0f, 30f));

        Vector3 overFirstCorner = new Vector3(0f, RideHeight, 30f);

        Assert.IsTrue(path.TryGetSteerTarget(overFirstCorner, ArriveRadius, out Vector3 target));
        Assert.AreEqual(40f, target.x, 1e-3f);
    }

    [Test]
    public void SeveralCornersInsideTheRadius_AreConsumedInOneCall()
    {
        // A machine this size covers short corners between frames. Advancing one per frame would
        // aim it at a corner it is already past.
        WalkerPath path = PathAlong(
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(2f, 0f, 0f),
            new Vector3(3f, 0f, 0f),
            new Vector3(60f, 0f, 0f));

        Assert.IsTrue(path.TryGetSteerTarget(Vector3.zero, ArriveRadius, out Vector3 target));
        Assert.AreEqual(60f, target.x, 1e-3f);
    }

    [Test]
    public void PathIsSpent_OnceTheLastCornerIsReached()
    {
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 25f));

        Assert.IsFalse(path.TryGetSteerTarget(new Vector3(0f, RideHeight, 25f), ArriveRadius, out _));
        Assert.IsFalse(path.HasPath);
        Assert.AreEqual(0, path.RemainingCorners);
    }

    // ─────────── the cursor never rewinds ───────────

    [Test]
    public void OvershootingACorner_DoesNotSendTheWalkerBackToIt()
    {
        // The walker is long and pivots slowly, so it swings wide of every turn. A nearest-corner
        // search would hand it the corner it just rounded and it would walk the leg twice.
        WalkerPath path = PathAlong(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 30f),
            new Vector3(60f, 0f, 30f));

        path.TryGetSteerTarget(new Vector3(0f, RideHeight, 30f), ArriveRadius, out _);

        // Carried past the turn by its own momentum, and now nearer the corner behind it.
        Vector3 overshot = new Vector3(0f, RideHeight, 45f);
        Assert.IsTrue(path.TryGetSteerTarget(overshot, ArriveRadius, out Vector3 target));
        Assert.AreEqual(60f, target.x, 1e-3f, "steered back at the corner already rounded");
    }

    [Test]
    public void ResettingThePath_StartsTheCursorOver()
    {
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f));
        path.TryGetSteerTarget(new Vector3(0f, 0f, 10f), ArriveRadius, out _);
        Assert.IsFalse(path.HasPath);

        path.Set(Corners(new Vector3(0f, 0f, 10f), new Vector3(0f, 0f, 80f)), 2);

        Assert.IsTrue(path.TryGetSteerTarget(new Vector3(0f, 0f, 10f), ArriveRadius, out Vector3 target));
        Assert.AreEqual(80f, target.z, 1e-3f);
    }

    [Test]
    public void ClearedPath_HasNothingToSteerAt()
    {
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 40f));
        path.Clear();

        Assert.IsFalse(path.HasPath);
        Assert.IsFalse(path.TryGetSteerTarget(Vector3.zero, ArriveRadius, out _));
    }

    // ─────────── distance ───────────

    [Test]
    public void RemainingDistance_FollowsThePathRatherThanTheStraightLine()
    {
        // A dog-leg around an obstacle: 30 forward then 40 across is 70 m of walking, against
        // 50 m as the crow flies. Pacing the walk off the straight line stops it early.
        WalkerPath path = PathAlong(
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 0f, 30f),
            new Vector3(40f, 0f, 30f));

        Assert.AreEqual(70f, path.RemainingDistance(Vector3.zero), 1e-3f);
    }

    [Test]
    public void RemainingDistance_IgnoresRideHeight()
    {
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 30f));

        Assert.AreEqual(30f, path.RemainingDistance(new Vector3(0f, RideHeight, 0f)), 1e-3f);
    }

    [Test]
    public void RemainingDistance_IsZeroOnceThePathIsSpent()
    {
        WalkerPath path = PathAlong(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f));
        path.TryGetSteerTarget(new Vector3(0f, 0f, 10f), ArriveRadius, out _);

        Assert.AreEqual(0f, path.RemainingDistance(new Vector3(0f, 0f, 10f)), 1e-3f);
    }
}
