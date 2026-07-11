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
    }
}
