using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class XRGrabSetup : MonoBehaviour
{
    [Tooltip("Root of imported model.")]
    public Transform modelRoot;

    [Header("Part Picking")]
    public bool useTopLevelChildAsPart = true;

    [Header("Collider")]
    public bool preferMeshCollider = true;
    public bool forceConvexMeshCollider = true;
    public bool replaceExistingColliders = true;
    public bool addColliderIfMissing = true;
    public bool disableCollisionBetweenParts = true;  // 新增：禁用零件之间的碰撞

    [Header("Physics")]
    public bool addRigidbodyIfMissing = true;
    public bool forceKinematicBody = true;

    [Header("Grab")]
    public XRBaseInteractable.MovementType movementType = XRBaseInteractable.MovementType.Kinematic;
    public bool useDynamicAttach = false;  // 修复：改为 false，避免动态附加导致的缩放问题
    public bool retainTransformParent = false;  // 修复：改为 false，允许零件独立移动
    public bool stabilizeParentDuringGrab = false;  // 修复：改为 false，不需要稳定父级
    public bool matchAttachPosition = true;
    public bool matchAttachRotation = true;
    public bool snapToColliderVolume = false;
    [Min(0f)] public float attachEaseInTime = 0f;
    public bool throwOnDetach = false;
    public bool trackPosition = true;  // 新增：确保跟踪位置
    public bool trackRotation = true;  // 新增：确保跟踪旋转
    public bool trackScale = false;    // 新增：禁止跟踪缩放

    public void Apply()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning("[XRGrabSetup] modelRoot is null.");
            return;
        }

        var parts = CollectParts();
        if (parts.Count == 0)
        {
            Debug.LogWarning("[XRGrabSetup] no parts found under modelRoot.");
            return;
        }

        int applied = 0;
        int meshColliderCount = 0;
        int fallbackBoxCount = 0;

        // 修复：在应用抓取设置前，先缓存每个部件的初始 localScale
        var initialScales = new Dictionary<Transform, Vector3>();
        foreach (var part in parts)
        {
            if (part != null)
            {
                initialScales[part] = part.localScale;
            }
        }

        foreach (var part in parts)
        {
            if (part == null) continue;
            var go = part.gameObject;

            var colliders = EnsurePartColliders(part, out int partMeshCount, out bool usedFallbackBox);
            meshColliderCount += partMeshCount;
            if (usedFallbackBox) fallbackBoxCount++;

            if (addRigidbodyIfMissing && !go.TryGetComponent<Rigidbody>(out _))
                go.AddComponent<Rigidbody>();

            if (go.TryGetComponent<Rigidbody>(out var body))
            {
                // 修复：完全禁用物理效果
                body.useGravity = false;  // 不受重力影响
                body.isKinematic = true;  // 运动学模式，不受物理引擎影响
                body.interpolation = RigidbodyInterpolation.Interpolate;  // 平滑移动
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;  // 连续碰撞检测
                
                // 修复：禁用所有物理约束，防止零件飞走或被撞开
                body.constraints = RigidbodyConstraints.None;  // 不锁定任何轴
                body.mass = 1f;  // 质量设为 1
                body.linearDamping = 0f;  // 无空气阻力
                body.angularDamping = 0f;  // 无角阻力
                
                Debug.Log($"[XRGrabSetup] Configured Rigidbody for {part.name}: isKinematic={body.isKinematic}, useGravity={body.useGravity}");
            }

            if (!go.TryGetComponent<XRGrabInteractable>(out var grab))
                grab = go.AddComponent<XRGrabInteractable>();

            grab.movementType = movementType;
            grab.useDynamicAttach = useDynamicAttach;
            grab.retainTransformParent = retainTransformParent;
            grab.matchAttachPosition = matchAttachPosition;
            grab.matchAttachRotation = matchAttachRotation;
            grab.snapToColliderVolume = snapToColliderVolume;
            grab.attachEaseInTime = attachEaseInTime;
            grab.throwOnDetach = throwOnDetach;
            
            // 修复：禁用缩放跟踪，防止抓取时缩放变化
            grab.trackPosition = trackPosition;
            grab.trackRotation = trackRotation;
            grab.trackScale = trackScale;
            
            // 修复：添加 ScaleLock 组件，强制锁定 scale
            if (!go.TryGetComponent<ScaleLock>(out _))
            {
                go.AddComponent<ScaleLock>();
                Debug.Log($"[XRGrabSetup] Added ScaleLock to {part.name}");
            }
            
            // 临时：添加 ScaleDiagnostic 用于调试
            if (!go.TryGetComponent<ScaleDiagnostic>(out _))
            {
                go.AddComponent<ScaleDiagnostic>();
                Debug.Log($"[XRGrabSetup] Added ScaleDiagnostic to {part.name}");
            }

            grab.colliders.Clear();
            for (int i = 0; i < colliders.Count; i++)
            {
                var col = colliders[i];
                if (col == null || !col.enabled) continue;
                if (!grab.colliders.Contains(col))
                    grab.colliders.Add(col);
            }

            if (grab.colliders.Count == 0)
            {
                var allCols = part.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < allCols.Length; i++)
                {
                    var col = allCols[i];
                    if (col == null || !col.enabled) continue;
                    if (!grab.colliders.Contains(col))
                        grab.colliders.Add(col);
                }
            }

            if (stabilizeParentDuringGrab)
            {
                if (!go.TryGetComponent<XRStableParentDuringGrab>(out var stableParent))
                    stableParent = go.AddComponent<XRStableParentDuringGrab>();
                stableParent.grabInteractable = grab;
                stableParent.originalParent = part.parent;
            }

            // 修复：确保在设置完所有组件后，恢复初始 localScale
            if (initialScales.TryGetValue(part, out var initialScale))
            {
                if (part.localScale != initialScale)
                {
                    Debug.LogWarning($"[XRGrabSetup] Part {part.name} scale changed during setup, restoring from {part.localScale} to {initialScale}");
                    part.localScale = initialScale;
                }
            }

            applied++;
        }

        int interactorCount = UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        ).Length;

        Debug.Log("[XRGrabSetup] Applied to parts=" + applied
                  + ", meshColliders=" + meshColliderCount
                  + ", boxFallbackParts=" + fallbackBoxCount
                  + ", sceneInteractors=" + interactorCount
                  + ", movementType=" + movementType
                  + ", forceKinematicBody=" + forceKinematicBody
                  + ", useDynamicAttach=" + useDynamicAttach
                  + ", retainTransformParent=" + retainTransformParent
                  + ", stabilizeParentDuringGrab=" + stabilizeParentDuringGrab);
        
        // 修复：禁用零件之间的碰撞，防止零件互相撞开
        if (disableCollisionBetweenParts && parts.Count > 1)
        {
            int disabledCollisions = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                for (int j = i + 1; j < parts.Count; j++)
                {
                    var part1 = parts[i];
                    var part2 = parts[j];
                    if (part1 == null || part2 == null) continue;
                    
                    var colliders1 = part1.GetComponentsInChildren<Collider>(true);
                    var colliders2 = part2.GetComponentsInChildren<Collider>(true);
                    
                    foreach (var col1 in colliders1)
                    {
                        foreach (var col2 in colliders2)
                        {
                            if (col1 != null && col2 != null)
                            {
                                Physics.IgnoreCollision(col1, col2, true);
                                disabledCollisions++;
                            }
                        }
                    }
                }
            }
            Debug.Log($"[XRGrabSetup] Disabled {disabledCollisions} collision pairs between parts");
        }
    }

    List<Collider> EnsurePartColliders(Transform part, out int meshColliderCount, out bool usedFallbackBox)
    {
        meshColliderCount = 0;
        usedFallbackBox = false;
        var result = new List<Collider>();

        if (replaceExistingColliders)
        {
            var existing = part.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null) existing[i].enabled = false;
            }
        }

        if (preferMeshCollider)
        {
            AddMeshColliders(part, result, ref meshColliderCount);
        }

        if (result.Count == 0 && addColliderIfMissing)
        {
            var box = AddFittedBoxCollider(part);
            if (box != null)
            {
                box.enabled = true;
                result.Add(box);
                usedFallbackBox = true;
            }
        }

        if (result.Count == 0)
        {
            var existing = part.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < existing.Length; i++)
            {
                var col = existing[i];
                if (col == null) continue;
                col.enabled = true;
                result.Add(col);
            }
        }
        
        // 修复：配置所有 collider，禁用物理碰撞
        foreach (var col in result)
        {
            if (col == null) continue;
            
            // 修复：不设置为 Trigger，因为 XR Interaction 需要实体 collider
            // 但是由于 Rigidbody 是 Kinematic，所以不会产生物理碰撞
            col.isTrigger = false;
            
            // 修复：如果是 MeshCollider，确保是 convex
            if (col is MeshCollider mc)
            {
                mc.convex = true;
            }
            
            Debug.Log($"[XRGrabSetup] Configured Collider for {part.name}: type={col.GetType().Name}, isTrigger={col.isTrigger}");
        }

        return result;
    }

    void AddMeshColliders(Transform part, List<Collider> collector, ref int meshColliderCount)
    {
        var seenGo = new HashSet<int>();

        var meshFilters = part.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            var mf = meshFilters[i];
            if (mf == null || mf.sharedMesh == null) continue;

            var go = mf.gameObject;
            if (!seenGo.Add(go.GetInstanceID())) continue;

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();

            mc.sharedMesh = mf.sharedMesh;
            mc.convex = forceConvexMeshCollider;
            mc.enabled = true;
            collector.Add(mc);
            meshColliderCount++;
        }

        var skinned = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            var smr = skinned[i];
            if (smr == null || smr.sharedMesh == null) continue;

            var go = smr.gameObject;
            if (!seenGo.Add(go.GetInstanceID())) continue;

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();

            mc.sharedMesh = smr.sharedMesh;
            mc.convex = forceConvexMeshCollider;
            mc.enabled = true;
            collector.Add(mc);
            meshColliderCount++;
        }
    }

    List<Transform> CollectParts()
    {
        var parts = new List<Transform>();
        var seen = new HashSet<int>();

        // 修复：改进零件识别逻辑
        // 策略：找到所有有 Renderer 的 GameObject，每个都作为独立的零件
        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        
        Debug.Log($"[XRGrabSetup] Found {renderers.Length} renderers under {modelRoot.name}");
        
        foreach (var r in renderers)
        {
            if (r == null) continue;
            
            Transform part = r.transform;
            
            // 修复：直接使用 renderer 所在的 GameObject 作为零件
            // 不再向上查找父级，这样每个有 mesh 的对象都是独立的零件
            if (seen.Add(part.GetInstanceID()))
            {
                parts.Add(part);
                Debug.Log($"[XRGrabSetup] Added part: {part.name} (path: {GetPath(part)})");
            }
        }

        Debug.Log($"[XRGrabSetup] Total parts collected: {parts.Count}");
        return parts;
    }
    
    string GetPath(Transform t)
    {
        if (t == null || t == modelRoot) return "";
        var path = t.name;
        var current = t.parent;
        while (current != null && current != modelRoot)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }

    BoxCollider AddFittedBoxCollider(Transform part)
    {
        var renderers = part.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            return part.gameObject.AddComponent<BoxCollider>();
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        var box = part.GetComponent<BoxCollider>();
        if (box == null) box = part.gameObject.AddComponent<BoxCollider>();

        box.center = part.InverseTransformPoint(b.center);

        // 修复：使用 localScale 而不是 lossyScale，避免父级变化时的缩放问题
        // lossyScale 会受到父级层次结构的影响，在 XR 抓取时父级可能改变导致计算错误
        var localScale = part.localScale;
        float sx = Mathf.Abs(localScale.x) > 1e-5f ? Mathf.Abs(localScale.x) : 1f;
        float sy = Mathf.Abs(localScale.y) > 1e-5f ? Mathf.Abs(localScale.y) : 1f;
        float sz = Mathf.Abs(localScale.z) > 1e-5f ? Mathf.Abs(localScale.z) : 1f;
        
        // 使用世界空间 bounds 转换到局部空间
        Vector3 worldSize = b.size;
        Vector3 localSize = new Vector3(worldSize.x / sx, worldSize.y / sy, worldSize.z / sz);
        
        // 额外保护：如果计算出的 size 异常小或异常大，使用安全默认值
        if (localSize.x < 0.001f || localSize.x > 1000f) localSize.x = 1f;
        if (localSize.y < 0.001f || localSize.y > 1000f) localSize.y = 1f;
        if (localSize.z < 0.001f || localSize.z > 1000f) localSize.z = 1f;
        
        box.size = localSize;
        return box;
    }
}

[DisallowMultipleComponent]
public class XRStableParentDuringGrab : MonoBehaviour
{
    public XRGrabInteractable grabInteractable;
    public Transform originalParent;

    bool _isSelected;
    Vector3 _originalLocalScale;  // 修复：改名，明确这是原始缩放
    Vector3 _cachedWorldScale;

    void Awake()
    {
        CacheRefs();
        // 修复：在 Awake 时就缓存原始 scale，而不是在抓取时
        _originalLocalScale = transform.localScale;
        _cachedWorldScale = transform.lossyScale;
        Debug.Log($"[XRStableParent] {transform.name} initialized with original scale: local={_originalLocalScale}, world={_cachedWorldScale}");
    }

    void OnEnable()
    {
        CacheRefs();
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void LateUpdate()
    {
        if (_isSelected)
        {
            // 修复：不再强制恢复父级关系，允许零件独立移动
            // RestoreOriginalParent();  // ← 注释掉这行
            
            // 修复：只锁定 scale，不管父级关系
            if (transform.localScale != _originalLocalScale)
            {
                Debug.LogWarning($"[XRStableParent] {transform.name} scale changed from {transform.localScale} to {_originalLocalScale}, restoring!");
                transform.localScale = _originalLocalScale;
            }
            
            // 修复：额外检查 lossyScale，如果世界缩放变化也强制恢复
            Vector3 currentWorldScale = transform.lossyScale;
            float scaleDiff = Vector3.Distance(currentWorldScale, _cachedWorldScale);
            if (scaleDiff > 0.001f)
            {
                Debug.LogWarning($"[XRStableParent] {transform.name} world scale changed by {scaleDiff}, forcing local scale restore!");
                transform.localScale = _originalLocalScale;
            }
        }
    }

    void CacheRefs()
    {
        if (grabInteractable == null)
            grabInteractable = GetComponent<XRGrabInteractable>();
        if (originalParent == null)
            originalParent = transform.parent;
    }

    void Subscribe()
    {
        if (grabInteractable == null) return;
        grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        grabInteractable.selectExited.RemoveListener(OnSelectExited);
        grabInteractable.selectEntered.AddListener(OnSelectEntered);
        grabInteractable.selectExited.AddListener(OnSelectExited);
    }

    void Unsubscribe()
    {
        if (grabInteractable == null) return;
        grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
        grabInteractable.selectExited.RemoveListener(OnSelectExited);
    }

    void OnSelectEntered(SelectEnterEventArgs _)
    {
        _isSelected = true;
        // 修复：不要在这里重新缓存 scale！使用 Awake 时缓存的原始值
        Debug.Log($"[XRStableParent] {transform.name} grabbed, will maintain original scale: {_originalLocalScale}");
        // 修复：不再恢复父级关系
        // RestoreOriginalParent();  // ← 注释掉这行
        // 修复：立即恢复到原始 scale
        transform.localScale = _originalLocalScale;
    }

    void OnSelectExited(SelectExitEventArgs _)
    {
        _isSelected = false;
        // 修复：不再恢复父级关系
        // RestoreOriginalParent();  // ← 注释掉这行
        // 修复：释放时也确保 scale 正确
        transform.localScale = _originalLocalScale;
        Debug.Log($"[XRStableParent] {transform.name} released, restored scale to {_originalLocalScale}");
    }

    void RestoreOriginalParent()
    {
        if (originalParent == null || transform.parent == originalParent)
            return;

        // 修复：使用 worldPositionStays=true 保持世界空间位置，避免缩放问题
        transform.SetParent(originalParent, true);
    }
    
    /// <summary>
    /// 手动更新原始缩放基准（例如在模型重新加载后）
    /// </summary>
    public void UpdateOriginalScale()
    {
        _originalLocalScale = transform.localScale;
        _cachedWorldScale = transform.lossyScale;
        Debug.Log($"[XRStableParent] {transform.name} updated original scale to {_originalLocalScale}");
    }
}
