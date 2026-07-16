using System;
using Project001.Gameplay.Pixels;
using UnityEngine;

namespace Project001.Gameplay.Victory
{
    /// <summary>
    /// Observes a PixelGrid and triggers the prototype victory exactly once,
    /// on the first frame its IsComplete becomes true. Makes no gameplay
    /// decisions of its own beyond that detection — no UI, animation, or
    /// scene-loading logic. Notifies listeners solely via OnVictory; this
    /// class has no knowledge of Canvas, Button, Text, TMP, Images, or any
    /// other presentation concern, and never will — presentation depends on
    /// this event, this event never depends on presentation.
    /// </summary>
    public class VictoryController : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid whose completion state is observed.")]
        private PixelGrid pixelGrid;

        private bool _victoryTriggered;

        /// <summary>
        /// Raised exactly once, the first frame pixelGrid.IsComplete becomes
        /// true. The sole notification channel presentation (e.g. a Victory
        /// UI) should use instead of polling IsComplete or this component.
        /// </summary>
        public event Action OnVictory;

        private void Update()
        {
            if (_victoryTriggered || pixelGrid == null)
                return;

            if (!pixelGrid.IsComplete)
                return;

            _victoryTriggered = true;
            Debug.Log("Victory!");
            OnVictory?.Invoke();
        }
    }
}
