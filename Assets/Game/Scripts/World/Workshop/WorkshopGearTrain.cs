using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// The quantities the workshop's moving parts follow. Every shaft, gear and disc turns as a fixed ratio of
    /// exactly one of them, which is what keeps meshing teeth meshed without anything simulating a gear.
    /// Numbers are serialized on the prefab: append only.
    /// </summary>
    public enum WorkshopChannel : byte
    {
        /// <summary>The gear wall's motor, which never stops: units run since the workshop woke.</summary>
        Idle = 1,

        /// <summary>Metres the hoist hook hangs below its parked height.</summary>
        Crane = 5,

        /// <summary>Seconds, for the parts that simply run.</summary>
        Ambient = 9,

        /// <summary>Metres the hoist's bridge and trolley have travelled from where they are modelled. Its hook is <see cref="Crane"/>.</summary>
        HoistBridge = 16,
        HoistTrolley = 17,

        /// <summary>Metres the hoist's runways, and all they carry, have climbed its tower beams from where they are modelled.</summary>
        HoistLift = 18,

        /// <summary>
        /// Degrees the first turning room has turned from the pose it is modelled in. The rooms follow one another
        /// from here in the layout's order, as many as the workshop has: room i is <c>Room + i</c>, which is why
        /// nothing may be added after this one.
        /// </summary>
        Room = 32,
    }

    /// <summary>
    /// Poses every moving part of the workshop's machinery from the channel values it is handed. It decides nothing
    /// and knows nothing about the puzzle: <see cref="WorkshopMachinery"/> says what the values are.
    ///
    /// <para>
    /// The lists are written by the prefab builder from <c>workshop_layout.json</c>, which the Blender export derives
    /// from the model's own geometry. Axes are in each node's own frame, because an FBX node arrives turned and at
    /// scale 100 and a world axis would be wrong the moment the building is placed at a yaw.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorkshopGearTrain : MonoBehaviour
    {
        [Serializable]
        private struct Spinner
        {
            public Transform node;
            public Vector3 localAxis;
            [Tooltip("Degrees per unit of the channel.")] public float ratio;
            public WorkshopChannel channel;
        }

        [Serializable]
        private struct Slider
        {
            public Transform node;
            [Tooltip("Direction of travel in the parent's frame.")] public Vector3 direction;
            [Tooltip("Metres per unit of the channel.")] public float perUnit;
            public WorkshopChannel channel;
        }

        [Serializable]
        private struct Cable
        {
            public Transform node;
            [Tooltip("Which of the node's own axes runs along the cable: 0 x, 1 y, 2 z.")] public int axis;
            [Tooltip("Its length as modelled, in metres. It hangs from its origin.")] public float length;
            public WorkshopChannel channel;
        }

        [SerializeField] private Spinner[] spinners = Array.Empty<Spinner>();
        [SerializeField] private Slider[] sliders = Array.Empty<Slider>();
        [SerializeField] private Cable[] cables = Array.Empty<Cable>();

        private Quaternion[] spinnerRest;
        private Vector3[] sliderRest;
        private Vector3[] cableRest;
        private float[] shown;

        /// <summary>
        /// Pose everything from <paramref name="values"/>, indexed by <see cref="WorkshopChannel"/> and as long as
        /// the last room's channel needs. Parts whose
        /// channel has not changed since the last call are left alone, so a workshop at rest costs one comparison
        /// per part.
        /// </summary>
        public void Apply(IReadOnlyList<float> values)
        {
            // The editor's reload hands back an empty array, not null: anything of the wrong length is remembered afresh.
            bool first = shown == null || shown.Length != values.Count;
            if (first) Remember(values.Count);

            for (int i = 0; i < spinners.Length; i++)
            {
                Spinner part = spinners[i];
                if (part.node == null || !Changed(values, part.channel, first)) continue;
                part.node.localRotation =
                    spinnerRest[i] * Quaternion.AngleAxis(values[(int)part.channel] * part.ratio, part.localAxis);
            }

            for (int i = 0; i < sliders.Length; i++)
            {
                Slider part = sliders[i];
                if (part.node == null || !Changed(values, part.channel, first)) continue;
                part.node.localPosition = sliderRest[i] + part.direction * (values[(int)part.channel] * part.perUnit);
            }

            for (int i = 0; i < cables.Length; i++)
            {
                Cable part = cables[i];
                if (part.node == null || part.length <= 0f || !Changed(values, part.channel, first)) continue;
                Vector3 scale = cableRest[i];
                scale[part.axis] *= 1f + (values[(int)part.channel] / part.length);
                part.node.localScale = scale;
            }

            for (int i = 0; i < shown.Length && i < values.Count; i++) shown[i] = values[i];
        }

        private bool Changed(IReadOnlyList<float> values, WorkshopChannel channel, bool first) =>
            first || !Mathf.Approximately(values[(int)channel], shown[(int)channel]);

        private void Remember(int channels)
        {
            shown = new float[channels];
            spinnerRest = new Quaternion[spinners.Length];
            sliderRest = new Vector3[sliders.Length];
            cableRest = new Vector3[cables.Length];

            for (int i = 0; i < spinners.Length; i++)
                if (spinners[i].node != null) spinnerRest[i] = spinners[i].node.localRotation;
            for (int i = 0; i < sliders.Length; i++)
                if (sliders[i].node != null) sliderRest[i] = sliders[i].node.localPosition;
            for (int i = 0; i < cables.Length; i++)
                if (cables[i].node != null) cableRest[i] = cables[i].node.localScale;
        }
    }
}
