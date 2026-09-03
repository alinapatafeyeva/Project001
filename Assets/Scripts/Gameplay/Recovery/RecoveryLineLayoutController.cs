using Project001.Gameplay.Presentation;
using UnityEngine;

namespace Project001.Gameplay.Recovery
{
    /// <summary>
    /// Expands/collapses the vertical gap between the normal Waiting Line
    /// and everything below it (lowerContentRoot — CollectorQueueBoard's own
    /// root, see BootstrapSceneCreator.CreateRecoveryLineLayoutController)
    /// so an occupied Recovery Row gets real physical space instead of
    /// sharing Waiting Line's own row.
    ///
    /// The source of truth is exclusively RecoveryRowController.Collectors.Count
    /// — never whether Save me was pressed, never a separate "is expanded"
    /// flag this class invents. Save me is one way Recovery Row can become
    /// occupied; the player launching a held collector back onto the
    /// Conveyor (RecoveryRowController's own ICollectorSource.ReleaseCollector)
    /// is the other direction, and both already raise the exact same
    /// CollectorsChanged event this class subscribes to — no new Recovery
    /// Line event was needed.
    ///
    /// lowerContentRoot only ever moves as one rigid unit: every row
    /// CollectorQueueBoard itself lays out is already positioned in that
    /// board's own LOCAL space (see CollectorQueueBoard.RowLocalPosition),
    /// so translating the shared root here moves the whole formation
    /// together with zero change to any row's relative spacing — the same
    /// "shift the composition, not its parts" principle
    /// GameplayLayout.TopCompositionPadding already established for the
    /// camera side of this composition.
    ///
    /// Deliberately NOT a change to GameplayLayout's own static formulas
    /// (WaitingLinePositionY, CollectorQueueBoardPositionY stay exactly as
    /// authored): those describe the STATE A / baseline composition only —
    /// a level's fixed, authored layout, never a measurement of live
    /// gameplay state (see GameplayLayout's own class remarks). This
    /// controller is a thin runtime presentation layer on top of that fixed
    /// baseline, exactly like CollectorAnimation's own presentation-depth
    /// pulls sit on top of the fixed Z=0 gameplay plane without changing it.
    /// </summary>
    public class RecoveryLineLayoutController : MonoBehaviour
    {
        [SerializeField, Tooltip("Recovery Row whose occupancy (Collectors.Count) this controller watches — never inferred from Save me having been pressed.")]
        private RecoveryRowController recoveryRowController;

        [SerializeField, Tooltip("The single root moved when Recovery Row becomes occupied — CollectorQueueBoard's own Transform. Everything parented under it (every queue row) inherits the shift automatically.")]
        private Transform lowerContentRoot;

        // Captured once in Awake, before any expansion is ever applied, so
        // every later Refresh() computes an ABSOLUTE target position from
        // this fixed baseline rather than an incremental delta — the reason
        // 2+ Recovery Row collectors never stack additional offset (Refresh
        // recomputes the same "baseline - QueueRowStep" target every time,
        // regardless of how many times CollectorsChanged has already fired
        // while still occupied) and why collapsing always restores the
        // EXACT original position rather than accumulating rounding drift.
        private Vector3 _baselinePosition;
        private bool _hasBaseline;

        private void Awake()
        {
            if (lowerContentRoot != null)
            {
                _baselinePosition = lowerContentRoot.position;
                _hasBaseline = true;
            }

            if (recoveryRowController != null)
                recoveryRowController.CollectorsChanged += Refresh;

            Refresh();
        }

        private void OnDestroy()
        {
            if (recoveryRowController != null)
                recoveryRowController.CollectorsChanged -= Refresh;
        }

        /// <summary>
        /// The Y delta applied on top of the captured baseline while
        /// Recovery Row is occupied — derived so the resulting ABSOLUTE
        /// lower-content Y sits exactly GameplayLayout.RecoveryToCollectorsGap
        /// (Gap B) below Recovery Row's own fixed row Y
        /// (GameplayLayout.WaitingLinePositionY - GameplayLayout.
        /// WaitingToRecoveryGap — the exact position BootstrapSceneCreator.
        /// CreateRecoveryRow bakes Recovery Row's own Transform to), measured
        /// against lowerContentRoot's own AUTHORED baseline
        /// (GameplayLayout.CollectorQueueBoardPositionY — the position
        /// BootstrapSceneCreator.CreateCollectorQueueBoard bakes it to, and
        /// what _baselinePosition captures in Awake, since nothing moves
        /// lowerContentRoot before then) — never a hand-picked shift
        /// distance. This is what makes Gap B independently tunable at all:
        /// see GameplayLayout.RecoveryToCollectorsGap's own remarks for why
        /// a single shared shift amount (the previous approach) could only
        /// ever change Gap A, leaving Gap B mathematically stuck.
        /// </summary>
        private static float ExpandedOffsetY =>
            (GameplayLayout.WaitingLinePositionY - GameplayLayout.WaitingToRecoveryGap - GameplayLayout.RecoveryToCollectorsGap)
            - GameplayLayout.CollectorQueueBoardPositionY;

        /// <summary>
        /// Recomputes lowerContentRoot's position from scratch: the
        /// captured baseline, shifted down by ExpandedOffsetY whenever
        /// Recovery Row holds at least one collector, or back to the
        /// untouched baseline (offset exactly 0) the instant it holds none —
        /// an ABSOLUTE target recomputed from GameplayLayout's own constants
        /// every call, never an incremental delta, so 2+ Recovery Row
        /// collectors never stack additional offset and collapsing always
        /// restores the exact original position (see _baselinePosition's
        /// own remarks).
        /// </summary>
        private void Refresh()
        {
            if (!_hasBaseline || recoveryRowController == null)
                return;

            bool isOccupied = recoveryRowController.Collectors.Count > 0;
            float offsetY = isOccupied ? ExpandedOffsetY : 0f;
            lowerContentRoot.position = _baselinePosition + new Vector3(0f, offsetY, 0f);
        }
    }
}
