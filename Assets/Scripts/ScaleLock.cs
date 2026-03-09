using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 简单的缩放锁定组件 - 防止抓取时缩放变化
/// 自动添加到每个可抓取的零件上
/// 修复：保持世界空间缩放（lossyScale）不变，而不是 localScale
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ScaleLock : MonoBehaviour
{
    private Vector3 _lockedLocalScale;
    private Vector3 _lockedLossyScale;  // 锁定世界空间缩放
    private Transform _initialParent;
    private bool _isGrabbed = false;
    
    void Awake()
    {
        // 在初始化时锁定当前的 scale
        _lockedLocalScale = transform.localScale;
        _lockedLossyScale = transform.lossyScale;  // 记录世界空间缩放
        _initialParent = transform.parent;
        
        Debug.Log($"[ScaleLock] {name} locked scale:");
        Debug.Log($"  localScale: {_lockedLocalScale}");
        Debug.Log($"  lossyScale: {_lockedLossyScale}");
        Debug.Log($"  parent: {(_initialParent != null ? _initialParent.name : "null")}");
        if (_initialParent != null)
        {
            Debug.Log($"  parent.lossyScale: {_initialParent.lossyScale}");
        }
    }
    
    void LateUpdate()
    {
        if (_isGrabbed)
        {
            // 修复：保持世界空间缩放（lossyScale）不变
            Vector3 targetLossyScale = _lockedLossyScale;
            Vector3 currentLossyScale = transform.lossyScale;
            
            // 如果世界缩放变化了，调整 localScale 来补偿
            float diff = Vector3.Distance(currentLossyScale, targetLossyScale);
            if (diff > 0.001f)
            {
                Vector3 parentLossyScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
                
                // 计算需要的 localScale = targetLossyScale / parentLossyScale
                Vector3 newLocalScale = new Vector3(
                    Mathf.Abs(parentLossyScale.x) > 0.001f ? targetLossyScale.x / parentLossyScale.x : _lockedLocalScale.x,
                    Mathf.Abs(parentLossyScale.y) > 0.001f ? targetLossyScale.y / parentLossyScale.y : _lockedLocalScale.y,
                    Mathf.Abs(parentLossyScale.z) > 0.001f ? targetLossyScale.z / parentLossyScale.z : _lockedLocalScale.z
                );
                
                Debug.LogWarning($"[ScaleLock] {name} adjusting localScale to maintain lossyScale:");
                Debug.LogWarning($"  currentLossyScale: {currentLossyScale}");
                Debug.LogWarning($"  targetLossyScale: {targetLossyScale}");
                Debug.LogWarning($"  parentLossyScale: {parentLossyScale}");
                Debug.LogWarning($"  currentLocalScale: {transform.localScale}");
                Debug.LogWarning($"  newLocalScale: {newLocalScale}");
                
                transform.localScale = newLocalScale;
            }
        }
        else
        {
            // 未抓取时，只锁定 localScale
            if (Vector3.Distance(transform.localScale, _lockedLocalScale) > 0.001f)
            {
                Debug.LogWarning($"[ScaleLock] {name} localScale changed from {transform.localScale} to {_lockedLocalScale}, restoring!");
                transform.localScale = _lockedLocalScale;
            }
        }
    }
    
    void OnEnable()
    {
        var grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }
    }
    
    void OnDisable()
    {
        var grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }
    }
    
    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        _isGrabbed = true;
        Debug.Log($"[ScaleLock] {name} grabbed, will maintain lossyScale {_lockedLossyScale}");
        Debug.Log($"  current parent: {(transform.parent != null ? transform.parent.name : "null")}");
        Debug.Log($"  current lossyScale: {transform.lossyScale}");
    }
    
    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        _isGrabbed = false;
        
        Debug.Log($"[ScaleLock] {name} released");
        Debug.Log($"  current parent: {(transform.parent != null ? transform.parent.name : "null")}");
        Debug.Log($"  initial parent: {(_initialParent != null ? _initialParent.name : "null")}");
        Debug.Log($"  current localScale: {transform.localScale}");
        Debug.Log($"  current lossyScale: {transform.lossyScale}");
        Debug.Log($"  target localScale: {_lockedLocalScale}");
        Debug.Log($"  target lossyScale: {_lockedLossyScale}");
        
        // 修复：必须先恢复父级，再恢复 localScale
        // 因为 localScale 的效果取决于父级的 scale
        if (transform.parent != _initialParent)
        {
            if (_initialParent != null)
            {
                string currentParentName = transform.parent != null ? transform.parent.name : "null";
                Debug.Log($"[ScaleLock] {name} restoring parent from {currentParentName} to {_initialParent.name}");
                
                // 使用 worldPositionStays = true 保持世界位置
                transform.SetParent(_initialParent, true);
                
                Debug.Log($"[ScaleLock] {name} parent restored, new lossyScale: {transform.lossyScale}");
            }
            else
            {
                Debug.LogError($"[ScaleLock] {name} initial parent is null! Cannot restore parent.");
            }
        }
        
        // 修复：恢复 localScale
        transform.localScale = _lockedLocalScale;
        
        Debug.Log($"[ScaleLock] {name} after restore:");
        Debug.Log($"  parent: {(transform.parent != null ? transform.parent.name : "null")}");
        Debug.Log($"  localScale: {transform.localScale}");
        Debug.Log($"  lossyScale: {transform.lossyScale}");
        
        // 验证 lossyScale 是否正确
        float lossyDiff = Vector3.Distance(transform.lossyScale, _lockedLossyScale);
        if (lossyDiff > 0.01f)
        {
            Debug.LogError($"[ScaleLock] {name} lossyScale mismatch after restore! Expected {_lockedLossyScale}, got {transform.lossyScale}, diff={lossyDiff}");
        }
        else
        {
            Debug.Log($"[ScaleLock] {name} lossyScale verified correct!");
        }
    }
    
    /// <summary>
    /// 手动更新锁定的 scale（例如在需要缩放功能时调用）
    /// </summary>
    public void UpdateLockedScale(Vector3 newLocalScale, Vector3 newLossyScale)
    {
        _lockedLocalScale = newLocalScale;
        _lockedLossyScale = newLossyScale;
        transform.localScale = newLocalScale;
        Debug.Log($"[ScaleLock] {name} updated locked scale to local={newLocalScale}, lossy={newLossyScale}");
    }
    
    /// <summary>
    /// 获取当前锁定的 localScale
    /// </summary>
    public Vector3 GetLockedLocalScale()
    {
        return _lockedLocalScale;
    }
    
    /// <summary>
    /// 获取当前锁定的 lossyScale
    /// </summary>
    public Vector3 GetLockedLossyScale()
    {
        return _lockedLossyScale;
    }
}
