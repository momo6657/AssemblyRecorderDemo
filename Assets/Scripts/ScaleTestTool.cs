using UnityEngine;

/// <summary>
/// 缩放测试工具 - 用于验证缩放保护机制是否正常工作
/// 在 Unity Editor 中运行，模拟缩放变化并观察是否被自动修正
/// </summary>
public class ScaleTestTool : MonoBehaviour
{
    [Header("测试设置")]
    [Tooltip("要测试的目标物体（留空则自动查找第一个 XRGrabInteractable）")]
    public Transform targetTransform;
    
    [Tooltip("测试缩放值")]
    public Vector3 testScale = new Vector3(0.5f, 0.5f, 0.5f);
    
    [Tooltip("自动测试间隔（秒）")]
    public float autoTestInterval = 2f;
    
    [Tooltip("是否启用自动测试")]
    public bool enableAutoTest = false;
    
    private float _nextTestTime;
    private Vector3 _originalScale;
    
    void Start()
    {
        if (targetTransform == null)
        {
            var grabInteractable = FindFirstObjectByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grabInteractable != null)
            {
                targetTransform = grabInteractable.transform;
                Debug.Log($"[ScaleTest] Auto-found target: {targetTransform.name}");
            }
        }
        
        if (targetTransform != null)
        {
            _originalScale = targetTransform.localScale;
            Debug.Log($"[ScaleTest] Original scale: {_originalScale}");
        }
        
        _nextTestTime = Time.time + autoTestInterval;
    }
    
    void Update()
    {
        if (!enableAutoTest) return;
        if (targetTransform == null) return;
        if (Time.time < _nextTestTime) return;
        
        _nextTestTime = Time.time + autoTestInterval;
        TestScaleChange();
    }
    
    [ContextMenu("Test Scale Change")]
    public void TestScaleChange()
    {
        if (targetTransform == null)
        {
            Debug.LogError("[ScaleTest] No target transform set!");
            return;
        }
        
        Vector3 beforeScale = targetTransform.localScale;
        Debug.Log($"[ScaleTest] Before: {beforeScale}");
        
        // 强制修改 scale
        targetTransform.localScale = testScale;
        Debug.Log($"[ScaleTest] Changed to: {testScale}");
        
        // 等待一帧后检查是否被恢复
        StartCoroutine(CheckScaleAfterFrame(beforeScale));
    }
    
    System.Collections.IEnumerator CheckScaleAfterFrame(Vector3 expectedScale)
    {
        yield return new WaitForEndOfFrame();
        
        Vector3 afterScale = targetTransform.localScale;
        Debug.Log($"[ScaleTest] After frame: {afterScale}");
        
        float diff = Vector3.Distance(afterScale, expectedScale);
        if (diff < 0.001f)
        {
            Debug.Log($"<color=green>[ScaleTest] ✓ Scale protection working! Scale restored to {afterScale}</color>");
        }
        else
        {
            Debug.LogWarning($"<color=red>[ScaleTest] ✗ Scale protection NOT working! Scale is {afterScale}, expected {expectedScale}</color>");
        }
    }
    
    [ContextMenu("Reset Scale")]
    public void ResetScale()
    {
        if (targetTransform == null)
        {
            Debug.LogError("[ScaleTest] No target transform set!");
            return;
        }
        
        targetTransform.localScale = _originalScale;
        Debug.Log($"[ScaleTest] Reset scale to {_originalScale}");
    }
    
    [ContextMenu("Print Current Scale")]
    public void PrintCurrentScale()
    {
        if (targetTransform == null)
        {
            Debug.LogError("[ScaleTest] No target transform set!");
            return;
        }
        
        Debug.Log($"[ScaleTest] Current scale: local={targetTransform.localScale}, world={targetTransform.lossyScale}");
    }
    
    [ContextMenu("Find All Grab Interactables")]
    public void FindAllGrabInteractables()
    {
        var interactables = FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
        
        Debug.Log($"[ScaleTest] Found {interactables.Length} grab interactables:");
        foreach (var interactable in interactables)
        {
            Debug.Log($"  - {interactable.name}: scale={interactable.transform.localScale}");
        }
    }
}
