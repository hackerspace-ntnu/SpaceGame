using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.World
{
    /// <summary>
    /// Win the ranch's tame animal over by petting it (<see cref="CreatureTrust"/>). Met the moment it trusts people —
    /// at once if somebody already won it over before the quest asked. The count is read off the animal's replicated
    /// trust, so every machine shows the same.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Ranch/Win Trust Step")]
    public class WinTrustStep : RanchStep
    {
        [Tooltip("Under the title; {0} is pets so far, {1} pets it takes.")]
        [SerializeField] private string progress = "<b>RMB</b>  pet its head  ·  trust {0}/{1}";

        public override bool IsMet(ObjectiveWorld world)
        {
            CreatureTrust tame = Tame;
            return tame != null && tame.IsTrusting;
        }

        public override string Status(ObjectiveWorld world)
        {
            CreatureTrust tame = Tame;
            return tame != null ? string.Format(progress, tame.Pets, tame.PetsToTrust) : string.Empty;
        }

        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position)
        {
            CreatureTrust tame = Tame;
            position = tame != null ? tame.transform.position : site != null ? site.Yard : default;
            return tame != null || site != null;
        }

        private CreatureTrust Tame => LoadedRanch != null ? LoadedRanch.Tame : null;
    }
}
