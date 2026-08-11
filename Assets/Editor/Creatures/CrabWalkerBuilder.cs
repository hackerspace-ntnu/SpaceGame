// Builds the three crab walker prefabs from the three FBXs.
//
// One machine, three leg counts, and deliberately ONE code path: nothing below knows how many legs
// a variant has. The leg ids are read off the armature, the collision is measured off the meshes,
// and `CrabLocomotion` derives its swing count and minimum planted count from whatever
// `WalkerRig.Build` finds. Adding a ten-legged variant is a Blender argument and a line in `Legs`.
//
// The FBX arrives WITH its rig — CRAB_Rig, a Root plus one Coxa/Hip/Knee/Ankle/Foot chain per leg
// and one Arm/Shoulder/Elbow/Wrist chain per claw — because the locomotion measures that hierarchy
// live at Initialise and poses it every frame. This script does not build the articulation; it
// wires up everything around it.
//
// Model orientation: authored −Y forward in Blender, which the default FBX axis conversion lands on
// Unity's +Z. There is deliberately no yaw correction here.
//
// Re-run from: Tools ▸ Creatures ▸ Build Crab Walker Prefabs
using System.Collections.Generic;
using System.Linq;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;

public static class CrabWalkerBuilder
{
    private const string ModelDir = "Assets/Models/Creatures/Robotic/Crab";
    private const string PrefabDir = "Assets/Prefabs/agents/creatures";

    /// The variants. A leg count is the only thing that differs.
    private static readonly int[] Legs = { 4, 6, 8 };

    /// Body meshes that get a collision box of their own, and the name the box takes. Anything not
    /// listed is decoration and is deliberately not solid.
    private static readonly (string mesh, string col)[] BodyBoxes =
    {
        ("Mesh_Crab_Carapace", "COL_Carapace"),
        ("Mesh_Crab_Underbelly", "COL_Underbelly"),
        ("Mesh_Crab_Prow", "COL_Prow"),
        ("Mesh_Crab_Stern", "COL_Stern"),
    };

    /// Limb meshes get a box each, named for the joint they hang off.
    private static readonly (string suffix, string joint)[] LimbBoxes =
    {
        ("Upper", "Hip"),
        ("Lower", "Knee"),
        ("Foot", "Ankle"),
    };

    [MenuItem("Tools/Creatures/Build Crab Walker Prefabs")]
    public static void BuildAll()
    {
        foreach (int legs in Legs) Build(legs);
        AssetDatabase.SaveAssets();
    }

    private static void Build(int legCount)
    {
        string modelPath = $"{ModelDir}/crab_walker_{legCount}.fbx";
        string prefabPath = $"{PrefabDir}/CrabWalker{legCount}.prefab";

        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null)
        {
            Debug.LogError($"[Crab] No model at {modelPath}. Run " +
                           "Assets/Models/_Source~/models/creatures/crab_walker_export.py first.");
            return;
        }

        var root = new GameObject($"CrabWalker{legCount}");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely,
                                           InteractionMode.AutomatedAction);
        instance.transform.SetParent(root.transform, false);

        Dictionary<string, Transform> parts = root.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name)
            .ToDictionary(g => g.Key, g => g.First());

        Transform armature = WalkerRig.FindArmature(root.transform);
        // Read the leg ids off the rig rather than listing them: the whole machine is parameterised
        // by leg count, and a hard-coded id table is the one place that would have to be edited per
        // variant. It is also what catches an export that lost its armature.
        List<string> ids = LegIds(root.transform);
        if (armature == null || ids.Count != legCount)
        {
            Debug.LogError($"[Crab] {modelPath} has {ids.Count} leg chains, expected {legCount}. " +
                           "Expected bones named Coxa_/Hip_/Knee_/Ankle_/Foot_<id>. Was the FBX " +
                           "exported with object_types including ARMATURE?");
            Object.DestroyImmediate(root);
            return;
        }

        DropModelOntoHips(root, instance);

        int boxes = 0;
        foreach ((string mesh, string col) in BodyBoxes) boxes += AddBox(parts, mesh, col) ? 1 : 0;
        foreach (string id in ids)
            foreach ((string suffix, string joint) in LimbBoxes)
                boxes += AddLimbBox(parts, id, suffix, joint) ? 1 : 0;

        WireLocomotion(root, armature);

        string rigName = armature.name;
        System.IO.Directory.CreateDirectory(PrefabDir);
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        Debug.Log($"[Crab] Built {prefabPath}: {legCount} legs on {rigName}, {boxes} collision boxes.");
    }

    /// The leg ids the armature actually carries, taken from the Coxa_ bones. Sorted so a rebuild
    /// logs the same order twice.
    private static List<string> LegIds(Transform root)
    {
        var ids = new List<string>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("Coxa_")) ids.Add(t.name.Substring(5));
        ids.Sort();
        return ids;
    }

    /// Sink the model so the prefab's own origin sits on the HIP PLANE rather than on the soles.
    ///
    /// This is not cosmetic. `LeggedLocomotion` takes ride height as `body.y - averageFootY`, and
    /// the model is authored standing on z = 0 — so with the root left on the ground that difference
    /// is zero, the hull is pinned at foot level, every hip sits on the floor, and every leg is
    /// asked to reach the ground from there. Permanently unreachable, and no leg ever swings,
    /// because the gait cannot place a foothold it cannot reach.
    ///
    /// Measured off the rig rather than typed in, so re-proportioning the model re-derives it.
    private static void DropModelOntoHips(GameObject root, GameObject instance)
    {
        float hipSum = 0f;
        int hips = 0;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Hip_")) continue;
            hipSum += t.position.y;
            hips++;
        }
        if (hips == 0) return;

        float soleY = float.MaxValue;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            soleY = Mathf.Min(soleY, r.bounds.min.y);

        float rideHeight = hipSum / hips - soleY;
        instance.transform.localPosition = new Vector3(0f, -rideHeight, 0f);
        Debug.Log($"[Crab] Origin set on the hip plane: ride height {rideHeight:F2} m.");
    }

    /// A box matching one mesh, parented to that mesh so it rides whatever bone moves it.
    private static bool AddBox(Dictionary<string, Transform> parts, string meshName, string colName)
    {
        if (!parts.TryGetValue(meshName, out Transform t)) return false;
        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;

        var box = new GameObject(colName).transform;
        box.SetParent(t, false);
        BoxCollider bc = box.gameObject.AddComponent<BoxCollider>();
        // Local bounds, not world: parented to the mesh, the collider inherits its transform, so
        // the box stays right as the leg swings instead of being frozen at the rest pose.
        bc.center = mf.sharedMesh.bounds.center;
        bc.size = mf.sharedMesh.bounds.size;
        return true;
    }

    /// A limb's collision box, hung DIRECTLY off its joint rather than off the mesh.
    ///
    /// The nesting matters and it is not obvious: the runtime takes a segment's radius from the
    /// first `COL_`-prefixed box under the joint and searches recursively, so a box parked one level
    /// down under the mesh is still found — but the depth-first walk reaches the knee's subtree
    /// before the thigh's own mesh, and the thigh ends up measuring the shin. On the crawler fixing
    /// exactly this took worst reach from 5.88 to 1.38.
    ///
    /// The mesh is found by walking the joint's own subtree, NOT by name, so a duplicated `.001`
    /// suffix or a part reused elsewhere in the rig cannot hand back the wrong one.
    private static bool AddLimbBox(Dictionary<string, Transform> parts, string id, string suffix,
                                   string joint)
    {
        if (!parts.TryGetValue($"{joint}_{id}", out Transform bone)) return false;

        Transform mesh = FindLimbMesh(bone, suffix);
        if (mesh == null) return false;
        MeshFilter mf = mesh.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return false;

        var box = new GameObject($"COL_{joint}_{id}").transform;
        box.SetParent(bone, false);
        box.localPosition = mesh.localPosition;
        box.localRotation = mesh.localRotation;
        box.localScale = mesh.localScale;

        BoxCollider bc = box.gameObject.AddComponent<BoxCollider>();
        bc.center = mf.sharedMesh.bounds.center;
        bc.size = mf.sharedMesh.bounds.size;
        return true;
    }

    private static Transform FindLimbMesh(Transform joint, string suffix)
    {
        string needle = $"_{suffix}";
        foreach (MeshFilter mf in joint.GetComponentsInChildren<MeshFilter>(true))
            if (mf.name.Contains(needle)) return mf.transform;
        return null;
    }

    private static void WireLocomotion(GameObject root, Transform armature)
    {
        CrabLocomotion loco = root.AddComponent<CrabLocomotion>();
        var so = new SerializedObject(loco);
        Set(so, "armatureRoot", armature);
        Set(so, "body", root.transform);
        // 28, not the crawler's 40. Stride is 2 * RestFootRadius * sin(yawRange * 0.85), so this is
        // the machine's top speed — and on a splayed rig it is also how far a foot is dragged from
        // home before it steps. Measured across 40 / 34 / 28 on all three variants: 40 walks at
        // steady reach 0.94..0.99 with nothing to spare, and overshoots past 1.6 in the first three
        // seconds after a cold start while the feet, which all begin at home and in phase, catch up
        // with the wave. 28 costs 28% of the top speed and brings steady reach to 0.88..0.93 and the
        // cold-start peak to 1.07. A crab is not a fast machine.
        SetFloat(so, "yawRange", 28f);
        SetFloat(so, "hipRange", 45f);
        SetFloat(so, "kneeRange", 60f);
        SetFloat(so, "ankleRange", 45f);
        SetFloat(so, "rollRange", 30f);
        // Left at 0 / -1: DERIVED from the leg count at Bind, which is the whole reason one
        // component covers four to eight legs. A swing count authored here would leave the
        // eight-legged variant creeping and the four-legged one standing on two feet.
        SetInt(so, "swingLegs", 0);
        SetInt(so, "minPlantedLegs", -1);
        SetFloat(so, "footholdReach", 0.78f);
        SetFloat(so, "stepDuration", 0.4f);
        SetFloat(so, "stepClearance", 0.10f);
        SetFloat(so, "obstacleClearance", 0.4f);
        // High, unlike the crawler's deck: nothing rides on a crab to be tipped off, and a shell
        // held flat over a slope strands its downhill legs.
        SetFloat(so, "slopeFollow", 0.92f);
        SetFloat(so, "maxShellTilt", 35f);
        SetFloat(so, "clawSway", 0.12f);
        SetFloat(so, "clawRaise", 0.72f);
        SetFloat(so, "clawRaiseTime", 0.35f);
        // Every layer. Left unset this serialises as 0, and a walker whose ground mask matches
        // nothing finds no ground at all: it never snaps down, never places a foothold, and stands
        // perfectly still looking for all the world like a rig fault. The machine's own colliders
        // are rejected by WalkerGround regardless of mask.
        SetInt(so, "groundMask", ~0);
        SetFloat(so, "rayStartAbove", 4f);
        SetFloat(so, "rayLength", 120f);
        SetBool(so, "snapToGroundOnStart", true);
        SetBool(so, "autoCalibrateRideHeight", true);
        SetFloat(so, "heightSmooth", 5f);
        SetBool(so, "drawGizmos", true);
        so.ApplyModifiedPropertiesWithoutUndo();

        root.AddComponent<CrabClaws>();

        CrabDriver driver = root.AddComponent<CrabDriver>();
        var dso = new SerializedObject(driver);
        // THE flag that makes this machine what it is: the drive becomes planar, so the crab sets
        // off toward its destination whatever way it is pointing and turns only to bring the target
        // abeam — the axis its legs sweep fastest on.
        SetBool(dso, "lateralSteering", true);
        SetFloat(dso, "moveSpeed", 1.6f);
        SetFloat(dso, "turnSpeed", 20f);
        SetFloat(dso, "acceleration", 1.6f);
        SetFloat(dso, "defaultStopDistance", 5f);
        SetFloat(dso, "cornerArriveRadius", 5f);
        SetFloat(dso, "navMeshSampleDistance", 12f);
        dso.ApplyModifiedPropertiesWithoutUndo();

        // Kinematic, gravity off. The locomotion writes the hull transform directly (invariant I4),
        // so a dynamic body would fight it every frame and win. It is here because without a
        // Rigidbody every collider on this machine is a STATIC collider, and moving static colliders
        // makes PhysX rebuild its broadphase every frame.
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    // Private [SerializeField] fields are not reachable from an editor script any other way, and
    // making them public purely so this could set them would widen the runtime API for a build-time
    // convenience. A missing name warns loudly rather than silently doing nothing.
    private static void Set(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = Find(so, field);
        if (p != null) p.objectReferenceValue = value;
    }

    private static void SetFloat(SerializedObject so, string field, float value)
    {
        SerializedProperty p = Find(so, field);
        if (p != null) p.floatValue = value;
    }

    private static void SetInt(SerializedObject so, string field, int value)
    {
        SerializedProperty p = Find(so, field);
        if (p != null) p.intValue = value;
    }

    private static void SetBool(SerializedObject so, string field, bool value)
    {
        SerializedProperty p = Find(so, field);
        if (p != null) p.boolValue = value;
    }

    private static SerializedProperty Find(SerializedObject so, string field)
    {
        SerializedProperty p = so.FindProperty(field);
        if (p == null)
            Debug.LogWarning($"[Crab] {so.targetObject.GetType().Name} has no serialized field " +
                             $"'{field}' — it was renamed; this value is unset.");
        return p;
    }
}
