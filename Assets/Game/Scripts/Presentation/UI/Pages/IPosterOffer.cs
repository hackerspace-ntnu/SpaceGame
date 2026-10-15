using UnityEngine;

namespace SpaceGame.Presentation
{
    /// <summary>
    /// Something a poster held up in <see cref="PosterViewerUI"/> offers the reader: a bounty to take, or the news that it
    /// has ended (CLAIMED, EXPIRED). Read every frame the sheet is up, so it may change while the player reads.
    /// </summary>
    public interface IPosterOffer
    {
        /// <summary>What the sheet is stamped with (CLAIMED, EXPIRED); empty for no stamp.</summary>
        string Stamp { get; }

        /// <summary>The ink of <see cref="Stamp"/>, so the sheet in hand matches the one on the wall.</summary>
        Color StampColour { get; }

        /// <summary>The line under the sheet; empty for none.</summary>
        string Prompt { get; }

        /// <summary>Pressing Y takes it now.</summary>
        bool CanTake { get; }

        void Take();
    }
}
