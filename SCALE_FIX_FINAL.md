# Quest 抓取缩小问题 - 根本原因与最终修复

## 🔴 问题现象
- 手柄抓取模型时，模型缩小到原来的 16%
- 释放后恢复正常
- 抓取时产生不正常的位置偏移

## 🔍 根本原因（通过日志分析发现）

### 日志证据
```
[ScaleProtector] GLB_test scale changed from (1.00, 1.00, 1.00) to (0.16, 0.16, 0.16)
[XRStableParent] GLB_test grabbed, cached scale: local=(0.16, 0.16, 0.16)
```

### 问题分析
1. **模型初始 scale**: `(1.0, 1.0, 1.0)` ✅ 正确
2. **XR 系统在抓取时修改 scale**: 改为 `(0.16, 0.16, 0.16)` ❌ 错误
3. **之前的保护代码在抓取时缓存了错误的 scale**: 
   - `OnSelectEntered` 中执行 `_cachedLocalScale = transform.localScale`
   - 此时 scale 已经被 XR 系统改为 0.16，所以缓存了错误的值
4. **LateUpdate 中尝试恢复到错误的 scale**: 
   - 一直恢复到 0.16，而不是原始的 1.0
5. **释放时恢复正常**: 
   - `ScaleProtector` 在释放后把 scale 恢复到 1.0

## ✅ 最终修复方案

### 核心修复：在 Awake 时缓存原始 scale

**修改文件**: `Assets/Scripts/XRGrabSetup.cs` - `XRStableParentDuringGrab` 类

**关键改动**:
```csharp
// 之前（错误）：
void OnSelectEntered(SelectEnterEventArgs _)
{
    _cachedLocalScale = transform.localScale;  // ❌ 缓存了被 XR 系统修改后的 scale
}

// 现在（正确）：
void Awake()
{
    _originalLocalScale = transform.localScale;  // ✅ 在初始化时缓存原始 scale
}

void OnSelectEntered(SelectEnterEventArgs _)
{
    // ✅ 不再重新缓存，使用 Awake 时的原始值
    transform.localScale = _originalLocalScale;  // ✅ 立即恢复到原始 scale
}
```

### 辅助修复

1. **禁用动态附加**
   ```csharp
   public bool useDynamicAttach = false;  // 减少 XR 系统对 transform 的干预
   ```

2. **禁用缩放跟踪**
   ```csharp
   public bool trackScale = false;  // 明确告诉 XR 系统不要跟踪缩放
   ```

3. **ScaleProtector 同步**
   - 当检测到 scale 变化时，同时更新 `XRStableParentDuringGrab` 的缓存
   - 确保两个保护系统的数据一致

## 📝 修改的文件

1. ✅ `Assets/Scripts/XRGrabSetup.cs`
   - 修复 `XRStableParentDuringGrab.Awake()` - 在初始化时缓存原始 scale
   - 修复 `XRStableParentDuringGrab.OnSelectEntered()` - 不再重新缓存，立即恢复
   - 添加 `UpdateOriginalScale()` 方法供外部同步

2. ✅ `Assets/Scripts/ScaleProtector.cs`
   - 添加与 `XRStableParentDuringGrab` 的同步机制
   - 恢复 scale 后通知相关组件更新缓存

## 🧪 验证步骤

### 1. 重新构建 APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 2. 查看日志
```bash
adb logcat -s Unity | findstr "Scale"
```

### 3. 期望的日志输出

**初始化时**:
```
[XRStableParent] GLB_test initialized with original scale: local=(1.00, 1.00, 1.00)
[ScaleProtector] Registered GLB_test with scale (1.00, 1.00, 1.00)
```

**抓取时**:
```
[XRStableParent] GLB_test grabbed, will maintain original scale: (1.00, 1.00, 1.00)
[XRStableParent] GLB_test scale changed from (0.16, 0.16, 0.16) to (1.00, 1.00, 1.00), restoring!
```
> 注意：现在会看到从 0.16 恢复到 1.00，而不是保持在 0.16

**释放时**:
```
[XRStableParent] GLB_test released, restored scale to (1.00, 1.00, 1.00)
```

### 4. 实际测试
1. 在 Quest 中加载模型
2. 用手柄抓取模型
3. **模型应该保持原始大小，不再缩小** ✅
4. 移动模型，观察是否有异常偏移
5. 释放模型，确认位置和大小都正确

## 🎯 为什么这次修复会成功

### 之前的问题
```
时间线：
1. 模型加载，scale = 1.0
2. 用户抓取
3. XR 系统修改 scale = 0.16  ← 在这里发生
4. OnSelectEntered 缓存 scale = 0.16  ← 缓存了错误的值
5. LateUpdate 恢复到 0.16  ← 恢复到错误的值
6. 用户看到模型缩小 ❌
```

### 现在的修复
```
时间线：
1. 模型加载，scale = 1.0
2. Awake 缓存 scale = 1.0  ← 在初始化时就缓存了正确的值
3. 用户抓取
4. XR 系统修改 scale = 0.16
5. OnSelectEntered 立即恢复 scale = 1.0  ← 立即恢复到正确的值
6. LateUpdate 持续恢复到 1.0  ← 持续保持正确的值
7. 用户看到模型保持原始大小 ✅
```

## 🔧 如果问题仍然存在

### 检查清单

1. **确认使用了最新的 APK**
   ```bash
   adb shell pm list packages | findstr AssemblyRecorder
   adb uninstall com.YourCompany.AssemblyRecorder
   # 然后重新 Build and Run
   ```

2. **检查日志中的初始化信息**
   - 应该看到 `initialized with original scale: local=(1.00, 1.00, 1.00)`
   - 如果看到的是 `(0.16, 0.16, 0.16)`，说明模型加载时就有问题

3. **检查 ImportManager 中的缩放设置**
   - 搜索 `xrTargetMaxSize` 相关代码
   - 确认模型加载后没有被额外缩放

4. **检查模型文件本身**
   - 在 3D 软件中打开 GLB 文件
   - 确认模型的原始 scale 是否正确

## 📊 技术细节

### XR Interaction Toolkit 的 Scale 修改时机
1. **OnSelectEntering** - 选择开始前
2. **OnSelectEntered** - 选择开始后 ← XR 系统在这之前修改了 scale
3. **ProcessInteractable** - 每帧处理
4. **OnSelectExiting** - 选择结束前
5. **OnSelectExited** - 选择结束后

### 我们的保护时机
1. **Awake** - 组件初始化时缓存原始 scale ← 最早的时机
2. **OnSelectEntered** - 立即恢复到原始 scale
3. **LateUpdate** - 每帧检查并恢复 ← 在所有 Update 之后
4. **OnSelectExited** - 释放时确保恢复

### 为什么 LateUpdate 很重要
- XR Interaction Toolkit 在 `Update` 中修改 transform
- 我们在 `LateUpdate` 中恢复，确保在每帧的最后执行
- 这样可以覆盖 XR 系统的任何修改

## 🎉 预期结果

修复后，用户体验应该是：
1. ✅ 抓取时模型保持原始大小
2. ✅ 移动时没有异常偏移
3. ✅ 释放后位置和大小都正确
4. ✅ 多次抓取都稳定可靠

---

**修复版本**: V3.0 (最终版)
**最后更新**: 2026-03-09
**状态**: 等待测试验证
