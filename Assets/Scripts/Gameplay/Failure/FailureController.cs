using System;
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
    /// destruction logic of its own. Notifies listeners solely via
    /// OnFailure; this class has no knowledge of Canvas, Button, Text,
    /// Images, VictoryUI, or any other presentation concern, and never will
    /// — presentation depends on this event, this event never depends on
    /// presentation.
    /// </summary>
    public class FailureController : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid checked for remaining active pixels before failure is allowed to trigger.")]
        private PixelGrid pixelGrid;

        public bool HasFailed { get; private set; }

        /// <summary>
        /// Raised exactly once, the same moment HasFailed becomes true. The
        /// sole notification channel presentation (e.g. a Failure UI) should
        /// use instead of polling HasFailed or this component.
        /// </summary>
        public event Action OnFailure;

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
            OnFailure?.Invoke();
        }

        /// <summary>
        /// Rearms Failure detection for a new attempt within the same level
        /// (e.g. after the player chooses Continue rather than a full
        /// restart), so a later Waiting Line overflow can trigger OnFailure
        /// again. Only resets HasFailed — never touches PixelGrid, Waiting
        /// Line, queues, hunger, or any other gameplay state.
        /// </summary>
        public void ResetFailure()
        {
            HasFailed = false;
        }
    }
}
