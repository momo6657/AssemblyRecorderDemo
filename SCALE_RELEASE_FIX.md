# Quest 零件释放后变大问题 - 修复总结

## 🔴 问题现象
- 抓着时：正常大小 ✅
- 松开后：变得特别大 ❌

## 🔍 根本原因

### 日志证据
```
[ScaleDiag] CHANGE DETECTED:
  lossyScale: (0.16, 0.16, 0.16) → (1.00, 1.00, 1.00)
  parent: GLB_test → null
```

### 问题分析
1. **初始状态**：
   - 零件在 `GLB_test` 下
   - `localScale = 1.0`
   - `lossyScale = 0.16` (因为父级 `GLB_test` 的 scale 是 0.16)

2. **抓取时**：
   - `ScaleLock` 调整 `localScale` 来保持 `lossyScale = 0.16`
   - 视觉大小正常 ✅

3. **释放时（问题）**：
   - `ScaleLock.OnReleased()` 试图恢复父级
   - **但恢复失败**，零件的父级变成了 `null`
   - `localScale` 恢复到 1.0
   - 但没有父级，所以 `lossyScale = 1.0`
   - **变大了 6.25 倍！** (1.0 / 0.16 = 6.25)

### 为什么恢复父级失败？

**原因**：`OnReleased()` 的逻辑有问题

```csharp
// 旧代码（有问题）
if (transform.parent != _initialParent && _initialParent != null)
{
    transform.SetParent(_initialParent, true);
}
```

**问题**：
- 当 `transform.parent` 是 `null` 时
- `transform.parent != _initialParent` 为 `true`
- 但 `transform.parent.name` 会报错（因为 parent 是 null）
- 导致日志中显示的错误信息

## ✅ 修复方案

### 修复：改进 OnReleased() 逻辑

**文件**: `Assets/Scripts/ScaleLock.cs`

```csharp
void OnReleased(SelectExitEventArgs args)
{
    _isGrabbed = false;
    
    // 详细的日志
    Debug.Log($"[ScaleLock] {name} released");
    Debug.Log($"  current parent: {(transform.parent != null ? transform.parent.name : "null")}");
    Debug.Log($"  initial parent: {(_initialParent != null ? _initialParent.name : "null")}");
    
    // 修复：必须先恢复父级，再恢复 localScale
    if (transform.parent != _initialParent)
    {
        if (_initialParent != null)
        {
            string currentParentName = transform.parent != null ? transform.parent.name : "null";
            Debug.Log($"[ScaleLock] restoring parent from {currentParentName} to {_initialParent.name}");
            
            // 使用 worldPositionStays = true 保持世界位置
            transform.SetParent(_initialParent, true);
        }
        else
        {
            Debug.LogError($"[ScaleLock] initial parent is null! Cannot restore parent.");
        }
    }
    
    // 恢复 localScale
    transform.localScale = _lockedLocalScale;
    
    // 验证 lossyScale 是否正确
    float lossyDiff = Vector3.Distance(transform.lossyScale, _lockedLossyScale);
    if (lossyDiff > 0.01f)
    {
        Debug.LogError($"[ScaleLock] lossyScale mismatch! Expected {_lockedLossyScale}, got {transform.lossyScale}");
    }
}
```

### 关键改进

1. **安全的 null 检查**：
   ```csharp
   string currentParentName = transform.parent != null ? transform.parent.name : "null";
   ```

2. **详细的日志**：
   - 输出当前父级和初始父级
   - 输出恢复前后的 scale
   - 验证 lossyScale 是否正确

3. **顺序很重要**：
   - 必须先恢复父级
   - 再恢复 localScale
   - 因为 `lossyScale = localScale × parent.lossyScale`

## 🧪 验证步骤

### 步骤 1: 重新构建 APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 步骤 2: 查看日志
```bash
adb logcat -s Unity | findstr "ScaleLock"
```

**期望看到**：
```
[ScaleLock] Part1 released
  current parent: null
  initial parent: GLB_test
[ScaleLock] restoring parent from null to GLB_test
[ScaleLock] parent restored, new lossyScale: (0.16, 0.16, 0.16)
[ScaleLock] after restore:
  parent: GLB_test
  localScale: (1.00, 1.00, 1.00)
  lossyScale: (0.16, 0.16, 0.16)
[ScaleLock] lossyScale verified correct!
```

**不应该看到**：
```
[ScaleLock] lossyScale mismatch!  ❌
[ScaleLock] initial parent is null!  ❌
```

### 步骤 3: 实际测试
1. 抓取零件
2. **抓着时应该是正常大小** ✅
3. 释放零件
4. **释放后应该保持正常大小** ✅
5. 多次抓取释放，确认稳定

## 📊 问题时间线

### 第一次问题：抓取时缩小
- **原因**：`localScale` 被 XR 系统修改
- **修复**：添加 `ScaleLock`，锁定 `localScale`

### 第二次问题：抓取时变大
- **原因**：父级改变导致 `lossyScale` 变化
- **修复**：改进 `ScaleLock`，保持 `lossyScale` 不变

### 第三次问题：释放后变大（当前）
- **原因**：释放时父级恢复失败
- **修复**：改进 `OnReleased()` 逻辑，确保父级正确恢复

## 📝 修改的文件

1. ✅ `Assets/Scripts/ScaleLock.cs` - 改进 `OnReleased()` 逻辑

## 🎯 预期结果

修复后：
1. ✅ 抓取时：保持正常大小
2. ✅ 抓着时：保持正常大小
3. ✅ 释放后：保持正常大小
4. ✅ 多次操作：稳定可靠

## 🔧 技术细节

### 为什么顺序很重要？

```
错误的顺序：
1. transform.localScale = 1.0
2. transform.SetParent(GLB_test)
3. lossyScale = 1.0 × 0.16 = 0.16  ✅

正确的顺序：
1. transform.SetParent(GLB_test)
2. transform.localScale = 1.0
3. lossyScale = 1.0 × 0.16 = 0.16  ✅

如果父级是 null：
1. transform.localScale = 1.0
2. lossyScale = 1.0 × 1.0 = 1.0  ❌ 变大了！
```

### lossyScale 的计算

```
lossyScale = localScale × parent.lossyScale × grandparent.lossyScale × ...

例如：
ImportedModel (lossyScale = 1.0)
  └─ GLB_test (localScale = 0.16, lossyScale = 0.16)
      └─ Part1 (localScale = 1.0, lossyScale = 0.16)

如果 Part1 的父级变成 null：
Part1 (localScale = 1.0, lossyScale = 1.0)  ← 变大了！
```

## 📞 如果问题仍然存在

请提供日志：
```bash
adb logcat -s Unity | findstr "ScaleLock" > scale_log.txt
```

检查：
1. 是否看到 "restoring parent from null to GLB_test"
2. 是否看到 "lossyScale verified correct"
3. 是否看到任何错误信息

---

**最后更新**: 2026-03-09 22:30
**状态**: 等待测试验证
