using UnityEngine;
using UnityEngine.Rendering;

namespace Project001.Gameplay.Presentation
{
    /// <summary>
    /// One shared, runtime-generated procedural quad Mesh plus one shared
    /// transparent Unlit URP Material, both reused by every collector's
    /// contact shadow (see CollectorView.CreateContactShadow) - the direct
    /// MeshRenderer/MeshFilter replacement for the previous SpriteRenderer-
    /// based ContactShadowSprite, which a real Game View capture proved
    /// rendered zero visible pixels despite every measured value being
    /// valid.
    ///
    /// A stylized, camera-facing presentation decal (local XY plane, like
    /// every other flat element in this project - PixelGrid cells,
    /// WaitingSlot outlines, Conveyor path), not a physically projected
    /// ground-plane shape. CollectorView sizes it directly as a multiple of
    /// the character's own visible width, matched against a reference image
    /// for visual "grounding" feel rather than any camera-tilt projection
    /// math - visual similarity is the only success criterion here.
    ///
    /// Mirrors ContactShadowSprite's own established idiom - build once,
    /// lazily, on first access, cache for the process lifetime, shared
    /// globally across every collector rather than per-instance. Color/
    /// opacity never vary per collector or per MatchId, so a single shared
    /// Material instance is sufficient.
    /// </summary>
    public static class ContactShadowMesh
    {
        private const int TextureSize = 64;
        private const string ShaderName = "Universal Render Pipeline/Unlit";

        // Neutral dark gray/black, no directional tint. Baked into the
        // shared Material's own _BaseColor (color/opacity never vary per
        // collector or MatchId).
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.5f);

        private static Mesh _sharedMesh;
        private static Material _sharedMaterial;

        /// <summary>
        /// The shared 1x1-world-unit (at scale 1) flat quad, camera-facing
        /// (local XY plane). Every caller scales this non-uniformly (wider
        /// than tall) via its own Transform to get an oval footprint of its
        /// own desired on-screen size.
        /// </summary>
        public static Mesh SharedMesh => _sharedMesh != null ? _sharedMesh : _sharedMesh = CreateMesh();

        /// <summary>
        /// The shared transparent Unlit URP material, textured with the
        /// shared soft radial-gradient texture and tinted ShadowColor. Null
        /// only if this project's URP package does not expose a shader named
        /// ShaderName (logged as an error there, mirroring this project's
        /// usual Shader.Find null-check convention).
        /// </summary>
        public static Material SharedMaterial => _sharedMaterial != null ? _sharedMaterial : _sharedMaterial = CreateMaterial();

        private static Mesh CreateMesh()
        {
            var mesh = new Mesh { name = "ContactShadowQuad" };

            // Centered unit square, camera-facing (local XY plane, Z=0) -
            // the same convention every other flat element in this project
            // already uses. Front face normal -Z (toward the camera, which
            // sits on the -Z side of this scene). Cull is Off on the
            // material regardless (see CreateMaterial), so winding is a
            // correctness nicety only.
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();

            return mesh;
        }

        private static Material CreateMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"ContactShadowMesh: shader '{ShaderName}' not found; contact shadows will not render.");
                return null;
            }

            var material = new Material(shader) { name = "ContactShadowMaterial" };

            // Transparent alpha-blended surface, both faces visible, no
            // depth write.
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

            material.SetTexture("_BaseMap", CreateRadialTexture());
            material.SetColor("_BaseColor", ShadowColor);

            return material;
        }

        /// <summary>
        /// The soft oval's own alpha falloff, baked once into a small shared
        /// texture and sampled by the shared material's _BaseMap. A broad
        /// solid-ish plateau through InnerPlateauRadius of the radius, then
        /// a soft SmoothStep fade to fully transparent at the edge - a
        /// visible, exaggerated grounding mark rather than a tiny dark
        /// centre that disappears immediately.
        /// </summary>
        private static Texture2D CreateRadialTexture()
        {
            const float InnerPlateauRadius = 0.35f;

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

                    float alpha = 1f - Mathf.SmoothStep(InnerPlateauRadius, 1f, normalizedDistance);
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
