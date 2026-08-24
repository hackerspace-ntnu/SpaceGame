using UnityEngine;
using SpaceGame.Characters;

namespace SpaceGame.Items
{
    /// <summary>
    /// Tells a holder's rig what is in its hand.
    ///
    /// <para>
    /// This used to be much larger. It measured the holder's input, rigidbody, NavMeshAgent and
    /// CharacterController every frame, and dropped the pose whenever any of them reported motion,
    /// because the player's controller had a single unmasked layer and a hold pose therefore
    /// replaced the whole body — legs included. A player who held a gun and walked either glided
    /// with frozen legs or lost the pose. There was no third option.
    /// </para>
    /// <para>
    /// The Upper Body layer removed the problem rather than managing it: the pose is masked to the
    /// chest and arms, the legs keep running the Base Layer, and the item can stay in the hand
    /// while the player walks. All of the movement gating went with it.
    /// </para>
    /// <para>
    /// Two kinds of holder, because they have different rigs. A player has a
    /// <see cref="PlayerAimRig"/> and gets a hold style. Anything else — an NPC, a turret — keeps
    /// the original <c>Hold</c> bool, which is what its controller is still built around.
    /// </para>
    /// </summary>
    public class HoldAnimator : MonoBehaviour
    {
        [Tooltip("Optional explicit Animator for the fallback bool. If null, the component first " +
                 "tries the holder's Animator, then any Animator in this object's children.")]
        [SerializeField] private Animator animator;

        [Tooltip("Bool parameter driven on holders that have no PlayerAimRig — NPCs and turrets.")]
        [SerializeField] private string boolParameter = "Hold";

        private Animator resolvedAnimator;
        private PlayerAimRig rig;
        private bool wroteBool;

        private int cachedHash;
        private string cachedFor;

        /// <summary>
        /// The hashed parameter name, resolved on demand rather than in Awake.
        ///
        /// <para>
        /// Awake is not guaranteed to have run by the time <see cref="SetHeld"/> is called.
        /// <c>UsableItem.OnEquipped</c> adds this component and calls straight into it on the very
        /// next line, and a component whose hash is still 0 silently matches no parameter and
        /// poses nothing — an NPC standing in its idle tree holding a rifle, with no error to say
        /// why. Resolving here removes the ordering dependency instead of relying on it.
        /// </para>
        /// <para>
        /// Cached against the name it was built from so a caller that retunes
        /// <see cref="boolParameter"/> in the inspector is not left driving the old one.
        /// </para>
        /// </summary>
        private int ParamHash
        {
            get
            {
                if (cachedFor != boolParameter)
                {
                    cachedFor = boolParameter;
                    cachedHash = Animator.StringToHash(boolParameter);
                }
                return cachedHash;
            }
        }

        /// <summary>Called by UsableItem.OnEquipped/OnUnequipped. <paramref name="holder"/> is the holder.</summary>
        public void SetHeld(GameObject holder, bool value)
        {
            if (value)
            {
                rig = holder != null ? holder.GetComponent<PlayerAimRig>() : null;
                resolvedAnimator = rig != null ? null : ResolveAnimator(holder);

                if (rig != null)
                {
                    var grip = GetComponent<ItemGrip>();
                    rig.SetHeldStyle(grip != null ? grip.Style : ItemGrip.HoldStyle.OneHanded);
                }
                else
                {
                    WriteBool(true);
                }

                return;
            }

            if (rig != null) rig.SetHeldStyle(ItemGrip.HoldStyle.None);
            else WriteBool(false);

            rig = null;
            resolvedAnimator = null;
        }

        /// <summary>
        /// The NPC path, unchanged in behaviour from the version this replaced.
        ///
        /// <para>
        /// Tracked with <see cref="wroteBool"/> so the parameter is only ever cleared by whoever
        /// set it — an item destroyed after its holder has already picked up something else must
        /// not switch that new item's pose off.
        /// </para>
        /// </summary>
        private void WriteBool(bool value)
        {
            if (resolvedAnimator == null || resolvedAnimator.runtimeAnimatorController == null) return;
            if (!HasParam(resolvedAnimator, ParamHash)) return;
            if (!value && !wroteBool) return;

            resolvedAnimator.SetBool(ParamHash, value);
            wroteBool = value;
        }

        private Animator ResolveAnimator(GameObject holder)
        {
            if (holder != null)
            {
                var fromHolder = holder.GetComponentInChildren<Animator>(true);
                if (fromHolder != null) return fromHolder;
            }
            if (animator != null) return animator;
            return GetComponentInChildren<Animator>(true);
        }

        private static bool HasParam(Animator a, int hash)
        {
            if (a == null) return false;
            var ps = a.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].nameHash == hash) return true;
            return false;
        }
    }
}
