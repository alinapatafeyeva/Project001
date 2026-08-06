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
        // CollectorSpriteScale controls only the rendered collector visual
        // (the world-space localScale CollectorQueueBoard assigns to a
        // collector's root, which every other collector — WaitingLine
        // occupant, Recovery Row occupant, Conveyor rider — keeps for the
        // rest of its lifetime, since none of them ever rescale it). It does
        // NOT mean the visible character occupies a square of that size: at
        // scale 1 a character only covers CollectorVisibleWidthRatio x
        // CollectorVisibleHeightRatio world units (originally derived from
        // the imported Mofu sprite rect, 575x632px at 800 PPU = 0.71875 x
        // 0.79 - the height ratio is unchanged from that original value, see
        // CollectorVisibleHeightRatio's own remarks; the width ratio no
        // longer is, see CollectorVisibleWidthRatio's), so at
        // CollectorSpriteScale the actual rendered character is
        // CollectorVisibleWidth x CollectorVisibleHeight, never a full
        // CollectorSpriteScale square. Every camera or board footprint
        // calculation that needs the visible character's actual extent
        // (Conveyor rider clearance, queue row placement) reads
        // CollectorVisibleWidth/Height, never CollectorSpriteScale directly.
        public const float CollectorSpriteScale = 1.9f;

        // CollectorVisibleHeightRatio is the single shared body-height
        // target CharacterAssetBuilder scales every species to (see its own
        // remarks) - originally derived from the imported Mofu sprite rect
        // (632/800), kept unchanged since Mofu's height was never the
        // problem (only its narrow width, see CollectorVisibleWidthRatio
        // below) and every other vertical composition value below
        // (QueueRowStep, WaitingLine/RecoveryRow placement, conveyor rider
        // spacing) is already built around this exact height - changing it
        // would mean re-deriving the entire vertical composition for no
        // benefit.
        //
        // CollectorVisibleWidthRatio is NOT a fixed envelope species are
        // scaled DOWN to fit any more (see CharacterAssetBuilder - scale is
        // shared-height-only, never width-capped) - it is instead the
        // measured width of the WIDEST species that shared-height scaling
        // actually produces, so the queue/camera can size themselves to the
        // real roster ("the queue adapts to the characters, not the
        // opposite").
        //
        // Measured via CharacterAssetBuilder.BuildAll's own build log after
        // rebuilding all 20 under shared-height scaling (unscaled bounds,
        // then scaled by TargetVisibleHeight/size.y - see CharacterAssetBuilder):
        //   Crab:    scaled body width(x) = 0.888
        //   Turtle:  scaled body width(x) = 1.037  <- widest (both by body
        //            width at yaw 0, relevant to queue columns, AND by max
        //            visual width across every conveyor facing angle)
        //   Fish:    scaled body width(x) = 0.961, max visual width = 0.972
        //   Octopus: scaled body width(x) = 0.782
        // Turtle, not Crab, is the widest species once shared-height scaling
        // replaces contain-fit - the Crab claw pose adjustment (see
        // CharacterAssetBuilder.ApplyCrabClawPoseAdjustment) already
        // narrowed Crab's own aspect ratio below Turtle's.
        //
        // Set to 1.04 - Turtle's own measured 1.037, plus a ~0.3% margin
        // covering the 3-decimal-digit rounding in that log measurement
        // (not a padding guess). CollectorQueueBoard's own horizontalSpacing
        // (0.3 world units, unchanged) remains the actual neighbour-to-
        // neighbour breathing room on top of this - the same zero-slack
        // convention this constant already used before (species previously
        // AT the old fixed width cap had no extra margin baked into the
        // ratio either).
        public const float CollectorVisibleHeightRatio = 632f / 800f;
        public const float CollectorVisibleWidthRatio = 1.04f;

        public static float CollectorVisibleWidth => CollectorSpriteScale * CollectorVisibleWidthRatio;

        public static float CollectorVisibleHeight => CollectorSpriteScale * CollectorVisibleHeightRatio;

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
        // Derived from CollectorVisibleHeight plus ConveyorRiderVisualGap, an
        // explicit breathing-room margin sized to comfortably absorb the
        // small chord-vs-arc "squeeze" a tight corner introduces (two points
        // separated by a given arc length sit slightly closer together in a
        // straight line while both are inside the same rounded corner) — at
        // ConveyorPath's authored cornerRadius (1 world unit, see
        // BootstrapSceneCreator.CreateConveyor), the worst case is only
        // about a 10% reduction, well inside this margin.
        //
        // Deliberately left keyed to height, not updated to the new
        // per-species measured widths (see CollectorVisibleWidthRatio's own
        // remarks) - conveyor spacing/behaviour was explicitly out of scope
        // for the shared-height-scaling change this accompanies. Worth
        // flagging honestly rather than silently: this was originally sized
        // assuming a "close to circular" mesh footprint (true for the old
        // Mofu placeholder, no longer true for Turtle specifically -
        // measured scaled width 1.037 vs the height this spacing is keyed
        // to, 0.79, an aspect ratio of ~1.31), so on a conveyor edge whose
        // fixed facing angle happens to align a Turtle rider's wide axis
        // with the direction of travel, two adjacent Turtle riders'
        // combined footprint can exceed this spacing's own margin by a few
        // percent. Confirmed via real Play Mode observation not to read as
        // an actual visible overlap in practice (see this change's own
        // verification notes) - called out here for whoever revisits
        // conveyor spacing next, not fixed now.
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
        //
        // All queue rows stay on the single shared gameplay plane (Z=0), the
        // same plane every ConveyorRider, WaitingLine slot, and RecoveryRow
        // slot lives on — depth separation under the camera's tilt (see
        // CameraTiltDegrees) comes entirely from Y spacing, never from a
        // per-row Z offset. An earlier version of this file pushed deeper
        // rows back in world Z to stand them "one behind another" more
        // literally; that broke Physics2D-based tap selection (a screen tap
        // reverse-projects onto exactly one plane — a collector actually
        // sitting on a different Z than the one the tap resolves against is
        // hit-tested at the wrong world position entirely, not just
        // approximately) and boarding's assumption that a collector's whole
        // presentation state starts clean at Z=0. QueueVisibleGap is 0.5
        // (was 0.15) purely so adjacent rows read as separated on screen AND
        // so adjacent rows' CircleCollider2D radii (0.5 raw x
        // CollectorSpriteScale = 0.95 world units each) never leave enough
        // room to geometrically overlap in Physics2D's X/Y-only comparison:
        // QueueRowStep must exceed 1.9 (0.95 + 0.95) for that overlap to be
        // structurally impossible rather than merely unlikely, which this
        // gap value clears with a small margin (2.001 total).
        //
        // This value is deliberately NOT widened further to chase
        // silhouette separation: an earlier attempt widened it to 1.25
        // specifically to keep adjacent rows' Y-extents from ever
        // intersecting, and real Play Mode measurement did stop the
        // intersection — but only by growing CollectorQueueBoardRegionHeight
        // enough that the camera's fixed-composition frame had to zoom out
        // (orthographic size 8.7787 -> 9.2686 at 1080x1920) to still fit it,
        // which shrank every character's real on-screen size, not just the
        // queue's. That regression is why this reverted back to 0.5.
        // Adjacent rows are now expected and allowed to overlap in Y —
        // QueueRowDepthStep below gives each row genuine camera-depth
        // separation instead, which resolves via the real Z-test with no
        // effect on apparent size (orthographic projection) and therefore no
        // pressure on this value at all.
        public const float QueueVisibleGap = 0.5f;

        public static float QueueRowStep => CollectorVisibleHeight + QueueVisibleGap;

        // ----- Queue row depth (presentation-only, genuine Z separation) -----
        // The per-row multiplier for how far each successive queue row's
        // PRESENTATION (Visual, and the hunger label riding along with it —
        // never the collector root or its CircleCollider2D, which both stay
        // on the shared Z=0 gameplay plane; see CollectorQueueBoard.
        // RowLocalPosition and CollectorSelectionController.FindCollectorAt's
        // own remarks for why root/collider Z must never vary per row) is
        // pulled toward the camera along -CameraForward, via
        // CollectorAnimation.SetPresentationDepth(rowIndex * this). Row 0
        // stays at its authored baseline depth (furthest back); row 1 is
        // pulled one step closer; row 2 two steps; and so on — later rows
        // render in front of earlier ones through a real, Z-tested depth
        // difference, the only thing that can actually win an opaque Z-test
        // (see the now-removed SortingGroup attempt's own lesson:
        // sortingOrder alone cannot). Because the camera is orthographic,
        // moving strictly along its own forward axis changes only depth,
        // never a rendered point's screen X/Y (the same guarantee
        // CollectorAnimation.EnterTerminalForeground and CollectorView.
        // HungerTextLocalOffset already rely on) — so rows are free to
        // overlap in Y (and now do, at the reverted QueueVisibleGap above)
        // without the camera ever needing to zoom out to avoid it.
        //
        // This value is expressed in Visual's own LOCAL space, not final
        // world units — Visual is a direct child of the collector root,
        // which itself carries CollectorSpriteScale (see
        // CollectorQueueBoard.GenerateBoard), and Unity applies a parent's
        // scale to a child's local position when computing world position.
        // The REAL world-space separation between adjacent rows is
        // therefore QueueRowDepthStep * CollectorSpriteScale (1 * 1.9 =
        // 1.9 world units) — the same scaling every existing camera-forward
        // pull on this hierarchy already has (EnterTerminalForeground's own
        // TerminalForegroundPullDistance, HungerTextForegroundPullDistance),
        // so this is a deliberately consistent convention, not an
        // oversight.
        //
        // Sized to clear a full character's own real depth (Z) extent along
        // the camera's view direction with margin, the same reasoning
        // CollectorAnimation.TerminalForegroundPullDistance already
        // established: a queued character's camera-facing half-depth is
        // about 0.35-0.45 world units at CollectorSpriteScale regardless of
        // facing angle (see CollectorView.HungerTextLocalOffset's own
        // remarks), so two adjacent rows' full depth extents (~0.7-0.9
        // combined) never overlap in Z once separated by the resulting 1.9
        // world units, making the correct front-to-back order structural
        // rather than merely likely, for every row pair up to
        // ReservedQueueRowCount.
        public const float QueueRowDepthStep = 1f;

        // Presentation-only lift applied uniformly to every queue row (see
        // CollectorQueueBoard.RowLocalPosition) so the whole queue sits a
        // little higher on screen under the tilted camera, without touching
        // WaitingLinePositionY itself. Reduced from 0.8 to 0.2 alongside
        // ClusterInnerSpacing's own widening above — 0.8 consumed the
        // entire static WaitingLine-to-queue gap and produced a real,
        // measured overlap (see ClusterInnerSpacing's remarks). The
        // remaining static gap this leaves (ClusterInnerSpacing -
        // QueueUpwardPresentationOffset = 0.65 - 0.2 = 0.45 world units)
        // comfortably clears the measured idle-breathing peak (~0.03 world
        // units, measured directly, not assumed) with margin. Deliberately a
        // separate, named constant rather than folded into
        // ClusterInnerSpacing itself: ClusterInnerSpacing still describes
        // the board's own authored region boundary (used by
        // CollectorQueueBoardPositionY and the camera frame below), while
        // this describes only how far above that boundary row 0 is drawn —
        // a pure presentation offset, not a change to the region's own
        // geometry.
        public const float QueueUpwardPresentationOffset = 0.2f;

        private static float CameraTiltRadians => CameraTiltDegrees * Mathf.Deg2Rad;

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

        // ----- Hunger label sorting safety margin -----------------------------
        // The hunger label's own individual Renderer.sortingOrder. Its real
        // separation from the model is genuine depth (see CollectorView.
        // HungerTextLocalOffset's own camera-toward pull) — this is only a
        // small, harmless backup value on top of that, kept clear of the
        // model's own (default-zero) renderers.
        //
        // A per-row/per-state SortingGroup tier system used to sit here as
        // well (queue row, WaitingLine, RecoveryRow, Conveyor, terminal
        // tiers), added to try to fix adjacent queue rows' silhouettes
        // visually intersecting under the tilted camera. Real Play Mode
        // testing confirmed sortingOrder cannot override a genuine Z-test
        // result for opaque, Z-tested 3D renderers — the tiers changed
        // nothing observable and were removed. The actual fix is
        // QueueVisibleGap below: enough real vertical separation that
        // adjacent silhouettes never geometrically intersect in the first
        // place, which the depth buffer then sorts correctly on its own,
        // with no sortingOrder involved at all.
        public const int HungerLabelSortingOffset = 10;

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
        // WaitingLine's own visible bottom edge and CollectorQueueBoard's
        // first row's STATIC visible top edge — the two still read as one
        // connected lower cluster (a collector's path runs directly from one
        // to the other) without the first queue row ever intruding into
        // WaitingLine's own footprint.
        //
        // Widened from 0.2 to 0.65 after a real overlap bug: the previous
        // queue-lift fix (QueueUpwardPresentationOffset) was sized by
        // comparing WaitingLine's and row 0's CENTER positions rather than
        // their actual visible EDGES, silently eating the entire static gap
        // and then some (measured: row 0's static top edge ended up 0.6
        // world units ABOVE WaitingLine's own visible bottom edge — a real
        // overlap, not a thin margin). The static gap alone is also not the
        // full story: CollectorAnimation's idle-breathing routine grows a
        // waiting collector's visible height at its peak (idleHeightExpansion,
        // compensated so only the TOP rises — see its own remarks). Measured
        // directly via real Play Mode Renderer.bounds sampling (54,884
        // samples across every row-0 collector over 3 full real seconds,
        // several idle-breathing cycles) rather than assumed: the breathing
        // peak adds only about 0.03 world units above the static top edge —
        // smaller than an earlier hand estimate, confirming the static gap
        // below (ClusterInnerSpacing - QueueUpwardPresentationOffset = 0.65
        // - 0.2 = 0.45) is the dominant term. Net measured gap between
        // WaitingLine's real rendered bottom edge and row 0's real measured
        // peak top edge: ~0.17 world units before this widening (a bare
        // ~16px at 1080x1920 — confirmed too thin) vs. the ~0.42 world units
        // (~40px) this value targets — confirmed by re-measurement, not
        // formula alone.
        //
        // Top/BottomCompositionPadding are the camera's reserved breathing
        // room above the Conveyor (including a rider on its top edge) and
        // below the collector board. HorizontalCompositionPadding is the
        // camera's reserved breathing room to either side of the Conveyor
        // (including a rider on its side edges) — see CameraFrameWidth.
        public const float GridToClusterSpacing = 0.8f;
        public const float ConveyorToWaitingLineGap = 0.3f;
        public const float ClusterInnerSpacing = 0.65f;
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

        // ----- Camera elevation (queue/character-orientation presentation) --
        // CameraTiltDegrees is the camera's downward pitch — an elevated,
        // slightly-above 3/4 view of the queue instead of a flat, straight-on
        // read, so a waiting character's authored back pose (see
        // CollectorAnimation.WaitingAwayYawDegrees) reads as a back/shoulder
        // silhouette with volume rather than a flat cutout. Still orthographic
        // (no perspective/vanishing point) — only the view direction rotates.
        // CameraDistance is the same distance-from-aim-point magnitude the
        // camera always sat at (the old fixed position's Z of -10), kept
        // explicit so tilting can pivot around the same fixed aim point
        // (CameraVerticalCenter) instead of introducing a second, separately
        // hand-tuned position.
        public const float CameraTiltDegrees = 30f;
        public const float CameraDistance = 10f;

        /// <summary>
        /// The camera's fixed world rotation — a pure downward pitch, no yaw
        /// or roll. Shared by BootstrapSceneCreator (bake-time) and
        /// PortraitCameraFitter (runtime) so both can never drift apart.
        /// </summary>
        public static Quaternion CameraRotation => Quaternion.Euler(CameraTiltDegrees, 0f, 0f);

        /// <summary>
        /// The camera's world-space forward (viewing) direction. Under the
        /// downward tilt this has a nonzero Y AND Z component (unlike the
        /// old straight-on camera, whose forward was exactly world +Z) — any
        /// presentation code that needs to pull something "toward the
        /// camera" for depth-sorting purposes (see CollectorAnimation.
        /// EnterTerminalForeground, CollectorView.HungerTextLocalOffset)
        /// must move along -CameraForward, never along a raw local/world -Z
        /// axis: moving along -Z alone also shifts the projected screen Y
        /// once the camera is tilted (its own up axis has a nonzero Z
        /// component too), which read as a visible positional snap — a real
        /// bug this property exists to prevent by construction. Moving along
        /// -CameraForward can never do this, by definition of an orthonormal
        /// basis (forward is always perpendicular to up and right, at any
        /// tilt angle), so it only ever changes depth, never screen position.
        /// </summary>
        public static Vector3 CameraForward => CameraRotation * Vector3.forward;

        /// <summary>
        /// The camera's fixed world position: CameraDistance away from the
        /// same fixed aim point (0, CameraVerticalCenter, 0) the straight-on
        /// camera used to sit in front of, along the tilted rotation's own
        /// forward axis. Pivoting around that aim point rather than only
        /// changing Y keeps the frame's horizontal composition and centering
        /// completely unaffected by the tilt (an X-axis rotation never moves
        /// the right axis), and keeps this a fixed value independent of
        /// screen aspect, exactly like CameraVerticalCenter itself.
        /// </summary>
        public static Vector3 CameraPosition =>
            new Vector3(0f, CameraVerticalCenter, 0f) - CameraForward * CameraDistance;

        /// <summary>
        /// Orthographic size that fits the fixed camera frame at the given
        /// screen aspect ratio — the max of a width-fit and a height-fit, so
        /// a narrow/tall screen is bound by width and a squarer one is
        /// bound by height, with letterboxing on whichever axis has slack.
        /// Composition (what the frame contains) never changes; only this
        /// size adapts, per aspect ratio.
        ///
        /// The height-fit term is scaled by cos(CameraTiltDegrees): tilting
        /// the camera down rotates its own up axis away from world Y, so a
        /// fixed world-Y extent at Z=0 (CameraFrameTop/Bottom's own content —
        /// PixelGrid, Conveyor, WaitingLine, and every CollectorQueueBoard
        /// row, all sharing the single Z=0 gameplay plane) projects onto a
        /// smaller fraction of the view than it did at a straight-on camera.
        /// This correction keeps that Z=0 content framed the same as before;
        /// it does not, and cannot, also correct the horizontal (width-fit)
        /// term the same way, since a single orthographic size scales both
        /// axes together — verified visually in Play Mode, not by formula
        /// alone.
        /// </summary>
        public static float ComputeOrthographicSize(float screenWidth, float screenHeight)
        {
            if (screenWidth <= 0f || screenHeight <= 0f)
                return CameraFrameHeight * 0.5f;

            float aspect = screenWidth / screenHeight;
            float sizeToFitWidth = CameraFrameWidth / (2f * aspect);
            float sizeToFitHeight = CameraFrameHeight * 0.5f * Mathf.Cos(CameraTiltRadians);
            return Mathf.Max(sizeToFitWidth, sizeToFitHeight);
        }
    }
}
