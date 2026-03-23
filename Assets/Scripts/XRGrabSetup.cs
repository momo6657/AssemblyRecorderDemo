using System.Collections;
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
    public bool preferMeshCollider = false;         // ← 改为 false：MeshCollider 在 Quest 上耗时过长
    public bool forceConvexMeshCollider = true;
    public bool replaceExistingColliders = true;
    public bool addColliderIfMissing = true;
    public bool disableCollisionBetweenParts = true;

    [Header("Physics")]
    public bool addRigidbodyIfMissing = true;
    public bool forceKinematicBody = true;

    [Header("Grab")]
    public XRBaseInteractable.MovementType movementType = XRBaseInteractable.MovementType.Kinematic;
    public bool useDynamicAttach = false;
    public bool retainTransformParent = false;
    public bool stabilizeParentDuringGrab = false;
    public bool matchAttachPosition = true;
    public bool matchAttachRotation = true;
    public bool snapToColliderVolume = false;
    [Min(0f)] public float attachEaseInTime = 0f;
    public bool throwOnDetach = false;
    public bool trackPosition = true;
    public bool trackRotation = true;
    public bool trackScale = false;

    // -------------------------------------------------------
    // 同步入口（供 ImportManager 调用）
    // -------------------------------------------------------
    public void Apply()
    {
        StartCoroutine(ApplyAsync());
    }

    // -------------------------------------------------------
    // 核心协程：分帧处理每个 part，避免主线程卡死
    // -------------------------------------------------------
    public IEnumerator ApplyAsync()
    {
        if (modelRoot == null)
        {
            Debug.LogWarning("[XRGrabSetup] modelRoot is null.");
            yield break;
        }

        var parts = CollectParts();
        if (parts.Count == 0)
        {
            Debug.LogWarning("[XRGrabSetup] no parts found under modelRoot.");
            yield break;
        }

        // 缓存初始 localScale
        var initialScales = new Dictionary<Transform, Vector3>(parts.Count);
        foreach (var part in parts)
            if (part != null)
                initialScales[part] = part.localScale;

        int applied = 0;
        int meshColliderCount = 0;
        int fallbackBoxCount = 0;

        for (int pi = 0; pi < parts.Count; pi++)
        {
            var part = parts[pi];
            if (part == null) continue;
            var go = part.gameObject;

            // --- Collider（最耗时，每个 part 处理完后 yield）---
            var colliders = EnsurePartColliders(part, out int partMeshCount, out bool usedFallbackBox);
            meshColliderCount += partMeshCount;
            if (usedFallbackBox) fallbackBoxCount++;

            // 每处理完一个 part 就让出主线程
            yield return null;

            // --- Rigidbody ---
            if (addRigidbodyIfMissing && !go.TryGetComponent<Rigidbody>(out _))
                go.AddComponent<Rigidbody>();

            if (go.TryGetComponent<Rigidbody>(out var body))
            {
                body.useGravity = false;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                body.constraints = RigidbodyConstraints.None;
                body.mass = 1f;
                body.linearDamping = 0f;
                body.angularDamping = 0f;
                Debug.Log($"[XRGrabSetup] Configured Rigidbody for {part.name}: isKinematic={body.isKinematic}, useGravity={body.useGravity}");
            }

            // --- XRGrabInteractable ---
            if (!go.TryGetComponent<XRGrabInteractable>(out var grab))
                grab = go.AddComponent<XRGrabInteractable>();

            grab.movementType          = movementType;
            grab.useDynamicAttach      = useDynamicAttach;
            grab.retainTransformParent = retainTransformParent;
            grab.matchAttachPosition   = matchAttachPosition;
            grab.matchAttachRotation   = matchAttachRotation;
            grab.snapToColliderVolume  = snapToColliderVolume;
            grab.attachEaseInTime      = attachEaseInTime;
            grab.throwOnDetach         = throwOnDetach;
            grab.trackPosition         = trackPosition;
            grab.trackRotation         = trackRotation;
            grab.trackScale            = trackScale;

            // --- ScaleLock ---
            if (!go.TryGetComponent<ScaleLock>(out _))
            {
                go.AddComponent<ScaleLock>();
                Debug.Log($"[XRGrabSetup] Added ScaleLock to {part.name}");
            }

            // --- Collider 列表绑定到 Grab ---
            grab.colliders.Clear();
            foreach (var col in colliders)
            {
                if (col != null && col.enabled && !grab.colliders.Contains(col))
                    grab.colliders.Add(col);
            }
            if (grab.colliders.Count == 0)
            {
                var allCols = part.GetComponentsInChildren<Collider>(true);
                foreach (var col in allCols)
                    if (col != null && col.enabled && !grab.colliders.Contains(col))
                        grab.colliders.Add(col);
            }

            // --- 恢复 localScale（防止 AddComponent 触发 scale 变化）---
            if (initialScales.TryGetValue(part, out var initScale) && part.localScale != initScale)
            {
                Debug.LogWarning($"[XRGrabSetup] Restoring scale for {part.name}");
                part.localScale = initScale;
            }

            applied++;

            // 每个 part 结束后让出主线程（保证帧率）
            yield return null;
        }

        // --- 禁用零件间碰撞（分帧处理，每 10 对 yield 一次）---
        if (disableCollisionBetweenParts && parts.Count > 1)
        {
            int disabledCollisions = 0;
            int pairCount = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                for (int j = i + 1; j < parts.Count; j++)
                {
                    var p1 = parts[i];
                    var p2 = parts[j];
                    if (p1 == null || p2 == null) continue;

                    var cols1 = p1.GetComponentsInChildren<Collider>(true);
                    var cols2 = p2.GetComponentsInChildren<Collider>(true);
                    foreach (var c1 in cols1)
                        foreach (var c2 in cols2)
                            if (c1 != null && c2 != null)
                            {
                                Physics.IgnoreCollision(c1, c2, true);
                                disabledCollisions++;
                            }

                    pairCount++;
                    if (pairCount % 10 == 0)
                        yield return null;
                }
            }
            Debug.Log($"[XRGrabSetup] Disabled {disabledCollisions} collision pairs between parts");
        }

        int interactorCount = UnityEngine.Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInteractor>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

        Debug.Log($"[XRGrabSetup] Applied to parts={applied}, meshColliders={meshColliderCount}, boxFallback={fallbackBoxCount}, interactors={interactorCount}");
    }

    // -------------------------------------------------------
    // Collider 处理（每个 part 同步，但整体分帧）
    // -------------------------------------------------------
    List<Collider> EnsurePartColliders(Transform part, out int meshColliderCount, out bool usedFallbackBox)
    {
        meshColliderCount = 0;
        usedFallbackBox = false;
        var result = new List<Collider>();

        if (replaceExistingColliders)
        {
            var existing = part.GetComponentsInChildren<Collider>(true);
            foreach (var col in existing)
                if (col != null) col.enabled = false;
        }

        if (preferMeshCollider)
            AddMeshColliders(part, result, ref meshColliderCount);

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
            foreach (var col in existing)
            {
                if (col == null) continue;
                col.enabled = true;
                result.Add(col);
            }
        }

        foreach (var col in result)
        {
            if (col == null) continue;
            col.isTrigger = false;
            if (col is MeshCollider mc)
                mc.convex = forceConvexMeshCollider;
            Debug.Log($"[XRGrabSetup] Configured Collider for {part.name}: type={col.GetType().Name}, isTrigger={col.isTrigger}");
        }

        return result;
    }

    void AddMeshColliders(Transform part, List<Collider> collector, ref int meshColliderCount)
    {
        // Quest 性能限制：单个 mesh 超过此三角形数量时跳过 MeshCollider，改用 BoxCollider
        const int MAX_TRIANGLES = 50000;

        var seenGo = new HashSet<int>();

        var meshFilters = part.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf == null || mf.sharedMesh == null) continue;
            var go = mf.gameObject;
            if (!seenGo.Add(go.GetInstanceID())) continue;

            // 超过三角形阈值时跳过，外层会回退到 BoxCollider
            if (mf.sharedMesh.triangles.Length / 3 > MAX_TRIANGLES)
            {
                Debug.LogWarning($"[XRGrabSetup] Mesh '{mf.sharedMesh.name}' has {mf.sharedMesh.triangles.Length / 3} triangles (>{MAX_TRIANGLES}), skipping MeshCollider");
                continue;
            }

            var mc = go.GetComponent<MeshCollider>();
            if (mc == null) mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = forceConvexMeshCollider;
            mc.enabled = true;
            collector.Add(mc);
            meshColliderCount++;
        }

        var skinned = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var smr in skinned)
        {
            if (smr == null || smr.sharedMesh == null) continue;
            var go = smr.gameObject;
            if (!seenGo.Add(go.GetInstanceID())) continue;

            if (smr.sharedMesh.triangles.Length / 3 > MAX_TRIANGLES)
            {
                Debug.LogWarning($"[XRGrabSetup] SkinnedMesh '{smr.sharedMesh.name}' has {smr.sharedMesh.triangles.Length / 3} triangles (>{MAX_TRIANGLES}), skipping MeshCollider");
                continue;
            }

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
        var seen  = new HashSet<int>();

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        Debug.Log($"[XRGrabSetup] Found {renderers.Length} renderers under {modelRoot.name}");

        foreach (var r in renderers)
        {
            if (r == null) continue;
            var part = r.transform;
            if (seen.Add(part.GetInstanceID()))
            {
                parts.Add(part);
            }
        }

        Debug.Log($"[XRGrabSetup] Total parts collected: {parts.Count}");
        return parts;
    }

    BoxCollider AddFittedBoxCollider(Transform part)
    {
        var renderers = part.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return part.gameObject.AddComponent<BoxCollider>();

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        var box = part.GetComponent<BoxCollider>();
        if (box == null) box = part.gameObject.AddComponent<BoxCollider>();

        box.center = part.InverseTransformPoint(b.center);

        var ls = part.localScale;
        float sx = Mathf.Abs(ls.x) > 1e-5f ? Mathf.Abs(ls.x) : 1f;
        float sy = Mathf.Abs(ls.y) > 1e-5f ? Mathf.Abs(ls.y) : 1f;
        float sz = Mathf.Abs(ls.z) > 1e-5f ? Mathf.Abs(ls.z) : 1f;
        Vector3 worldSize = b.size;
        Vector3 localSize = new Vector3(worldSize.x / sx, worldSize.y / sy, worldSize.z / sz);
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

    bool    _isSelected;
    Vector3 _originalLocalScale;
    Vector3 _cachedWorldScale;

    void Awake()
    {
        CacheRefs();
        _originalLocalScale = transform.localScale;
        _cachedWorldScale   = transform.lossyScale;
    }

    void OnEnable()  { CacheRefs(); Subscribe(); }
    void OnDisable() { Unsubscribe(); }

    void LateUpdate()
    {
        if (!_isSelected) return;
        if (transform.localScale != _originalLocalScale)
        {
            Debug.LogWarning($"[XRStableParent] {transform.name} scale changed, restoring.");
            transform.localScale = _originalLocalScale;
        }
    }

    void CacheRefs()
    {
        if (grabInteractable == null) grabInteractable = GetComponent<XRGrabInteractable>();
        if (originalParent == null)   originalParent   = transform.parent;
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
        transform.localScale = _originalLocalScale;
        Debug.Log($"[XRStableParent] {transform.name} grabbed, scale locked to {_originalLocalScale}");
    }

    void OnSelectExited(SelectExitEventArgs _)
    {
        _isSelected = false;
        transform.localScale = _originalLocalScale;
        Debug.Log($"[XRStableParent] {transform.name} released, scale restored to {_originalLocalScale}");
    }

    public void UpdateOriginalScale()
    {
        _originalLocalScale = transform.localScale;
        _cachedWorldScale   = transform.lossyScale;
    }
}