# Quest 抓取模型缩小问题 - 修复总结 (V2)

## 🔴 问题状态
**仍未解决** - 需要重新构建 APK 并测试

## 📋 问题描述
在 Quest 中使用手柄抓取模型时，模型会异常缩小。

## 🔍 深度分析

### 第一次修复尝试（未生效）
- 修改了 `AddFittedBoxCollider` 使用 `localScale` 而不是 `lossyScale`
- 添加了基础的 scale 缓存机制
- **问题**：修复不够彻底，XR Interaction Toolkit 仍然可能修改 scale

### 第二次修复（当前版本）
基于更深入的分析，发现问题的真正根源：

1. **XR Interaction Toolkit 的动态附加机制**
   - `useDynamicAttach = true` 会让系统动态调整物体 transform
   - 这个过程可能意外修改 scale

2. **缺少持续的缩放监控**
   - 之前只在抓取开始/结束时检查
   - 抓取过程中的 scale 变化没有被及时纠正

3. **多个系统可能修改 scale**
   - XR Interaction Toolkit
   - Physics 系统
   - 父级关系变化

## 🛠️ 修复方案 V2

### 修复 1: 禁用动态附加
**文件**: `Assets/Scripts/XRGrabSetup.cs`

```csharp
// 改为 false，避免动态附加导致的缩放问题
public bool useDynamicAttach = false;

// 新增：禁止跟踪缩放
public bool trackScale = false;
```

### 修复 2: 增强 XRStableParentDuringGrab
**文件**: `Assets/Scripts/XRGrabSetup.cs`

- 同时缓存 `localScale` 和 `lossyScale`
- 在 `LateUpdate` 中持续检查并恢复
- 添加详细的调试日志
- 检查世界空间缩放变化

### 修复 3: 新增全局缩放保护组件
**文件**: `Assets/Scripts/ScaleProtector.cs` (新增)

- 独立的缩放监控组件
- 每 0.1 秒检查所有可抓取物体
- 自动恢复任何异常的缩放变化
- 可配置的容差和日志

### 修复 4: 自动集成保护系统
**文件**: `Assets/Scripts/ImportManager.cs`

- 模型加载后自动创建 `ScaleProtector`
- 自动注册所有可抓取物体到保护系统

### 修复 5: 测试工具
**文件**: `Assets/Scripts/ScaleTestTool.cs` (新增)

- 可以在 Unity Editor 中测试缩放保护
- 提供多个测试方法（右键菜单）

## 📝 修改的文件清单

1. ✅ `Assets/Scripts/XRGrabSetup.cs` - 禁用动态附加，增强缩放保护
2. ✅ `Assets/Scripts/ImportManager.cs` - 自动集成保护系统
3. ✅ `Assets/Scripts/ScaleProtector.cs` - 新增全局缩放监控
4. ✅ `Assets/Scripts/ScaleTestTool.cs` - 新增测试工具
5. ✅ `SCALE_FIX_V2.md` - 详细的修复文档

## 🧪 如何验证修复

### 步骤 1: 在 Unity Editor 中测试（可选）
1. 打开 QuestScene
2. 创建一个空 GameObject，添加 `ScaleTestTool` 组件
3. 运行场景
4. 在 Inspector 中右键点击 `ScaleTestTool`，选择 "Test Scale Change"
5. 查看 Console，应该看到 "Scale protection working!" 的绿色消息

### 步骤 2: 重新构建 Quest APK
```
1. File -> Build Settings
2. 选择 Android 平台
3. 确保选择了 QuestScene
4. Build and Run
```

### 步骤 3: 在 Quest 中测试
1. 加载模型
2. 用手柄抓取模型
3. 观察模型大小是否保持不变

### 步骤 4: 查看日志
```bash
adb logcat -s Unity | findstr "Scale"
```

**期望看到的日志**：
```
[ScaleProtector] Registered Part1 with scale (1.0, 1.0, 1.0)
[XRStableParent] Part1 grabbed, cached scale: local=(1.0, 1.0, 1.0)
[XRStableParent] Part1 released, restored scale to (1.0, 1.0, 1.0)
```

**如果看到警告**（说明保护机制正在工作）：
```
[ScaleProtector] Part1 scale changed, restoring!
[XRStableParent] Part1 scale changed, restoring!
```

## ⚠️ 重要提示

### 必须重新构建 APK！
之前的修复可能没有生效，因为：
1. 可能没有重新构建 APK
2. 或者修复不够彻底

### 如何确认使用了新的 APK
```bash
# 检查 APK 安装时间
adb shell pm dump com.YourCompany.AssemblyRecorder | findstr "firstInstallTime"

# 或者直接卸载重装
adb uninstall com.YourCompany.AssemblyRecorder
# 然后在 Unity 中 Build and Run
```

## 🔧 如果问题仍然存在

### 调试步骤

1. **确认日志中有 ScaleProtector 的输出**
   - 如果没有，说明组件没有被创建
   - 检查 `ImportManager.TryApplyXrGrab` 是否被调用

2. **确认 useDynamicAttach 已改为 false**
   - 在 Unity Inspector 中检查 `XRGrabSetup` 组件
   - 或者在日志中搜索 "useDynamicAttach"

3. **启用详细日志**
   - `ScaleProtector.logScaleChanges = true`
   - 查看每次检查的结果

4. **检查是否有其他脚本修改 scale**
   ```bash
   # 在项目中搜索
   grep -r "\.localScale\s*=" Assets/Scripts/
   ```

### 临时解决方案

如果上述修复仍然无效，可以尝试：

**方案 A**: 完全禁用 XRGrabInteractable，使用自定义抓取逻辑

**方案 B**: 在 `Update` 中强制锁定 scale（性能较差但最可靠）

```csharp
void Update()
{
    transform.localScale = _cachedScale;
}
```

## 📊 技术细节

### 为什么需要三层保护？

1. **XRStableParentDuringGrab** - 在抓取时立即响应
2. **ScaleProtector** - 全局监控，防止遗漏
3. **禁用 useDynamicAttach** - 从源头预防

### 性能影响
- `ScaleProtector` 每 0.1 秒检查一次，几乎无性能影响
- 只在有 XRGrabInteractable 的物体上工作
- 可以通过配置调整检查频率

## 📞 需要帮助？

如果问题仍然无法解决，请提供：
1. 完整的 adb logcat 日志（包含 Scale 关键字的部分）
2. Unity Inspector 中 `XRGrabSetup` 组件的截图
3. 使用的模型文件（GLB）
4. Unity 版本和 XR Interaction Toolkit 版本

---

**修复版本**: V2.0
**最后更新**: 2026-03-08 21:00
**状态**: 等待测试验证
