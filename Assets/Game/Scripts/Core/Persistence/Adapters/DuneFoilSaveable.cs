using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Persistence;
using SpaceGame.Vehicles.DuneFoil;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Persists how the DuneFoil is rigged: every sail's sheet, cant and hoist, plus whether the craft
    /// was moored.
    ///
    /// <b>Why the rig and not the motion.</b> Where the craft is and how fast it was going is the
    /// record's own pose — this craft is transform-driven and has no Rigidbody, so there is no velocity
    /// to keep either. What a player actually invests in is the rig: sheeting in, canting the mast,
    /// choosing what is hoisted. A reload that put the hull back but furled every sail would throw away
    /// the only part of sailing that took effort, and would leave the craft becalmed for no visible
    /// reason.
    ///
    /// <b>Mooring is stored because it is a decision with consequences.</b> An unmoored craft that comes
    /// back moored cannot be sailed until someone notices; one that comes back adrift sails off on the
    /// first gust while nobody is aboard.
    ///
    /// Sails are keyed by list position, which is safe here in a way hierarchy indices are not: the list
    /// is authored on the prefab, and a sail added to it appends rather than reorders.
    /// </summary>
    [RequireComponent(typeof(SailRig))]
    public class DuneFoilSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "dunefoil";

        private SailRig rig;
        private DuneFoilLocomotion craft;

        private SailRig Rig => rig != null ? rig : rig = GetComponent<SailRig>();

        public string SaveKey => Key;

        public struct SailState
        {
            public float sheet;
            public float cant;
            public float hoist;
        }

        public struct State
        {
            public List<SailState> sails;
            public bool moored;
        }

        public object CaptureState()
        {
            if (Rig == null) return null;

            craft ??= GetComponent<DuneFoilLocomotion>();

            var sails = new List<SailState>(Rig.Sails.Count);

            foreach (SailSurface sail in Rig.Sails)
            {
                if (sail == null)
                {
                    // Held rather than skipped: positions are the key, so dropping an empty entry
                    // would shift every sail after it onto the wrong record.
                    sails.Add(default);
                    continue;
                }

                sails.Add(new SailState { sheet = sail.SheetOut, cant = sail.Cant, hoist = sail.Hoist01 });
            }

            return new State { sails = sails, moored = craft != null && craft.HoldStation };
        }

        public void RestoreState(JObject state)
        {
            if (Rig == null || state == null) return;

            craft ??= GetComponent<DuneFoilLocomotion>();

            var restored = state.ToObject<State>(SaveSerializer.Serializer);

            if (craft != null) craft.HoldStation = restored.moored;
            if (restored.sails == null) return;

            for (int i = 0; i < restored.sails.Count && i < Rig.Sails.Count; i++)
            {
                SailSurface sail = Rig.Sails[i];
                if (sail == null) continue;

                SailState saved = restored.sails[i];

                sail.SetSheet(saved.sheet);
                sail.SetCant(saved.cant);

                // SetHoist rather than SetHoisted: the latter sets a target the halyards then travel
                // toward, so a fully hoisted sail would come back mid-raise and haul itself up in front
                // of the player.
                sail.SetHoist(saved.hoist);
            }
        }
    }
}
