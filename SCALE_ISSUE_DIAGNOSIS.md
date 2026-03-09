# Quest 零件变大问题 - 完整排查方案

## 🔴 问题现象
抓握零件后，零件变得特别大

## 🔍 可能的原因分析

### 原因 1: 父级缩放导致的视觉变化（最可能）

#### 场景
```
ImportedModel (scale = 0.5)
  └─ Part1 (localScale = 1.0, lossyScale = 0.5)

抓取后（retainTransformParent = false）：
XR Origin (scale = 1.0)
  └─ Part1 (localScale = 1.0, lossyScale = 1.0)  ← 视觉上变大了 2 倍！
```

#### 解释
- `localScale` 保持不变（1.0）
- 但父级从 `ImportedModel (0.5)` 变成了 `XR Origin (1.0)`
- `lossyScale` = `localScale` × `parent.lossyScale`
- 所以 `lossyScale` 从 0.5 变成了 1.0
- **视觉上看起来变大了 2 倍**

#### 解决方案
在抓取时，调整 `localScale` 来补偿父级的变化：
```csharp
void OnGrabbed()
{
    // 计算需要的 localScale 来保持相同的 lossyScale
    Vector3 targetLossyScale = _initialLossyScale;
    Vector3 newParentLossyScale = transform.parent.lossyScale;
    
    // localScale = targetLossyScale / parentLossyScale
    Vector3 newLocalScale = new Vector3(
        targetLossyScale.x / newParentLossyScale.x,
        targetLossyScale.y / newParentLossyScale.y,
        targetLossyScale.z / newParentLossyScale.z
    );
    
    transform.localScale = newLocalScale;
}
```

### 原因 2: 使用了旧的 APK

#### 证据
日志显示：`useDynamicAttach=True`

但代码中已经设置为：`useDynamicAttach = false`

#### 解决方案
1. 卸载旧 APK：
   ```bash
   adb uninstall com.YourCompany.AssemblyRecorder
   ```
2. 重新构建并安装

### 原因 3: XR Interaction Toolkit 的自动调整

#### 可能的设置
- `useDynamicAttach = true` - 动态附加
- `snapToColliderVolume = true` - 对齐到碰撞体
- `matchAttachPosition/Rotation` - 匹配附加点

#### 解决方案
确保所有设置正确：
```csharp
grab.useDynamicAttach = false;
grab.snapToColliderVolume = false;
grab.trackScale = false;
```

### 原因 4: 模型本身的缩放问题

#### 检查
模型导入时可能被缩放了

#### 解决方案
检查 `ImportManager` 中的缩放逻辑

## ✅ 完整修复方案

### 修复 1: 改进 ScaleLock，补偿父级变化

**文件**: `Assets/Scripts/ScaleLock.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class ScaleLock : MonoBehaviour
{
    private Vector3 _lockedLocalScale;
    private Vector3 _lockedLossyScale;  // 新增：锁定世界空间缩放
    private Transform _initialParent;
    private bool _isGrabbed = false;
    
    void Awake()
    {
        _lockedLocalScale = transform.localScale;
        _lockedLossyScale = transform.lossyScale;  // 记录初始的世界缩放
        _initialParent = transform.parent;
        
        Debug.Log($"[ScaleLock] {name} locked scale:");
        Debug.Log($"  localScale: {_lockedLocalScale}");
        Debug.Log($"  lossyScale: {_lockedLossyScale}");
        Debug.Log($"  parent: {(_initialParent != null ? _initialParent.name : "null")}");
    }
    
    void LateUpdate()
    {
        if (_isGrabbed)
        {
            // 修复：保持世界空间缩放不变
            Vector3 targetLossyScale = _lockedLossyScale;
            Vector3 currentLossyScale = transform.lossyScale;
            
            // 如果世界缩放变化了，调整 localScale 来补偿
            if (Vector3.Distance(currentLossyScale, targetLossyScale) > 0.001f)
            {
                Vector3 parentLossyScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
                
                // 计算需要的 localScale
                Vector3 newLocalScale = new Vector3(
                    Mathf.Abs(parentLossyScale.x) > 0.001f ? targetLossyScale.x / parentLossyScale.x : _lockedLocalScale.x,
                    Mathf.Abs(parentLossyScale.y) > 0.001f ? targetLossyScale.y / parentLossyScale.y : _lockedLocalScale.y,
                    Mathf.Abs(parentLossyScale.z) > 0.001f ? targetLossyScale.z / parentLossyScale.z : _lockedLocalScale.z
                );
                
                Debug.LogWarning($"[ScaleLock] {name} adjusting localScale to maintain lossyScale:");
                Debug.LogWarning($"  currentLossyScale: {currentLossyScale}");
                Debug.LogWarning($"  targetLossyScale: {targetLossyScale}");
                Debug.LogWarning($"  parentLossyScale: {parentLossyScale}");
                Debug.LogWarning($"  newLocalScale: {newLocalScale}");
                
                transform.localScale = newLocalScale;
            }
        }
        else
        {
            // 未抓取时，只锁定 localScale
            if (transform.localScale != _lockedLocalScale)
            {
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
    }
    
    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        _isGrabbed = false;
        
        // 释放时恢复到初始父级和 localScale
        if (transform.parent != _initialParent && _initialParent != null)
        {
            transform.SetParent(_initialParent, true);
        }
        
        transform.localScale = _lockedLocalScale;
        Debug.Log($"[ScaleLock] {name} released, restored localScale to {_lockedLocalScale}");
    }
    
    public void UpdateLockedScale(Vector3 newLocalScale, Vector3 newLossyScale)
    {
        _lockedLocalScale = newLocalScale;
        _lockedLossyScale = newLossyScale;
        transform.localScale = newLocalScale;
        Debug.Log($"[ScaleLock] {name} updated locked scale to local={newLocalScale}, lossy={newLossyScale}");
    }
}
```

### 修复 2: 确保配置正确

**文件**: `Assets/Scripts/XRGrabSetup.cs`

确认以下设置：
```csharp
public bool useDynamicAttach = false;
public bool retainTransformParent = false;
public bool stabilizeParentDuringGrab = false;
public bool trackScale = false;
```

### 修复 3: 添加诊断工具

**文件**: `Assets/Scripts/ScaleDiagnostic.cs` (已创建)

临时添加到零件上，输出详细的 scale 信息。

## 🧪 诊断步骤

### 步骤 1: 卸载旧 APK
```bash
adb uninstall com.YourCompany.AssemblyRecorder
```

### 步骤 2: 重新构建
```
Unity -> File -> Build Settings -> Build and Run
```

### 步骤 3: 查看诊断日志
```bash
adb logcat -s Unity | findstr "ScaleDiag"
```

**期望看到**:
```
[ScaleDiag] Part1 INITIAL STATE:
  localScale: (1.00, 1.00, 1.00)
  lossyScale: (0.50, 0.50, 0.50)  ← 注意这个值
  parent: ImportedModel
  parent.lossyScale: (0.50, 0.50, 0.50)

[ScaleDiag] Part1 GRABBED:
  localScale: (1.00, 1.00, 1.00)
  lossyScale: (1.00, 1.00, 1.00)  ← 变大了！
  parent: XR Origin
  parent.lossyScale: (1.00, 1.00, 1.00)

[ScaleDiag] Part1 CHANGE DETECTED:
  lossyScale: (0.50, 0.50, 0.50) → (1.00, 1.00, 1.00)
  parent: ImportedModel → XR Origin
```

### 步骤 4: 确认问题类型

如果看到 `lossyScale` 变化，说明是**父级缩放问题**（原因 1）

如果看到 `localScale` 变化，说明是**scale 被修改**（原因 3）

如果日志显示 `useDynamicAttach=True`，说明是**旧 APK**（原因 2）

## 📝 修改的文件

1. ✅ `Assets/Scripts/ScaleLock.cs` - 改进，补偿父级变化
2. ✅ `Assets/Scripts/ScaleDiagnostic.cs` - 新增，诊断工具
3. ✅ `Assets/Scripts/XRGrabSetup.cs` - 添加 ScaleDiagnostic

## 🎯 预期结果

修复后：
1. ✅ 抓取零件时，视觉大小保持不变
2. ✅ `lossyScale` 保持不变
3. ✅ `localScale` 会自动调整来补偿父级变化
4. ✅ 释放后恢复到原始状态

## 📞 如果问题仍然存在

请提供完整的诊断日志：
```bash
adb logcat -s Unity > quest_full_log.txt
```

然后搜索：
- `[ScaleDiag]` - 诊断信息
- `[ScaleLock]` - Scale 锁定信息
- `[XRGrabSetup] Applied` - 配置信息

---

**最后更新**: 2026-03-09 22:00
**状态**: 等待诊断结果
