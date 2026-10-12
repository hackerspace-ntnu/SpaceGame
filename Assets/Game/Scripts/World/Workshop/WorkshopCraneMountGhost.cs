using System.Collections.Generic;
using SpaceGame.Vehicles;

namespace SpaceGame.World
{
    /// <summary>
    /// What the crane's driver sees of the ship while the core hangs on the hook: every empty mount it fits drawn red,
    /// and green the one letting go now would fit it into (GDC-L1-UX-0003: where it goes, before the press). The same
    /// ghosts a module in the hands paints (<see cref="ShipPartSocket.SetGhost"/>). Local only: never sent, never saved.
    /// </summary>
    public sealed class WorkshopCraneMountGhost
    {
        private readonly List<ShipPartSocket> painted = new();
        private readonly List<ShipPartSocket> wanted = new();

        /// <summary>Paint this frame's ghosts; every frame while driving. A mount no longer wanted is cleared.</summary>
        public void Show(WorkshopCrane crane)
        {
            wanted.Clear();
            if (crane != null && crane.IsLoaded)
            {
                crane.TryFindMount(crane.LoadCentre, out ShipPartRack target, out int targetSocket);
                foreach (ShipPartRack rack in ShipPartRack.Active)
                {
                    for (int i = 0; i < rack.Sockets.Count; i++)
                    {
                        ShipPartSocket mount = rack.Sockets[i];
                        if (mount == null || !rack.Accepts(i, crane.Installs)) continue;

                        mount.SetGhost(rack == target && i == targetSocket ? ShipPartGhost.Target : ShipPartGhost.Missing);
                        wanted.Add(mount);
                    }
                }
            }

            foreach (ShipPartSocket mount in painted)
                if (mount != null && !wanted.Contains(mount)) mount.SetGhost(ShipPartGhost.Off);
            painted.Clear();
            painted.AddRange(wanted);
        }

        public void Clear()
        {
            foreach (ShipPartSocket mount in painted)
                if (mount != null) mount.SetGhost(ShipPartGhost.Off);
            painted.Clear();
        }
    }
}
