# 📝 Assembly Recorder Demo - 项目变更日志

> 本文档记录项目的所有重要变更，包括功能添加、Bug 修复、架构调整等。

---

## 📅 2026-04-22

### 🚀 云部署改造

#### 后端 server.py 云部署适配
- **功能描述**：将后端从"本地 demo 服务"改造为"可部署到云服务器"的版本
- **解决方案**：
  - 所有配置改为环境变量读取（`ASSEMBLE_HOST`、`ASSEMBLE_PORT`、`ASSEMBLE_DATA_DIR`、`ASSEMBLE_API_KEY`、`ASSEMBLE_MAX_UPLOAD_MB`）
  - 增加 API Key 鉴权（通过 `X-Api-Key` 请求头校验，`/ping` 免鉴权）
  - 增加上传大小限制（默认 100MB，超出返回 413）
  - 增加 CORS 支持（`Access-Control-Allow-Origin: *`）
  - 增加 OPTIONS 预检请求处理
  - 增加结构化日志（同时输出到控制台和 `data/logs/server.log`）
  - 关键操作（任务创建、录制创建、模型上传等）增加日志记录
- **修改文件**：`D:\assemble server\server.py`
- **影响范围**：后端全部接口
- **向后兼容**：✅ 不设 API Key 时行为与改造前完全一致

#### 客户端 ApiClient.cs 适配
- **功能描述**：客户端支持 API Key 鉴权、全局服务器配置、Local/Cloud 双环境切换与运行时地址覆盖
- **解决方案**：
  - 新增 `ServerConfig.cs` ScriptableObject 作为全局服务器配置
  - 约定从 `Resources/ServerConfig.asset` 自动加载环境配置与默认 API Key
  - `ServerConfig` 新增 `environment`、`localBaseUrl`、`cloudBaseUrl`
  - `ApiClient.cs` 按当前环境自动选择本地地址或云端地址
  - 保留组件级 `baseUrl` 覆盖能力，但受全局配置项控制
  - 保留 `PlayerPrefs` 运行时地址覆盖能力，便于切换本地调试环境
  - 所有请求自动附加 `X-Api-Key` 头（组件级覆盖优先，否则走全局配置）
- **修改文件**：
  - `Assets/Scripts/net/ApiClient.cs`
  - `Assets/Scripts/net/ServerConfig.cs`
  - `Assets/Resources/ServerConfig.asset`
- **影响范围**：手机端 + Quest 端网络请求配置
- **需要重新打包**：✅ 手机端 + Quest 端

#### 部署配置文件
- **新增文件**：
  - `D:\assemble server\requirements.txt` - 依赖声明
  - `D:\assemble server\.env.example` - 环境变量模板
  - `D:\assemble server\start.sh` - Linux 启动脚本
  - `D:\assemble server\deploy\assemble-server.service` - systemd 服务文件
  - `D:\assemble server\deploy\Caddyfile` - Caddy 反向代理配置
  - `D:\assemble server\deploy\nginx.conf` - Nginx 反向代理配置

## 📅 2026-04-09

### 🎨 功能优化

#### 手机端 UI Toolkit 播放页重构与视频教程化样式
- **功能描述**：将手机端播放页改造成更接近苹果风纯播放器的深灰界面，同时保留 Create Task 与 Import GLB 功能但默认隐藏
- **解决方案**：
  - 重构 `MainView.uxml` 的层级与信息文案，突出播放器与步骤查看体验
  - 删除顶部统一控制条，改为每个 UI 模块内置最小化按钮与边缘恢复按钮
  - 最小化后以贴边小按钮保留在原相对区域附近，点击即可恢复
  - 重写 `MainStyle.uss` 的高级灰深色卡片样式，避免过黑与按钮重叠
  - 修正 `PhonePlaybackPanelSettings.asset` 与 `PhonePlaybackPanelSettings_Real.asset` 的运行时缩放模式与参考分辨率，避免手机真机布局挤压
  - 提高真机 `PhonePlaybackPanelSettings_Real.asset` 缩放倍率，并放大主要面板尺寸、字号与按钮，提升协调性与高级感
  - 再次增大纵屏真机下的左右底部面板宽度、状态卡尺寸与交互控件尺寸，解决“整体仍然偏小”问题
  - 修复 `PhonePlaybackPanelSettings_Real.asset` 被回退为旧缩放模式的问题，确保真机放大配置真正生效
  - 再次显著增大所有 UI 模块，尤其放大顶部 Import 区与右上状态卡，按手机首屏交互尺寸进行调整
  - 继续增大顶部两个模块，同时缩窄左右底部模块宽度并提高高度，避免底部重叠
  - 统一放大所有文字字号，使标题、状态、列表、表单与按钮文案匹配当前大号 UI 模块尺寸
  - 针对 Import GLB、Playback 状态区、Jump / Prev / Play Next 等仍偏小的文字继续增大字号
  - 修复 UI Toolkit 大面积容器拦截触摸的问题，恢复点击零件选中与手指滑动旋转视角
  - 进一步将触摸判定从泛化的 EventSystem UI 判断改为仅拦截真实交互控件，避免普通面板区域误判为 UI
  - 结合手机端日志确认模型、SelectionManager 与 OrbitPinchCamera 均已绑定后，移除对 EventSystem 的兜底拦截，避免 UI Toolkit 全屏面板持续误判
  - 为 `PhonePlaybackUIDocumentController.cs` 增加每个模块独立的运行时显示/隐藏开关
  - 调整 Inspector 勾选逻辑：取消勾选时模块直接隐藏，仅在模块内点击减号时进入最小化并显示恢复按钮
  - 将 `showImportButton` 与 `showTaskPanel` 默认值改为隐藏，按需打开
  - 保持原有回放、导入、创建任务逻辑不变，仅调整 View 层
- **修改文件**：
  - `Assets/Scripts/PhonePlaybackUIDocumentController.cs`
  - `Assets/UI/PhonePlayback/MainView.uxml`
  - `Assets/UI/PhonePlayback/MainStyle.uss`
- **影响范围**：手机端播放页 UI
- **需要重新打包**：✅ 手机端

#### 手机端 UI Toolkit 播放页重构骨架
- **功能描述**：新增一套 UI Toolkit 手机端播放页骨架，目标视觉对齐新的四角卡片式深色 UI
- **解决方案**：
  - 新增 `PhonePlaybackUIDocumentController.cs` 作为 UI Toolkit 主控制器
  - 新增 `MainView.uxml`、`MainStyle.uss`、`PhonePlaybackPanelSettings.asset`
  - `PhoneCreateTask.cs` 增加 UI Toolkit 输入兼容字段与状态缓存
  - `PhoneStepPlayback.cs` 增加 `LastStatus` 状态缓存供 UI Toolkit 状态卡读取
- **修改文件**：
  - `Assets/Scripts/PhonePlaybackUIDocumentController.cs`
  - `Assets/Scripts/PhoneCreateTask.cs`
  - `Assets/Scripts/PhoneStepPlayback.cs`
  - `Assets/UI/PhonePlayback/MainView.uxml`
  - `Assets/UI/PhonePlayback/MainStyle.uss`
  - `Assets/UI/PhonePlayback/PhonePlaybackPanelSettings.asset`
- **影响范围**：手机端播放页 UI
- **需要重新打包**：✅ 手机端

## 📅 2026-03-04

### ✅ Bug 修复

#### 动画播放位置偏移问题
- **问题描述**：手机端播放动画时，模型会先移动到远处，然后执行动作，最后再返回原位
- **根本原因**：`EnsureModelLoaded()` 没有检查模型是否已加载，每次播放都重新加载模型
- **解决方案**：
  - 添加 `_loadedModelId` 缓存机制
  - 实现三层检查（缓存检查、运行时验证、加载后更新）
  - 提供 `ClearModelCache()` 方法供手动清除缓存
- **修改文件**：`Assets/Scripts/PhoneStepPlayback.cs`
- **详细文档**：`BUGFIX_SUMMARY.md`
- **影响范围**：手机端回放功能
- **需要重新打包**：✅ 手机端

### 📚 文档更新

#### 创建项目核心文档
- **新增文件**：
  - `PROJECT_README.md` - 项目核心文档（必读）
  - `BUGFIX_SUMMARY.md` - Bug 修复详细报告
  - `.cursorrules` - Cursor AI 工作规则
  - `PROJECT_CHANGELOG.md` - 本文档
- **目的**：建立项目知识库，方便新 Agent 快速了解项目
- **影响范围**：开发流程
