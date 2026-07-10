using Project001.Gameplay.Collectors;
using UnityEngine;

namespace Project001.Gameplay.WaitingLine
{
    /// <summary>
    /// A single waiting position for a Collector. Tracks occupancy only;
    /// holds no movement, animation, input, or collection logic.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class WaitingSlot : MonoBehaviour
    {
        private CollectorView _occupant;

        public bool IsOccupied => _occupant != null;

        public CollectorView Occupant => _occupant;

        public bool Assign(CollectorView collectorView)
        {
            if (collectorView == null || IsOccupied)
                return false;

            _occupant = collectorView;
            return true;
        }

        public void Clear()
        {
            _occupant = null;
        }

        /// <summary>
        /// Clears the slot only if it currently holds the given collector.
        /// </summary>
        public bool ClearIfOccupant(CollectorView collectorView)
        {
            if (collectorView == null || _occupant != collectorView)
                return false;

            _occupant = null;
            return true;
        }
    }
}
