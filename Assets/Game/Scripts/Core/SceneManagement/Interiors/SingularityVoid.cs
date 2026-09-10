// The identity of the one interior nobody walks into.
//
// A scene name and an anchor id, in one place, because three things need to agree about them and
// none of them can see the other two: the editor builder that generates the scene and the
// InteriorScene asset (SingularityVoidBuilder), the artifact that sends bodies there
// (SingularityWell), and the guard that gets a stranded player back out (SingularityVoidGuard).
//
// Constants rather than values read off the InteriorScene asset, because the guard runs in sessions
// where no singularity has ever been thrown and nothing has loaded that asset.
namespace SpaceGame.Core
{
    /// <summary>The white nowhere a swallowed body is held. See the BottledSingularity system doc.</summary>
    public static class SingularityVoid
    {
        /// <summary>
        /// The scene's own name, not its path. Matches the file the builder writes, and is both
        /// what <see cref="UnityEngine.SceneManagement.Scene.name"/> answers and what
        /// <see cref="InteriorScene.SceneName"/> holds.
        /// </summary>
        public const string SceneName = "SingularityVoid";

        /// <summary>The <see cref="InteriorAnchor"/> id inside that scene, where an occupant lands.</summary>
        public const string AnchorId = "void";

        /// <summary>
        /// Where the <see cref="InteriorScene"/> asset lives, relative to a Resources folder.
        ///
        /// The well carries a direct reference and never uses this; it is here so the builder and
        /// the tests name the same asset, and so a well that has lost its wiring can say in one
        /// sentence what is missing and where it was supposed to be.
        /// </summary>
        public const string ResourcePath = "Interiors/Interior_SingularityVoid";
    }
}
