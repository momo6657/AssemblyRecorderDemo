using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using GLTFast.Schema;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 强制使用 URP/Lit shader 的 glTFast material generator。
/// 解决 Shader Graphs/glTF-pbrMetallicRoughness 在 Android 构建中缺失导致材质变白的问题。
/// </summary>
public class UrpLitMaterialGenerator : IMaterialGenerator
{
    ICodeLogger _logger;
    public void SetLogger(ICodeLogger logger) => _logger = logger;

    /// <summary>
    /// 亮度倍数，应用于模型所有材质的 BaseColor RGB 通道。
    /// 1.0 = 原始亮度，2.0 = 两倍亮度，以此类推。不影响 Alpha 和纹理内容。
    /// </summary>
    public float brightnessMultiplier = 1.0f;

    static Shader _urpLit;
    static Shader GetUrpLit()
    {
        if (_urpLit == null) _urpLit = Shader.Find("Universal Render Pipeline/Lit");
        return _urpLit;
    }

    public UnityEngine.Material GetDefaultMaterial(bool pointsSupport = false)
        => new UnityEngine.Material(GetUrpLit() ?? Shader.Find("Standard"));

    public UnityEngine.Material GenerateMaterial(
        MaterialBase gltfMaterial,
        IGltfReadable gltf,
        bool pointsSupport = false)
    {
        var shader = GetUrpLit();
        if (shader == null)
        {
            Debug.LogError("[UrpLitMaterial] URP/Lit not found!");
            return GetDefaultMaterial();
        }

        var mat = new UnityEngine.Material(shader);
        if (gltfMaterial == null) return mat;
        mat.name = gltfMaterial.name ?? "glTF_mat";

        // 尝试将 MaterialBase 转换为具体子类 Material
        // glTFast 6.x 中具体属性（DoubleSided、AlphaCutoff 等）在 Material 子类中
        var concreteMaterial = gltfMaterial as GLTFast.Schema.Material;

        try
        {
            // ── PBR ────────────────────────────────────────────────────
            var pbr = gltfMaterial.PbrMetallicRoughness;
            if (pbr != null)
            {
                // Base color（应用亮度倍数到 RGB，保留原始 Alpha）
                var bc = pbr.BaseColor;
                float bm = brightnessMultiplier;
                var baseColor = new Color(bc.r * bm, bc.g * bm, bc.b * bm, bc.a);
                mat.SetColor("_BaseColor", baseColor);

                // Base color texture
                if (pbr.BaseColorTexture != null)
                    SetTex(mat, "_BaseMap", gltf, pbr.BaseColorTexture.index);

                // Metallic / roughness - 使用字段名（小写）
                var concretePbr = pbr as GLTFast.Schema.PbrMetallicRoughness;
                if (concretePbr != null)
                {
                    mat.SetFloat("_Metallic",   concretePbr.metallicFactor);
                    mat.SetFloat("_Smoothness", 1f - concretePbr.roughnessFactor);
                }
                else
                {
                    mat.SetFloat("_Metallic",   0f);
                    mat.SetFloat("_Smoothness", 0.5f);
                }

                // Metallic-roughness texture
                if (pbr.MetallicRoughnessTexture != null)
                {
                    if (SetTex(mat, "_MetallicGlossMap", gltf, pbr.MetallicRoughnessTexture.index))
                        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }

                // Transparency
                if (baseColor.a < 1f)
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
            }

            // ── Normal ─────────────────────────────────────────────────
            if (gltfMaterial.NormalTexture != null)
            {
                if (SetTex(mat, "_BumpMap", gltf, gltfMaterial.NormalTexture.index))
                {
                    mat.SetFloat("_BumpScale", gltfMaterial.NormalTexture.scale);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            // ── Occlusion ──────────────────────────────────────────────
            if (gltfMaterial.OcclusionTexture != null)
                SetTex(mat, "_OcclusionMap", gltf, gltfMaterial.OcclusionTexture.index);

            // ── Emissive ───────────────────────────────────────────────
            var em = gltfMaterial.Emissive;
            var emColor = new Color(em.r, em.g, em.b);
            if (emColor.r > 0f || emColor.g > 0f || emColor.b > 0f)
            {
                mat.SetColor("_EmissionColor", emColor);
                mat.EnableKeyword("_EMISSION");
            }
            if (gltfMaterial.EmissiveTexture != null)
            {
                if (SetTex(mat, "_EmissionMap", gltf, gltfMaterial.EmissiveTexture.index))
                    mat.EnableKeyword("_EMISSION");
            }

            // ── Alpha mode / Double sided (需要 concrete Material) ─────
            if (concreteMaterial != null)
            {
                // Alpha mode - alphaMode 是字段
                var alphaMode = gltfMaterial.GetAlphaMode();
                if (alphaMode == GLTFast.Schema.Material.AlphaMode.Blend)
                {
                    mat.SetFloat("_Surface", 1f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                else if (alphaMode == GLTFast.Schema.Material.AlphaMode.Mask)
                {
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = (int)RenderQueue.AlphaTest;
                    if (mat.HasProperty("_Cutoff"))
                        mat.SetFloat("_Cutoff", concreteMaterial.alphaCutoff);
                }

                // Double sided
                if (concreteMaterial.doubleSided)
                {
                    mat.SetFloat("_Cull", (float)CullMode.Off);
                    mat.EnableKeyword("_CULL_OFF");
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[UrpLitMaterial] '{mat.name}': {ex.Message}");
        }

        Debug.Log($"[UrpLitMaterial] Generated '{mat.name}' brightness={brightnessMultiplier:F2}");
        return mat;
    }

    static bool SetTex(UnityEngine.Material mat, string prop, IGltfReadable gltf, int index)
    {
        if (index < 0) return false;
        Texture2D tex = null;
        try { tex = gltf.GetTexture(index); } catch { }
        if (tex == null) return false;
        mat.SetTexture(prop, tex);
        return true;
    }
}
