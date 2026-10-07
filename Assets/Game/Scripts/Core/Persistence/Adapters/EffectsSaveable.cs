using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpaceGame.Items;
using SpaceGame.Persistence;

namespace SpaceGame.Core.Persistence
{
    /// <summary>
    /// Keeps the buffs a player is under running across a reload.
    ///
    /// <para>
    /// A five-second anti-gravity float ended the instant the world was saved and reopened, which is
    /// not merely a lost buff: the effect's <c>stopEffect</c> is what puts <c>useGravity</c> back,
    /// and an effect that never expires because its manager was destroyed mid-flight is the same
    /// shape as the kinematic-flag bug that made loaded worlds unplayable.
    /// </para>
    /// <para>
    /// <b>What is stored, and why it cannot be the effect.</b> An <see cref="Effect"/> is three
    /// <c>System.Action&lt;Rigidbody&gt;</c> delegates closing over a local, and nothing in a file
    /// can hold one. So the record holds a token naming the KIND of effect plus how many seconds of
    /// it were left, and <see cref="EffectCatalog"/> builds a fresh set of delegates from the token.
    /// Fresh is also the correct answer rather than a compromise: the anti-gravity potion's
    /// <c>onApply</c> reads the body's gravity flag as it finds it, so a rebuilt effect ends by
    /// restoring the state of the body it actually landed on.
    /// </para>
    /// <para>
    /// Effects with no token are skipped. That is the deliberate opt-in — an item that hands the
    /// manager raw delegates is saying its effect is a one-off nudge, not something to resurrect.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(EffectManager))]
    public class EffectsSaveable : MonoBehaviour, ISaveable
    {
        public const string Key = "effects";

        private EffectManager manager;

        private EffectManager Manager =>
            manager != null ? manager : manager = GetComponent<EffectManager>();

        public string SaveKey => Key;

        public struct Entry
        {
            public string token;
            public float remaining;
        }

        public struct State
        {
            public List<Entry> effects;
        }

        public object CaptureState()
        {
            if (Manager == null) return null;

            IReadOnlyList<Effect> active = Manager.ActiveEffects;
            if (active == null || active.Count == 0) return null;

            var entries = new List<Entry>(active.Count);

            for (int i = 0; i < active.Count; i++)
            {
                Effect effect = active[i];
                if (effect == null || string.IsNullOrEmpty(effect.SaveToken)) continue;
                if (effect.timer <= 0f) continue;

                entries.Add(new Entry { token = effect.SaveToken, remaining = effect.timer });
            }

            return entries.Count == 0 ? null : new State { effects = entries };
        }

        public void RestoreState(JObject state)
        {
            if (Manager == null) return;

            // Null means nothing was running, and that has to be applied rather than skipped: this
            // same manager may already be carrying an effect from a previous restore of the same
            // body, and leaving it would be a buff that outlives its own record.
            if (state == null)
            {
                Manager.RestoreEffects(null);
                return;
            }

            List<Entry> entries = state.ToObject<State>(SaveSerializer.Serializer).effects;
            var rebuilt = new List<Effect>();

            if (entries != null)
            {
                foreach (Entry entry in entries)
                {
                    // An unknown token warns inside the catalog and is dropped. A save written by a
                    // build that had one more potion in it must still load.
                    if (EffectCatalog.TryBuild(entry.token, entry.remaining, out Effect effect))
                        rebuilt.Add(effect);
                }
            }

            Manager.RestoreEffects(rebuilt);
        }
    }
}
