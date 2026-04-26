// Emits noise events that nearby NoiseReceiverModules can hear.
// Call Emit() from any system: footsteps, weapons, explosions, HealthReactionModule, etc.
// Uses OverlapSphereNonAlloc — no allocations per emission.
using UnityEngine;

public class NoiseEmitter : MonoBehaviour
{
    [SerializeField] private LayerMask receiverLayers;

    private readonly Collider[] hitBuffer = new Collider[64];

    private void Awake()
    {
        if (receiverLayers == 0)
            Debug.LogWarning($"{name}: NoiseEmitter.receiverLayers is Nothing — noise will never reach any receiver. Set the layer mask in the Inspector.", this);
    }

    public void Emit(NoiseType type, float radius, Transform instigator = null)
    {
        if (instigator == null)
            instigator = transform;

        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, hitBuffer, receiverLayers);
        for (int i = 0; i < count; i++)
        {
            NoiseReceiverModule receiver = hitBuffer[i].GetComponent<NoiseReceiverModule>();
            if (receiver && hitBuffer[i].transform != transform)
                receiver.OnNoiseHeard(type, transform.position, radius, instigator);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, 10f); // preview radius only
    }
}
