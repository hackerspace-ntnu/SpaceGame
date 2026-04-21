// Broadcasts a target alert to all AlertReceiverModules within radius on the same faction.
// Call Broadcast() from ChaseModule (OnEnterAttackRange), PerceptionModule, or any external trigger.
// Receivers wake up and chase the alerted target position even without independent detection.
using UnityEngine;

public class AlertBroadcaster : MonoBehaviour
{
    [SerializeField] private float alertRadius = 20f;
    [SerializeField] private LayerMask receiverLayers;
    [Tooltip("If true, only alert entities of the same faction.")]
    [SerializeField] private bool alliedOnly = true;

    private readonly Collider[] hitBuffer = new Collider[32];
    private EntityFaction myFaction;

    private void Awake()
    {
        myFaction = GetComponent<EntityFaction>();

        if (receiverLayers == 0)
            Debug.LogWarning($"{name}: AlertBroadcaster.receiverLayers is Nothing — alerts will never reach any receiver. Set the layer mask in the Inspector.", this);
    }

    public void Broadcast(Transform alertTarget, Vector3 lastKnownPosition)
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, alertRadius, hitBuffer, receiverLayers);
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].transform == transform)
                continue;

            AlertReceiverModule receiver = hitBuffer[i].GetComponent<AlertReceiverModule>();
            if (!receiver)
                continue;

            if (alliedOnly && myFaction != null)
            {
                EntityFaction theirFaction = hitBuffer[i].GetComponent<EntityFaction>();
                if (!myFaction.IsAlliedWith(theirFaction))
                    continue;
            }

            receiver.ReceiveAlert(alertTarget, lastKnownPosition);
        }
    }

    private void OnValidate() => alertRadius = Mathf.Max(0f, alertRadius);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, alertRadius);
    }
}
