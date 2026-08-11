using UnityEngine;
using UnityEngine.Rendering;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// A very soft, low-opacity darkening haze centered behind the
    /// CollectorQueueBoard region - purely decorative, so the queue area
    /// reads as sitting on one shared surface instead of open air behind
    /// individual contact shadows. Created once, entirely from code, by
    /// LevelBootstrapper.BuildLevel() - never baked into Bootstrap.unity
    /// (BootstrapSceneCreator is an editor-only tool; touching it would not
    /// take effect without manually re-running its menu command and
    /// re-serializing the whole scene, risking other manual edits already in
    /// that file). Building this purely at runtime, the same way
    /// CollectorView.CreateContactShadow already creates its own shadow
    /// GameObjects with no scene-authored counterpart, sidesteps that
    /// entirely.
    ///
    /// Deliberately NOT a rounded rectangle or panel: reuses
    /// ContactShadowMesh's own plain camera-facing unit quad for geometry,
    /// but with its own separate Unlit URP material/texture - a broad,
    /// gradual radial gradient with NO inner solid plateau (unlike
    /// ContactShadowMesh's own oval, which deliberately keeps one for a
    /// visible "chunky" mark) so it reads as an amorphous haze with no
    /// perceptible edge at all, not a shape.
    ///
    /// Positioned a small, fixed distance behind the shared Z=0 gameplay
    /// plane along GameplayLayout.CameraForward (never a raw world -Z
    /// offset - see GameplayLayout.CameraForward's own remarks on why that
    /// would also drift screen Y under this project's tilted camera), so it
    /// reliably Z-tests behind every collector and contact shadow (both sit
    /// at/near Z=0) while staying far in front of GameplayBackground's own
    /// much deeper local Z=15 (parented to the camera).
    /// </summary>
    public static class QueueGroundingZone
    {
        private const int TextureSize = 64;
        private const string ShaderName = "Universal Render Pipeline/Unlit";

        // ~8-15% target visual strength - the requested starting point.
        // Very slightly cool/neutral dark, matching GameplayBackground's own
        // "barely cool, not desaturating" tint direction, never tinted
        // toward any MatchId colour.
        private static readonly Color ZoneColor = new Color(0.03f, 0.05f, 0.07f, 0.12f);

        // Generous multiples of the existing composition constants
        // (GameplayLayout.ConveyorSize / CollectorQueueBoardRegionHeight) -
        // deliberately larger than the queue's own tight footprint, since
        // the falloff itself (no inner plateau, fades across virtually the
        // whole radius) needs room to reach zero smoothly well before its
        // own edge, rather than visibly "running out" right at the queue's
        // exact boundary.
        private const float WidthMultiplier = 1.3f;
        private const float HeightMultiplier = 2.2f;

        // Small fixed pull away from the camera (see class remarks) - just
        // enough to reliably sit behind collectors/contact shadows via real
        // Z-testing, nowhere near GameplayBackground's own much deeper
        // local Z=15.
        private const float DepthPushDistance = 1.5f;

        /// <summary>
        /// Builds and places the zone once. Safe to call exactly once per
        /// scene lifetime (see LevelBootstrapper.BuildLevel, its only
        /// caller) - not idempotent/guarded, since BuildLevel itself only
        /// ever runs once per Awake.
        /// </summary>
        public static void Create()
        {
            float centerY = GameplayLayout.CollectorQueueBoardPositionY - GameplayLayout.CollectorQueueBoardRegionHeight * 0.5f;
            float width = GameplayLayout.ConveyorSize * WidthMultiplier;
            float height = GameplayLayout.CollectorQueueBoardRegionHeight * HeightMultiplier;

            var zoneObject = new GameObject("QueueGroundingZone", typeof(MeshFilter), typeof(MeshRenderer));
            zoneObject.transform.position = new Vector3(0f, centerY, 0f) + GameplayLayout.CameraForward * DepthPushDistance;
            zoneObject.transform.localScale = new Vector3(width, height, 1f);

            var meshFilter = zoneObject.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = ContactShadowMesh.SharedMesh;

            var meshRenderer = zoneObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = CreateMaterial();
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // Further back than the contact shadows' own -1, as a cheap
            // extra guarantee alongside the real Z placement above.
            meshRenderer.sortingOrder = -2;
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"QueueGroundingZone: shader '{ShaderName}' not found; the grounding zone will not render.");
                return null;
            }

            var material = new Material(shader) { name = "QueueGroundingZoneMaterial" };

            material.SetFloat("_Surface", 1f); // 1 = Transparent
            material.SetFloat("_Blend", 0f); // 0 = Alpha
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.doubleSidedGI = true;

            material.SetTexture("_BaseMap", CreateHazeTexture());
            material.SetColor("_BaseColor", ZoneColor);

            return material;
        }

        /// <summary>
        /// A pure, gradual radial falloff - maximum alpha at the exact
        /// centre, smoothly reaching zero at the edge via SmoothStep, with
        /// NO inner solid plateau (unlike ContactShadowMesh's own oval) -
        /// deliberately so this reads as a soft haze with no perceptible
        /// edge/border/shape, not a "chunky" mark.
        /// </summary>
        private static Texture2D CreateHazeTexture()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[TextureSize * TextureSize];
            float center = (TextureSize - 1) * 0.5f;
            float maxRadius = TextureSize * 0.5f;

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float normalizedDistance = Mathf.Sqrt(dx * dx + dy * dy) / maxRadius;

                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, normalizedDistance);
                    var alphaByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, alphaByte);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            return texture;
        }
    }
}
