using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Scale 诊断工具 - 输出详细的 scale 信息
/// 临时添加到零件上用于调试
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ScaleDiagnostic : MonoBehaviour
{
    private Vector3 _initialLocalScale;
    private Vector3 _initialLossyScale;
    private Transform _initialParent;
    private bool _isGrabbed = false;
    
    void Awake()
    {
        _initialLocalScale = transform.localScale;
        _initialLossyScale = transform.lossyScale;
        _initialParent = transform.parent;
        
        Debug.Log($"[ScaleDiag] {name} INITIAL STATE:");
        Debug.Log($"  localScale: {_initialLocalScale}");
        Debug.Log($"  lossyScale: {_initialLossyScale}");
        Debug.Log($"  parent: {(_initialParent != null ? _initialParent.name : "null")}");
        Debug.Log($"  parent.localScale: {(_initialParent != null ? _initialParent.localScale.ToString() : "N/A")}");
        Debug.Log($"  parent.lossyScale: {(_initialParent != null ? _initialParent.lossyScale.ToString() : "N/A")}");
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
        Debug.Log($"[ScaleDiag] {name} GRABBED:");
        LogCurrentState();
    }
    
    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        _isGrabbed = false;
        Debug.Log($"[ScaleDiag] {name} RELEASED:");
        LogCurrentState();
    }
    
    void LateUpdate()
    {
        if (_isGrabbed)
        {
            // 检查是否有变化
            Vector3 currentLocal = transform.localScale;
            Vector3 currentLossy = transform.lossyScale;
            Transform currentParent = transform.parent;
            
            bool localChanged = Vector3.Distance(currentLocal, _initialLocalScale) > 0.001f;
            bool lossyChanged = Vector3.Distance(currentLossy, _initialLossyScale) > 0.001f;
            bool parentChanged = currentParent != _initialParent;
            
            if (localChanged || lossyChanged || parentChanged)
            {
                Debug.LogWarning($"[ScaleDiag] {name} CHANGE DETECTED:");
                if (localChanged)
                    Debug.LogWarning($"  localScale: {_initialLocalScale} → {currentLocal}");
                if (lossyChanged)
                    Debug.LogWarning($"  lossyScale: {_initialLossyScale} → {currentLossy}");
                if (parentChanged)
                    Debug.LogWarning($"  parent: {(_initialParent != null ? _initialParent.name : "null")} → {(currentParent != null ? currentParent.name : "null")}");
                
                LogCurrentState();
            }
        }
    }
    
    void LogCurrentState()
    {
        Debug.Log($"  localScale: {transform.localScale}");
        Debug.Log($"  lossyScale: {transform.lossyScale}");
        Debug.Log($"  parent: {(transform.parent != null ? transform.parent.name : "null")}");
        if (transform.parent != null)
        {
            Debug.Log($"  parent.localScale: {transform.parent.localScale}");
            Debug.Log($"  parent.lossyScale: {transform.parent.lossyScale}");
        }
        Debug.Log($"  position: {transform.position}");
        Debug.Log($"  localPosition: {transform.localPosition}");
    }
}
