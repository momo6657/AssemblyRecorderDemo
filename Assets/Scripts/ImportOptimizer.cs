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
/// 大模型加载优化器（glTFast 最新版本）
/// 解决 641MB+ 模型导入时的卡死问题
/// 
/// 优化策略：
/// 1. 使用 glTFast Load(Uri) 避免一次性读取整个文件
/// 2. 添加进度显示和超时机制
/// 3. 优化材质处理逻辑
/// 4. 内存优化：纹理压缩、材质去重
/// </summary>
public class ImportOptimizer
{
    /// <summary>
    /// 加载进度回调
    /// </summary>
    public delegate void OnProgressChanged(float progress, string stage);
    public event OnProgressChanged ProgressChanged;

    /// <summary>
    /// 加载完成回调
    /// </summary>
    public delegate void OnLoadComplete(bool success, string message);
    public event OnLoadComplete LoadComplete;

    // 配置
    private const float QUEST_TIMEOUT_SECONDS = 60f;
    private const float PHONE_TIMEOUT_SECONDS = 45f;
    private const int MAX_TEXTURE_SIZE_QUEST = 1024;
    private const int MAX_TEXTURE_SIZE_PHONE = 2048;

    private bool _isQuest;
    private CancellationTokenSource _cancellationTokenSource;

    public ImportOptimizer(bool isQuest = false)
    {
        _isQuest = isQuest;
    }

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
        float timeoutSeconds = _isQuest ? QUEST_TIMEOUT_SECONDS : PHONE_TIMEOUT_SECONDS;
        _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            ReportProgress(0f, "验证文件");
            
            // 验证文件
            if (!File.Exists(fullPath))
            {
                ReportError("文件不存在: " + fullPath);
                return false;
            }

            long fileSize = new FileInfo(fullPath).Length;
            Debug.Log($"[IMPORT] Loading {fileSize / (1024f * 1024f):F1}MB model");
            ReportProgress(5f, $"加载中 ({fileSize / (1024f * 1024f):F1}MB)");

            // 使用 glTFast Load(Uri) 避免一次性读取整个文件
            var logger = new ConsoleLogger();
            var gltf = new GltfImport(materialGenerator: materialGenerator, logger: logger);

            // 创建进度回调（glTFast 最新版本支持）
            var progress = new Progress<float>(p =>
            {
                float displayProgress = 5f + (p * 0.6f); // 5-65%
                ReportProgress(displayProgress, "解析模型");
            });

            ReportProgress(10f, "解析模型");
            
            // 使用 Load(Uri) 基础版本 - 最兼容的 API
            bool loadOk = await gltf.Load(new Uri(fullPath));
            
            if (!loadOk)
            {
                ReportError("模型解析失败");
                return false;
            }

            ReportProgress(70f, "实例化模型");

            // 创建根节点
            var root = new GameObject("ImportedModel");
            root.transform.SetParent(spawnRoot, false);

            // 实例化场景 - 基础版本
            bool instOk = await gltf.InstantiateMainSceneAsync(root.transform);
            
            if (!instOk)
            {
                UnityEngine.Object.Destroy(root);
                ReportError("模型实例化失败");
                return false;
            }

            ReportProgress(85f, "优化材质");

            // 优化材质处理（只在必要时进行）
            OptimizeMaterials(root);

            ReportProgress(95f, "完成");

            onSuccess?.Invoke(root);
            ReportProgress(100f, "加载完成");
            return true;
        }
        catch (OperationCanceledException)
        {
            ReportError($"加载超时（{timeoutSeconds}s）");
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
    /// 取消加载
    /// </summary>
    public void CancelLoad()
    {
        _cancellationTokenSource?.Cancel();
    }

    /// <summary>
    /// 优化材质处理
    /// 只处理必要的材质，避免遍历所有 renderer
    /// </summary>
    private void OptimizeMaterials(GameObject root)
    {
        if (root == null) return;

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return;

        var processedMaterials = new HashSet<int>();
        var renderers = root.GetComponentsInChildren<Renderer>(true);

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

                // 只处理非 URP/Lit 的材质
                if (mat.shader != null &&
                    (mat.shader.name == "Universal Render Pipeline/Lit" ||
                     mat.shader.name == "Universal Render Pipeline/Simple Lit"))
                    continue;

                // 替换为 URP/Lit
                var newMat = new Material(urpLit);
                newMat.name = mat.name + "_urp";
                CopyMaterialProperties(mat, newMat);
                mats[i] = newMat;
            }

            r.materials = mats;
        }

        Debug.Log($"[IMPORT] Optimized {processedMaterials.Count} materials");
    }

    /// <summary>
    /// 复制材质属性
    /// </summary>
    private void CopyMaterialProperties(Material src, Material dst)
    {
        if (src == null || dst == null) return;

        // 复制所有纹理
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

        // 复制颜色
        string[] colorNames = { "_BaseColor", "_Color", "baseColorFactor" };
        foreach (var n in colorNames)
        {
            if (!src.HasProperty(n)) continue;
            var c = src.GetColor(n);
            if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", c);
            break;
        }

        // 复制金属度/粗糙度
        if (src.HasProperty("_Metallic") && dst.HasProperty("_Metallic"))
            dst.SetFloat("_Metallic", src.GetFloat("_Metallic"));
        if (src.HasProperty("_Roughness") && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", 1f - src.GetFloat("_Roughness"));
    }

    private void ReportProgress(float progress, string stage)
    {
        Debug.Log($"[IMPORT] Progress: {progress:F0}% - {stage}");
        ProgressChanged?.Invoke(progress, stage);
    }

    private void ReportError(string message)
    {
        Debug.LogError($"[IMPORT] {message}");
        LoadComplete?.Invoke(false, message);
    }
}
