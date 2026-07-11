using Project001.Gameplay.Pixels;
using UnityEngine;

namespace Project001.Gameplay.Failure
{
    /// <summary>
    /// Reacts only to explicit notification from CollectorLifecycle that an
    /// unsatisfied collector completed a lap with no free Waiting Line slot.
    /// Triggers the prototype failure exactly once, and only if PixelGrid
    /// still has active pixels — otherwise the level was already won. Holds
    /// no per-frame rider inspection, Waiting Line search, movement, or
    /// destruction logic of its own.
    /// </summary>
    public class FailureController : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid checked for remaining active pixels before failure is allowed to trigger.")]
        private PixelGrid pixelGrid;

        public bool HasFailed { get; private set; }

        /// <summary>
        /// Called when an unsatisfied collector completes a lap and finds
        /// every Waiting Line slot occupied. No-op if failure already
        /// triggered, or if PixelGrid is already complete — victory takes
        /// precedence over failure.
        /// </summary>
        public void NotifyWaitingLineFull()
        {
            if (HasFailed)
                return;

            if (pixelGrid == null || pixelGrid.IsComplete)
                return;

            HasFailed = true;
            Debug.Log("Failure!");
        }
    }
}
