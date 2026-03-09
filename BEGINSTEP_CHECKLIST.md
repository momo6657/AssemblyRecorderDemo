# BeginStep 回放位置问题 - 完整检查清单

## ✅ 代码确认

我已经仔细检查了代码，逻辑是正确的：

### 1. BeginStep() - 保存快照 ✅
```csharp
public void BeginStep()
{
    // 第一次调用时保存快照
    if (!_hasRecordingStartSnapshot)
    {
        _recordingStartSnapshot.Clear();
        var snapshot = CaptureCurrentSnapshot();
        foreach (var kv in snapshot)
        {
            _recordingStartSnapshot[kv.Key] = CloneState(kv.Value);
        }
        _hasRecordingStartSnapshot = true;
        Debug.Log($"[QuestStepSession] Captured recording start snapshot with {_recordingStartSnapshot.Count} parts");
    }
}
```

### 2. JumpTo(-1) - 恢复快照 ✅
```csharp
public void JumpTo(int index)
{
    if (index < 0)
    {
        if (_hasRecordingStartSnapshot && _recordingStartSnapshot.Count > 0)
        {
            Debug.Log("[QuestStepSession] JumpTo recording start pose");
            
            // 恢复父级
            foreach (var kv in modelIndex.map)
            {
                if (modelIndex.TryGetOriginalParent(id, out var originalParent))
                {
                    t.SetParent(originalParent, true);
                }
            }
            
            // 恢复位置
            foreach (var kv in _recordingStartSnapshot)
            {
                t.localPosition = ps.localPos;
                t.localRotation = ps.localRot;
                t.localScale = ps.localScale;
            }
        }
    }
}
```

### 3. CoPlayStep(0) - 调用 JumpTo(-1) ✅
```csharp
IEnumerator CoPlayStep(int index)
{
    // 播放第一步时，index = 0
    // 所以会调用 JumpTo(-1)
    JumpTo(index - 1);
    
    // 播放动画...
}
```

## 🔍 可能的问题

### 问题 1: 使用了旧的 APK
**症状**: 日志中看不到 "Captured recording start snapshot"

**解决方案**:
```bash
# 1. 卸载旧 APK
adb uninstall com.YourCompany.AssemblyRecorder

# 2. 重新构建并安装
Unity -> File -> Build Settings -> Build and Run
```

### 问题 2: 没有点击 BeginStep
**症状**: 直接点击了 RecordStep 或 EndStep

**解决方案**: 确保按照正确的流程操作：
```
1. 调整零件位置
2. 点击 "BeginStep" 按钮  ← 必须点击这个！
3. 移动零件
4. 点击 "EndStep" 按钮
```

### 问题 3: 零件的父级关系不对
**症状**: 零件被抓取后，父级改变了

**解决方案**: JumpTo 会自动恢复父级（代码已实现）

### 问题 4: 快照保存的时机不对
**症状**: BeginStep 在移动零件之后才点击

**解决方案**: 确保在移动零件**之前**点击 BeginStep

## 🧪 详细测试步骤

### 步骤 1: 确认使用最新代码
```bash
# 检查文件修改时间
ls -l Assets/Scripts/QuestStepSession.cs
```

### 步骤 2: 完全重新构建
```
1. Unity -> File -> Build Settings
2. 点击 "Clean Build"（如果有）
3. Build and Run
```

### 步骤 3: 卸载旧 APK
```bash
adb uninstall com.YourCompany.AssemblyRecorder
```

### 步骤 4: 安装新 APK
在 Unity 中 Build and Run，或手动安装

### 步骤 5: 清除日志
```bash
adb logcat -c
```

### 步骤 6: 开始录制
```
1. 加载模型
2. 调整零件位置（例如：Part1 移到 (0.5, 0.2, 0)）
3. 点击 "BeginStep" 按钮  ← 关键！
4. 移动零件（例如：Part1 移到 (2, 1, 0)）
5. 点击 "EndStep" 按钮
```

### 步骤 7: 查看日志
```bash
adb logcat -s Unity > test_log.txt
```

在 `test_log.txt` 中搜索：
- `Captured recording start snapshot` ← 应该看到这个
- `JumpTo recording start pose` ← 回放时应该看到这个

### 步骤 8: 测试回放
```
1. 点击 "播放第一步"
2. 观察零件的起始位置
3. 应该从 (0.5, 0.2, 0) 开始，不是从 (0, 0, 0) 开始
```

## 📊 日志分析

### 正确的日志应该是：

```
# 点击 BeginStep 时
[QuestStepSession] Captured recording start snapshot with 4 parts

# 点击 EndStep 时
[QuestStepSession] Recorded step 1/1, dur=5.23s, tracks=1

# 点击播放第一步时
[QuestStepSession] JumpTo recording start pose
[QuestStepSession] JumpTo: restoring parent for Part1
[QuestStepSession] JumpTo: restoring parent for Part2
...
```

### 如果看不到 "Captured recording start snapshot"

**原因**: 
1. 使用了旧的 APK
2. 或者 `_hasRecordingStartSnapshot` 已经是 true（之前已经保存过）

**解决方案**:
1. 卸载重装 APK
2. 或者点击 "清除录制" 按钮（会清除快照）

### 如果看不到 "JumpTo recording start pose"

**原因**:
1. `_hasRecordingStartSnapshot` 是 false
2. 或者 `_recordingStartSnapshot.Count` 是 0

**解决方案**:
1. 确认 BeginStep 时保存了快照
2. 检查日志中是否有 "Captured recording start snapshot"

## 🔧 调试技巧

### 添加更多日志

如果问题仍然存在，可以在代码中添加更多日志：

```csharp
public void BeginStep()
{
    Debug.Log($"[DEBUG] BeginStep called, _hasRecordingStartSnapshot={_hasRecordingStartSnapshot}");
    
    if (!_hasRecordingStartSnapshot)
    {
        var snapshot = CaptureCurrentSnapshot();
        Debug.Log($"[DEBUG] Captured {snapshot.Count} parts");
        
        foreach (var kv in snapshot)
        {
            Debug.Log($"[DEBUG] Part {kv.Key}: pos={kv.Value.localPos}");
        }
        
        // ... 保存快照 ...
    }
}

public void JumpTo(int index)
{
    Debug.Log($"[DEBUG] JumpTo({index}), _hasRecordingStartSnapshot={_hasRecordingStartSnapshot}, count={_recordingStartSnapshot.Count}");
    
    if (index < 0)
    {
        if (_hasRecordingStartSnapshot && _recordingStartSnapshot.Count > 0)
        {
            Debug.Log($"[DEBUG] Restoring {_recordingStartSnapshot.Count} parts");
            
            foreach (var kv in _recordingStartSnapshot)
            {
                Debug.Log($"[DEBUG] Restoring {kv.Key} to pos={kv.Value.localPos}");
            }
            
            // ... 恢复快照 ...
        }
    }
}
```

## ✅ 最终确认

**我确认代码逻辑是正确的**，如果还有问题，最可能的原因是：

1. ❌ **使用了旧的 APK** - 必须卸载重装
2. ❌ **没有点击 BeginStep** - 必须在移动零件之前点击
3. ❌ **点击 BeginStep 的时机不对** - 必须在调整位置之后、移动零件之前点击

**请按照上面的详细测试步骤操作，并提供完整的日志文件，我可以帮你分析具体问题。**

---

**最后更新**: 2026-03-10 00:00
**代码状态**: ✅ 已确认正确
**需要**: 完整的测试日志
