// Marker placed on a rock, wall, or ruin that entities can hide behind.
// Self-registers in CoverPointRegistry on enable so CoverModule never calls FindObjectsByType.
// Just drop this on any cover object in the scene — no configuration needed.
using System.Collections.Generic;
using UnityEngine;

public class CoverPoint : MonoBehaviour
{
    [Tooltip("How many entities can use this cover simultaneously.")]
    [SerializeField] private int maxOccupants = 1;

    private int currentOccupants;

    public bool IsAvailable => currentOccupants < maxOccupants;
    public Vector3 Position => transform.position;

    private void OnEnable()  => CoverPointRegistry.Register(this);
    private void OnDisable() => CoverPointRegistry.Unregister(this);

    public bool TryOccupy()
    {
        if (!IsAvailable)
            return false;
        currentOccupants++;
        return true;
    }

    public void Vacate()
    {
        currentOccupants = Mathf.Max(0, currentOccupants - 1);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsAvailable ? Color.green : Color.red;
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.4f);
    }
}

// O(1) registry — mirrors EntityTargetRegistry and HerdModule patterns.
public static class CoverPointRegistry
{
    private static readonly List<CoverPoint> s_all = new();

    public static void Register(CoverPoint cp)
    {
        if (!s_all.Contains(cp))
            s_all.Add(cp);
    }

    public static void Unregister(CoverPoint cp) => s_all.Remove(cp);

    // Returns the best available cover relative to 'self' within 'searchRadius', given 'threatPos'.
    // Returns null if nothing is available.
    public static CoverPoint FindBest(Vector3 self, Vector3 threatPos, float searchRadius)
    {
        CoverPoint best = null;
        float bestScore = float.MinValue;

        foreach (CoverPoint cp in s_all)
        {
            if (!cp || !cp.IsAvailable)
                continue;

            float distFromSelf = Vector3.Distance(self, cp.Position);
            if (distFromSelf > searchRadius)
                continue;

            float distFromThreat = Vector3.Distance(threatPos, cp.Position);
            float score = distFromThreat - distFromSelf * 0.5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = cp;
            }
        }

        return best;
    }
}
