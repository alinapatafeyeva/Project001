using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Decides which of this collector's two sprites (queue back-idle,
    /// gameplay front-idle) is currently shown, and asks CollectorView to
    /// apply it. Holds no rendering logic itself — CollectorView owns the
    /// SpriteRenderer and is the only thing that ever touches it.
    ///
    /// The two sprites come from whichever MonsterSkin CollectorQueueBoard
    /// resolved for this collector's MonsterColor (see
    /// CollectorQueueBoard.GenerateBoard) — this component only ever holds
    /// and switches between them, it never looks up a skin itself.
    ///
    /// Supports exactly two poses: back-idle (shown only while queued on
    /// CollectorQueueBoard) and front-idle (shown from the moment the
    /// collector boards the Conveyor onward, including Waiting Line and
    /// Recovery Row). Eating/Satisfied sprites and Heart are not switched to
    /// by anything yet — that is future work, not handled here.
    /// </summary>
    public class CollectorPresentation : MonoBehaviour
    {
        private Sprite _backSprite;
        private Sprite _frontSprite;
        private CollectorView _view;

        private CollectorView View => _view != null ? _view : _view = GetComponent<CollectorView>();

        /// <summary>
        /// Assigns the two sprites this collector may display. Collectors are
        /// built at runtime (CollectorQueueBoard.GenerateBoard), never from a
        /// prefab, so there is no Inspector session for this component to
        /// deserialize values into directly — the real, Inspector-assigned
        /// sprites live on the MonsterSkin CollectorQueueBoard resolved for
        /// this collector, and are injected here exactly once, the same way
        /// ConveyorRider, PixelConsumer, and CollectorLifecycle are each
        /// initialized.
        /// </summary>
        public void Initialize(Sprite backSprite, Sprite frontSprite)
        {
            _backSprite = backSprite;
            _frontSprite = frontSprite;
        }

        /// <summary>
        /// Shows the back-idle pose. Called once, by CollectorQueueBoard,
        /// right after Initialize — this is the only pose a collector shows
        /// while queued on CollectorQueueBoard.
        /// </summary>
        public void ShowQueueBack()
        {
            View.ApplySprite(_backSprite);
        }

        /// <summary>
        /// Shows the front-idle pose. Called by whoever boards this collector
        /// onto the Conveyor (CollectorSelectionController), the moment it
        /// leaves CollectorQueueBoard. The collector stays front-facing for
        /// the rest of its lifecycle (Waiting Line, Recovery Row) — nothing
        /// switches it back to ShowQueueBack() afterwards.
        /// </summary>
        public void ShowGameplayFront()
        {
            View.ApplySprite(_frontSprite);
        }
    }
}
