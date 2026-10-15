using System;
using System.IO;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>One clip to cut out of a take: a name, a start and an end in SECONDS, and whether it cycles.</summary>
    [Serializable]
    public sealed class ClipCut
    {
        public string take;
        public string name;
        public float start;
        public float end;
        public bool loop;

        /// <summary>
        /// The take climbs: its height gain is travel, not pose, and would pop back every cycle of a
        /// loop. Left out of the pose, so the body stays at standing height.
        /// </summary>
        public bool travelsVertically;
    }

    /// <summary>
    /// The <c>cuts.json</c> shared by the clip importers: per take (FBX file name without extension)
    /// the clips to cut from it. Seconds, not frames, so the analysis that produced the numbers and
    /// the importer cannot disagree about a take's sample rate.
    /// </summary>
    [Serializable]
    public sealed class ClipCutList
    {
        public ClipCut[] cuts = Array.Empty<ClipCut>();

        public static ClipCutList Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"[ClipCutList] {path} is missing; its takes need their cuts.");
            return JsonUtility.FromJson<ClipCutList>(File.ReadAllText(path));
        }
    }
}
