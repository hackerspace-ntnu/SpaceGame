using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Persistence;

namespace SpaceGame.World
{
    /// <summary>
    /// The ship the run is won at: hand it enough scrap and it leaves with everyone aboard.
    ///
    /// <para>
    /// <see cref="IPersistentEntity"/> because the count below is the game's win condition and the
    /// hull carries none of the components <c>SaveablePolicy.NeedsSaving</c> otherwise looks for.
    /// Without the marker a player could hand over two of the three pieces of scrap, save, load,
    /// and find both the deposits and the scrap itself gone.
    /// </para>
    /// </summary>
    public class Ship : MonoBehaviour, IPersistentEntity
    {
        private int scrapAmount = 0;
        private int scrapToWin = 3;

        /// <summary>How much scrap has been handed over, on the machine that counts it.</summary>
        public int ScrapCollected => scrapAmount;

        /// <summary>How much it takes to win.</summary>
        public int ScrapToWin => scrapToWin;

        /// <summary>
        /// Takes one piece of scrap.
        ///
        /// The count belongs to whoever simulates the ship, and today only that machine can get
        /// here — ShipInteraction hands every deposit to the server through <c>Network.Execute</c>,
        /// so a client boarding the ship still adds to the one count that matters. The guard is what
        /// stops a second caller from undoing that: a client keeping its own tally would reach three
        /// on its own and try to end the run for everybody from a machine not allowed to decide it.
        ///
        /// <c>Simulates</c> rather than a bare server test, per the project's rule: a ship placed in
        /// a scene with no NetworkObject over it has no wire at all, the deposit was dispatched
        /// locally, and this machine is its only authority. A server test would refuse it forever.
        /// </summary>
        public void AddScrap()
        {
            if (!Network.Simulates(this)) return;

            scrapAmount += 1;
            Debug.Log($"[Ship] Scrap deposited ({scrapAmount}/{scrapToWin}).");
            CheckWin();
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Deliberately does NOT re-check the win. Whether the run was already over is the game
        /// state's business, restored by <c>GameStateSaveable</c>, and a ship that re-won on
        /// load would send a player who saved on the last piece of scrap straight back to the win
        /// scene the moment their world finished loading.
        /// </summary>
        public void RestoreScrap(int collected)
        {
            scrapAmount = Mathf.Max(0, collected);
        }

        private void CheckWin()
        {
            if (scrapAmount < scrapToWin) return;
            if (!GameManager.Instance) return;

            GameManager.Instance.WinGame();
        }
    }
}
