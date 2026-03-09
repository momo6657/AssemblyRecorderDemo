# Quest 回放位置不一致问题 - 修复总结

## 🔴 问题现象
点击开始播放第一步时，各个零件的位置和开始录制时的相对位置不一样

## 🔍 问题原因

### 场景分析

**录制时**：
```
ImportedModel
  └─ GLB_test
      ├─ Part1 (localPosition = (0, 0, 0))
      ├─ Part2 (localPosition = (1, 0, 0))
      └─ Part3 (localPosition = (2, 0, 0))

录制的数据：
- Part1: localPosition = (0, 0, 0)
- Part2: localPosition = (1, 0, 0)
- Part3: localPosition = (2, 0, 0)
```

**回放时（如果零件被抓取过）**：
```
ImportedModel
  └─ GLB_test
      ├─ Part2 (localPosition = (1, 0, 0))
      └─ Part3 (localPosition = (2, 0, 0))
XR Origin
  └─ Part1 (localPosition = (5, 3, 2))  ← 父级改变了！

回放应用数据：
- Part1: localPosition = (0, 0, 0)  ← 但父级是 XR Origin，不是 GLB_test！
- Part2: localPosition = (1, 0, 0)  ✅ 正确
- Part3: localPosition = (2, 0, 0)  ✅ 正确

结果：Part1 的世界位置错误！
```

### 根本原因

1. **录制时**：零件在原始父级（`GLB_test`）下，记录的是相对于原始父级的 `localPosition`
2. **抓取后**：零件的父级可能改变（变成 `XR Origin` 或其他）
3. **回放时**：直接应用 `localPosition`，但父级不对，导致世界位置错误

## ✅ 修复方案

### 修复 1: 在 JumpTo 前恢复父级关系

**文件**: `Assets/Scripts/QuestStepSession.cs` - `JumpTo()` 方法

```csharp
public void JumpTo(int index)
{
    // ... 省略其他代码 ...
    
    // ✅ 修复：在应用步骤之前，先恢复所有零件的父级关系
    if (modelIndex != null)
    {
        foreach (var kv in modelIndex.map)
        {
            var id = kv.Key;
            var t = kv.Value;
            if (t == null) continue;
            
            // 恢复到原始父级
            if (modelIndex.TryGetOriginalParent(id, out var originalParent))
            {
                if (t.parent != originalParent)
                {
                    Debug.Log($"[QuestStepSession] JumpTo: restoring parent for {id}");
                    t.SetParent(originalParent, true);  // worldPositionStays = true
                }
            }
        }
    }
    
    // 然后应用步骤的状态
    for (int i = 0; i < frame.parts.Count; i++)
    {
        var ps = frame.parts[i];
        t.localPosition = ps.localPos;  // 现在父级正确了，localPosition 才能正确应用
        // ...
    }
}
```

### 修复 2: 在 CoPlayStep 开始时恢复所有零件

**文件**: `Assets/Scripts/QuestStepSession.cs` - `CoPlayStep()` 方法

```csharp
IEnumerator CoPlayStep(int index)
{
    // ✅ 修复：在播放任何步骤之前，先恢复所有零件到初始状态
    if (modelIndex != null)
    {
        Debug.Log($"[QuestStepSession] Restoring all parts to base pose before playing step {index + 1}");
        modelIndex.RestoreBaseAll();
    }
    
    // 锁定模型根节点位置
    Transform modelRoot = modelIndex?.modelRoot;
    Vector3 lockedPosition = modelRoot.position;
    Quaternion lockedRotation = modelRoot.rotation;
    
    // 跳转到前一步
    JumpTo(index - 1);
    
    // 播放动画...
}
```

### 工作原理

**修复后的流程**：
```
1. 用户点击"播放第一步"
2. CoPlayStep(0) 被调用
3. RestoreBaseAll() - 恢复所有零件到初始状态（包括父级）
   ImportedModel
     └─ GLB_test
         ├─ Part1 (localPosition = 初始值, parent = GLB_test) ✅
         ├─ Part2 (localPosition = 初始值, parent = GLB_test) ✅
         └─ Part3 (localPosition = 初始值, parent = GLB_test) ✅

4. JumpTo(-1) - 跳到 base pose（已经恢复了，所以不变）
5. JumpTo 内部也会恢复父级（双重保险）
6. 播放动画 - 应用录制的 localPosition
7. 结果：所有零件的相对位置和录制时一致 ✅
```

## 📝 修改的文件

1. ✅ `Assets/Scripts/QuestStepSession.cs`
   - 修改 `JumpTo()` - 在应用状态前恢复父级关系
   - 修改 `CoPlayStep()` - 在播放前调用 `RestoreBaseAll()`

## 🧪 验证步骤

### 步骤 1: 重新构建 APK
```
Unity -> File -> Build Settings -> Build and Run
```

### 步骤 2: 测试流程
1. 加载模型
2. 录制几个步骤（移动零件）
3. **用手柄抓取并移动一些零件**（改变父级）
4. 点击"播放第一步"
5. **零件应该回到录制时的相对位置** ✅

### 步骤 3: 查看日志
```bash
adb logcat -s Unity | findstr "QuestStepSession\|JumpTo\|Restoring"
```

**期望看到**：
```
[QuestStepSession] Restoring all parts to base pose before playing step 1
[QuestStepSession] JumpTo: restoring parent for Part1
[QuestStepSession] JumpTo: restoring parent for Part2
[QuestStepSession] JumpTo base pose
```

## 🎯 关键点

### 1. 为什么需要恢复父级？

```
localPosition 的含义取决于父级：

父级 = GLB_test (scale = 0.16):
  localPosition = (1, 0, 0)
  worldPosition = GLB_test.position + (1, 0, 0) * 0.16

父级 = XR Origin (scale = 1.0):
  localPosition = (1, 0, 0)
  worldPosition = XR Origin.position + (1, 0, 0) * 1.0

结果：相同的 localPosition，不同的 worldPosition！
```

### 2. 为什么在 CoPlayStep 开始时调用 RestoreBaseAll？

- 确保所有零件都回到初始状态
- 包括位置、旋转、缩放、**父级关系**
- 这样录制的 localPosition 才能正确应用

### 3. 为什么在 JumpTo 中也恢复父级？

- 双重保险
- JumpTo 可能被单独调用（不通过 CoPlayStep）
- 确保任何情况下父级都正确

## 📊 对比

### 修复前
```
录制时：Part1 在 GLB_test 下，localPosition = (0, 0, 0)
抓取后：Part1 在 XR Origin 下
回放时：应用 localPosition = (0, 0, 0)，但父级是 XR Origin
结果：Part1 的世界位置错误 ❌
```

### 修复后
```
录制时：Part1 在 GLB_test 下，localPosition = (0, 0, 0)
抓取后：Part1 在 XR Origin 下
回放时：
  1. 先恢复父级：Part1 → GLB_test
  2. 应用 localPosition = (0, 0, 0)
结果：Part1 的世界位置正确 ✅
```

## 🔧 相关代码

### ModelIndex.RestoreBaseAll()
```csharp
public void RestoreBaseAll()
{
    foreach (var kv in map)
    {
        var id = kv.Key;
        var t = kv.Value;
        
        // 恢复父级
        if (_baseParents.TryGetValue(id, out var parent) && t.parent != parent)
            t.SetParent(parent, false);
        
        // 恢复 transform
        if (_base.TryGetValue(id, out var trs))
        {
            t.localPosition = trs.pos;
            t.localRotation = trs.rot;
            t.localScale = trs.scale;
        }
    }
}
```

### ModelIndex.TryGetOriginalParent()
```csharp
public bool TryGetOriginalParent(string id, out Transform parent)
{
    return _baseParents.TryGetValue(id, out parent);
}
```

## 🎉 预期结果

修复后：
1. ✅ 回放第一步时，零件的相对位置和录制时一致
2. ✅ 即使零件被抓取过，回放也正确
3. ✅ 所有步骤的回放都正确
4. ✅ 模型根节点的位置保持不变（不会飘走）

## 📞 如果问题仍然存在

请检查：
1. 日志中是否看到 "Restoring all parts to base pose"
2. 日志中是否看到 "restoring parent for ..."
3. `ModelIndex._baseParents` 是否正确记录了原始父级
4. 录制时零件的父级是否正确

---

**最后更新**: 2026-03-09 23:00
**状态**: 等待测试验证
