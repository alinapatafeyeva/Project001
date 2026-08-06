using UnityEngine;

namespace Project001.Gameplay.Feeding
{
    /// <summary>
    /// Marks the single world position on a character's model where a
    /// FoodPacket should arrive - approximately the mouth/upper-face area.
    /// Purely a position marker: holds no logic, no configuration, nothing
    /// beyond its own Transform. FoodFlightController reads WorldPosition
    /// fresh every frame while a packet is in flight (never a cached
    /// snapshot), so the destination stays correct even while the collector
    /// - and therefore this Transform, since FeedTarget is a child of the
    /// character model - is moving, e.g. riding the Conveyor.
    ///
    /// Added to every species' Character_XX prefab by CharacterAssetBuilder
    /// (see CreateFeedTarget), positioned from that species' own measured
    /// mesh bounds - never hand-authored per prefab and never a
    /// screen-space position. Deliberately just a marker rather than a
    /// bare Transform reference: CollectorView resolves it the same way it
    /// resolves CharacterPosePresentation (GetComponentInChildren), so a
    /// species whose prefab predates this feature (or whose build failed to
    /// add one) is simply absent rather than silently wrong.
    /// </summary>
    public class FeedTarget : MonoBehaviour
    {
        public Vector3 WorldPosition => transform.position;
    }
}
