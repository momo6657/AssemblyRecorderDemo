# Quest 回放从录制开始状态播放 - 修复总结

## 🎯 需求说明

用户的录制流程：
1. 导入模型（初始状态）
2. **调整零件位置**（准备状态）← 这是"录制开始状态"
3. 点击"开始录制"
4. 移动零件（第一步）
5. 点击"记录步骤"
6. 移动零件（第二步）
7. 点击"记录步骤"

**回放需求**：
- 播放第一步时：从"准备状态"（录制开始状态）开始，**不是**从"初始状态"开始
- 播放第二步时：从"第一步结束"开始

## 🔍 问题分析

### 之前的错误理解

我之前以为需要恢复到"导入时的初始状态"，所以调用了 `RestoreBaseAll()`。

### 正确的理解

用户需要的是：
- **录制开始状态** = 点击"开始录制"时的零件位置
- **不是** 导入时的初始状态

## ✅ 修复方案

### 修复 1: 添加录制开始快照

**文件**: `Assets/Scripts/QuestStepSession.cs`

```csharp
// 新增字段：记录开始录制时的状态
readonly Dictionary<string, PartState> _recordingStartSnapshot = new Dictionary<string, PartState>();
bool _hasRecordingStartSnapshot = false;
```

### 修复 2: 在 BeginStep 时保存快照

**文件**: `Assets/Scripts/QuestStepSession.cs` - `BeginStep()` 方法

```csharp
public void BeginStep()
{
    // ... 省略其他代码 ...
    
    // ✅ 如果这是第一次开始录制，保存当前状态
    if (!_hasRecordingStartSnapshot)
    {
        _recordingStartSnapshot.Clear();
        var snapshot = CaptureCurrentSnapshot();
        foreach (var kv in snapshot)
        {
            _recordingStartSnapshot[kv.Key] = CloneState(kv.Value);
        }
        _hasRecordingStartSnapshot = true;
        Debug.Log($"[QuestStepSession] Captured recording start snapshot");
    }
    
    // ... 省略其他代码 ...
}
```

### 修复 3: JumpTo(-1) 恢复到录制开始状态

**文件**: `Assets/Scripts/QuestStepSession.cs` - `JumpTo()` 方法

```csharp
public void JumpTo(int index)
{
    if (index < 0)
    {
        // ✅ 跳到"录制开始状态"而不是"初始状态"
        if (_hasRecordingStartSnapshot && _recordingStartSnapshot.Count > 0)
        {
            Debug.Log("[QuestStepSession] JumpTo recording start pose");
            
            // 先恢复父级关系
            foreach (var kv in modelIndex.map)
            {
                // 恢复到原始父级
                if (modelIndex.TryGetOriginalParent(id, out var originalParent))
                {
                    t.SetParent(originalParent, true);
                }
            }
            
            // 应用录制开始时的状态
            foreach (var kv in _recordingStartSnapshot)
            {
                t.localPosition = ps.localPos;
                t.localRotation = ps.localRot;
                t.localScale = ps.localScale;
            }
        }
        else
        {
            // 如果没有录制开始快照，才恢复到初始状态
            ResetToBasePose();
        }
        return;
    }
    
    // ... 省略其他代码 ...
}
```

### 修复 4: 清除录制时清除快照

**文件**: `Assets/Scripts/QuestStepSession.cs` - `ClearRecordedSteps()` 方法

```csharp
public void ClearRecordedSteps()
{
    data.steps.Clear();
    currentIndex = -1;
    _segmentPrepared = false;
    
    // ✅ 清除录制开始快照
    _recordingStartSnapshot.Clear();
    _hasRecordingStartSnapshot = false;
}
```

### 修复 5: CoPlayStep 不再调用 RestoreBaseAll

**文件**: `Assets/Scripts/QuestStepSession.cs` - `CoPlayStep()` 方法

```csharp
IEnumerator CoPlayStep(int index)
{
    // ✅ 不再调用 RestoreBaseAll()
    // 而是通过 JumpTo(index - 1) 来恢复到正确的状态
    
    // 跳转到前一步
    // 如果 index = 0（第一步），则 JumpTo(-1) 会恢复到录制开始状态
    JumpTo(index - 1);
    
    // 播放动画...
}
```

## 📊 工作流程

### 录制流程

```
1. 导入模型
   Part1: position = (0, 0, 0)
   Part2: position = (1, 0, 0)

2. 用户调整位置（准备状态）
   Part1: position = (0.5, 0.2, 0)  ← 用户手动调整
   Part2: position = (1.2, 0.1, 0)  ← 用户手动调整

3. 点击"开始录制"（BeginStep）
   ✅ 保存快照：
   _recordingStartSnapshot = {
     Part1: (0.5, 0.2, 0),
     Part2: (1.2, 0.1, 0)
   }

4. 用户移动零件
   Part1: position = (2, 1, 0)

5. 点击"记录步骤"
   ✅ 记录第一步：
   Step 1 = {
     Part1: (2, 1, 0),
     Part2: (1.2, 0.1, 0)
   }
```

### 回放流程

```
播放第一步：
1. CoPlayStep(0) 被调用
2. JumpTo(-1) - 恢复到录制开始状态
   Part1: position = (0.5, 0.2, 0)  ← 从快照恢复
   Part2: position = (1.2, 0.1, 0)  ← 从快照恢复
3. 播放动画：Part1 从 (0.5, 0.2, 0) 移动到 (2, 1, 0)
4. 结果：✅ 和录制时一致！

播放第二步：
1. CoPlayStep(1) 被调用
2. JumpTo(0) - 恢复到第一步结束状态
   Part1: position = (2, 1, 0)
   Part2: position = (1.2, 0.1, 0)
3. 播放动画：...
4. 结果：✅ 和录制时一致！
```

## 📝 修改的文件

1. ✅ `Assets/Scripts/QuestStepSession.cs`
   - 添加 `_recordingStartSnapshot` 和 `_hasRecordingStartSnapshot` 字段
   - 修改 `BeginStep()` - 保存录制开始快照
   - 修改 `JumpTo()` - 恢复到录制开始状态而不是初始状态
   - 修改 `ClearRecordedSteps()` - 清除快照
   - 修改 `CoPlayStep()` - 移除 `RestoreBaseAll()` 调用

## 🧪 验证步骤

### 步骤 1: 重新构建 APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 步骤 2: 测试流程
1. 加载模型
2. **调整零件位置**（例如：Part1 移到 (0.5, 0.2, 0)）
3. 点击"开始录制"
4. 移动零件（例如：Part1 移到 (2, 1, 0)）
5. 点击"记录步骤"
6. 点击"播放第一步"
7. **Part1 应该从 (0.5, 0.2, 0) 开始移动到 (2, 1, 0)** ✅
8. **不是**从导入时的 (0, 0, 0) 开始 ✅

### 步骤 3: 查看日志
```bash
adb logcat -s Unity | findstr "QuestStepSession\|recording start"
```

**期望看到**：
```
[QuestStepSession] Captured recording start snapshot with 4 parts
[QuestStepSession] JumpTo recording start pose
```

## 🎯 关键点

### 1. 录制开始快照 vs 初始状态

```
初始状态（导入时）：
  Part1: (0, 0, 0)
  Part2: (1, 0, 0)

录制开始状态（用户调整后）：
  Part1: (0.5, 0.2, 0)  ← 用户手动调整
  Part2: (1.2, 0.1, 0)  ← 用户手动调整

回放时应该从"录制开始状态"开始，不是"初始状态"！
```

### 2. 何时保存快照？

- 在第一次调用 `BeginStep()` 时保存
- 只保存一次（`_hasRecordingStartSnapshot` 标志）
- 清除录制时清除快照

### 3. 何时使用快照？

- `JumpTo(-1)` 时使用
- `CoPlayStep(0)` 会调用 `JumpTo(-1)`，所以播放第一步时会用到

## 🎉 预期结果

修复后：
1. ✅ 播放第一步时，从"录制开始状态"开始
2. ✅ 不会恢复到"导入时的初始状态"
3. ✅ 用户在录制前的调整会被保留
4. ✅ 回放和录制时的相对位置一致

## 📞 如果问题仍然存在

请检查：
1. 是否点击了"开始录制"按钮（触发 `BeginStep()`）
2. 日志中是否看到 "Captured recording start snapshot"
3. 日志中是否看到 "JumpTo recording start pose"
4. `_recordingStartSnapshot` 是否正确保存了状态

---

**最后更新**: 2026-03-09 23:30
**状态**: 等待测试验证
