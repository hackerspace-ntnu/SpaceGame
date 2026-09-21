// The rules of a hand-in, with nothing attached to them.
//
// Separated from QuestGiver so the awkward cases — a full hotbar, a step with a reward, a step
// without, the last step — can be tested without a scene, a popup, a player or a network. The
// component is then only plumbing, which is the half that has to be tested by playing.
using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Quests
{
    public static class QuestProgress
    {
        /// <summary>
        /// Can this hand-in happen right now?
        ///
        /// <paramref name="why"/> is for the log, never for the player — the NPC says its own line
        /// either way.
        /// </summary>
        public static bool CanHandIn(IPlayerInventory inventory, QuestStep step, out string why)
        {
            if (step == null)                { why = "no step"; return false; }
            if (inventory == null)           { why = "no inventory"; return false; }
            if (step.required == null)       { why = "step asks for no item"; return false; }

            if (InventoryQuery.CountHeld(inventory, step.required) < 1)
            {
                why = $"player is not carrying {step.required.itemName}";
                return false;
            }

            // Room for what comes back. The slot the payment frees counts toward it, which is what
            // lets a player with a completely full hotbar still complete a one-for-one step — the
            // same allowance TraderInteraction.CanAfford makes, for the same reason.
            if (step.reward != null)
            {
                int freeAfterPayment = InventoryQuery.CountFree(inventory) + 1;
                if (freeAfterPayment < 1)
                {
                    why = "no room for the reward";
                    return false;
                }
            }

            why = string.Empty;
            return true;
        }

        /// <summary>Where the questline stands after a step lands.</summary>
        public static int NextStep(int current, Questline questline)
        {
            if (questline == null) return current;
            return Mathf.Clamp(current + 1, 0, questline.StepCount);
        }

        /// <summary>
        /// True when there is nothing left to ask for. A finished giver falls silent and the
        /// character goes back to whatever dialog its prefab already had.
        /// </summary>
        public static bool IsDone(int current, Questline questline) =>
            questline == null || current >= questline.StepCount;

        /// <summary>The step a giver on <paramref name="current"/> is asking about, or null.</summary>
        public static QuestStep Current(int current, Questline questline) =>
            questline == null ? null : questline.StepAt(current);
    }
}
