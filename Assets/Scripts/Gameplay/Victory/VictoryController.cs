using Project001.Gameplay.Pixels;
using UnityEngine;

namespace Project001.Gameplay.Victory
{
    /// <summary>
    /// Observes a PixelGrid and triggers the prototype victory exactly once,
    /// on the first frame its IsComplete becomes true. Makes no gameplay
    /// decisions of its own beyond that detection — no UI, animation, or
    /// scene-loading logic.
    /// </summary>
    public class VictoryController : MonoBehaviour
    {
        [SerializeField, Tooltip("Grid whose completion state is observed.")]
        private PixelGrid pixelGrid;

        private bool _victoryTriggered;

        private void Update()
        {
            if (_victoryTriggered || pixelGrid == null)
                return;

            if (!pixelGrid.IsComplete)
                return;

            _victoryTriggered = true;
            Debug.Log("Victory!");
        }
    }
}
