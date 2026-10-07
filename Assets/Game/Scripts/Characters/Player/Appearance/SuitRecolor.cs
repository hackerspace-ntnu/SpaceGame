using System.Collections.Generic;
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Characters
{
    /// <summary>
    /// Paints an astronaut in a chosen suit colour.
    ///
    /// <para>
    /// Deliberately knows nothing about netcode, the lobby, or settings. The same component runs on
    /// the networked player body and on the four figures standing in the lobby, and the only thing
    /// either of them does is call <see cref="PaletteRecolor.Apply"/> with an index.
    /// <c>PlayerIdentity</c> supplies it from a NetworkVariable in the world; the lobby supplies it
    /// from what the player is pressing right now.
    /// </para>
    ///
    /// <para>
    /// Everything about HOW a model is painted — property blocks rather than material instances,
    /// matching by name, the linear conversion on upload — is in <see cref="PaletteRecolor"/>, which
    /// this and the ship livery share. All that is left here is which table to use and what an index
    /// means.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class SuitRecolor : PaletteRecolor
    {
        protected override IReadOnlyList<SuitPalette.Relationship> Relationships =>
            SuitPalette.Relationships;

        /// <summary>
        /// Always a colour: every index means a swatch once clamped, so an astronaut is never left
        /// unpainted. The ship livery is the case where an index can mean "no colour yet".
        /// </summary>
        protected override bool TryColorOf(int index, out Color chosen)
        {
            chosen = SuitPalette.ColorOf(index);
            return true;
        }

        /// <summary>
        /// Clamped rather than rejected: this value arrives from a peer's NetworkVariable and from a
        /// save, and a build that has seen a longer palette must not be able to throw.
        /// </summary>
        protected override int Normalise(int index) => SuitPalette.Clamp(index);

        protected override string NothingToPaintMessage =>
            $"[SuitRecolor] '{name}' has no material matching SuitPalette.Relationships, so the " +
            "suit colour will do nothing. The model's materials were probably renamed by a Blender " +
            "re-export — SuitPaletteTests asserts the expected names and will say which.";
    }
}
