using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Hang an item on a worn garment's mount, and make the anchors.
    ///
    /// <para>
    /// The arithmetic is shared by <c>BeltCarrier</c>, which hangs an NPC's bag at runtime, and by
    /// the editor's preview of the same, so what is rendered while tuning a tool is exactly what
    /// the game does — the reason <see cref="WornSeat"/> exists for worn gear.
    /// </para>
    /// </summary>
    public static class BeltSeat
    {
        /// <summary>
        /// One anchor per slot the wearer's garments offer, each a child of the bone its garment
        /// names and sitting where the garment's mount point does on that bone's bind pose. Empty
        /// when nothing worn offers a mount.
        /// </summary>
        public static Dictionary<BeltSlot, Transform> CreateAnchors(Transform character, Animator wearer)
        {
            var anchors = new Dictionary<BeltSlot, Transform>();
            foreach (GarmentMounts garment in character.GetComponentsInChildren<GarmentMounts>())
            {
                Transform bone = wearer.GetBoneTransform(garment.Bone);
                if (bone == null)
                {
                    Debug.LogError($"{wearer.name}: '{garment.name}' hangs items from {garment.Bone}, " +
                                   "which the rig lacks.", garment);
                    continue;
                }

                foreach (GarmentMounts.Mount mount in garment.Mounts)
                {
                    if (mount.point == null || anchors.ContainsKey(mount.slot)) continue;

                    if (!garment.TryPoseOnBone(bone, mount, out Vector3 position, out Quaternion rotation))
                    {
                        Debug.LogError($"{wearer.name}: '{garment.name}' is not skinned to {garment.Bone}, " +
                                       $"so its {mount.slot} mount cannot be placed.", garment);
                        continue;
                    }

                    var anchor = new GameObject($"BeltAnchor_{mount.slot}").transform;
                    anchor.SetParent(bone, false);
                    anchor.SetLocalPositionAndRotation(position, rotation);
                    anchors.Add(mount.slot, anchor);
                }
            }

            return anchors;
        }

        /// <summary>
        /// Which slot each mountable item hangs from: the first of its <see cref="BeltMount.Slots"/> that
        /// <paramref name="offered"/> contains and no earlier item took. <paramref name="mounts"/> is the bag in
        /// slot order, null where a slot holds nothing or something with no mount; the result is keyed by that
        /// index and leaves out an item with no slot left. Pure, so the carrier and the roster's tests ask the
        /// same question.
        /// </summary>
        public static Dictionary<int, BeltSlot> Plan(IReadOnlyList<BeltMount> mounts, ICollection<BeltSlot> offered)
        {
            var placement = new Dictionary<int, BeltSlot>();
            var taken = new HashSet<BeltSlot>();
            for (int i = 0; i < mounts.Count; i++)
            {
                if (mounts[i] == null) continue;

                foreach (BeltSlot slot in mounts[i].Slots)
                {
                    if (!offered.Contains(slot) || !taken.Add(slot)) continue;
                    placement.Add(i, slot);
                    break;
                }
            }

            return placement;
        }

        /// <summary>
        /// Instantiate <paramref name="prefab"/> on <paramref name="anchor"/>, sized as it is in the
        /// hand, with its <see cref="BeltMount.Hang"/> laid on the anchor. Null when the prefab has
        /// no <see cref="BeltMount"/>. <paramref name="socket"/> owns the instance: unequip it to
        /// take the item off.
        /// </summary>
        public static GameObject Hang(Transform anchor, GameObject prefab, out EquipItemSocket socket)
        {
            socket = null;
            if (prefab == null || prefab.GetComponent<BeltMount>() == null) return null;

            socket = new EquipItemSocket(anchor, HandGripFrame.Identity("belt anchor"));
            GameObject instance = socket.Equip(prefab);
            if (instance == null) return null;

            // Equip seated the item's grip point on the anchor; swing it round so its hang
            // transform lies on the anchor instead.
            Transform hang = instance.GetComponent<BeltMount>().Hang;
            instance.transform.rotation = anchor.rotation * Quaternion.Inverse(hang.rotation) * instance.transform.rotation;
            instance.transform.position += anchor.position - hang.position;
            return instance;
        }
    }
}
