using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Spreads a freshly loaded chunk's expensive object construction across frames.
    ///
    /// A chunk scene's <c>Awake</c> calls all run in the same frame Unity finishes integrating the
    /// scene. For a content-heavy chunk that means 34 terrain features each building a GameObject
    /// tree and a MeshCollider — and cooking a collider is synchronous PhysX work on the main
    /// thread, measured at 110 ms for one chunk's worth. No amount of async loading helps, because
    /// the cost is not the loading; it is what the loaded objects do when they wake up.
    ///
    /// So work that can wait defers to here, and the streamer drains it under a millisecond budget.
    /// A chunk becomes complete a few frames late instead of costing one enormous frame.
    ///
    /// Deliberately not a MonoBehaviour: the queue is pure logic with no Unity lifecycle, which is
    /// what makes its budget behaviour testable. <see cref="Shared"/> exists because chunk-scene
    /// components need to reach it from <c>Awake</c> without a scene-wide search — the same reason
    /// <c>WorldStreamer</c> keeps a static registry for <c>SceneTracked</c>.
    /// </summary>
    public sealed class ChunkActivationQueue
    {
        /// <summary>The one queue. Static so chunk-scene components can enqueue from Awake.</summary>
        public static readonly ChunkActivationQueue Shared = new();

        /// <summary>
        /// Statics survive a Play-mode exit when Enter Play Mode Options has domain reload turned
        /// off — a common speed setting. Without this the next session starts holding spawn tasks
        /// that close over objects destroyed by the previous one.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Shared.Clear();

        /// <summary>
        /// Per-frame budget the runner spends. The streamer writes it from its inspector field; the
        /// default stands on its own so a scene with no streamer still drains at a sane rate.
        /// </summary>
        public float budgetMs = 2f;

        private readonly Queue<Task> tasks = new();

        private readonly struct Task
        {
            public readonly Action Work;
            public readonly string Label;

            public Task(Action work, string label)
            {
                Work = work;
                Label = label;
            }
        }

        /// <summary>Tasks still waiting. Zero means the last-loaded chunk is fully built.</summary>
        public int PendingCount => tasks.Count;

        /// <summary>Total tasks completed since the last <see cref="Clear"/>. For diagnostics.</summary>
        public int CompletedCount { get; private set; }

        /// <summary>
        /// Queue deferrable work. <paramref name="label"/> names it in the log if it throws — an
        /// exception here would otherwise be a chunk that is quietly missing its geometry.
        /// </summary>
        public void Enqueue(Action work, string label)
        {
            if (work == null) return;
            tasks.Enqueue(new Task(work, label));

            if (this == Shared) ChunkActivationRunner.Ensure();
        }

        /// <summary>
        /// Run queued work until the budget is spent. Returns how many tasks ran.
        ///
        /// The budget is checked AFTER each task, not before: a single task that overruns cannot be
        /// split, and checking first would let a queue of expensive tasks make no progress at all
        /// on a frame that is already slightly late. One task per frame minimum guarantees drainage.
        /// </summary>
        public int Tick(float budgetMs)
        {
            if (tasks.Count == 0) return 0;

            float start = Time.realtimeSinceStartup;
            float budgetSeconds = Mathf.Max(0f, budgetMs) * 0.001f;
            int ran = 0;

            while (tasks.Count > 0)
            {
                var task = tasks.Dequeue();

                try
                {
                    task.Work();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ChunkActivationQueue] '{task.Label}' threw and was skipped: {e}");
                }

                ran++;
                CompletedCount++;

                if (Time.realtimeSinceStartup - start >= budgetSeconds) break;
            }

            return ran;
        }

        /// <summary>Drop everything still queued. Used when streaming shuts down.</summary>
        public void Clear()
        {
            tasks.Clear();
            CompletedCount = 0;
        }
    }

    /// <summary>
    /// Drains <see cref="ChunkActivationQueue.Shared"/> once per frame.
    ///
    /// The queue owns its own draining rather than relying on the <see cref="WorldStreamer"/> to
    /// tick it, because terrain features also live in scenes that have no streamer at all — the
    /// per-developer test scenes, the interiors. Making the streamer the drainer would mean features
    /// in those scenes silently never appear, which is a far worse failure than the stall this
    /// whole queue exists to avoid.
    /// </summary>
    internal sealed class ChunkActivationRunner : MonoBehaviour
    {
        private static ChunkActivationRunner instance;

        internal static void Ensure()
        {
            if (instance != null) return;

            var go = new GameObject("[ChunkActivation]") { hideFlags = HideFlags.HideAndDontSave };
            instance = go.AddComponent<ChunkActivationRunner>();
            DontDestroyOnLoad(go);
        }

        private void Update() => ChunkActivationQueue.Shared.Tick(ChunkActivationQueue.Shared.budgetMs);

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
