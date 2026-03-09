# Quest 手柄抓取模型缩小问题修复报告

## 问题描述
在 Quest 中导入模型后，使用手柄抓取模型时，模型会异常缩小。

## 根本原因分析

### 1. BoxCollider 尺寸计算错误（主要原因）
**位置**: `XRGrabSetup.cs` -> `AddFittedBoxCollider()` 方法

**问题**:
- 使用 `lossyScale`（世界空间缩放）来计算 BoxCollider 的 size
- 当 XR Interaction Toolkit 在抓取时改变物体的父级关系时，`lossyScale` 会发生变化
- 这导致 BoxCollider 的 size 计算错误，进而影响物体的视觉表现

**原代码**:
```csharp
var lossy = part.lossyScale;
float sx = Mathf.Abs(lossy.x) > 1e-5f ? Mathf.Abs(lossy.x) : 1f;
float sy = Mathf.Abs(lossy.y) > 1e-5f ? Mathf.Abs(lossy.y) : 1f;
float sz = Mathf.Abs(lossy.z) > 1e-5f ? Mathf.Abs(lossy.z) : 1f;
box.size = new Vector3(b.size.x / sx, b.size.y / sy, b.size.z / sz);
```

**修复方案**:
- 改用 `localScale` 替代 `lossyScale`
- 添加异常值保护，防止计算出过小或过大的 size
- 确保在局部空间中正确计算 collider 尺寸

### 2. 抓取过程中缩放值未锁定
**位置**: `XRGrabSetup.cs` -> `XRStableParentDuringGrab` 类

**问题**:
- 在抓取过程中，父级关系变化可能导致 `localScale` 被意外修改
- 没有机制保护和恢复原始的 scale 值

**修复方案**:
- 在 `OnSelectEntered` 时缓存初始的 `localScale`
- 在 `LateUpdate` 中持续检查并恢复正确的 scale
- 在 `OnSelectExited` 时确保 scale 被正确恢复

### 3. XRGrabSetup.Apply() 过程中的缩放保护
**位置**: `XRGrabSetup.cs` -> `Apply()` 方法

**问题**:
- 在添加 collider、rigidbody 等组件的过程中，可能触发 Unity 的内部重计算
- 没有验证机制确保这些操作不会影响物体的 scale

**修复方案**:
- 在 Apply 开始时缓存所有部件的初始 `localScale`
- 在 Apply 结束时验证并恢复任何被意外修改的 scale

## 修改的文件

### 1. XRGrabSetup.cs
- ✅ 修复 `AddFittedBoxCollider()`: 使用 `localScale` 替代 `lossyScale`
- ✅ 添加异常值保护，防止 collider size 计算错误
- ✅ 增强 `XRStableParentDuringGrab`: 添加 scale 缓存和恢复机制
- ✅ 改进 `Apply()`: 添加初始 scale 保护

### 2. ImportManager.cs
- ✅ 在 `TryApplyXrGrab()` 中添加日志，确认 ModelIndex 在 XRGrabSetup 之前构建

## 技术细节

### lossyScale vs localScale
- `lossyScale`: 世界空间的累积缩放，受整个父级层次结构影响
- `localScale`: 相对于直接父级的局部缩放
- 在父级可能变化的场景中（如 XR 抓取），应该使用 `localScale`

### SetParent 的 worldPositionStays 参数
- 使用 `SetParent(parent, true)` 保持世界空间位置和缩放
- 这确保在父级变化时，物体的视觉表现保持一致

## 测试建议

1. **基础抓取测试**
   - 导入模型到 Quest
   - 使用手柄抓取模型
   - 验证模型尺寸保持不变

2. **多次抓取测试**
   - 反复抓取和释放同一个模型
   - 验证每次操作后模型尺寸一致

3. **多部件测试**
   - 测试包含多个子部件的复杂模型
   - 分别抓取不同部件
   - 验证所有部件的尺寸都正确

4. **边界情况测试**
   - 测试非常小的模型（< 0.1m）
   - 测试非常大的模型（> 10m）
   - 测试带有非均匀缩放的模型

## 预期效果

修复后，在 Quest 中：
- ✅ 抓取模型时尺寸保持不变
- ✅ 释放模型后尺寸正确
- ✅ 多次抓取操作不会累积缩放错误
- ✅ 复杂模型的所有部件都能正确抓取

## 相关 Commit
- 修复前的问题可能在 commit f3832d8a 之后引入
- 本次修复针对 XR 抓取系统的缩放稳定性

## 注意事项

1. 这些修改主要影响 Quest（XR）环境，对手机端影响最小
2. 如果仍有问题，检查 Unity XR Interaction Toolkit 的版本兼容性
3. 确保场景中的 XR Origin 和 Camera 设置正确
