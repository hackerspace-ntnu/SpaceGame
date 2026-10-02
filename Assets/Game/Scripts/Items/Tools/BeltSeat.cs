using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>One place on a belt, in the wearer's own frame.</summary>
    [Serializable]
    public struct BeltAnchorPose
    {
        public BeltSlot slot;

        [Tooltip("Metres, in the wearer's root frame (+Y up, +Z forward).")]
        public Vector3 position;

        [Tooltip("Degrees, in the wearer's root frame. Identity hangs the item with its +Z pointing " +
                 "forward; the defaults turn each anchor so +Z points out from the body.")]
        public Vector3 euler;
    }

    /// <summary>
    /// Hang an item on a belt anchor, and make the anchors.
    ///
    /// <para>
    /// The arithmetic is shared by <c>BeltCarrier</c>, which hangs an NPC's bag at runtime, and by
    /// the editor's preview of the same, so what is rendered while tuning a tool is exactly what
    /// the game does — the reason <see cref="WornSeat"/> exists for worn gear.
    /// </para>
    /// </summary>
    public static class BeltSeat
    {
        /// <summary>Right hip, left hip, and the small of the back, in a 3 m character's frame.</summary>
        public static BeltAnchorPose[] DefaultAnchors() => new[]
        {
            new BeltAnchorPose { slot = BeltSlot.HipRight, position = new Vector3(0.33f, 1.45f, 0f), euler = new Vector3(0f, 90f, 0f) },
            new BeltAnchorPose { slot = BeltSlot.HipLeft, position = new Vector3(-0.33f, 1.45f, 0f), euler = new Vector3(0f, -90f, 0f) },
            new BeltAnchorPose { slot = BeltSlot.Back, position = new Vector3(0f, 1.50f, -0.26f), euler = new Vector3(0f, 180f, 0f) },
        };

        /// <summary>
        /// A child of <paramref name="hips"/> that sits where <paramref name="pose"/> says, in the
        /// frame of <paramref name="character"/>. Authored in character space and converted here
        /// because a bone's own axes are whatever its last export left them.
        /// </summary>
        public static Transform CreateAnchor(Transform character, Transform hips, BeltAnchorPose pose)
        {
            var go = new GameObject($"BeltAnchor_{pose.slot}");
            go.transform.SetParent(hips, false);
            go.transform.SetPositionAndRotation(character.TransformPoint(pose.position),
                                                character.rotation * Quaternion.Euler(pose.euler));
            return go.transform;
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
