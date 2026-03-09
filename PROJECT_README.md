# 🎯 Assembly Recorder Demo - 项目文档

> **重要提示**：本文档是项目的核心说明文档，任何 AI Agent 在开始工作前必须先阅读本文档！

---

## 📋 项目概述

### 一句话定位
这是一个 **Unity 双端（手机 + Meta Quest）3D 装配录制与回放系统**，支持"Quest 端录制装配步骤 → 上传服务器 → 手机端按步骤动画回看"。

### 核心价值
- 🎥 **录制**：在 Quest VR 环境中录制 3D 模型的装配过程
- 📱 **回放**：在手机端以动画形式回放装配步骤，支持 360° 查看
- 🔄 **跨端同步**：通过服务器实现 Quest 和手机端的数据同步

---

## 🎯 项目目标

### 核心目标
打通模型导入、任务分发、轨迹录制、步骤上传、录制列表管理、跨端回放的完整闭环。

### 成功标准
- ✅ 手机端可导入 GLB/GLTF 模型并创建任务
- ✅ Quest 端可轮询到任务、加载模型、交互操作、记录步骤并上传
- ✅ 手机端可加载并回放录制，支持步骤列表与逐步动画播放
- ✅ 录制可持久化为 "recording" 并在列表中再次检索

### 当前阶段
**MVP 闭环已实现**，进入稳定性和产品化阶段（账号体系、Android Studio 宿主集成、工程规范化）。

---

## 🏗️ 技术架构

### 技术栈
- **客户端**：Unity 2022+ (C#, URP, TextMeshPro, XR Interaction Toolkit, OpenXR, glTFast)
- **服务端**：Python Flask (简易 HTTP 服务，内存 + JSON 文件落盘)
- **平台**：Android Phone + Meta Quest (Android/OpenXR)
- **调试**：`adb logcat -s Unity`

### 核心模块

#### 📱 手机端 (PhoneScene.unity)
- **导入与模型索引**
  - `ImportManager.cs` - GLB/GLTF 模型导入
  - `ModelIndex.cs` - 模型零件索引与姿态管理
  
- **任务与回放**
  - `PhoneCreateTask.cs` - 创建任务并上传模型
  - `PhoneStepPlayback.cs` - 步骤回放与动画插值
  - `RecordingListUI.cs` - 录制列表管理
  - `StepListUI.cs` - 步骤列表显示
  - `UIController.cs` - UI 总控制器

#### 🥽 Quest 端 (QuestSence.unity)
- **任务与录制**
  - `QuestPollTask.cs` - 轮询任务并下载模型
  - `QuestStepSession.cs` - 步骤录制与上传
  - `ImportManager.cs` - 模型加载（共享）
  - `ModelIndex.cs` - 模型索引（共享）

#### 🌐 网络层
- `ApiClient.cs` - HTTP 请求封装
- `NetModels.cs` - 数据传输对象 (DTO)

#### 🖥️ 服务端
- `server.py` - Flask HTTP 服务
- 数据目录：`D:\assemble server\data\`
  - `tasks/` - 任务元数据
  - `models/` - GLB 模型文件
  - `recordings/` - 录制元数据
  - `recording_steps/` - 录制步骤数据
  - `steps/` - Legacy 步骤数据（已废弃）

---

## 🔄 核心业务流程

### 1. 手机端创建任务流程
```
用户导入 GLB 模型
  ↓
ImportManager.LoadGlbFromPathAsync()
  ↓
PhoneCreateTask.UploadAndCreate()
  ↓
POST /uploadModel (上传模型文件)
  ↓
POST /createTask (创建任务元数据)
  ↓
任务进入 pending 状态，等待 Quest 拉取
```

### 2. Quest 端录制流程
```
QuestPollTask 轮询 GET /pollTask
  ↓
获取 pending 任务
  ↓
GET /downloadModel (下载模型)
  ↓
ImportManager 加载模型
  ↓
用户手柄抓取零件进行装配操作
  ↓
QuestStepSession 录制轨迹（采样 position/rotation/scale）
  ↓
POST /createRecording (创建录制元数据)
  ↓
POST /uploadRecordingSteps (上传步骤数据)
  ↓
录制完成
```

### 3. 手机端回放流程
```
RecordingListUI.RefreshList()
  ↓
GET /listRecordings (获取录制列表)
  ↓
用户点击录制条目
  ↓
GET /downloadRecording (下载录制元数据)
  ↓
GET /downloadRecordingSteps (下载步骤数据)
  ↓
PhoneStepPlayback.EnsureModelLoaded() (加载模型)
  ↓
用户点击 "Play Step" 或 "Next"
  ↓
PhoneStepPlayback.CoPlayStep() (轨迹插值动画播放)
  ↓
模型在当前位置执行动画
```

---

## 📊 数据结构

### Task (任务)
```json
{
  "taskId": "t1772625087295",
  "modelId": "m_20260304_195122_383",
  "modelFile": "m_20260304_195122_383.glb",
  "status": "pending",
  "title": "Assembly Task 20260304_195127",
  "userId": "",
  "createdAt": 1772625087295,
  "updatedAt": 1772625087295
}
```

### Recording (录制)
```json
{
  "recordingId": "r1772625108433",
  "taskId": "t1772625087295",
  "modelId": "m_20260304_195122_383",
  "title": "Recording 20260304_195148",
  "userId": "",
  "createdAt": 1772625108433
}
```

### RecordingSteps (录制步骤)
```json
{
  "recordingId": "r1772625108433",
  "modelId": "m_20260304_195122_383",
  "steps": [
    {
      "duration": 2.5,
      "parts": [...],
      "trajectories": [
        {
          "id": "ImportedModel/Part1",
          "from": { "localPos": {...}, "localRot": {...}, "localScale": {...} },
          "to": { "localPos": {...}, "localRot": {...}, "localScale": {...} },
          "samples": [
            { "t": 0.0, "localPos": {...}, "localRot": {...}, "localScale": {...} },
            { "t": 0.1, "localPos": {...}, "localRot": {...}, "localScale": {...} },
            ...
          ]
        }
      ]
    }
  ]
}
```

---

## 🐛 已知问题与解决方案

### ✅ 已修复问题

#### 1. 动画播放位置偏移问题 (2026-03-04)
**问题**：手机端播放动画时，模型会先移动到远处，然后执行动作，最后再返回原位。

**根本原因**：`EnsureModelLoaded()` 没有检查模型是否已加载，每次播放都重新加载模型，导致 `modelRoot` 引用失效和位置重置。

**解决方案**：
- 添加 `_loadedModelId` 缓存机制
- 实现三层检查（缓存检查、运行时验证、加载后更新）
- 提供 `ClearModelCache()` 方法供手动清除缓存

**修改文件**：`Assets/Scripts/PhoneStepPlayback.cs`

**详细文档**：`BUGFIX_SUMMARY.md`

#### 2. Quest 模型"出现一瞬间后消失"
**根因**：XR Origin 重力/移动设置导致相机/玩家坠落。

**方案**：关闭不当重力影响，修正 XR 移动设置。

**风险**：XR Rig 参数被改回后会复发。

#### 3. Insecure connection not allowed
**根因**：Android/Unity 明文 HTTP 限制。

**方案**：允许 HTTP（Player Settings → Network Security 配置）。

**风险**：生产环境建议 HTTPS + 域名证书。

#### 4. 手机列表一直空
**根因A**：Quest 上传走了 legacy `/uploadSteps`，未生成 recording 元数据。

**根因B**：`/listRecordings?taskId=...` 过滤导致只显示单任务。

**方案**：Quest 上传优先 named recording，列表逻辑支持全量。

**风险**：旧 APK 未更新时仍会走旧逻辑。

#### 5. 步骤回放瞬移
**根因**：只应用终态或无轨迹样本。

**方案**：加入轨迹采样与插值播放，Next/Step 点击走动画路径。

**风险**：若录制时无有效移动，仍可能退化为近似瞬移。

### ⚠️ 待修复问题

#### 1. Quest 端抓握模型时模型缩小
**状态**：待排查

**日志文件**：`D:\灵境项目相关\日志\quest端日志.txt`

**初步分析**：可能与 XR Grab Interactable 的 scale 处理有关

---

## 🚫 不在范围内的功能

- ❌ MP4 视频渲染导出（明确不做）
- ❌ 完整用户账号系统（登录、鉴权、权限管理）
- ❌ Android Studio 超级 App 宿主侧深度集成（仅给出接入路径，未实现）
- ❌ 生产级后端（当前是 demo Python HTTP server，非高并发/高安全架构）

---

## 🔧 开发环境配置

### Unity 配置
- **Unity 版本**：2022.3 LTS 或更高
- **平台**：Android
- **渲染管线**：URP (Universal Render Pipeline)
- **XR 插件**：OpenXR, XR Interaction Toolkit

### 手机端构建设置
- **Platform**：Android
- **XR Settings**：关闭 XR 支持
- **Network Security**：允许 HTTP 明文连接
- **API Level**：Min 25, Target 36

### Quest 端构建设置
- **Platform**：Android
- **XR Settings**：启用 OpenXR
- **XR Plugin Management**：Oculus/Meta Quest
- **Network Security**：允许 HTTP 明文连接
- **API Level**：Min 25, Target 36

### 服务端启动
```bash
cd "D:\assemble server"
.\.venv\Scripts\python.exe -u server.py
# 监听 0.0.0.0:5000
```

### ApiClient 配置
在 Unity Inspector 中设置 `ApiClient.baseUrl` 为服务器 IP（同一局域网）。

---

## 🧪 调试方法

### 客户端日志
```bash
# 手机端
adb logcat -s Unity

# Quest 端
adb logcat -s Unity
```

### 服务端日志
直接查看 Python 控制台输出，或检查数据文件：
- `data/recordings/*.json`
- `data/recording_steps/*.json`
- `data/tasks/*.json`

### 常见日志关键字
- `[PHONE]` - 手机端日志
- `[QUEST]` - Quest 端日志
- `[IMPORT]` - 模型导入日志
- `[PLAYBACK]` - 回放日志
- `[RecordingListUI]` - 录制列表日志

---

## 📝 代码规范

### 命名约定
- **类名**：PascalCase (e.g., `PhoneStepPlayback`)
- **方法名**：PascalCase (e.g., `LoadRecording`)
- **字段名**：camelCase (e.g., `currentIndex`)
- **私有字段**：_camelCase (e.g., `_loadedModelId`)
- **常量**：UPPER_SNAKE_CASE (e.g., `PREF_PHONE_LAST_TASK_ID`)

### 日志规范
```csharp
// 信息日志
Debug.Log($"[MODULE] message with {variable}");

// 警告日志
Debug.LogWarning($"[MODULE] warning message");

// 错误日志
Debug.LogError($"[MODULE] error message: {exception}");
```

### 异步方法规范
```csharp
// 使用 async Task 而非 async void（除非是事件处理器）
public async Task<bool> LoadRecording(string id)
{
    // ...
}

// UI 按钮回调可以使用 async void
public async void OnButtonClick()
{
    await LoadRecording(recordingId);
}
```

---

## 🔄 项目变更记录

### 2026-03-04
- ✅ 修复动画播放位置偏移问题
- ✅ 添加模型加载缓存机制
- ✅ 创建 `BUGFIX_SUMMARY.md` 详细文档
- ✅ 创建 `PROJECT_README.md` 项目文档

### 2026-03-03
- ✅ 修复步骤列表只显示 Step2 的问题
- ✅ 实现轨迹插值动画播放
- ✅ 统一 recording API 为主链路

### 2026-03-02
- ✅ 实现 Quest 端录制上传
- ✅ 实现手机端录制列表
- ✅ 打通端到端闭环

---

## 📚 相关文档

- `BUGFIX_SUMMARY.md` - Bug 修复详细报告
- `PROJECT_CHANGELOG.md` - 项目变更日志（待创建）
- `API_DOCUMENTATION.md` - API 接口文档（待创建）

---

## 🤝 开发协作指南

### 新 Agent 接手流程
1. ✅ **必读**：完整阅读本文档 (`PROJECT_README.md`)
2. ✅ **了解现状**：查看"已知问题与解决方案"章节
3. ✅ **检查日志**：如果有问题，先查看相关日志文件
4. ✅ **理解架构**：熟悉核心模块和业务流程
5. ✅ **开始工作**：基于现有代码进行修改或新增功能

### 修改代码时的注意事项
- ⚠️ 不要破坏已修复的 Bug（查看"已修复问题"章节）
- ⚠️ 保持代码风格一致（遵循"代码规范"章节）
- ⚠️ 添加必要的日志（遵循"日志规范"）
- ⚠️ 更新本文档的"项目变更记录"章节
- ⚠️ 如果修复了 Bug，在"已知问题与解决方案"中记录

### 提交变更时
1. 在"项目变更记录"中添加条目
2. 如果是 Bug 修复，更新"已知问题与解决方案"
3. 如果是重大变更，考虑创建独立的文档（如 `BUGFIX_SUMMARY.md`）

---

## 🎯 下一步计划

### P0 任务
- [ ] 修复 Quest 端抓握模型时模型缩小的问题
- [ ] 统一"录制数据唯一真源"为 recording（移除 legacy steps）

### P1 任务
- [ ] 录制列表可观测性（显示请求 URL + 返回条数 + 首条 ID）
- [ ] 录制管理能力（重命名、删除、按时间排序、分页加载）
- [ ] 用户维度接入（userId 关联与过滤）

### P2 任务
- [ ] 服务端工程化（FastAPI/Flask + DB）
- [ ] Android Studio 宿主集成方案
- [ ] 完整的 API 文档

---

## 📞 联系方式

**项目负责人**：[王乐泉]

**技术支持**：[Unity]

**最后更新**：2026-03-04

---

> 💡 **提示**：本文档会持续更新，请确保在开始工作前阅读最新版本！