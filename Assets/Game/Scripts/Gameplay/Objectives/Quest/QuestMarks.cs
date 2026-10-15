using SpaceGame.Core;
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>What happened to a named thing in the world, as far as a quest step cares.</summary>
    public enum QuestMark
    {
        /// <summary>Somebody finished a conversation with a <see cref="QuestActor"/>.</summary>
        Talked,

        /// <summary>A <see cref="QuestActor"/> died.</summary>
        Dead,

        /// <summary>A <see cref="QuestActor"/> was beaten without being killed.</summary>
        Subdued,

        /// <summary>A chant circle finished its chant.</summary>
        Chanted,

        /// <summary>Something fire clears away was burned away (<c>BurnAway</c>).</summary>
        Burned,
    }

    /// <summary>
    /// SERVER: the one place quest-relevant happenings are announced. Each one is recorded against every
    /// unfinished quest's current step (<see cref="QuestStepMemory"/>), where a step reads it with
    /// <c>world.HasMark</c> — so a step never subscribes to anything itself and stays a stateless asset.
    ///
    /// <para>
    /// A mark counts only for the step that is current when it lands: the per-step memory is wiped
    /// on every step change. Talking to the elder before the step that asks for it does not finish
    /// that step early.
    /// </para>
    /// </summary>
    public static class QuestMarks
    {
        public static void Raise(QuestMark mark, string id)
        {
            if (!Network.Decides)
            {
                Debug.LogError($"[Quest] '{mark} {id}' was raised on a client. Quest marks are decided by the server.");
                return;
            }

            if (string.IsNullOrEmpty(id)) return;

            QuestStepMemory.Record(mark, id);
        }
    }
}
