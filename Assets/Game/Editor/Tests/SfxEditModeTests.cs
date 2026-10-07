// A sound asked for outside Play Mode stays silent and says nothing.
//
// FMOD's RuntimeManager exists only in Play Mode and logs an ERROR when it is reached outside it.
// Any EditMode test whose action happens to make a sound — throwing a lasso, firing a gun — then
// fails on that log rather than on anything it was testing.
using NUnit.Framework;
using SpaceGame.Audio;
using UnityEngine.TestTools;

namespace SpaceGame.EditorTools
{
    public class SfxEditModeTests
    {
        [Test]
        public void PlayingASoundInEditModeLogsNoError()
        {
            // An id the AudioCatalog assigns an event to, played without a position: a positioned
            // play is culled before FMOD whenever the open scene's listener is out of range, and
            // then this would pass without ever reaching the RuntimeManager.
            Sfx.Play2D(SfxId.RopeThrow);

            LogAssert.NoUnexpectedReceived();
        }
    }
}
