using System;
using UnityEngine;

namespace SpaceGame.EditorTools.Outposts
{
    /// <summary>
    /// The outposts as <c>raxy_outposts_export.py</c> wrote them: each one a list of finished decoration pieces and where
    /// each stands, in Unity axes, relative to the outpost's footprint centre on the ground. Plain data: the builder turns
    /// every row into an instance of the piece's own prefab.
    /// </summary>
    [Serializable]
    public sealed class OutpostLayoutFile
    {
        public OutpostLayout[] outposts = Array.Empty<OutpostLayout>();

        public static OutpostLayoutFile Parse(string json) => JsonUtility.FromJson<OutpostLayoutFile>(json);
    }

    [Serializable]
    public sealed class OutpostLayout
    {
        public string name;
        public string source;
        public OutpostPiece[] pieces = Array.Empty<OutpostPiece>();
    }

    [Serializable]
    public sealed class OutpostPiece
    {
        /// <summary>The decoration this is: <c>Seat_Clay</c> for <c>Deco_Seat_Clay</c>.</summary>
        public string kind;
        public string name;
        public float[] position;
        public float[] rotation;
        public float[] scale;

        public Vector3 Position => new Vector3(position[0], position[1], position[2]);
        public Quaternion Rotation => new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        public Vector3 Scale => new Vector3(scale[0], scale[1], scale[2]);
    }
}
