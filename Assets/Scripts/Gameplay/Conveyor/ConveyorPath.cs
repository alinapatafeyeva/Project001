using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// A closed, rounded-rectangle route around the pixel grid, expressed as an
    /// ordered list of local-space points. Only stores and samples the route;
    /// it does not move anything along it.
    /// </summary>
    public class ConveyorPath : MonoBehaviour
    {
        private const int SegmentsPerCorner = 8;

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
            AddCornerArc(new Vector2(halfWidth - radius, halfHeight - radius), radius, 0f, 90f);
            AddCornerArc(new Vector2(-halfWidth + radius, halfHeight - radius), radius, 90f, 180f);
            AddCornerArc(new Vector2(-halfWidth + radius, -halfHeight + radius), radius, 180f, 270f);
            AddCornerArc(new Vector2(halfWidth - radius, -halfHeight + radius), radius, 270f, 360f);

            BuildLengthTable();
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
