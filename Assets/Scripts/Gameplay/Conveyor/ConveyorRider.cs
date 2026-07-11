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

        public int HungerCapacity { get; private set; }

        public int RemainingHunger { get; private set; }

        public bool IsSatisfied => RemainingHunger == 0;

        /// <summary>
        /// True while this rider is currently riding a conveyor. Read-only from
        /// the outside; only ConveyorSystem changes it, via EnterRiding/ExitRiding.
        /// </summary>
        public bool IsRiding { get; private set; }

        public void Initialize(Color foodColor, int hungerCapacity)
        {
            FoodColor = foodColor;
            HungerCapacity = Mathf.Max(0, hungerCapacity);
            RemainingHunger = HungerCapacity;
        }

        public void SetPosition(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }

        /// <summary>
        /// Registers one successfully consumed matching pixel. RemainingHunger
        /// never drops below zero.
        /// </summary>
        public void RegisterConsumedPixel()
        {
            RemainingHunger = Mathf.Max(0, RemainingHunger - 1);
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
