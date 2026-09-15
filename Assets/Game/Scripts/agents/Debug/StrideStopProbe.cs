// TEMPORARY DIAGNOSTIC. Delete this file (and the read-only properties on
// AgentAnimatorDriver and ConjurerCastModule that feed it) once the hanging foot is
// understood.
//
// It answers one question: at the moment the walk hands over to the standing pose, where
// is everything? The console cannot answer it -- the interesting window is two or three
// seconds long, sixty lines a second, and Unity's console throws most of that away -- so
// this writes a CSV to the project root instead, one line per frame, and only while
// something is actually happening.
//
// It attaches itself. Nothing in the prefab, the scene or the builder refers to it: a
// bootstrap hook drops one on every ConjurerCastModule it finds, including creatures
// spawned later, so removing the file removes the whole diagnostic and nothing else has to
// be unwound. That matters here because this prefab's save id lives in the prefab asset --
// re-saving it to add a component would need Tools > Save System > Wire Saveable Prefabs
// afterwards, which is a real change to be making for a debug session.
//
// LateUpdate, deliberately: the Animator has evaluated by then, so the bone positions this
// samples are the pose that was actually drawn this frame rather than the previous one's.
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace SpaceGame.Agents.Diagnostics
{
    public static class StrideStopProbeBootstrap
    {
        private const float ScanInterval = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Left visible in the hierarchy on purpose: "is the probe running at all" is
            // the first question to ask of a session that produced no file.
            var host = new GameObject("~StrideStopProbe");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<Scanner>();
        }

        private sealed class Scanner : MonoBehaviour
        {
            private float nextScan;

            private void Update()
            {
                // F9 stamps the log. A stop that looked wrong and a stop that looked fine
                // produce very similar numbers, and being told WHICH ONE to read is worth
                // more than any amount of inference over the whole session.
                if (Keyboard.current != null && Keyboard.current.f9Key.wasPressedThisFrame)
                    StrideStopProbe.Mark();

                if (Time.unscaledTime < nextScan) return;
                nextScan = Time.unscaledTime + ScanInterval;

                ConjurerCastModule[] casters =
                    FindObjectsByType<ConjurerCastModule>(FindObjectsInactive.Include,
                                                          FindObjectsSortMode.None);

                foreach (ConjurerCastModule caster in casters)
                {
                    if (caster.GetComponent<StrideStopProbe>() == null)
                        caster.gameObject.AddComponent<StrideStopProbe>();
                }
            }
        }
    }

    public sealed class StrideStopProbe : MonoBehaviour
    {
        // Everything past a quiet frame, so a stop's tail is captured rather than cut off at
        // the moment the numbers stop moving -- which is exactly where the fault is.
        private const float TailSeconds = 3f;

        private const float MovingSpeed = 0.05f;

        // A session that is left running should not fill the disk.
        private const int MaxLines = 400000;

        private static StreamWriter writer;
        private static int lines;

        private Animator animator;
        private NavMeshAgent agent;
        private AgentAnimatorDriver driver;
        private ConjurerCastModule caster;
        private Transform footL;
        private Transform footR;

        private readonly Dictionary<int, string> stateNames = new();

        private int lastStateHash;
        private bool lastInTransition;
        private bool lastHolding;
        private bool lastStopped;
        private string lastPhase = "";
        private float lastInterestingTime = -999f;

        private readonly StringBuilder line = new();

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>(true);
            agent = GetComponent<NavMeshAgent>();
            driver = GetComponentInChildren<AgentAnimatorDriver>(true);
            caster = GetComponent<ConjurerCastModule>();

            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (footL == null && t.name == "Foot_L") footL = t;
                if (footR == null && t.name == "Foot_R") footR = t;
            }

            foreach (string n in new[] { "Idle", "Walk", "Attack", "Sleep", "Awakening" })
                stateNames[Animator.StringToHash(n)] = n;

            OpenWriter();
            WriteConfigHeader();
        }

        private static void OpenWriter()
        {
            if (writer != null) return;

            // Project root, and a .log extension because the repo's .gitignore already
            // covers *.log -- a debug artefact should not need a second decision about
            // whether it is committed.
            string path = Path.Combine(Application.dataPath, "..", "ConjurerStop.log");

            writer = new StreamWriter(path, append: false) { AutoFlush = true };

            // Statics survive a play session when domain reload is off, and a line budget
            // carried over from the last run would silently truncate this one.
            lines = 0;

            writer.WriteLine("# stride stop probe, session started " +
                             System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteLine("t,frame,who,event,speed,stopped,immobile,speedX,speedY," +
                             "state,normTime,inTrans,transTo,transPct,hold,endKnown," +
                             "strideEnd,heldX,phase,settleT,posX,posZ,footLdY,footRdY");
        }

        private void WriteConfigHeader()
        {
            float[] phases = driver != null ? driver.StrideEndPhases : null;
            string list = phases == null || phases.Length == 0
                ? "NONE"
                : string.Join(" ", phases);

            writer.WriteLine($"# {name}: strideEndPhases=[{list}] " +
                             $"animatorSpeed={(animator != null ? animator.speed : -1f)} " +
                             $"footL={(footL != null ? "ok" : "MISSING")} " +
                             $"footR={(footR != null ? "ok" : "MISSING")} " +
                             $"agent={(agent != null ? "ok" : "MISSING")} " +
                             $"driver={(driver != null ? "ok" : "MISSING")} " +
                             $"caster={(caster != null ? "ok" : "MISSING")}");

            // One console line per creature, so a session that produced no file at all is
            // distinguishable from one where the probe never attached.
            Debug.Log($"[StrideProbe] watching {name}, phases=[{list}]", this);
        }

        private void LateUpdate()
        {
            if (writer == null || lines >= MaxLines) return;
            if (animator == null || animator.runtimeAnimatorController == null) return;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool inTransition = animator.IsInTransition(0);

            float speed = agent != null ? agent.velocity.magnitude : 0f;
            bool stopped = agent != null && agent.isOnNavMesh && agent.isStopped;
            bool immobile = agent == null || !agent.isOnNavMesh || agent.isStopped;
            bool holding = driver != null && driver.IsHoldingStride;
            string phase = caster != null ? caster.Phase : "-";

            string ev = Events(state.shortNameHash, inTransition, holding, stopped, phase);

            bool interesting = speed > MovingSpeed || holding || inTransition ||
                               phase != "idle" || ev.Length > 0;

            if (interesting) lastInterestingTime = Time.time;
            else if (Time.time - lastInterestingTime > TailSeconds) return;

            string transTo = "-";
            float transPct = -1f;

            if (inTransition)
            {
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
                transTo = StateName(next.shortNameHash);
                transPct = animator.GetAnimatorTransitionInfo(0).normalizedTime;
            }

            float rootY = transform.position.y;

            line.Clear();
            Add(Time.time);
            Add(Time.frameCount);
            line.Append(name).Append(',');
            line.Append(ev).Append(',');
            Add(speed);
            Add(stopped ? 1 : 0);
            Add(immobile ? 1 : 0);
            Add(animator.GetFloat("SpeedX"));
            Add(animator.GetFloat("SpeedY"));
            line.Append(StateName(state.shortNameHash)).Append(',');
            Add(state.normalizedTime);
            Add(inTransition ? 1 : 0);
            line.Append(transTo).Append(',');
            Add(transPct);
            Add(holding ? 1 : 0);
            Add(driver != null && driver.StrideEndKnown ? 1 : 0);
            Add(driver != null ? driver.StrideEndTime : -1f);
            Add(driver != null ? driver.HeldCadence.x : 0f);
            line.Append(phase).Append(',');
            Add(caster != null ? caster.SettleElapsed : -1f);
            Add(transform.position.x);
            Add(transform.position.z);
            Add(footL != null ? footL.position.y - rootY : -999f);
            Add(footR != null ? footR.position.y - rootY : -999f, last: true);

            writer.WriteLine(line.ToString());
            lines++;
        }

        /// What changed since the last frame, as a "|"-joined list. Empty on a frame where
        /// nothing did, which is also what keeps a quiet session out of the file.
        private string Events(int stateHash, bool inTransition, bool holding, bool stopped,
                              string phase)
        {
            string ev = "";

            if (stateHash != lastStateHash)
            {
                ev = Join(ev, "state>" + StateName(stateHash));
                lastStateHash = stateHash;
            }

            if (inTransition != lastInTransition)
            {
                ev = Join(ev, inTransition ? "trans+" : "trans-");
                lastInTransition = inTransition;
            }

            if (holding != lastHolding)
            {
                ev = Join(ev, holding ? "HOLD+" : "HOLD-");
                lastHolding = holding;
            }

            if (stopped != lastStopped)
            {
                ev = Join(ev, stopped ? "stop+" : "stop-");
                lastStopped = stopped;
            }

            if (phase != lastPhase)
            {
                ev = Join(ev, "phase>" + phase);
                lastPhase = phase;
            }

            return ev;
        }

        private static string Join(string a, string b) => a.Length == 0 ? b : a + "|" + b;

        private string StateName(int hash) =>
            stateNames.TryGetValue(hash, out string n) ? n : hash.ToString();

        // InvariantCulture throughout: this machine writes decimal commas, and a decimal
        // comma in a comma-separated file is not a small problem to notice later.
        private void Add(float v, bool last = false)
        {
            line.Append(v.ToString("0.####", CultureInfo.InvariantCulture));
            if (!last) line.Append(',');
        }

        private void Add(int v) => line.Append(v).Append(',');

        /// Stamp the log where the player says something looked wrong.
        public static void Mark()
        {
            if (writer == null)
            {
                Debug.LogWarning("[StrideProbe] MARK ignored - no conjurer is being watched " +
                                 "yet, so no log is open.");
                return;
            }

            writer.WriteLine($"# MARK t={Time.time:0.###} frame={Time.frameCount}");
            Debug.Log($"[StrideProbe] MARK at t={Time.time:0.###}");
        }

        private void OnApplicationQuit() => Close();

        private void OnDestroy() => Close();

        private static void Close()
        {
            if (writer == null) return;

            writer.WriteLine("# closed, " + lines + " sampled lines");
            writer.Flush();
            writer.Dispose();
            writer = null;
        }
    }
}
