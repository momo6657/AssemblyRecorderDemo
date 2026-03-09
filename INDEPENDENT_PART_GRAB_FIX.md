# Quest 零件独立抓取问题修复

## 🔴 问题描述
- 用手柄抓住一个零件时，所有零件都跟着移动
- 需要每个零件都可以单独抓取和移动
- 像手机端一样，每个零件彼此之间的移动不受限制

## 🔍 问题原因

### 1. retainTransformParent = true
- 这个设置让零件在抓取时保持父级关系
- 导致抓取一个零件时，整个父级对象（包含所有零件）都会移动

### 2. stabilizeParentDuringGrab = true
- 这个设置会添加 `XRStableParentDuringGrab` 组件
- 该组件会强制恢复父级关系，阻止零件独立移动

### 3. XRStableParentDuringGrab 组件的 RestoreOriginalParent()
- 在 `LateUpdate` 中持续调用 `RestoreOriginalParent()`
- 强制把零件的父级恢复到原始父级
- 阻止零件脱离父级独立移动

## ✅ 修复方案

### 修复 1: 禁用父级保持
**文件**: `Assets/Scripts/XRGrabSetup.cs`

```csharp
// 之前（错误）：
public bool retainTransformParent = true;  // ❌ 保持父级关系
public bool stabilizeParentDuringGrab = true;  // ❌ 稳定父级

// 现在（正确）：
public bool retainTransformParent = false;  // ✅ 允许零件独立移动
public bool stabilizeParentDuringGrab = false;  // ✅ 不需要稳定父级
```

### 修复 2: 修改 XRStableParentDuringGrab 组件
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `XRStableParentDuringGrab` 类

```csharp
void LateUpdate()
{
    if (_isSelected)
    {
        // 修复：不再强制恢复父级关系
        // RestoreOriginalParent();  // ← 注释掉
        
        // 只锁定 scale，不管父级关系
        if (transform.localScale != _originalLocalScale)
        {
            transform.localScale = _originalLocalScale;
        }
    }
}

void OnSelectEntered(SelectEnterEventArgs _)
{
    _isSelected = true;
    // 修复：不再恢复父级关系
    // RestoreOriginalParent();  // ← 注释掉
    transform.localScale = _originalLocalScale;
}

void OnSelectExited(SelectExitEventArgs _)
{
    _isSelected = false;
    // 修复：不再恢复父级关系
    // RestoreOriginalParent();  // ← 注释掉
    transform.localScale = _originalLocalScale;
}
```

## 📝 修改的文件

1. ✅ `Assets/Scripts/XRGrabSetup.cs`
   - `retainTransformParent = false`
   - `stabilizeParentDuringGrab = false`
   - 注释掉 `XRStableParentDuringGrab` 中的 `RestoreOriginalParent()` 调用

## 🎯 工作原理

### 之前的行为
```
模型结构：
ImportedModel (root)
  ├─ Part1
  ├─ Part2
  └─ Part3

抓取 Part1 时：
1. XRGrabInteractable 尝试移动 Part1
2. retainTransformParent=true 保持 Part1 的父级为 ImportedModel
3. XRStableParentDuringGrab 强制恢复父级关系
4. 结果：Part1 无法脱离 ImportedModel，整个模型一起移动 ❌
```

### 现在的行为
```
模型结构：
ImportedModel (root)
  ├─ Part1
  ├─ Part2
  └─ Part3

抓取 Part1 时：
1. XRGrabInteractable 移动 Part1
2. retainTransformParent=false 允许 Part1 改变父级
3. XRStableParentDuringGrab 不再强制恢复父级
4. 结果：Part1 可以独立移动，其他零件不受影响 ✅
```

## 🧪 验证步骤

### 1. 重新构建 Quest APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 2. 测试零件独立抓取
1. 在 Quest 中加载模型
2. 用手柄抓取 Part1
3. **只有 Part1 应该移动，其他零件保持不动** ✅
4. 释放 Part1，抓取 Part2
5. **只有 Part2 应该移动** ✅
6. 同时抓取多个零件（双手）
7. **每个零件应该独立移动** ✅

### 3. 查看日志
```bash
adb logcat -s Unity | findstr "XRGrabSetup"
```

期望看到：
```
[XRGrabSetup] Applied to parts=5, retainTransformParent=False, stabilizeParentDuringGrab=False
```

## 🔧 其他相关设置

### CollectParts() 方法
确保正确识别每个零件：
- `useTopLevelChildAsPart = true` - 使用顶层子对象作为零件
- 每个零件都会获得独立的 `XRGrabInteractable` 组件
- 每个零件都有自己的 collider 和 rigidbody

### Rigidbody 设置
```csharp
body.useGravity = false;  // 不受重力影响
body.isKinematic = true;  // 运动学模式，不受物理引擎影响
```

### XRGrabInteractable 设置
```csharp
grab.movementType = Kinematic;  // 运动学移动
grab.retainTransformParent = false;  // ✅ 关键：允许改变父级
grab.matchAttachPosition = true;  // 匹配抓取位置
grab.matchAttachRotation = true;  // 匹配抓取旋转
```

## ⚠️ 注意事项

### 1. 零件可能会"飞走"
- 由于不再保持父级关系，零件可以移动到任意位置
- 这是正常的，符合需求

### 2. 零件之间没有碰撞
- 如果需要零件之间的碰撞检测，需要额外配置
- 当前设置下，零件可以互相穿透

### 3. 录制系统需要适配
- `QuestStepSession` 需要能够记录零件的独立移动
- `ModelIndex` 需要正确跟踪每个零件的位置

## 🎉 预期结果

修复后，用户体验应该是：
1. ✅ 抓取一个零件时，只有该零件移动
2. ✅ 其他零件保持在原位
3. ✅ 可以同时抓取多个零件（双手）
4. ✅ 每个零件可以移动到任意位置
5. ✅ 零件的 scale 保持正确（不会缩小）
6. ✅ 释放后零件保持在新位置

## 🔄 与手机端的对比

### 手机端
- 使用 `SelectionManager` 选择零件
- 通过触摸和拖拽移动零件
- 每个零件独立移动

### Quest 端（修复后）
- 使用 `XRGrabInteractable` 抓取零件
- 通过手柄移动零件
- 每个零件独立移动 ✅ 现在一致了

---

**修复版本**: V4.0
**最后更新**: 2026-03-09
**状态**: 等待测试验证
