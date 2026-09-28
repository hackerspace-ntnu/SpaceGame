using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Desert settlement flavor: buildings face the settlement center — the same convention already
    /// used for the Clanker town (<see cref="RobotSettlementGenerator"/>) — snapped to 90° so
    /// axis-aligned prefabs still read square. Everything else is the generic <see cref="Settlement"/>
    /// pipeline.
    /// </summary>
    public class DesertSettlement : Settlement
    {
        protected override Quaternion GetSpawnRotation(Vector2 localXZ, ref SettlementPlacementUtil.SeededRng rng)
        {
            if (localXZ.sqrMagnitude < 0.01f)
                return base.GetSpawnRotation(localXZ, ref rng);

            float yaw = Mathf.Atan2(-localXZ.x, -localXZ.y) * Mathf.Rad2Deg;
            yaw = Mathf.Round(yaw / 90f) * 90f;
            return Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
