using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Vehicles;

namespace SpaceGame.Items
{
    /// <summary>
    /// Paints the holes in nearby hulls while somebody is carrying a module, and answers which
    /// hole they are pointing at.
    ///
    /// <para>
    /// Entirely local and entirely cosmetic — nothing here is sent or saved. Two players carrying
    /// different modules see two different sets of green, which is right: the colour answers
    /// <em>your</em> question, it is not a property of the ship.
    /// </para>
    /// <para>
    /// Every empty socket reads red, including ones this module does not fit. Red says "something
    /// belongs here"; green says "the thing in your hands belongs here". A socket left unlit
    /// because you happened to be carrying the wrong module would hide the very information a
    /// player out salvaging is looking for.
    /// </para>
    /// </summary>
    public sealed class ShipPartHighlighter
    {
        /// <summary>Exactly the sockets this highlighter painted, so clearing is exact and cannot
        /// reset a socket something else is drawing.</summary>
        private readonly List<ShipPartSocket> painted = new();

        /// <summary>The rack holding the socket being pointed at, or null.</summary>
        public ShipPartRack AimedRack { get; private set; }

        /// <summary>Index of the aimed socket within <see cref="AimedRack"/>, or -1.</summary>
        public int AimedIndex { get; private set; } = -1;

        /// <summary>The socket being pointed at, or null.</summary>
        public ShipPartSocket Aimed { get; private set; }

        /// <summary>
        /// Repaint for one frame: every empty socket within <paramref name="ghostRange"/> goes
        /// red, and the aimed socket that would actually take this module goes green.
        /// </summary>
        public void Refresh(ShipPartKind kind, Ray aim,
                            float ghostRange, float installRange, float aimMargin)
        {
            Clear();

            Resolve(kind, aim, installRange, aimMargin);

            float ghostRangeSq = ghostRange * ghostRange;

            foreach (ShipPartRack rack in ShipPartRack.Active)
            {
                if (rack == null) continue;
                if ((rack.transform.position - aim.origin).sqrMagnitude > ghostRangeSq) continue;

                IReadOnlyList<ShipPartSocket> sockets = rack.Sockets;

                for (int i = 0; i < sockets.Count; i++)
                {
                    ShipPartSocket socket = sockets[i];
                    if (socket == null || rack.IsInstalled(i)) continue;

                    socket.SetGhost(socket == Aimed ? ShipPartGhost.Target : ShipPartGhost.Missing);
                    painted.Add(socket);
                }
            }
        }

        /// <summary>Put every socket this highlighter lit back the way it found it.</summary>
        public void Clear()
        {
            foreach (ShipPartSocket socket in painted)
                if (socket != null)
                    socket.SetGhost(ShipPartGhost.Off);

            painted.Clear();
            Aimed = null;
            AimedRack = null;
            AimedIndex = -1;
        }

        /// <summary>
        /// Which socket the aim is on, tested analytically against each empty socket's own bounds
        /// rather than with a physics cast.
        ///
        /// <para>
        /// A cast would be the obvious choice and is the wrong one here. The part's collider is
        /// <em>disabled</em> while the socket is empty — that is what makes the hole a hole — so
        /// a cast finds only the hull slabs behind it, and adding trigger volumes to eleven
        /// sockets per ship to give the cast something to hit puts eleven new answers into every
        /// other query in the game. A bounds test asks the question directly, allocates nothing,
        /// and keeps working when the player is standing inside the volume, which a ray does not.
        /// </para>
        /// <para>
        /// Occlusion is deliberately not tested. The bounds are also grown by
        /// <paramref name="aimMargin"/>: the skill this loop is testing is finding the module, not
        /// hitting a nacelle from thirty metres (GDC-L1-FEEL-0003).
        /// </para>
        /// </summary>
        private void Resolve(ShipPartKind kind, Ray aim, float installRange, float aimMargin)
        {
            float bestDistance = installRange;

            foreach (ShipPartRack rack in ShipPartRack.Active)
            {
                if (rack == null) continue;

                IReadOnlyList<ShipPartSocket> sockets = rack.Sockets;

                for (int i = 0; i < sockets.Count; i++)
                {
                    ShipPartSocket socket = sockets[i];
                    if (socket == null || socket.Kind != kind) continue;
                    if (!rack.Accepts(i, kind)) continue;

                    Bounds bounds = socket.AimBounds;
                    bounds.Expand(aimMargin * 2f);

                    if (!bounds.IntersectRay(aim, out float distance)) continue;

                    // Inside the volume the ray reports a negative entry distance. That is a hit at
                    // arm's length, not a hit behind the player.
                    distance = Mathf.Max(0f, distance);
                    if (distance > bestDistance) continue;

                    bestDistance = distance;
                    Aimed = socket;
                    AimedRack = rack;
                    AimedIndex = i;
                }
            }
        }
    }
}
