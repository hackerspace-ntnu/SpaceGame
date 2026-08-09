using NUnit.Framework;

public class MatchRulesTests
{
    [Test]
    public void TeamDeathmatch_KeepsTheHostsSelectedWinCondition()
    {
        Assert.AreEqual(WinCondition.KillTarget,
            MatchRules.ResolveCondition(MatchGameMode.TeamDeathmatch, WinCondition.KillTarget, soloLives: 3));
        Assert.AreEqual(WinCondition.LivesPerPlayer,
            MatchRules.ResolveCondition(MatchGameMode.TeamDeathmatch, WinCondition.LivesPerPlayer, soloLives: 3));
        Assert.AreEqual(WinCondition.LastStanding,
            MatchRules.ResolveCondition(MatchGameMode.TeamDeathmatch, WinCondition.LastStanding, soloLives: 3));
    }

    [Test]
    public void BattleRoyale_IsAlwaysLastStandingWithOneLife()
    {
        Assert.AreEqual(WinCondition.LastStanding,
            MatchRules.ResolveCondition(MatchGameMode.BattleRoyale, WinCondition.KillTarget, soloLives: 5));
        Assert.AreEqual(1,
            MatchRules.ResolveLives(MatchGameMode.BattleRoyale, WinCondition.KillTarget, teamLives: 4, soloLives: 5));
    }

    [Test]
    public void FreeForAll_WithMultipleLives_RunsAsLivesPerPlayer()
    {
        Assert.AreEqual(WinCondition.LivesPerPlayer,
            MatchRules.ResolveCondition(MatchGameMode.FreeForAll, WinCondition.KillTarget, soloLives: 3));
        Assert.AreEqual(3,
            MatchRules.ResolveLives(MatchGameMode.FreeForAll, WinCondition.KillTarget, teamLives: 9, soloLives: 3));
    }

    [Test]
    public void FreeForAll_WithASingleLife_CollapsesToLastStanding()
    {
        Assert.AreEqual(WinCondition.LastStanding,
            MatchRules.ResolveCondition(MatchGameMode.FreeForAll, WinCondition.LivesPerPlayer, soloLives: 1));
    }

    [Test]
    public void RespawnsAreEnabledForEveryConditionExceptLastStanding()
    {
        Assert.IsTrue(MatchRules.RespawnsEnabled(WinCondition.KillTarget));
        Assert.IsTrue(MatchRules.RespawnsEnabled(WinCondition.LivesPerPlayer));
        Assert.IsFalse(MatchRules.RespawnsEnabled(WinCondition.LastStanding));
    }

    [Test]
    public void TeamDeathmatchLives_OnlyApplyWhenLivesPerPlayerIsSelected()
    {
        Assert.AreEqual(4,
            MatchRules.ResolveLives(MatchGameMode.TeamDeathmatch, WinCondition.LivesPerPlayer, teamLives: 4, soloLives: 9));
        Assert.AreEqual(1,
            MatchRules.ResolveLives(MatchGameMode.TeamDeathmatch, WinCondition.LastStanding, teamLives: 4, soloLives: 9));
    }

    [Test]
    public void OnlyTeamDeathmatchUsesTeams()
    {
        Assert.IsTrue(MatchRules.UsesTeams(MatchGameMode.TeamDeathmatch));
        Assert.IsFalse(MatchRules.UsesTeams(MatchGameMode.FreeForAll));
        Assert.IsFalse(MatchRules.UsesTeams(MatchGameMode.BattleRoyale));
    }

    [Test]
    public void BotCounts_AreClampedToTheAuthoredFactionPool()
    {
        Assert.AreEqual(MatchRules.MaxTeamBotsPerSide, MatchRules.ClampTeamBots(99));
        Assert.AreEqual(0, MatchRules.ClampTeamBots(-3));

        // One solo faction is always held back for the human host.
        Assert.AreEqual(MatchRules.SoloFactionCount - 1, MatchRules.MaxSoloBots);
        Assert.AreEqual(MatchRules.MaxSoloBots, MatchRules.ClampSoloBots(99));
        Assert.AreEqual(1, MatchRules.ClampSoloBots(0));
    }

    [Test]
    public void KillTargetAndLives_AreClampedToTheirConfigurableRanges()
    {
        Assert.AreEqual(MatchRules.MaxKillTarget, MatchRules.ClampKillTarget(1000));
        Assert.AreEqual(MatchRules.MinKillTarget, MatchRules.ClampKillTarget(0));
        Assert.AreEqual(MatchRules.MaxLives, MatchRules.ClampLives(50));
        Assert.AreEqual(MatchRules.MinLives, MatchRules.ClampLives(0));
    }

    [Test]
    public void SoloLives_AreClampedBeforeDecidingTheCondition()
    {
        // A soloLives of 0 is out of range, not "no lives" — it clamps up to 1
        // and the match runs as last-standing rather than ending instantly.
        Assert.AreEqual(WinCondition.LastStanding,
            MatchRules.ResolveCondition(MatchGameMode.FreeForAll, WinCondition.LastStanding, soloLives: 0));
        Assert.AreEqual(1,
            MatchRules.ResolveLives(MatchGameMode.FreeForAll, WinCondition.LastStanding, teamLives: 3, soloLives: 0));
    }
}
