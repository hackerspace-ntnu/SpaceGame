using System.Collections.Generic;
using NUnit.Framework;

public class SpawnReachabilityTests
{
    // Builds a reachability predicate from explicit islands: two indices are
    // mutually reachable exactly when they share an island.
    private static System.Func<int, int, bool> Islands(params int[][] islands)
    {
        var islandOf = new Dictionary<int, int>();
        for (int i = 0; i < islands.Length; i++)
            foreach (int index in islands[i])
                islandOf[index] = i;

        return (a, b) => islandOf.TryGetValue(a, out int ia)
                         && islandOf.TryGetValue(b, out int ib)
                         && ia == ib;
    }

    [Test]
    public void AllPointsConnected_ReturnsEveryPoint()
    {
        var group = SpawnReachability.LargestConnectedGroup(5, (a, b) => true);
        Assert.AreEqual(5, group.Count);
    }

    [Test]
    public void PicksTheLargestIsland_AndDropsTheRest()
    {
        var group = SpawnReachability.LargestConnectedGroup(7,
            Islands(new[] { 0, 2 }, new[] { 1, 3, 4, 6 }, new[] { 5 }));

        CollectionAssert.AreEquivalent(new[] { 1, 3, 4, 6 }, group);
    }

    [Test]
    public void ChainedReachability_MergesIntoOneComponent()
    {
        // 0-1 and 1-2 connect, 0-2 does not test true directly. Union-find must
        // still merge all three rather than splitting on the failing pair.
        bool CanReach(int a, int b)
        {
            int low = a < b ? a : b;
            int high = a < b ? b : a;
            return (low == 0 && high == 1) || (low == 1 && high == 2);
        }

        var group = SpawnReachability.LargestConnectedGroup(3, CanReach);
        CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, group);
    }

    [Test]
    public void NothingConnected_ReturnsASinglePoint()
    {
        var group = SpawnReachability.LargestConnectedGroup(4, (a, b) => false);
        Assert.AreEqual(1, group.Count);
    }

    [Test]
    public void EqualSizedIslands_ResolveDeterministicallyToTheEarlierOne()
    {
        var group = SpawnReachability.LargestConnectedGroup(4, Islands(new[] { 2, 3 }, new[] { 0, 1 }));
        CollectionAssert.AreEquivalent(new[] { 0, 1 }, group);
    }

    [Test]
    public void EmptyInput_ReturnsEmpty()
    {
        Assert.AreEqual(0, SpawnReachability.LargestConnectedGroup(0, (a, b) => true).Count);
    }

    [Test]
    public void SinglePoint_IsItsOwnGroup()
    {
        var group = SpawnReachability.LargestConnectedGroup(1, (a, b) => false);
        CollectionAssert.AreEqual(new[] { 0 }, group);
    }
}
