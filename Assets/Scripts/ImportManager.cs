using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using GLTFast.Materials;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

public class ImportManager : MonoBehaviour
{
    [Header("Optional UI")]
    public TMP_Text statusText;

    [Header("Spawn Settings")]
    public Transform spawnRoot;
    public bool destroyPrevious = true;
    public bool resetSpawnRootTransform = true;

    [Header("Optional refs (auto-find if null)")]
    public OrbitPinchCamera orbitPinchCamera;
    public SelectionManager selectionManager;
    public ModelIndex modelIndex;

    [Header("Legacy UI Compatibility")]
    public string persistentFileName = "";
    public string modelId = "";

    [Header("Camera Framing")]
    public bool autoFocusModel = true;
    public bool autoFrameMainCamera = true;
    public bool skipAutoFrameWhenXrActive = true;

    [Header("XR Placement")]
    public bool placeInFrontOfXrCamera = true;
    public float xrPlaceDistance = 1.2f;
    public float xrPlaceVerticalOffset = -0.1f;
    public float xrTargetMaxSize = 0.5f;
    public float xrAbnormalCameraYThreshold = 20f;
    public bool xrAnchorToCameraParentWhenPoseAbnormal = true;

    [Header("XR Visibility Debug")]
    public bool forceVisibleDebugMaterialWhenCameraAbnormal = false;
    public Color debugVisibleColor = new Color(0.1f, 1f, 0.1f, 1f);
    public bool spawnDebugPlacementMarker = false;
    public float debugPlacementMarkerSize = 0.08f;
    public bool xrAttachToCameraWhenAbnormal = false;
    public float xrAttachDurationSec = 8f;

    [Header("Material Fixup")]
    public bool preferGltfUrpMaterialGenerator = true;
    public bool remapImportedMaterialsToUrpLit = true;
    public bool remapOnlyWhenUnsupported = false;

    Material _debugVisibleMat;
    Coroutine _attachRoutine;

    GameObject _currentModel;
    bool _isLoading;

    public GameObject CurrentModel => _currentModel;
    public bool IsLoading => _isLoading;

    void Awake()
    {
        if (spawnRoot == null) spawnRoot = transform;
        LogStatus("[IMPORT] Ready");
        Debug.Log("[IMPORT] XR flags => placeInFront=" + placeInFrontOfXrCamera
                  + ", anchorToCameraParentWhenAbnormal=" + xrAnchorToCameraParentWhenPoseAbnormal
                  + ", forceVisibleDebugWhenAbnormal=" + forceVisibleDebugMaterialWhenCameraAbnormal
                  + ", spawnDebugMarker=" + spawnDebugPlacementMarker
                  + ", attachToCameraWhenAbnormal=" + xrAttachToCameraWhenAbnormal);
    }

    public void LoadGlbFromPath(string fullPath)
    {
        _ = LoadGlbFromPathAsync(fullPath);
    }

    public void LoadGlbFromPersistentFileName(string fileName)
    {
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);
        _ = LoadGlbFromPathAsync(fullPath);
    }

    // Legacy button compatibility: load by the cached persistent file name.
    public void LoadFromPersistent()
    {
        if (string.IsNullOrWhiteSpace(persistentFileName))
        {
            LogStatus("[IMPORT] persistentFileName is empty.");
            return;
        }

        LoadGlbFromPersistentFileName(persistentFileName);
    }

    // Legacy input compatibility.
    public void SetFileName(string fileName)
    {
        persistentFileName = fileName != null ? fileName.Trim() : "";
        Debug.Log("[IMPORT] persistentFileName set => " + persistentFileName);
    }

    // Legacy input compatibility.
    public void SetModelId(string id)
    {
        modelId = id != null ? id.Trim() : "";
        Debug.Log("[IMPORT] modelId set => " + modelId);
    }

    public async Task<bool> LoadGlbFromPathAsync(string fullPath)
    {
        if (_isLoading)
        {
            LogStatus("[IMPORT] Busy, skip duplicate load request.");
            return false;
        }

        _isLoading = true;
        try
        {
            // 检测平台
            bool isQuest = Application.platform == RuntimePlatform.Android && 
                          UnityEngine.XR.XRSettings.enabled;
            
            var previousModel = _currentModel;
            bool success = false;

            if (isQuest)
            {
                // Quest 端使用深度优化加载器
                Debug.Log("[IMPORT] Using QuestImportOptimizer for Quest platform");
                var questOptimizer = new QuestImportOptimizer();
                questOptimizer.ProgressChanged += (progress, stage) =>
                {
                    LogStatus($"[IMPORT] {stage} ({progress:F0}%)");
                };

                success = await questOptimizer.LoadGltfOptimizedAsync(
                    fullPath,
                    spawnRoot,
                    CreateRuntimeMaterialGenerator(),
                    (root) =>
                    {
                        ProcessLoadedModel(root, previousModel);
                    }
                );
            }
            else
            {
                // 手机端使用标准优化加载器
                Debug.Log("[IMPORT] Using ImportOptimizer for phone platform");
                var optimizer = new ImportOptimizer(false);
                optimizer.ProgressChanged += (progress, stage) =>
                {
                    LogStatus($"[IMPORT] {stage} ({progress:F0}%)");
                };

                success = await optimizer.LoadGltfOptimizedAsync(
                    fullPath,
                    spawnRoot,
                    CreateRuntimeMaterialGenerator(),
                    (root) =>
                    {
                        ProcessLoadedModel(root, previousModel);
                    }
                );
            }

            return success;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            LogStatus("[IMPORT][EXCEPTION] " + ex.Message);
            return false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 处理加载完成的模型（共享逻辑）
    /// 分帧执行后处理，避免卡顿
    /// </summary>
    private void ProcessLoadedModel(GameObject root, GameObject previousModel)
    {
        if (destroyPrevious && previousModel != null && previousModel != root)
        {
            Debug.Log("[IMPORT] replacing previous model => " + previousModel.name);
            Destroy(previousModel);
        }

        _currentModel = root;
        CleanupDuplicateImportedModels(_currentModel);
        LogStatus("[IMPORT] Success");

        // 分帧执行后处理，避免卡顿
        StartCoroutine(ProcessLoadedModelCoroutine(root));
    }

    /// <summary>
    /// 异步处理加载完成的模型（分帧执行）
    /// </summary>
    private System.Collections.IEnumerator ProcessLoadedModelCoroutine(GameObject root)
    {
        // 第一帧：XR 相关处理
        Debug.Log("[IMPORT] Starting post-processing...");
        TryPlaceInFrontOfXrCamera(_currentModel);
        yield return null;

        // 第二帧起：分帧应用 XR Grab（XRGrabSetup.ApplyAsync 内部每个 part 都会 yield）
        Debug.Log("[IMPORT] Applying XR Grab (async)...");
        var setup = FindFirstObjectByType<XRGrabSetup>();
        if (setup != null)
        {
            setup.modelRoot = root.transform;
            yield return setup.ApplyAsync();
            Debug.Log("[IMPORT] XR Grab applied");

            // 确保有 collider
            bool hasCollider = root.GetComponentInChildren<Collider>(true) != null;
            if (!hasCollider)
            {
                Debug.Log("[IMPORT] No colliders after XRGrab, ensuring colliders...");
                EnsureColliders(_currentModel);
            }

            // 注册到 ScaleProtector
            var scaleProtector = FindFirstObjectByType<ScaleProtector>();
            if (scaleProtector == null)
            {
                var protectorGo = new GameObject("ScaleProtector");
                scaleProtector = protectorGo.AddComponent<ScaleProtector>();
                Debug.Log("[IMPORT] Created ScaleProtector");
            }
            if (scaleProtector != null)
            {
                var grabs = root.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(true);
                foreach (var g in grabs)
                    if (g != null) scaleProtector.RegisterTransform(g.transform);
                Debug.Log($"[IMPORT] Registered {grabs.Length} grab interactables to ScaleProtector");
            }
        }
        else
        {
            Debug.Log("[IMPORT] XRGrabSetup not found, ensuring colliders...");
            EnsureColliders(_currentModel);
        }
        yield return null;

        // 模型索引（分帧执行）
        Debug.Log("[IMPORT] Rebuilding model index...");
        yield return RebuildModelIndexAsync(root.transform);

        // 选择管理
        Debug.Log("[IMPORT] Binding selection...");
        BindSelection(root.transform);
        yield return null;

        // 轨道相机
        Debug.Log("[IMPORT] Binding orbit...");
        BindOrbit(root.transform);
        yield return null;

        // 相机聚焦
        Debug.Log("[IMPORT] Framing camera...");
        TryFocusAndFrame(root);
        
        Debug.Log("[IMPORT] Post-processing complete");
    }

    /// <summary>
    /// 异步重建模型索引（分帧执行）
    /// </summary>
    private System.Collections.IEnumerator RebuildModelIndexAsync(Transform root)
    {
        Debug.Log("[IMPORT] Rebuilding model index...");
        RebuildModelIndex(root);
        yield return null;
    }

    void RebuildModelIndex(Transform root)
    {
        if (modelIndex == null) modelIndex = FindFirstObjectByType<ModelIndex>();
        if (modelIndex == null)
        {
            Debug.LogWarning("[IMPORT] ModelIndex not found in scene");
            return;
        }

        modelIndex.SetModelRoot(root, rebuild: true);
        Debug.Log("[IMPORT] ModelIndex map count=" + (modelIndex.map != null ? modelIndex.map.Count : 0));
    }

    void BindSelection(Transform root)
    {
        if (selectionManager == null) selectionManager = FindFirstObjectByType<SelectionManager>();
        if (selectionManager == null)
        {
            Debug.Log("[IMPORT] SelectionManager not found, skip bind.");
            return;
        }

        selectionManager.BindModel(root);
        Debug.Log("[IMPORT] SelectionManager bound");
    }

    void BindOrbit(Transform root)
    {
        if (orbitPinchCamera == null) orbitPinchCamera = FindFirstObjectByType<OrbitPinchCamera>();
        if (orbitPinchCamera == null)
        {
            Debug.Log("[IMPORT] OrbitPinchCamera not found, skip bind.");
            return;
        }

        orbitPinchCamera.target = root;
        orbitPinchCamera.ForceUpdateOnce();
        Debug.Log("[IMPORT] OrbitPinchCamera target bound");
    }

    void TryFocusAndFrame(GameObject modelRoot)
    {
        if (modelRoot == null) return;
        if (skipAutoFrameWhenXrActive && IsXrActive())
        {
            Debug.Log("[IMPORT] XR active, skipped camera framing.");
            return;
        }

        if (autoFocusModel) FocusModelRough(modelRoot);
        if (autoFrameMainCamera) FrameCameraToModel(modelRoot);
    }

    void TryPlaceInFrontOfXrCamera(GameObject modelRoot)
    {
        if (!placeInFrontOfXrCamera || modelRoot == null) return;
        if (!IsXrActive()) return;

        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[IMPORT] XR place skipped: Camera.main is null");
            return;
        }

        if (!TryGetBounds(modelRoot, out var bounds))
        {
            Debug.LogWarning("[IMPORT] XR place skipped: no renderer bounds");
            return;
        }

        float maxSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (maxSize > 0.0001f && xrTargetMaxSize > 0.0001f)
        {
            float s = xrTargetMaxSize / maxSize;
            modelRoot.transform.localScale *= s;
            if (!TryGetBounds(modelRoot, out bounds))
            {
                Debug.LogWarning("[IMPORT] XR place: bounds invalid after rescale");
                return;
            }
        }

        Vector3 rawCamPos = cam.transform.position;
        float abnormalThreshold = xrAbnormalCameraYThreshold > 0.01f ? xrAbnormalCameraYThreshold : 20f;
        bool cameraYAbnormal = Mathf.Abs(rawCamPos.y) > abnormalThreshold;
        Vector3 camForward = cam.transform.forward.sqrMagnitude > 0.0001f ? cam.transform.forward.normalized : Vector3.forward;
        Vector3 camUp = cam.transform.up.sqrMagnitude > 0.0001f ? cam.transform.up.normalized : Vector3.up;

        if (cameraYAbnormal && xrAnchorToCameraParentWhenPoseAbnormal && cam.transform.parent != null)
        {
            if (modelRoot.transform.parent != cam.transform.parent)
            {
                modelRoot.transform.SetParent(cam.transform.parent, true);
                Debug.LogWarning("[IMPORT] XR abnormal pose: model reparented to camera parent to survive origin recenter.");
            }
        }
        else if (cameraYAbnormal && xrAnchorToCameraParentWhenPoseAbnormal && cam.transform.parent == null)
        {
            Debug.LogWarning("[IMPORT] XR abnormal pose: camera parent is null, skip reparent fallback.");
        }

        // Always place by current headset forward/up to guarantee in-view spawn in XR.
        Vector3 targetCenter = rawCamPos + camForward * xrPlaceDistance + camUp * xrPlaceVerticalOffset;
        if (cameraYAbnormal)
            Debug.LogWarning("[IMPORT] XR camera y looks abnormal, force headset-forward placement. rawCamPos=" + rawCamPos + " targetCenter=" + targetCenter);

        Vector3 delta = targetCenter - bounds.center;
        modelRoot.transform.position += delta;

        // Safety correction: if still behind the camera, push to front immediately.
        if (TryGetBounds(modelRoot, out var placedBounds))
        {
            Vector3 toModel = placedBounds.center - rawCamPos;
            float distToModel = toModel.magnitude;
            float frontDot = distToModel > 0.0001f ? Vector3.Dot(camForward, toModel / distToModel) : -1f;
            if (frontDot < 0.05f)
            {
                Vector3 safeCenter = rawCamPos + camForward * Mathf.Max(0.8f, xrPlaceDistance) + camUp * xrPlaceVerticalOffset;
                modelRoot.transform.position += (safeCenter - placedBounds.center);
                Debug.LogWarning("[IMPORT] XR placement corrected: model was outside front view cone. frontDot=" + frontDot);
            }

            Debug.Log("[IMPORT] XR placement check: dist=" + distToModel + " frontDot=" + frontDot);
        }

        bool allowRuntimeDebugVisual = Application.isEditor;
        if (cameraYAbnormal && forceVisibleDebugMaterialWhenCameraAbnormal && allowRuntimeDebugVisual)
            ForceVisibleDebugMaterial(modelRoot);
        if (spawnDebugPlacementMarker && allowRuntimeDebugVisual)
            SpawnDebugPlacementMarker(targetCenter);
        if (cameraYAbnormal && xrAttachToCameraWhenAbnormal && allowRuntimeDebugVisual)
            StartAttachToCameraRoutine(modelRoot.transform, cam.transform);

        Debug.Log("[IMPORT] XR placed model. rawCamPos=" + rawCamPos + " center=" + targetCenter + " size=" + bounds.size + " rootPos=" + modelRoot.transform.position);
    }

    bool TryApplyXrGrab(Transform root)
    {
        var setup = FindFirstObjectByType<XRGrabSetup>();
        if (setup == null) return false;
        setup.modelRoot = root;
        
        // 修复：在应用 XR 抓取设置前，确保 ModelIndex 已经构建
        // 这样可以正确记录初始缩放状态，避免抓取时的缩放问题
        if (modelIndex != null && modelIndex.modelRoot == root)
        {
            Debug.Log("[IMPORT] ModelIndex already built before XRGrabSetup, scale baseline captured correctly.");
        }
        
        setup.Apply();
        bool hasCollider = root != null && root.GetComponentInChildren<Collider>(true) != null;
        if (!hasCollider)
            Debug.LogWarning("[IMPORT] XRGrabSetup applied but no colliders found; fallback collider pass will run.");
        
        // 修复：确保场景中有 ScaleProtector 组件
        var scaleProtector = FindFirstObjectByType<ScaleProtector>();
        if (scaleProtector == null)
        {
            var protectorGo = new GameObject("ScaleProtector");
            scaleProtector = protectorGo.AddComponent<ScaleProtector>();
            Debug.Log("[IMPORT] Created ScaleProtector to monitor and prevent scale changes");
        }
        
        // 注册所有抓取物体到 ScaleProtector
        if (scaleProtector != null && root != null)
        {
            var grabInteractables = root.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(true);
            foreach (var interactable in grabInteractables)
            {
                if (interactable != null)
                {
                    scaleProtector.RegisterTransform(interactable.transform);
                }
            }
            Debug.Log($"[IMPORT] Registered {grabInteractables.Length} grab interactables to ScaleProtector");
        }
        
        return hasCollider;
    }

    bool IsXrActive()
    {
#if UNITY_2019_3_OR_NEWER
        return UnityEngine.XR.XRSettings.enabled && UnityEngine.XR.XRSettings.isDeviceActive;
#else
        return false;
#endif
    }

    public void DestroyCurrentModel()
    {
        if (_currentModel == null) return;
        Debug.Log("[IMPORT] DestroyCurrentModel => " + _currentModel.name);
        Destroy(_currentModel);
        _currentModel = null;
    }

    void CleanupDuplicateImportedModels(GameObject keep)
    {
        if (spawnRoot == null) return;

        int removed = 0;
        var toRemove = new List<GameObject>();
        for (int i = 0; i < spawnRoot.childCount; i++)
        {
            var child = spawnRoot.GetChild(i);
            if (child == null) continue;
            if (child.gameObject == keep) continue;
            if (!string.Equals(child.name, "ImportedModel", StringComparison.Ordinal)) continue;
            toRemove.Add(child.gameObject);
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            Destroy(toRemove[i]);
            removed++;
        }

        if (removed > 0)
            Debug.Log("[IMPORT] Removed duplicate ImportedModel count=" + removed);
    }

    void EnsureColliders(GameObject modelRoot)
    {
        if (modelRoot == null) return;

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var go = r.gameObject;
            if (go.GetComponent<Collider>() == null)
                go.AddComponent<BoxCollider>();
        }

        Debug.Log("[IMPORT] EnsureColliders done, renderers=" + renderers.Length);
    }

    void RemapImportedMaterials(GameObject modelRoot)
    {
        if (modelRoot == null) return;

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogWarning("[IMPORT] URP/Lit shader not found, skip remap.");
            return;
        }

        int total = 0, replaced = 0, ok = 0;
        var cache = new Dictionary<int, Material>();
        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);

        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;  // 用 materials 而不是 sharedMaterials 获取实例化材质
            if (mats == null || mats.Length == 0)
            {
                Debug.LogWarning($"[IMPORT] Renderer '{r.gameObject.name}' has no materials");
                continue;
            }
            Debug.Log($"[IMPORT] Renderer '{r.gameObject.name}' has {mats.Length} materials");

            bool changed = false;
            var newMats = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                newMats[i] = mats[i];
                total++;
                var src = mats[i];
                
                // 记录所有材质的 shader 名，用于诊断
                if (src == null)
                {
                    Debug.LogWarning($"[IMPORT] Material[{i}] is NULL on renderer '{r.gameObject.name}'");
                    continue;
                }

                Debug.Log($"[IMPORT] Material[{i}] name='{src.name}' shader='{src.shader?.name}' supported={src.shader?.isSupported}");

                // ✅ 只有已经是 URP/Lit 才跳过，其余全部替换
                if (src.shader != null &&
                    (src.shader.name == "Universal Render Pipeline/Lit" ||
                     src.shader.name == "Universal Render Pipeline/Simple Lit"))
                {
                    ok++;
                    continue;
                }

                int key = src.GetInstanceID();
                if (!cache.TryGetValue(key, out var dst))
                {
                    dst = new Material(urpLit);
                    dst.name = src.name + "_urp";
                    CopyAllTexturesAndProperties(src, dst);
                    cache[key] = dst;
                    Debug.Log($"[IMPORT] Remapped '{src.name}' shader='{src.shader?.name}' -> URP/Lit");
                }

                newMats[i] = dst;
                replaced++;
                changed = true;
            }

            if (changed)
                r.materials = newMats;
        }

        Debug.Log($"[IMPORT] Remap done. total={total} ok={ok} replaced={replaced}");
    }

    /// <summary>
    /// 从源材质中提取所有纹理和常用属性，复制到目标 URP/Lit 材质。
    /// 这是解决 glTFast ShaderGraph shader 缺失问题的核心方法。
    /// </summary>
    static void CopyAllTexturesAndProperties(Material src, Material dst)
    {
        if (src == null || dst == null) return;

        // ── 提取所有纹理 ──────────────────────────────────────────────
        // glTFast 即使用了错误的 shader，纹理属性仍然被正确绑定
        // 我们枚举所有纹理属性名并尝试读取
        var texNames = new List<string>();
        src.GetTexturePropertyNames(texNames);

        // 纹理属性名映射：glTFast shader 属性名 → URP/Lit 属性名
        var texMap = new Dictionary<string, string>
        {
            { "_BaseColorTexture",         "_BaseMap" },
            { "_baseColorTexture",         "_BaseMap" },
            { "baseColorTexture",          "_BaseMap" },
            { "_MainTex",                  "_BaseMap" },
            { "_BaseMap",                  "_BaseMap" },
            { "_NormalTexture",            "_BumpMap" },
            { "_normalTexture",            "_BumpMap" },
            { "normalTexture",             "_BumpMap" },
            { "_BumpMap",                  "_BumpMap" },
            { "_MetallicRoughnessTexture", "_MetallicGlossMap" },
            { "_metallicRoughnessTexture", "_MetallicGlossMap" },
            { "metallicRoughnessTexture",  "_MetallicGlossMap" },
            { "_MetallicGlossMap",         "_MetallicGlossMap" },
            { "_OcclusionTexture",         "_OcclusionMap" },
            { "_occlusionTexture",         "_OcclusionMap" },
            { "occlusionTexture",          "_OcclusionMap" },
            { "_OcclusionMap",             "_OcclusionMap" },
            { "_EmissiveTexture",          "_EmissionMap" },
            { "_emissiveTexture",          "_EmissionMap" },
            { "emissiveTexture",           "_EmissionMap" },
            { "_EmissionMap",              "_EmissionMap" },
        };

        bool hasNormal = false, hasMetallic = false, hasEmission = false;

        foreach (var srcName in texNames)
        {
            var tex = src.GetTexture(srcName);
            if (tex == null) continue;

            string dstName = null;
            texMap.TryGetValue(srcName, out dstName);

            // 如果没有映射，尝试直接用相同名字
            if (dstName == null)
            {
                if (dst.HasProperty(srcName)) dstName = srcName;
                else continue;
            }

            if (!dst.HasProperty(dstName)) continue;
            dst.SetTexture(dstName, tex);
            Debug.Log($"[IMPORT] CopyTexture '{src.name}': {srcName} -> {dstName} ({tex.name})");

            if (dstName == "_BumpMap") hasNormal = true;
            if (dstName == "_MetallicGlossMap") hasMetallic = true;
            if (dstName == "_EmissionMap") hasEmission = true;
        }

        // Enable keywords based on assigned textures
        if (hasNormal) dst.EnableKeyword("_NORMALMAP");
        if (hasMetallic) dst.EnableKeyword("_METALLICSPECGLOSSMAP");
        if (hasEmission) dst.EnableKeyword("_EMISSION");

        // ── 颜色属性 ──────────────────────────────────────────────────
        string[] baseColorNames = { "_BaseColor", "_Color", "baseColorFactor", "_BaseColorFactor" };
        foreach (var n in baseColorNames)
        {
            if (!src.HasProperty(n)) continue;
            var c = src.GetColor(n);
            if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", c);
            if (c.a < 1f)
            {
                dst.SetFloat("_Surface", 1f);
                dst.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                dst.SetOverrideTag("RenderType", "Transparent");
                dst.renderQueue = (int)RenderQueue.Transparent;
            }
            break;
        }

        // ── Metallic / Smoothness ─────────────────────────────────────
        if (src.HasProperty("_Metallic") && dst.HasProperty("_Metallic"))
            dst.SetFloat("_Metallic", src.GetFloat("_Metallic"));
        if (src.HasProperty("_Roughness") && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", 1f - src.GetFloat("_Roughness"));
        else if (src.HasProperty("_Smoothness") && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
        else if (src.HasProperty("roughnessFactor") && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", 1f - src.GetFloat("roughnessFactor"));

        // ── Emission color ─────────────────────────────────────────────
        string[] emissionColorNames = { "_EmissionColor", "emissiveFactor", "_EmissiveFactor" };
        foreach (var n in emissionColorNames)
        {
            if (!src.HasProperty(n)) continue;
            var c = src.GetColor(n);
            if (c != Color.black)
            {
                dst.SetColor("_EmissionColor", c);
                dst.EnableKeyword("_EMISSION");
            }
            break;
        }
    }

    IMaterialGenerator CreateRuntimeMaterialGenerator()
    {
        // ✅ 使用自定义 UrpLitMaterialGenerator
        // glTFast 默认的 ShaderGraphMaterialGenerator 需要 Shader Graphs/glTF-pbrMetallicRoughness
        // 该 shader 在 Android 构建中不存在，导致所有材质变成 InternalErrorShader（白色/紫色）
        // UrpLitMaterialGenerator 直接使用 URP/Lit 并正确读取纹理
        Debug.Log("[IMPORT] Using UrpLitMaterialGenerator");
        return new UrpLitMaterialGenerator();
    }

    static bool IsUnsupportedShader(Shader shader)
    {
        if (shader == null) return true;
        if (!shader.isSupported) return true;
        return string.Equals(shader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal);
    }

    static Material CreateFallbackMaterial(Material src, Shader targetShader)
    {
        var dst = new Material(targetShader);
        dst.name = src.name + "_URP";

        // ✅ 复制基础颜色
        Color baseColor = Color.white;
        if (TryReadColor(src, out var color)) baseColor = color;
        if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", baseColor);
        if (dst.HasProperty("_Color")) dst.SetColor("_Color", baseColor);

        // ✅ 复制 Albedo/BaseColor 纹理
        var baseTex = TryReadMainTexture(src);
        if (baseTex != null)
        {
            if (dst.HasProperty("_BaseMap")) dst.SetTexture("_BaseMap", baseTex);
            else if (dst.HasProperty("_MainTex")) dst.SetTexture("_MainTex", baseTex);
        }

        // ✅ 复制法线贴图
        Texture normalTex = TryReadTexture(src, new[] { "_BumpMap", "_NormalMap", "_NormalTexture", "_DetailNormalMap" });
        if (normalTex != null && dst.HasProperty("_BumpMap"))
        {
            dst.SetTexture("_BumpMap", normalTex);
            dst.EnableKeyword("_NORMALMAP");
            float bumpScale = TryReadFloat(src, "_BumpScale", out var bs) ? bs : 1f;
            if (dst.HasProperty("_BumpScale")) dst.SetFloat("_BumpScale", bumpScale);
        }

        // ✅ 复制金属度/粗糙度
        if (TryReadFloat(src, "_Metallic", out var metallic) && dst.HasProperty("_Metallic"))
            dst.SetFloat("_Metallic", metallic);
        
        // GLTFast 使用 _Roughness，URP 使用 _Smoothness (= 1 - roughness)
        if (TryReadFloat(src, "_Roughness", out var roughness) && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", 1f - roughness);
        else if (TryReadFloat(src, "_Glossiness", out var smoothness) && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", smoothness);

        // ✅ 复制金属度/粗糙度贴图
        Texture metallicTex = TryReadTexture(src, new[] { "_MetallicGlossMap", "_MetallicRoughnessMap", "_MetallicRoughnessTexture" });
        if (metallicTex != null && dst.HasProperty("_MetallicGlossMap"))
        {
            dst.SetTexture("_MetallicGlossMap", metallicTex);
            dst.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        // ✅ 复制自发光
        if (src.HasProperty("_EmissionColor") && dst.HasProperty("_EmissionColor"))
        {
            var emissionColor = src.GetColor("_EmissionColor");
            dst.SetColor("_EmissionColor", emissionColor);
            if (emissionColor != Color.black)
                dst.EnableKeyword("_EMISSION");
        }
        Texture emissionTex = TryReadTexture(src, new[] { "_EmissionMap", "_EmissiveTexture" });
        if (emissionTex != null && dst.HasProperty("_EmissionMap"))
        {
            dst.SetTexture("_EmissionMap", emissionTex);
            dst.EnableKeyword("_EMISSION");
        }

        // ✅ 复制遮挡贴图
        Texture occlusionTex = TryReadTexture(src, new[] { "_OcclusionMap", "_OcclusionTexture" });
        if (occlusionTex != null && dst.HasProperty("_OcclusionMap"))
            dst.SetTexture("_OcclusionMap", occlusionTex);

        // ✅ 处理透明度
        float alpha = baseColor.a;
        if (alpha < 1f)
        {
            dst.SetFloat("_Surface", 1f);  // 0=Opaque, 1=Transparent
            dst.SetFloat("_Blend", 0f);    // 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply
            dst.SetOverrideTag("RenderType", "Transparent");
            dst.renderQueue = (int)RenderQueue.Transparent;
            dst.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        Debug.Log($"[IMPORT] CreateFallbackMaterial: '{src.name}' baseTex={(baseTex != null)} normalTex={(normalTex != null)} metallic={metallic:F2} alpha={alpha:F2}");
        return dst;
    }

    static bool TryReadColor(Material mat, out Color color)
    {
        color = Color.white;
        string[] names = { "_BaseColor", "_Color", "_TintColor" };
        foreach (var n in names)
        {
            if (!mat.HasProperty(n)) continue;
            color = mat.GetColor(n);
            return true;
        }
        return false;
    }

    static bool TryReadFloat(Material mat, string propertyName, out float value)
    {
        value = 0f;
        if (!mat.HasProperty(propertyName)) return false;
        value = mat.GetFloat(propertyName);
        return true;
    }

    static Texture TryReadTexture(Material mat, string[] propertyNames)
    {
        foreach (var n in propertyNames)
        {
            if (!mat.HasProperty(n)) continue;
            var t = mat.GetTexture(n);
            if (t != null) return t;
        }
        return null;
    }

    static Texture TryReadMainTexture(Material mat)
    {
        string[] preferred = { "_BaseMap", "_MainTex", "_BaseColorMap", "_BaseColorTexture" };
        foreach (var n in preferred)
        {
            if (!mat.HasProperty(n)) continue;
            var t = mat.GetTexture(n);
            if (t != null) return t;
        }

        var names = new List<string>();
        mat.GetTexturePropertyNames(names);
        foreach (var n in names)
        {
            var t = mat.GetTexture(n);
            if (t != null) return t;
        }
        return null;
    }

    void LogStatus(string msg)
    {
        Debug.Log(msg);
        if (statusText != null) statusText.text = msg;
    }

    void FocusModelRough(GameObject modelRoot)
    {
        if (!TryGetBounds(modelRoot, out var b)) return;

        Vector3 worldCenter = b.center;
        Vector3 delta = modelRoot.transform.position - worldCenter;
        modelRoot.transform.position += delta;
    }

    void FrameCameraToModel(GameObject modelRoot)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[IMPORT] Camera.main is null");
            return;
        }

        if (!TryGetBounds(modelRoot, out var b))
        {
            Debug.LogWarning("[IMPORT] No renderers found");
            return;
        }

        float radius = b.extents.magnitude;
        float fov = cam.fieldOfView * Mathf.Deg2Rad;
        float dist = (radius / Mathf.Tan(fov * 0.5f)) * 1.2f;

        Vector3 dir = cam.transform.forward;
        cam.transform.position = b.center - dir * dist;
        cam.transform.LookAt(b.center);

        Debug.Log("[IMPORT] Framed camera. center=" + b.center + " dist=" + dist);
    }

    bool TryGetBounds(GameObject modelRoot, out Bounds bounds)
    {
        bounds = default;
        if (modelRoot == null) return false;

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return false;

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    void StartAttachToCameraRoutine(Transform model, Transform cam)
    {
        if (model == null || cam == null) return;
        if (_attachRoutine != null)
        {
            StopCoroutine(_attachRoutine);
            _attachRoutine = null;
        }
        _attachRoutine = StartCoroutine(AttachToCameraRoutine(model, cam));
    }

    IEnumerator AttachToCameraRoutine(Transform model, Transform cam)
    {
        float duration = xrAttachDurationSec > 0.1f ? xrAttachDurationSec : 8f;
        Vector3 localPos = new Vector3(0f, xrPlaceVerticalOffset, xrPlaceDistance);

        model.SetParent(cam, true);
        model.localRotation = Quaternion.identity;
        model.localPosition = localPos;
        Debug.Log("[IMPORT] Abnormal XR pose: model temporarily attached to camera.");

        float endAt = Time.unscaledTime + duration;
        while (Time.unscaledTime < endAt)
        {
            if (model == null || cam == null)
            {
                _attachRoutine = null;
                yield break;
            }
            model.localPosition = localPos;
            yield return null;
        }

        if (model != null)
        {
            model.SetParent(spawnRoot, true);
            Debug.Log("[IMPORT] Model detached from camera after stabilization.");
        }

        _attachRoutine = null;
    }

    void ForceVisibleDebugMaterial(GameObject modelRoot)
    {
        if (modelRoot == null) return;

        if (_debugVisibleMat == null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Unlit/Color");
            if (s == null) s = Shader.Find("Standard");
            if (s == null)
            {
                Debug.LogWarning("[IMPORT] ForceVisibleDebugMaterial skipped: no usable shader found.");
                return;
            }
            _debugVisibleMat = new Material(s);
        }

        if (_debugVisibleMat.HasProperty("_BaseColor"))
            _debugVisibleMat.SetColor("_BaseColor", debugVisibleColor);
        if (_debugVisibleMat.HasProperty("_Color"))
            _debugVisibleMat.SetColor("_Color", debugVisibleColor);

        int changed = 0;
        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;
            if (mats == null || mats.Length == 0) continue;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = _debugVisibleMat;
            r.materials = mats;
            changed++;
        }

        Debug.Log("[IMPORT] ForceVisibleDebugMaterial applied. rendererCount=" + changed);
    }

    void SpawnDebugPlacementMarker(Vector3 worldPos)
    {
        float size = debugPlacementMarkerSize > 0.01f ? debugPlacementMarkerSize : 0.08f;
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = "ImportDebugMarker";
        marker.transform.position = worldPos;
        marker.transform.localScale = Vector3.one * size;
        marker.layer = 0;

        var col = marker.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var r = marker.GetComponent<Renderer>();
        if (r != null)
        {
            if (_debugVisibleMat == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit");
                if (s == null) s = Shader.Find("Unlit/Color");
                if (s == null) s = Shader.Find("Standard");
                if (s != null) _debugVisibleMat = new Material(s);
            }

            if (_debugVisibleMat != null)
            {
                if (_debugVisibleMat.HasProperty("_BaseColor"))
                    _debugVisibleMat.SetColor("_BaseColor", debugVisibleColor);
                if (_debugVisibleMat.HasProperty("_Color"))
                    _debugVisibleMat.SetColor("_Color", debugVisibleColor);
                r.material = _debugVisibleMat;
            }
        }

        Destroy(marker, 15f);
        Debug.Log("[IMPORT] Spawned debug marker at " + worldPos + " size=" + size);
    }
}
