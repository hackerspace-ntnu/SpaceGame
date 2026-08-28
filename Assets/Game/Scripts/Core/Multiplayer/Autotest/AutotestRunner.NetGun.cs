// The net-gun shot the host run and the persistence run both fire.
using System.Collections;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Gameplay;
using SpaceGame.Items;

namespace SpaceGame.Core
{
    internal sealed partial class AutotestRunner
    {
        /// <summary>The net gun, by its Resources path. The only artifact this test fires.</summary>
        private const string NetGunPath = "Items/Artifacts/NetGun";

        /// <summary>
        /// Put a net gun in the host's hands and fire it at a creature.
        ///
        /// <para>
        /// The shot goes through <c>EquipmentController.UseHeldItem</c>, which is
        /// <c>OnUse</c> — the same request/present/hop-to-server path a button press takes. Only
        /// the binding from the Use action to that method is left out, because a C# event
        /// cannot be raised from outside the class that declares it.
        /// </para>
        /// <para>
        /// The creature is MOVED to where the shot comes down rather than aimed at, because the
        /// aim is not this test's to choose: it comes out of the muzzle on the end of an
        /// animated arm. Asking the gun where its next shot would land and putting the quarry
        /// there tests the same thing a player walking into range tests, and it does not depend
        /// on the hold pose happening to point the barrel anywhere in particular.
        /// </para>
        /// </summary>
        private IEnumerator FireNetGunAtQuarry()
        {
            GameObject shooter = AutotestProbes.LocalPlayerObject();
            AgentController quarry = AutotestProbes.FindNetworkedQuarry(out ulong quarryId);

            if (shooter == null || quarry == null)
            {
                Report("HOST_NETGUN", shooter == null ? "no player object" : "no creature to net");
                yield break;
            }

            Report("HOST_QUARRY_ID", quarryId);
            Report("HOST_QUARRY_NAME", quarry.name);

            var gun = Resources.Load<InventoryItem>(NetGunPath);
            var inventory = shooter.GetComponent<IPlayerInventory>();
            var equipment = shooter.GetComponent<EquipmentController>();

            if (gun == null || inventory == null || equipment == null)
            {
                Report("HOST_NETGUN", gun == null ? "item asset missing" : "player has no inventory");
                yield break;
            }

            inventory.TryAddItem(gun);
            yield return WaitAtMost(() => SlotHolding(inventory, gun) >= 0, 10f);

            int slot = SlotHolding(inventory, gun);
            Report("HOST_NETGUN_SLOT", slot);
            if (slot < 0) yield break;

            // Only when it is not already the selection: SelectSlot TOGGLES, so asking for the
            // slot that is already held puts the gun away instead of taking it out.
            if (inventory.SelectedSlotIndex != slot) inventory.SelectSlot(slot);
            yield return WaitAtMost(() => equipment.HeldUsable is NetGunArtifact, 10f);

            var netGun = equipment.HeldUsable as NetGunArtifact;
            Report("HOST_NETGUN_EQUIPPED", netGun != null);
            if (netGun == null) yield break;

            Report("HOST_NETGUN_CHARGES", netGun.ChargesRemaining);

            Vector3 landing = GroundUnder(PredictedLanding(netGun));
            Persistence.SaveTeleport.Move(quarry.gameObject, landing, quarry.transform.rotation);

            Report("HOST_NETGUN_MUZZLE_TO_QUARRY",
                   Vector3.Distance(shooter.transform.position, quarry.transform.position).ToString("F1"));

            equipment.UseHeldItem();
            Report("HOST_NETGUN_FIRED", true);

            // The net flies for MaxFlightSeconds and the capture pass runs on the first frame
            // after that, so this is the flight plus room for the drape to settle.
            yield return WaitAtMost(() => AutotestProbes.CountNets(out int held) > 0 && held > 0, 10f);

            int nets = AutotestProbes.CountNets(out int captives);
            Report("HOST_NETS", nets);
            Report("HOST_NET_CAPTIVES", captives);
            Report("HOST_QUARRY_BOUND", AutotestProbes.IsNetted(quarry.gameObject));
            Report("HOST_NETGUN_CHARGES_AFTER", netGun.ChargesRemaining);

            // Only interesting when nothing was caught, and then it is the whole story: it says
            // whether the net missed or whether the capture pass refused what it found.
            if (captives == 0) Report("HOST_NET_MISS_BY", NetToQuarryDistance(quarry).ToString("F1"));
        }

        /// <summary>Which hotbar slot an item ended up in, or -1.</summary>
        private static int SlotHolding(IPlayerInventory inventory, InventoryItem item)
        {
            for (int i = 0; i < inventory.GetInventorySize(); i++)
            {
                InventorySlot slot = inventory.GetSlot(i);
                if (slot != null && slot.Item == item) return i;
            }

            return -1;
        }

        /// <summary>
        /// Where this gun's next shot would come down.
        ///
        /// Asked of the gun itself — <c>OnRequestUse</c> is what fills in the muzzle and the
        /// aim, and it is the only thing that knows where the barrel is on the end of an
        /// animated arm. The seed it rolls here is not the one the real shot rolls, and that
        /// does not matter: the seed scatters the aim by a fraction of a degree, and the net is
        /// six metres across.
        /// </summary>
        private static Vector3 PredictedLanding(NetGunArtifact gun)
        {
            var probe = new NetArg();
            gun.OnRequestUse(ref probe);

            return NetGunFlight.PositionAt(probe.P, probe.R * Vector3.forward, probe.B,
                                           NetGunFlight.MaxFlightSeconds);
        }

        /// <summary>
        /// The ground under a point, or the point itself where there is none.
        ///
        /// The fallback is not defensive padding. This build's scene set is Bootstrap, MainMenu
        /// and persistentScene — the terrain lives in the chunk scenes, which are left out
        /// because they are most of the build time and none of the netcode. With no ground to
        /// hit, <c>SnareCatch</c>'s own height sample also falls back to the net's position, so
        /// putting the quarry at the raw landing point is what keeps the two agreeing.
        /// </summary>
        private static Vector3 GroundUnder(Vector3 point)
        {
            const float probeUp = 200f;
            const float probeRange = 400f;

            return Physics.Raycast(point + Vector3.up * probeUp, Vector3.down,
                                   out RaycastHit hit, probeRange,
                                   ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : point;
        }

        /// <summary>How far the nearest net's footprint is from a creature. For a miss report.</summary>
        private static float NetToQuarryDistance(AgentController quarry)
        {
            float nearest = float.PositiveInfinity;

            foreach (SnareCatch net in Object.FindObjectsByType<SnareCatch>(FindObjectsSortMode.None))
            {
                float distance = Vector3.Distance(net.Footprint.center, quarry.transform.position);
                if (distance < nearest) nearest = distance;
            }

            return nearest;
        }
    }
}
