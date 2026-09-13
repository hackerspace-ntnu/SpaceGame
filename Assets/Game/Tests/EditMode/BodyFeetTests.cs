using NUnit.Framework;
using UnityEngine;
using SpaceGame.Locomotion;

/// <summary>
/// Finding a character's soles when their pivot is not at them — which, for this project's player,
/// it is not: the capsule hangs about a metre below the transform everything else is measured
/// from. Anything that answers "how far above the ground is this body" from the pivot is out by
/// that metre, silently and forever.
/// </summary>
public class BodyFeetTests
{
    private GameObject body;

    [TearDown]
    public void TearDown()
    {
        if (body != null) Object.DestroyImmediate(body);
    }

    /// <summary>
    /// A stand-in for the player: a pivot with a capsule slung below it, which is how
    /// PlayerCharacter is authored.
    /// </summary>
    private void Build(float capsuleCentre, float capsuleHeight)
    {
        body = new GameObject("Body");

        var capsule = new GameObject("Collider").AddComponent<CapsuleCollider>();
        capsule.transform.SetParent(body.transform, false);
        capsule.transform.localPosition = new Vector3(0f, capsuleCentre, 0f);
        capsule.height = capsuleHeight;
        capsule.radius = 0.5f;
        capsule.direction = 1;

        Physics.SyncTransforms();
    }

    [Test]
    public void ThePivotAboveTheSolesIsMeasuredRatherThanAssumed()
    {
        // The player's own numbers: a 3 m capsule centred half a metre above the pivot, so the
        // soles are a metre below it.
        Build(0.5f, 3f);

        Assert.AreEqual(1f, new BodyFeet(body.transform).RootAboveFeet, 1e-3f);
    }

    [Test]
    public void APivotAlreadyAtTheSolesAnswersZero()
    {
        Build(1f, 2f);

        Assert.AreEqual(0f, new BodyFeet(body.transform).RootAboveFeet, 1e-3f);
    }

    [Test]
    public void TriggersAreNotPartOfTheBody()
    {
        Build(0.5f, 3f);

        // An interaction volume reaching to the player's ankles and beyond. It is not what they
        // stand on, and letting it answer would drop everything measured from the soles.
        var probe = new GameObject("Reach").AddComponent<SphereCollider>();
        probe.transform.SetParent(body.transform, false);
        probe.isTrigger = true;
        probe.radius = 4f;
        Physics.SyncTransforms();

        Assert.AreEqual(1f, new BodyFeet(body.transform).RootAboveFeet, 1e-3f);
    }

    [Test]
    public void ACrouchThatKeepsTheSolesDownDoesNotMoveThem()
    {
        Build(0.5f, 3f);
        var capsule = body.GetComponentInChildren<CapsuleCollider>();
        var feet = new BodyFeet(body.transform);

        // PlayerStance shortens the capsule and lowers its centre by half of what it lost, so the
        // bottom cap stays on the floor. Re-measured on every read, this follows it either way.
        capsule.height = 2f;
        capsule.center = new Vector3(0f, -0.5f, 0f);
        Physics.SyncTransforms();

        Assert.AreEqual(1f, feet.RootAboveFeet, 1e-3f);
    }
}
