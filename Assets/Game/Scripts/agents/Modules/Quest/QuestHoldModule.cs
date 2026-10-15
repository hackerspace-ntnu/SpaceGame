// Holds a character still, facing the nearest player, while a quest is in a given stretch -- the
// witch, standing at her door through her monologue before the fight begins.
//
// The server's half of a split cutscene (Cutscenes.md, Extending #6): the cutscene is presentation
// on each machine, and THIS is what keeps the character from attacking anyone mid-sentence, decided
// from replicated quest progress so it agrees with every machine's cutscene. Scripted priority, so
// no movement module below it -- casting, chasing -- runs while it holds.
//
// Priority does not reach side-effect modules: the controller ticks those every frame whoever wins
// the movement. So anything that acts without moving -- summoning, blinking -- is listed in
// silencedWhileHeld and switched off for the hold, or it fights the player through the monologue.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;

namespace SpaceGame.Agents
{
    public class QuestHoldModule : BehaviourModuleBase
    {
        [Tooltip("While the chain is inside this window, the character stands and faces the nearest player.")]
        [SerializeField] private QuestStepWindow holdDuring;

        [Tooltip("Side-effect modules (summon, blink) switched off while the hold lasts — priority does not stop them.")]
        [SerializeField] private BehaviourModuleBase[] silencedWhileHeld = System.Array.Empty<BehaviourModuleBase>();

        private bool silenced;

        /// <summary>Is the quest holding the character still right now? Every machine, off replicated progress.</summary>
        public bool IsHolding => holdDuring.Contains(holdDuring.FindRun());

        private void Reset() => SetPriorityDefault(ModulePriority.Scripted);

        public override string ModuleDescription =>
            "Holds the character still, facing the nearest player, while the quest is inside " +
            "holdDuring, and switches off silencedWhileHeld meanwhile. Passes otherwise.";

        private void OnDisable() => Silence(false);

        public override MoveIntent? Tick(in AgentContext context, float deltaTime)
        {
            bool holding = IsHolding;
            Silence(holding);
            if (!holding)
                return null;

            PlayerIdentity nearest = PlayerIdentity.Nearest(context.Position, float.PositiveInfinity, out _);
            return nearest != null ? MoveIntent.StopAndFace(nearest.transform.position) : MoveIntent.Idle();
        }

        private void Silence(bool value)
        {
            if (silenced == value) return;
            silenced = value;
            foreach (BehaviourModuleBase module in silencedWhileHeld)
                if (module != null) module.Active = !value;
        }

        protected override void OnValidate() => SetMinPriority(ModulePriority.Scripted);
    }
}
