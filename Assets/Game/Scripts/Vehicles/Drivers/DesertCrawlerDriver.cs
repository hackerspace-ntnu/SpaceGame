// Drive for the six-legged desert crawler.
//
// Everything a legged machine's driver does -- the rider channel, the AI channel, the rider-frame
// guard, acceleration, NavMesh path following, ForceStop -- is LeggedDriver. This machine adds
// nothing to it: the station's driver was the more complete of the two, so the extraction moved its
// pathfinding up into the base and the bird gained it.
//
// The tunables are all on the base and appear in the inspector as they always did.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Vehicles.Crawler;

namespace SpaceGame.Vehicles
{
    [RequireComponent(typeof(DesertCrawlerLocomotion))]
    public class DesertCrawlerDriver : LeggedDriver
    {
    }
}
