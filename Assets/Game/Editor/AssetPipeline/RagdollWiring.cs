using System.Collections.Generic;
using System.Text;
using SpaceGame.Agents;
using SpaceGame.Characters;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Locomotion;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Puts <see cref="AgentRagdoll"/> on every creature with a body worth felling, and
    /// <see cref="PlayerRagdoll"/> on the player.
    ///
    /// <para>
    /// A tool rather than a hand edit because most of these prefabs are VARIANTS — their root is a
    /// prefab instance, not an authored GameObject — and adding a component to one by editing the
    /// YAML means writing a modification entry against a source object rather than a component
    /// block, which is the kind of thing that silently produces a prefab Unity cannot open. Going
    /// through PrefabUtility means the variant, the instance overrides and the .meta files all come
    /// out right.
    /// </para>
    ///
    /// <para>
    /// Re-runnable. It skips anything already wired, so it can be run again after new creatures are
    /// added without touching the ones that already have it.
    /// </para>
    /// </summary>
    public static class RagdollWiring
    {
        private const string AgentPrefabRoot = "Assets/Game/Prefabs";

        [MenuItem("Tools/SpaceGame/Ragdoll/Wire Prefabs")]
        public static void WirePrefabs()
        {
            var report = new StringBuilder("[Ragdoll] wiring:\n");
            int changed = 0;

            foreach (string path in PrefabPaths())
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null) continue;

                try
                {
                    if (!Wire(contents, path, out string what)) continue;

                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    report.AppendLine($"  {path} — {what}");
                    changed++;
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            AssetDatabase.SaveAssets();
            report.AppendLine(changed == 0
                ? "  nothing to do — every prefab that can ragdoll already does"
                : $"  {changed} prefab(s) updated");

            Debug.Log(report.ToString());
        }

        /// <summary>
        /// One prefab. Which adapter it gets is decided by what the prefab already is: a player body
        /// has a PlayerController, a creature has a driven skeleton (see
        /// <see cref="HasDrivenSkeleton"/>), and a thing with neither — a crate, a bullet, a door —
        /// has nothing to ragdoll and is left alone.
        /// </summary>
        private static bool Wire(GameObject root, string path, out string what)
        {
            what = null;

            if (root.GetComponent<PlayerController>() != null)
            {
                if (root.GetComponent<PlayerRagdoll>() != null) return false;

                // RagdollRig comes with it — [RequireComponent] on the adapter.
                root.AddComponent<PlayerRagdoll>();
                what = "added PlayerRagdoll";
                return true;
            }

            bool qualifies = !IsVehicle(path) && HasDrivenSkeleton(root);
            bool wired = root.GetComponent<AgentRagdoll>() != null;

            // Removal, not just addition. Without it this tool can only ever be wrong in one
            // direction: an earlier, looser rule put AgentRagdoll on four vehicles, and re-running
            // a corrected tool would have left every one of them exactly as it was.
            if (wired && !qualifies)
            {
                Object.DestroyImmediate(root.GetComponent<AgentRagdoll>());

                // The rig second and only if nothing else wants it — [RequireComponent] refuses to
                // let it go while the adapter is still there, which is why the order matters.
                var rig = root.GetComponent<RagdollRig>();
                if (rig != null) Object.DestroyImmediate(rig);

                what = "removed AgentRagdoll (not a body)";
                return true;
            }

            if (!qualifies || wired) return false;

            root.AddComponent<AgentRagdoll>();
            what = "added AgentRagdoll";
            return true;
        }

        /// <summary>
        /// Is this prefab a vehicle rather than a creature?
        ///
        /// <para>
        /// Decided by the folder, and deliberately so: no COMPONENT separates these. The
        /// DuneOrnithopter and the Ostrich carry an almost identical set — AgentController,
        /// MountModule, SteerModule, a motor — because a rideable flying machine and a rideable
        /// bird genuinely are the same kind of thing to everything except a ragdoll. Any component
        /// rule fine enough to split them would be a rule about one prefab wearing a disguise.
        /// </para>
        ///
        /// <para>
        /// The folder, by contrast, is a statement the team has already made. What lives under it —
        /// the crawler, the rig walker, the ornithopter, the ShipRV — are machines people ride and
        /// stand on; the ShipRV is a mobile base with a SpawnPoint and a sandstorm shelter on it,
        /// and the crawler carries passengers on a deck through WalkerPlatformCarrier. A blast is
        /// not supposed to make any of them go limp, empty or not, and the runtime rider check is
        /// no help because it only refuses while somebody is actually aboard.
        /// </para>
        /// </summary>
        private static bool IsVehicle(string path) =>
            path.Replace('\\', '/').Contains("/Prefabs/Agents/Vehicles/",
                                             System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Is there a body here for physics to take, and something driving it that would have to be
        /// told to stop?
        ///
        /// <para>
        /// Three separate signals rather than one, because the prefabs genuinely differ and gating
        /// on any single one misses creatures that the shock wave can plainly hit. The first cut of
        /// this asked only for a <c>HealthComponent</c> and skipped the Ostrich — a rideable mount
        /// that has no health because it cannot die — along with the crab and the humanoid, which
        /// are walked by <c>LeggedLocomotion</c> and a driver with no agent brain at all. All three
        /// have skeletons and all three can be caught by a blast. Dying is only ONE of the two ways
        /// a body ends up on the ground.
        /// </para>
        /// </summary>
        private static bool HasDrivenSkeleton(GameObject root) =>
            root.GetComponentInChildren<AgentController>(true) != null
            || root.GetComponentInChildren<LeggedLocomotion>(true) != null
            || root.GetComponent<HealthComponent>() != null;

        /// <summary>
        /// Diagnostic: what each candidate prefab actually has, so the qualifying rule can be
        /// decided from the prefabs rather than guessed at. Changes nothing.
        /// </summary>
        [MenuItem("Tools/SpaceGame/Ragdoll/Report Candidates")]
        public static void ReportCandidates()
        {
            var report = new StringBuilder("[Ragdoll] candidates:\n");

            foreach (string path in PrefabPaths())
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null) continue;

                try
                {
                    if (!HasDrivenSkeleton(contents) && contents.GetComponent<PlayerController>() == null)
                        continue;

                    int skinned = 0, bonedSkins = 0;
                    foreach (SkinnedMeshRenderer r in contents.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        skinned++;
                        if (r.bones != null && r.bones.Length > 1) bonedSkins++;
                    }

                    report.AppendLine(
                        $"  {System.IO.Path.GetFileNameWithoutExtension(path)}: " +
                        $"skins={skinned} boned={bonedSkins} " +
                        $"agent={contents.GetComponentInChildren<AgentController>(true) != null} " +
                        $"legged={contents.GetComponentInChildren<LeggedLocomotion>(true) != null} " +
                        $"health={contents.GetComponent<HealthComponent>() != null} " +
                        $"animator={contents.GetComponentInChildren<Animator>(true) != null}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            // To a file as well as the console: the unity-mcp bridge reports an empty console often
            // enough that a Debug.Log alone is not a result you can read back.
            System.IO.File.WriteAllText("Temp/ragdoll_candidates.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Prove the wiring actually produces a ragdoll, rather than a component that will find
        /// nothing on the first blast and log a warning nobody is watching for.
        ///
        /// <para>
        /// Worth having as its own step because that failure is invisible from the prefab: the
        /// golem, the crab and the humanoid robot all carried a correctly-wired AgentRagdoll while
        /// having no skinned bones at all for it to build from. The only way to know is to build
        /// one and count what came out.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Ragdoll/Audit Skeletons")]
        public static void AuditSkeletons()
        {
            var report = new StringBuilder("[Ragdoll] skeleton audit:\n");

            foreach (string path in PrefabPaths())
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                if (asset.GetComponent<AgentRagdoll>() == null
                    && asset.GetComponent<PlayerRagdoll>() == null) continue;

                GameObject instance = Object.Instantiate(asset);
                try
                {
                    var rig = instance.GetComponent<RagdollRig>();
                    if (rig == null)
                    {
                        report.AppendLine($"  {Name(path)}: NO RagdollRig");
                        continue;
                    }

                    // The real path, not a private hook: settled means no impulse and no throw, so
                    // this builds the skeleton and leaves the body exactly where it stands.
                    rig.GoLimp(Vector3.zero, settled: true);

                    report.AppendLine(rig.HasSkeleton
                        ? $"  {Name(path)}: {rig.BoneCount} bones, {rig.JointCount} joints " +
                          $"(from {rig.CandidateCount} candidates, measured by {rig.Measure})"
                        : $"  {Name(path)}: NO SKELETON — cannot ragdoll");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }

            System.IO.File.WriteAllText("Temp/ragdoll_audit.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// Everything about one built ragdoll: what each bone weighs and is shaped like, which pairs
        /// INTERPENETRATE without a joint between them, and what is still enabled that could be
        /// writing those bones while physics owns them.
        ///
        /// <para>
        /// Written for a body that jitters rather than falls. Two causes produce exactly that
        /// symptom — a collider overlap the solver has to resolve every tick, and a second component
        /// writing the same transforms — and neither is visible from the prefab or from a bone count.
        /// </para>
        /// </summary>
        [MenuItem("Tools/SpaceGame/Ragdoll/Diagnose Wired Prefabs")]
        public static void DiagnoseWired()
        {
            var report = new StringBuilder();

            foreach (string path in PrefabPaths())
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                if (asset.GetComponent<AgentRagdoll>() == null
                    && asset.GetComponent<PlayerRagdoll>() == null) continue;

                Diagnose(asset, report);
            }

            System.IO.File.WriteAllText("Temp/ragdoll_diagnosis.txt", report.ToString());
            Debug.Log("[Ragdoll] diagnosis written to Temp/ragdoll_diagnosis.txt");
        }

        private static void Diagnose(GameObject asset, StringBuilder report)
        {
            report.AppendLine("[Ragdoll] diagnosis of " + asset.name + ":");
            GameObject instance = Object.Instantiate(asset);

            try
            {
                var rig = instance.GetComponent<RagdollRig>();
                if (rig == null) { report.AppendLine("  no RagdollRig"); return; }

                rig.GoLimp(Vector3.zero, settled: true);
                report.AppendLine("  hips=" + Describe(rig.Hips) + " bones=" + rig.BoneCount
                                  + " joints=" + rig.JointCount + " measure=" + rig.Measure);

                var bodies = new List<Rigidbody>(instance.GetComponentsInChildren<Rigidbody>(true));
                bodies.RemoveAll(b => b.transform == instance.transform);

                report.AppendLine("\n  bones:");
                foreach (Rigidbody b in bodies)
                {
                    var joint = b.GetComponent<CharacterJoint>();
                    string parent = joint != null && joint.connectedBody != null
                        ? Describe(joint.connectedBody.transform) : "-";

                    report.AppendLine("    " + Describe(b.transform).PadRight(34)
                                      + " mass=" + b.mass.ToString("F2").PadLeft(6) + "  "
                                      + Shape(b.GetComponent<Collider>()).PadRight(36)
                                      + " parent=" + parent);
                }

                report.AppendLine("\n  interpenetrating pairs with NO joint between them:");
                int overlaps = 0;
                int unfiltered = 0;
                for (int i = 0; i < bodies.Count; i++)
                for (int j = i + 1; j < bodies.Count; j++)
                {
                    if (Jointed(bodies[i], bodies[j])) continue;

                    Collider a = bodies[i].GetComponent<Collider>();
                    Collider c = bodies[j].GetComponent<Collider>();
                    if (a == null || c == null || !a.enabled || !c.enabled) continue;

                    if (!Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                                                    c, c.transform.position, c.transform.rotation,
                                                    out _, out float depth)) continue;

                    overlaps++;

                    // The number that actually matters. An overlap the solver has been told to
                    // ignore costs nothing; one it has not is a contact it must fight every tick
                    // and can never win, which is what a jittering ragdoll is made of.
                    bool ignored = Physics.GetIgnoreCollision(a, c);
                    if (!ignored) unfiltered++;

                    if (overlaps <= 30)
                        report.AppendLine("    " + Describe(a.transform) + " <-> " + Describe(c.transform)
                                          + "  depth=" + depth.ToString("F3") + " m"
                                          + (ignored ? "  (ignored)" : "  ** NOT FILTERED **"));
                }
                report.AppendLine("    total: " + overlaps + ", unfiltered: " + unfiltered);

                report.AppendLine("\n  enabled behaviours under the rig that are not the ragdoll:");
                foreach (MonoBehaviour mb in instance.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || !mb.enabled) continue;
                    if (mb is RagdollRig || mb is AgentRagdoll || mb is PlayerRagdoll) continue;
                    report.AppendLine("    " + mb.GetType().Name + " on " + Describe(mb.transform));
                }

                foreach (Cloth cloth in instance.GetComponentsInChildren<Cloth>(true))
                    report.AppendLine("    [Cloth] on " + Describe(cloth.transform)
                                      + " enabled=" + cloth.enabled);

                foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
                    report.AppendLine("    [Animator] on " + Describe(animator.transform)
                                      + " enabled=" + animator.enabled);
            }
            finally
            {
                Object.DestroyImmediate(instance);
                report.AppendLine();
            }
        }

        private static bool Jointed(Rigidbody a, Rigidbody b)
        {
            var ja = a.GetComponent<CharacterJoint>();
            var jb = b.GetComponent<CharacterJoint>();

            return (ja != null && ja.connectedBody == b) || (jb != null && jb.connectedBody == a);
        }

        private static string Describe(Transform t) => t == null ? "<null>" : t.name;

        private static string Shape(Collider collider)
        {
            if (collider is CapsuleCollider capsule)
                return "capsule r=" + capsule.radius.ToString("F3")
                       + " h=" + capsule.height.ToString("F3") + " axis=" + capsule.direction;

            if (collider is BoxCollider box)
                return "box " + box.size.x.ToString("F2") + "x" + box.size.y.ToString("F2")
                       + "x" + box.size.z.ToString("F2");

            return collider == null ? "NO COLLIDER" : collider.GetType().Name;
        }

        private static string Name(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

        private static IEnumerable<string> PrefabPaths()
        {
            var paths = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { AgentPrefabRoot }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            paths.Sort();
            return paths;
        }
    }
}
