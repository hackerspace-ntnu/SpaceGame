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
// does not pass those on — and a fight an alert started is provoked unannounced
// (ProvocationModule.Announces). One sighting wakes one camp, not the map. The receivers still
// converge and fight; they just do not re-broadcast.
//
// Calling for help is the deliberate exception, per prefab (callForHelpEvery > 0, e.g. every Raxy):
// while this agent is fighting an enemy still inside its ProvocationModule leash, it hands that enemy
// to every ally in range again and again, naming itself as the one in need — and an ally it pulls in
// calls too if it has the same setting. The fight then spreads through allies standing close to it,
// but never further than alertRadius past a fighter that is itself within its leash of the enemy, and
// it stops when the fighters calm down. It cannot walk from camp to camp across the map.
//
// Until 2026-09-07 nothing called Broadcast at all, so "alert allies" had never happened.
using System.Collections.Generic;
using SpaceGame.Core;
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

        [Tooltip("Seconds between calls for help while this agent is fighting an enemy inside its leash: " +
                 "every ally in alertRadius is handed the enemy again, and one it pulls in calls in turn if " +
                 "it has this set too. 0 = announce once, when the fight starts.")]
        [SerializeField, Min(0f)] private float callForHelpEvery;

        // Instance-level, not static: two broadcasters firing in the same frame would otherwise
        // read each other's results out of one shared list.
        private readonly List<EntityFaction> allies = new List<EntityFaction>(32);

        private EntityFaction myFaction;
        private AgentTargeting targeting;
        private ProvocationModule provocation;
        private float nextCallAt;

        public float AlertRadius => alertRadius;

        private EntityFaction MyFaction =>
            myFaction != null ? myFaction : myFaction = GetComponent<EntityFaction>();

        private ProvocationModule Provocation =>
            provocation != null ? provocation : provocation = GetComponent<ProvocationModule>();

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

        private void Update()
        {
            if (callForHelpEvery <= 0f || !Network.Decides || Time.time < nextCallAt)
                return;

            if (CallForHelp())
                nextCallAt = Time.time + callForHelpEvery;
        }

        /// <summary>
        /// One call for help: if this agent is fighting an enemy still inside its leash (a calming-down
        /// agent whose enemy walked off calls nobody), hand that enemy to every ally in range, naming this
        /// agent as whose fight it is. True when it called.
        /// </summary>
        public bool CallForHelp()
        {
            ProvocationModule fight = Provocation;
            if (fight == null || !fight.IsProvoked || fight.CalmingFor > 0f)
                return false;

            Transform enemy = fight.Aggressor;
            if (!TargetResolution.IsViable(enemy))
                return false;

            Broadcast(enemy, enemy.position, transform);
            return true;
        }

        private void HandleTargetAcquired(Transform target, bool seen)
        {
            if (seen && announceSightings)
                Broadcast(target, target.position);
        }

        /// <summary>
        /// Hand <paramref name="alertTarget"/> to every allied receiver in range. Public so a
        /// provocation, a settlement alarm or a scripted event can raise the camp without a sighting.
        /// <paramref name="victim"/> is whose fight it is — the ally that was hurt or wronged, so a
        /// receiver can weigh its bond to them; null for a sighting.
        /// </summary>
        public void Broadcast(Transform alertTarget, Vector3 lastKnownPosition, Transform victim = null)
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
                    receiver.ReceiveAlert(alertTarget, lastKnownPosition, victim);
            }
        }

        private void OnValidate()
        {
            alertRadius = Mathf.Max(0f, alertRadius);
            callForHelpEvery = Mathf.Max(0f, callForHelpEvery);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, alertRadius);
        }
    }
}
