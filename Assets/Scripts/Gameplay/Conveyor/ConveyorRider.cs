using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// A single object currently occupying the conveyor. Holds no progress,
    /// input, queue, or capacity logic of its own — ConveyorSystem owns its
    /// path progress and drives its position via SetPosition. Also carries the
    /// minimal food colour/type state a consumer needs to match pixels; it
    /// never searches for pixels itself.
    /// </summary>
    public class ConveyorRider : MonoBehaviour
    {
        public Color FoodColor { get; private set; }

        /// <summary>
        /// True while this rider is currently riding a conveyor. Read-only from
        /// the outside; only ConveyorSystem changes it, via EnterRiding/ExitRiding.
        /// </summary>
        public bool IsRiding { get; private set; }

        public void Initialize(Color foodColor)
        {
            FoodColor = foodColor;
        }

        public void SetPosition(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        internal void EnterRiding()
        {
            IsRiding = true;
        }

        internal void ExitRiding()
        {
            IsRiding = false;
        }
    }
}
