// One of the Shellbacks who were shut inside the workshop. They appear when the gate opens, stand in a row in the main
// hall to meet whoever opened it, and afterwards keep to their own spots round the hall, turning to a player who comes
// near. Talked to, each says what fits the moment.
using UnityEngine;

namespace SpaceGame.World
{
    [DisallowMultipleComponent]
    public sealed class WorkshopFamilyMember : WorkshopShellback
    {
        [Header("Lines")]
        [Tooltip("Said in turn while everyone gathers to meet the player.")]
        [SerializeField, TextArea(2, 4)] private string[] gathering =
        {
            "You opened it! We thought we'd never see the sky again.",
            "They came in the night - we hid in the loft while they tore the place apart.",
        };

        [Tooltip("Said in turn while the thieves still have the gear.")]
        [SerializeField, TextArea(2, 4)] private string[] hunting =
        {
            "Please, bring our Heart Gear back. The engines won't turn without it.",
            "One of them had a limp. Can't have got far with a cog that heavy.",
            "They went out the ship lane. Look for what they dropped - that old cart of theirs was falling apart.",
        };

        [Tooltip("Said in turn when talked to by somebody who has the gear.")]
        [SerializeField, TextArea(2, 4)] private string[] carrying =
        {
            "You've got it! Take it to the middle of the hall!",
        };

        [Tooltip("Said in turn while the vault's reactor core is still to be fetched and fitted.")]
        [SerializeField, TextArea(2, 4)] private string[] vault =
        {
            "Our vault's past the turning rooms. Grandmother hid it well - you'll have to turn a few to find it.",
            "Mind the hoist when you lower the core. Gently does it, or you'll dent your own ship.",
        };

        [Tooltip("Said in turn once the gear is home and the core is fitted.")]
        [SerializeField, TextArea(2, 4)] private string[] afterwards =
        {
            "Hear that hum? That's the Heart Gear turning again.",
            "You're a friend of this workshop now. Never forget it.",
        };

        protected override string NextLine(WorkshopMission at) => at.Phase switch
        {
            WorkshopMissionPhase.Hunting => Turn(at, at.GearCarried ? carrying : hunting),
            WorkshopMissionPhase.Vault => Turn(at, vault),
            WorkshopMissionPhase.Repaired => Turn(at, afterwards),
            _ => Turn(at, gathering),
        };
    }
}
