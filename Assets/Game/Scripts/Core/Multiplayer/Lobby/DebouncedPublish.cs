using System;
using System.Threading.Tasks;
using UnityEngine;

namespace SpaceGame.Core
{
    /// <summary>
    /// Coalesces a burst of requests into the one send that actually matters: the last one.
    ///
    /// <para>
    /// Lobby rate-limits <c>UpdatePlayer</c> to five calls per five seconds per player, and
    /// stepping a palette to see what a swatch looks like is a dozen presses in a couple of
    /// seconds. Without this, browsing the palette trips the limiter and the colour a player
    /// settles on is the one request that gets refused — everyone else keeps seeing whatever they
    /// happened to be on when the budget ran out.
    /// </para>
    ///
    /// <para>
    /// The suit cycler already carried a private version of exactly this shape. VS needs the same
    /// thing three times over — suit colour, team, team colour — so it is pulled out once here
    /// rather than copied twice more.
    /// </para>
    ///
    /// <para>
    /// Not Unity-aware: <see cref="Tick"/> takes its own delta time rather than reading
    /// <c>Time.deltaTime</c>, so this can be driven from any MonoBehaviour's <c>Update</c> and
    /// tested without a live PlayMode loop.
    /// </para>
    /// </summary>
    public class DebouncedPublish<T>
    {
        private readonly float seconds;

        private bool hasPending;
        private T pendingValue;
        private float timer;

        /// <summary>
        /// Guards against a second send starting while the first is still on the wire. A press
        /// landing mid-flight becomes the next pending value rather than a second concurrent
        /// request racing the first for which one the server sees last.
        /// </summary>
        private bool inFlight;

        public DebouncedPublish(float seconds)
        {
            this.seconds = seconds;
        }

        /// <summary>Replaces whatever was waiting and restarts the clock.</summary>
        public void Request(T value)
        {
            pendingValue = value;
            hasPending = true;
            timer = seconds;
        }

        /// <summary>Forgets the pending value outright — for leaving a lobby, say, where a value
        /// that fires later would land on whatever session this peer joins next.</summary>
        public void Cancel()
        {
            hasPending = false;
            pendingValue = default;
        }

        /// <summary>The value waiting to be sent, without consuming it.</summary>
        public bool TryPeek(out T value)
        {
            value = pendingValue;
            return hasPending;
        }

        /// <summary>
        /// Counts down, and once the clock expires, sends the last requested value.
        ///
        /// <para>
        /// The pending value is cleared BEFORE <paramref name="send"/> is awaited, not after — a
        /// press landing while the request is in flight has to become a new pending value on its
        /// own clock, rather than being silently swallowed because this method still thought there
        /// was nothing new to send.
        /// </para>
        ///
        /// <para>
        /// Failures are logged as warnings and never raised. The caller's own local state is
        /// already correct by the time this runs — that is the whole premise of publishing
        /// something that already happened — so the only casualty of a failed send is that other
        /// people see the previous value until the next press.
        /// </para>
        /// </summary>
        public async void Tick(float deltaTime, Func<T, Task> send)
        {
            if (!hasPending || inFlight) return;

            timer -= deltaTime;
            if (timer > 0f) return;

            T sending = pendingValue;
            hasPending = false;
            pendingValue = default;
            inFlight = true;

            try
            {
                await send(sending);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DebouncedPublish] Could not publish {sending}: {e.Message}");
            }
            finally { inFlight = false; }
        }
    }
}
