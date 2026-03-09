# Quest 零件物理配置 - 防止碰撞和飞走

## 🎯 需求
1. 每个零件都有 Rigidbody 和 Collider
2. 取消重力和物理效果
3. 零件之间不会互相碰撞（不会被撞开）
4. 释放零件后不会飞走
5. 每个零件可以独立抓取和移动

## ✅ 完整配置方案

### 1. Rigidbody 配置
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `Apply()` 方法

```csharp
if (go.TryGetComponent<Rigidbody>(out var body))
{
    // 完全禁用物理效果
    body.useGravity = false;  // ✅ 不受重力影响
    body.isKinematic = true;  // ✅ 运动学模式，不受物理引擎影响
    body.interpolation = RigidbodyInterpolation.Interpolate;  // 平滑移动
    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    
    // 禁用所有物理约束
    body.constraints = RigidbodyConstraints.None;
    body.mass = 1f;
    body.drag = 0f;  // 无空气阻力
    body.angularDrag = 0f;  // 无角阻力
}
```

**关键设置**:
- `isKinematic = true` - 这是最重要的！运动学模式下，物体不受物理引擎影响，不会飞走
- `useGravity = false` - 不受重力影响
- `drag = 0` - 无阻力，释放后立即停止

### 2. Collider 配置
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `EnsurePartColliders()` 方法

```csharp
foreach (var col in result)
{
    // 不设置为 Trigger，因为 XR Interaction 需要实体 collider
    col.isTrigger = false;
    
    // 如果是 MeshCollider，确保是 convex
    if (col is MeshCollider mc)
    {
        mc.convex = true;
    }
}
```

**为什么不用 Trigger**:
- XR Interaction Toolkit 需要实体 collider 来检测抓取
- 由于 Rigidbody 是 Kinematic，所以不会产生物理碰撞

### 3. 禁用零件之间的碰撞
**文件**: `Assets/Scripts/XRGrabSetup.cs` - `Apply()` 方法末尾

```csharp
// 新增配置选项
public bool disableCollisionBetweenParts = true;

// 在 Apply() 方法末尾
if (disableCollisionBetweenParts && parts.Count > 1)
{
    for (int i = 0; i < parts.Count; i++)
    {
        for (int j = i + 1; j < parts.Count; j++)
        {
            var colliders1 = parts[i].GetComponentsInChildren<Collider>();
            var colliders2 = parts[j].GetComponentsInChildren<Collider>();
            
            foreach (var col1 in colliders1)
            {
                foreach (var col2 in colliders2)
                {
                    Physics.IgnoreCollision(col1, col2, true);  // ✅ 忽略碰撞
                }
            }
        }
    }
}
```

**工作原理**:
- 使用 `Physics.IgnoreCollision()` 让零件之间互相忽略碰撞
- 这样零件可以重叠，不会互相推开

## 📊 配置对比

### 错误配置（会飞走/碰撞）
```csharp
body.useGravity = true;  // ❌ 会掉落
body.isKinematic = false;  // ❌ 会受物理引擎影响，可能飞走
col.isTrigger = true;  // ❌ XR Interaction 无法检测
// 没有 IgnoreCollision  // ❌ 零件会互相碰撞
```

### 正确配置（稳定可控）
```csharp
body.useGravity = false;  // ✅ 不掉落
body.isKinematic = true;  // ✅ 不受物理引擎影响
col.isTrigger = false;  // ✅ XR Interaction 可以检测
Physics.IgnoreCollision(col1, col2, true);  // ✅ 零件不碰撞
```

## 🎯 工作原理

### Kinematic Rigidbody 的特性
1. **不受力的影响** - 不会因为碰撞而移动
2. **不受重力影响** - 不会掉落
3. **可以通过 transform 移动** - XR Interaction 通过修改 transform 来移动
4. **仍然可以检测碰撞** - 可以触发 OnCollisionEnter 等事件
5. **不会产生物理反应** - 碰撞不会产生力

### 为什么零件不会飞走
```
抓取零件时：
1. XR Interaction 修改 transform.position
2. Rigidbody 是 Kinematic，所以不会产生速度
3. 释放时，没有速度，所以停在原地 ✅

如果是 Non-Kinematic：
1. XR Interaction 修改 transform.position
2. Rigidbody 计算速度（从位置变化推算）
3. 释放时，保持速度，继续飞行 ❌
```

### 为什么零件不会碰撞
```
零件接触时：
1. Physics.IgnoreCollision 让它们互相忽略
2. 即使重叠也不会产生碰撞力
3. 零件可以自由移动，不会被推开 ✅

如果没有 IgnoreCollision：
1. 零件接触时产生碰撞
2. 如果是 Kinematic，碰撞不产生力，但会触发事件
3. 如果是 Non-Kinematic，会互相推开 ❌
```

## 🧪 验证步骤

### 1. 检查日志
```bash
adb logcat -s Unity | findstr "Rigidbody\|Collider\|collision"
```

**期望看到**:
```
[XRGrabSetup] Configured Rigidbody for Part1: isKinematic=True, useGravity=False
[XRGrabSetup] Configured Collider for Part1: type=MeshCollider, isTrigger=False
[XRGrabSetup] Disabled 10 collision pairs between parts
```

### 2. 测试重力
1. 抓取一个零件
2. 移动到空中
3. 释放
4. **零件应该停在原地，不掉落** ✅

### 3. 测试碰撞
1. 抓取 Part1
2. 移动到 Part2 的位置
3. **Part1 和 Part2 应该可以重叠，不会互相推开** ✅

### 4. 测试飞走
1. 抓取一个零件
2. 快速甩动
3. 释放
4. **零件应该立即停止，不会继续飞行** ✅

## ⚠️ 常见问题

### 问题 1: 零件还是会掉落
**原因**: `useGravity = true` 或 `isKinematic = false`

**检查**:
```bash
adb logcat -s Unity | findstr "useGravity"
```

**应该看到**: `useGravity=False`

### 问题 2: 零件释放后飞走
**原因**: `isKinematic = false`

**检查**:
```bash
adb logcat -s Unity | findstr "isKinematic"
```

**应该看到**: `isKinematic=True`

### 问题 3: 零件互相碰撞
**原因**: 没有调用 `Physics.IgnoreCollision`

**检查**:
```bash
adb logcat -s Unity | findstr "Disabled.*collision"
```

**应该看到**: `Disabled N collision pairs between parts`

### 问题 4: 无法抓取零件
**原因**: `isTrigger = true`

**检查**:
```bash
adb logcat -s Unity | findstr "isTrigger"
```

**应该看到**: `isTrigger=False`

## 📝 配置选项

在 Unity Inspector 中，`XRGrabSetup` 组件有以下选项：

```
[Physics]
✓ addRigidbodyIfMissing = true
✓ forceKinematicBody = true

[Collider]
✓ preferMeshCollider = true
✓ forceConvexMeshCollider = true
✓ disableCollisionBetweenParts = true  ← 新增！
```

## 🎉 预期结果

配置正确后：
1. ✅ 每个零件都有 Rigidbody 和 Collider
2. ✅ 零件不受重力影响（不掉落）
3. ✅ 零件不受物理引擎影响（不飞走）
4. ✅ 零件之间不会碰撞（可以重叠）
5. ✅ 零件可以独立抓取和移动
6. ✅ 释放后零件立即停止在原位

## 🔧 高级配置

### 如果需要零件之间有碰撞
设置 `disableCollisionBetweenParts = false`

### 如果需要零件受重力影响
```csharp
body.useGravity = true;
body.isKinematic = false;
```
但这样会导致零件掉落和飞走！

### 如果需要零件有物理反应
```csharp
body.isKinematic = false;
body.drag = 5f;  // 增加阻力，减少飞走
body.angularDrag = 5f;
```
但这样仍然可能飞走，不推荐！

## 📚 Unity 物理系统参考

### Kinematic vs Non-Kinematic
| 特性 | Kinematic | Non-Kinematic |
|------|-----------|---------------|
| 受重力影响 | ❌ | ✅ |
| 受力影响 | ❌ | ✅ |
| 碰撞产生力 | ❌ | ✅ |
| 可以检测碰撞 | ✅ | ✅ |
| 通过 transform 移动 | ✅ | ⚠️ 不推荐 |
| 通过 AddForce 移动 | ❌ | ✅ |

### 我们的选择
- **Kinematic** - 因为我们通过 XR Interaction 直接修改 transform
- **不需要物理模拟** - 零件不需要真实的物理行为
- **需要稳定性** - 零件不能飞走或掉落

---

**最后更新**: 2026-03-09 21:30
**状态**: 完整配置方案
