// Look at a creature's head, press E, it enjoys it.
//
// Lives on a small trigger volume parented to the head bone, NOT on the agent root, and that
// placement is the whole mechanism for "you must be pointing at his head". Interactor.ResolveAlongRay
// treats a trigger as a detection volume rather than a surface: a trigger only answers when it
// carries the IInteractable ITSELF, and it never inherits one from a parent. So this component on a
// trigger on the head is reachable by looking at the head and by nothing else, while the body's own
// solid collider — which does not have this component, and is not a parent of it — offers nothing.
//
// Putting it on the root instead would have made every square metre of a five-and-a-half metre
// animal say "pet me", including his tail and the underside of his feet.
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Agents
{
    [RequireComponent(typeof(Collider))]
    public class PettableModule : MonoBehaviour, IInteractable, IContextualInteractable,
                                  IInteractionReadout
    {
        [Header("Wiring")]
        [Tooltip("The agent root. Networking and animation both belong to it, not to this trigger " +
                 "— the NetworkObject and the AgentAnimatorDriver are up there.")]
        [SerializeField] private GameObject agentRoot;
        [SerializeField] private AgentAnimatorDriver animatorDriver;

        [Header("Reaction")]
        [Tooltip("Animator trigger fired on every machine when this creature is petted.")]
        [SerializeField] private string happyTrigger = "Happy";
        [SerializeField] private SfxId happySound = SfxId.NpcMumbleFriendly;

        [Tooltip("Seconds before it can be petted again. Long enough that the reaction plays out " +
                 "rather than restarting under itself — hold E and he would twitch, not enjoy it.")]
        [SerializeField] private float cooldown = 3f;

        [Header("Mood")]
        [Tooltip("Optional. Nobody pets an animal that is mid-charge: while this reports enraged " +
                 "or fleeing the prompt does not appear at all, rather than appearing and refusing.")]
        [SerializeField] private FightOrFlightModule mood;

        [Tooltip("Animator trigger fired on the PLAYER who petted, on every machine. Their " +
                 "controller needs an Upper Body one-shot by this name — see PlayerPetGestureBuilder.")]
        [SerializeField] private string petterTrigger = "Pet";

        [Tooltip("How long the petter's gesture runs. Matches PetCreature.fbx (2.5 s); it keeps " +
                 "the masked Upper Body layer raised for that long so the reach is visible.")]
        [SerializeField] private float petGestureSeconds = 2.5f;

        [Tooltip("Label the HUD shows on the crosshair.")]
        [SerializeField] private string label = "Appa";

        private float readyAt;

        /// <summary>True for the seconds after a successful pet. Read by tests and by the builder.</summary>
        public bool OnCooldown => Time.time < readyAt;

        private void Reset()
        {
            Collider own = GetComponent<Collider>();
            if (own != null) own.isTrigger = true;
        }

        private void Awake()
        {
            if (agentRoot == null) agentRoot = transform.root.gameObject;
            if (animatorDriver == null && agentRoot != null)
                animatorDriver = agentRoot.GetComponentInChildren<AgentAnimatorDriver>();
            if (mood == null && agentRoot != null)
                mood = agentRoot.GetComponentInChildren<FightOrFlightModule>();
        }

        private void OnEnable()
        {
            if (agentRoot == null) return;
            // Both ends on the root's channel: the request only ever arrives on the server, the
            // answer arrives everywhere. Registered here rather than in a component of its own
            // because this object is part of the prefab and so exists on every machine.
            agentRoot.transform.NetOn(NetMsg.PetRequest, OnPetRequested);
            agentRoot.transform.NetOn(NetMsg.Petted, OnPetted);
        }

        private void OnDisable()
        {
            if (agentRoot == null) return;
            agentRoot.transform.NetOff(NetMsg.PetRequest, OnPetRequested);
            agentRoot.transform.NetOff(NetMsg.Petted, OnPetted);
        }

        // ─────────── IInteractable ───────────

        public bool CanInteract() => !OnCooldown && !IsUpset();

        /// <summary>
        /// Contextual as well as plain, so a refusal hides the prompt instead of showing one that
        /// does nothing. Same answer for everybody here — the creature's mood does not depend on
        /// who is looking — but the interface is what the Interactor consults to hide the prompt.
        /// </summary>
        public bool CanInteract(Interactor interactor) => CanInteract();

        public void Interact(Interactor interactor)
        {
            if (!CanInteract()) return;

            // Start the cooldown on the presser's machine straight away. The round trip to the
            // server and back is long enough to press E three more times into it.
            readyAt = Time.time + cooldown;

            // The server decides, then tells everyone — including the presser, who has played
            // nothing yet. A creature's reaction is world state: the other players are standing
            // there watching, and an animation played only locally would have him sit inert for
            // them while he leans into your hand for you.
            // The petter travels with the message. Everyone needs to know WHO reached out, or
            // the arm goes up on the host's body whoever actually pressed the key.
            NetMessaging.NetSendTo(agentRoot, NetMsg.PetRequest,
                                   new NetArg().With(interactor.gameObject), NetTo.Server);
        }

        // ─────────── Wire ───────────

        /// <summary>
        /// Server side. The mood is re-checked here rather than trusted from the sender: the
        /// machine that composed the request was looking at its own copy of this creature, and it
        /// may have started charging since.
        /// </summary>
        private void OnPetRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Owns(this)) return;
            if (IsUpset()) return;

            readyAt = Time.time + cooldown;
            NetMessaging.NetSendTo(agentRoot, NetMsg.Petted, arg, NetTo.All);
        }

        private void OnPetted(in NetArg arg, ulong sender)
        {
            readyAt = Time.time + cooldown;
            Play(arg.Resolve());
        }

        private void Play(GameObject petter)
        {
            if (animatorDriver != null && !string.IsNullOrEmpty(happyTrigger))
                animatorDriver.TriggerByName(happyTrigger);

            Sfx.Play(happySound, transform.position, GetInstanceID());

            // The reaching arm belongs to the player, not to the creature. Resolve can come back
            // null on a machine that has not spawned that player yet — the animal still enjoys it.
            if (petter == null || string.IsNullOrEmpty(petterTrigger)) return;

            // Through the aim rig, not straight at the Animator. That component owns the masked
            // Upper Body layer's weight and rewrites it every frame from whether an item is held —
            // so a trigger set directly plays the clip on a layer weighted 0 and you see nothing,
            // which is exactly what happened: you pet with a free hand.
            var rig = petter.GetComponentInChildren<SpaceGame.Characters.PlayerAimRig>();
            if (rig != null)
            {
                rig.PlayGesture(petterTrigger, petGestureSeconds);
                return;
            }

            // Anything that is not a player still gets the trigger, if its controller has one.
            Animator petterAnimator = petter.GetComponentInChildren<Animator>();
            if (petterAnimator == null || petterAnimator.runtimeAnimatorController == null) return;

            foreach (AnimatorControllerParameter parameter in petterAnimator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger
                    && parameter.name == petterTrigger)
                {
                    petterAnimator.SetTrigger(petterTrigger);
                    return;
                }
            }
        }

        /// <summary>
        /// Only a creature that is actively charging refuses.
        ///
        /// This used to refuse on anything but Calm, which swallowed Fleeing too — and because a
        /// refusal HIDES the prompt rather than greying it out, an animal that was quietly taking
        /// damage from somewhere became permanently unpettable with nothing on screen to say why.
        /// A fleeing animal is running away from you, so allowing it costs nothing and removes a
        /// silent failure mode; a charging one is about to ram you and should not offer its cheek.
        /// </summary>
        private bool IsUpset() => mood != null && mood.CurrentMood == FightOrFlightModule.Mood.Enraged;

        // ─────────── IInteractionReadout ───────────

        public string Label => label;
        public string Prompt => OnCooldown ? "" : "E: pet";
        public float? Value01 => null;
        public string ValueText => "";

        private void OnValidate() => cooldown = Mathf.Max(0.1f, cooldown);
    }
}
