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

        public IReadOnlyList<CollectorView> Views => _views;

        public void Add(CollectorView view)
        {
            _views.Add(view);
        }

        public bool IsFirstAvailable(CollectorView view)
        {
            return view != null && FirstAvailable == view;
        }

        /// <summary>
        /// Removes the given view only if it is currently FirstAvailable.
        /// Preserves the order of all remaining collectors.
        /// </summary>
        public bool TryRemoveFirstAvailable(CollectorView view)
        {
            if (!IsFirstAvailable(view))
                return false;

            _views.RemoveAt(0);
            return true;
        }
    }
}
