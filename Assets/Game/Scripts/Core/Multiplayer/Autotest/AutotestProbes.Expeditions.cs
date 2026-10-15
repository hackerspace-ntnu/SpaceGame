// Queries about settlement expeditions as THIS machine has them: the stand-ins it can see, who they stand in for,
// whether they carry their kit in hand, and whether anybody is shown twice — at home and on the road at once.
//
// A client was never told which band is out or who is on it, so everything here is found from what the machine shows:
// a stand-in names the resident it stands in for (ExpeditionMember's replicated key), and the settlement's own scene
// content holds that resident under the same key on every machine.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.World;

namespace SpaceGame.Core
{
    internal static partial class AutotestProbes
    {
        /// <summary>Every stand-in on this machine: a resident's prefab out with a band, named for the resident.</summary>
        public static List<ExpeditionMember> StandIns()
        {
            var found = new List<ExpeditionMember>();
            foreach (ExpeditionMember member in Object.FindObjectsByType<ExpeditionMember>(FindObjectsSortMode.None))
                if (member.IsStandIn) found.Add(member);
            return found;
        }

        /// <summary>
        /// The loaded settlement that sends bands out (its culture has an expedition profile); null when none is loaded.
        /// Not <see cref="FindSettlement"/>: another settlement with residents (the astronaut colony beside it) may load first.
        /// </summary>
        public static Settlement FindExpeditionSettlement()
        {
            foreach (Settlement settlement in Object.FindObjectsByType<Settlement>(FindObjectsSortMode.None))
                if (settlement.HasResidents && settlement.Culture != null && settlement.Culture.expeditions != null) return settlement;
            return null;
        }

        /// <summary>
        /// Every body of <paramref name="settlement"/>'s people on this machine: its residents at home and every stand-in
        /// (a stand-in has no settlement of its own). Other settlements' residents are not counted.
        /// </summary>
        public static int ResidentBodies(Settlement settlement)
        {
            int bodies = 0;
            foreach (Resident resident in Object.FindObjectsByType<Resident>(FindObjectsSortMode.None))
                if (resident.IsStandIn || resident.Settlement == settlement) bodies++;
            return bodies;
        }

        /// <summary>The settlement's resident at home with <paramref name="residentKey"/>; null when it has none.</summary>
        public static Resident HomeResident(Settlement settlement, string residentKey)
        {
            foreach (Resident resident in settlement.Society.Residents)
                if (resident != null && SettlementSociety.ResidentKey(resident) == residentKey) return resident;
            return null;
        }

        /// <summary>
        /// The residents' identities (their authored <see cref="SaveableEntity"/> ids), in roster order: what has to be the
        /// same before a band leaves and after it is home. A resident without one is listed as empty.
        /// </summary>
        public static List<string> ResidentIds(Settlement settlement)
        {
            var ids = new List<string>();
            foreach (Resident resident in settlement.Society.Residents)
            {
                SaveableEntity entity = resident != null ? resident.GetComponent<SaveableEntity>() : null;
                ids.Add(entity != null ? entity.InstanceId : string.Empty);
            }
            return ids;
        }

        /// <summary>How many of <paramref name="ids"/> repeat one listed before them.</summary>
        public static int Duplicates(IReadOnlyList<string> ids)
        {
            var seen = new HashSet<string>();
            int repeats = 0;
            foreach (string id in ids)
                if (!seen.Add(id)) repeats++;
            return repeats;
        }

        /// <summary>Stand-ins whose name is the name of the resident at home they stand in for.</summary>
        public static int StandInsNamedRight(Settlement settlement, IReadOnlyList<ExpeditionMember> standIns)
        {
            int right = 0;
            foreach (ExpeditionMember standIn in standIns)
            {
                Resident home = HomeResident(settlement, standIn.Identity.residentKey);
                Resident body = standIn.GetComponent<Resident>();
                if (home != null && body != null && !string.IsNullOrEmpty(body.DisplayName) && body.DisplayName == home.DisplayName)
                    right++;
            }
            return right;
        }

        /// <summary>Stand-ins with their kit's weapon in hand.</summary>
        public static int StandInsArmed(IReadOnlyList<ExpeditionMember> standIns)
        {
            int armed = 0;
            foreach (ExpeditionMember standIn in standIns)
            {
                InventoryItem weapon = standIn.KitWeapon;
                if (weapon != null && InHand(standIn.gameObject) == weapon) armed++;
            }
            return armed;
        }

        /// <summary>Residents shown at home while a stand-in of theirs is out: the one body somebody would see twice.</summary>
        public static int SeenTwice(Settlement settlement, IReadOnlyList<ExpeditionMember> standIns)
        {
            int twice = 0;
            foreach (ExpeditionMember standIn in standIns)
            {
                Resident home = HomeResident(settlement, standIn.Identity.residentKey);
                if (home != null && home.Presence != null && !home.Presence.Hidden) twice++;
            }
            return twice;
        }

        /// <summary>
        /// Every stand-in on one line, key, name and the item in its hand: <c>r:12=Rasha(Tool_Spear_Bone)</c>. The host's line
        /// and the client's are laid side by side by the caller and must agree.
        /// </summary>
        public static string StandInLine(IReadOnlyList<ExpeditionMember> standIns)
        {
            var line = new StringBuilder();
            foreach (ExpeditionMember standIn in standIns)
            {
                Resident body = standIn.GetComponent<Resident>();
                InventoryItem held = InHand(standIn.gameObject);
                if (line.Length > 0) line.Append(" | ");
                line.Append(standIn.Identity.residentKey).Append('=').Append(body != null ? body.DisplayName : "-")
                    .Append('(').Append(held != null ? held.name : "-").Append(')');
            }
            return line.ToString();
        }

        private static InventoryItem InHand(GameObject body)
        {
            var equipment = body.GetComponent<EntityEquipmentController>();
            return equipment != null ? equipment.EquippedItem : null;
        }
    }
}
