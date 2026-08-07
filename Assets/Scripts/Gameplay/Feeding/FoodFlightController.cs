using System.Collections;
using System.Collections.Generic;
using Project001.Gameplay.Collectors;
using Project001.Gameplay.Conveyor;
using UnityEngine;

namespace Project001.Gameplay.Feeding
{
    /// <summary>
    /// Owns one collector's own incoming food queue and launches its
    /// FoodPackets one at a time, staggered by launchInterval, so several
    /// packets converging on the same collector read as a short flowing
    /// sequence rather than one simultaneous burst. A sibling of
    /// PixelConsumer on the same collector GameObject (see
    /// CollectorQueueBoard.GenerateBoard) - PixelConsumer is the only
    /// caller of Enqueue; this class never searches for pixels itself, only
    /// receives ones already consumed (and therefore already reserved - see
    /// class remarks below).
    ///
    /// Every collector gets its own instance, its own queue, and its own
    /// coroutine, so two collectors receiving food at the same moment never
    /// interact - nothing here is shared/static state (contrast FoodPacket's
    /// own OnFoodPacketLaunched/OnFoodPacketArrived, which deliberately ARE
    /// static, for a different reason - see their own remarks).
    ///
    /// Gameplay correctness this class exists to guarantee: PixelGrid
    /// already reserves a pixel (deactivates it, so it can never be matched
    /// again) the instant PixelConsumer consumes it, but this collector's
    /// RemainingHunger must not decrease until the resulting packet actually
    /// arrives. Enqueue never touches hunger; HandleArrived - a packet's
    /// mandatory arrival callback, wired in SpawnAndLaunch - is the only
    /// place ConveyorRider.RegisterConsumedPixel is ever called for a pixel
    /// that went through this pipeline, and it is called exactly once per
    /// packet, exactly when that packet visually reaches FeedTarget.
    /// </summary>
    public class FoodFlightController : MonoBehaviour
    {
        [SerializeField, Tooltip("Seconds a single packet takes to fly from its consumed pixel's position to this collector's FeedTarget. Constant for every packet - no easing/variation yet.")]
        [Min(0.01f)]
        private float flightDuration = 0.35f;

        [SerializeField, Tooltip("Seconds between LAUNCHING consecutive queued packets (packet 1, wait this long, packet 2, ...) - not a gap between arrivals, and not the flight duration itself.")]
        [Min(0f)]
        private float launchInterval = 0.04f;

        private ConveyorRider _rider;
        private FeedTarget _feedTarget;
        private CollectorView _collectorView;
        private readonly Queue<PendingPacket> _pending = new Queue<PendingPacket>();
        private Coroutine _launchRoutine;

        private struct PendingPacket
        {
            public Vector3 StartWorldPosition;
            public Color Color;
        }

        /// <summary>
        /// Assigns the rider this controller eventually decrements hunger
        /// on and the FeedTarget every launched packet flies toward.
        /// feedTarget may be null (a species prefab with none) -
        /// SpawnAndLaunch then falls back to this collector's own root
        /// transform as the destination, so a missing FeedTarget degrades
        /// to "food arrives at the collector" rather than silently
        /// dropping that pixel's hunger credit forever.
        /// </summary>
        public void Initialize(ConveyorRider rider, FeedTarget feedTarget, CollectorView collectorView)
        {
            _rider = rider;
            _feedTarget = feedTarget;
            _collectorView = collectorView;
        }

        /// <summary>
        /// Queues one packet starting at startWorldPosition (the pixel's own
        /// world position at the moment PixelConsumer consumed it) and
        /// starts the launch sequence if it is not already running. Safe to
        /// call while a sequence is already in flight - the new packet just
        /// joins the back of the queue.
        /// </summary>
        public void Enqueue(Vector3 startWorldPosition, Color color)
        {
            _pending.Enqueue(new PendingPacket { StartWorldPosition = startWorldPosition, Color = color });

            if (_launchRoutine == null)
                _launchRoutine = StartCoroutine(LaunchSequence());
        }

        /// <summary>
        /// Dequeues and launches one packet at a time, waiting
        /// launchInterval after EVERY launch - including the last one
        /// currently known - before checking the queue again. Each packet's
        /// own flight then runs independently on its own coroutine
        /// (FoodPacket.FlightRoutine), so packets launched close together
        /// are typically still in flight simultaneously; this loop never
        /// waits for one to arrive before launching the next.
        ///
        /// Deliberately NOT "only wait if the queue still has more items
        /// right now": StartCoroutine runs a coroutine body synchronously up
        /// to its first yield, so the very first Enqueue call that starts
        /// this coroutine would otherwise dequeue-and-launch its own single
        /// item, find the queue already empty again (nothing later has been
        /// enqueued yet), skip the wait entirely, and exit - clearing
        /// _launchRoutine before a caller's very next Enqueue call (e.g.
        /// several pixels consumed in the same frame) even runs, so that
        /// call sees _launchRoutine == null and starts a SECOND, independent
        /// single-shot coroutine instead of joining the first one's queue -
        /// every packet launching essentially simultaneously despite
        /// launchInterval, confirmed empirically via a direct burst test
        /// before this fix. Waiting unconditionally keeps this coroutine
        /// (and therefore _launchRoutine) alive and suspended for
        /// launchInterval after every launch, so a same-frame or
        /// shortly-after Enqueue call always finds _launchRoutine already
        /// non-null and correctly joins the queue instead of racing a new
        /// coroutine - the small cost is one harmless trailing
        /// launchInterval wait after the last real packet before this
        /// coroutine notices the queue is empty and clears _launchRoutine.
        /// </summary>
        private IEnumerator LaunchSequence()
        {
            while (_pending.Count > 0)
            {
                PendingPacket next = _pending.Dequeue();
                SpawnAndLaunch(next);

                if (launchInterval > 0f)
                    yield return new WaitForSeconds(launchInterval);
                else
                    yield return null;
            }

            _launchRoutine = null;
        }

        private void SpawnAndLaunch(PendingPacket pending)
        {
            Transform target = _feedTarget != null ? _feedTarget.transform : transform;

            FoodPacket packet = FoodPacket.Create(pending.StartWorldPosition, pending.Color);
            packet.Launch(target, flightDuration, _collectorView, HandleArrived);
        }

        private void HandleArrived(FoodPacket packet)
        {
            _rider?.RegisterConsumedPixel();
        }
    }
}
