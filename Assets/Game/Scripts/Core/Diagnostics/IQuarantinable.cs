// Opt out of "quarantine means enabled = false", for a component whose broken part is smaller than
// the whole component.
//
// The default is deliberately blunt — switching the Behaviour off is the only thing that is correct
// for a component this code knows nothing about. But a component that owns several jobs can lose
// one and keep the others: a station that can no longer draw its readout can still be operated, and
// disabling it outright would take the seat away too.
namespace SpaceGame.Diagnostics
{
    public interface IQuarantinable
    {
        /// <summary>
        /// Shed the part that keeps throwing. Called once, on the fault that trips quarantine, and
        /// never again for that site. Implementing this means the component is NOT disabled — so an
        /// implementation that does nothing leaves the fault running forever.
        /// </summary>
        void OnQuarantined();
    }
}
