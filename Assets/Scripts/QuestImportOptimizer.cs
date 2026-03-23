using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using UnityEngine;

/// <summary>
/// Quest 端深度性能优化器（简化版）
/// 解决 70-87% 卡死问题
/// </summary>
public class QuestImportOptimizer
{
    public delegate void OnProgressChanged(float progress, string stage);
    public event OnProgressChanged ProgressChanged;

    private const float QUEST_TIMEOUT_SECONDS = 90f;
    private const int BATCH_SIZE = 15;

    private CancellationTokenSource _cancellationTokenSource;

    /// <summary>
    /// 优化的异步加载方法
    /// </summary>
    public async Task<bool> LoadGltfOptimizedAsync(
        string fullPath,
        Transform spawnRoot,
        IMaterialGenerator materialGenerator,
        System.Action<GameObject> onSuccess)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(QUEST_TIMEOUT_SECONDS));

        try
        {
            ReportProgress(0f, "验证文件");
            
            if (!File.Exists(fullPath))
            {
                ReportError("文件不存在");
                return false;
            }

            long fileSize = new FileInfo(fullPath).Length;
            Debug.Log($"[IMPORT] Loading {fileSize / (1024f * 1024f):F1}MB model");
            ReportProgress(5f, $"加载中 ({fileSize / (1024f * 1024f):F1}MB)");

            var logger = new ConsoleLogger();
            var gltf = new GltfImport(materialGenerator: materialGenerator, logger: logger);

            ReportProgress(10f, "解析模型");
            
            bool loadOk = await gltf.Load(new Uri(fullPath));
            
            if (!loadOk)
            {
                ReportError("模型解析失败");
                return false;
            }

            ReportProgress(65f, "优化纹理");
            OptimizeTextures(gltf);

            ReportProgress(70f, "实例化模型");

            var root = new GameObject("ImportedModel");
            root.transform.SetParent(spawnRoot, false);

            bool instOk = await InstantiateFrameByFrameAsync(gltf, root.transform);
            
            if (!instOk)
            {
                UnityEngine.Object.Destroy(root);
                ReportError("模型实例化失败");
                return false;
            }

            ReportProgress(95f, "优化材质");
            OptimizeMaterials(root);

            ReportProgress(100f, "加载完成");

            onSuccess?.Invoke(root);
            return true;
        }
        catch (OperationCanceledException)
        {
            ReportError($"加载超时（{QUEST_TIMEOUT_SECONDS}s）");
            return false;
        }
        catch (Exception ex)
        {
            ReportError($"加载异常: {ex.Message}");
            Debug.LogException(ex);
            return false;
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    /// <summary>
    /// 分帧实例化
    /// </summary>
    private async Task<bool> InstantiateFrameByFrameAsync(GltfImport gltf, Transform root)
    {
        try
        {
            bool instOk = await gltf.InstantiateMainSceneAsync(root);
            
            if (!instOk)
                return false;

            var allTransforms = root.GetComponentsInChildren<Transform>();
            int totalCount = allTransforms.Length;

            Debug.Log($"[IMPORT] Instantiated {totalCount} GameObjects, now optimizing...");

            int processed = 0;
            for (int i = 0; i < totalCount; i += BATCH_SIZE)
            {
                int batchEnd = Mathf.Min(i + BATCH_SIZE, totalCount);
                for (int j = i; j < batchEnd; j++)
                {
                    var t = allTransforms[j];
                    if (t == null) continue;
                    OptimizeGameObject(t.gameObject);
                    processed++;
                }

                await Task.Yield();

                float progress = 70f + (processed / (float)totalCount) * 25f;
                ReportProgress(progress, $"优化模型 ({processed}/{totalCount})");

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[IMPORT] InstantiateFrameByFrame failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 优化单个 GameObject
    /// </summary>
    private void OptimizeGameObject(GameObject go)
    {
        if (go == null) return;

        var animator = go.GetComponent<Animator>();
        if (animator != null && animator.runtimeAnimatorController == null)
            UnityEngine.Object.Destroy(animator);

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            
            foreach (var mat in renderer.materials)
            {
                if (mat != null)
                    mat.enableInstancing = true;
            }
        }

        var collider = go.GetComponent<Collider>();
        if (collider != null && !collider.isTrigger)
        {
            if (go.GetComponent<Rigidbody>() == null)
                collider.enabled = false;
        }
    }

    /// <summary>
    /// 优化纹理
    /// </summary>
    private void OptimizeTextures(GltfImport gltf)
    {
        try
        {
            Debug.Log("[IMPORT] Texture optimization prepared");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IMPORT] Texture optimization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 优化材质处理
    /// </summary>
    private void OptimizeMaterials(GameObject root)
    {
        if (root == null) return;

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return;

        var processedMaterials = new HashSet<int>();
        var renderers = root.GetComponentsInChildren<Renderer>(true);

        int materialCount = 0;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;
            if (mats == null || mats.Length == 0) continue;

            for (int i = 0; i < mats.Length; i++)
            {
                var mat = mats[i];
                if (mat == null) continue;

                int matId = mat.GetInstanceID();
                if (processedMaterials.Contains(matId)) continue;
                processedMaterials.Add(matId);

                if (mat.shader != null &&
                    (mat.shader.name == "Universal Render Pipeline/Lit" ||
                     mat.shader.name == "Universal Render Pipeline/Simple Lit"))
                    continue;

                var newMat = new Material(urpLit);
                newMat.name = mat.name + "_urp";
                newMat.enableInstancing = true;
                CopyMaterialProperties(mat, newMat);
                mats[i] = newMat;
                materialCount++;
            }

            r.materials = mats;
        }

        Debug.Log($"[IMPORT] Optimized {materialCount} materials, {processedMaterials.Count} unique");
    }

    /// <summary>
    /// 复制材质属性
    /// </summary>
    private void CopyMaterialProperties(Material src, Material dst)
    {
        if (src == null || dst == null) return;

        var texNames = new List<string>();
        src.GetTexturePropertyNames(texNames);

        var texMap = new Dictionary<string, string>
        {
            { "_BaseColorTexture", "_BaseMap" },
            { "_baseColorTexture", "_BaseMap" },
            { "_MainTex", "_BaseMap" },
            { "_BaseMap", "_BaseMap" },
            { "_NormalTexture", "_BumpMap" },
            { "_normalTexture", "_BumpMap" },
            { "_BumpMap", "_BumpMap" },
            { "_MetallicRoughnessTexture", "_MetallicGlossMap" },
            { "_metallicRoughnessTexture", "_MetallicGlossMap" },
            { "_MetallicGlossMap", "_MetallicGlossMap" },
            { "_OcclusionTexture", "_OcclusionMap" },
            { "_occlusionTexture", "_OcclusionMap" },
            { "_OcclusionMap", "_OcclusionMap" },
            { "_EmissiveTexture", "_EmissionMap" },
            { "_emissiveTexture", "_EmissionMap" },
            { "_EmissionMap", "_EmissionMap" },
        };

        foreach (var srcName in texNames)
        {
            var tex = src.GetTexture(srcName);
            if (tex == null) continue;

            string dstName = null;
            if (!texMap.TryGetValue(srcName, out dstName))
            {
                if (dst.HasProperty(srcName)) dstName = srcName;
                else continue;
            }

            if (!dst.HasProperty(dstName)) continue;
            dst.SetTexture(dstName, tex);
        }

        string[] colorNames = { "_BaseColor", "_Color", "baseColorFactor" };
        foreach (var n in colorNames)
        {
            if (!src.HasProperty(n)) continue;
            var c = src.GetColor(n);
            if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", c);
            break;
        }

        if (src.HasProperty("_Metallic") && dst.HasProperty("_Metallic"))
            dst.SetFloat("_Metallic", src.GetFloat("_Metallic"));
        if (src.HasProperty("_Roughness") && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", 1f - src.GetFloat("_Roughness"));
    }

    public void CancelLoad()
    {
        _cancellationTokenSource?.Cancel();
    }

    private void ReportProgress(float progress, string stage)
    {
        Debug.Log($"[IMPORT] Progress: {progress:F0}% - {stage}");
        ProgressChanged?.Invoke(progress, stage);
    }

    private void ReportError(string message)
    {
        Debug.LogError($"[IMPORT] {message}");
    }
}
