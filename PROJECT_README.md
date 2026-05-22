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
- **客户端**：Unity 6 (C#, URP, TextMeshPro, XR Interaction Toolkit, OpenXR, glTFast, UI Toolkit)
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
  - `PhonePlaybackUIDocumentController.cs` - UI Toolkit 手机端主界面控制器
  - `RecordingListUI.cs` - 录制列表管理（旧 UGUI）
  - `StepListUI.cs` - 步骤列表显示（旧 UGUI）
  - `UIController.cs` - UI 总控制器（旧 UGUI）

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
```text
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
```text
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
```text
PhonePlaybackUIDocumentController.RefreshRecordingsAsync()
  ↓
GET /listRecordings (获取录制列表，可按 logicalModelId/modelType/modelHash 过滤打包版本对应录制)
  ↓
用户点击录制条目
  ↓
GET /downloadRecording (下载录制元数据)
  ↓
GET /downloadRecordingSteps (下载录制步骤数据)
  ↓
PhoneStepPlayback.EnsureModelLoaded() (加载模型)
  ↓
用户点击 Step / Prev / Next
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
  "logicalModelId": "engine_v1",
  "modelType": "engine",
  "modelHash": "sha256...",
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
  "logicalModelId": "engine_v1",
  "modelType": "engine",
  "modelHash": "sha256...",
  "createdAt": 1772625108433
}
```

---

## 🐛 已知问题与解决方案

### ✅ 已修复问题

#### 1. 动画播放位置偏移问题 (2026-03-04)
**问题**：手机端播放动画时，模型会先移动到远处，然后执行动作，最后再返回原位。

**根本原因**：`EnsureModelLoaded()` 没有检查模型是否已加载，每次调用都会重新加载模型。

**解决方案**：
- 添加 `_loadedModelId` 缓存机制
- 实现三层检查（缓存检查、运行时验证、加载后更新）
- 提供 `ClearModelCache()` 方法供手动清除缓存

**修改文件**：`Assets/Scripts/PhoneStepPlayback.cs`

#### 2. 手机端 UI 切换到 UI Toolkit 观看器样式 (2026-04-09)
**目标**：把手机端播放页升级为更接近苹果风纯播放器的深灰卡片式 UI。

**解决方案**：
- 新增 `PhonePlaybackUIDocumentController.cs`
- 新增 `Assets/UI/PhonePlayback/MainView.uxml`
- 新增 `Assets/UI/PhonePlayback/MainStyle.uss`
- 新增 `Assets/UI/PhonePlayback/PhonePlaybackPanelSettings.asset`
- `PhoneCreateTask.cs` 增加 UI Toolkit 输入兼容和状态缓存
- `PhoneStepPlayback.cs` 增加 `LastStatus` 供状态卡读取
- `PhonePlaybackUIDocumentController.cs` 增加每个模块独立的运行时显示/隐藏开关
- Inspector 取消勾选时对应模块直接隐藏，仅在模块内点击减号时进入最小化
- 每个 UI 模块内置最小化按钮，并在对应边缘保留恢复按钮
- 修正 `PhonePlaybackPanelSettings.asset` 与 `PhonePlaybackPanelSettings_Real.asset` 的缩放设置，使真机分辨率下与编辑器布局一致
- 进一步提高真机 `PhonePlaybackPanelSettings_Real.asset` 的缩放倍率，并放大主要 UI 模块尺寸与字号
- 针对 1260x2600 纵屏再次大幅提升移动端面板宽度、按钮高度与列表可读性
- 修正真机配置被回退的问题：确保 `PhonePlaybackPanelSettings_Real.asset` 使用参考分辨率缩放并显著增大 `m_Scale`
- 再次显著增大所有 UI 模块，尤其放大顶部 Import 按钮区与右上状态卡
- 继续增大顶部两个模块，同时缩窄底部左右模块宽度并提高高度，避免重叠
- 统一放大所有文字字号，使文字与当前放大的 UI 模块匹配
- 针对 Import GLB、Playback 状态区、Jump / Prev / Play Next 等仍偏小的文字继续增大字号
- 修复 UI Toolkit 大面积容器拦截触摸的问题，恢复零件选中与单指旋转视角
- 进一步把输入屏蔽逻辑收敛到真正可交互控件，避免普通卡片背景区域错误拦截触摸
- 结合手机端日志确认输入链路绑定正常后，移除 EventSystem 的兜底拦截，避免 UI Toolkit 全屏面板误伤模型点击与旋转
- Create Task 与 Import GLB 保留但默认隐藏，不改变原有逻辑
- 保留老版本录制的步骤展示方式，因此右下区域显示为 `Steps`

**影响范围**：手机端播放页 UI

### ⚠️ 待修复问题

#### 1. Quest 端抓握模型时模型缩小
**状态**：待排查

**日志文件**：`D:\灵境项目相关\日志\quest端日志.txt`

---

## 🚫 不在范围内的功能

- ❌ MP4 视频渲染导出（明确不做）
- ❌ 完整用户账号系统（登录、鉴权、权限管理）
- ❌ Android Studio 超级 App 宿主侧深度集成（仅给出接入路径，未实现）
- ❌ 生产级后端（当前是 demo Python HTTP server，非高并发/高安全架构）

---

## 🔧 开发环境配置

### Unity 配置
- **Unity 版本**：6000.3.9 LTS 或更高
- **平台**：Android
- **渲染管线**：URP (Universal Render Pipeline)
- **XR 插件**：OpenXR, XR Interaction Toolkit
- **UI**：PhoneScene 推荐使用 UI Toolkit `UIDocument`

### 服务端启动
```bash
# 本地开发（默认无鉴权，数据存 ./data）
cd "D:\assemble server"
.\.venv\Scripts\python.exe -u server.py

# 云服务器部署（配置 .env 后用 start.sh）
cp .env.example .env   # 编辑 .env 设置 API_KEY 等
chmod +x start.sh
./start.sh
```

---

## 📝 代码规范

### 命名约定
- **类名**：PascalCase (e.g., `PhoneStepPlayback`)
- **方法名**：PascalCase (e.g., `LoadRecording`)
- **字段名**：camelCase (e.g., `currentIndex`)
- **私有字段**：_camelCase (e.g., `_loadedModelId`)

---

## 🔄 项目变更记录

### 2026-04-25
- ✅ 新增 `PlaybackAppConfig` 打包版本播放配置，可配置 `logicalModelId` / `modelType` / `modelHash` 与录制列表锁定开关
- ✅ 手机端创建任务上传 GLB 后计算 SHA256 `modelHash`，并随 `/createTask` 写入 logical/model 元数据
- ✅ Quest 端从 `/pollTask` 保存 logical/model 元数据，并在 `/createRecording` 与 `/uploadRecordingSteps` 中继续传递
- ✅ 手机端 UI Toolkit 录制列表在锁定配置开启时通过 `/listRecordings` 传入 `logicalModelId` / `modelType` / `modelHash` 过滤参数
- ✅ 后端录制接口保存并返回新字段，`/listRecordings` 支持 `taskId` / `userId` / `modelId` / `logicalModelId` / `modelType` / `modelHash` AND 组合过滤
- ✅ 保持播放时使用 recording/steps 中的 `modelId` 下载服务器模型，不用 `modelId` 做打包版本过滤
- ✅ 大幅增大网络请求、模型上传下载与 GLB 导入超时，适配云服务器大模型/慢网络场景

### 2026-04-22
- ✅ 后端 `server.py` 云部署改造（环境变量配置、API Key 鉴权、上传大小限制、日志文件、CORS）
- ✅ 客户端 `ApiClient.cs` 增加 API Key 支持与可配置服务器地址
- ✅ 客户端网络配置升级为 `ServerConfig` 全局配置方案（`Resources/ServerConfig.asset`）
- ✅ `ServerConfig` 升级为 Local / Cloud 双环境配置，可手动切换本地开发与云端发布
- ✅ 新增部署配置文件（systemd、Nginx、Caddy、启动脚本、环境变量模板）

### 2026-04-09
- ✅ 新增 UI Toolkit 手机端播放页骨架
- ✅ 新增 `PhonePlaybackUIDocumentController.cs`
- ✅ 新增 `MainView.uxml`、`MainStyle.uss`、`PhonePlaybackPanelSettings.asset`
- ✅ `PhoneCreateTask.cs` 与 `PhoneStepPlayback.cs` 增加 UI Toolkit 兼容状态
- ✅ 手机端 UI 调整为更接近苹果风纯播放器的深色卡片式布局
- ✅ 每个 UI 模块支持在运行时独立显示/隐藏
- ✅ 顶部新增模块控制条，可在观看时快速开关各 UI 模块
- ✅ Import GLB 与 Create Task 默认隐藏，按需在运行时打开
- ✅ 右下区域文案保留为 `Steps`，兼容老版本录制步骤播放认知

### 2026-03-04
- ✅ 修复动画播放位置偏移问题
- ✅ 添加模型加载缓存机制
- ✅ 创建 `BUGFIX_SUMMARY.md` 详细文档
- ✅ 创建 `PROJECT_README.md` 项目文档

---

## 📚 相关文档

- `BUGFIX_SUMMARY.md` - Bug 修复详细报告
- `PROJECT_CHANGELOG.md` - 项目变更日志
- `PROJECT_REQUIREMENTS.md` - 项目需求管理文档

---

## 🤝 开发协作指南

### 修改代码时的注意事项
- ⚠️ 不要破坏已修复的 Bug（查看"已修复问题"章节）
- ⚠️ 手机端 UI 迁移优先保留 `PhoneStepPlayback` 业务逻辑，替换 View 层
- ⚠️ 保持代码风格一致
