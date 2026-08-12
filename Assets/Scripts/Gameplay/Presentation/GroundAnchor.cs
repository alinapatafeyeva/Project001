using UnityEngine;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// Marks the single point on a character's model that should visually
    /// CONTACT whatever surface it is standing/riding on - the model's own
    /// true visual ground/contact point (leg tips, shell underside, tentacle
    /// tips - whatever that species' own geometry actually rests on), never
    /// derived from the collector root or from any bounds-center
    /// recentering. Purely a position marker: holds no logic, no
    /// configuration, nothing beyond its own Transform - the exact same
    /// contract as Project001.Gameplay.Feeding.FeedTarget, which this
    /// mirrors deliberately.
    ///
    /// Added to every species' Character_XX prefab by CharacterAssetBuilder
    /// (see CreateGroundAnchor), positioned from that species' own measured
    /// mesh bounds at BUILD time - never hand-authored per prefab instance
    /// and never a runtime MatchId lookup. Because it is baked once, at
    /// build time, from the same single-level "just-Instantiated, not yet
    /// deep-nested" hierarchy CharacterAssetBuilder already assembles the
    /// prefab in, its own local position never depends on the confirmed-
    /// unreliable-at-deep-nesting-depth BakeMesh measurement runtime code
    /// elsewhere in this project had to work around (see CollectorAnimation.
    /// ComputeReliableLocalBounds's own former remarks) - reading this
    /// Transform's live .position at runtime, however deep it ends up
    /// nested (Collector root -&gt; PresentationOffset -&gt; Visual -&gt; VisualMotion
    /// -&gt; CharacterVisual -&gt; GroundAnchor), is always exactly correct,
    /// since it is plain Transform hierarchy math, not a mesh bake.
    ///
    /// Generic and species-agnostic by design: CollectorView only ever
    /// reads WorldPosition here, never a species name or MatchId - see its
    /// own remarks for how the Conveyor riding correction consumes this.
    /// </summary>
    public class GroundAnchor : MonoBehaviour
    {
        public Vector3 WorldPosition => transform.position;
    }
}
