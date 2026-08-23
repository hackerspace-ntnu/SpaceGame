// A kinematic layer that normally OWNS its body's transform, but can be told to stop writing it
// and read it instead.
//
// The case this exists for is a replicated entity. LeggedLocomotion is invariant I4: the single
// owner of the body's transform. On the machine deciding where the machine goes that is exactly
// right. On every other machine in a session the pose arrives over the wire, and a locomotion that
// keeps writing its own answer in LateUpdate wins that argument every frame -- so a remote ostrich
// stands where it spawned while its rider rides away over the dunes.
//
// Switching the locomotion OFF is not the alternative: it both moves the body and solves the legs,
// so a disabled one leaves a remote copy sliding along with still feet. Following is the only
// option that gets both halves right.
//
// Declared in this assembly rather than in the netcode layer because the locomotion assembly
// cannot reference that one, and because "something else is posing me" is not a networking idea.
// NetAuthority is merely the component that happens to know the answer.
namespace SpaceGame.Locomotion
{
    public interface IExternallyPosed
    {
        /// <summary>
        /// False (the default) to own the body's transform. True to leave it to whoever else is
        /// writing it and derive this frame's motion by measuring the result.
        /// </summary>
        bool ExternallyPosed { get; set; }
    }
}
