# Quest 零件独立抓取问题 - 完整修复方案

## 🔴 问题现象
1. 抓住一个零件时，所有零件都跟着移动
2. 抓住后产生极大位置偏移
3. 模型会变大
4. 日志显示：`Applied to parts=1` - 只识别到 1 个零件

## 🔍 根本原因分析

### 问题 1: 零件识别错误
**日志证据**:
```
[XRGrabSetup] Applied to parts=1
```

**原因**:
```
模型结构：
ImportedModel (modelRoot)
  └─ GLB_test (唯一的直接子对象)
      ├─ Part1
      ├─ Part2
      └─ Part3

旧的 CollectParts() 逻辑：
if (useTopLevelChildAsPart) {
    // 只收集 modelRoot 的直接子对象
    // 结果：只找到 GLB_test 这一个"零件"
}
```

**后果**: 整个 `GLB_test`（包含所有零件）被当作一个可抓取对象，所以抓取时所有零件一起移动。

### 问题 2: 配置未生效
**日志证据**:
```
retainTransformParent=True, stabilizeParentDuringGrab=True
```

**原因**: 使用了旧的 APK，没有包含最新的修改。

### 问题 3: 位置偏移和缩放问题
**原因**: 
- `useDynamicAttach = true` 导致 XR 系统动态调整 transform
- `retainTransformParent = true` 导致父级关系混乱
- 缺少 scale 保护机制

## ✅ 完整修复方案

### 修复 1: 改进零件识别逻辑
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `CollectParts()` 方法

**之前（错误）**:
```csharp
List<Transform> CollectParts()
{
    if (useTopLevelChildAsPart) {
        // 只收集 modelRoot 的直接子对象
        for (int i = 0; i < modelRoot.childCount; i++) {
            var child = modelRoot.GetChild(i);
            parts.Add(child);  // ❌ 只找到 GLB_test
        }
    }
}
```

**现在（正确）**:
```csharp
List<Transform> CollectParts()
{
    // 找到所有有 Renderer 的 GameObject
    var renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
    
    foreach (var r in renderers) {
        Transform part = r.transform;
        // 直接使用 renderer 所在的 GameObject 作为零件
        parts.Add(part);  // ✅ 找到所有零件：Part1, Part2, Part3...
    }
}
```

### 修复 2: 禁用父级保持
**文件**: `Assets/Scripts/XRGrabSetup.cs`

```csharp
public bool retainTransformParent = false;  // ✅ 允许零件独立移动
public bool stabilizeParentDuringGrab = false;  // ✅ 不需要稳定父级
public bool useDynamicAttach = false;  // ✅ 避免动态附加
public bool trackScale = false;  // ✅ 禁止跟踪缩放
```

### 修复 3: 修改 XRStableParentDuringGrab
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `XRStableParentDuringGrab` 类

```csharp
void LateUpdate()
{
    if (_isSelected) {
        // 注释掉父级恢复
        // RestoreOriginalParent();  // ← 不再强制恢复父级
        
        // 只锁定 scale
        if (transform.localScale != _originalLocalScale) {
            transform.localScale = _originalLocalScale;
        }
    }
}
```

### 修复 4: 在 Awake 时缓存原始 scale
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `XRStableParentDuringGrab` 类

```csharp
void Awake()
{
    // ✅ 在初始化时就缓存原始 scale
    _originalLocalScale = transform.localScale;
}

void OnSelectEntered(SelectEnterEventArgs _)
{
    // ✅ 不再重新缓存，使用 Awake 时的值
    transform.localScale = _originalLocalScale;
}
```

## 📝 修改的文件清单

1. ✅ `Assets/Scripts/XRGrabSetup.cs`
   - 修改 `CollectParts()` - 改进零件识别逻辑
   - 修改配置参数 - `retainTransformParent = false` 等
   - 修改 `XRStableParentDuringGrab` - 注释掉父级恢复，在 Awake 缓存 scale

## 🧪 验证步骤

### 步骤 1: 确认使用最新代码
```bash
# 检查 XRGrabSetup.cs 的修改时间
ls -l Assets/Scripts/XRGrabSetup.cs
```

### 步骤 2: 完全重新构建
```
1. Unity -> File -> Build Settings
2. 点击 "Clean Build" 或删除旧的构建文件
3. Build and Run
```

### 步骤 3: 卸载旧 APK
```bash
adb uninstall com.YourCompany.AssemblyRecorder
```

### 步骤 4: 安装新 APK
在 Unity 中 Build and Run，或手动安装

### 步骤 5: 查看日志
```bash
adb logcat -s Unity | findstr "XRGrabSetup"
```

**期望看到**:
```
[XRGrabSetup] Found 5 renderers under ImportedModel
[XRGrabSetup] Added part: Part1 (path: GLB_test/Part1)
[XRGrabSetup] Added part: Part2 (path: GLB_test/Part2)
[XRGrabSetup] Added part: Part3 (path: GLB_test/Part3)
...
[XRGrabSetup] Total parts collected: 5
[XRGrabSetup] Applied to parts=5, retainTransformParent=False, stabilizeParentDuringGrab=False
```

**不应该看到**:
```
Applied to parts=1  ❌ 错误！
retainTransformParent=True  ❌ 错误！
```

### 步骤 6: 实际测试
1. 在 Quest 中加载模型
2. 用手柄抓取 Part1
3. **只有 Part1 应该移动** ✅
4. **Part1 不应该缩小或变大** ✅
5. **Part1 不应该产生位置偏移** ✅
6. 释放 Part1，抓取 Part2
7. **只有 Part2 应该移动** ✅

## 🎯 工作原理对比

### 之前（错误）
```
模型结构：
ImportedModel
  └─ GLB_test (被识别为唯一的"零件")
      ├─ Part1
      ├─ Part2
      └─ Part3

抓取 Part1 时：
1. 实际抓取的是 GLB_test
2. GLB_test 包含所有零件
3. 结果：所有零件一起移动 ❌
```

### 现在（正确）
```
模型结构：
ImportedModel
  └─ GLB_test
      ├─ Part1 (独立的可抓取零件)
      ├─ Part2 (独立的可抓取零件)
      └─ Part3 (独立的可抓取零件)

抓取 Part1 时：
1. 只抓取 Part1
2. Part1 可以独立移动
3. 结果：只有 Part1 移动 ✅
```

## ⚠️ 重要提示

### 1. 必须卸载旧 APK
```bash
adb uninstall com.YourCompany.AssemblyRecorder
```
否则可能会保留旧的配置和代码。

### 2. 检查日志确认
日志中必须看到：
- `Total parts collected: N` (N > 1)
- `retainTransformParent=False`
- `stabilizeParentDuringGrab=False`

### 3. 模型结构要求
模型必须有多个独立的 Renderer 组件，每个 Renderer 对应一个零件。

### 4. 与手机端的对比
- **手机端**: 通过 `SelectionManager` 选择 + `TransformPanel` 按钮移动
- **Quest 端**: 通过 `XRGrabInteractable` 直接抓取移动
- **共同点**: 都是直接操作每个零件的 `transform`，彼此独立

## 🔧 调试技巧

### 如果还是只识别到 1 个零件
1. 检查模型结构：
   ```bash
   adb logcat -s Unity | findstr "Found.*renderers"
   ```
2. 如果显示 `Found 1 renderers`，说明模型本身只有一个 mesh
3. 需要在建模软件中将模型拆分成多个独立的 mesh

### 如果零件还是一起移动
1. 检查配置：
   ```bash
   adb logcat -s Unity | findstr "retainTransformParent"
   ```
2. 如果显示 `True`，说明使用了旧 APK
3. 必须卸载重装

### 如果有位置偏移
1. 检查 `useDynamicAttach`:
   ```bash
   adb logcat -s Unity | findstr "useDynamicAttach"
   ```
2. 应该显示 `False`

### 如果有缩放问题
1. 检查 scale 日志：
   ```bash
   adb logcat -s Unity | findstr "Scale"
   ```
2. 应该看到 `initialized with original scale`

## 🎉 预期结果

修复后，用户体验应该是：
1. ✅ 每个零件都可以独立抓取
2. ✅ 抓取一个零件时，其他零件不动
3. ✅ 零件保持原始大小（不缩小不变大）
4. ✅ 抓取时没有位置偏移
5. ✅ 可以同时抓取多个零件（双手）
6. ✅ 就像手机端一样，每个零件彼此独立

---

**修复版本**: V5.0 (最终完整版)
**最后更新**: 2026-03-09 21:00
**状态**: 等待测试验证

## 📞 如果问题仍然存在

请提供：
1. 完整的 adb logcat 日志（包含 XRGrabSetup 的输出）
2. 模型的层级结构截图（Unity Hierarchy 窗口）
3. XRGrabSetup 组件的 Inspector 截图
4. 确认是否卸载了旧 APK
