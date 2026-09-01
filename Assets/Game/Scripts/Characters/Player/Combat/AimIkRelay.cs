using UnityEngine;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Forwards the Animator's IK pass to <see cref="PlayerAimRig"/>, which lives on the root.
    ///
    /// <para>
    /// Unity delivers <c>OnAnimatorIK</c> only to components sharing a GameObject with the
    /// Animator. On this character the Animator is on the model child and the rig is on the root
    /// next to PlayerController, so without this the callback simply never arrives — silently,
    /// with no warning and no error, which is the worst way for it to fail.
    /// </para>
    /// <para>
    /// Added at runtime by the rig rather than authored on the prefab, so re-exporting the model
    /// cannot lose it.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class AimIkRelay : MonoBehaviour
    {
        private PlayerAimRig rig;

        public void Bind(PlayerAimRig owner) => rig = owner;

        private void OnAnimatorIK(int layerIndex)
        {
            if (rig != null) rig.ApplyIk(layerIndex);
        }
    }
}
