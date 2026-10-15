// The headless runner decides, before every run, what to do about scenes with unsaved changes —
// because the Test Framework's first step asks with a MODAL dialog, and a modal dialog in an
// editor shared by several MCP sessions blocks all of them until someone at the keyboard answers.
using NUnit.Framework;

namespace SpaceGame.EditorTools
{
    public class HeadlessTestRunnerTests
    {
        [Test]
        public void ACleanSceneLetsTheRunProceed()
        {
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.Proceed,
                            HeadlessTestRunner.PreRunActionFor(false, "Assets/Game/Scenes/Core/Bootstrap.unity"));
        }

        [Test]
        public void ACleanUntitledSceneLetsTheRunProceed()
        {
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.Proceed,
                            HeadlessTestRunner.PreRunActionFor(false, string.Empty));
        }

        [Test]
        public void ADirtyUntitledSceneIsScratchAndIsDiscarded()
        {
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.DiscardScratch,
                            HeadlessTestRunner.PreRunActionFor(true, string.Empty));
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.DiscardScratch,
                            HeadlessTestRunner.PreRunActionFor(true, null));
        }

        [Test]
        public void ADirtySavedSceneAbortsTheRunRatherThanPromptingOrSaving()
        {
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.Abort,
                            HeadlessTestRunner.PreRunActionFor(true, "Assets/Game/Scenes/world/persistentScene.unity"));
        }

        [Test]
        public void TheWorstSceneDecidesForTheWholeSetup()
        {
            // Discarding the scratch reopens Bootstrap in Single mode, which would also throw away a
            // dirty saved scene loaded beside it — so any dirty saved scene must win.
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.Abort,
                            HeadlessTestRunner.Worse(HeadlessTestRunner.PreRunAction.DiscardScratch,
                                                     HeadlessTestRunner.PreRunAction.Abort));
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.DiscardScratch,
                            HeadlessTestRunner.Worse(HeadlessTestRunner.PreRunAction.Proceed,
                                                     HeadlessTestRunner.PreRunAction.DiscardScratch));
            Assert.AreEqual(HeadlessTestRunner.PreRunAction.Proceed,
                            HeadlessTestRunner.Worse(HeadlessTestRunner.PreRunAction.Proceed,
                                                     HeadlessTestRunner.PreRunAction.Proceed));
        }

        [Test]
        public void AnAssemblyCompiledFromAssetsIsOurs()
        {
            Assert.IsTrue(HeadlessTestRunner.IsProjectAssembly(new[] { "Assets/Game/Editor/Tests/FormationMathTests.cs" }));
            Assert.IsTrue(HeadlessTestRunner.IsProjectAssembly(new[] { @"Assets\Game\Tests\EditMode\WalkerTestRig.cs" }));
        }

        [Test]
        public void AnEmbeddedPackagesTestsAreNotOurs()
        {
            // com.unity.netcode.gameobjects is embedded under Packages/, and its BuildTests build a
            // player mid-run — the reason an unfiltered run is narrowed to this project.
            Assert.IsFalse(HeadlessTestRunner.IsProjectAssembly(
                new[] { "Packages/com.unity.netcode.gameobjects/Tests/Editor/Build/BuildTests.cs" }));
        }

        [Test]
        public void TheAbortLineNamesTheSceneAndStillEndsInDone()
        {
            string result = HeadlessTestRunner.DirtySceneAbortResult("Assets/Game/Scenes/world/persistentScene.unity");

            StringAssert.StartsWith("ABORTED=dirty-scene Assets/Game/Scenes/world/persistentScene.unity", result);
            StringAssert.EndsWith("DONE" + System.Environment.NewLine, result);
        }
    }
}
