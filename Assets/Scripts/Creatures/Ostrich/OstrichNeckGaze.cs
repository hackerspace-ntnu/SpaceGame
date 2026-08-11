// Where the bird is looking, and when it decides to look somewhere else.
//
// The one thing that separates a live bird from a camera on a stick is that a bird does not SWEEP
// its head. It snaps to a heading, holds dead still while it reads what is there, then snaps
// somewhere else. Easing smoothly between look directions reads as a periscope, which is the exact
// failure this class exists to avoid -- so the motion here is deliberately two-state: slew fast,
// then hold.
//
// Pure state, no components and no Unity callbacks, so an EditMode test can march it directly.
// Randomness comes from an injected seed rather than UnityEngine.Random for the same reason: the
// same seed must give the same sequence in a test as it does in play.
using UnityEngine;

/// Tunables for the gaze. A struct so OstrichNeckMotion can expose the whole thing as one
/// inspector block rather than a dozen loose fields.
[System.Serializable]
public struct OstrichNeckGazeSettings
{
    [Tooltip("Largest left/right look, in degrees, measured from straight ahead.")]
    public float yawRange;
    [Tooltip("Largest up/down look, in degrees. Smaller than yaw: a bird scans the horizon far " +
             "more than it scans the sky.")]
    public float pitchRange;
    [Tooltip("Shortest and longest time the head holds a heading before choosing another.")]
    public float minHold, maxHold;
    [Tooltip("How fast the head snaps to a new heading, in degrees per second. High on purpose -- " +
             "this is a saccade, not a pan.")]
    public float slewRate;
    [Tooltip("Chance that any given look is a big startled turn rather than a small scan.")]
    [Range(0f, 1f)] public float startleChance;
    [Tooltip("Yaw of a startled turn, in degrees. Allowed past yawRange; the neck limits clamp it.")]
    public float startleYaw;
    [Tooltip("How much running suppresses looking around. At 1 a bird at top speed stares dead " +
             "ahead, which is what a sprinting ostrich actually does.")]
    [Range(0f, 1f)] public float travelSuppression;

    public static OstrichNeckGazeSettings Default => new OstrichNeckGazeSettings
    {
        yawRange = 62f,
        pitchRange = 20f,
        minHold = 0.7f,
        maxHold = 2.6f,
        slewRate = 240f,
        startleChance = 0.15f,
        startleYaw = 120f,
        travelSuppression = 0.65f,
    };
}

public sealed class OstrichNeckGaze
{
    private readonly System.Random rng;
    private OstrichNeckGazeSettings s;

    private Vector2 current;        // x = yaw, y = pitch, both degrees from neutral
    private Vector2 target;
    private float holdTimer;
    private bool holding;

    /// Where the head is pointing this frame, in degrees from neutral.
    public Vector2 Current => current;
    /// What it is heading toward. Exposed for tests and gizmos.
    public Vector2 Target => target;
    public bool Holding => holding;

    public OstrichNeckGaze(OstrichNeckGazeSettings settings, int seed)
    {
        s = settings;
        rng = new System.Random(seed);
        holding = true;
        holdTimer = Range(s.minHold, s.maxHold);
    }

    public void Configure(OstrichNeckGazeSettings settings) => s = settings;

    public void Reset()
    {
        current = Vector2.zero;
        target = Vector2.zero;
        holding = true;
        holdTimer = Range(s.minHold, s.maxHold);
    }

    /// One frame. speed01 is travel speed as a fraction of top speed, and is what makes a running
    /// bird stop rubbernecking.
    public void Step(float deltaTime, float speed01)
    {
        float dt = Mathf.Max(deltaTime, 0f);
        float attention = 1f - s.travelSuppression * Mathf.Clamp01(speed01);

        if (holding)
        {
            holdTimer -= dt;
            // A bird at speed also holds each heading longer, because it is looking where it is
            // going rather than surveying.
            if (holdTimer <= 0f)
            {
                target = PickTarget(attention);
                holding = false;
            }
        }

        if (!holding)
        {
            float step = s.slewRate * dt;
            Vector2 delta = target - current;
            float distance = delta.magnitude;
            if (distance <= step || distance < 1e-4f)
            {
                current = target;
                holding = true;
                holdTimer = Range(s.minHold, s.maxHold);
            }
            else
            {
                current += delta * (step / distance);
            }
        }

        // The suppression applies to where the head is allowed to BE, not just to newly chosen
        // targets, so breaking into a run pulls the head forward instead of leaving it stuck at
        // whatever it happened to be staring at.
        float cap = s.yawRange * attention;
        current.x = Mathf.Clamp(current.x, -Mathf.Max(cap, s.startleYaw * attention),
                                 Mathf.Max(cap, s.startleYaw * attention));
        current.y = Mathf.Clamp(current.y, -s.pitchRange * attention, s.pitchRange * attention);
    }

    private Vector2 PickTarget(float attention)
    {
        bool startled = rng.NextDouble() < s.startleChance;
        float yaw = startled
            ? s.startleYaw * (rng.NextDouble() < 0.5 ? -1f : 1f) * (float)rng.NextDouble()
            : Range(-s.yawRange, s.yawRange);

        // Pitch is biased downward: there is far more worth looking at on the ground than above.
        float pitch = Range(-s.pitchRange, s.pitchRange * 0.45f);
        return new Vector2(yaw * attention, pitch * attention);
    }

    private float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
}
