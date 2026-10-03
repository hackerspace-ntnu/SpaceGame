// Every prop and rest one settlement's buildings brought along, gathered in hierarchy order so an index names
// the same rest on every machine, plus the deciding machine's reservations: a prop a resident is on its way to
// pick up, a rest it is on its way to set one down at. Reservations are never saved or sent — a load restarts
// every errand round, so there is nothing in flight to remember.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.World
{
    public sealed class SettlementProps
    {
        private readonly List<PropRest> rests = new();
        private readonly List<SettlementProp> props = new();
        private readonly Dictionary<PropRest, int> restIndex = new();
        private readonly Dictionary<SettlementProp, int> propIndex = new();
        private readonly Dictionary<SettlementProp, SettlementPropSync> syncOf = new();
        private readonly Dictionary<SettlementSpot, List<int>> restsOfSpot = new();
        private readonly HashSet<SettlementProp> reservedProps = new();
        private readonly HashSet<int> reservedRests = new();
        private static readonly List<int> NoRests = new();

        public SettlementProps(Transform generated)
        {
            if (generated == null) return;

            foreach (PropRest rest in generated.GetComponentsInChildren<PropRest>(true))
            {
                restIndex[rest] = rests.Count;
                rests.Add(rest);
                if (rest.Spot == null) continue;
                if (!restsOfSpot.TryGetValue(rest.Spot, out List<int> list)) restsOfSpot[rest.Spot] = list = new List<int>();
                list.Add(rests.Count - 1);
            }

            foreach (SettlementPropSync sync in generated.GetComponentsInChildren<SettlementPropSync>(true))
                foreach (SettlementProp prop in sync.Props)
                {
                    propIndex[prop] = props.Count;
                    props.Add(prop);
                    syncOf[prop] = sync;
                }
        }

        public int RestCount => rests.Count;
        public IReadOnlyList<SettlementProp> Props => props;

        public PropRest Rest(int index) => index >= 0 && index < rests.Count ? rests[index] : null;
        public int IndexOf(PropRest rest) => rest != null && restIndex.TryGetValue(rest, out int i) ? i : -1;
        public int IndexOf(SettlementProp prop) => prop != null && propIndex.TryGetValue(prop, out int i) ? i : -1;
        public SettlementProp Prop(int index) => index >= 0 && index < props.Count ? props[index] : null;

        /// <summary>The rests a resident reaches from this errand spot, in rest order.</summary>
        public IReadOnlyList<int> RestsOf(SettlementSpot spot) =>
            spot != null && restsOfSpot.TryGetValue(spot, out List<int> list) ? list : NoRests;

        /// <summary>The rest the prop stands at, or <see cref="SettlementPropSync.Carried"/> (or a legacy <see cref="SettlementPropSync.Taken"/>).</summary>
        public short StateOf(SettlementProp prop) =>
            prop != null && syncOf.TryGetValue(prop, out SettlementPropSync sync) ? sync.StateOf(prop) : SettlementPropSync.Taken;

        /// <summary>A prop standing at this rest and promised to nobody, else null.</summary>
        public SettlementProp FreePropAt(int rest)
        {
            foreach (SettlementProp prop in props)
                if (StateOf(prop) == rest && !reservedProps.Contains(prop)) return prop;
            return null;
        }

        /// <summary>Nothing stands at this rest and nobody is on the way to set something there.</summary>
        public bool IsFree(int rest)
        {
            if (rest < 0 || rest >= rests.Count || reservedRests.Contains(rest)) return false;
            foreach (SettlementProp prop in props)
                if (StateOf(prop) == rest) return false;
            return true;
        }

        public void Reserve(SettlementProp prop, int rest)
        {
            if (prop != null) reservedProps.Add(prop);
            if (rest >= 0) reservedRests.Add(rest);
        }

        public void Release(SettlementProp prop, int rest)
        {
            if (prop != null) reservedProps.Remove(prop);
            if (rest >= 0) reservedRests.Remove(rest);
        }

        /// <summary>Server only. Into a resident's hands; false when it is not resting anywhere (already in somebody's hands).</summary>
        public bool Pick(SettlementProp prop)
        {
            if (StateOf(prop) < 0) return false;
            syncOf[prop].Set(prop, SettlementPropSync.Carried);
            return true;
        }

        /// <summary>Server only. Set down at <paramref name="rest"/>.</summary>
        public void Put(SettlementProp prop, int rest)
        {
            if (prop == null || rest < 0 || rest >= rests.Count || !syncOf.TryGetValue(prop, out SettlementPropSync sync)) return;
            sync.Set(prop, (short)rest);
        }
    }
}
