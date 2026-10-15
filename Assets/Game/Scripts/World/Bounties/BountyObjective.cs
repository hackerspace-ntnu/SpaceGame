using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Presentation;
using SpaceGame.World.Posters;

namespace SpaceGame.World.Bounties
{
    /// <summary>
    /// The open bounty as the visor shows it: ask around town, search where the tip pointed, bring the target back — and,
    /// while every player is outside the hunt's bounds, how long the crew have to come back before it expires. Read on
    /// every machine from the board's replicated progress and the target's body; holds nothing of its own. One per taken
    /// bounty, so the visor announces a new one.
    /// </summary>
    public sealed class BountyObjective : ITrackedObjective
    {
        private readonly BountyBoard board;
        private readonly BountyHunt bounty;

        public BountyObjective(BountyBoard board, BountyHunt bounty)
        {
            this.board = board;
            this.bounty = bounty;
        }

        private string Name => bounty != null ? bounty.TargetName : "the outlaw";

        private bool OutOfBounds => board.Progress.OutsideFor > 0f;

        public string Title => board.Progress.Phase switch
        {
            BountyPhase.Asking => $"Ask around town about {Name}",
            BountyPhase.Located => $"Find {Name}",
            _ => board.Progress.TargetDead ? $"Bring {Name}'s body back to town" : $"Bring {Name} back to town",
        };

        public string Status => OutOfBounds ? $"{OutOfBoundsLine}\n{PhaseStatus}" : PhaseStatus;

        /// <summary>The server counts <see cref="BountyProgress.OutsideFor"/> in whole seconds, so this ticks once a second.</summary>
        private string OutOfBoundsLine
        {
            get
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(board.OutOfBoundsSeconds - board.Progress.OutsideFor));
                // A colon, not a dash: the visor's lines punctuate that way, and its font may not carry an em dash.
                return $"Out of bounds: return within {left} s";
            }
        }

        private string PhaseStatus => board.Progress.Phase switch
        {
            BountyPhase.Asking => "Talk to the people where the poster hangs",
            BountyPhase.Located => Wanted,
            _ => board.Progress.TargetDead ? "Drag the body in" : "They follow whoever is nearest",
        };

        private string Wanted => bounty != null && bounty.Wanted == WantedStatus.Dead
            ? "Wanted dead. Alive pays less"
            : "Wanted alive: beat them and they give up";

        /// <summary>
        /// The town while asking, handing in, or out of bounds (the bounds are round it, so it is the way back); the
        /// target once it is close; otherwise the search area round the hunt's site.
        /// </summary>
        public bool TryGetWaypoint(out Vector3 position)
        {
            BountyProgress progress = board.Progress;
            if (progress.Phase != BountyPhase.Located || OutOfBounds)
            {
                position = progress.Town;
                return true;
            }

            if (board.TryGetTargetPosition(out position) && NearLocalPlayer(position)) return true;

            position = default;
            if (bounty == null || !bounty.HasSite) return false;
            position = BountyRules.SearchCentre(bounty.Id, progress.Site, board.SearchOffset);
            return true;
        }

        private bool NearLocalPlayer(Vector3 point)
        {
            Transform local = GameplayMenuScope.LocalPlayerTransform;
            return local != null && (local.position - point).sqrMagnitude <= board.RevealRange * board.RevealRange;
        }
    }
}
