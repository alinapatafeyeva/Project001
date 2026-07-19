using System.Collections.Generic;
using System.Collections.ObjectModel;
using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Recovery
{
    /// <summary>
    /// Owns the collectors held in the Recovery Row after a Continue transfer
    /// from ConveyorSystem. Stores the same ConveyorRider instances it
    /// receives — never clones or recreates one — so RemainingHunger,
    /// HungerCapacity, and MatchTypeId are preserved exactly as they were on
    /// the Conveyor. A pure gameplay system: no rendering, selection, input,
    /// or animation of any kind, and no knowledge of a future
    /// ContinueRecoveryController or FailureRecoveryController. Later PRs
    /// will add Recovery Row UI, collector selection, and relaunching a held
    /// collector back onto the Conveyor.
    /// </summary>
    public class RecoveryRowController : MonoBehaviour
    {
        private readonly List<ConveyorRider> _collectors = new List<ConveyorRider>();

        // Wraps _collectors once, rather than allocating a new wrapper on
        // every access — callers cannot cast this back to List<ConveyorRider>
        // and mutate it, unlike exposing _collectors itself typed as
        // IReadOnlyList.
        private readonly ReadOnlyCollection<ConveyorRider> _collectorsReadOnly;

        public RecoveryRowController()
        {
            _collectorsReadOnly = new ReadOnlyCollection<ConveyorRider>(_collectors);
        }

        /// <summary>
        /// Every collector currently held in the Recovery Row, in the order
        /// they were received. Genuinely read-only — callers must go through
        /// ReceiveCollectors to change what this controller holds.
        /// </summary>
        public IReadOnlyList<ConveyorRider> Collectors => _collectorsReadOnly;

        /// <summary>
        /// Accepts ownership of the given collectors, appending them to
        /// whatever the Recovery Row already holds, in the given order. Null
        /// entries and collectors already held by this Recovery Row are
        /// skipped rather than added twice. Does not touch any collector's
        /// transform, riding state, hunger, or MatchTypeId — the caller
        /// (e.g. a future ContinueRecoveryController) is responsible for
        /// whatever gameplay event triggered the transfer, and for removing
        /// the collectors from their previous owner (e.g.
        /// ConveyorSystem.TakeAllRiders) before calling this.
        /// </summary>
        public void ReceiveCollectors(IReadOnlyList<ConveyorRider> collectors)
        {
            if (collectors == null)
                return;

            foreach (ConveyorRider collector in collectors)
            {
                if (collector == null || _collectors.Contains(collector))
                    continue;

                _collectors.Add(collector);
            }
        }
    }
}
