namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Minimal abstraction over anywhere a CollectorView can be selected from
    /// and boarded onto a conveyor — a queue's front position or an occupied
    /// waiting slot. Exposes no internal queue or slot collections; only
    /// whether a view is currently selectable, and how to release it once
    /// boarding has already succeeded elsewhere.
    /// </summary>
    public interface ICollectorSource
    {
        /// <summary>
        /// True when the given CollectorView currently belongs to and is
        /// selectable from this source.
        /// </summary>
        bool CanSelect(CollectorView collectorView);

        /// <summary>
        /// Finalizes removal of a CollectorView from this source after it has
        /// already successfully boarded a conveyor. Must only be called once
        /// boarding has succeeded. Returns false if the view is no longer
        /// present in this source.
        /// </summary>
        bool ReleaseCollector(CollectorView collectorView);

        /// <summary>
        /// Restores the given CollectorView's presentation depth (see
        /// CollectorAnimation.SetPresentationDepth) to whatever this
        /// source's own state implies — a queue re-derives the view's
        /// current row index and reapplies that row's genuine depth,
        /// WaitingLine/RecoveryRow just reapply their own neutral (baseline)
        /// depth. Called by CollectorSelectionController only when a
        /// boarding attempt is rolled back (the source never actually
        /// released the view): boarding clears queue-row depth the instant
        /// it starts, so a rolled-back collector must have its real source
        /// depth put back, or it would stay stuck at the Conveyor's neutral
        /// depth while visually back in its old queue row.
        /// </summary>
        void RestorePresentationDepth(CollectorView collectorView);
    }
}
