// Tells nearby friends who to fight.
//
// Two things raise an alert on the agent carrying this: spotting a target by itself
// (AgentTargeting.TargetAcquired with seen == true) and being hurt (ProvocationModule announces
// its new aggressor). Both go to every AlertReceiverModule on an ALLIED entity within alertRadius,
// found through EntityTargetRegistry rather than a physics overlap — the registry is the one
// definition of who is on whose side, and a layer mask left at Nothing was the silent way the
// old overlap version delivered no alert to anyone.
//
// Cascade cap, by construction: a target that ARRIVES by alert is handed over with
// AgentTargeting.ForceTarget, which raises TargetAcquired with seen == false, and this component
// does not pass those on. One sighting wakes one camp, not the map. The receivers still converge
// and fight; they just do not re-broadcast.
//
// Until 2026-09-07 nothing called Broadcast at all, so "alert allies" had never happened.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    public class AlertBroadcaster : MonoBehaviour
    {
        [Tooltip("How far an alert carries, in metres, to allied receivers.")]
        [SerializeField] private float alertRadius = 30f;

        [Tooltip("Also announce a target this agent spotted by itself, not only one it was hurt " +
                 "by. Off for a scout that should watch quietly.")]
        [SerializeField] private bool announceSightings = true;

        // Instance-level, not static: two broadcasters firing in the same frame would otherwise
        // read each other's results out of one shared list.
        private readonly List<EntityFaction> allies = new List<EntityFaction>(32);

        private EntityFaction myFaction;
        private AgentTargeting targeting;

        public float AlertRadius => alertRadius;

        private EntityFaction MyFaction =>
            myFaction != null ? myFaction : myFaction = GetComponent<EntityFaction>();

        private void OnEnable()
        {
            targeting = AgentTargeting.GetOrAdd(gameObject);
            targeting.TargetAcquired += HandleTargetAcquired;
        }

        private void OnDisable()
        {
            if (targeting != null)
                targeting.TargetAcquired -= HandleTargetAcquired;
        }

        private void HandleTargetAcquired(Transform target, bool seen)
        {
            if (seen && announceSightings)
                Broadcast(target, target.position);
        }

        /// <summary>
        /// Hand <paramref name="alertTarget"/> to every allied receiver in range. Public so a
        /// provocation, a settlement alarm or a scripted event can raise the camp without a sighting.
        /// </summary>
        public void Broadcast(Transform alertTarget, Vector3 lastKnownPosition)
        {
            if (!TargetResolution.IsViable(alertTarget))
                return;

            EntityFaction self = MyFaction;
            if (self == null)
            {
                Debug.LogWarning($"{name}: AlertBroadcaster has no EntityFaction, so it has no allies to alert.", this);
                return;
            }

            EntityTargetRegistry.Query(self, FactionRelationship.Allied, transform.position, alertRadius, allies);
            for (int i = 0; i < allies.Count; i++)
            {
                EntityFaction ally = allies[i];
                if (ally == null || ally.transform == transform)
                    continue;
                if (ally.TryGetComponent(out AlertReceiverModule receiver))
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
}
