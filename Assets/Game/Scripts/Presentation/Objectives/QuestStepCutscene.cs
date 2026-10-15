using UnityEngine;
using SpaceGame.Characters;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Plays a cutscene to this machine's player the moment a quest reaches a step, if they are there
    /// to see it — the witch's monologue, for whoever stands in her glade when the crew arrive.
    ///
    /// <para>
    /// Every machine decides for its own player from replicated progress; nothing goes on the wire.
    /// A player out of range, joining late or reloading does not see it: it belongs to the moment
    /// (<see cref="StepArrival"/>). What the scene's characters do meanwhile is the server's — the
    /// step that started it lasts long enough for it.
    /// </para>
    /// </summary>
    public class QuestStepCutscene : MonoBehaviour
    {
        [SerializeField] private ObjectiveChain chain;

        [Tooltip("Play when the chain reaches this step.")]
        [SerializeField] private string onStep;

        [Tooltip("The QuestSite id the player must be near to see it.")]
        [SerializeField] private string siteId;

        [Tooltip("How far beyond the site's own radius still counts as there, metres.")]
        [SerializeField, Min(0f)] private float extraRange = 20f;

        [SerializeField] private Cutscene cutscene;

        private ObjectiveDirector director;
        private StepArrival arrival;

        private void OnEnable()
        {
            director = ObjectiveDirector.Instance;
            if (director != null)
            {
                director.Started += OnChanged;
                director.Changed += OnChanged;
            }
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.Started -= OnChanged;
                director.Changed -= OnChanged;
            }
        }

        private void OnChanged(Quest quest, bool live)
        {
            if (!arrival.Notice(quest, live, chain, onStep)) return;
            if (cutscene == null || CutsceneDirector.Instance == null) return;

            PlayerController player = GameplayMenuScope.FindLocalPlayer();
            if (player == null || !QuestSite.TryGet(siteId, out Vector3 site, out float radius)) return;
            if (!QuestPlaces.IsInside(player.transform.position, site, radius + extraRange)) return;

            CutsceneDirector.Instance.Play(cutscene, player.gameObject);
        }
    }
}
