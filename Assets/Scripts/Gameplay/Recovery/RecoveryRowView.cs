using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Recovery
{
    /// <summary>
    /// Presentation for a RecoveryRowController: reparents its collectors
    /// under this transform and lays them out as a centered horizontal row,
    /// same formula as WaitingLine.GenerateSlots — spacing scales with
    /// however many collectors the row currently holds, so it grows to fit
    /// the Conveyor's actual occupancy at the time of transfer (including
    /// future capacity upgrades) rather than a fixed slot count. Displays the
    /// same ConveyorRider instances the controller holds — never clones or
    /// recreates them. Refreshes automatically whenever
    /// recoveryRowController.CollectorsChanged fires (a successful receive or
    /// release); never polls or searches for the controller itself.
    /// </summary>
    public class RecoveryRowView : MonoBehaviour
    {
        [SerializeField, Tooltip("Recovery Row whose held collectors this view displays.")]
        private RecoveryRowController recoveryRowController;

        [SerializeField, Tooltip("World-space size of a single collector, in world units.")]
        [Min(0.01f)]
        private float collectorSize = 1f;

        [SerializeField, Tooltip("Gap between neighbouring collectors, in world units.")]
        [Min(0f)]
        private float horizontalSpacing = 0.3f;

        private void OnEnable()
        {
            if (recoveryRowController == null)
                return;

            recoveryRowController.CollectorsChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (recoveryRowController != null)
                recoveryRowController.CollectorsChanged -= Refresh;
        }

        /// <summary>
        /// Reparents every collector currently held by recoveryRowController
        /// under this transform and centers them along a single horizontal
        /// row. Safe to call any number of times; a collector already
        /// parented here is simply repositioned.
        /// </summary>
        public void Refresh()
        {
            if (recoveryRowController == null)
                return;

            IReadOnlyList<ConveyorRider> collectors = recoveryRowController.Collectors;

            float stepX = collectorSize + horizontalSpacing;
            float offsetX = (collectors.Count - 1) * stepX * 0.5f;

            for (int index = 0; index < collectors.Count; index++)
            {
                ConveyorRider collector = collectors[index];
                if (collector == null)
                    continue;

                Transform collectorTransform = collector.transform;
                collectorTransform.SetParent(transform, true);
                collectorTransform.localPosition = new Vector3(index * stepX - offsetX, 0f, 0f);
            }
        }
    }
}
