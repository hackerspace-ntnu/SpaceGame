using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Gameplay.Status;

namespace SpaceGame.Gameplay.Hazards
{
    /// <summary>
    /// Something fire clears out of the way for good — a thorn wall across the forest path, a
    /// spore-puff, the growth choking the hamlet's well. Set it alight with anything that lights
    /// bodies (the flamethrower, the flame gauntlet, a patch of burning ground) and after
    /// <see cref="burnSeconds"/> of burning it is gone.
    ///
    /// <para>
    /// <b>Authored with a <see cref="StatusReceiver"/></b>, because the flame only lights what is a
    /// body and scenery is not — an authored receiver is how a piece of scenery opts in. Burning is
    /// drawn on every machine by the status system; the removal is decided where the receiver
    /// decides, and goes through <c>World.Despawn</c>, which both despawns it on every peer (it needs
    /// an in-scene <c>NetworkObject</c>) and leaves a tombstone so it stays gone after a reload (it
    /// needs a <c>SaveableEntity</c>).
    /// </para>
    /// <para>
    /// <b>A quest's obstacle</b> names a <see cref="questMarkId"/>, raised as <see cref="QuestMark.Burned"/>
    /// as it goes, and may only take flame inside <see cref="burnableDuring"/>. The window is what
    /// keeps a favour finishable: burned before anyone asked, the mark would land in no step and the
    /// thing would be gone for good with the favour still open.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(StatusReceiver))]
    public class BurnAway : MonoBehaviour
    {
        [Tooltip("Seconds it must burn before it is gone. Longer than one touch of flame, so it has " +
                 "to be set alight on purpose.")]
        [SerializeField, Min(0.1f)] private float burnSeconds = 4f;

        [Tooltip("Optional. Raised as a Burned quest mark when it goes, for a step waiting on it.")]
        [SerializeField] private string questMarkId;

        [Tooltip("Optional. With a chain set, it only burns while the chain is inside this window.")]
        [SerializeField] private QuestStepWindow burnableDuring;

        private StatusReceiver receiver;
        private float burning;

        private void Awake() => receiver = GetComponent<StatusReceiver>();

        private bool Burnable => burnableDuring.chain == null || burnableDuring.IsInside;

        private void Update()
        {
            if (!receiver.Decides) return;

            if (!receiver.Has(StatusKind.Burning) || !Burnable)
            {
                burning = 0f;
                return;
            }

            burning += Time.deltaTime;
            if (burning < burnSeconds) return;

            if (!string.IsNullOrEmpty(questMarkId)) QuestMarks.Raise(QuestMark.Burned, questMarkId);
            GameServices.World.Despawn(gameObject);
        }
    }
}
