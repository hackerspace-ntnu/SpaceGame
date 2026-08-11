// Being rideable is PREFAB WIRING, not code.
//
// `LeggedDriver` already implements `IRiderControllable` and `IMovementMotor`, so a horse carries a
// rider the moment it has a `MountModule` and a `SteerModule` on it, and drives under an
// `AgentController` the moment it has one of those. Nothing was written to make that true -- which
// is exactly why it needs asserting: a missing seat point or a `MountStation` pointing at nothing
// is silent until a player walks up and cannot get on.
//
// This lives in Assembly-CSharp-Editor rather than in SpaceGame.Tests.EditMode because every type
// it names -- MountModule, SteerModule, MountStation, AgentController, HorseDriver, RiderInput,
// MoveIntent -- is declared in Assembly-CSharp, and an asmdef may not reference a predefined
// assembly. The locomotion's own tests are in the asmdef, where they belong.
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class HorseRigWiringTests
{
    private const string PrefabPath = "Assets/Prefabs/agents/creatures/HorseRobot.prefab";
    private const float Dt = 1f / 60f;

    private GameObject horse;

    /// Destroy what this made, on the failure path too. A leaked clone stands at the world origin
    /// in the editor's open scene and the next machine measured in that editor stands on it.
    [TearDown]
    public void TearDown()
    {
        if (horse != null) Object.DestroyImmediate(horse);
        horse = null;

        foreach (GameObject go in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go != null && go.transform.parent == null && go.name.StartsWith("HorseRobot"))
                Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ThePrefabCarriesTheMountAndAgentRig()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "Horse prefab missing at " + PrefabPath +
                         " -- run Tools/Creatures/Build Horse Robot Prefab.");

        Assert.IsNotNull(prefab.GetComponent<HorseLocomotion>(), "no locomotion");
        Assert.IsNotNull(prefab.GetComponent<HorseDriver>(), "no driver");
        Assert.IsNotNull(prefab.GetComponent<HorseSpineMotion>(), "no spine motion");

        var mount = prefab.GetComponent<MountModule>();
        Assert.IsNotNull(mount, "not rideable: no MountModule");
        Assert.IsNotNull(prefab.GetComponent<SteerModule>(), "no SteerModule to steer it with");

        AgentController[] controllers = prefab.GetComponents<AgentController>();
        Assert.AreEqual(1, controllers.Length,
            "exactly one AgentController -- with two, which one answers GetComponent is a coin toss");

        var station = prefab.GetComponentInChildren<MountStation>(true);
        Assert.IsNotNull(station, "no MountStation for a player to interact with");

        var so = new SerializedObject(mount);
        Assert.IsNotNull(so.FindProperty("seatPoint").objectReferenceValue,
                         "MountModule has no seat point; a rider would be parented to the origin");

        var sso = new SerializedObject(station);
        Assert.AreEqual(mount, sso.FindProperty("mount").objectReferenceValue,
                        "the MountStation is not wired to this machine's MountModule");

        Rigidbody rb = prefab.GetComponent<Rigidbody>();
        Assert.IsNotNull(rb, "no Rigidbody: every collider on the machine would be a static one");
        Assert.IsTrue(rb.isKinematic, "the locomotion is the single owner of the pose (I4)");
        Assert.IsFalse(rb.useGravity, "gravity here is procedural, not physical");
    }

    /// Both driver channels reach the legs. Driven directly rather than through `Update`, which
    /// EditMode never runs.
    [Test]
    public void BothDriverChannelsReachTheLegs()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, "Horse prefab missing at " + PrefabPath);

        horse = Object.Instantiate(prefab);
        var driver = horse.GetComponent<HorseDriver>();
        Assert.IsNotNull(driver);
        Assert.IsInstanceOf<IRiderControllable>(driver, "a rider could not steer this");
        Assert.IsInstanceOf<IMovementMotor>(driver, "an AI module could not move this");

        // AI channel first, because the rider-frame guard is real: a rider that spoke this frame
        // stands the AI channel down, and the two would otherwise fight over one heading.
        //
        // The destination is inside the stop distance on purpose. Further out the driver would
        // path to it, and `NavMesh.CalculatePath` needs the NavMeshPath that `Awake` builds --
        // which EditMode never runs. What is under test here is that the channel is wired, not
        // that the NavMesh is baked.
        Vector3 near = horse.transform.position + horse.transform.forward * 1f;
        driver.Tick(new MoveIntent
        {
            Type = AgentIntentType.MoveToPosition,
            TargetPosition = near,
            StopDistance = 5f,
            SpeedMultiplier = 1f,
        }, Dt);
        Assert.IsTrue(driver.CurrentDestination.HasValue,
                      "the AI channel did not take the destination");
        Assert.IsTrue(driver.HasReachedDestination, "it should already be there");

        driver.ApplyRiderInput(new RiderInput(new Vector2(0.4f, 1f), 0f, true), Dt);
        Assert.IsTrue(driver.IsRiderDriven, "the rider channel did not register");

        // And now the guard: with a rider aboard this frame, the AI channel is ignored outright.
        driver.Tick(new MoveIntent
        {
            Type = AgentIntentType.MoveToPosition,
            TargetPosition = horse.transform.position + horse.transform.right * 40f,
            StopDistance = 2f,
            SpeedMultiplier = 1f,
        }, Dt);
        Assert.AreEqual(near, driver.CurrentDestination.Value,
                        "the rider-frame guard let the AI channel steal the frame");
    }
}
