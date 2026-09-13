using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Presentation.Lobbies
{
    /// <summary>
    /// Takes the menu's decorative astronauts out of the shot while the lobby rank is up, and puts
    /// them back on teardown.
    ///
    /// <para>
    /// The lobby's authored view is yawed away from them, so at the composed distance they are out
    /// of frame — but <see cref="LobbyPreviewCamera.Fit"/> backs the camera off along its own axis
    /// for a big rank, and past a few teams that pull-back walks the set dressing right into the
    /// foreground, where two menu props stand in front of the roster. Hiding them is deterministic
    /// where more camera work is not, and invisible in the small-rank case where they were never
    /// in frame to begin with.
    /// </para>
    ///
    /// <para>
    /// Matched by name prefix among the scene's ROOT objects only, because they are scene-authored
    /// prefab instances with nothing else distinguishing them — and because the rank's own figures
    /// contain an <c>AstronautArmature</c> node inside their hierarchy, which a deep search would
    /// hide too. Only what THIS class hid is restored, so a figure someone disabled in the scene on
    /// purpose stays disabled.
    /// </para>
    /// </summary>
    internal sealed class LobbySetDressing
    {
        /// <summary>The decorative figures in MainMenu.unity: AstronautArmature, (1), (2).</summary>
        private const string DressingPrefix = "AstronautArmature";

        private readonly List<GameObject> hidden = new();

        public void Hide()
        {
            foreach (GameObject root in
                     UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.activeSelf || !root.name.StartsWith(DressingPrefix)) continue;

                root.SetActive(false);
                hidden.Add(root);
            }
        }

        public void Restore()
        {
            foreach (GameObject dressing in hidden)
                if (dressing != null) dressing.SetActive(true);

            hidden.Clear();
        }
    }
}
