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

        // Presentation-only exclusion set — see SetReservedForDeparture.
        // Deliberately NOT part of _collectors/Collectors.Count: a reserved
        // collector still counts as genuinely held (so
        // RecoveryLineLayoutController's occupancy-driven expand/collapse
        // stays expanded for the whole of its departure travel), it is only
        // excluded from RecoveryRowView's own row layout while
        // CollectorSelectionController's departure-travel coroutine owns
        // its transform.
        private readonly HashSet<ConveyorRider> _reservedForDeparture = new HashSet<ConveyorRider>();

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
        ///
        /// Does switch presentation, though: every collector actually added
        /// is arriving from the Conveyor (Front Eating, mid-ride), and every
        /// waiting source — Recovery Row included — shows Back Idle, so each
        /// newly received collector is switched to it here.
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

                CollectorPresentation presentation = collector.GetComponent<CollectorPresentation>();
                if (presentation != null)
                {
                    presentation.ShowWaitingBackIdle();
                    presentation.ClearPresentationDepth();
                }
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
        /// Marks (or unmarks) a currently-held collector as reserved for
        /// departure — set by CollectorSelectionController the instant a
        /// Recovery Row collector is tapped, before its visual travel back
        /// onto the Conveyor even starts, and cleared automatically once
        /// ReleaseCollector actually removes it (or on a rollback, so no
        /// reservation ever leaks past the attempt that set it). Purely a
        /// presentation exclusion — see the field's own remarks; never
        /// affects Collectors/Count.
        /// </summary>
        public void SetReservedForDeparture(ConveyorRider rider, bool reserved)
        {
            if (rider == null)
                return;

            if (reserved)
                _reservedForDeparture.Add(rider);
            else
                _reservedForDeparture.Remove(rider);
        }

        /// <summary>True while rider is reserved for departure — see SetReservedForDeparture. Read by RecoveryRowView to exclude it from row layout.</summary>
        public bool IsReservedForDeparture(ConveyorRider rider) => rider != null && _reservedForDeparture.Contains(rider);

        /// <summary>
        /// Reapplies this row's own neutral (baseline) presentation depth —
        /// used only when CollectorSelectionController rolls back a
        /// boarding attempt (this row never actually released the view).
        /// </summary>
        void ICollectorSource.RestorePresentationDepth(CollectorView collectorView) => collectorView?.Presentation.ClearPresentationDepth();

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
            if (rider == null)
                return false;

            // Cleared unconditionally, before the removal outcome is known,
            // so a reservation never survives either a successful release
            // or the rare rollback where this returns false — see
            // SetReservedForDeparture's own remarks.
            _reservedForDeparture.Remove(rider);

            if (!_collectors.Remove(rider))
                return false;

            CollectorsChanged?.Invoke();
            return true;
        }
    }
}
