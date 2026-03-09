# 录制列表同步问题修复文档

## 🐛 问题描述
服务器端删除录制数据后，客户端列表仍显示已删除的条目，点击后无响应。

## ✅ 已实施的修复

### 1. 自动刷新机制
- **应用重新获得焦点时自动刷新**：从后台返回时自动更新列表
- **加载失败时自动刷新**：点击已删除的录制后，1.5秒后自动刷新列表

### 2. 增强错误处理
- **友好的错误提示**：明确告知用户录制可能已被删除
- **详细的日志输出**：便于调试和问题追踪
- **空响应检测**：检测服务器返回的空数据

### 3. 手动刷新功能
- 添加 `ForceRefresh()` 公开方法
- 可绑定到 UI 按钮供用户手动刷新

## 📝 修改的文件

### RecordingListUI.cs
1. 添加 `OnApplicationFocus()` - 应用获得焦点时刷新
2. 增强 `OnRecordingClicked()` - 加载失败时自动刷新
3. 添加 `ForceRefresh()` - 强制刷新方法
4. 增强 `RefreshList()` - 添加详细日志和状态提示

### PhoneStepPlayback.cs
1. 增强 `LoadRecording()` - 检测空响应和错误状态
2. 改进错误消息 - 更友好的用户提示
3. 添加详细日志 - 便于问题诊断

## 🎮 使用方法

### 方法 1：自动刷新（推荐）
**无需任何操作**，系统会在以下情况自动刷新：
- 打开录制列表界面时
- 从后台返回应用时
- 点击已删除的录制后（1.5秒延迟）

### 方法 2：手动刷新按钮
在 Unity Editor 中设置：

1. 打开 `PhoneScene.unity`
2. 找到包含 `RecordingListUI` 组件的 GameObject
3. 添加一个 UI Button（如果还没有）
4. 在 Button 的 `OnClick()` 事件中：
   - 拖入 `RecordingListUI` 组件
   - 选择 `RecordingListUI.ForceRefresh()`

### 方法 3：代码调用
```csharp
// 在其他脚本中调用
var listUI = FindFirstObjectByType<RecordingListUI>();
if (listUI != null)
{
    listUI.ForceRefresh();
}
```

## 🧪 测试步骤

### 测试场景 1：删除服务器数据
1. 手机端打开录制列表，记录显示的录制数量（例如：3个）
2. 在服务器端删除一个录制：
   ```bash
   # 删除 recording 元数据
   del "D:\assemble server\data\recordings\xxx.json"
   # 删除 recording steps
   del "D:\assemble server\data\recording_steps\xxx.json"
   ```
3. 手机端点击刷新按钮（或切换到后台再返回）
4. **期望结果**：列表更新为 2 个录制

### 测试场景 2：点击已删除的录制
1. 手机端打开录制列表
2. 在服务器端删除某个录制（不刷新客户端列表）
3. 手机端点击被删除的录制条目
4. **期望结果**：
   - 显示错误提示："Recording not found. It may have been deleted."
   - 1.5秒后自动刷新列表
   - 该条目从列表中消失

### 测试场景 3：网络错误
1. 关闭服务器
2. 手机端点击刷新按钮
3. **期望结果**：显示网络错误提示

## 📊 日志输出示例

### 正常刷新
```
[RecordingListUI] Refreshing...
[RecordingListUI] Requesting recordings with taskFilter=NULL (all)
[RecordingListUI] Received 3 recordings from server.
[RecordingListUI] loaded 3 recordings.
```

### 点击已删除的录制
```
[RecordingListUI] Loading rec_123...
[PHONE] Recording not found (empty response). It may have been deleted.
[RecordingListUI] Failed to load rec_123. It may have been deleted. Refreshing list...
[RecordingListUI] Refreshing...
[RecordingListUI] Received 2 recordings from server.
[RecordingListUI] loaded 2 recordings.
```

### 加载失败
```
[PHONE] Recording not found: file not found
[RecordingListUI] Failed to load rec_456. It may have been deleted. Refreshing list...
```

## 🔍 调试命令

### 查看客户端日志
```bash
# Android 手机
adb logcat -s Unity | grep -E "RecordingListUI|PHONE"

# 或者只看录制列表相关
adb logcat -s Unity | grep RecordingListUI
```

### 查看服务器文件
```bash
# 查看 recording 元数据数量
dir "D:\assemble server\data\recordings\*.json" /b | find /c /v ""

# 查看 recording steps 数量
dir "D:\assemble server\data\recording_steps\*.json" /b | find /c /v ""

# 列出所有 recording ID
dir "D:\assemble server\data\recordings\*.json" /b
```

## ⚙️ 配置选项

在 Unity Inspector 中可配置：

### RecordingListUI 组件
- `Auto Refresh On Enable` (默认: true) - 界面激活时自动刷新
- `Min Refresh Interval Sec` (默认: 0.4) - 最小刷新间隔，防止频繁请求
- `Always Show All Recordings` (默认: true) - 显示所有录制，不按任务过滤

## 🚀 后续优化建议

### P1 - 下拉刷新手势
```csharp
// 在 RecordingListUI 中添加
public ScrollRect scrollRect;

void Update()
{
    if (scrollRect != null && scrollRect.verticalNormalizedPosition > 1.1f)
    {
        // 用户拉到顶部以上，触发刷新
        ForceRefresh();
    }
}
```

### P2 - 定时自动刷新
```csharp
[Header("Auto Refresh")]
public bool enableAutoRefresh = false;
public float autoRefreshIntervalSec = 30f;

float _nextAutoRefreshAt;

void Update()
{
    if (enableAutoRefresh && Time.unscaledTime >= _nextAutoRefreshAt)
    {
        RefreshList();
        _nextAutoRefreshAt = Time.unscaledTime + autoRefreshIntervalSec;
    }
}
```

### P3 - 缓存验证
```csharp
// 在 PhoneStepPlayback 中添加
Dictionary<string, long> _recordingTimestamps = new Dictionary<string, long>();

public async Task<bool> LoadRecording(string id)
{
    // 先检查服务器上的时间戳
    var meta = await DownloadRecordingMeta(id);
    if (meta != null && _recordingTimestamps.TryGetValue(id, out var cachedTime))
    {
        if (meta.updatedAt <= cachedTime)
        {
            // 使用缓存
            return LoadFromCache(id);
        }
    }
    // 下载新数据
    // ...
}
```

## 📞 问题排查

### 问题：刷新后列表仍显示旧数据
**检查**：
1. 确认服务器文件确实已删除
2. 查看日志中的 "Received X recordings" 数量
3. 检查 `alwaysShowAllRecordings` 是否为 true

### 问题：点击录制无反应
**检查**：
1. 查看日志是否有 "Recording not found" 消息
2. 确认网络连接正常
3. 检查服务器是否运行

### 问题：刷新按钮不工作
**检查**：
1. 确认 Button 的 OnClick 事件已正确绑定
2. 查看日志是否有 "Refresh already in progress" 消息
3. 等待 0.4 秒后重试（最小刷新间隔）

## 📅 更新日志

### 2026-02-28
- ✅ 添加应用焦点自动刷新
- ✅ 添加加载失败自动刷新
- ✅ 增强错误提示和日志
- ✅ 添加 ForceRefresh 公开方法
- ✅ 改进空响应检测

---

**维护者**: Cursor AI Assistant  
**最后更新**: 2026-02-28
