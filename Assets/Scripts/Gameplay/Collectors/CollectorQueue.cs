using System.Collections.Generic;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// An ordered queue of CollectorView objects. Holds no input or movement logic.
    /// </summary>
    public class CollectorQueue
    {
        private readonly List<CollectorView> _views = new List<CollectorView>();

        public CollectorView FirstAvailable => _views.Count > 0 ? _views[0] : null;

        public void Add(CollectorView view)
        {
            _views.Add(view);
        }
    }
}
