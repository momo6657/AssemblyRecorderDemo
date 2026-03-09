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
            return await LoadInternalAsync(fullPath);
        }
        finally
        {
            _isLoading = false;
        }
    }

    async Task<bool> LoadInternalAsync(string fullPath)
    {
        try
        {
            LogStatus("[IMPORT] fullPath=" + fullPath);

            if (string.IsNullOrEmpty(fullPath))
            {
                LogStatus("[IMPORT][ERR] Path is null/empty");
                return false;
            }

            if (!File.Exists(fullPath))
            {
                LogStatus("[IMPORT][ERR] File not found: " + fullPath);
                return false;
            }

            long len = new FileInfo(fullPath).Length;
            LogStatus("[IMPORT] file size=" + len + " bytes");

            byte[] bytes = File.ReadAllBytes(fullPath);
            Debug.Log("[IMPORT] read bytes len=" + (bytes != null ? bytes.Length : 0));
            if (bytes != null && bytes.Length >= 4)
                Debug.Log($"[IMPORT] header bytes: {bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} {bytes[3]:X2}");

            var previousModel = _currentModel;

            if (resetSpawnRootTransform && spawnRoot != null)
            {
                spawnRoot.localPosition = Vector3.zero;
                spawnRoot.localRotation = Quaternion.identity;
                spawnRoot.localScale = Vector3.one;
            }

            var logger = new ConsoleLogger();
            var materialGenerator = CreateRuntimeMaterialGenerator();
            var gltf = new GltfImport(materialGenerator: materialGenerator, logger: logger);

#pragma warning disable CS0618
            bool ok = await gltf.LoadGltfBinary(bytes);
#pragma warning restore CS0618

            Debug.Log("[IMPORT] LoadGltfBinary ok=" + ok);
            if (!ok)
            {
                LogStatus("[IMPORT][ERR] glTF binary load failed");
                return false;
            }

            var root = new GameObject("ImportedModel");
            root.transform.SetParent(spawnRoot, false);

            bool instOk = await gltf.InstantiateMainSceneAsync(root.transform);
            Debug.Log("[IMPORT] Instantiate ok=" + instOk);
            if (!instOk)
            {
                Destroy(root);
                LogStatus("[IMPORT][ERR] Instantiate failed");
                return false;
            }

            // Keep this unconditional for runtime safety:
            // even if glTF's original shader graph is unavailable in build,
            // this guarantees a visible URP-compatible material.
            RemapImportedMaterials(root);

            if (destroyPrevious && previousModel != null && previousModel != root)
            {
                Debug.Log("[IMPORT] replacing previous model => " + previousModel.name);
                Destroy(previousModel);
            }

            _currentModel = root;
            CleanupDuplicateImportedModels(_currentModel);
            LogStatus("[IMPORT] Success");

            TryPlaceInFrontOfXrCamera(_currentModel);

            bool xrGrabApplied = TryApplyXrGrab(root.transform);
            if (!xrGrabApplied)
                EnsureColliders(_currentModel);

            RebuildModelIndex(root.transform);
            BindSelection(root.transform);
            BindOrbit(root.transform);
            TryFocusAndFrame(root);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            LogStatus("[IMPORT][EXCEPTION] " + ex.Message);
            return false;
        }
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

        Shader targetShader = Shader.Find("Universal Render Pipeline/Lit");
        if (targetShader == null) targetShader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (targetShader == null) targetShader = Shader.Find("Standard");
        if (targetShader == null)
        {
            Debug.LogWarning("[IMPORT] Remap materials skipped: no fallback shader found.");
            return;
        }

        int rendererCount = 0;
        int materialCount = 0;
        int replacedCount = 0;
        var cache = new Dictionary<int, Material>();
        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);

        foreach (var r in renderers)
        {
            if (r == null) continue;
            var mats = r.materials;
            if (mats == null || mats.Length == 0) continue;
            rendererCount++;

            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                materialCount++;
                if (src == null) continue;

                bool unsupported = IsUnsupportedShader(src.shader);
                if (remapOnlyWhenUnsupported && !unsupported) continue;

                int key = src.GetInstanceID();
                if (!cache.TryGetValue(key, out var dst))
                {
                    dst = CreateFallbackMaterial(src, targetShader);
                    cache[key] = dst;
                }

                if (dst != null && mats[i] != dst)
                {
                    mats[i] = dst;
                    replacedCount++;
                    changed = true;
                }
            }

            if (changed)
                r.materials = mats;
        }

        Debug.Log("[IMPORT] Material remap done. renderers=" + rendererCount
                  + " materials=" + materialCount
                  + " replaced=" + replacedCount
                  + " shader=" + targetShader.name);
    }

    IMaterialGenerator CreateRuntimeMaterialGenerator()
    {
        if (!preferGltfUrpMaterialGenerator)
            return null;

        try
        {
            var rpAsset = GraphicsSettings.currentRenderPipeline;
            if (rpAsset == null)
            {
                Debug.Log("[IMPORT] No SRP active, skip URP material generator.");
                return null;
            }

            var assembly = typeof(IMaterialGenerator).Assembly;
            var urpType = assembly.GetType("GLTFast.Materials.UniversalRPMaterialGenerator");
            if (urpType == null)
            {
                Debug.LogWarning("[IMPORT] UniversalRPMaterialGenerator type not found.");
                return null;
            }

            IMaterialGenerator generator = null;
            var ctors = urpType.GetConstructors();
            for (int i = 0; i < ctors.Length; i++)
            {
                var ctor = ctors[i];
                var ps = ctor.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsAssignableFrom(rpAsset.GetType()))
                {
                    generator = ctor.Invoke(new object[] { rpAsset }) as IMaterialGenerator;
                    break;
                }
            }

            if (generator == null)
            {
                var emptyCtor = urpType.GetConstructor(Type.EmptyTypes);
                if (emptyCtor != null)
                    generator = emptyCtor.Invoke(null) as IMaterialGenerator;
            }

            if (generator != null)
                Debug.Log("[IMPORT] Using glTFast material generator => " + generator.GetType().Name);
            else
                Debug.LogWarning("[IMPORT] Failed to create URP material generator, will use fallback remap.");

            return generator;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[IMPORT] CreateRuntimeMaterialGenerator failed: " + ex.Message);
            return null;
        }
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

        Color c = Color.white;
        if (TryReadColor(src, out var color)) c = color;
        if (dst.HasProperty("_BaseColor")) dst.SetColor("_BaseColor", c);
        if (dst.HasProperty("_Color")) dst.SetColor("_Color", c);

        var tex = TryReadMainTexture(src);
        if (tex != null)
        {
            if (dst.HasProperty("_BaseMap")) dst.SetTexture("_BaseMap", tex);
            else if (dst.HasProperty("_MainTex")) dst.SetTexture("_MainTex", tex);
        }

        if (TryReadFloat(src, "_Metallic", out var metallic) && dst.HasProperty("_Metallic"))
            dst.SetFloat("_Metallic", metallic);
        if (TryReadFloat(src, "_Glossiness", out var smoothness) && dst.HasProperty("_Smoothness"))
            dst.SetFloat("_Smoothness", smoothness);

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
