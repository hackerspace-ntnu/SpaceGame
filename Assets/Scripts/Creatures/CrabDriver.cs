// Drive for the crab walker.
//
// Everything a legged machine's driver does -- the rider channel, the AI channel, the rider-frame
// guard, acceleration, NavMesh path following, ForceStop -- is `LeggedDriver`. This machine adds
// nothing to it in code; what it adds is a serialized flag. `lateralSteering` is set on the prefab,
// and it is what turns the drive from "a heading and a throttle" into a planar one: the crab sets
// off toward its destination whatever way it is pointing, and turns only to bring the target abeam,
// where its legs sweep fastest.
//
// The tunables are all on the base and appear in the inspector as they always did.
using UnityEngine;

[RequireComponent(typeof(CrabLocomotion))]
public class CrabDriver : LeggedDriver
{
}
