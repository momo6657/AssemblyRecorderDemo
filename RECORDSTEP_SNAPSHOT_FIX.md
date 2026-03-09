# Quest 回放位置修复 - RecordStep 支持

## 🔴 问题现象
开始播放第一步时的位置还是跟录制开始时的位置不一样

## 🔍 问题原因

### 日志分析
日志中没有看到：
- `Captured recording start snapshot`
- `JumpTo recording start pose`

这说明录制开始快照没有被保存。

### 根本原因
之前的修复只在 `BeginStep()` 中保存快照，但用户可能直接使用 `RecordStep()` 按钮，而不是 `BeginStep()` + `EndStep()` 的两步流程。

### 两种录制方式

**方式 1: 两步流程**（之前已支持）
```
1. 点击 "BeginStep" 按钮
2. 移动零件
3. 点击 "EndStep" 按钮
```

**方式 2: 单步流程**（之前未支持）
```
1. 移动零件
2. 点击 "RecordStep" 按钮  ← 用户使用的是这个！
```

## ✅ 修复方案

### 在 RecordStep 中也保存快照

**文件**: `Assets/Scripts/QuestStepSession.cs` - `RecordStep()` 方法

```csharp
public void RecordStep()
{
    // ... 省略其他代码 ...
    
    // ✅ 修复：如果这是第一次录制步骤，保存录制开始状态
    if (!_hasRecordingStartSnapshot)
    {
        // 使用 _segmentStartSnapshot 作为录制开始状态
        // 因为 PrepareSegmentFromCurrentPose 会在第一次录制前被调用
        if (_segmentStartSnapshot.Count > 0)
        {
            _recordingStartSnapshot.Clear();
            foreach (var kv in _segmentStartSnapshot)
            {
                _recordingStartSnapshot[kv.Key] = CloneState(kv.Value);
            }
            _hasRecordingStartSnapshot = true;
            Debug.Log($"[QuestStepSession] Captured recording start snapshot with {_recordingStartSnapshot.Count} parts (from segment start)");
        }
    }
    
    // ... 省略其他代码 ...
}
```

### 工作原理

**_segmentStartSnapshot 是什么？**
- 这是 `QuestStepSession` 内部用于轨迹录制的快照
- 在第一次调用 `RecordStep()` 之前，`PrepareSegmentFromCurrentPose()` 会被调用
- `PrepareSegmentFromCurrentPose()` 会保存当前状态到 `_segmentStartSnapshot`
- 这个快照正好就是"录制开始时的状态"！

**流程**：
```
1. 用户调整零件位置
   Part1: position = (0.5, 0.2, 0)

2. 用户移动零件
   Part1: position = (2, 1, 0)

3. 用户点击 "RecordStep"
   a. PrepareSegmentFromCurrentPose() 被调用（如果还没准备）
      但此时零件已经移动了，所以这不是我们想要的
   
   b. 实际上，PrepareSegmentFromCurrentPose() 在 Update() 中就被调用了
      当模型加载完成后，它会自动保存当前状态到 _segmentStartSnapshot
      这个状态就是用户调整后的状态！
   
   c. 第一次 RecordStep 时，复制 _segmentStartSnapshot 到 _recordingStartSnapshot
      _recordingStartSnapshot = {
        Part1: (0.5, 0.2, 0)  ← 这是录制开始时的状态
      }
```

## 📝 修改的文件

1. ✅ `Assets/Scripts/QuestStepSession.cs`
   - 修改 `RecordStep()` - 在第一次录制时保存快照

## 🧪 验证步骤

### 步骤 1: 重新构建 APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 步骤 2: 测试流程
1. 加载模型
2. **调整零件位置**（例如：Part1 移到某个位置）
3. **等待几秒**（让 `PrepareSegmentFromCurrentPose()` 在 Update 中被调用）
4. 移动零件
5. 点击 "RecordStep" 按钮
6. 点击 "播放第一步"
7. **零件应该从调整后的位置开始移动** ✅

### 步骤 3: 查看日志
```bash
adb logcat -s Unity | findstr "Captured recording start\|JumpTo recording"
```

**期望看到**：
```
[QuestStepSession] Captured recording start snapshot with 4 parts (from segment start)
[QuestStepSession] JumpTo recording start pose
```

## ⚠️ 重要说明

### _segmentStartSnapshot 的时机

`_segmentStartSnapshot` 是在以下时机保存的：

1. **Update() 中自动保存**：
   ```csharp
   void Update()
   {
       if (!_segmentPrepared && modelIndex != null && modelIndex.map.Count > 0)
       {
           PrepareSegmentFromCurrentPose();  // 保存当前状态
       }
   }
   ```

2. **第一次 RecordStep() 时**：
   ```csharp
   if (!_segmentPrepared && !PrepareSegmentFromCurrentPose())
   {
       return;
   }
   ```

**关键**：`PrepareSegmentFromCurrentPose()` 会在模型加载完成后自动调用，保存的是用户调整后的状态。

### 如果还是不对

如果回放位置还是不对，可能是因为：

1. **_segmentStartSnapshot 保存的时机不对**
   - 可能在用户调整之前就保存了
   - 解决方案：在第一次 RecordStep 时，使用当前状态作为基准

2. **零件的父级关系改变了**
   - 用户抓取零件后，父级可能改变
   - 解决方案：JumpTo 时恢复父级关系（已实现）

## 🔧 备用方案

如果 `_segmentStartSnapshot` 的时机不对，可以改为在第一次 `RecordStep` 时使用当前快照：

```csharp
public void RecordStep()
{
    // 在录制之前先保存快照
    if (!_hasRecordingStartSnapshot)
    {
        // 使用当前状态作为录制开始状态
        var snapshot = CaptureCurrentSnapshot();
        _recordingStartSnapshot.Clear();
        foreach (var kv in snapshot)
        {
            _recordingStartSnapshot[kv.Key] = CloneState(kv.Value);
        }
        _hasRecordingStartSnapshot = true;
        Debug.Log($"[QuestStepSession] Captured recording start snapshot (current state)");
    }
    
    // 然后准备 segment
    if (!_segmentPrepared && !PrepareSegmentFromCurrentPose())
    {
        return;
    }
    
    // ... 继续录制 ...
}
```

但这样的问题是：录制的第一步会从"当前状态"到"当前状态"，没有移动。

## 🎯 最佳实践

### 推荐的录制流程

**方式 1: 使用 BeginStep + EndStep**（最准确）
```
1. 调整零件位置
2. 点击 "BeginStep"  ← 明确保存录制开始状态
3. 移动零件
4. 点击 "EndStep"
```

**方式 2: 使用 RecordStep**（依赖自动保存）
```
1. 调整零件位置
2. 等待几秒（让系统自动保存状态）
3. 移动零件
4. 点击 "RecordStep"
```

## 📊 对比

### 修复前
```
RecordStep() 不保存录制开始快照
→ JumpTo(-1) 恢复到初始状态
→ 回放位置错误 ❌
```

### 修复后
```
RecordStep() 第一次调用时保存 _segmentStartSnapshot 到 _recordingStartSnapshot
→ JumpTo(-1) 恢复到录制开始状态
→ 回放位置正确 ✅
```

## 🎉 预期结果

修复后：
1. ✅ 使用 RecordStep 也能正确保存录制开始状态
2. ✅ 回放时从录制开始状态播放
3. ✅ 不需要使用 BeginStep + EndStep 两步流程

---

**最后更新**: 2026-03-09 23:45
**状态**: 等待测试验证
