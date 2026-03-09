using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局缩放保护组件，防止模型在 XR 交互时意外缩放
/// 挂载到场景中的任意 GameObject 上即可
/// </summary>
public class ScaleProtector : MonoBehaviour
{
    [Header("监控设置")]
    [Tooltip("是否启用缩放保护")]
    public bool enableProtection = true;
    
    [Tooltip("检查间隔（秒）")]
    public float checkInterval = 0.1f;
    
    [Tooltip("允许的缩放误差")]
    public float scaleTolerance = 0.001f;
    
    [Header("调试")]
    public bool logScaleChanges = true;
    
    private Dictionary<Transform, Vector3> _originalScales = new Dictionary<Transform, Vector3>();
    private float _nextCheckTime;
    
    void Start()
    {
        _nextCheckTime = Time.time + checkInterval;
    }
    
    void Update()
    {
        if (!enableProtection) return;
        if (Time.time < _nextCheckTime) return;
        
        _nextCheckTime = Time.time + checkInterval;
        CheckAndRestoreScales();
    }
    
    void CheckAndRestoreScales()
    {
        // 查找所有带有 XRGrabInteractable 的物体
        var grabInteractables = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
        
        foreach (var interactable in grabInteractables)
        {
            if (interactable == null) continue;
            
            Transform t = interactable.transform;
            
            // 记录初始缩放
            if (!_originalScales.ContainsKey(t))
            {
                _originalScales[t] = t.localScale;
                if (logScaleChanges)
                {
                    Debug.Log($"[ScaleProtector] Registered {t.name} with scale {t.localScale}");
                }
                continue;
            }
            
            // 检查缩放是否改变
            Vector3 originalScale = _originalScales[t];
            Vector3 currentScale = t.localScale;
            float scaleDiff = Vector3.Distance(currentScale, originalScale);
            
            if (scaleDiff > scaleTolerance)
            {
                if (logScaleChanges)
                {
                    Debug.LogWarning($"[ScaleProtector] {t.name} scale changed from {originalScale} to {currentScale} (diff={scaleDiff}), restoring!");
                }
                
                // 修复：立即恢复
                t.localScale = originalScale;
                
                // 修复：同时通知 XRStableParentDuringGrab 组件更新其缓存
                var stableParent = t.GetComponent<XRStableParentDuringGrab>();
                if (stableParent != null)
                {
                    stableParent.UpdateOriginalScale();
                }
            }
        }
    }
    
    /// <summary>
    /// 手动注册需要保护的 Transform
    /// </summary>
    public void RegisterTransform(Transform t)
    {
        if (t == null) return;
        
        if (!_originalScales.ContainsKey(t))
        {
            _originalScales[t] = t.localScale;
            if (logScaleChanges)
            {
                Debug.Log($"[ScaleProtector] Manually registered {t.name} with scale {t.localScale}");
            }
        }
    }
    
    /// <summary>
    /// 更新已注册 Transform 的基准缩放
    /// </summary>
    public void UpdateBaseScale(Transform t)
    {
        if (t == null) return;
        
        _originalScales[t] = t.localScale;
        if (logScaleChanges)
        {
            Debug.Log($"[ScaleProtector] Updated base scale for {t.name} to {t.localScale}");
        }
    }
    
    /// <summary>
    /// 清除所有记录
    /// </summary>
    public void ClearAll()
    {
        _originalScales.Clear();
        if (logScaleChanges)
        {
            Debug.Log("[ScaleProtector] Cleared all registered transforms");
        }
    }
}
