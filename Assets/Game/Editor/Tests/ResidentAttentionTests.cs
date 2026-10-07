// A worker that turns to look at a player puts its work down, and picks it up a moment after it looks away.
using NUnit.Framework;
using SpaceGame.Agents.Residents;

namespace SpaceGame.EditorTools
{
    public class ResidentAttentionTests
    {
        private const float Resume = 2.5f;

        [Test]
        public void AWorkerLooksUpTheMomentItLooksAtAPlayer()
        {
            float until = float.NegativeInfinity;
            Assert.IsFalse(ResidentAttention.HandsOff(Activity.Work, false, 0f, Resume, ref until), "working, nobody noticed");
            Assert.IsTrue(ResidentAttention.HandsOff(Activity.Work, true, 1f, Resume, ref until), "looking at a player");
        }

        [Test]
        public void TheWorkResumesOnlyAfterTheResumeDelay()
        {
            float until = float.NegativeInfinity;
            ResidentAttention.HandsOff(Activity.Work, true, 10f, Resume, ref until);

            Assert.IsTrue(ResidentAttention.HandsOff(Activity.Work, false, 11f, Resume, ref until), "just looked away");
            Assert.IsTrue(ResidentAttention.HandsOff(Activity.Work, false, 12.4f, Resume, ref until), "still inside the delay");
            Assert.IsFalse(ResidentAttention.HandsOff(Activity.Work, false, 12.6f, Resume, ref until), "delay over, back to work");
        }

        [Test]
        public void AGlanceEveryFewSecondsIsOneLongPauseNotAStutter()
        {
            // Looking during the first 3 s of every 10 (the last look sample of each is at 2.5 s).
            float until = float.NegativeInfinity;
            for (float t = 0f; t < 30f; t += 0.5f)
            {
                float into = t % 10f;
                bool off = ResidentAttention.HandsOff(Activity.Chore, into < 3f, t, Resume, ref until);
                if (t >= 3f && into < 2.5f + Resume) Assert.IsTrue(off, $"t={t}: bridged by the resume delay");
                if (t >= 3f && into >= 2.5f + Resume) Assert.IsFalse(off, $"t={t}: back at work");
            }
        }

        [Test]
        public void OnlyWorkersLetGo()
        {
            foreach (Activity pose in new[] { Activity.Sleep, Activity.Sitting, Activity.Hearth, Activity.Climbing, Activity.Walking, Activity.Stalking })
            {
                float until = float.NegativeInfinity;
                Assert.IsFalse(ResidentAttention.HandsOff(pose, true, 0f, Resume, ref until), $"{pose} keeps its pose");
            }
        }
    }
}
