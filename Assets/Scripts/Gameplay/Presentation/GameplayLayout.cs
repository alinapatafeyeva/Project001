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
    /// one (e.g. making the character bigger) silently distorted the other three
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
    /// GridRegionHeight, at up to GridPreferredCellSize/GridPreferredGap and
    /// never larger, uniformly shrinking both together only when a denser
    /// grid would otherwise exceed that region; a level whose
    /// CollectorQueueBoard queues run deeper than
    /// CollectorQueueBoardRegionHeight simply extends past that region's
    /// bottom edge rather than the camera compensating — a content
    /// authoring constraint, not something this class or the camera solves.
    /// </summary>
    public static class GameplayLayout
    {
        // ----- PixelGrid's authored region ---------------------------------
        // GridRegionWidth/Height are assigned to PixelGrid's
        // maxGridWorldWidth/Height (via SetFieldBounds, see
        // BootstrapSceneCreator) — the field's outer bound, never a target
        // it must fill. Every other camera/composition value below
        // (ConveyorSize, CameraFrameTop/Width) is still derived from these
        // two exactly as before; changing PixelGrid's own preferred cell
        // size/gap below never touches this region's own bounds, so the
        // rest of the fixed composition (Conveyor, WaitingLine,
        // CollectorQueueBoard, camera frame, boost-reserved space) is
        // unaffected by denser pixel art.
        public const float GridRegionWidth = 6f;
        public const float GridRegionHeight = 6f;

        // GridPreferredCellSize/GridPreferredGap are assigned to PixelGrid's
        // own preferredCellSize/preferredGap fields by BootstrapSceneCreator
        // — the design-target size/gap a grid renders at whenever it fits
        // GridRegionWidth x GridRegionHeight without shrinking (see
        // PixelGrid.TryComputeCellMetrics). Both were previously one
        // constant (GridMaximumCellSize, 0.95, with a separate fixed
        // PixelGrid.cellGap of 0.05) that acted as an upper cap a small grid
        // would nearly reach edge-to-edge; GridPreferredCellSize is
        // deliberately smaller (denser, cleaner pixel art for the same 6x6
        // prototype grid) and GridPreferredGap now scales down together with
        // the cell size for any grid dense enough to need shrinking, instead
        // of staying a fixed absolute value that would dominate the cell at
        // high density (see PixelGrid's own remarks). Tuned visually against
        // the real Portrait 1080x1920 Game View, not solved from a formula.
        public const float GridPreferredCellSize = 0.45f;
        public const float GridPreferredGap = 0.05f;

        // ----- Collector sprite scale and its actual visible extent ---------
        // CollectorSpriteScale controls only the rendered collector visual
        // (the world-space localScale CollectorQueueBoard assigns to a
        // collector's root, which every other collector — WaitingLine
        // occupant, Recovery Row occupant, Conveyor rider — keeps for the
        // rest of its lifetime, since none of them ever rescale it). It does
        // NOT mean the visible character occupies a square of that size: at
        // scale 1 a character only covers CollectorVisibleWidthRatio x
        // CollectorVisibleHeightRatio world units (originally derived from
        // the imported sprite rect, 575x632px at 800 PPU = 0.71875 x
        // 0.79 - the height ratio is unchanged from that original value, see
        // CollectorVisibleHeightRatio's own remarks; the width ratio no
        // longer is, see CollectorVisibleWidthRatio's), so at
        // CollectorSpriteScale the actual rendered character is
        // CollectorVisibleWidth x CollectorVisibleHeight, never a full
        // CollectorSpriteScale square. Every camera or board footprint
        // calculation that needs the visible character's actual extent
        // (Conveyor rider clearance, queue row placement) reads
        // CollectorVisibleWidth/Height, never CollectorSpriteScale directly.
        //
        // Reduced from 1.9 to 1.75 (~8%) as a deliberate, secondary
        // complement to GridToClusterSpacing's own fix below (see its
        // remarks for the full "collectors outgrew the composition" root
        // cause) - NOT a substitute for it: even a much larger trim leaves
        // GridToClusterSpacing needing to roughly double, so the primary
        // fix is still deriving that gap from CollectorVisibleHeight. This
        // trim only reduces how much the whole composition/camera needs to
        // grow to accommodate that derived gap comfortably, and secondarily
        // eases the "collectors read as too big" side of the same crowding
        // complaint. Every downstream quantity that already keyed off
        // CollectorVisibleHeight/Width (QueueRowStep's own tap-selection
        // non-overlap margin, ConveyorRiderMinimumSpacing's boarding
        // clearance) shrinks proportionally and automatically - re-checked
        // by hand for QueueRowStep specifically, since that one is a hard
        // correctness constraint, not just a visual preference: adjacent
        // rows' CircleCollider2D radii still sum to 1.75 world units against
        // a QueueRowStep of 1.8825, a 0.13 unit margin (was 0.10 before this
        // change) - if anything safer than before, not tighter.
        //
        // Raised again, from 1.75 to 1.83 (~4.6%), after the Conveyor visual
        // recalibration pass (see ConveyorBeltCalibration.VisualScale) made
        // collectors read as slightly undersized against the now-larger
        // belt - a real Game View review asked for collectors to "regain
        // visual presence" without crowding PixelGrid or cramping iPhone
        // portrait. Landed inside the requested ~1.83-1.84 range rather
        // than jumping back to the old pre-trim 1.9 - re-verified
        // QueueRowStep's own hard non-overlap margin still holds at this
        // scale (adjacent CircleCollider2D radii sum to 1.83, QueueRowStep
        // 1.9457 - a 0.116 unit margin, still comfortably positive).
        // GridToClusterBreathingRoom below is deliberately reduced in the
        // same pass to hold GridToClusterSpacing - and therefore
        // ConveyorSize - numerically fixed despite this increase (see its
        // own remarks): the Conveyor visual/size was explicitly accepted as
        // final this pass and must not move again just because the
        // collector driving its own clearance math got taller.
        public const float CollectorSpriteScale = 1.83f;

        // CollectorVisibleHeightRatio is the single shared body-height
        // target CharacterAssetBuilder scales every species to (see its own
        // remarks) - originally derived from the imported sprite rect
        // (632/800), kept unchanged since the character's height was never the
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
        // ----- ConveyorRiderMaxVisualFootprint -----
        // The conservative maximum on-screen footprint (world units) ANY
        // current roster collector can present while actively RIDING the
        // Conveyor, across any facing/orientation - a single roster-wide
        // worst case, never a per-species table (mirrors
        // CollectorVisibleWidthRatio's own established convention: "the
        // queue adapts to the characters, not the opposite" - here, the
        // Conveyor's own spacing adapts to the roster the same way).
        //
        // Replaces the previous CollectorVisibleHeight basis, which assumed
        // a "close to circular" footprint - true for the old single-species
        // placeholder, never re-verified once real species were added. Two
        // real measurements now confirm that assumption was wrong, in the
        // same direction, by a wide margin:
        //   - Turtle: CollectorVisibleWidthRatio's own build-log measurement
        //     (1.037 unscaled, ~1.898 world units at today's
        //     CollectorSpriteScale) already showed a species reading
        //     noticeably wider than CollectorVisibleHeight (1.4457).
        //   - Crab: CharacterVerification.cs's own live measurement records
        //     ~2.03 world units for Crab's ACTUAL Conveyor riding pose
        //     (claws in their wide, authored bind pose - CharacterAssetBuilder.
        //     ApplyCrabPosePresentation only tucks them for the Waiting
        //     pose, never the Conveyor one) - wider even than Turtle, and
        //     the true current worst case, superseding the older, now-stale
        //     "Crab: 0.888" figure in CollectorVisibleWidthRatio's own
        //     remarks (that figure was measured under a build pipeline that
        //     permanently baked a claw-narrowing adjustment at build time;
        //     that adjustment was later deleted in favor of the runtime
        //     pose-switcher CharacterPosePresentation now uses, so today's
        //     baked/measured Crab pose is the wide one, not the narrow one
        //     that comment still describes).
        //
        // 1.9 is a deliberately CONSERVATIVE first pass, not the full
        // measured worst case (~2.03): the goal here is substantially
        // reducing ugly body/claw overlap while keeping boarding cadence
        // reasonably quick, not a mathematical zero-overlap guarantee - see
        // ConveyorRiderVisualGap's own remarks for how this combines with
        // the corner-squeeze margin below. Revisit upward (toward the full
        // ~2.03, or re-measure via CharacterAssetBuilder.BuildAll's own log
        // once it is re-run against the current pipeline) if this first
        // pass still reads as insufficient in real Play Mode review.
        public const float ConveyorRiderMaxVisualFootprint = 1.9f;

        // Explicit breathing-room margin on top of
        // ConveyorRiderMaxVisualFootprint, sized to comfortably absorb the
        // small chord-vs-arc "squeeze" a tight corner introduces (two points
        // separated by a given arc length sit slightly closer together in a
        // straight line while both are inside the same rounded corner) — at
        // ConveyorPath's authored cornerRadius (1 world unit, see
        // BootstrapSceneCreator.CreateConveyor), the worst case is only
        // about a 10% reduction, well inside this margin. Unchanged in
        // value from its own prior history — only what it is now added ON
        // TOP OF changed (ConveyorRiderMaxVisualFootprint, not
        // CollectorVisibleHeight — see that constant's own remarks for why).
        public const float ConveyorRiderVisualGap = 0.35f;

        public static float ConveyorRiderMinimumSpacing => ConveyorRiderMaxVisualFootprint + ConveyorRiderVisualGap;

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

        // Widened from 0.3 to 0.4, then to 0.48 after a follow-up real
        // Game View review found the COLLECTORS themselves (not just the
        // slot art) still read as visually crowded at 0.4 - expected, since
        // a standing collector's own rendered width (GameplayLayout.
        // CollectorVisibleWidth, ~1.98 world units) is wider than either
        // step value, so some body-to-body closeness between neighbours is
        // an unavoidable consequence of fitting WaitingLineCapacity
        // collectors in the fixed Conveyor-derived CameraFrameWidth - not
        // something this constant alone can fully eliminate without either
        // shrinking collectors (out of scope - CollectorSpriteScale is
        // shared gameplay-wide, not WaitingLine-specific) or narrowing the
        // camera frame itself (explicitly out of scope for a presentation-
        // only change).
        //
        // 0.48 is the highest value that still keeps a full 6-slot row
        // (step * 5 + WaitingSlotSize * WaitingSlotVisualScale) inside
        // CameraFrameWidth (~9.88) with a small positive margin (~0.1 world
        // units) rather than exactly zero - the 6-slot case is the binding
        // constraint, not the 5-slot default (5 slots alone would tolerate
        // a substantially larger value, up to ~0.83, before CameraFrameWidth
        // became the limit). Confirmed via real capture at both an
        // iPhone-portrait 1080x1920 aspect and an iPad-portrait aspect: 5
        // slots read with genuinely more breathing room than 0.4, and 6
        // slots still fit on screen without clipping.
        public const float WaitingSlotSpacing = 0.48f;

        // WaitingSlotVisualScale is a pure presentation multiplier applied
        // ONLY by WaitingSlotVisual's own child-local Transform.localScale —
        // never read by WaitingLine.GenerateSlots, never applied to the
        // WaitingSlot GameObject itself. WaitingSlotSize/WaitingSlotSpacing
        // above stay the sole source of truth for slot CENTER positions and
        // the gap between them (GenerateSlots' stepX/offsetX math never
        // reads this value), so growing this constant makes each slot's art
        // visually larger without moving a single logical slot position or
        // narrowing the authored gap between them.
        //
        // Hard ceiling: WaitingSlot.png's own alpha content fills its
        // imported square nearly edge to edge (~98%, negligible built-in
        // transparent margin - see WaitingSlot.png's own alpha bounds), so
        // this value cannot exceed step/WaitingSlotSize = (WaitingSlotSize +
        // WaitingSlotSpacing) / WaitingSlotSize = 1.25 without adjacent
        // slots' opaque art actually overlapping - confirmed the hard way:
        // an initial 1.7 attempt looked like five clean, separated cells in
        // a real Game View capture, but pixel-measuring that screenshot
        // against the known step showed adjacent sprites truly overlapping
        // by roughly a quarter of their own width, with the clean-looking
        // seam only an accident of opaque draw-order masking (both slots
        // share the same Z push and sortingOrder, so which one paints over
        // the other at the overlap is undefined) - not real spacing, and not
        // something to rely on.
        //
        // 1.15 keeps every slot's full width (1.2 * 1.15 = 1.38) safely
        // under that 1.5 non-overlap ceiling, leaving a real ~0.12 world
        // unit (~8% of the 1.5 step) visible gap confirmed in a real Game
        // View capture at both an iPhone-portrait 1080x1920 aspect and an
        // iPad-portrait aspect. Measured against what a real Unity Editor
        // Game View actually rendered before this fix - WaitingSlot.png's
        // own pixel dimensions changed after this project's sprite import
        // settings were first authored (785x787, not the 1024x1024 they
        // were calibrated against - see WaitingSlot.png.meta), so every
        // slot was silently rendering at 1.2 * (785/1024) = 0.92 world
        // units, not the intended 1.2 - this constant's own 1.15 factor
        // lands at 1.38 world units, ~1.5x that actually-observed 0.92,
        // matching the requested starting direction once measured against
        // what Play Mode genuinely showed rather than the never-actually-
        // rendered 1.2 design intent.
        public const float WaitingSlotVisualScale = 1.15f;

        // ----- Queue row rhythm -----------------------------------------------
        // QueueRowStep is the vertical distance between consecutive
        // CollectorQueueBoard row centers, built from the collector's actual
        // visible height (not CollectorSpriteScale) plus an explicit,
        // authored visible gap — so the gap between two rendered characters
        // is exactly QueueVisibleGap, regardless of how much empty
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

        /// <summary>
        /// Gap A: the vertical distance from Waiting Line's own row Y down to
        /// Recovery Row's own fixed row Y (BootstrapSceneCreator.
        /// CreateRecoveryRow bakes Recovery Row's Transform to exactly
        /// WaitingLinePositionY - WaitingToRecoveryGap). Deliberately
        /// compact — Recovery Row belongs to the same queue system as
        /// Waiting Line, so this reuses QueueRowStep, the exact same
        /// center-to-center spacing every other pair of adjacent
        /// CollectorQueueBoard rows already uses, rather than a bespoke
        /// number. See RecoveryToCollectorsGap below for the second,
        /// independent gap on the OTHER side of Recovery Row, and its own
        /// remarks for why these two needed to become separate constants.
        /// </summary>
        public static float WaitingToRecoveryGap => QueueRowStep;

        /// <summary>
        /// Gap B: the vertical distance from Recovery Row's own fixed row Y
        /// down to the lower collector-queue content's own top, once
        /// Recovery Row is occupied (RecoveryLineLayoutController.Refresh —
        /// see its own remarks for the exact derivation). Deliberately
        /// larger than WaitingToRecoveryGap: Recovery Row and the lower
        /// "available characters" content are two different zones (an
        /// occupied recovery slot vs. the collectors still waiting to be
        /// chosen), and reading that separation on screen needs this gap to
        /// be clearly bigger than — not merely different from — the compact
        /// Waiting-to-Recovery gap above it. Reuses QueueRowStep plus one
        /// further QueueVisibleGap of extra breathing room (the same "small
        /// breathing room" unit WaitingToRecoveryGap's own QueueRowStep is
        /// already partly built from) rather than a fresh magic number.
        ///
        /// Before this fix, BOTH gaps were driven by this exact same
        /// QueueRowStep + QueueVisibleGap total, applied as Recovery Row's
        /// own distance below Waiting Line (i.e. on the Gap-A side, where
        /// this constant now lives). RecoveryLineLayoutController then
        /// shifted the lower content down by that identical amount, measured
        /// from ITS OWN independent baseline — so the shared shift canceled
        /// out of the Recovery-Row-to-lower-content distance entirely,
        /// leaving Gap B permanently pinned at the original, much smaller
        /// Waiting-Line-to-board gap (WaitingSlotSize*0.5 + ClusterInnerSpacing,
        /// ~1.25) no matter how large the shared shift was made — the
        /// structural reason Gap A read too large and Gap B too small at the
        /// same time. Splitting the single shift into these two
        /// independently-named gaps is what actually gives Gap B its own
        /// tunable size.
        /// </summary>
        public static float RecoveryToCollectorsGap => QueueRowStep + QueueVisibleGap;

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

        // ----- Spacing and padding: the rest of the composition ------------
        // GridToClusterSpacing is the single gap between PixelGrid — the
        // primary play surface — and the Conveyor's own margin (see
        // ConveyorSize): the Conveyor's bottom straightaway runs exactly
        // GridToClusterSpacing below PixelGrid, which is why the gap and the
        // margin share one authored value instead of two numbers that would
        // otherwise need to be kept in sync by hand.
        //
        // This gap is measured from the Conveyor's path line to PixelGrid's
        // own ALLOCATED region boundary (GridRegionWidth/Height), never from
        // a level's actual smaller rendered grid content - true regardless
        // of level (level_001's 6x6 grid renders at a 2.95-unit natural
        // size, level_002's 4x8 at 1.95x3.95, both far under the 6-unit
        // allocation on every side), so this can never be satisfied by a
        // level simply having a smaller grid.
        //
        // Was a flat, standalone-authored 0.8 - never referencing collector
        // size at all - until a real Game View review found collectors
        // reading as almost touching PixelGrid above and cramped for room
        // side-to-side on the Conveyor. Root cause: collector size grew
        // through later, independent passes (the shared-height-scaling
        // migration, CollectorVisibleWidthRatio's own widening for Turtle)
        // without this gap ever being revisited to match, so a grounded
        // Conveyor rider's own real reach toward the grid quietly outgrew
        // it - on the Bottom straight, a full CollectorVisibleHeight (see
        // CameraFrameTop's own remarks for why grounding means "full
        // height", not half); on Left/Right, CollectorVisibleWidth*0.5 (a
        // plain symmetric fact, unrelated to grounding - a standing
        // character's width is centered on its own root regardless).
        // CollectorVisibleHeight is the larger of the two, so it is the
        // binding case this gap must clear.
        //
        // Now derives from that same existing per-character data instead of
        // a standalone number, plus GridToClusterBreathingRoom - the one
        // genuinely new authored constant this fix needed, since "how much
        // MORE than the bare non-overlap minimum feels comfortable" is a
        // real design judgment, not something any existing measurement
        // already encodes (see its own remarks for exactly what it
        // represents). Growing this necessarily grows ConveyorSize and the
        // whole camera frame with it (see ComputeOrthographicSize) - the
        // correct, honest consequence of a composition that was undersized
        // for today's collectors, not something to route around with
        // another shift elsewhere.
        //
        // ConveyorToWaitingLineGap is the single explicit, positive gap
        // between the Conveyor's own bottom-straightaway path line and a
        // WaitingLine occupant's own real visible top edge — see
        // WaitingLinePositionY's own remarks for why BOTH of those are now
        // GroundAnchor-derived quantities (a grounded Conveyor rider's own
        // lowest point IS the path line, and a grounded WaitingLine
        // occupant's own highest point is a full CollectorVisibleHeight
        // above this row's Y), not the pre-grounding "half-height on each
        // side, plus the slot marker's own edge" quantities this gap used
        // to be measured between.
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
        // The one genuinely new authored constant GridToClusterSpacing's own
        // fix needed (see its remarks): explicit breathing room ABOVE the
        // bare geometric minimum (CollectorVisibleHeight - the distance a
        // grounded Bottom-straight rider's own body reaches toward the
        // grid, the binding case). Without this, GridToClusterSpacing would
        // equal CollectorVisibleHeight exactly - zero actual overlap, but a
        // rider's own silhouette would graze PixelGrid's allocated boundary
        // with no visible daylight, the same "geometrically correct but
        // reads as touching" problem the exact-margin fixes elsewhere in
        // this file already ran into. 0.25 was chosen to read as genuinely
        // comfortable without inflating ConveyorSize (and therefore the
        // camera's own zoom) more than necessary - confirmed via real Game
        // View review, not assumed from the number alone.
        //
        // Raised from 0.25 after a follow-up manual review found riders
        // still reading as too close to PixelGrid even with that margin in
        // place. Root cause of why 0.25 undershot: it is the ONLY clearance
        // term shared by all four Conveyor sides, but the four sides are
        // not equally tight - a Bottom/Top rider's own body reaches a full
        // CollectorVisibleHeight toward the grid (the binding case this
        // value is sized against), while a Left/Right rider's body only
        // reaches CollectorVisibleWidth*0.5, a genuinely smaller number -
        // so Top/Bottom riders were left with only the bare 0.25 itself as
        // real daylight, while Left/Right already had roughly 3x that
        // (GridToClusterSpacing - CollectorVisibleWidth*0.5 ≈ 0.72). The
        // previous pass's own real Game View review evidently judged the
        // easier Left/Right case, not the actually-binding Top/Bottom one.
        //
        // There is no cheaper lever than this one: ConveyorPath's riding
        // line sits exactly on ConveyorSize's own outer edge (confirmed
        // against ConveyorPath.cs), so the distance from that line to
        // PixelGrid's own boundary IS this gap, by construction - not a
        // number that can be recovered from Conveyor.png's own art
        // calibration (ConveyorVisual is scale-independent of ConveyorPath/
        // ConveyorSize, confirmed against ConveyorVisual.cs/
        // BootstrapSceneCreator.CreateConveyor - re-authoring the belt art
        // could change how tight the BELT ITSELF looks, but not the real
        // distance between a rider's silhouette and the grid). Growing this
        // value therefore still grows ConveyorSize and the camera frame
        // with it (see GridToClusterSpacing's own remarks) - the same
        // honest, unavoidable consequence as before, not something this
        // pass tries to hide behind new art or a second constant. Kept
        // deliberately as the smallest increase that read as genuinely
        // comfortable on the actually-binding Top/Bottom/corner case in a
        // real Game View review (0.45, not a much larger round number) so
        // the resulting zoom-out is no bigger than necessary.
        //
        // Reduced from 0.45 to 0.3868 when CollectorSpriteScale rose from
        // 1.75 to 1.83 (see its own remarks) - a deliberate, exact
        // arithmetic offset (old GridToClusterSpacing at 1.75, 1.8325,
        // minus CollectorVisibleHeight at the new 1.83 scale, 1.4457), not
        // a fresh visual re-tuning: the Conveyor visual/size were
        // explicitly accepted as final this pass ("do not change
        // ConveyorSize again"), so this value's only job here is to absorb
        // CollectorVisibleHeight's own increase and hold GridToClusterSpacing
        // - and therefore ConveyorSize - numerically frozen at its previous
        // 1.8325 / 9.665. Still comfortably positive (real, visible
        // daylight above a grounded Top/Bottom rider remains, just
        // slightly less than 0.45) - confirmed via real Game View review
        // that this still reads as comfortable, not merely non-negative.
        public const float GridToClusterBreathingRoom = 0.3868f;

        public static float GridToClusterSpacing => CollectorVisibleHeight + GridToClusterBreathingRoom;

        // Widened from 0.3 to 0.55 after a real Game View review found
        // WaitingLine collectors still visually intruding into the
        // Conveyor's own region above them, even though the grounding-model
        // fix (see WaitingLinePositionY's own remarks) already correctly
        // sized this gap's own endpoints. The gap itself simply read too
        // thin as a genuine breathing band between the two clusters,
        // especially once CollectorSpriteScale's own increase (see its
        // remarks) made a grounded WaitingLine occupant's own upward reach
        // (CollectorVisibleHeight) taller too, eating further into the same
        // fixed 0.3. Changed here, in GameplayLayout - the single
        // structural owner of WaitingLine's own Y position (see
        // WaitingLinePositionY) - rather than nudging individual WaitingLine
        // occupants, which would silently break the "every WaitingLine
        // occupant stands exactly on its own WaitingSlot" contract
        // GroundAnchor grounding depends on.
        //
        // Raised again, from 0.55 to 0.70, after a further real Play Mode
        // review found WaitingLine collectors still slightly intruding into
        // the Conveyor with the tops of their bodies. Deliberately a small,
        // conservative bump (+0.15, ~27%) rather than a full re-derivation:
        // an earlier attempt jumped to 1.08 (fully compensating
        // ConveyorBeltCalibration's own Bottom-edge belt-art correction on
        // top of restoring the old 0.55 real gap) and was confirmed too
        // large in real Play Mode review, pushing WaitingLine visibly
        // further away than intended.
        //
        // Raised again, from 0.70 to 0.90, after a further real Play Mode
        // review found 0.70 still not enough - WaitingLine collectors
        // still read as too close to the Conveyor, with the whole
        // downstream queue cluster (CollectorQueueBoard, carried along
        // through CollectorQueueBoardPositionY's own dependency on this
        // value) still reading as too high as a result. 0.90 is the low
        // end of that pass's own requested ~0.90-0.95 starting target -
        // the smallest value in that range, kept deliberately close to
        // the low end rather than jumping to the high end or back toward
        // 1.08, consistent with this constant's own history of small,
        // confirmed-by-review increments rather than large corrective
        // jumps.
        //
        // Raised again, from 0.90 to 1.05, after a further real Play Mode
        // review found the WaitingLine and the entire downstream cluster
        // still needed to sit slightly lower.
        //
        // Raised again, from 1.05 to 1.15, after a further real Play Mode
        // review found the WaitingLine and downstream queue still needed
        // a little more vertical breathing room. Direction confirmed
        // correct, but still not enough.
        //
        // Raised again, from 1.15 to 1.45, this time aimed at a specific
        // visual TARGET rather than another small nudge: the existing
        // ClusterInnerSpacing relationship, read as a VISUAL gap rather
        // than a raw constant. ClusterInnerSpacing (0.65) is not itself
        // that visual gap - CollectorQueueBoard's row 0 is lifted toward
        // WaitingLine by QueueUpwardPresentationOffset (0.2), so the real
        // visible gap between WaitingLine's own WaitingSlot platform
        // (bottom edge WaitingLinePositionY - WaitingSlotSize*0.5) and row
        // 0's own static top edge is ClusterInnerSpacing -
        // QueueUpwardPresentationOffset = 0.65 - 0.2 = 0.45 world units -
        // see QueueUpwardPresentationOffset's own remarks for the same
        // arithmetic. This pass's goal is making the Conveyor<->WaitingLine
        // gap read as that same ~0.45 world units, not raw ClusterInnerSpacing.
        //
        // Unlike that WaitingLine<->queue gap, this one cannot be derived
        // to the same exactness purely from GameplayLayout's own
        // constants: WaitingSlotSize*0.5 gives Gap2's upper edge a real,
        // modeled "platform footprint" term, but nothing in this file (or
        // in scope for this pass - ConveyorVisual/ConveyorBeltCalibration
        // were explicitly not touched) models the Conveyor's own rendered
        // visual footprint below its bottom-straightaway path line the
        // same way. WaitingLinePositionY's formula already cleanly cancels
        // CollectorVisibleHeight against the grounded WaitingLine
        // occupant's own upward reach, so this constant maps 1:1 onto that
        // gap only if the Conveyor's true visual edge sat exactly on the
        // mathematical path line - real Play Mode review already showed
        // that assumption undershoots (1.15 still read as insufficient
        // against this ~0.45 target), meaning the Conveyor's actual
        // rendered footprint extends a real, currently-unmodeled amount
        // below that line. 1.45 = 0.45 (the derived target gap) + 1.0 (a
        // reasoned estimate for that unmodeled Conveyor-side extent, not a
        // measured value) - a genuine step past 1.15 rather than another
        // marginal nudge, but still an ESTIMATE pending real Play Mode
        // confirmation, not a guaranteed exact match the way the 0.45
        // figure itself is.
        public const float ConveyorToWaitingLineGap = 1.45f;
        public const float ClusterInnerSpacing = 0.65f;

        // Raised from 0.2 to 1.2 after a real Game View review found the
        // top HUD (Exit/Level/speed/Pause, a ScreenSpaceOverlay canvas with
        // no reservation of its own in this composition — see
        // BootstrapSceneCreator.CreateTopHudCanvas's own remarks) reading as
        // almost touching the Conveyor's top edge, with no real breathing
        // room between them.
        //
        // This is the single value that owns that gap: raising it grows
        // CameraFrameTop, which — since CameraVerticalCenter is this frame's
        // own midpoint (see CameraVerticalCenter below) — pulls the fixed
        // aim point up and therefore shifts the ENTIRE composition (grid,
        // Conveyor, WaitingLine, CollectorQueueBoard — everything on the
        // shared Z=0 gameplay plane) down on screen as one coherent unit,
        // with zero change to any internal spacing (ConveyorSize,
        // GridToClusterSpacing, ConveyorToWaitingLineGap, ClusterInnerSpacing,
        // QueueRowStep are all untouched) — exactly the "move the composition,
        // not its parts" fix this file's own composition model is meant for.
        //
        // At the reference 1080x1920 portrait aspect (still width-bound
        // afterward — confirmed by hand-computing ComputeOrthographicSize
        // before and after: sizeToFitWidth stays the binding term up to
        // TopCompositionPadding ~1.6-1.7, comfortably past 1.2), this reads
        // as a ~39px larger gap between the Conveyor's top edge and the
        // screen's top edge — nearly doubling the ~42px gap the HUD's own
        // lowest element (SpeedButton, bottom edge 144px from the top at
        // this aspect) previously had above the Conveyor, to ~81px — with
        // NO orthographic zoom-out (the composition does not shrink; only
        // where it sits on screen changes).
        //
        // Raised again, from 1.2 to 1.6, after a real Game View review found
        // energy bars on TOP-ROW/top-Conveyor-straight riders still reading
        // as too close to (and able to overlap) the HUD. Root cause: this
        // constant's own margin was sized purely against a rider's BODY
        // reach (CameraFrameTop already adds a full CollectorVisibleHeight
        // for that — see its own remarks), but CollectorView's own
        // EnergyBarConveyorVerticalOffset (0.43, in the collector root's
        // local space, i.e. *CollectorSpriteScale world units) plus the
        // bar's own half-height place a riding bar's TOP a further ~0.97
        // world units above the character's own visible top edge — a
        // distance this constant's own margin was never re-checked against
        // when the energy bar feature was added. Confirmed via a real
        // Unity Camera.WorldToScreenPoint projection (not a hand estimate):
        // at the previous 1.2, a worst-case rider (Crab — the one species
        // with no EnergyBarSpeciesConveyorCorrection reduction) sitting at
        // the Conveyor's own exact top-center — a point every rider legitimately
        // passes through every lap, not a rare edge case — put that bar's own
        // top at ~35px from the top of a 1080x1920 screen, INSIDE the HUD's
        // own 0-144px band (SpeedButton's bottom edge) by ~109px.
        //
        // 1.6 is deliberately the LARGEST increase that still holds
        // 1080x1920 exactly width-bound (confirmed via the same real
        // projection: orthoSize is bit-for-bit unchanged, 10.5495, at both
        // 1.2 and 1.6 — a pure on-screen translation, zero zoom, on the
        // primary reference aspect; iPad portrait 1668x2388 is already
        // height-bound even at 1.2, so it absorbs a small, honest, expected
        // consequence of this same shared value — orthoSize 10.3660 ->
        // 10.5392, ~1.7% larger view — rather than a second, aspect-specific
        // constant). This closes real, measured distance (worst-case bar-top
        // moves from ~35px to ~51px at 1080x1920, ~23px to ~62px at
        // 1668x2388) but does NOT fully clear the HUD band in the exact
        // worst case by itself — a full guarantee would need roughly
        // TopCompositionPadding ~3.0+, which also means a real ~5-6%
        // orthographic zoom-out on both aspects; confirmed too large a
        // composition change for this pass's own explicit "small
        // translation, not a redesign" scope. Revisit upward, accepting a
        // small zoom-out, if a future review still finds this insufficient
        // in real Play Mode.
        public const float TopCompositionPadding = 1.6f;
        public const float BottomCompositionPadding = 0.2f;
        public const float HorizontalCompositionPadding = 0.15f;

        // PixelGrid stays centered at the world origin - see class remarks.
        // (A previous pass introduced CompositionDownwardShift/
        // WaitingLineExtraDownwardShift here - two empirically-tuned
        // translations that shifted the whole composition and WaitingLine
        // specifically to paper over a top-clipping bug and a WaitingLine-
        // overlaps-Conveyor bug. Both bugs shared one real cause: CameraFrameTop
        // and WaitingLinePositionY below were sized for a collector centered
        // on its anchor line, but grounded collectors - conveyor riders and,
        // as of last pass, WaitingLine occupants - are aligned by GroundAnchor
        // instead, which reaches a full CollectorVisibleHeight above their
        // anchor line, not CollectorVisibleHeight*0.5. Fixing that margin at
        // its source below removes the need for either shift - see
        // CameraFrameTop's and WaitingLinePositionY's own remarks.)
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
        /// WaitingLine's center (every slot shares this Y - see
        /// WaitingLine.GenerateSlots). A rider on the Conveyor's bottom
        /// straightaway is GROUNDED there (see CameraFrameTop's own remarks
        /// for the full mechanism), not centered on it: its GroundAnchor
        /// sits exactly ON the path line, and its own body extends upward
        /// from there, toward the grid - never downward, toward WaitingLine.
        /// So the path line itself (-ConveyorHalfSize), not
        /// "-CollectorVisibleHeight*0.5 below the path line," is the real
        /// lowest point ConveyorToWaitingLineGap's own breathing room is
        /// measured from.
        ///
        /// The collector THIS line grounds is symmetric to that: a
        /// WaitingLine occupant's own GroundAnchor sits exactly at this slot
        /// row's own Y (see CollectorLifecycle.ResolveLap and
        /// CollectorView.SetGroundedPosition), and its body extends a full
        /// CollectorVisibleHeight upward FROM there - toward the Conveyor -
        /// for the identical structural reason CameraFrameTop's own remarks
        /// document (GroundAnchor reaches a full height above its anchor
        /// line, not half). That upward reach is what ConveyorToWaitingLineGap
        /// must actually clear, not WaitingSlotSize*0.5 (the SLOT MARKER's
        /// own half-height, a separate, smaller quantity that the taller
        /// grounded collector already exceeds - see WaitingSlotSize's own
        /// remarks on why the marker is deliberately not sized to the
        /// collector).
        ///
        /// Was previously computed assuming both ends were center-anchored
        /// (CollectorVisibleHeight*0.5 below the Conveyor's path line, plus
        /// WaitingSlotSize*0.5 to reach this row's own center from a
        /// "WaitingLine top edge" that no longer exists as a useful
        /// intermediate once grounding, not the slot marker, owns the real
        /// boundary) - patched afterward with two empirically-tuned
        /// downward shifts (CompositionDownwardShift, WaitingLineExtraDownward
        /// Shift) that this formula no longer needs, since it now derives
        /// the correct gap directly from CollectorVisibleHeight, the same
        /// existing per-character data CameraFrameTop uses.
        /// </summary>
        public static float WaitingLinePositionY =>
            -ConveyorHalfSize - ConveyorToWaitingLineGap - CollectorVisibleHeight;

        /// <summary>
        /// Unlike WaitingLinePositionY, this is the board region's true top
        /// edge, not a center — CollectorQueueBoard places row 0's own center
        /// half a CollectorVisibleHeight below this value (see
        /// CollectorQueueBoard.GenerateBoard), so the row's actual visible
        /// top edge lands ClusterInnerSpacing below WaitingLine's own SLOT
        /// MARKER's bottom edge (WaitingLinePositionY - WaitingSlotSize*0.5) -
        /// the marker, not the grounded collector standing on it, is what
        /// CollectorQueueBoard must clear here, since (unlike the Conveyor
        /// side above) a WaitingLine occupant's own GroundAnchor sits AT
        /// this row's Y and never reaches below it - the slot marker's own
        /// art, centered on that same Y, is the actual lowest visible point
        /// of this row. CollectorQueueBoard itself is unaffected by
        /// grounding (see CollectorQueueBoard.RowLocalPosition - still a
        /// plain center-anchored placement), so nothing about its own
        /// region needs to change here.
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
        // edge always clears the grid's top edge too.
        //
        // A collector riding the Conveyor's top edge is GROUNDED there, not
        // centered on it: CollectorView.ApplyConveyorPresentationOffset
        // aligns this rider's GroundAnchor (its own baked feet-contact
        // point, see CharacterAssetBuilder.CreateGroundAnchor) with the path
        // line, never its root/body center. Because only Visual's YAW ever
        // rotates a rider (CollectorAnimation drives facing around the Y
        // axis only - no pitch/roll), a character's own head-to-feet axis
        // stays vertically aligned regardless of which edge it is on, so
        // GroundAnchor sits a fixed CollectorVisibleHeight*0.5 below this
        // model's own vertical center whenever PresentationOffset is zero
        // (GroundAnchorHeightFraction = 1.0 in CharacterAssetBuilder places
        // it exactly at the model's own lowest measured point, i.e.
        // "center minus one full half-height"). Grounding pulls that point
        // UP to the path line, which pushes the model's own TOP up by that
        // same half-height beyond where a center-anchored rider's head
        // would have reached - so a grounded rider's real visible top
        // reaches a full CollectorVisibleHeight above the path line, not
        // CollectorVisibleHeight*0.5. This is the single, structural reason
        // a top rider was clipping despite this frame's own top padding:
        // the reserved margin was sized for a center-anchored rider that no
        // longer exists once grounding was introduced, not a magic-number
        // shortfall to patch with an extra shift.
        private static float CameraFrameTop =>
            ConveyorHalfSize + CollectorVisibleHeight + TopCompositionPadding;

        // CollectorQueueBoard is unaffected by grounding (see
        // CollectorQueueBoardPositionY's own remarks - still plain
        // center-anchored placement), so this reserved margin needs no
        // correction of its own; it simply inherits whatever real change
        // WaitingLinePositionY's own fix above produces, through
        // CollectorQueueBoardPositionY's existing dependency on it.
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

        // ----- Presentation depth layering (explicit rendering-order contract) -----
        // Every gameplay-plane element that must never visually occlude a
        // collector, and every collector-attached element that must always
        // render in FRONT of the character itself, is pushed/pulled a real,
        // explicit distance along CameraForward - never left to sortingOrder
        // alone (see the now-removed SortingGroup attempt's own lesson,
        // QueueRowDepthStep's remarks: "sortingOrder alone cannot [win an
        // opaque Z-test]"). Collecting the three riding-relevant distances
        // here, in the one file this project already treats as composition's
        // single source of truth, makes the intended stack an explicit,
        // named contract instead of three independently-authored constants
        // in three different files that merely happen to agree:
        //
        //   GameplayBackground (BootstrapSceneCreator.GameplayBackgroundLocalDepth,
        //   15 - far deeper than every distance below combined)
        //     -> ConveyorVisual (ConveyorVisualDepthPush, pushed AWAY from camera)
        //       -> WaitingSlotVisual (WaitingSlotVisualDepthPush, pushed AWAY
        //          from camera - a slot's own backdrop must sit behind both
        //          the contact shadow and the collector standing in it)
        //         -> ContactShadow (ContactShadowDepthPush, pushed AWAY from camera)
        //           -> Character baseline (0 - the shared Z=0 gameplay plane
        //              every ConveyorRider/collector root actually sits on)
        //             -> EnergyBar (EnergyBarDepthPull, pulled TOWARD camera)
        //
        // Each step only needs to clear the previous layer by more than a
        // collector's own real camera-facing depth extent (~0.35-0.45 world
        // units at CollectorSpriteScale - see CollectorAnimation.
        // TerminalForegroundPullDistance's own remarks for that figure's
        // origin) for the ordering to hold structurally, everywhere around
        // the loop, regardless of any per-collector alignment variance -
        // never a coincidence of whatever numbers happened to be chosen.
        //
        // 5.0 (raised from an initial 1.5, then 3.5 - see its own history): once
        // ConveyorBeltCalibration started shifting a riding collector's own
        // world Y position (to follow the belt art, not just the
        // mathematical path), that same Y shift also changes the
        // collector's camera-space DEPTH under this project's tilted
        // camera (CameraForward has a nonzero Y component - see
        // GameplayLayout.CameraForward's own remarks), shrinking the
        // margin 1.5 alone left. Confirmed via a real Play Mode diagnostic
        // (ConveyorAlignmentDiagnostic) that conservatively checks EVERY
        // world-axis-aligned bounding-box corner of a riding collector
        // (deliberately a looser, safer test than the collector's own true
        // rotated silhouette, which real screenshots at every sampled edge/
        // corner/species already confirmed renders correctly with no
        // clipping even before this margin was raised) - a real Play Mode
        // run at 3.5 already reached zero violations (worst-case margin
        // +0.14, at the Bottom straight); 5.0 gives that same worst case a
        // full +1 unit of comfortable headroom instead of a thin margin,
        // still far short of the camera's far clip plane.
        public const float ConveyorVisualDepthPush = 5.0f;

        // A slot's own backdrop art (WaitingSlotVisual) must sit behind both
        // the collector standing in it AND that collector's own contact
        // shadow, which itself only clears the character baseline by
        // ContactShadowDepthPush (0.15). Set to comfortably exceed that same
        // value, by the same margin ContactShadowDepthPush itself already
        // uses over the character baseline (0.15), so the three-way order
        // (WaitingSlotVisual behind ContactShadow behind Character) holds
        // with the same margin discipline throughout, not a smaller one.
        // Expressed in WaitingSlotVisual's own LOCAL space - it is parented
        // under a WaitingSlot GameObject carrying WaitingSlotSize as its own
        // localScale (see WaitingSlotVisual's own remarks), so the real
        // world-space push is this value times WaitingSlotSize, the same
        // deliberate parent-scale convention QueueRowDepthStep already
        // documents above.
        public const float WaitingSlotVisualDepthPush = 0.3f;



        // The scrolling belt-marker ribbon (ConveyorBeltAnimation) sits
        // between ConveyorVisual (5.0, the static frame/belt art) and
        // ContactShadow (0.15) in this same stack — pulled closer to the
        // camera than the static art so it draws on top of it (this
        // project's transparent objects sort back-to-front by real camera
        // distance, not sortingOrder alone — see ConveyorVisual's own
        // remarks), while staying far short of ContactShadow/the character
        // baseline (0) so it can never win a draw-order contest against a
        // riding collector. 4.5 leaves a full 4.5-unit margin under the
        // baseline — vastly more than a collector's own ~0.35-0.45 real
        // depth extent (see ConveyorVisualDepthPush's own history) — and a
        // comfortable 0.5-unit gap under ConveyorVisual so it reliably wins
        // that ordering too.
        public const float ConveyorBeltAnimationDepthPush = 4.5f;
        public const float ContactShadowDepthPush = 0.15f;
        public const float EnergyBarDepthPull = 0.8f;
    }
}
