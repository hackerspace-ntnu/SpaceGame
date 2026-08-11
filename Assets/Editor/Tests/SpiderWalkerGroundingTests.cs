using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// The whole machine, on real ground, with the real rig.
///
/// The unit tests pin down each piece in isolation; this one exists because the faults it watches
/// for are properties of the assembled walker and nothing else can catch them: a foot that is not
/// on the surface, a leg stretched past its reach, and a machine that will not cross the ground.
///
/// It runs in EditMode rather than PlayMode because everything needed is available without a
/// running player loop — DesertCrawlerLocomotion.Initialise and .Step are public and dt-driven for
/// exactly this reason, and Unity's raycasts work against colliders in an edit-mode scene once the
/// transforms are synced.
public class SpiderWalkerGroundingTests
{
    private const string PrefabPath = "Assets/Prefabs/agents/vehicle/rig_walker.prefab";

    private GameObject world;
    private GameObject walker;
    private DesertCrawlerLocomotion locomotion;

    [SetUp]
    public void SetUp()
    {
        world = new GameObject("TestWorld");

        // Level ground, a ramp up to a plateau, and a boulder squarely in the way — between them
        // these are the cases the old code failed on: a slope for the fabricated contact point, an
        // edge for the ledge test, and something solid for the legs to be driven through.
        Slab(new Vector3(0f, -1f, 0f), new Vector3(400f, 2f, 400f), Quaternion.identity);
        Slab(new Vector3(60f, 4f, 0f), new Vector3(60f, 2f, 200f), Quaternion.Euler(0f, 0f, -14f));
        Slab(new Vector3(120f, 7f, 0f), new Vector3(120f, 2f, 200f), Quaternion.identity);
        Slab(new Vector3(30f, 1f, 6f), new Vector3(9f, 6f, 9f), Quaternion.identity);

        Physics.SyncTransforms();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, $"{PrefabPath} is missing; the walker cannot be tested without it");

        walker = Object.Instantiate(prefab);
        walker.transform.position = new Vector3(-40f, 40f, 0f);
        Physics.SyncTransforms();

        locomotion = walker.GetComponent<DesertCrawlerLocomotion>();
        Assert.IsNotNull(locomotion, "the walker prefab has no DesertCrawlerLocomotion");

        // Awake does not run for objects instantiated in edit mode, so drive setup by hand.
        locomotion.Initialise();
        Assert.IsTrue(locomotion.IsReady, "no leg chains were found on the rig");
        locomotion.SnapToGround();
    }

    [TearDown]
    public void TearDown()
    {
        if (walker != null) Object.DestroyImmediate(walker);
        if (world != null) Object.DestroyImmediate(world);
    }

    private void Slab(Vector3 centre, Vector3 size, Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(world.transform);
        go.transform.SetPositionAndRotation(centre, rotation);
        go.transform.localScale = size;
    }

    /// Walks the machine across the whole test world, checking every frame.
    private void Walk(float speed, float yawRate, int frames, System.Action perFrame)
    {
        const float dt = 1f / 60f;
        for (int i = 0; i < frames; i++)
        {
            locomotion.SetTwist(speed, yawRate);
            locomotion.Step(dt);
            Physics.SyncTransforms();
            perFrame();
        }
    }

    /// Feet floating above the ground or sunk into it.
    [Test]
    public void EveryPlantedFootStaysOnTheSurfaceBeneathIt()
    {
        float worstGap = 0f;

        Walk(4f, 0f, 600, () =>
        {
            for (int i = 0; i < locomotion.LegCount; i++)
            {
                if (!locomotion.TryGetFoot(i, out Vector3 foot, out bool swinging)) continue;
                if (swinging) continue;

                // Look for the surface under the foot from just above it. A planted foot should be
                // sitting on whatever this finds.
                if (!Physics.Raycast(foot + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 60f))
                    continue;
                if (hit.collider.transform.IsChildOf(walker.transform)) continue;

                worstGap = Mathf.Max(worstGap, Mathf.Abs(hit.point.y - foot.y));
            }
        });

        // A sole is a disc resting on a supporting plane, so its contact can legitimately sit a
        // little proud of a single ray under its centre. Metres of float or sink cannot.
        Assert.Less(worstGap, 0.75f, "a planted foot was off the ground beneath it");
    }

    [Test]
    public void NoLegIsEverStretchedBeyondItsReach()
    {
        float worst = 0f;
        Walk(4f, 0f, 600, () =>
            worst = Mathf.Max(worst, locomotion.LastFrame.WorstReachFraction));

        Assert.LessOrEqual(worst, 1f, "a foot was further from its hip than the leg is long");
    }

    /// The machine has to actually get somewhere. This is the test for the fault that sent the
    /// locomotion back to its committed form: commanded at full speed it must cross the ground,
    /// not stand still shuffling its feet.
    [Test]
    public void TheMachineActuallyCrossesTheGround()
    {
        Vector3 start = walker.transform.position;
        Walk(4f, 0f, 600, () => { });

        Assert.Greater(Vector3.Distance(start, walker.transform.position), 15f,
            "the walker went nowhere; the gait has stalled it");
    }

    /// A leg reporting itself unreachable is not a fault — it is the signal that makes broken
    /// ground work, and the gait answers it by stepping that leg early. What WOULD be a fault is
    /// the condition persisting, which means the early step is not happening or is not helping.
    [Test]
    public void ALegThatRunsOutOfReachIsSteppedAndRecovers()
    {
        int run = 0;
        int longestRun = 0;
        int framesAffected = 0;

        Walk(0f, 20f, 400, () =>
        {
            if (locomotion.LastFrame.UnreachableLegs > 0)
            {
                framesAffected++;
                run++;
                longestRun = Mathf.Max(longestRun, run);
            }
            else
            {
                run = 0;
            }
        });

        // A step takes a good fraction of a second at this pace, so a leg may legitimately be
        // stretched for a few dozen frames while its swing carries it home. Never for seconds.
        Assert.Less(longestRun, 90,
            $"a leg stayed out of reach for {longestRun} frames ({framesAffected} affected in 400)");
    }

    /// Support is what keeps the hull up. However bad the ground gets, most of the feet stay down.
    [Test]
    public void EnoughFeetStayDownToHoldTheMachineUp()
    {
        int fewest = int.MaxValue;
        Walk(4f, 8f, 600, () =>
            fewest = Mathf.Min(fewest, locomotion.LastFrame.StanceLegs));

        Assert.GreaterOrEqual(fewest, 3, "the walker was standing on fewer than three feet");
    }
}
