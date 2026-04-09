# 📝 Assembly Recorder Demo - 项目变更日志

> 本文档记录项目的所有重要变更，包括功能添加、Bug 修复、架构调整等。

---

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
