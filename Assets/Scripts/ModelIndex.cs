using System.Collections.Generic;
using UnityEngine;

public class ModelIndex : MonoBehaviour
{
    [Header("导入模型根节点（ImportedModel）")]
    public Transform modelRoot;

    [Header("Collider Auto Add")]
    public bool autoAddBoxColliderForRenderers = true;
    public bool skipAutoColliderWhenXrActive = true;

    // 旧脚本在用的字段名：map
    // key: partId (路径)  value: part Transform
    public Dictionary<string, Transform> map = new Dictionary<string, Transform>();

    // 记录“初始姿态”，给 RestoreBaseAll 用
    struct TRS
    {
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 scale;
        public TRS(Vector3 p, Quaternion r, Vector3 s) { pos = p; rot = r; scale = s; }
    }
    Dictionary<string, TRS> _base = new Dictionary<string, TRS>();

    /// <summary>
    /// 旧接口：Build()
    /// 扫描 modelRoot 下所有 Renderer，当作零件，生成 id，并填充 map/base
    /// </summary>
    // 兼容旧代码：Build(Transform root)
    

    // 兼容旧代码：BuildPath(Transform root, Transform t)
    public static ModelIndex Build(Transform root)
    {
        var idx = Object.FindFirstObjectByType<ModelIndex>();
        if (idx == null)
        {
            Debug.LogWarning("[ModelIndex] static Build: no ModelIndex in scene");
            return null;
        }

        idx.SetModelRoot(root, rebuild: true);
        return idx;
    }
    public static string BuildPath(Transform root, Transform t)
    {
        var idx = Object.FindFirstObjectByType<ModelIndex>();
        if (idx == null)
        {
            Debug.LogWarning("[ModelIndex] static BuildPath: no ModelIndex in scene");
            return "";
        }
        idx.modelRoot = root;
        return idx.BuildPath(t);
    }
    public void Build()
    {
        map.Clear();
        _base.Clear();

        if (modelRoot == null)
        {
            Debug.LogWarning("[ModelIndex] modelRoot is NULL");
            return;
        }

        var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            var t = r.transform;
            string id = BuildPath(t);

            if (!map.ContainsKey(id))
                map.Add(id, t);

            if (!_base.ContainsKey(id))
                _base.Add(id, new TRS(t.localPosition, t.localRotation, t.localScale));

            // Quest 抓取需要 collider：先保证有 collider（手机端也不坏）
            bool allowAutoCollider = autoAddBoxColliderForRenderers;
            if (allowAutoCollider && skipAutoColliderWhenXrActive)
            {
#if UNITY_2019_3_OR_NEWER
                if (UnityEngine.XR.XRSettings.enabled && UnityEngine.XR.XRSettings.isDeviceActive)
                    allowAutoCollider = false;
#endif
            }

            if (allowAutoCollider && t.GetComponent<Collider>() == null)
            {
                t.gameObject.AddComponent<BoxCollider>();
            }
        }

        Debug.Log($"[ModelIndex] Build ok, parts={map.Count}");
    }

    /// <summary>
    /// 旧接口：BuildPath(Transform)
    /// 返回零件的稳定 id（用 modelRoot 作为根的相对路径）
    /// </summary>
    public string BuildPath(Transform t)
    {
        if (modelRoot == null || t == null) return "";

        // 用 local hierarchy 路径：Root/Arm/Hand
        if (t == modelRoot) return modelRoot.name;

        var stack = new Stack<string>();
        var cur = t;
        while (cur != null && cur != modelRoot)
        {
            stack.Push(cur.name);
            cur = cur.parent;
        }
        stack.Push(modelRoot.name);
        return string.Join("/", stack);
    }

    /// <summary>
    /// 旧接口：恢复所有零件到初始姿态
    /// </summary>
    public void RestoreBaseAll()
    {
        foreach (var kv in map)
        {
            var id = kv.Key;
            var t = kv.Value;
            if (t == null) continue;

            if (_base.TryGetValue(id, out var trs))
            {
                t.localPosition = trs.pos;
                t.localRotation = trs.rot;
                t.localScale = trs.scale;
            }
        }
    }

    /// <summary>
    /// 给外部一个“设置当前模型根”的方便入口（不会影响旧代码）
    /// </summary>
    public void SetModelRoot(Transform root, bool rebuild = true)
    {
        modelRoot = root;
        if (rebuild) Build();
    }

    /// <summary>
    /// 可选：通过 id 找零件
    /// </summary>
    public Transform Get(string id)
    {
        map.TryGetValue(id, out var t);
        return t;
    }
}
