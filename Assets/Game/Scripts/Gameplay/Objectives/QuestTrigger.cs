using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>
    /// What begins a catalog quest: the crew walking into a settlement, another quest finishing. Each kind is a
    /// subclass; each trigger in the game is one asset, set on the quest (<see cref="ObjectiveChain.Trigger"/>).
    ///
    /// <para>
    /// <b>Stateless and SERVER only,</b> like a step's <see cref="ObjectiveStep.IsMet"/>: the director asks it every
    /// few tenths of a second while its quest has not begun and the crew have landed, and begins the quest for
    /// everyone the first time it answers true. Ask only about state every machine already shares — player
    /// positions, saved quests, the baked world.
    /// </para>
    /// </summary>
    public abstract class QuestTrigger : ScriptableObject
    {
        /// <summary>SERVER: true once the quest should begin.</summary>
        public abstract bool ShouldStart(ObjectiveDirector director);
    }
}
