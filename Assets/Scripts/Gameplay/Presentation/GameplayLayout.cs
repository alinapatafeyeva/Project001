using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Single source of truth for the gameplay scene's composition: the
    /// authored size of each region (PixelGrid, Conveyor, the shared
    /// WaitingLine/Recovery row, CollectorQueueBoard), the spacing between
    /// them, the authored presentation tokens every collector-related
    /// component renders at, and the padding the camera frame reserves
    /// around the whole stack. Every value here is a deliberate design
    /// allocation, not a measurement of any specific level's actual content
    /// and not a value solved back from a reference screenshot — the
    /// composition is fixed by this class alone.
    ///
    /// These presentation tokens are deliberately separate values, not one
    /// overloaded size: a collector's own rendered scale
    /// (CollectorSpriteScale), the actual visible extent that scale produces
    /// (CollectorVisibleWidth/Height, since the imported sprite does not
    /// fill its scale square), WaitingLine's own slot size
    /// (WaitingSlotSize), and the vertical rhythm of CollectorQueueBoard's
    /// rows (QueueRowStep) are four different visual concerns that happen to
    /// have shared one number before — coupling them meant a change aimed at
    /// one (e.g. making Mofu bigger) silently distorted the other three
    /// (giant WaitingLine squares, an oversized hunger label, an
    /// unnecessarily tall board).
    ///
    /// PixelGrid stays centered at the world origin; every row below it is
    /// derived by stacking downward from GridRegionHeight, the authored gaps,
    /// and each row's own authored height — never a separately hand-typed Y
    /// position. Conveyor is centered on that same origin, sized to fully
    /// enclose PixelGrid plus GridToClusterSpacing of clearance on every side
    /// (see ConveyorSize).
    ///
    /// A level's actual generated content (grid dimensions, queue depth) is
    /// never consulted here and never changes any of these values.
    /// PixelGrid still scales itself to fit inside GridRegionWidth x
    /// GridRegionHeight (capped at GridMaximumCellSize) exactly as before;
    /// a level whose CollectorQueueBoard queues run deeper than
    /// CollectorQueueBoardRegionHeight simply extends past that region's
    /// bottom edge rather than the camera compensating — a content
    /// authoring constraint, not something this class or the camera solves.
    /// </summary>
    public static class GameplayLayout
    {
        // ----- PixelGrid's authored region ---------------------------------
        // Assigned directly to PixelGrid's availableWidth/availableHeight/
        // maximumCellSize fields by BootstrapSceneCreator.
        public const float GridRegionWidth = 6f;
        public const float GridRegionHeight = 6f;
        public const float GridMaximumCellSize = 0.95f;

        // ----- Collector sprite scale and its actual visible extent ---------
        // CollectorSpriteScale controls only the rendered Mofu visual (the
        // world-space localScale CollectorQueueBoard assigns to a collector's
        // root, which every other collector — WaitingLine occupant, Recovery
        // Row occupant, Conveyor rider — keeps for the rest of its lifetime,
        // since none of them ever rescale it). It does NOT mean the visible
        // character occupies a square of that size: the imported Mofu sprite
        // rect (575x632 px at 800 PPU) only covers 0.71875 x 0.79 world units
        // at scale 1, so at CollectorSpriteScale the actual rendered
        // character is CollectorVisibleWidth x CollectorVisibleHeight, never
        // a full CollectorSpriteScale square. Every camera or board footprint
        // calculation that needs the visible character's actual extent
        // (Conveyor rider clearance, queue row placement) reads
        // CollectorVisibleWidth/Height, never CollectorSpriteScale directly.
        public const float CollectorSpriteScale = 1.9f;

        private const float MofuVisibleWidthRatio = 575f / 800f;
        private const float MofuVisibleHeightRatio = 632f / 800f;

        public static float CollectorVisibleWidth => CollectorSpriteScale * MofuVisibleWidthRatio;

        public static float CollectorVisibleHeight => CollectorSpriteScale * MofuVisibleHeightRatio;

        // ----- Conveyor rider spacing -----------------------------------------
        // The minimum path-progress (arc-length) distance ConveyorSystem must
        // keep between the boarding point and every already-riding collector
        // before it lets another one board (see ConveyorSystem.
        // boardingClearance, assigned this value by BootstrapSceneCreator).
        // Two riders that board with at least this much arc-length between
        // them keep exactly that separation forever afterward — every rider
        // moves at the same fixed speed, so their relative spacing along the
        // path never changes once boarding is behind them, on straight
        // sections and rounded corners alike.
        //
        // Derived from the rendered footprint (CollectorVisibleHeight, the
        // larger of the two visible-extent tokens above — the model's own
        // mesh footprint is close to circular, see Mofu3DSetup, so either
        // axis is a reasonable stand-in) plus ConveyorRiderVisualGap, an
        // explicit breathing-room margin sized to comfortably absorb the
        // small chord-vs-arc "squeeze" a tight corner introduces (two points
        // separated by a given arc length sit slightly closer together in a
        // straight line while both are inside the same rounded corner) — at
        // ConveyorPath's authored cornerRadius (1 world unit, see
        // BootstrapSceneCreator.CreateConveyor), the worst case is only
        // about a 10% reduction, well inside this margin.
        public const float ConveyorRiderVisualGap = 0.35f;

        public static float ConveyorRiderMinimumSpacing => CollectorVisibleHeight + ConveyorRiderVisualGap;

        // ----- WaitingLine's own presentation tokens -------------------------
        // WaitingSlotSize is independent of CollectorSpriteScale: WaitingLine
        // only ever snaps an arriving collector to a slot's world position
        // (see CollectorLifecycle.ResolveLap), never its scale, so the slot's
        // own outline size is a free visual-design choice — a marker the
        // collector lands on, not a bounding box it must fully fill. Sized
        // here to frame a collector without producing an oversized square,
        // and small enough that a full WaitingLineCapacity-wide row plus
        // WaitingSlotSpacing comfortably fits inside CameraFrameWidth (see
        // the verification in GameplayLayout's audit trail — 5 slots at this
        // size and spacing total 7.2 world units, well inside the ~9.27-unit
        // frame).
        public const float WaitingSlotSize = 1.2f;
        public const float WaitingSlotSpacing = 0.3f;

        // ----- Queue row rhythm -----------------------------------------------
        // QueueRowStep is the vertical distance between consecutive
        // CollectorQueueBoard row centers, built from the collector's actual
        // visible height (not CollectorSpriteScale) plus an explicit,
        // authored visible gap — so the gap between two rendered Mofu
        // sprites is exactly QueueVisibleGap, regardless of how much empty
        // margin CollectorSpriteScale's square itself has.
        public const float QueueVisibleGap = 0.15f;

        public static float QueueRowStep => CollectorVisibleHeight + QueueVisibleGap;

        // CollectorQueueBoardRegionHeight is a deliberate design allocation,
        // not a measurement of any level's actual queue depth: it reserves
        // room for ReservedQueueRowCount rows using the actual row geometry
        // above (QueueRowStep, CollectorVisibleHeight) — exactly covering
        // both approved levels today (level_001: 3 rows, level_002: 4 rows).
        // A future level whose deepest queue exceeds this count will render
        // that queue extending past this region's bottom edge; that is a
        // content authoring constraint for that level, not something the
        // camera dynamically compensates for.
        private const int ReservedQueueRowCount = 4;

        public static float CollectorQueueBoardRegionHeight =>
            (ReservedQueueRowCount - 1) * QueueRowStep + CollectorVisibleHeight;

        // ----- Hunger label's own presentation token -------------------------
        // The world-space size a collector's RemainingHunger label renders
        // at, regardless of CollectorSpriteScale. CollectorView applies this
        // via an inverse-scale compensation on the label's own child
        // transform, so a bigger Mofu never produces a bigger hunger number.
        public const float HungerLabelWorldSize = 0.12f;

        // ----- Spacing and padding: the rest of the composition ------------
        // GridToClusterSpacing is the single gap between PixelGrid — the
        // primary play surface — and the Conveyor's own margin (see
        // ConveyorSize): the Conveyor's bottom straightaway runs exactly
        // GridToClusterSpacing below PixelGrid, which is why the gap and the
        // margin share one authored value instead of two numbers that would
        // otherwise need to be kept in sync by hand.
        //
        // ConveyorToWaitingLineGap is the single explicit, positive gap
        // between the lowest visible edge of a Conveyor rider travelling the
        // bottom straightaway and WaitingLine's own top edge — not the
        // Conveyor path line itself. A rider is centered on the path, so its
        // visible bottom reaches CollectorVisibleHeight*0.5 below that line
        // (see WaitingLinePositionY); this gap is purely the breathing room
        // beyond that, never the distance to the path line.
        //
        // ClusterInnerSpacing is the single explicit, positive gap between
        // WaitingLine and CollectorQueueBoard's first row — the two still
        // read as one connected lower cluster (a collector's path runs
        // directly from one to the other) without the first queue row ever
        // intruding into WaitingLine's own footprint.
        //
        // Top/BottomCompositionPadding are the camera's reserved breathing
        // room above the Conveyor (including a rider on its top edge) and
        // below the collector board. HorizontalCompositionPadding is the
        // camera's reserved breathing room to either side of the Conveyor
        // (including a rider on its side edges) — see CameraFrameWidth.
        public const float GridToClusterSpacing = 0.8f;
        public const float ConveyorToWaitingLineGap = 0.3f;
        public const float ClusterInnerSpacing = 0.2f;
        public const float TopCompositionPadding = 0.2f;
        public const float BottomCompositionPadding = 0.2f;
        public const float HorizontalCompositionPadding = 0.15f;

        public const float PixelGridPositionY = 0f;

        // ----- Conveyor's authored extent ------------------------------------
        // Square, centered on PixelGridPositionY (the world origin), sized so
        // its edges sit exactly GridToClusterSpacing away from PixelGrid's
        // own edges on every side. Grid width / ConveyorSize therefore always
        // equals GridRegionWidth / (GridRegionHeight + GridToClusterSpacing*2)
        // = 6 / 7.6 ≈ 0.79 — comfortably inside the "grid reads as ~70-80% of
        // the Conveyor's inner width" composition target. BootstrapSceneCreator
        // assigns this directly to ConveyorPath's width/height, so the actual
        // authored Conveyor and the camera frame below can never drift apart.
        public static float ConveyorSize => GridRegionHeight + GridToClusterSpacing * 2f;

        private static float ConveyorHalfSize => ConveyorSize * 0.5f;

        /// <summary>
        /// WaitingLine's center. A rider on the Conveyor's bottom
        /// straightaway is centered on the path line (-ConveyorHalfSize), not
        /// resting on top of it, so its own visible bottom edge reaches a
        /// further CollectorVisibleHeight*0.5 below that line — the sprite's
        /// actual visible extent, not CollectorSpriteScale (a transform scale
        /// is not the visible sprite height; see CollectorVisibleHeight).
        /// WaitingLine's top edge sits ConveyorToWaitingLineGap below THAT
        /// visible edge, an explicit positive gap rather than the two
        /// touching directly.
        /// </summary>
        public static float WaitingLinePositionY =>
            -ConveyorHalfSize - CollectorVisibleHeight * 0.5f - ConveyorToWaitingLineGap - WaitingSlotSize * 0.5f;

        /// <summary>
        /// Unlike WaitingLinePositionY, this is the board region's true top
        /// edge, not a center — CollectorQueueBoard places row 0's own center
        /// half a CollectorVisibleHeight below this value (see
        /// CollectorQueueBoard.GenerateBoard), so the row's actual visible
        /// top edge lands exactly here, ClusterInnerSpacing below
        /// WaitingLine's bottom edge. Treating this value as a center instead
        /// (a previous bug) silently ate part of that gap and let the first
        /// queue row intrude into WaitingLine.
        /// </summary>
        public static float CollectorQueueBoardPositionY =>
            WaitingLinePositionY - WaitingSlotSize * 0.5f - ClusterInnerSpacing;

        // ----- The fixed camera frame ---------------------------------------
        // Derived entirely from the authored allocations above — never from
        // Renderer.bounds, a level's actual grid dimensions, or how many
        // collectors currently exist. Composition is fixed; only the
        // resulting orthographic size adapts, per screen aspect ratio.
        //
        // CameraFrameTop is derived from the Conveyor's own authored extent
        // (ConveyorHalfSize), not from GridRegionHeight: the Conveyor always
        // encloses PixelGrid (ConveyorHalfSize = grid half-height +
        // GridToClusterSpacing), so a frame that clears the Conveyor's top
        // edge always clears the grid's top edge too. A collector riding the
        // Conveyor's top edge is centered exactly on that edge, so its own
        // visible top reaches CollectorVisibleHeight*0.5 further up (the
        // sprite's actual visible extent, not CollectorSpriteScale) — that
        // term is what guarantees a travelling rider is never clipped.
        private static float CameraFrameTop =>
            ConveyorHalfSize + CollectorVisibleHeight * 0.5f + TopCompositionPadding;

        private static float CameraFrameBottom =>
            CollectorQueueBoardPositionY - CollectorQueueBoardRegionHeight - BottomCompositionPadding;

        private static float CameraFrameHeight => CameraFrameTop - CameraFrameBottom;

        /// <summary>
        /// Derived from the Conveyor's authored extent the same way
        /// CameraFrameTop is: a rider travelling the Conveyor's side edges
        /// reaches ConveyorHalfSize + CollectorVisibleWidth*0.5 from center
        /// (the sprite's actual visible width, not CollectorSpriteScale),
        /// plus HorizontalCompositionPadding of breathing room on each side.
        /// </summary>
        private static float CameraFrameWidth =>
            (ConveyorHalfSize + CollectorVisibleWidth * 0.5f + HorizontalCompositionPadding) * 2f;

        /// <summary>
        /// Fixed vertical camera center — the frame's own midpoint, entirely
        /// determined by the authored allocations above.
        /// </summary>
        public static float CameraVerticalCenter => (CameraFrameTop + CameraFrameBottom) * 0.5f;

        /// <summary>
        /// Orthographic size that fits the fixed camera frame at the given
        /// screen aspect ratio — the max of a width-fit and a height-fit, so
        /// a narrow/tall screen is bound by width and a squarer one is
        /// bound by height, with letterboxing on whichever axis has slack.
        /// Composition (what the frame contains) never changes; only this
        /// size adapts, per aspect ratio.
        /// </summary>
        public static float ComputeOrthographicSize(float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f)
                return CameraFrameHeight * 0.5f;

            float aspect = screenWidth / screenHeight;
            float sizeToFitWidth = CameraFrameWidth / (2f * aspect);
            float sizeToFitHeight = CameraFrameHeight * 0.5f;
            return Mathf.Max(sizeToFitWidth, sizeToFitHeight);
        }
    }
}
