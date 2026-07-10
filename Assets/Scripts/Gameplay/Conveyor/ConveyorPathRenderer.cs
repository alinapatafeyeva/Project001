using System.Collections.Generic;
using UnityEngine;

namespace Project001.Gameplay.Conveyor
{
    /// <summary>
    /// Draws the closed route of a ConveyorPath using a LineRenderer. Purely
    /// visual and temporary; owns no path or movement logic of its own.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class ConveyorPathRenderer : MonoBehaviour
    {
        private const string ShaderName = "Universal Render Pipeline/Unlit";

        [SerializeField, Tooltip("ConveyorPath to visualise. Defaults to one on this GameObject.")]
        private ConveyorPath conveyorPath;

        [SerializeField, Tooltip("Width of the drawn line, in world units.")]
        [Min(0.01f)]
        private float lineWidth = 0.1f;

        [SerializeField, Tooltip("Colour of the drawn line.")]
        private Color lineColor = Color.white;

        private LineRenderer _lineRenderer;
        private Material _lineMaterial;

        private void Awake()
        {
            _lineRenderer = GetComponent<LineRenderer>();

            if (conveyorPath == null)
                conveyorPath = GetComponent<ConveyorPath>();
        }

        // Runs after every Awake, guaranteeing the referenced ConveyorPath has
        // already generated its points regardless of component execution order.
        private void Start()
        {
            ConfigureLineRenderer();
            DrawPath();
        }

        private void ConfigureLineRenderer()
        {
            _lineRenderer.loop = true;
            _lineRenderer.useWorldSpace = false;
            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.startColor = lineColor;
            _lineRenderer.endColor = lineColor;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                return;

            _lineMaterial = new Material(shader) { name = "ConveyorPathMaterial" };
            _lineMaterial.SetColor("_BaseColor", lineColor);
            _lineRenderer.material = _lineMaterial;
        }

        private void DrawPath()
        {
            if (conveyorPath == null)
                return;

            IReadOnlyList<Vector3> points = conveyorPath.Points;
            _lineRenderer.positionCount = points.Count;

            for (int i = 0; i < points.Count; i++)
                _lineRenderer.SetPosition(i, points[i]);
        }

        private void OnDestroy()
        {
            if (_lineMaterial != null)
                Destroy(_lineMaterial);
        }
    }
}
