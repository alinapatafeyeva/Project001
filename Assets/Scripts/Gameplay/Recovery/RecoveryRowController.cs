using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Recovery
{
    /// <summary>
    /// Owns the collectors held in the Recovery Row after a Continue transfer
    /// from ConveyorSystem — the same ConveyorRider instances it receives,
    /// never cloned or recreated, so RemainingHunger, HungerCapacity, and
    /// MatchTypeId are preserved exactly as they were on the Conveyor. Purely
    /// a gameplay-ownership component: it does not reparent, position, or
    /// otherwise touch any collector's transform — that is RecoveryRowView's
    /// job, driven by the CollectorsChanged event this controller raises
    /// whenever its held set changes. Doubles as an ICollectorSource so
    /// CollectorSelectionController can launch a held collector back onto
    /// the Conveyor: ReleaseCollector is only ever called once
    /// ConveyorSystem has already accepted the collector, so a rejected
    /// launch leaves this row untouched. Has no knowledge of a future
    /// ContinueRecoveryController or FailureRecoveryController — something
    /// else is responsible for calling ReceiveCollectors when Continue is
    /// confirmed.
    /// </summary>
    public class RecoveryRowController : MonoBehaviour, ICollectorSource
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
        /// Raised after the held collector set changes — a receive that
        /// actually added something, or a release that actually removed
        /// something. Presentation (RecoveryRowView) subscribes to this to
        /// know when to re-lay out; this controller never calls into any
        /// view directly.
        /// </summary>
        public event Action CollectorsChanged;

        /// <summary>
        /// Every collector currently held in the Recovery Row, in the order
        /// they were received. Genuinely read-only — callers must go through
        /// ReceiveCollectors to change what this controller holds.
        /// </summary>
        public IReadOnlyList<ConveyorRider> Collectors => _collectorsReadOnly;

        /// <summary>
        /// Accepts ownership of the given collectors, appending them to
        /// whatever the Recovery Row already holds, in the given order, then
        /// raises CollectorsChanged. Null entries and collectors already held
        /// by this Recovery Row are skipped rather than added twice. Does not
        /// touch a collector's transform, riding state, hunger, or
        /// MatchTypeId — the caller (e.g. a future ContinueRecoveryController)
        /// is responsible for whatever gameplay event triggered the transfer,
        /// and for removing the collectors from their previous owner (e.g.
        /// ConveyorSystem.TakeAllRiders) before calling this.
        /// </summary>
        public void ReceiveCollectors(IReadOnlyList<ConveyorRider> collectors)
        {
            if (collectors == null)
                return;

            bool added = false;

            foreach (ConveyorRider collector in collectors)
            {
                if (collector == null || _collectors.Contains(collector))
                    continue;

                _collectors.Add(collector);
                added = true;
            }

            if (added)
                CollectorsChanged?.Invoke();
        }

        /// <summary>
        /// True when the given collector currently sits in this Recovery Row.
        /// </summary>
        public bool Contains(CollectorView collectorView)
        {
            if (collectorView == null)
                return false;

            var rider = collectorView.GetComponent<ConveyorRider>();
            return rider != null && _collectors.Contains(rider);
        }

        bool ICollectorSource.CanSelect(CollectorView collectorView) => Contains(collectorView);

        /// <summary>
        /// Finalizes removal after the collector has already successfully
        /// boarded the conveyor elsewhere (see CollectorSelectionController):
        /// drops it from this row and raises CollectorsChanged. A launch
        /// ConveyorSystem rejects never reaches this method, so the
        /// collector simply stays exactly where it was.
        /// </summary>
        bool ICollectorSource.ReleaseCollector(CollectorView collectorView)
        {
            if (collectorView == null)
                return false;

            var rider = collectorView.GetComponent<ConveyorRider>();
            if (rider == null || !_collectors.Remove(rider))
                return false;

            CollectorsChanged?.Invoke();
            return true;
        }
    }
}
