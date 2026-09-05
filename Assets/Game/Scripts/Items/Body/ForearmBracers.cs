using System.Text;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// The armoured bracer every player wears on both forearms, always.
    ///
    /// <para>
    /// Until 2026-09-04 each gauntlet model carried its own copy of the bracer, so putting one on
    /// swapped a whole forearm and taking it off left a bare sleeve. Now the bracer is a permanent
    /// part of the suit and a gauntlet is only the device that stands on its hardpoint deck: the
    /// player always has somewhere to clip a device, an empty arm reads as a bare deck waiting for
    /// one, and six devices stopped shipping six copies of the same armour.
    /// </para>
    /// <para>
    /// This wears nothing and holds nothing. It is not a body slot, not an item, and cannot be
    /// removed — <see cref="BodyEquipmentController"/> owns everything that CAN be taken off. The
    /// two objects it makes are scenery parented to the arm bones.
    /// </para>
    ///
    /// <para><b>Multiplayer.</b> Nothing is networked and nothing needs registering. Every machine
    /// runs this for every player body it has, local or remote, and produces the same two children
    /// from the same prefab, so peers see the bracers without a byte on the wire. There is no state
    /// for two machines to disagree about: the bracer is a constant.</para>
    ///
    /// <para><b>Persistence.</b> Nothing to save, and deliberately so. "Everyone wears two" is not
    /// state, it is a rule — a saved copy of it could only ever be right or corrupt. The gauntlets
    /// standing on the deck are saved, by <c>BodyEquipmentSaveable</c>, as they always were.</para>
    /// </summary>
    [RequireComponent(typeof(BodyEquipmentController))]
    [DisallowMultipleComponent]
    public class ForearmBracers : MonoBehaviour
    {
        [Tooltip("The bracer model. Needs a GauntletFit, because it is seated by the very call that " +
                 "seats a gauntlet — see ForearmSeat.")]
        [SerializeField] private GameObject bracerPrefab;

        [Tooltip("How long to keep waiting for the arm sites before reporting that this body has " +
                 "none. Long enough to cover a body that spawns before the rest of its rig is up.")]
        [SerializeField, Min(0f)] private float resolveTimeoutSeconds = 5f;

        private BodyEquipmentController equipment;
        private bool leftSeated;
        private bool rightSeated;
        private bool reported;
        private float deadline;

        private void Awake()
        {
            equipment = GetComponent<BodyEquipmentController>();
            deadline = Time.time + resolveTimeoutSeconds;
        }

        /// <summary>
        /// Seat each arm as soon as its site exists, and keep looking until it does.
        ///
        /// <para>
        /// The sites are resolved in <see cref="BodyEquipmentController"/>'s own <c>Start</c>, and
        /// this used to take the first <c>LateUpdate</c> as proof that had happened — Unity runs
        /// every <c>Start</c> of a frame before any <c>LateUpdate</c> of it. That holds only while
        /// the controller's <c>Start</c> actually runs, and it does not run at all on a body whose
        /// slots are missing (it disables itself) or on one whose components come up in a different
        /// order than expected. One missed frame left the player permanently bare-armed and said
        /// nothing about it, which is the failure this screen exists to make impossible.
        /// </para>
        /// <para>
        /// So: poll, per arm, and after <see cref="resolveTimeoutSeconds"/> say plainly that no
        /// bracer is coming and why. Cheap — two null checks a frame, on a component that switches
        /// itself off the moment both arms are dressed.
        /// </para>
        /// </summary>
        private void LateUpdate()
        {
            leftSeated |= TrySeat(BodySlot.LeftGauntlet);
            rightSeated |= TrySeat(BodySlot.RightGauntlet);

            if (leftSeated && rightSeated) { enabled = false; return; }

            if (Time.time < deadline || reported) return;

            reported = true;
            Report();
            enabled = false;
        }

        /// <summary>Seats one arm if its site is ready. False means "not yet", and is asked again next frame.</summary>
        private bool TrySeat(BodySlot slot)
        {
            if (bracerPrefab == null)
            {
                // Not survivable and not going to change: say so once and stop asking.
                Debug.LogError("ForearmBracers: no bracer prefab. Run Tools ▸ SpaceGame ▸ Items ▸ " +
                               "Build Forearm Bracers, which makes the prefab and points this at it.", this);
                enabled = false;
                return false;
            }

            Transform forearm = equipment.ForearmBone(slot);
            EquipItemSocket hand = equipment.HandSocket(slot);
            if (forearm == null || hand == null) return false;

            var fit = bracerPrefab.GetComponent<GauntletFit>();
            if (fit == null)
            {
                Debug.LogError($"ForearmBracers: '{bracerPrefab.name}' has no GauntletFit, so there " +
                               "is nothing to say which frame its model is in.", this);
                enabled = false;
                return false;
            }

            GameObject instance = Instantiate(bracerPrefab, forearm);
            instance.name = $"Bracer ({slot})";
            ForearmSeat.Apply(instance, forearm, hand.Socket, hand.GripRotation,
                              slot == BodySlot.LeftGauntlet, fit);
            return true;
        }

        /// <summary>
        /// Name what is missing, once. Both halves are worth printing: which arm, and which of the
        /// two things an arm needs never turned up — the bone comes from the rig and the socket
        /// from the hand's grip frame, and they fail for different reasons.
        ///
        /// <para>
        /// A body that cannot wear gear at all is a warning, not an error, and there is one in the
        /// world: `world/persistentScene.unity` holds a plain instance of the BASE
        /// `PlayerCharacter` prefab, which has no `IBodyEquipment`, so its controller disables
        /// itself in `Start`. Bare arms are the correct outcome there and an error every load would
        /// only teach the next reader to ignore this one.
        /// </para>
        /// </summary>
        private void Report()
        {
            if (!equipment.enabled)
            {
                Debug.LogWarning(
                    "ForearmBracers: this body wears no bracers because it wears no gear at all — " +
                    "BodyEquipmentController disabled itself for want of an IBodyEquipment. Only " +
                    "PlayerCharacterNetworked has one; the base PlayerCharacter prefab cannot wear " +
                    "gear, and there is an instance of it in world/persistentScene.unity. Nothing " +
                    "to fix unless this body was meant to be a real player.", this);
                return;
            }

            var missing = new StringBuilder();
            foreach (BodySlot slot in new[] { BodySlot.LeftGauntlet, BodySlot.RightGauntlet })
            {
                if (slot == BodySlot.LeftGauntlet ? leftSeated : rightSeated) continue;

                missing.Append(missing.Length > 0 ? "; " : "");
                missing.Append(slot).Append(": ");
                missing.Append(equipment.ForearmBone(slot) == null ? "no forearm bone" : "forearm bone ok");
                missing.Append(", ");
                missing.Append(equipment.HandSocket(slot) == null ? "no hand socket" : "hand socket ok");
            }

            Debug.LogError(
                $"ForearmBracers: no bracer after {resolveTimeoutSeconds:F0} s — {missing}. " +
                "BodyEquipmentController resolves both in its own Start; it does not run at all if " +
                "the body has no IBodyEquipment (only PlayerCharacterNetworked has one — the base " +
                "PlayerCharacter prefab cannot wear gear), and the socket needs an " +
                "EquipmentController with a hand bone it can resolve. This body will stay bare-armed.",
                this);
        }
    }
}
