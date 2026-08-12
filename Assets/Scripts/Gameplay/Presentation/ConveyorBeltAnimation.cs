using System.Collections.Generic;
using Project001.Gameplay.Conveyor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// The first Conveyor movement animation: a thin, closed-loop ribbon of
    /// soft scrolling chevron markers drawn directly over the belt band of
    /// ConveyorVisual's static Conveyor.png, so the belt visibly reads as
    /// moving without the cream frame or the belt art itself ever moving,
    /// rotating, or scaling. Purely presentational — reads ConveyorPath's
    /// public Points/SampleOrientation and ConveyorSystem's public
    /// MoveSpeed, never writes to either, and owns no riding/gameplay state
    /// of its own (mirrors ConveyorVisual's own contract).
    ///
    /// ----- Why one ribbon mesh, not four per-side strips -----
    /// ConveyorPath.Points already walks the ENTIRE closed loop — every
    /// straight and every rounded corner — in one fixed order (see
    /// ConveyorPath's own remarks: corners visited counter-clockwise,
    /// straights implicit between them). Building a single ribbon strip
    /// along that exact point order, with each vertex's U texture
    /// coordinate set from its own cumulative arc-length position, gives
    /// one continuous, seamless UV parameterization all the way around —
    /// scrolling that one ribbon's material offset animates every straight
    /// AND every corner in lockstep, by construction, with no seam to keep
    /// synchronized (the four-independent-strips fallback the task brief
    /// allows is unnecessary here; a single ribbon is both simpler and
    /// already frame-synchronized).
    ///
    /// ----- Direction -----
    /// ConveyorSystem.Update's own comment establishes ground truth: "a
    /// positive delta progress walks the path's point order, which is
    /// counter-clockwise" — i.e. real riders move in exactly the order
    /// ConveyorPath.Points is stored in (increasing index / increasing
    /// cumulative arc length). This ribbon's vertex U coordinates are built
    /// from that exact same cumulative arc length (see BuildMesh), so
    /// "increasing U" already means "the real riding direction" by
    /// construction — never a separately-guessed direction, and never
    /// taken from Conveyor.png's own arrow decals (which happen to agree,
    /// but per the task brief were not trusted as the source of truth).
    /// See Update's own remarks for the sign that turns "increasing U"
    /// into markers that visibly advance forward once texture-tiling math
    /// is accounted for.
    ///
    /// ----- Speed -----
    /// Never reads or copies any gameplay constant. Every Update() reads
    /// ConveyorSystem.MoveSpeed live, so the belt visibly speeds up/slows
    /// down the instant ConveyorSystem.ApplySpeedMultiplier changes it
    /// (e.g. Endgame Cleanup) — no duplicated speed value can ever drift
    /// out of sync with the real one.
    ///
    /// ----- Alignment -----
    /// Each vertex is nudged by ConveyorBeltCalibration.ComputeOffset (the
    /// same measured, presentation-only belt-vs-path correction riding
    /// collectors already use) so the ribbon sits on the actual rendered
    /// belt centerline, not ConveyorPath's raw mathematical line — reusing
    /// that existing calibration rather than re-measuring the belt band
    /// independently.
    ///
    /// ----- Performance -----
    /// The ribbon mesh is built exactly once (Start, after ConveyorPath has
    /// generated its own points in Awake — mirrors the removed
    /// ConveyorPathRenderer's own execution-order note) and never rebuilt.
    /// Every subsequent frame only assigns one Vector2 to the shared
    /// material's texture offset — no per-frame mesh work, no
    /// spawning/despawning, no particle system, no physics.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ConveyorBeltAnimation : MonoBehaviour
    {
        public const string SortingLayerNameValue = ConveyorVisual.SortingLayerNameValue;

        // One step above ConveyorVisual's own sortingOrder (0) — a cheap,
        // secondary tie-breaker only, exactly like ConveyorVisual's own
        // remarks describe; the real guarantee is the depth push below.
        private const int SortingOrderValue = 1;

        public const float DepthPushDistanceValue = GameplayLayout.ConveyorBeltAnimationDepthPush;

        private const string ShaderName = "Universal Render Pipeline/Unlit";

        // Texture-space tile: MarkerTextureWidth columns cover exactly one
        // repeat period (MarkerTileLength world units, see below);
        // MarkerTextureHeight rows cover the ribbon's full width (V 0..1).
        // Raised from the original 128x32 alongside the thinner stroke
        // below — a thin line needs more texel resolution to stay crisp
        // and anti-aliased rather than blocky. Still small and cheap, one
        // texture built once and shared.
        private const int MarkerTextureWidth = 192;
        private const int MarkerTextureHeight = 64;

        // Chevron shape, in tile-local [0,1) coordinates. Apex points
        // toward increasing U (texture space) — since increasing U is
        // exactly "the real riding direction" (see class remarks), this
        // one baked shape already points the correct way on every straight
        // and every corner, with no per-edge rotation needed.
        //
        // Shrunk from an earlier, visually "too thick/heavy" pass (arm
        // length 0.24, spread 0.30, stroke half-thickness 0.075) per manual
        // Unity review — every dimension reduced so the mark reads as a
        // thin directional tick rather than a bold rectangular bar: shorter
        // arms, a narrower spread across the belt, and roughly half the old
        // stroke thickness (combined with beltHalfWidth also shrinking
        // below, the real on-belt stroke width drops from ~0.05 to
        // ~0.016 world units).
        private const float ChevronApexU = 0.62f;
        private const float ChevronArmLengthU = 0.16f;
        private const float ChevronHalfSpreadV = 0.22f;
        private const float ChevronStrokeHalfThicknessV = 0.045f;

        [SerializeField, Tooltip("The same ConveyorPath the real riders move along (read-only — never written).")]
        private ConveyorPath conveyorPath;

        [SerializeField, Tooltip("The same ConveyorSystem riders move through — only its MoveSpeed is read.")]
        private ConveyorSystem conveyorSystem;

        [SerializeField, Tooltip("Ribbon width, in world units — kept narrow so it sits inside the belt band, never reaching the cream frame.")]
        [Min(0.01f)]
        private float beltHalfWidth = 0.10f;

        [SerializeField, Tooltip("World-space length of one marker tile — smaller reads busier, larger reads sparser.")]
        [Min(0.05f)]
        private float markerTileLength = 0.9f;

        [SerializeField, Tooltip("Soft marker tint — kept light and low-alpha per the 'subtle and premium' brief; never pure white/flashing.")]
        private Color markerColor = new Color(0.90f, 0.93f, 0.97f, 0.45f);

        private Material _material;
        private Mesh _mesh;
        private Texture2D _markerTexture;
        private float _accumulatedU;

        private void Awake()
        {
            // Baked here purely for an accurate scene-view preview —
            // mirrors ConveyorVisual.Awake's own convention of applying
            // this same push at runtime regardless of what the scene has
            // saved.
            transform.localPosition = GameplayLayout.CameraForward * DepthPushDistanceValue;
        }

        // Runs after every Awake, guaranteeing conveyorPath has already
        // generated its own Points (built in ConveyorPath.Awake) regardless
        // of component execution order — the same reason the removed
        // ConveyorPathRenderer built in Start rather than Awake.
        private void Start()
        {
            BuildMaterial();
            BuildMesh();
        }

        private void Update()
        {
            if (_material == null || markerTileLength <= 0f)
                return;

            float speed = conveyorSystem != null ? conveyorSystem.MoveSpeed : 0f;
            _accumulatedU += speed * Time.deltaTime / markerTileLength;

            // A fragment at a fixed vertex (fixed vertexU) samples texcoord
            // = vertexU + offset (Unity's standard uv*tiling+offset
            // convention). Solving for which vertexU shows a texture
            // feature baked at a fixed coordinate T at any moment gives
            // vertexU = T - offset — so as offset INCREASES, the feature's
            // own apparent vertexU DECREASES, i.e. the visible pattern
            // slides toward LOWER vertexU. Since vertexU increases in the
            // real riding direction by construction (see BuildMesh), a
            // NEGATIVE, decreasing offset is what is required to slide the
            // pattern toward HIGHER vertexU — forward, matching real riders.
            // (An earlier pass flipped this sign based on a batch-mode
            // screenshot diagnostic that turned out to be unreliable —
            // small crop misreads and frame-timing flakes in that headless
            // harness — and a manual Play Mode review correctly caught the
            // resulting reversal. This sign is back to the one the formula
            // above supports.)
            _material.mainTextureOffset = new Vector2(-_accumulatedU, 0f);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                Destroy(_mesh);
            if (_material != null)
                Destroy(_material);
            if (_markerTexture != null)
                Destroy(_markerTexture);
        }

        private void BuildMesh()
        {
            if (conveyorPath == null)
            {
                Debug.LogError("ConveyorBeltAnimation: no ConveyorPath assigned; belt animation will not render.");
                return;
            }

            IReadOnlyList<Vector3> points = conveyorPath.Points;
            int pointCount = points.Count;
            if (pointCount < 2)
                return;

            // Cumulative arc length around the closed loop, including the
            // closing segment back to point 0 — the exact same summation
            // ConveyorPath's own internal BuildLengthTable performs over
            // this same public Points list, recomputed here only because
            // ConveyorPath exposes no public accessor for it (never touches
            // ConveyorPath itself). Because it is the same points in the
            // same order summed the same way, the normalized progress
            // values this produces line up exactly with ConveyorPath's own,
            // so SampleOrientation below resolves the correct edge/corner
            // for every vertex.
            var cumulativeLength = new float[pointCount + 1];
            for (int i = 1; i < pointCount; i++)
                cumulativeLength[i] = cumulativeLength[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            cumulativeLength[pointCount] = cumulativeLength[pointCount - 1] + Vector3.Distance(points[pointCount - 1], points[0]);

            float totalLength = cumulativeLength[pointCount];
            if (totalLength <= 0f)
                return;

            // pointCount + 1 vertex RINGS, not pointCount: the last ring
            // duplicates point 0's own position but carries u = totalLength
            // / markerTileLength instead of wrapping back to u = 0. Without
            // this duplicate, the single quad that closes the loop (from
            // the last point back to point 0 — the Right edge, see
            // ConveyorPath's own corner-visit order) would have its two
            // ends' U coordinates jump from nearly the full loop length
            // down to 0, making the GPU interpolate — and therefore scroll
            // — that one quad's texture BACKWARD relative to every other
            // edge and corner, even though every other quad's own U still
            // increases correctly in the real riding direction. (Caught via
            // a real Play Mode direction check on exactly that edge — see
            // ConveyorBeltAnimationDiagnostic's own report.) U is left
            // unbounded past 1 here on purpose — the material's Repeat wrap
            // mode (see BuildMaterial) samples texcoord mod 1 in hardware,
            // so the texture itself still tiles seamlessly; only the
            // straight-line vertex-to-vertex interpolation needed fixing.
            var vertices = new Vector3[(pointCount + 1) * 2];
            var uvs = new Vector2[(pointCount + 1) * 2];

            for (int ring = 0; ring <= pointCount; ring++)
            {
                int i = ring % pointCount;
                Vector3 prev = points[(i - 1 + pointCount) % pointCount];
                Vector3 next = points[(i + 1) % pointCount];

                Vector3 tangent = next - prev;
                tangent.z = 0f;
                if (tangent.sqrMagnitude < 1e-8f)
                    tangent = Vector3.right;
                tangent.Normalize();

                // In-plane perpendicular, one fixed rotation applied
                // uniformly at every point around the loop (never flipped
                // per-edge/per-corner) — the ribbon therefore keeps a
                // consistent width and side with no seam or twist anywhere
                // around the loop.
                var normal = new Vector3(-tangent.y, tangent.x, 0f);

                float progress = cumulativeLength[i] / totalLength;
                ConveyorOrientationSample orientation = conveyorPath.SampleOrientation(progress);
                Vector2 beltOffset = ConveyorBeltCalibration.ComputeOffset(orientation);
                Vector3 basePosition = points[i] + new Vector3(beltOffset.x, beltOffset.y, 0f);

                float u = cumulativeLength[ring] / markerTileLength;

                vertices[ring * 2] = basePosition - normal * beltHalfWidth;
                vertices[ring * 2 + 1] = basePosition + normal * beltHalfWidth;
                uvs[ring * 2] = new Vector2(u, 0f);
                uvs[ring * 2 + 1] = new Vector2(u, 1f);
            }

            var triangles = new int[pointCount * 6];
            for (int ring = 0; ring < pointCount; ring++)
            {
                int v0 = ring * 2, v1 = ring * 2 + 1, v2 = (ring + 1) * 2, v3 = (ring + 1) * 2 + 1;
                int t = ring * 6;
                triangles[t] = v0;
                triangles[t + 1] = v2;
                triangles[t + 2] = v1;
                triangles[t + 3] = v1;
                triangles[t + 4] = v2;
                triangles[t + 5] = v3;
            }

            _mesh = new Mesh { name = "ConveyorBeltAnimationRibbon" };
            _mesh.vertices = vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.RecalculateBounds();
            _mesh.RecalculateNormals();

            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.sortingLayerName = SortingLayerNameValue;
            meshRenderer.sortingOrder = SortingOrderValue;
        }

        private void BuildMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"ConveyorBeltAnimation: shader '{ShaderName}' not found; belt animation will not render.");
                return;
            }

            _material = new Material(shader) { name = "ConveyorBeltAnimationMaterial" };

            // Transparent, unlit, double-sided, no depth write — the same
            // convention ContactShadowMesh's own shared material already
            // established for every other flat, camera-facing decal in
            // this project.
            _material.SetFloat("_Surface", 1f); // 1 = Transparent
            _material.SetFloat("_Blend", 0f); // 0 = Alpha
            _material.SetFloat("_Cull", (float)CullMode.Off);
            _material.SetFloat("_ZWrite", 0f);
            _material.SetFloat("_AlphaClip", 0f);
            _material.SetOverrideTag("RenderType", "Transparent");
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.DisableKeyword("_ALPHATEST_ON");
            _material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)RenderQueue.Transparent;
            _material.doubleSidedGI = true;

            _markerTexture = CreateMarkerTexture();
            _material.SetTexture("_BaseMap", _markerTexture);
            _material.SetColor("_BaseColor", markerColor);
        }

        /// <summary>
        /// One tileable chevron, soft-edged, alpha-shaped on a transparent
        /// background — repeats seamlessly along U (wrap Repeat) as the
        /// ribbon's UVs run multiple tiles around the loop; V spans the
        /// ribbon's full width once (wrap Clamp), so no vertical repeat is
        /// needed. Mirrors ContactShadowMesh.CreateRadialTexture's own
        /// established idiom for a small, runtime-baked, soft-alpha
        /// presentation shape in this project.
        /// </summary>
        private static Texture2D CreateMarkerTexture()
        {
            var texture = new Texture2D(MarkerTextureWidth, MarkerTextureHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[MarkerTextureWidth * MarkerTextureHeight];

            for (int y = 0; y < MarkerTextureHeight; y++)
            {
                float v = (y + 0.5f) / MarkerTextureHeight;
                float distanceFromCenterV = Mathf.Abs(v - 0.5f);

                for (int x = 0; x < MarkerTextureWidth; x++)
                {
                    float u = (x + 0.5f) / MarkerTextureWidth;
                    float alpha = ChevronAlpha(u, distanceFromCenterV);
                    var byteAlpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * MarkerTextureWidth + x] = new Color32(255, 255, 255, byteAlpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }

        /// <summary>
        /// Alpha for one '>' chevron stroke (two arms meeting at
        /// ChevronApexU, pointing toward increasing U — the real riding
        /// direction, see class remarks), with a soft SmoothStep edge for
        /// anti-aliasing instead of a hard cutoff. Also fades the belt-edge
        /// rows (near distanceFromCenterV = 0.5) toward transparent so the
        /// ribbon's own straight top/bottom edges never read as a hard
        /// rectangle.
        /// </summary>
        private static float ChevronAlpha(float u, float distanceFromCenterV)
        {
            float edgeFade = 1f - Mathf.SmoothStep(0.42f, 0.5f, distanceFromCenterV);
            if (edgeFade <= 0f)
                return 0f;

            float distanceFromApex = ChevronApexU - u;
            if (distanceFromApex < 0f || distanceFromApex > ChevronArmLengthU)
                return 0f;

            float expectedDistanceFromCenterV = (distanceFromApex / ChevronArmLengthU) * ChevronHalfSpreadV;
            float strokeDelta = Mathf.Abs(distanceFromCenterV - expectedDistanceFromCenterV);
            float strokeAlpha = 1f - Mathf.SmoothStep(0f, ChevronStrokeHalfThicknessV, strokeDelta);

            return strokeAlpha * edgeFade;
        }
    }
}
