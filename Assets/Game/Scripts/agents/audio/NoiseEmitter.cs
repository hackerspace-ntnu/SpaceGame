// Convenience wrapper: emits noise from this GameObject's position.
//
// The broadcast itself lives in the static Noise class, so anything without a
// component — a weapon, an explosion, a scripted event — can report a sound with
// one call. This exists for the case where the emitter *is* an object in the
// world and its transform is the answer, which is what EntityAudioModule,
// PerceptionModule and HealthReactionModule all want.
//
// It used to own the broadcast, using an OverlapSphere against a serialized
// receiverLayers mask. That mask defaulted to Nothing, so an emitter added
// without one was silently deaf to the whole world; see Noise.cs for why the
// registry replaced it. The field is gone — nothing needs configuring now.
using UnityEngine;

namespace SpaceGame.Agents
{
    public class NoiseEmitter : MonoBehaviour
    {
        [Tooltip("Emitted noises skip receivers under this transform, so the entity does not " +
                 "startle itself with its own footsteps. Defaults to this object's root.")]
        [SerializeField] private Transform selfRoot;

        private void Awake()
        {
            if (selfRoot == null)
                selfRoot = transform.root;
        }

        /// <summary>Report a noise of <paramref name="type"/> carrying <paramref name="radius"/> metres.</summary>
        /// <param name="instigator">
        /// Who to blame. Defaults to this object — right for a footstep, wrong for a hurt
        /// noise, where the caller passes the attacker so receivers can aggro onto them.
        /// </param>
        public void Emit(NoiseType type, float radius, Transform instigator = null)
        {
            Noise.Emit(type, transform.position, radius,
                       instigator != null ? instigator : transform,
                       selfRoot != null ? selfRoot : transform);
        }
    }
}
