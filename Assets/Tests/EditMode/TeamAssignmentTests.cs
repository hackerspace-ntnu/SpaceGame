using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class TeamAssignmentTests
{
    [Test]
    public void SplitEvenly_TwoTeams_DividesPositionsInHalf()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0),
            new Vector3(2, 0, 0), new Vector3(3, 0, 0),
        };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        Assert.AreEqual(2, teams.Count);
        Assert.AreEqual(2, teams[0].Count);
        Assert.AreEqual(2, teams[1].Count);
    }

    [Test]
    public void SplitEvenly_UsesEveryPositionExactlyOnce()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0),
            new Vector3(3, 0, 0), new Vector3(4, 0, 0), new Vector3(5, 0, 0),
        };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        var all = new List<Vector3>();
        foreach (var team in teams) all.AddRange(team);

        Assert.AreEqual(positions.Count, all.Count);
        foreach (var p in positions)
            Assert.Contains(p, all);
    }

    [Test]
    public void SplitEvenly_OddCount_DistributesRemainderToEarlierTeams()
    {
        var positions = new List<Vector3>
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0),
        };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 2);

        Assert.AreEqual(2, teams[0].Count);
        Assert.AreEqual(1, teams[1].Count);
    }

    [Test]
    public void SplitEvenly_FewerPositionsThanTeams_SomeTeamsGetEmptyList()
    {
        var positions = new List<Vector3> { new Vector3(0, 0, 0) };

        var teams = TeamAssignment.SplitEvenly(positions, teamCount: 3);

        Assert.AreEqual(3, teams.Count);
        int nonEmpty = 0;
        foreach (var t in teams) if (t.Count > 0) nonEmpty++;
        Assert.AreEqual(1, nonEmpty);
    }
}
