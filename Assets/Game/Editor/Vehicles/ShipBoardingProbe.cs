// TEMPORARY DIAGNOSTIC — delete once the boarding question is settled.
//
// Casts the Interactor's own ray at the PlayerShip's four boarding volumes from points all around
// them and reports what answers, so "pressing E on the ship mounts me" can be traced to a ray path
// rather than guessed at.
//
// Runs off EditorApplication.update after a domain reload when Temp/probe_request exists, because
// a menu item invoked from outside the editor is queued on the interactive tick and never runs
// while the Unity window is in the background (see HeadlessTestRunner for the same problem).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Vehicles;

namespace SpaceGame.EditorTools
{
    public static class ShipBoardingProbe
    {
        private const string PrefabPath =
            "Assets/Game/Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab";
        private const string RequestPath = "Temp/probe_request";
        private const string ResultPath = "Temp/ship_boarding_probe.txt";

        // Interactor._castDistance, and the radius the samples sit at so every ray is inside it.
        private const float CastDistance = 20f;
        private const float SampleRadius = 4f;
        private const int Samples = 256;

        private static StringBuilder log;

        [MenuItem("Tools/Vehicles/Probe PlayerShip Boarding")]
        public static void Request()
        {
            File.WriteAllText(RequestPath, "go");
            Arm();
        }

        [InitializeOnLoadMethod]
        private static void Arm()
        {
            if (!File.Exists(RequestPath) && !File.Exists("Temp/build_request")
                && !File.Exists("Temp/test_request")) return;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        // Editor ticks to let pass after a reload before acting. A script edit compiles in waves and
        // isCompiling dips between them; a run started in that dip is thrown away by the next
        // reload, and a run that never finished looks exactly like one still going.
        private static int settle = 120;

        private static void Pump()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (settle-- > 0) return;
            EditorApplication.update -= Pump;

            if (File.Exists("Temp/build_request"))
            {
                File.Delete("Temp/build_request");
                PlayerShipBuilder.Build();
                return;
            }

            if (File.Exists("Temp/test_request"))
            {
                // Left in place until RunFinished: a reload that kills the run leaves the request
                // armed so the next one picks it up, instead of leaving silence behind.
                RunTests(File.ReadAllText("Temp/test_request").Trim());
                return;
            }

            if (!File.Exists(RequestPath)) return;
            File.Delete(RequestPath);
            Probe();
        }

        // A results path of this probe's own: the shared HeadlessTestRunner file is being rewritten
        // by whoever else is running tests in this editor, and a run you cannot attribute proves
        // nothing.
        private const string TestResultPath = "Temp/ship_tests.txt";
        private static TestRunnerApi api;
        private static Listener listener;

        private static void RunTests(string group)
        {
            if (File.Exists(TestResultPath)) File.Delete(TestResultPath);

            var filter = new Filter { testMode = TestMode.EditMode };
            if (!string.IsNullOrEmpty(group)) filter.groupNames = new[] { group };

            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            listener = new Listener();
            api.RegisterCallbacks(listener);
            api.Execute(new ExecutionSettings(filter));
        }

        private class Listener : ICallbacks
        {
            public void RunStarted(ITestAdaptor test) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"PASSED={result.PassCount} FAILED={result.FailCount}");
                Walk(result, sb);
                sb.AppendLine("DONE");
                File.WriteAllText(TestResultPath, sb.ToString());
                if (File.Exists("Temp/test_request")) File.Delete("Temp/test_request");
            }

            private static void Walk(ITestResultAdaptor result, StringBuilder sb)
            {
                if (!result.HasChildren)
                {
                    if (result.TestStatus == TestStatus.Failed)
                        sb.AppendLine($"Failed: {result.FullName}\n      {result.Message}");
                    return;
                }
                foreach (ITestResultAdaptor child in result.Children)
                    Walk(child, sb);
            }
        }

        private static void Probe()
        {
            log = new StringBuilder();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                File.WriteAllText(ResultPath, "no prefab\nDONE\n");
                return;
            }

            GameObject ship = Object.Instantiate(prefab);
            ship.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            try
            {
                Run(ship);
            }
            catch (System.Exception e)
            {
                log.AppendLine("THREW: " + e);
            }
            finally
            {
                Object.DestroyImmediate(ship);
                log.AppendLine("DONE");
                File.WriteAllText(ResultPath, log.ToString());
            }
        }

        private static void Run(GameObject ship)
        {
            Transform canopyTransform = ship.transform.Find("Model/Mesh_CanopyDome");
            Bounds canopy = canopyTransform != null && canopyTransform.TryGetComponent(out Renderer canopyRenderer)
                ? canopyRenderer.bounds
                : new Bounds(Vector3.zero, Vector3.zero);

            Bounds hull = default;
            bool first = true;
            foreach (Renderer renderer in ship.GetComponentsInChildren<Renderer>(true))
            {
                if (first) { hull = renderer.bounds; first = false; }
                else hull.Encapsulate(renderer.bounds);
            }

            log.AppendLine($"hull   {hull.min:F2} .. {hull.max:F2}");
            log.AppendLine($"canopy {canopy.min:F2} .. {canopy.max:F2}");

            Collider[] boarding = ship.GetComponentsInChildren<Collider>(true)
                .Where(c => c.isTrigger && c.GetComponent<IInteractable>() != null)
                .ToArray();

            log.AppendLine($"{boarding.Length} boarding volumes: " +
                           string.Join(", ", boarding.Select(b => b.name)));

            foreach (Collider volume in boarding)
                ProbeVolume(ship, volume, canopy);
        }

        // The exact eyes PlayerShip_NoChairIsBoardedThroughTheCanopy still reports, so what the
        // ray crosses on the way in can be read rather than guessed at.
        private static readonly Vector3[] Offenders =
        {
            new Vector3(0.4f, 8.0f, 4.6f), new Vector3(-1.3f, 7.9f, 4.4f),
            new Vector3(1.4f, 7.8f, 5.0f), new Vector3(-0.2f, 7.6f, 4.2f),
            new Vector3(-2.1f, 7.5f, 4.3f), new Vector3(2.8f, 6.9f, 6.1f),
        };

        private static void ProbeVolume(GameObject ship, Collider volume, Bounds canopy)
        {
            Vector3 centre = volume.bounds.center;
            log.AppendLine($"{volume.name} centre {centre:F2} bounds {volume.bounds.min:F2}..{volume.bounds.max:F2}");
            if (volume.name != "HelmSeat") return;

            Transform blocker = ship.transform.Find("Model/Mesh_CanopyDome/CanopyBlocker");
            Collider pane = blocker != null ? blocker.GetComponent<Collider>() : null;
            if (pane != null)
                log.AppendLine($"  blocker world bounds {pane.bounds.min:F2} .. {pane.bounds.max:F2}");
            else
                log.AppendLine("  NO BLOCKER ON THE PREFAB");

            foreach (Vector3 eye in Offenders)
            {
                Vector3 aim = (centre - eye).normalized;
                bool inside = pane != null && pane.bounds.Contains(eye);
                log.AppendLine($"  eye {eye:F2} (in blocker bounds: {inside})");
                log.AppendLine($"    {Describe(eye, aim, ship)}");
            }
        }

        /// <summary>The ordered hit list along a ray, so what is (or is not) in the way is visible.</summary>
        private static string Describe(Vector3 origin, Vector3 direction, GameObject ship)
        {
            int count = Cast(origin, direction, 256, out RaycastHit[] hits);
            return string.Join(" | ", hits.Take(count)
                .Where(h => h.collider != null && h.collider.transform.IsChildOf(ship.transform))
                .OrderBy(h => h.distance)
                .Select(h => $"{h.collider.name}{(h.collider.isTrigger ? "(trigger)" : "")}@{h.distance:F2}"));
        }

        private static int Cast(Vector3 origin, Vector3 direction, int bufferSize,
                                out RaycastHit[] hits)
        {
            hits = new RaycastHit[bufferSize];
            return Physics.RaycastNonAlloc(new Ray(origin, direction), hits, CastDistance,
                                           ~LayerMask.GetMask("Player"));
        }

        private static Vector3 FibonacciDirection(int index, int total)
        {
            float y = 1f - index / (float)(total - 1) * 2f;
            float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            float theta = Mathf.PI * (3f - Mathf.Sqrt(5f)) * index;
            return new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius);
        }
    }
}

// probe rev 2

// rev 3

// rev 4
