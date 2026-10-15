namespace SpaceGame.World
{
    /// <summary>Where the workshop's story stands. Written into save files by name: NEVER rename a value.</summary>
    public enum WorkshopMissionPhase : byte
    {
        /// <summary>The gate is shut. The Shellback who got out waits in front of it and begs for help.</summary>
        Locked,

        /// <summary>The gate is open. Everyone gathers in the middle of the main room to meet whoever opened it.</summary>
        Gathering,

        /// <summary>The player has been asked to get the stolen gear back; the thieves are out in the world.</summary>
        Hunting,

        /// <summary>
        /// Only in saves from before the vault: the gear was home and the story was over. Read back as
        /// <see cref="Vault"/>, so such a world goes on to the vault; the mission never enters it.
        /// </summary>
        Done,

        /// <summary>The gear is home. The Shellbacks offer the reactor core in their vault, behind the turning rooms;
        /// the player fetches it and lowers it onto their ship with the hoist.</summary>
        Vault,

        /// <summary>The core is fitted to the ship. The story is over.</summary>
        Repaired,
    }
}
