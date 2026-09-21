using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Gameplay.Quests;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how far along a quest giver's errand is.
    ///
    /// <para>
    /// Two fields, and the second one is the interesting half. The step index alone would be a
    /// record that means something different the moment the giver is holding a different questline
    /// — which happens whenever a town is regenerated, because <c>TownGenerator</c> re-deals its
    /// questlines to freshly placed NPCs. So the record names the questline by asset GUID, and a
    /// record about a different questline is discarded rather than applied: dropping somebody into
    /// step 3 of an errand they have never been given is worse than starting them at the beginning.
    /// </para>
    /// <para>
    /// The GUID, never an object reference: <see cref="Questline.ID"/> is the asset's GUID for the
    /// same reason <see cref="Items.InventoryItem.ID"/> is, so the questline can be renamed or moved
    /// without orphaning every record that named it.
    /// </para>
    /// <para>
    /// Progress is deliberately world state rather than per player. One member of a session hands
    /// the item over and the errand has moved on for everybody, which is how a trader's stock
    /// already behaves and is the only reading under which the NPC can hold a single conversation.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(QuestGiver))]
    public class QuestGiverSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "quest";        // written into save files — NEVER rename

        private QuestGiver giver;

        // Lazy, NOT cached in Awake: EditMode tests never run Awake, and a saver that caches there
        // cannot be round-trip tested by PersistenceProbe.
        private QuestGiver Giver => giver != null ? giver : giver = GetComponent<QuestGiver>();

        public string SaveKey => Key;

        public struct State
        {
            /// <summary>The questline's asset GUID, so a record can tell which errand it is about.</summary>
            public string questline;

            public int step;
        }

        public object CaptureState()
        {
            QuestGiver target = Giver;
            if (target == null || target.Line == null) return null;

            // Untouched is the overwhelmingly common case — most people in most towns are never
            // spoken to. Returning null drops the key entirely rather than writing a "step 0" row
            // for every NPC in every settlement in the world.
            if (target.StepIndex <= 0) return null;

            return new State
            {
                questline = target.QuestlineId,
                step = target.StepIndex,
            };
        }

        public void RestoreState(JObject state)
        {
            QuestGiver target = Giver;
            if (target == null) return;

            // A null payload is a VALUE, not an absence: it means this giver was at its defaults
            // when the save was taken, and it has to be put back there. Deliberately unlike
            // TraderSaveable, which returns early because Awake has just rebuilt its offers from
            // the profile — nothing rebuilds a step index, and a chunk being re-hydrated can hand
            // back a giver that is still mid-errand in memory.
            if (state == null)
            {
                target.RestoreStep(0);
                return;
            }

            State restored = state.ToObject<State>(SaveSerializer.Serializer);

            if (!string.IsNullOrEmpty(restored.questline) &&
                !string.Equals(restored.questline, target.QuestlineId, System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[QuestGiverSaveable] '{name}' has questline '{target.QuestlineId}' but its saved " +
                    $"record is about '{restored.questline}'. Starting from the beginning — this town " +
                    "was almost certainly regenerated after the save was written.", this);

                target.RestoreStep(0);
                return;
            }

            target.RestoreStep(restored.step);
        }
    }
}
