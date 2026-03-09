# Quest 零件缩放功能 - 实现方案与使用指南

## 📋 当前状态
**已禁用** - 零件的 scale 被锁定，不会在抓取时变化

## 🎯 功能说明

### 什么是"变大"功能
在抓取零件时，零件的 scale 会发生变化，可能是：
1. **视觉放大** - 让用户更清楚地看到零件细节
2. **操作放大** - 方便精细操作
3. **强调效果** - 突出显示当前抓取的零件

### 为什么会"变大"

#### 原因 1: 父级关系改变导致的 scale 变化
```
原始结构：
ImportedModel (scale = 0.5)
  └─ Part1 (localScale = 1.0, lossyScale = 0.5)

抓取后（retainTransformParent = false）：
World Root (scale = 1.0)
  └─ Part1 (localScale = 1.0, lossyScale = 1.0)  ← 看起来变大了！
```

**解释**:
- `localScale` 没变（仍然是 1.0）
- 但 `lossyScale`（世界空间缩放）从 0.5 变成了 1.0
- 视觉上看起来变大了 2 倍

#### 原因 2: XR Interaction Toolkit 的自动缩放
某些 XR Interaction 设置可能会自动调整物体的 scale：
- `useDynamicAttach = true` - 动态附加可能调整 scale
- `snapToColliderVolume = true` - 对齐到碰撞体可能调整 scale

#### 原因 3: 手动实现的缩放功能
可以通过监听抓取事件，手动放大零件：
```csharp
void OnGrabbed(SelectEnterEventArgs args)
{
    transform.localScale *= 1.5f;  // 放大 1.5 倍
}

void OnReleased(SelectExitEventArgs args)
{
    transform.localScale /= 1.5f;  // 恢复原始大小
}
```

## 🔧 如何实现缩放功能

### 方案 1: 简单的固定倍数缩放

**文件**: 创建新脚本 `GrabScaleEffect.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class GrabScaleEffect : MonoBehaviour
{
    [Header("缩放设置")]
    public float scaleMultiplier = 1.5f;  // 抓取时放大倍数
    public bool enableScaleEffect = true;  // 是否启用缩放效果
    
    private Vector3 _originalScale;
    private XRGrabInteractable _grabInteractable;
    
    void Awake()
    {
        _originalScale = transform.localScale;
        _grabInteractable = GetComponent<XRGrabInteractable>();
    }
    
    void OnEnable()
    {
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.AddListener(OnGrabbed);
            _grabInteractable.selectExited.AddListener(OnReleased);
        }
    }
    
    void OnDisable()
    {
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            _grabInteractable.selectExited.RemoveListener(OnReleased);
        }
    }
    
    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        if (enableScaleEffect)
        {
            transform.localScale = _originalScale * scaleMultiplier;
            Debug.Log($"[GrabScale] {name} scaled to {transform.localScale}");
        }
    }
    
    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (enableScaleEffect)
        {
            transform.localScale = _originalScale;
            Debug.Log($"[GrabScale] {name} restored to {_originalScale}");
        }
    }
}
```

**使用方法**:
1. 在 `XRGrabSetup.cs` 的 `Apply()` 方法中添加：
   ```csharp
   go.AddComponent<GrabScaleEffect>();
   ```
2. 在 Unity Inspector 中调整 `scaleMultiplier` 值

### 方案 2: 平滑的动画缩放

**文件**: 创建新脚本 `GrabScaleAnimated.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections;

[RequireComponent(typeof(XRGrabInteractable))]
public class GrabScaleAnimated : MonoBehaviour
{
    [Header("缩放设置")]
    public float targetScaleMultiplier = 1.5f;
    public float animationDuration = 0.2f;  // 动画时长
    public AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    
    private Vector3 _originalScale;
    private Coroutine _scaleCoroutine;
    
    void Awake()
    {
        _originalScale = transform.localScale;
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
    
    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(AnimateScale(_originalScale * targetScaleMultiplier));
    }
    
    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);
        _scaleCoroutine = StartCoroutine(AnimateScale(_originalScale));
    }
    
    IEnumerator AnimateScale(Vector3 targetScale)
    {
        Vector3 startScale = transform.localScale;
        float elapsed = 0f;
        
        while (elapsed < animationDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / animationDuration);
            float curveValue = scaleCurve.Evaluate(t);
            transform.localScale = Vector3.Lerp(startScale, targetScale, curveValue);
            yield return null;
        }
        
        transform.localScale = targetScale;
    }
}
```

### 方案 3: 基于距离的动态缩放

**文件**: 创建新脚本 `GrabScaleDistance.cs`

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
public class GrabScaleDistance : MonoBehaviour
{
    [Header("缩放设置")]
    public float minScale = 0.5f;
    public float maxScale = 2.0f;
    public float minDistance = 0.2f;  // 最近距离
    public float maxDistance = 2.0f;  // 最远距离
    
    private Vector3 _originalScale;
    private Transform _cameraTransform;
    private bool _isGrabbed = false;
    
    void Awake()
    {
        _originalScale = transform.localScale;
        _cameraTransform = Camera.main?.transform;
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
    
    void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
    {
        _isGrabbed = true;
    }
    
    void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
    {
        _isGrabbed = false;
        transform.localScale = _originalScale;
    }
    
    void Update()
    {
        if (!_isGrabbed || _cameraTransform == null) return;
        
        float distance = Vector3.Distance(transform.position, _cameraTransform.position);
        float t = Mathf.InverseLerp(minDistance, maxDistance, distance);
        float scaleMultiplier = Mathf.Lerp(maxScale, minScale, t);
        
        transform.localScale = _originalScale * scaleMultiplier;
    }
}
```

## 🔒 当前的 ScaleLock 实现

### 为什么需要 ScaleLock
- 防止意外的 scale 变化
- 确保零件大小一致
- 避免视觉混乱

### ScaleLock 的工作原理
```csharp
// 在 Awake 时锁定 scale
_lockedScale = transform.localScale;

// 在 LateUpdate 中强制恢复
if (transform.localScale != _lockedScale)
{
    transform.localScale = _lockedScale;
}
```

### 如何启用缩放功能

#### 步骤 1: 禁用 ScaleLock
在 `XRGrabSetup.cs` 中注释掉：
```csharp
// if (!go.TryGetComponent<ScaleLock>(out _))
// {
//     go.AddComponent<ScaleLock>();
// }
```

#### 步骤 2: 添加缩放效果组件
```csharp
// 添加你选择的缩放效果
go.AddComponent<GrabScaleEffect>();
// 或
go.AddComponent<GrabScaleAnimated>();
// 或
go.AddComponent<GrabScaleDistance>();
```

#### 步骤 3: 配置参数
在 Unity Inspector 中调整：
- `scaleMultiplier` - 缩放倍数
- `animationDuration` - 动画时长
- `enableScaleEffect` - 是否启用

## 📊 方案对比

| 方案 | 优点 | 缺点 | 适用场景 |
|------|------|------|----------|
| 固定倍数 | 简单、性能好 | 缺少动画，突兀 | 快速原型 |
| 平滑动画 | 视觉效果好 | 稍微复杂 | 正式产品 |
| 距离动态 | 自适应、智能 | 可能不稳定 | 特殊需求 |
| ScaleLock | 稳定、可靠 | 无缩放效果 | 当前使用 |

## 🎮 用户体验建议

### 推荐的缩放倍数
- **小零件** (< 5cm): 1.5x - 2.0x
- **中等零件** (5-20cm): 1.2x - 1.5x
- **大零件** (> 20cm): 1.0x - 1.2x

### 动画时长建议
- **快速** (0.1s): 响应迅速，但可能突兀
- **中等** (0.2s): 平衡，推荐
- **慢速** (0.3s+): 平滑，但可能感觉迟钝

### 何时使用缩放功能
- ✅ 需要查看零件细节时
- ✅ 进行精细操作时
- ✅ 强调当前选中的零件时
- ❌ 零件已经很大时
- ❌ 需要精确对齐时
- ❌ 多人协作时（可能造成混乱）

## 🔧 调试技巧

### 查看 scale 变化
```bash
adb logcat -s Unity | findstr "Scale\|scale"
```

### 检查 ScaleLock 是否工作
```bash
adb logcat -s Unity | findstr "ScaleLock"
```

期望看到：
```
[ScaleLock] Part1 locked scale at (1.00, 1.00, 1.00)
[ScaleLock] Part1 grabbed, enforcing scale (1.00, 1.00, 1.00)
```

### 测试缩放功能
1. 禁用 ScaleLock
2. 添加 GrabScaleEffect
3. 抓取零件
4. 观察 scale 变化

## 📝 代码位置

### 当前实现
- `Assets/Scripts/ScaleLock.cs` - Scale 锁定组件
- `Assets/Scripts/XRGrabSetup.cs` - 自动添加 ScaleLock

### 未来实现（需要时创建）
- `Assets/Scripts/GrabScaleEffect.cs` - 固定倍数缩放
- `Assets/Scripts/GrabScaleAnimated.cs` - 平滑动画缩放
- `Assets/Scripts/GrabScaleDistance.cs` - 距离动态缩放

## 🎯 快速启用缩放功能

如果将来需要启用缩放功能：

1. **打开** `Assets/Scripts/XRGrabSetup.cs`
2. **找到** 添加 ScaleLock 的代码（约第 100 行）
3. **注释掉** ScaleLock 相关代码
4. **添加** 你选择的缩放效果组件
5. **重新构建** APK

示例代码：
```csharp
// 禁用 ScaleLock
// if (!go.TryGetComponent<ScaleLock>(out _))
// {
//     go.AddComponent<ScaleLock>();
// }

// 启用缩放效果
if (!go.TryGetComponent<GrabScaleEffect>(out _))
{
    var scaleEffect = go.AddComponent<GrabScaleEffect>();
    scaleEffect.scaleMultiplier = 1.5f;  // 放大 1.5 倍
    scaleEffect.enableScaleEffect = true;
}
```

---

**文档版本**: v1.0
**最后更新**: 2026-03-09
**当前状态**: ScaleLock 已启用，缩放功能已禁用
