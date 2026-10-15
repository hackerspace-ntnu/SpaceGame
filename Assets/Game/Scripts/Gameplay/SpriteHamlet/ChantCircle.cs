using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Gameplay.SpriteHamlet
{
    /// <summary>
    /// Three Oathkeepers at three stones, chanting the old oath one verse at a time — on the Oath
    /// Stones around the witch's hut, and around Merob once he has been brought down.
    ///
    /// <para>
    /// <b>The chant only advances while every keeper is at their stone and on their feet.</b> A keeper
    /// knocked down (<c>StunOnHitModule</c>) stops it, which is the whole job of protecting them. Each
    /// finished verse raises <see cref="VerseCompletedOnServer"/> — what drops the witch's ward — and
    /// the chant rests for <see cref="restAfterVerse"/> before the next. A circle that does not
    /// <see cref="loop"/> finishes after <see cref="verses"/> verses and raises
    /// <see cref="QuestMark.Chanted"/>; one that loops (the witch's) keeps chanting while it runs.
    /// </para>
    /// <para>
    /// <b>When it runs is the quest's, not its own.</b> It runs while its chain is inside
    /// <see cref="runsDuring"/>, decided from replicated progress on every machine, so the keepers
    /// walk to their stones everywhere at once with nothing extra on the wire. Only the chant's
    /// progress is replicated, for the presentation. It is NOT saved: a reload mid-chant starts the
    /// verse again.
    /// </para>
    /// <para>
    /// Authored on the site's root with an in-scene <c>NetworkObject</c>. Without one it still runs
    /// on its own machine, which is offline play.
    /// </para>
    /// </summary>
    public class ChantCircle : NetworkBehaviour
    {
        private static readonly Dictionary<string, ChantCircle> Live = new();

        [Tooltip("Named by a ChantStep. Unique, readable.")]
        [SerializeField] private string circleId;

        [Tooltip("The stretch of the quest during which the keepers chant here.")]
        [SerializeField] private QuestStepWindow runsDuring;

        [Header("Keepers")]
        [Tooltip("QuestActor ids of the keepers, one per stone, in the order of the stones.")]
        [SerializeField] private string[] keeperIds = Array.Empty<string>();

        [Tooltip("Where each keeper stands, relative to the centre, metres. One per keeper.")]
        [SerializeField] private Vector3[] stoneOffsets = Array.Empty<Vector3>();

        [Tooltip("Optional QuestActor id the circle closes around, wherever it stands — Merob, " +
                 "brought down wherever the fight ended. Empty: this object is the centre.")]
        [SerializeField] private string centreActorId;

        [Tooltip("How far from its stone a keeper may stand and still count as there, metres.")]
        [SerializeField, Min(0.1f)] private float stoneTolerance = 1.2f;

        [Header("Chant")]
        [Tooltip("Seconds one verse takes while every keeper is chanting.")]
        [SerializeField, Min(1f)] private float verseSeconds = 12f;

        [Tooltip("How many verses the oath has.")]
        [SerializeField, Min(1)] private int verses = 3;

        [Tooltip("Seconds of rest after each verse before the next begins.")]
        [SerializeField, Min(0f)] private float restAfterVerse = 8f;

        [Tooltip("Start again after the last verse instead of finishing — for a fight that lasts as " +
                 "long as it lasts.")]
        [SerializeField] private bool loop;

        private readonly NetworkVariable<float> replicatedProgress = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private float progress;
        private float restUntil;
        private bool finished;

        /// <summary>SERVER: a verse just finished. The argument is how many have finished this cycle.</summary>
        public event Action<int> VerseCompletedOnServer;

        public string CircleId => circleId;
        public int Verses => verses;

        /// <summary>Verses finished plus the share of the current one, 0 … <see cref="Verses"/>. Every machine.</summary>
        public float Progress => IsSpawned ? replicatedProgress.Value : progress;

        /// <summary>The quest says the keepers chant here now. Every machine.</summary>
        public bool IsRunning => runsDuring.Contains(runsDuring.FindRun());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        public static bool TryFind(string id, out ChantCircle circle)
        {
            circle = null;
            return !string.IsNullOrEmpty(id) && Live.TryGetValue(id, out circle) && circle != null;
        }

        /// <summary>The running circle that has a stone for the keeper <paramref name="keeperId"/>, if any.</summary>
        public static bool TryFindRunningFor(string keeperId, out ChantCircle circle)
        {
            foreach (ChantCircle candidate in Live.Values)
            {
                if (candidate == null || !candidate.IsRunning) continue;
                if (Array.IndexOf(candidate.keeperIds, keeperId) < 0) continue;

                circle = candidate;
                return true;
            }

            circle = null;
            return false;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(circleId))
                Debug.LogError($"[Quest] {name} has a ChantCircle with no id.", this);
            else
                Live[circleId] = this;

            if (stoneOffsets.Length != keeperIds.Length)
                Debug.LogError($"[Quest] Chant circle '{circleId}' has {keeperIds.Length} keepers but " +
                               $"{stoneOffsets.Length} stones.", this);
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(circleId) && Live.TryGetValue(circleId, out ChantCircle registered) && registered == this)
                Live.Remove(circleId);
        }

        /// <summary>The middle of the circle: the centre actor while it is loaded, else this object.</summary>
        public Vector3 Centre =>
            QuestActor.TryFind(centreActorId, out QuestActor centre) ? centre.transform.position : transform.position;

        /// <summary>Where the keeper <paramref name="keeperId"/> stands to chant.</summary>
        public bool TryGetStone(string keeperId, out Vector3 stone)
        {
            int index = Array.IndexOf(keeperIds, keeperId);
            if (index < 0 || index >= stoneOffsets.Length)
            {
                stone = default;
                return false;
            }

            stone = Centre + transform.rotation * stoneOffsets[index];
            return true;
        }

        private void Update()
        {
            if (!Network.Decides) return;

            if (!IsRunning)
            {
                // Out of its stretch of the quest: the next time it runs, it runs from the start.
                if (progress != 0f || finished) SetProgress(0f);
                finished = false;
                return;
            }

            if (finished || Time.time < restUntil || !EveryKeeperChanting()) return;

            int before = Mathf.FloorToInt(progress);
            float next = progress + Time.deltaTime / verseSeconds;
            int after = Mathf.FloorToInt(next);

            if (after == before)
            {
                SetProgress(next);
                return;
            }

            SetProgress(after);
            restUntil = Time.time + restAfterVerse;
            VerseCompletedOnServer?.Invoke(after);

            if (after < verses) return;

            if (loop)
            {
                SetProgress(0f);
                return;
            }

            finished = true;
            QuestMarks.Raise(QuestMark.Chanted, circleId);
        }

        private bool EveryKeeperChanting()
        {
            foreach (string id in keeperIds)
            {
                if (!QuestActor.TryFind(id, out QuestActor keeper)) return false;
                if (!TryGetStone(id, out Vector3 stone)) return false;
                if (!QuestPlaces.IsInside(keeper.transform.position, stone, stoneTolerance)) return false;
                if (keeper.TryGetComponent(out IChantInterruptible body) && body.IsInterrupted) return false;
            }

            return true;
        }

        private void SetProgress(float value)
        {
            progress = value;
            if (IsSpawned) replicatedProgress.Value = value;
        }
    }

    /// <summary>Something that stops a keeper chanting for a while — a stun.</summary>
    public interface IChantInterruptible
    {
        bool IsInterrupted { get; }
    }
}
