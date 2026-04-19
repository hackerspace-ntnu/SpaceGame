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

    private static readonly Collider[] HitBuffer = new Collider[32];
    private EntityFaction myFaction;

    private void Awake() => myFaction = GetComponent<EntityFaction>();

    public void Broadcast(Transform alertTarget, Vector3 lastKnownPosition)
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, alertRadius, HitBuffer, receiverLayers);
        for (int i = 0; i < count; i++)
        {
            if (HitBuffer[i].transform == transform)
                continue;

            AlertReceiverModule receiver = HitBuffer[i].GetComponent<AlertReceiverModule>();
            if (!receiver)
                continue;

            if (alliedOnly && myFaction != null)
            {
                EntityFaction theirFaction = HitBuffer[i].GetComponent<EntityFaction>();
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
