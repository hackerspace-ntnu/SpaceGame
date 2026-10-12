using UnityEngine;
using SpaceGame.Gameplay.Objectives;
using SpaceGame.Items;
using SpaceGame.Vehicles;

namespace SpaceGame.World
{
    /// <summary>
    /// The workshop's story as the visor shows it: open the gate, meet the Shellbacks, follow the thieves' trail, raid
    /// their camp, catch the runner, bring the gear home; then turn the main room, find the vault, take the reactor core, carry it to the
    /// crane console and lower it onto the ship. Read on every machine from the mission's replicated state and what is
    /// in the world; holds nothing of its own.
    /// </summary>
    public sealed class WorkshopMissionObjective : ITrackedObjective
    {
        private enum Step
        {
            None, Gate, Meet, FindTrail, FollowTrail, RaidCamp, CatchRunner, PickUp, BringHome,
            TurnMainRoom, FindVault, TakeCore, PickUpCore, CarryCore, LowerCore, BringShip,
        }

        // Within this of the camp the gear is still where the thieves keep it: the raid, not a pick-up.
        private const float CampRadius = 20f;

        // Within this of its cradle the core is still in the vault: taken from there, not picked up off the floor.
        private const float VaultRadius = 12f;

        // How far from the workshop a dropped core is looked for, for the waypoint.
        private const float LostCoreRadius = 400f;

        private readonly WorkshopMission mission;

        public WorkshopMissionObjective(WorkshopMission mission) => this.mission = mission;

        public string Title => Now(out _, out _) switch
        {
            Step.Gate => "Open the workshop gate",
            Step.Meet => "Meet the Shellbacks in the main hall",
            Step.FindTrail => "Find the gear thieves' trail",
            Step.FollowTrail => "Follow the gear thieves' trail",
            Step.RaidCamp => "Get the Heart Gear back from the thieves' camp",
            Step.CatchRunner => "Catch the thief with the Heart Gear",
            Step.PickUp => "Pick up the Heart Gear",
            Step.BringHome => "Bring the Heart Gear home",
            Step.TurnMainRoom => "Turn the main room to open the hidden hallways",
            Step.FindVault => "Find the Shellbacks' vault",
            Step.TakeCore => "Take the reactor core from the vault",
            Step.PickUpCore => "Pick up the reactor core",
            Step.CarryCore => "Take the core to the crane console on the third floor",
            Step.LowerCore => "Lower the core onto your ship",
            Step.BringShip => "Bring your ship into the main hall",
            _ => string.Empty,
        };

        public string Status
        {
            get
            {
                switch (Now(out _, out _))
                {
                    case Step.Gate:
                        int loose = mission.Gate != null ? mission.Gate.LooseGears : 0;
                        return loose == 1 ? "1 gear still lies by the gate" : $"{loose} gears still lie by the gate";
                    case Step.FindTrail:
                        return "They left through the gate";
                    case Step.FollowTrail:
                        return $"Search the marked area: your scanner picks up what they dropped\nClues {mission.CluesFound}/{mission.Trail.Count}";
                    case Step.RaidCamp:
                        return "Sneak in and take it, or fight for it";
                    case Step.CatchRunner:
                        return "Knock him down and he drops it";
                    case Step.BringHome:
                        return "To the middle of the workshop's main hall";
                    case Step.TurnMainRoom:
                        return "Hold the big hand cog by the entrance until the room's doorway faces a new hallway";
                    case Step.FindVault:
                        int missing = mission.VaultDoor != null ? mission.VaultDoor.LooseGears : 0;
                        return "Deep in the caves, behind the turning rooms: turn each room to find the way through" +
                               (missing > 0 ? $"\nThe vault's gear lock is missing {missing} cog{(missing == 1 ? string.Empty : "s")}" : string.Empty);
                    case Step.CarryCore:
                        return "Bring the hook to you there and hang the core on it";
                    case Step.LowerCore:
                        return "Over the green mount, let go with the grab lever";
                    case Step.BringShip:
                        return "Park it under the hoist, then lower the core onto it";
                    default:
                        return string.Empty;
                }
            }
        }

        public bool TryGetWaypoint(out Vector3 position)
        {
            Now(out position, out bool pointed);
            return pointed;
        }

        /// <summary>Which step of the story this machine is on, and where it points.</summary>
        private Step Now(out Vector3 waypoint, out bool pointed)
        {
            waypoint = default;
            pointed = true;
            switch (mission.Phase)
            {
                case WorkshopMissionPhase.Locked:
                    pointed = mission.Gate != null;
                    if (pointed) waypoint = mission.Gate.transform.position;
                    return Step.Gate;
                case WorkshopMissionPhase.Gathering:
                    waypoint = mission.Meeting.position;
                    return Step.Meet;
                case WorkshopMissionPhase.Hunting:
                    return Hunting(out waypoint);
                case WorkshopMissionPhase.Vault:
                    return Vault(out waypoint, out pointed);
                default:
                    pointed = false;
                    return Step.None;
            }
        }

        private Step Hunting(out Vector3 waypoint)
        {
            if (mission.GearCarried)
            {
                waypoint = mission.Meeting.position;
                return Step.BringHome;
            }

            Vector3 start = mission.GateStand.position;
            if (!mission.TryGetCamp(out Vector3 camp))
            {
                waypoint = start;
                return Step.FindTrail;
            }

            if (mission.CarrierHasGear)
            {
                GearThief runner = GearThiefBand.Carrier();
                waypoint = runner != null ? runner.transform.position : camp;
                return Step.CatchRunner;
            }

            // Out of the camp: dropped by the runner, or by a thief who fell. Point at it.
            if (LooseItems.TryFind(mission.StolenGear, camp, mission.LostGearSearchRadius, out waypoint) &&
                (waypoint - camp).sqrMagnitude > CampRadius * CampRadius)
                return Step.PickUp;

            if (mission.CluesFound < mission.Trail.Count)
            {
                waypoint = mission.Trail.SearchCentre(mission.CluesFound, start, camp);
                return Step.FollowTrail;
            }

            waypoint = camp;
            return Step.RaidCamp;
        }

        private Step Vault(out Vector3 waypoint, out bool pointed)
        {
            pointed = true;
            WorkshopCrane crane = mission.Crane;
            if (crane != null && crane.IsLoaded)
            {
                if (TryFindEmptyMount(crane, out waypoint)) return Step.LowerCore;
                waypoint = mission.Meeting.position;
                return Step.BringShip;
            }

            if (mission.GearCarried)
            {
                waypoint = mission.CraneConsole.position;
                return Step.CarryCore;
            }

            Vector3 rest = mission.CoreRest.position;
            GearDoor door = mission.VaultDoor;
            if (door != null && !door.IsSolved)
            {
                // Every way to the vault starts at a hallway off the main room, and none is open while it faces the lane.
                if (mission.MainRoomFacesLane)
                {
                    waypoint = mission.MainWheel.transform.position;
                    return Step.TurnMainRoom;
                }
                waypoint = door.transform.position;
                return Step.FindVault;
            }

            if (LooseItems.TryFind(mission.VaultCore, rest, LostCoreRadius, out waypoint))
                return (waypoint - rest).sqrMagnitude > VaultRadius * VaultRadius ? Step.PickUpCore : Step.TakeCore;

            waypoint = rest;
            pointed = mission.CoreSpawned;
            return Step.TakeCore;
        }

        /// <summary>The nearest mount on any ship that the hoist's load would fit, wherever it is.</summary>
        private static bool TryFindEmptyMount(WorkshopCrane crane, out Vector3 position)
        {
            position = default;
            float best = float.MaxValue;
            foreach (ShipPartRack rack in ShipPartRack.Active)
            {
                for (int i = 0; i < rack.Sockets.Count; i++)
                {
                    ShipPartSocket mount = rack.Sockets[i];
                    if (mount == null || !rack.Accepts(i, crane.Installs)) continue;

                    float distance = (mount.Centre - crane.HookPosition).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    position = mount.Centre;
                }
            }
            return best < float.MaxValue;
        }
    }
}
