// Worn gear across two machines: the host puts a grappling hook on its right forearm, fires it
// from that slot, and the client must see the bracer on the host's arm.
//
// A worn instance is never a spawned NetworkObject — every machine wears its own copy from the
// replicated slot list, exactly as it equips its own copy of a held item — so nothing in
// SpawnManager can say whether a peer saw it. Only a client can.
using System.Collections;
using UnityEngine;
using Unity.Netcode;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        private const string GrapplePath = "Items/Artifacts/GrapplingHook";

        /// <summary>
        /// Host: pick the grapple up into the hotbar, move it to the right gauntlet slot the way the
        /// gear screen's request lands on the server, then fire it from there.
        /// </summary>
        private IEnumerator WearGrappleOnRightArm()
        {
            GameObject wearer = AutotestProbes.LocalPlayerObject();
            var grapple = Resources.Load<InventoryItem>(GrapplePath);
            var inventory = wearer != null ? wearer.GetComponent<IPlayerInventory>() : null;
            var body = wearer != null ? wearer.GetComponent<BodyEquipmentNetwork>() : null;
            var controller = wearer != null ? wearer.GetComponent<BodyEquipmentController>() : null;

            if (wearer == null || grapple == null || inventory == null || body == null || controller == null)
            {
                Report("HOST_BODY", wearer == null ? "no player object"
                                  : grapple == null ? "grapple asset missing"
                                  : inventory == null ? "player has no inventory"
                                  : "player has no body slots");
                yield break;
            }

            Report("HOST_GRAPPLE_KIND", grapple.equipKind);

            inventory.TryAddItem(grapple);
            yield return WaitAtMost(() => SlotHolding(inventory, grapple) >= 0, 10f);

            int slot = SlotHolding(inventory, grapple);
            Report("HOST_GRAPPLE_SLOT", slot);
            if (slot < 0) yield break;

            body.ServerMove(GearRef.Hotbar(slot), GearRef.Body(BodySlot.RightGauntlet));
            yield return WaitAtMost(() => controller.WornIn(BodySlot.RightGauntlet) is GrapplingHookArtifact, 10f);

            Report("HOST_GRAPPLE_WORN", controller.WornIn(BodySlot.RightGauntlet) is GrapplingHookArtifact);
            Report("HOST_GRAPPLE_LEFT_HOTBAR", SlotHolding(inventory, grapple) < 0);

            controller.UseWorn(BodySlot.RightGauntlet);
            Report("HOST_GRAPPLE_FIRED", true);
        }

        /// <summary>Client: the host's bracer must be on the host's arm here too.</summary>
        private IEnumerator ReadWornGearAsClient()
        {
            yield return WaitAtMost(() => AutotestProbes.CountWornGauntlets(out int remote) > 0 && remote > 0, 20f);

            int worn = AutotestProbes.CountWornGauntlets(out int onRemote);
            Report("CLIENT_WORN_SEEN", worn);
            Report("CLIENT_WORN_ON_REMOTE", onRemote);

            GameObject mine = AutotestProbes.LocalPlayerObject();
            Report("CLIENT_OWN_BODY", mine != null && mine.GetComponent<BodyEquipmentNetwork>() != null);
        }
    }

    internal static partial class AutotestProbes
    {
        /// <summary>
        /// How many players on this machine wear something on either forearm, and how many of
        /// those are somebody else's body — the number that proves replication.
        /// </summary>
        public static int CountWornGauntlets(out int onRemotePlayers)
        {
            int worn = 0;
            onRemotePlayers = 0;

            foreach (BodyEquipmentController controller in Object.FindObjectsByType<BodyEquipmentController>(FindObjectsSortMode.None))
            {
                bool wearing = controller.WornIn(BodySlot.LeftGauntlet) != null
                               || controller.WornIn(BodySlot.RightGauntlet) != null;
                if (!wearing) continue;

                worn++;

                var netObject = controller.GetComponent<NetworkObject>();
                if (netObject != null && !netObject.IsOwner) onRemotePlayers++;
            }

            return worn;
        }
    }
}
