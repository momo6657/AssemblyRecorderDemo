# Quest 抓取缩小问题 - 深度修复方案

## 问题现象
在 Quest 中使用手柄抓取模型时，模型会异常缩小。

## 根本原因分析

经过深入分析，发现问题有多个层面：

### 1. XR Interaction Toolkit 的动态附加机制
- `useDynamicAttach = true` 会导致 XR 系统尝试动态调整物体的 transform
- 这个过程中可能会意外修改 scale

### 2. BoxCollider 尺寸计算错误
- 使用 `lossyScale` 而不是 `localScale` 计算 collider 尺寸
- 当父级关系改变时，`lossyScale` 会变化

### 3. 缺少缩放保护机制
- 没有持续监控和恢复 scale 的机制
- 抓取过程中的 scale 变化没有被及时纠正

## 修复方案

### 修复 1: 禁用动态附加 (XRGrabSetup.cs)
```csharp
public bool useDynamicAttach = false;  // 改为 false
public bool trackScale = false;        // 新增：禁止跟踪缩放
```

### 修复 2: 增强缩放锁定 (XRStableParentDuringGrab)
- 在抓取时缓存 `localScale` 和 `lossyScale`
- 在 `LateUpdate` 中持续检查并恢复
- 添加详细的调试日志

### 修复 3: 全局缩放保护 (ScaleProtector.cs - 新增)
- 新增独立的缩放监控组件
- 每 0.1 秒检查所有可抓取物体的 scale
- 自动恢复任何异常的缩放变化

### 修复 4: BoxCollider 计算优化
- 使用 `localScale` 替代 `lossyScale`
- 添加异常值保护

## 修改的文件

1. **Assets/Scripts/XRGrabSetup.cs**
   - 禁用 `useDynamicAttach`
   - 添加 `trackScale = false`
   - 增强 `XRStableParentDuringGrab` 的缩放保护
   - 优化 `AddFittedBoxCollider` 计算

2. **Assets/Scripts/ImportManager.cs**
   - 自动创建 `ScaleProtector` 组件
   - 注册所有可抓取物体到保护系统

3. **Assets/Scripts/ScaleProtector.cs** (新增)
   - 全局缩放监控和保护组件

## 如何验证修复

### 步骤 1: 重新构建 Quest APK
```bash
# 在 Unity 中
1. File -> Build Settings
2. 选择 Android 平台
3. 确保选择了 QuestScene
4. Build and Run
```

### 步骤 2: 查看日志
```bash
# 在 PC 上运行
adb logcat -s Unity | findstr "Scale"
```

你应该看到类似的日志：
```
[ScaleProtector] Registered Part1 with scale (1.0, 1.0, 1.0)
[XRStableParent] Part1 grabbed, cached scale: local=(1.0, 1.0, 1.0), world=(0.5, 0.5, 0.5)
[XRStableParent] Part1 released, restored scale to (1.0, 1.0, 1.0)
```

如果看到警告日志：
```
[ScaleProtector] Part1 scale changed from (1.0, 1.0, 1.0) to (0.5, 0.5, 0.5), restoring!
[XRStableParent] Part1 scale changed from (1.0, 1.0, 1.0) to (0.5, 0.5, 0.5), restoring!
```

这说明保护机制正在工作，自动修正了缩放问题。

### 步骤 3: 实际测试
1. 在 Quest 中加载模型
2. 用手柄抓取模型的任意部件
3. 观察模型是否保持原始大小
4. 释放后再次抓取，验证多次操作后是否稳定

## 如果问题仍然存在

### 检查清单

1. **确认使用了新的 APK**
   ```bash
   # 检查 APK 构建时间
   adb shell pm dump com.YourCompany.AssemblyRecorder | findstr "firstInstallTime"
   ```

2. **检查 XRGrabSetup 配置**
   - 在 Unity Inspector 中检查 `XRGrabSetup` 组件
   - 确认 `useDynamicAttach = false`
   - 确认 `trackScale = false`

3. **检查是否有其他脚本修改 scale**
   ```bash
   # 搜索所有修改 scale 的代码
   grep -r "localScale\s*=" Assets/Scripts/
   grep -r "lossyScale\s*=" Assets/Scripts/
   ```

4. **启用详细日志**
   - 在 `ScaleProtector` 中设置 `logScaleChanges = true`
   - 在 `XRStableParentDuringGrab` 中已经添加了详细日志

5. **检查 XR Interaction Toolkit 版本**
   - 确保使用的是兼容版本
   - 当前项目使用：XR Interaction Toolkit 3.3.1

## 临时解决方案（如果上述修复无效）

如果问题仍然存在，可以尝试以下临时方案：

### 方案 A: 完全禁用 XR 抓取的 scale 修改
在 `XRGrabInteractable` 的 `ProcessInteractable` 方法中强制锁定 scale：

```csharp
// 在 XRStableParentDuringGrab 的 LateUpdate 中
void LateUpdate()
{
    // 强制锁定 scale，无论任何情况
    transform.localScale = _cachedLocalScale;
}
```

### 方案 B: 使用固定的 Attach Transform
创建一个固定的 attach point，而不是动态计算：

```csharp
// 在每个可抓取物体上添加一个子物体作为 attach transform
GameObject attachPoint = new GameObject("AttachPoint");
attachPoint.transform.SetParent(part, false);
attachPoint.transform.localPosition = Vector3.zero;
attachPoint.transform.localRotation = Quaternion.identity;
attachPoint.transform.localScale = Vector3.one;
grab.attachTransform = attachPoint.transform;
```

## 技术细节

### XR Interaction Toolkit 的 Scale 处理
XR Interaction Toolkit 在以下情况下可能修改 scale：
1. `useDynamicAttach = true` 时的动态附加计算
2. `snapToColliderVolume = true` 时的碰撞体对齐
3. 父级关系改变时的坐标系转换
4. Smooth tracking 的插值计算

### 我们的保护策略
1. **预防**：禁用可能导致 scale 变化的功能
2. **监控**：持续检查 scale 是否改变
3. **修正**：立即恢复任何异常的 scale 变化
4. **日志**：记录所有 scale 相关的操作，便于调试

## 性能影响

- `ScaleProtector` 每 0.1 秒检查一次，性能影响极小
- `XRStableParentDuringGrab` 只在抓取时激活，几乎无性能影响
- 所有日志都可以通过配置关闭

## 后续优化建议

1. 如果确认修复有效，可以将 `ScaleProtector.checkInterval` 增加到 0.2 秒
2. 可以将 `ScaleProtector.logScaleChanges` 改为 false，减少日志输出
3. 考虑将 scale 保护逻辑直接集成到 `XRGrabInteractable` 的子类中

## 联系支持

如果问题仍然无法解决，请提供：
1. 完整的 adb logcat 日志（从启动到出现问题）
2. Unity Editor 中 `XRGrabSetup` 组件的截图
3. 问题复现的详细步骤
4. 使用的模型文件（GLB）

---

**最后更新**: 2026-03-08
**修复版本**: v2.0
