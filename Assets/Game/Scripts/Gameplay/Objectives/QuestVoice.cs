using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// A character in the world who says a quest's briefings and remarks: put it on the character's root and list the
    /// quests. While it is loaded and alive on a machine, <see cref="Presentation.ObjectiveBriefing"/> says those lines
    /// through the character's own <c>Speaker</c>, so the mouth moves and other players see the bubble; without one (not
    /// spawned here, streamed out, dead) the lines still play on the popup under the quest's
    /// <see cref="ObjectiveChain.Speaker"/> name. Presentation only: nothing here is sent or saved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QuestVoice : MonoBehaviour
    {
        [Tooltip("The quests whose lines this character says. Their Speaker name should be this character's.")]
        [SerializeField] private ObjectiveChain[] quests = System.Array.Empty<ObjectiveChain>();

        private static readonly List<QuestVoice> Live = new();

        private HealthComponent health;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Live.Clear();

        /// <summary>EVERY machine: the character saying <paramref name="quest"/>'s lines here, or null for nobody.</summary>
        public static Transform Of(ObjectiveChain quest)
        {
            if (quest == null) return null;

            foreach (QuestVoice voice in Live)
                if (voice.Says(quest)) return voice.transform;

            return null;
        }

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        private bool Says(ObjectiveChain quest)
        {
            if (health != null && !health.Alive) return false;

            foreach (ObjectiveChain own in quests)
                if (own == quest) return true;

            return false;
        }
    }
}
