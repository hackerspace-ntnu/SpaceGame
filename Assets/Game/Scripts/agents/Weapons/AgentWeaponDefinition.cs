using FMODUnity;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Weapons;

namespace SpaceGame.Agents
{
    [CreateAssetMenu(menuName = "Agents/Weapon Definition", fileName = "NewAgentWeapon")]
    public class AgentWeaponDefinition : ScriptableObject
    {
        [Header("Visuals")]
        [Tooltip("Prefab spawned and attached to the muzzle socket on the entity. Leave null if the weapon is built into the model.")]
        public GameObject weaponModelPrefab;

        [Header("Projectile")]
        public GameObject projectilePrefab;
        [Tooltip("Speed in m/s the projectile Rigidbody is launched at.")]
        public float projectileSpeed = 20f;

        [Header("Damage")]
        public int damagePerHit = 15;

        [Header("Noise")]
        [Tooltip("Metres a shot from this weapon is heard over by NoiseReceiverModules — guards " +
                 "investigate it, wildlife bolts from it. Nothing to do with how loud it sounds; " +
                 "that is fireId below. 0 = silent to AI.")]
        public float gunshotNoiseRadius = 40f;

        [Header("Audio")]
        [Tooltip("Which catalog slot this weapon fires with. fireSound below overrides it outright.")]
        public SfxId fireId = SfxId.WeaponGunFire;
        public EventReference fireSound;
    }
}
