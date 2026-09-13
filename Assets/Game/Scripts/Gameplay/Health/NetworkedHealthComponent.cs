// Replicates one entity's health, and receives the damage requests that change it.
//
// Both halves live here because they are the same statement: the server owns this health value.
// It publishes it outward, and it is the only machine that may be asked to change it.
using System;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    [RequireComponent(typeof(HealthComponent))]
    public class NetworkedHealthComponent : NetworkBehaviour
    {
        /// <summary>
        /// Raised on every peer when a hit dealt by a player lands on anything — the victim, how
        /// much, and which player's object did it.
        /// <para>
        /// It carries the attacker rather than a "was it me" flag on purpose: whether a hit is
        /// worth drawing is a presentation question, and the answer differs between a damage
        /// number, a hit marker and a combat log. Ask <c>NetworkObject.IsOwner</c> on the attacker
        /// to find out whether this machine fired the shot.
        /// </para>
        /// <para>
        /// Static because the listener is one screen-wide overlay rather than something living on
        /// each victim, and the victims are every animal, NPC and player in a streamed world.
        /// Nothing here keeps a reference to a subscriber, so an overlay that outlives a victim is
        /// safe; the arguments are the ones that may be destroyed and must be null-checked.
        /// </para>
        /// </summary>
        public static event Action<HealthComponent, int, GameObject> DamageAnnounced;

        private HealthComponent health;

        // Everyone, not Owner. A server-owned entity — which is every AI, creature and vehicle in
        // the game — has no owning client, so Owner permission published its health to nobody and
        // every client kept showing a monster at full health while the server watched it die.
        private readonly NetworkVariable<int> networkHealth = new(
            100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private void Awake() => health = GetComponent<HealthComponent>();

        private void OnEnable()
        {
            this.NetOn(NetMsg.Damage, OnDamageRequested);
            this.NetOn(NetMsg.Damaged, OnDamageAnnounced);
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.Damage, OnDamageRequested);
            this.NetOff(NetMsg.Damaged, OnDamageAnnounced);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                networkHealth.Value = health.GetHealth;

                health.OnDamage += SyncHealth;
                health.OnHeal += SyncHealth;
                health.OnDeath += SyncHealth;
                health.OnRestored += SyncHealth;

                // Server-only, like every other subscription here, because the server is the only
                // machine that ever learns who dealt a hit — LastDamageSource is written by
                // HealthComponent.Damage, which runs nowhere else.
                health.OnDamage += AnnounceDamage;
                health.OnDamage += TellOwnerWhereFrom;
            }
            else
            {
                // Late joiners and newly streamed-in entities arrive with the current value
                // already in the variable and no change event coming, so read it once on spawn.
                networkHealth.OnValueChanged += ApplyHealth;
                ApplyHealth(health.GetHealth, networkHealth.Value);
            }
        }

        public override void OnDestroy()
        {
            // Only the unsubscribes are conditional. The early `return` used to skip
            // base.OnDestroy() as well, and NetworkBehaviour.OnDestroy is what disposes this
            // behaviour's NetworkVariables and drops it from its NetworkObject's
            // ChildNetworkBehaviours list -- so every destroy leaked both.
            if (health != null)
            {
                if (IsServer)
                {
                    health.OnDamage -= SyncHealth;
                    health.OnHeal -= SyncHealth;
                    health.OnDeath -= SyncHealth;
                    health.OnRestored -= SyncHealth;
                    health.OnDamage -= AnnounceDamage;
                    health.OnDamage -= TellOwnerWhereFrom;
                }
                else
                {
                    networkHealth.OnValueChanged -= ApplyHealth;
                }
            }

            base.OnDestroy();
        }

        /// <summary>
        /// A client asking the server to hurt this entity — see <see cref="NetDamage"/>. Ignored
        /// anywhere but the server, where the message cannot arrive in the first place; the guard
        /// is for the offline path, on which the message is dispatched locally.
        /// </summary>
        private void OnDamageRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this) || health == null || arg.A <= 0) return;

            GameObject source = arg.Resolve();
            health.Damage(arg.A, source != null ? source.transform : null);
        }

        /// <summary>
        /// Tells the victim's own machine which way the hit came from, so their visor can point at
        /// it.
        ///
        /// <para>
        /// Separate from <see cref="AnnounceDamage"/> and deliberately not folded into it. That one
        /// is a BROADCAST and is gated on a player having dealt the hit, because damage numbers
        /// over things nobody is watching are not worth the bandwidth — which means a client mauled
        /// by a creature is told nothing at all by it. This is a UNICAST to one player about their
        /// own body, so it is cheap enough to send for every source, creature and cactus included.
        /// </para>
        /// <para>
        /// An Rpc rather than a NetMessaging message because that layer has no unicast: its sends
        /// are to everyone or to everyone-but-me, and this must not tell the whole session which
        /// way to flinch.
        /// </para>
        /// </summary>
        private void TellOwnerWhereFrom(int amount)
        {
            if (health == null || amount <= 0) return;

            // The host's own player already raised the event locally inside Damage(); sending to
            // it as well would flash the arc twice for one hit.
            if (!IsSpawned || OwnerClientId == NetworkManager.ServerClientId) return;

            Transform source = health.LastDamageSource;
            DamageDirectionRpc(amount, source != null ? source.position : Vector3.zero, source != null);
        }

        /// <summary>
        /// Runs on the victim's machine only. Raises the directional event and changes no health:
        /// the value itself arrives through <see cref="networkHealth"/>, and subtracting here as
        /// well would apply the same hit twice.
        /// </summary>
        [Rpc(SendTo.Owner)]
        private void DamageDirectionRpc(int amount, Vector3 sourcePosition, bool hasSource)
        {
            if (health != null) health.ReportDamageDirection(amount, sourcePosition, hasSource);
        }

        private void SyncHealth(int _) => SyncHealth();

        private void SyncHealth() => networkHealth.Value = health.GetHealth;

        /// <summary>
        /// Server side: tell everyone that a player just hurt this entity — see
        /// <see cref="NetMsg.Damaged"/> for why it is a broadcast.
        ///
        /// Silent unless a player dealt the hit. Environmental damage (a cactus, a fall) arrives
        /// with no source at all, and an AI mauling a creature resolves to no PlayerIdentity, so
        /// neither costs a message. That is what keeps this affordable in a world where most of
        /// the damage happens between things nobody is watching.
        /// </summary>
        private void AnnounceDamage(int amount)
        {
            if (amount <= 0 || health == null) return;

            Transform source = health.LastDamageSource;
            if (source == null) return;

            // GetComponentInParent, not GetComponent: callers disagree about what "source" is, and
            // both spellings are correct. A projectile reports the shooter's root, while a hitscan
            // rifle reports its own transform — and an equipped weapon is instantiated under a
            // socket on the player's rig, so walking up finds the same player either way.
            PlayerIdentity attacker = source.GetComponentInParent<PlayerIdentity>();
            if (attacker == null) return;

            // Others, not All. This machine just applied the damage, so HealthComponent.AnyDamaged
            // has already fired here and anything local has been shown. Sending to everyone would
            // hand the authority its own news back and draw the number twice.
            this.NetToOthers(NetMsg.Damaged, new NetArg { A = amount }.With(attacker));
        }

        /// <summary>
        /// Every peer, including the host that sent it: a player-dealt hit landed here. Republished
        /// as a plain C# event so the UI never has to know a message id.
        /// </summary>
        private void OnDamageAnnounced(in NetArg arg, ulong sender)
        {
            if (health == null || arg.A <= 0) return;

            DamageAnnounced?.Invoke(health, arg.A, arg.Resolve());
        }

        /// <summary>
        /// Assigns the server's value rather than replaying the difference.
        ///
        /// The old version computed a delta and called Damage/Heal with it, which meant a client
        /// that missed one update stayed wrong forever, a heal past max silently clamped away part
        /// of the correction, and every replicated hit fired the local damage flash a second time.
        /// RestoreHealth exists precisely for "this value is now the truth".
        /// </summary>
        private void ApplyHealth(int previous, int current)
        {
            if (health != null) health.RestoreHealth(current);
        }
    }
}
