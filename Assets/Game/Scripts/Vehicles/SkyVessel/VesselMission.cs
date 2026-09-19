// A sky vessel's drop-off run: Cruise → Approach → Descend → Unload → Climb → Depart → Done.
//
// Pure: VesselPilot reads its sensors, ticks this, and flies wherever Destination and
// TargetAltitude say. Passengers are only a count of who is still aboard — a party may have
// members flying alongside that were never seated, and the run must not wait for them.
using System;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    public enum VesselMissionState { Cruise, Approach, Descend, Unload, Climb, Depart, Done }

    /// <summary>What the vessel steers for horizontally in the current state.</summary>
    public enum VesselDestination { Quarry, Site, Home }

    [Serializable]
    public struct VesselMissionSettings
    {
        [Tooltip("Horizontal distance from the quarry at which Cruise becomes Approach and a drop site is chosen.")]
        public float approachDistance;

        [Tooltip("Horizontal distance from the drop site that counts as over it.")]
        public float arrivalTolerance;

        [Tooltip("Altitude error that counts as at the target altitude.")]
        public float altitudeTolerance;

        [Tooltip("Seconds between passengers leaving the vessel.")]
        public float unloadInterval;

        [Tooltip("Seconds the vessel stays down after the last passenger leaves, so nobody is still " +
                 "on the ramp or under the hull when it lifts.")]
        public float clearDelay;

        // A departing vessel is Done once every player is further than this (or once it is back over
        // home). Never tuned on a prefab: VesselPilot.Begin takes it from whoever despawns the vessel
        // (the world sim's despawn radius), so a vessel never vanishes in view.
        [HideInInspector] public float despawnDistance;

        public static VesselMissionSettings Default => new VesselMissionSettings
        {
            approachDistance = 250f,
            arrivalTolerance = 3f,
            altitudeTolerance = 0.75f,
            unloadInterval = 0.6f,
            clearDelay = 1.5f,
        };
    }

    /// <summary>One tick's sensor readings.</summary>
    public readonly struct VesselSensors
    {
        /// <summary>Horizontal distance to the current <see cref="VesselMission.Destination"/>.</summary>
        public float DistanceToDestination { get; }

        /// <summary>Current altitude minus <see cref="VesselMission.TargetAltitude"/>.</summary>
        public float AltitudeError { get; }

        public int PassengersAboard { get; }

        /// <summary>Distance to the nearest player; infinity when there are none.</summary>
        public float NearestPlayerDistance { get; }

        public VesselSensors(float distanceToDestination, float altitudeError, int passengersAboard,
                             float nearestPlayerDistance)
        {
            DistanceToDestination = distanceToDestination;
            AltitudeError = altitudeError;
            PassengersAboard = passengersAboard;
            NearestPlayerDistance = nearestPlayerDistance;
        }
    }

    public sealed class VesselMission
    {
        private readonly VesselMissionSettings settings;
        private float unloadTimer;
        private float clearTimer;

        public VesselMissionState State { get; private set; } = VesselMissionState.Cruise;
        public DropSite? Site { get; private set; }
        public Vector3 Home { get; }

        public VesselMission(Vector3 home, in VesselMissionSettings settings)
        {
            Home = home;
            this.settings = settings;
        }

        public VesselDestination Destination
        {
            get
            {
                switch (State)
                {
                    case VesselMissionState.Cruise:
                        return VesselDestination.Quarry;
                    case VesselMissionState.Approach:
                        return Site.HasValue ? VesselDestination.Site : VesselDestination.Quarry;
                    case VesselMissionState.Descend:
                    case VesselMissionState.Unload:
                        return VesselDestination.Site;
                    case VesselMissionState.Climb:
                        return Site.HasValue ? VesselDestination.Site : VesselDestination.Home;
                    default:
                        return VesselDestination.Home;
                }
            }
        }

        /// <summary>The site's point while going down to it or unloading there; cruise altitude otherwise.</summary>
        public float TargetAltitude(float cruiseAltitude) =>
            (State == VesselMissionState.Descend || State == VesselMissionState.Unload) && Site.HasValue
                ? Site.Value.Point.y
                : cruiseAltitude;

        /// <summary>
        /// Sets or replaces the drop site, e.g. when the chosen one became blocked. A vessel already
        /// going down to, or unloading at, the old site goes back to Approach: it is no longer over
        /// the site it would be descending onto.
        /// </summary>
        public void SetSite(DropSite site)
        {
            Site = site;
            if (State == VesselMissionState.Descend || State == VesselMissionState.Unload)
                State = VesselMissionState.Approach;
        }

        /// <summary>Gives up on the drop: climbs out and heads home with whoever is still aboard.</summary>
        public void Abort()
        {
            if (State == VesselMissionState.Depart || State == VesselMissionState.Done) return;
            State = VesselMissionState.Climb;
        }

        /// <summary>Advances at most one state. Returns how many passengers to release this tick.</summary>
        public int Tick(in VesselSensors sensors, float dt)
        {
            switch (State)
            {
                case VesselMissionState.Cruise:
                    if (sensors.DistanceToDestination <= settings.approachDistance)
                        State = VesselMissionState.Approach;
                    return 0;

                case VesselMissionState.Approach:
                    if (Site.HasValue && sensors.DistanceToDestination <= settings.arrivalTolerance)
                        State = VesselMissionState.Descend;
                    return 0;

                case VesselMissionState.Descend:
                    // Over the site as well as down at it: a hull that reached the height first would
                    // start unloading while still sliding across.
                    if (Mathf.Abs(sensors.AltitudeError) > settings.altitudeTolerance ||
                        sensors.DistanceToDestination > settings.arrivalTolerance) return 0;
                    unloadTimer = 0f;
                    clearTimer = 0f;
                    State = sensors.PassengersAboard > 0 ? VesselMissionState.Unload : VesselMissionState.Climb;
                    return 0;

                case VesselMissionState.Unload:
                    return TickUnload(sensors.PassengersAboard, dt);

                case VesselMissionState.Climb:
                    if (sensors.AltitudeError >= -settings.altitudeTolerance)
                        State = VesselMissionState.Depart;
                    return 0;

                case VesselMissionState.Depart:
                    // Out of every player's sight, or docked at home — a vessel that only finished
                    // unseen would circle over its own city for as long as anyone stood there.
                    if (sensors.NearestPlayerDistance > settings.despawnDistance ||
                        sensors.DistanceToDestination <= settings.arrivalTolerance)
                        State = VesselMissionState.Done;
                    return 0;

                default:
                    return 0;
            }
        }

        private int TickUnload(int aboard, float dt)
        {
            if (aboard <= 0)
            {
                clearTimer += dt;
                if (clearTimer >= settings.clearDelay) State = VesselMissionState.Climb;
                return 0;
            }

            unloadTimer += dt;
            int release = 0;
            while (unloadTimer >= settings.unloadInterval && release < aboard)
            {
                unloadTimer -= settings.unloadInterval;
                release++;
            }
            if (release == aboard) unloadTimer = 0f;
            return release;
        }
    }
}
