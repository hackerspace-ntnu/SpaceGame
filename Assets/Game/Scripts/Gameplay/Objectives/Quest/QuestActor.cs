using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A character or thing a quest step can name: the elder, a keeper, the witch, Merob, a fallen log.
    /// Steps refer to it by <see cref="ActorId"/>, never by reference, because a step is an asset and
    /// the character lives in a chunk scene or is spawned at runtime.
    ///
    /// <para>
    /// <b>The id is authored, not generated.</b> Step assets type it in ("hamlet-elder"), so it has to
    /// be readable, and it has to be the same on every copy: a generated id baked into a prefab is
    /// shared by every instance, and one regenerated per instance would not match the step. Two live
    /// actors with one id is an error.
    /// </para>
    /// <para>
    /// Announces its death on the server as a <see cref="QuestMark.Dead"/> — never for a death that is
    /// only a save being restored. <see cref="ReportSubdued"/> is the same for a fight it survives.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestActor : MonoBehaviour
    {
        private static readonly Dictionary<string, QuestActor> Live = new();

        [Tooltip("Named in quest step assets. Unique among characters, readable, never renamed once a " +
                 "step names it.")]
        [SerializeField] private string actorId;

        [Tooltip("How close a player must be for the server to believe they talked to this character, metres.")]
        [SerializeField, Min(0.5f)] private float talkRange = 6f;

        private HealthComponent health;

        public string ActorId => actorId;
        public float TalkRange => talkRange;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        /// <summary>The loaded actor with this id. False while its chunk is not loaded, or it is dead and gone.</summary>
        public static bool TryFind(string id, out QuestActor actor)
        {
            actor = null;
            return !string.IsNullOrEmpty(id) && Live.TryGetValue(id, out actor) && actor != null;
        }

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(actorId))
            {
                Debug.LogError($"[Quest] {name} has a QuestActor with no id, so no quest step can name it.", this);
                return;
            }

            if (Live.TryGetValue(actorId, out QuestActor other) && other != null && other != this)
                Debug.LogError($"[Quest] Two characters share the quest id '{actorId}': {other.name} and {name}.", this);

            Live[actorId] = this;

            health = GetComponent<HealthComponent>();
            if (health != null) health.OnDeath += OnDied;
        }

        private void OnDisable()
        {
            if (!string.IsNullOrEmpty(actorId) && Live.TryGetValue(actorId, out QuestActor registered) && registered == this)
                Live.Remove(actorId);

            if (health != null) health.OnDeath -= OnDied;
        }

        private void OnDied()
        {
            // A restore announces a death so listeners match the record; it is not this one dying now.
            if (Network.Decides && !health.IsRestoring) QuestMarks.Raise(QuestMark.Dead, actorId);
        }

        /// <summary>SERVER: this character was beaten and lived. Raised by whatever decides that.</summary>
        public void ReportSubdued() => QuestMarks.Raise(QuestMark.Subdued, actorId);

        /// <summary>Whether <paramref name="who"/> is close enough to have been talking to this character.</summary>
        public bool InTalkRange(Transform who) =>
            who != null && (who.position - transform.position).sqrMagnitude <= talkRange * talkRange;
    }
}
