using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// The four straight edges of the conveyor loop, in the fixed world-space
    /// sense a rider on that edge faces the pixel grid (which always sits at
    /// the loop's center) — never the loop's own point-list winding order.
    /// Used by ConveyorOrientationSample/SampleOrientation below, and by
    /// CollectorAnimation to pick the one fixed yaw each straight edge holds.
    /// </summary>
    public enum ConveyorEdge
    {
        Bottom,
        Right,
        Top,
        Left,
    }

    /// <summary>
    /// Which stable edge orientation a path progress value currently belongs
    /// to, for facing purposes. On a straight, FromEdge == ToEdge and
    /// CornerT is meaningless (always 0f); inside a rounded corner, FromEdge
    /// is the straight just left, ToEdge is the straight about to be
    /// entered, and CornerT is that corner's own [0, 1] progress — 0f the
    /// instant the corner is entered, 1f the instant it is exited — so a
    /// caller can interpolate FromEdge's fixed orientation to ToEdge's
    /// exactly across the corner's own travel, with no dependency on frame
    /// rate or elapsed time.
    /// </summary>
    public readonly struct ConveyorOrientationSample
    {
        public ConveyorEdge FromEdge { get; }
        public ConveyorEdge ToEdge { get; }
        public float CornerT { get; }

        public ConveyorOrientationSample(ConveyorEdge fromEdge, ConveyorEdge toEdge, float cornerT)
        {
            FromEdge = fromEdge;
            ToEdge = toEdge;
            CornerT = cornerT;
        }
    }

    /// <summary>
    /// A closed, rounded-rectangle route around the pixel grid, expressed as an
    /// ordered list of local-space points. Only stores and samples the route;
    /// it does not move anything along it.
    /// </summary>
    public class ConveyorPath : MonoBehaviour
    {
        private const int SegmentsPerCorner = 8;

        // BuildPath visits the 4 rounded corners counter-clockwise starting
        // top-right (see BuildPath); the straight edge a rider is on right
        // before/after each corner follows directly from that fixed winding,
        // never from width/height/radius, so these two arrays are constant
        // regardless of how the path is configured. Index matches the corner
        // visit order: 0 = top-right, 1 = top-left, 2 = bottom-left,
        // 3 = bottom-right.
        private static readonly ConveyorEdge[] EdgeBeforeCorner =
        {
            ConveyorEdge.Right, ConveyorEdge.Top, ConveyorEdge.Left, ConveyorEdge.Bottom,
        };

        private static readonly ConveyorEdge[] EdgeAfterCorner =
        {
            ConveyorEdge.Top, ConveyorEdge.Left, ConveyorEdge.Bottom, ConveyorEdge.Right,
        };

        [SerializeField, Tooltip("Total width of the route, in world units.")]
        [Min(0.1f)]
        private float width = 8f;

        [SerializeField, Tooltip("Total height of the route, in world units.")]
        [Min(0.1f)]
        private float height = 6f;

        [SerializeField, Tooltip("Radius of the rounded corners, in world units.")]
        [Min(0f)]
        private float cornerRadius = 1f;

        private readonly List<Vector3> _points = new List<Vector3>();
        private float[] _cumulativeLengths;
        private float _totalLength;

        // The point-index range each of the 4 corners occupies within
        // _points, captured in BuildPath rather than assumed from
        // SegmentsPerCorner — a zero cornerRadius collapses a corner to a
        // single point (see AddCornerArc), which would silently break a
        // fixed "9 points per corner" assumption. Converted to normalized
        // progress (via _cumulativeLengths) by BuildOrientationBoundaries,
        // indexed in the same corner-visit order as EdgeBeforeCorner/
        // EdgeAfterCorner: 0 = top-right, 1 = top-left, 2 = bottom-left,
        // 3 = bottom-right.
        private readonly int[] _cornerStartPointIndex = new int[4];
        private readonly int[] _cornerEndPointIndex = new int[4];
        private readonly float[] _cornerStartProgress = new float[4];
        private readonly float[] _cornerEndProgress = new float[4];

        public IReadOnlyList<Vector3> Points => _points;

        public float PathLength => _totalLength;

        private void Awake()
        {
            BuildPath();
        }

        private void BuildPath()
        {
            _points.Clear();

            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            float radius = Mathf.Min(cornerRadius, Mathf.Min(halfWidth, halfHeight));

            // Corners are visited counter-clockwise; the straight edges between them
            // are implicit, formed by the segment connecting one corner's last point
            // to the next corner's first point.
            AddCorner(0, new Vector2(halfWidth - radius, halfHeight - radius), radius, 0f, 90f);
            AddCorner(1, new Vector2(-halfWidth + radius, halfHeight - radius), radius, 90f, 180f);
            AddCorner(2, new Vector2(-halfWidth + radius, -halfHeight + radius), radius, 180f, 270f);
            AddCorner(3, new Vector2(halfWidth - radius, -halfHeight + radius), radius, 270f, 360f);

            BuildLengthTable();
            BuildOrientationBoundaries();
        }

        private void AddCorner(int cornerIndex, Vector2 center, float radius, float startAngleDeg, float endAngleDeg)
        {
            _cornerStartPointIndex[cornerIndex] = _points.Count;
            AddCornerArc(center, radius, startAngleDeg, endAngleDeg);
            _cornerEndPointIndex[cornerIndex] = _points.Count - 1;
        }

        private void AddCornerArc(Vector2 center, float radius, float startAngleDeg, float endAngleDeg)
        {
            // A zero radius collapses the arc to a single rectangle corner; avoid
            // emitting SegmentsPerCorner + 1 duplicate points at the same position.
            if (radius <= 0f)
            {
                _points.Add(new Vector3(center.x, center.y, 0f));
                return;
            }

            for (int i = 0; i <= SegmentsPerCorner; i++)
            {
                float t = i / (float)SegmentsPerCorner;
                float angle = Mathf.Lerp(startAngleDeg, endAngleDeg, t) * Mathf.Deg2Rad;
                var point = new Vector3(
                    center.x + radius * Mathf.Cos(angle),
                    center.y + radius * Mathf.Sin(angle),
                    0f);
                _points.Add(point);
            }
        }

        private void BuildLengthTable()
        {
            int count = _points.Count;
            _cumulativeLengths = new float[count + 1];

            for (int i = 1; i < count; i++)
                _cumulativeLengths[i] = _cumulativeLengths[i - 1] + Vector3.Distance(_points[i - 1], _points[i]);

            float closingLength = count > 0 ? Vector3.Distance(_points[count - 1], _points[0]) : 0f;
            _cumulativeLengths[count] = (count > 0 ? _cumulativeLengths[count - 1] : 0f) + closingLength;

            _totalLength = _cumulativeLengths[count];
        }

        /// <summary>
        /// Converts each corner's captured point-index range into normalized
        /// [0, 1] progress, using the same _cumulativeLengths table
        /// GetPositionAtProgress samples — so a corner's progress bounds
        /// always match its actual arc length, not an assumed equal share
        /// of the loop.
        /// </summary>
        private void BuildOrientationBoundaries()
        {
            for (int corner = 0; corner < 4; corner++)
            {
                _cornerStartProgress[corner] = _totalLength > 0f
                    ? _cumulativeLengths[_cornerStartPointIndex[corner]] / _totalLength
                    : 0f;
                _cornerEndProgress[corner] = _totalLength > 0f
                    ? _cumulativeLengths[_cornerEndPointIndex[corner]] / _totalLength
                    : 0f;
            }
        }

        /// <summary>
        /// Classifies a normalized path progress as either squarely on one of
        /// the 4 straight edges (FromEdge == ToEdge, CornerT unused) or
        /// partway through one of the 4 rounded corners connecting two
        /// straights (see ConveyorOrientationSample) — the segment/corner
        /// progress CollectorAnimation drives Visual's yaw from, replacing a
        /// per-frame "aim at the grid center" computation that produced
        /// continuous rotation even while a rider crossed a straight edge.
        /// </summary>
        public ConveyorOrientationSample SampleOrientation(float progress)
        {
            float wrapped = Mathf.Repeat(progress, 1f);

            for (int corner = 0; corner < 4; corner++)
            {
                if (wrapped < _cornerStartProgress[corner] || wrapped > _cornerEndProgress[corner])
                    continue;

                float span = _cornerEndProgress[corner] - _cornerStartProgress[corner];
                float cornerT = span > 0f ? (wrapped - _cornerStartProgress[corner]) / span : 1f;
                return new ConveyorOrientationSample(EdgeBeforeCorner[corner], EdgeAfterCorner[corner], cornerT);
            }

            // Not inside any corner: on the straight following whichever
            // corner most recently ended (searched back-to-front since
            // corners' end-progress values increase with corner index).
            for (int corner = 3; corner >= 0; corner--)
            {
                if (wrapped >= _cornerEndProgress[corner])
                {
                    ConveyorEdge edge = EdgeAfterCorner[corner];
                    return new ConveyorOrientationSample(edge, edge, 0f);
                }
            }

            // Only reachable if wrapped sits before corner 0's own start
            // (corner 0 starts at progress 0 by construction, so this is a
            // defensive fallback, not a real case) — treat it as still on
            // the straight that wraps into corner 0.
            ConveyorEdge wrapEdge = EdgeAfterCorner[3];
            return new ConveyorOrientationSample(wrapEdge, wrapEdge, 0f);
        }

        /// <summary>
        /// Returns the local-space position along the closed route at the given
        /// normalized progress. Values outside [0, 1] wrap around the loop.
        /// </summary>
        public Vector3 GetPositionAtProgress(float progress)
        {
            if (_points.Count == 0)
                return Vector3.zero;

            if (_totalLength <= 0f)
                return _points[0];

            float wrapped = progress - Mathf.Floor(progress);
            float targetDistance = wrapped * _totalLength;

            int segmentCount = _points.Count;
            for (int i = 0; i < segmentCount; i++)
            {
                float segmentStart = _cumulativeLengths[i];
                float segmentEnd = _cumulativeLengths[i + 1];

                if (targetDistance <= segmentEnd || i == segmentCount - 1)
                {
                    float segmentLength = segmentEnd - segmentStart;
                    float segmentT = segmentLength > 0f ? (targetDistance - segmentStart) / segmentLength : 0f;

                    Vector3 a = _points[i];
                    Vector3 b = _points[(i + 1) % segmentCount];
                    return Vector3.Lerp(a, b, segmentT);
                }
            }

            return _points[0];
        }
    }
}
