using NUnit.Framework;
using SpaceGame.Locomotion;
using UnityEngine;

/// The supporting plane a rigid sole comes to rest on.
///
/// The code this replaces paired the centre ray's x/z with the HIGHEST neighbour's y and called
/// that a contact point. That point lies on no surface at all, which is why feet were seen
/// hovering above the ground on one side of a slope and buried in it on the other.
public class WalkerSurfaceTests
{
    private static WalkerFootprintSample S(float x, float y, float z, Vector3 n) =>
        new WalkerFootprintSample { Point = new Vector3(x, y, z), Normal = n };

    /// Level ground: nothing to fit, and the answer must be the obvious one.
    [Test]
    public void FlatGround_ContactSitsOnTheSurface()
    {
        var samples = new[]
        {
            S(0f, 10f, 0f, Vector3.up),
            S(1f, 10f, 0f, Vector3.up),
            S(-1f, 10f, 0f, Vector3.up),
            S(0f, 10f, 1f, Vector3.up),
            S(0f, 10f, -1f, Vector3.up),
        };

        Assert.IsTrue(WalkerSurface.TryFit(new Vector3(0f, 0f, 0f), samples, samples.Length,
                                           out WalkerSurface s));

        Assert.AreEqual(10f, s.Point.y, 1e-4f);
        Assert.AreEqual(0f, s.Point.x, 1e-4f, "the contact stays under the sole's centre");
        Assert.AreEqual(0f, s.Point.z, 1e-4f);
        Assert.Less(Vector3.Angle(s.Normal, Vector3.up), 1e-2f);
    }

    /// The invariant that makes the sole a rigid body: no part of the ground may poke through it.
    /// Stated as the plane test rather than a height comparison, because on a slope the sole is
    /// tilted and "above every sample in y" is both wrong and too weak.
    [Test]
    public void NoFootprintSampleEverPokesThroughTheSole()
    {
        foreach (WalkerFootprintSample[] samples in new[]
        {
            // a slope running along x
            new[]
            {
                S(0f, 10f, 0f, Vector3.up), S(1f, 10.6f, 0f, Vector3.up),
                S(-1f, 9.4f, 0f, Vector3.up), S(0f, 10f, 1f, Vector3.up), S(0f, 10f, -1f, Vector3.up),
            },
            // one corner sitting on a rock: the case that used to bury the opposite toe
            new[]
            {
                S(0f, 10f, 0f, Vector3.up), S(1f, 12.5f, 0f, Vector3.up),
                S(-1f, 10f, 0f, Vector3.up), S(0f, 10f, 1f, Vector3.up), S(0f, 10f, -1f, Vector3.up),
            },
            // broken ground, every sample disagreeing
            new[]
            {
                S(0f, 10f, 0f, Vector3.up), S(1f, 11.2f, 0f, new Vector3(0.4f, 1f, 0f).normalized),
                S(-1f, 9.1f, 0f, new Vector3(-0.3f, 1f, 0.2f).normalized),
                S(0f, 10.7f, 1f, Vector3.up), S(0f, 9.6f, -1f, Vector3.up),
            },
        })
        {
            Assert.IsTrue(WalkerSurface.TryFit(Vector3.zero, samples, samples.Length,
                                               out WalkerSurface s));

            foreach (WalkerFootprintSample sample in samples)
            {
                float through = Vector3.Dot(sample.Point - s.Point, s.Normal);
                Assert.LessOrEqual(through, 1e-3f,
                    $"sample at {sample.Point} pokes {through} through a sole resting at {s.Point}");
            }
        }
    }

    /// A steep slope is still ground, and the contact belongs on it rather than at the height of
    /// its highest sample. The footprint spans 1.7 units of height here while lying exactly on one
    /// plane, which is the case a raw height spread gets wrong.
    [Test]
    public void AUniformSlope_PutsTheContactOnThePlaneUnderTheCentre()
    {
        const float degrees = 40f;
        float t = degrees * Mathf.Deg2Rad;
        Vector3 n = new Vector3(-Mathf.Sin(t), Mathf.Cos(t), 0f);

        // Ground rising along +x at 40 degrees, sampled over a unit footprint.
        WalkerFootprintSample At(float x, float z) => S(x, 10f + x * Mathf.Tan(t), z, n);
        var slope = new[] { At(0f, 0f), At(1f, 0f), At(-1f, 0f), At(0f, 1f), At(0f, -1f) };

        Assert.IsTrue(WalkerSurface.TryFit(Vector3.zero, slope, slope.Length, out WalkerSurface s));

        Assert.AreEqual(10f, s.Point.y, 1e-3f, "the contact sits on the plane under the centre");
        Assert.Less(Vector3.Angle(s.Normal, n), 1e-2f);
    }

    [Test]
    public void NoSamples_ReportsFailureRatherThanInventingASurface()
    {
        Assert.IsFalse(WalkerSurface.TryFit(Vector3.zero, new WalkerFootprintSample[4], 0, out _));
    }

    /// A vertical face returns a normal the caller can reject, and must not divide by a zero
    /// vertical component while doing it.
    [Test]
    public void VerticalFace_DoesNotProduceNaN()
    {
        var wall = new[]
        {
            S(0f, 10f, 0f, Vector3.right), S(0f, 11f, 0f, Vector3.right), S(0f, 9f, 0f, Vector3.right),
        };

        WalkerSurface.TryFit(Vector3.zero, wall, wall.Length, out WalkerSurface s);

        Assert.IsFalse(float.IsNaN(s.Point.x) || float.IsNaN(s.Point.y) || float.IsNaN(s.Point.z));
        Assert.IsFalse(float.IsNaN(s.Normal.x) || float.IsNaN(s.Normal.y) || float.IsNaN(s.Normal.z));
    }
}
