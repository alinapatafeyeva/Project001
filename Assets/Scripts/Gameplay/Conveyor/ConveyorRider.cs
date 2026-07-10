using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// A single object currently occupying the conveyor. Holds no progress,
    /// input, queue, or capacity logic of its own — ConveyorSystem owns its
    /// path progress and drives its position via SetPosition.
    /// </summary>
    public class ConveyorRider : MonoBehaviour
    {
        public void SetPosition(Vector3 worldPosition)
        {
            transform.position = worldPosition;
        }
    }
}
