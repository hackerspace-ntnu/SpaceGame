using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Vehicles
{
    /// <summary>
    /// Carries which team a ship belongs to, and paints it in that team's colour on every machine.
    ///
    /// <para>
    /// <b>The swatch is replicated, not the team index.</b> Deriving the colour locally from
    /// <c>VersusSession.ColorOf(team)</c> would work on every peer that adopted the same lobby — and
    /// would put a ship in the wrong livery on any peer that did not, silently, in the one mode
    /// where colour is how you tell friend from enemy. One int on the wire removes the whole class
    /// of disagreement, and the team index itself is a server-side bookkeeping detail no client has
    /// a use for.
    /// </para>
    ///
    /// <para>
    /// Server-write, like <c>PlayerIdentity</c>'s team and for the same reason: which side a ship
    /// belongs to is decided by the spawn flow before anyone can see it, and an owner-written value
    /// would let a client repaint the opposition.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ShipAccentRecolor))]
    public class ShipTeamAccent : NetworkBehaviour
    {
        /// <summary>
        /// The swatch this hull is painted in, as an index into <c>SuitPalette.Swatches</c>, or
        /// <see cref="ShipAccentPalette.NoTeam"/> for a ship nobody has claimed.
        ///
        /// Starting at the sentinel rather than at zero is what keeps a story-world lander in its
        /// authored paint instead of flashing into the first team's colour on spawn — the same
        /// distinction <c>PlayerIdentity.suitColor</c> makes.
        /// </summary>
        private readonly NetworkVariable<int> swatch = new(
            ShipAccentPalette.NoTeam, NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        [Tooltip("The component that paints this hull. Found in children when unset.")]
        [SerializeField] private ShipAccentRecolor recolor;

        /// <summary>The swatch this hull wears, or <see cref="ShipAccentPalette.NoTeam"/>.</summary>
        public int Swatch => swatch.Value;

        /// <summary>
        /// Paints this hull in a team's colour. Server-only, because the write would not replicate
        /// from anywhere else — and a ship that quietly stayed unpainted looks exactly like nobody
        /// having called this.
        /// </summary>
        public void SetSwatch(int swatchIndex)
        {
            if (!IsServer)
            {
                Debug.LogWarning($"[Net] '{name}' was told to wear swatch {swatchIndex} by a machine " +
                                 "that is not the server. Ignored — the write would not have " +
                                 "replicated.", this);
                return;
            }

            swatch.Value = swatchIndex;
        }

        public override void OnNetworkSpawn()
        {
            swatch.OnValueChanged += OnSwatchChanged;

            // Applied from whatever has already replicated rather than waiting for a change event: a
            // client that receives this ship as part of its spawn payload — every client joining a
            // match already in progress — has had that event before it existed.
            ApplySwatch();
        }

        public override void OnNetworkDespawn() => swatch.OnValueChanged -= OnSwatchChanged;

        private void OnSwatchChanged(int previous, int current) => ApplySwatch();

        private void ApplySwatch()
        {
            if (recolor == null) recolor = GetComponentInChildren<ShipAccentRecolor>(true);

            if (recolor == null)
            {
                Debug.LogError($"[ShipTeamAccent] '{name}' has no ShipAccentRecolor, so its team " +
                               "colour has nowhere to go.", this);
                return;
            }

            recolor.Apply(swatch.Value);
        }
    }
}
