// The steering rules OstrichDriver turns an AI MoveIntent into: how hard to yaw for a given
// heading error, and how much throttle that error and the intent's speed multiplier are worth.
//
// OstrichDriver itself is in Assembly-CSharp and so is unreachable from a test assembly; the rules
// live in OstrichSteering precisely so they can be checked here.
using NUnit.Framework;

public class OstrichSteeringTests
{
    private const float TurnInPlaceAngle = 60f;

    [Test]
    public void TurnsTowardTheTarget()
    {
        Assert.Greater(OstrichSteering.Turn(30f), 0f, "a target to the right should yaw right");
        Assert.Less(OstrichSteering.Turn(-30f), 0f, "a target to the left should yaw left");
        Assert.AreEqual(0f, OstrichSteering.Turn(0f), 1e-4f, "dead ahead needs no yaw");
    }

    [Test]
    public void TurnCommandSaturates()
    {
        Assert.AreEqual(1f, OstrichSteering.Turn(180f), 1e-4f);
        Assert.AreEqual(-1f, OstrichSteering.Turn(-180f), 1e-4f);
    }

    /// A bird this leggy pivots before committing rather than carving a wide arc around every
    /// waypoint.
    [Test]
    public void PivotsInPlaceWhenBadlyMisaligned()
    {
        Assert.AreEqual(0f, OstrichSteering.Throttle(90f, TurnInPlaceAngle, 1f), 1e-4f,
                        "90 degrees off, the bird should turn on the spot");
        Assert.Greater(OstrichSteering.Throttle(10f, TurnInPlaceAngle, 1f), 0f,
                       "roughly aligned, the bird should run on");
    }

    /// A wandering bird should walk, not sprint. The driver used to throw the throttle wide open
    /// for any MoveToPosition intent, so a module asking for a gentle roam got a bird at a dead run
    /// -- and walkThrottle, the field that exists to describe exactly this, went unread.
    [Test]
    public void ThrottleFollowsTheSpeedMultiplier()
    {
        Assert.AreEqual(0.45f, OstrichSteering.Throttle(0f, TurnInPlaceAngle, 0.45f), 1e-4f,
                        "a 45% intent should ask for 45% throttle");
        Assert.AreEqual(1f, OstrichSteering.Throttle(0f, TurnInPlaceAngle, 1f), 1e-4f);
    }

    /// The multiplier scales the throttle; it is not a way to ask for more than the legs were ever
    /// going to give. AgentController already multiplies a ±10% speed variation into every intent.
    [Test]
    public void ThrottleNeverExceedsFull()
    {
        Assert.AreEqual(1f, OstrichSteering.Throttle(0f, TurnInPlaceAngle, 3f), 1e-4f);
    }

    /// A pivot is a pivot however fast the intent wanted to go.
    [Test]
    public void PivotingIgnoresTheSpeedMultiplier()
    {
        Assert.AreEqual(0f, OstrichSteering.Throttle(90f, TurnInPlaceAngle, 1f), 1e-4f);
        Assert.AreEqual(0f, OstrichSteering.Throttle(90f, TurnInPlaceAngle, 0.45f), 1e-4f);
    }
}
