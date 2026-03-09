# Quest 第一步播放位置修复 - Trajectory From 状态

## 🔴 问题现象
后面几步播放正常，但第一步播放开始时位置还是不对

## 🔍 问题原因

### 之前的修复
我们已经修复了：
1. ✅ `BeginStep()` 保存录制开始快照
2. ✅ `JumpTo(-1)` 恢复录制开始状态
3. ✅ `CoPlayStep()` 始终调用 `JumpTo(-1)`

### 为什么第一步还是不对？

**问题在于 trajectory 的 from 状态**：

```csharp
// RecordStep() 中
var from = _segmentStartSnapshot[id];  // 这个可能不是 BeginStep 时的状态！
var to = nowSnapshot[id];
var traj = BuildTrajectory(id, from, to, duration);
```

**_segmentStartSnapshot 的问题**：
- `_segmentStartSnapshot` 是在 `PrepareSegmentFromCurrentPose()` 时保存的
- 这个时机可能在用户移动零件之后
- 所以 `from` 状态可能不是 BeginStep 时的状态

### 具体场景

```
1. 用户调整零件位置
   Part1: (0, 0, 0)

2. 点击 BeginStep
   _recordingStartSnapshot: Part1=(0, 0, 0)  ✅ 正确
   _segmentStartSnapshot: Part1=(0, 0, 0)    ✅ 正确

3. 用户移动零件
   Part1: (2, 0, 0)

4. 点击 EndStep (RecordStep)
   from = _segmentStartSnapshot[Part1] = (0, 0, 0)  ✅ 看起来正确
   to = (2, 0, 0)
   
   但是！如果 _segmentStartSnapshot 在移动后才保存，
   from 可能是 (1, 0, 0) 而不是 (0, 0, 0)  ❌ 错误！
```

## ✅ 修复方案

### 在 BuildTrajectory 中使用 _recordingStartSnapshot

**文件**: `Assets/Scripts/QuestStepSession.cs` - `BuildTrajectory()` 方法

```csharp
PartTrajectory BuildTrajectory(string id, PartState from, PartState to, float duration)
{
    // ✅ 修复：如果有录制开始快照，使用它作为 from 状态
    if (_hasRecordingStartSnapshot && _recordingStartSnapshot.TryGetValue(id, out var recordingStart))
    {
        from = CloneState(recordingStart);
    }
    
    var traj = new PartTrajectory
    {
        id = id,
        from = CloneState(from),  // 现在 from 是 BeginStep 时的状态
        to = CloneState(to)
    };
    
    // 确保第一个 sample 使用录制开始状态
    var first = traj.samples[0];
    first.localPos = from.localPos;
    first.localRot = from.localRot;
    first.localScale = from.localScale;
    traj.samples[0] = first;
    
    // ... 其他代码 ...
}
```

### 工作原理

**修复前**：
```
trajectory.from = _segmentStartSnapshot[id]  // 可能不准确
trajectory.samples[0] = _segmentSamples[id][0]  // 可能不准确
```

**修复后**：
```
trajectory.from = _recordingStartSnapshot[id]  // ✅ 准确！BeginStep 时的状态
trajectory.samples[0].pos = _recordingStartSnapshot[id].pos  // ✅ 准确！
```

## 📊 完整流程

### 录制流程

```
1. 调整零件位置
   Part1: (0, 0, 0)

2. 点击 BeginStep
   _recordingStartSnapshot[Part1] = (0, 0, 0)  ← 保存
   _segmentStartSnapshot[Part1] = (0, 0, 0)

3. 移动零件
   Part1: (2, 0, 0)

4. 点击 EndStep
   BuildTrajectory:
     from = _recordingStartSnapshot[Part1] = (0, 0, 0)  ✅ 使用录制开始状态
     to = (2, 0, 0)
     trajectory.from = (0, 0, 0)
     trajectory.samples[0] = (0, 0, 0)  ✅ 第一个点是 BeginStep 状态
     trajectory.samples[last] = (2, 0, 0)
```

### 回放流程

```
播放 Step 1:
1. JumpTo(-1) - 恢复到 BeginStep 状态
   Part1: (0, 0, 0)

2. 播放 trajectory:
   t=0: Part1 = trajectory.samples[0] = (0, 0, 0)  ✅ 正确！
   t=0.5: Part1 = lerp((0,0,0), (2,0,0), 0.5) = (1, 0, 0)
   t=1.0: Part1 = trajectory.samples[last] = (2, 0, 0)
```

## 📝 修改的文件

1. ✅ `Assets/Scripts/QuestStepSession.cs`
   - 修改 `BuildTrajectory()` - 使用 `_recordingStartSnapshot` 作为 from 状态

## 🧪 验证步骤

### 步骤 1: 重新构建 APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 步骤 2: 测试流程
```
1. 加载模型
2. 调整零件位置
   Part1: (0, 0, 0)
   Part2: (1, 0, 0)

3. 点击 BeginStep

4. 移动 Part1 到 (2, 0, 0)
5. 点击 EndStep

6. 播放 Step 1:
   起始: Part1=(0,0,0), Part2=(1,0,0)  ✅ 应该和 BeginStep 时一样！
   结束: Part1=(2,0,0), Part2=(1,0,0)
```

### 步骤 3: 查看日志
```bash
adb logcat -s Unity | findstr "BuildTrajectory\|from\|recording start"
```

## 🎯 关键点

### 三个快照的区别

1. **_recordingStartSnapshot**（录制开始快照）
   - 在第一次 `BeginStep()` 时保存
   - 代表用户点击 BeginStep 那一瞬间的状态
   - **这是我们想要的起始状态**

2. **_segmentStartSnapshot**（段开始快照）
   - 在 `PrepareSegmentFromCurrentPose()` 时保存
   - 用于计算移动增量
   - 可能在用户移动零件之后才保存
   - **不一定是 BeginStep 时的状态**

3. **nowSnapshot**（当前快照）
   - 在 `RecordStep()` 时捕获
   - 代表当前的状态
   - 用作 trajectory 的 to 状态

### 为什么要修改 BuildTrajectory？

因为 `trajectory.from` 和 `trajectory.samples[0]` 决定了回放时的起始位置。

如果这两个值不是 BeginStep 时的状态，回放就会从错误的位置开始。

## 🎉 预期结果

修复后：
1. ✅ 第一步播放从 BeginStep 状态开始
2. ✅ 后面几步也从 BeginStep 状态开始
3. ✅ 所有步骤的起始位置都和 BeginStep 时一致
4. ✅ trajectory 的 from 状态正确

---

**最后更新**: 2026-03-10 00:30
**状态**: 最终修复完成
**关键改动**: BuildTrajectory 使用 _recordingStartSnapshot
