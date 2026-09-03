using System;
using System.Collections;
using UnityEngine;

namespace Project001.Gameplay.Collectors
{
    /// <summary>
    /// Shared root-Transform position travel used by both Recovery Row
    /// transitions — arriving on a Save me rescue (RecoveryRowView.Refresh)
    /// and departing back onto the Conveyor (CollectorSelectionController's
    /// Recovery-Row-sourced pending boardings). Applies an eased Lerp over a
    /// fixed duration via the given applyPosition callback, so callers can
    /// target either transform.position (world space, still parented under
    /// Recovery Row while departing) or transform.localPosition (already
    /// reparented, RecoveryRowView's own row layout) with the same code.
    ///
    /// There is no earlier duration-based root-position tween anywhere in
    /// this project to reuse instead: the existing Waiting Line -> Conveyor
    /// boarding (ConveyorSystem.TryAddRider) sets a rider's position
    /// instantly to the fixed boarding point — it only reads as a smooth
    /// "fly" transition because that boarding point sits close to Waiting
    /// Line by construction, not because of any actual tween. This follows
    /// the project's own established elapsed/duration + Mathf.Clamp01 +
    /// shaping-function idiom instead (see e.g.
    /// CollectorAnimation.BoardingBounceRoutine/SquashPunchRoutine), with a
    /// standard smoothstep ease so a travel visibly accelerates then
    /// decelerates rather than moving at constant speed — the closest
    /// faithful match to "speed/easing/duration consistent with the rest of
    /// the game" available without inventing an unrelated animation system.
    /// </summary>
    public static class CollectorPositionTravel
    {
        public const float DefaultDuration = 0.35f;

        public static IEnumerator Travel(Vector3 from, Vector3 to, float duration, Action<Vector3> applyPosition)
        {
            if (applyPosition == null)
                yield break;

            if (duration <= 0f)
            {
                applyPosition(to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                applyPosition(Vector3.Lerp(from, to, eased));
                yield return null;
            }

            applyPosition(to);
        }
    }
}
