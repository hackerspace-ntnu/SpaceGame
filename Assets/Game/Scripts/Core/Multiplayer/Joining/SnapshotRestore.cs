// The joiner's half of SessionSnapshot: place each entry once what it names has arrived.
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core.Persistence;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core
{
    /// <summary>
    /// Rebuilds one <see cref="SnapshotPayload"/> entry on this machine. Every method answers
    /// false — and does nothing — while the thing the entry names has not been spawned here yet,
    /// so <see cref="SessionSnapshot"/> can simply ask again next frame.
    /// </summary>
    internal static class SnapshotRestore
    {
        /// <summary>Tie one rope, or false while either of its ends is still on its way.</summary>
        public static bool TryTie(in SnapshotPayload.RopeEntry entry)
        {
            if (!TryResolveEnd(entry.a, out GameObject rootA)) return false;
            if (!TryResolveEnd(entry.b, out GameObject rootB)) return false;

            // The tuning — and the rope MATERIAL, which nothing else can supply — comes off the
            // leash item's own prefab, exactly as the save path resolves it.
            LeashArtifact.TryResolveSettings(out Leash.Settings settings);
            if (entry.length > 0.01f) settings.length = entry.length;

            Leash rope = Leash.Create(settings);

            rope.RestoreEnd(true, rootA, entry.a.offset, entry.a.point, entry.a.held);
            rope.RestoreEnd(false, rootB, entry.b.offset, entry.b.point, entry.b.held);

            return true;
        }

        /// <summary>
        /// The live object for one end, or false while it is still on its way.
        ///
        /// <para>
        /// A null object with <c>true</c> is a legitimate answer and means "this end is a place,
        /// not a thing" — <see cref="Leash.RestoreEnd"/> makes an anchor for it.
        /// </para>
        /// </summary>
        private static bool TryResolveEnd(in SnapshotPayload.RopeEnd end, out GameObject root)
        {
            root = null;
            if (end.anchor == 0) return true;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) return false;

            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(end.anchor, out NetworkObject obj)
                || obj == null)
                return false;

            root = obj.gameObject;
            return true;
        }

        /// <summary>Place one shooter's apertures, or false while that shooter is still on its way.</summary>
        public static bool TryPlace(in SnapshotPayload.PortalEntry entry)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) return false;

            if (!manager.SpawnManager.SpawnedObjects.TryGetValue(entry.shooter, out NetworkObject shooter)
                || shooter == null)
                return false;

            // Added rather than required: the component is authored on the player prefab, but a
            // shooter with no saver is one prefab change away, and losing the portals silently is
            // the failure this whole class exists to stop.
            if (!shooter.TryGetComponent(out PortalPairSaveable saver))
                saver = shooter.gameObject.AddComponent<PortalPairSaveable>();

            // ApplyNow, not RestoreState: a client joining a running session runs no deferred load
            // pass, so a staged record would sit there for ever. See PortalPairSaveable.ApplyNow.
            saver.ApplyNow(entry.portals);
            return true;
        }
    }
}
