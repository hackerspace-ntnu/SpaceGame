using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.World.Diagnostics
{
    /// <summary>
    /// Measures whether crossing chunk boundaries is actually unnoticeable.
    ///
    /// Registers a synthetic streaming anchor with the <see cref="WorldStreamer"/> and walks it in
    /// a straight line across several chunk boundaries at a fixed speed, recording every frame's
    /// duration and attributing stalls to the scene event that was in flight at the time. The
    /// anchor rather than the real player is deliberate: it exercises the exact streaming path with
    /// nothing else varying, so two runs are comparable.
    ///
    /// Results are written to a file under <see cref="ResultsPath"/> because the console is not a
    /// reliable transport — a multi-second freeze is exactly the situation where log lines get
    /// coalesced or dropped.
    ///
    /// Armed by <c>World/Streaming/Run Chunk Traversal Probe</c>; never installs itself otherwise,
    /// so it cannot run in a shipped game.
    /// </summary>
    public class ChunkStreamingProbe : MonoBehaviour
    {
        /// <summary>EditorPrefs key the editor menu sets to arm a single run.</summary>
        public const string ArmedKey = "SpaceGame.ChunkStreamingProbe.Armed";

        /// <summary>Where the run writes its report, relative to the project root.</summary>
        public static string ResultsPath => "chunk-streaming-probe.txt";

        // Row y=3 of the grid, comfortably inside the world (worldOrigin.z = -1000, chunk 500 m).
        private const float TraverseZ = 750f;
        private const float StartX = 300f;
        private const float EndX = 3300f;      // crosses the x=1..6 boundaries: 6 crossings
        private const float Speed = 40f;       // m/s — vehicle pace
        private const float WarmupSeconds = 4f;
        private const float StallThresholdMs = 100f;
        private const float TimeoutSeconds = 300f;

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!UnityEditor.EditorPrefs.GetBool(ArmedKey, false)) return;
            UnityEditor.EditorPrefs.SetBool(ArmedKey, false);

            var go = new GameObject("[ChunkStreamingProbe]");
            go.AddComponent<ChunkStreamingProbe>();
            DontDestroyOnLoad(go);
        }
#endif

        // Editor pauses are not game stalls, and they are indistinguishable from one in
        // unscaledDeltaTime — a run that got error-paused for five minutes reported a single
        // 320-second "stall" and a passing frame count of one. The pause has to be observed
        // directly, and the frame that resumes it discarded, or the whole measurement is fiction.
        private bool discardNextFrame;
        private int discardedFrames;
        private int pauses;

        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged += OnPauseStateChanged;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= OnPauseStateChanged;
#endif
        }

#if UNITY_EDITOR
        private void OnPauseStateChanged(UnityEditor.PauseState state)
        {
            if (state != UnityEditor.PauseState.Paused) return;

            pauses++;
            discardNextFrame = true;

            // An unattended run must not sit paused forever because an unrelated system logged an
            // exception and Error Pause is on.
            UnityEditor.EditorApplication.isPaused = false;
        }
#endif

        private WorldStreamer streamer;
        private Transform anchor;
        private float warmupUntil;
        private float startedAt;
        private float gameSeconds;
        private bool running;
        private bool finished;

        private readonly List<float> frameMs = new();
        private readonly List<string> stalls = new();
        private readonly List<string> sceneEvents = new();
        private string lastEvent = "none";
        private float lastEventTime;

        private void Start()
        {
            streamer = FindFirstObjectByType<WorldStreamer>();
            if (streamer == null)
            {
                Finish("no WorldStreamer in the loaded scenes");
                return;
            }

            var a = new GameObject("[ProbeAnchor]");
            a.transform.position = new Vector3(StartX, 0f, TraverseZ);
            anchor = a.transform;
            DontDestroyOnLoad(a);

            SceneManager.sceneLoaded += OnLoaded;
            SceneManager.sceneUnloaded += OnUnloaded;

            warmupUntil = Time.realtimeSinceStartup + WarmupSeconds;
            Debug.Log("[ChunkStreamingProbe] armed; warming up");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneUnloaded -= OnUnloaded;
        }

        private void OnLoaded(Scene s, LoadSceneMode mode) => NoteEvent("LOAD", s.name);
        private void OnUnloaded(Scene s) => NoteEvent("UNLOAD", s.name);

        private void NoteEvent(string kind, string sceneName)
        {
            lastEvent = $"{kind} {sceneName}";
            lastEventTime = Time.realtimeSinceStartup;
            if (running)
                sceneEvents.Add($"t+{lastEventTime - startedAt:0.00}s x={anchor.position.x:0} {lastEvent}");
        }

        private void Update()
        {
            if (finished) return;

            float now = Time.realtimeSinceStartup;

            if (!running)
            {
                // Let the initial chunk set settle so start-up cost is not charged to the traverse.
                if (now < warmupUntil) return;
                running = true;
                startedAt = now;
                streamer.RegisterTrackedTransform(anchor);
                Debug.Log($"[ChunkStreamingProbe] traverse {StartX:0} -> {EndX:0} m at {Speed} m/s");
                return;
            }

            float ms = Time.unscaledDeltaTime * 1000f;

            if (discardNextFrame)
            {
                discardNextFrame = false;
                discardedFrames++;
                gameSeconds += 1f / 60f;   // nominal, so a pause cannot trip the timeout
                return;
            }

            frameMs.Add(ms);
            gameSeconds += Time.unscaledDeltaTime;

            if (ms > StallThresholdMs)
            {
                stalls.Add($"t+{gameSeconds:0.00}s x={anchor.position.x:0} {ms:0}ms " +
                           $"after [{lastEvent}] +{now - lastEventTime:0.00}s");
            }

            // unscaledDeltaTime, not a fixed step: the anchor must advance in real time so the
            // streamer sees a realistic crossing rate even while frames are long.
            var p = anchor.position;
            p.x += Speed * Time.unscaledDeltaTime;
            anchor.position = p;

            // Timeout on measured game time, not wall clock: wall clock includes pauses, and a
            // paused run would abort as if the streaming had hung.
            if (p.x >= EndX) Finish(null);
            else if (gameSeconds > TimeoutSeconds) Finish($"timed out at x={p.x:0}");
        }

        private void Finish(string error)
        {
            finished = true;
            var report = BuildReport(error);
            Debug.Log(report);

            try
            {
                System.IO.File.WriteAllText(ResultsPath, report);
                Debug.Log($"[ChunkStreamingProbe] wrote {System.IO.Path.GetFullPath(ResultsPath)}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ChunkStreamingProbe] could not write results: {e.Message}");
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private string BuildReport(string error)
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== chunk streaming probe =====");

            if (error != null) sb.AppendLine($"ERROR: {error}");

            if (frameMs.Count == 0)
            {
                sb.AppendLine("no frames recorded");
                return sb.ToString();
            }

            var sorted = new List<float>(frameMs);
            sorted.Sort();
            int n = sorted.Count;
            float total = 0f;
            int over16 = 0, over33 = 0, over100 = 0, over500 = 0, over1000 = 0;
            foreach (var f in sorted)
            {
                total += f;
                if (f > 16.7f) over16++;
                if (f > 33f) over33++;
                if (f > 100f) over100++;
                if (f > 500f) over500++;
                if (f > 1000f) over1000++;
            }

            float travelled = anchor != null ? anchor.position.x - StartX : 0f;
            sb.AppendLine($"frames={n}  measured={total / 1000f:0.0}s  speed={Speed} m/s  " +
                          $"distance={travelled:0} m");

            if (pauses > 0)
                sb.AppendLine($"NOTE: editor paused {pauses}x during the run; " +
                              $"{discardedFrames} resume frame(s) excluded as not game stalls.");
            sb.AppendLine($"median={sorted[n / 2]:0.0}ms  p95={sorted[Pct(n, 0.95f)]:0.0}ms  " +
                          $"p99={sorted[Pct(n, 0.99f)]:0.0}ms  max={sorted[n - 1]:0}ms");
            sb.AppendLine($">16.7ms={over16}  >33ms={over33}  >100ms={over100}  " +
                          $">500ms={over500}  >1000ms={over1000}");
            sb.AppendLine($"VERDICT: {(sorted[n - 1] <= 33f ? "PASS" : "FAIL")} " +
                          $"(target: max <= 33ms, p99 <= 20ms)");

            sb.AppendLine();
            sb.AppendLine($"stalls over {StallThresholdMs:0}ms ({stalls.Count}):");
            foreach (var s in stalls) sb.AppendLine("  " + s);

            sb.AppendLine();
            sb.AppendLine($"scene events ({sceneEvents.Count}):");
            foreach (var e in sceneEvents) sb.AppendLine("  " + e);

            sb.AppendLine();
            sb.AppendLine(streamer != null ? streamer.DebugDumpState() : "streamer gone");
            return sb.ToString();
        }

        private static int Pct(int count, float p) => Mathf.Clamp((int)(count * p), 0, count - 1);
    }
}
