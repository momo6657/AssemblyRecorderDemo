# 📝 Assembly Recorder Demo - 项目变更日志

> 本文档记录项目的所有重要变更，包括功能添加、Bug 修复、架构调整等。

---

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

---

## 📅 2026-03-03

### ✅ Bug 修复

#### 步骤列表只显示 Step2
- **问题描述**：手机端步骤列表只显示最后一个步骤
- **根本原因**：UI 刷新/索引更新策略问题
- **解决方案**：`StepListUI` 重新按 step 数重建
- **修改文件**：`Assets/Scripts/StepListUI.cs`
- **影响范围**：手机端步骤列表显示

#### 录制列表为空
- **问题描述**：Quest 上传成功但手机端列表为空
- **根本原因A**：Quest 上传走了 legacy `/uploadSteps`，未生成 recording 元数据
- **根本原因B**：`/listRecordings?taskId=...` 过滤导致只显示单任务
- **解决方案**：
  - Quest 上传优先 named recording API
  - 列表逻辑支持全量显示
- **修改文件**：
  - `Assets/Scripts/QuestStepSession.cs`
  - `Assets/Scripts/RecordingListUI.cs`
- **影响范围**：录制上传与列表显示

### 🎨 功能优化

#### 实现轨迹插值动画播放
- **功能描述**：步骤回放从瞬移改为平滑动画
- **实现方式**：
  - 录制时采样轨迹点（position/rotation/scale）
  - 回放时按时间插值
- **修改文件**：
  - `Assets/Scripts/PhoneStepPlayback.cs`
  - `Assets/Scripts/QuestStepSession.cs`
- **影响范围**：回放体验

---

## 📅 2026-03-02

### 🎉 功能实现

#### 端到端闭环打通
- **功能描述**：完成从手机创建任务 → Quest 录制 → 手机回放的完整流程
- **实现内容**：
  - Quest 端录制上传
  - 手机端录制列表
  - 手机端步骤回放
- **修改文件**：
  - `Assets/Scripts/QuestStepSession.cs`
  - `Assets/Scripts/PhoneStepPlayback.cs`
  - `Assets/Scripts/RecordingListUI.cs`
- **影响范围**：核心业务流程

---

## 📅 2026-03-01

### 🏗️ 架构搭建

#### 初始项目搭建
- **实现内容**：
  - Unity 项目初始化
  - 双端场景创建（PhoneScene, QuestSence）
  - 基础网络层实现（ApiClient）
  - 模型导入功能（ImportManager）
  - 模型索引功能（ModelIndex）
- **影响范围**：整体架构

#### 服务端搭建
- **实现内容**：
  - Flask HTTP 服务
  - 任务管理 API
  - 模型上传下载 API
  - 录制管理 API
- **文件位置**：`D:\assemble server\server.py`
- **影响范围**：后端服务

---

## 📋 变更记录模板

```markdown
## 📅 YYYY-MM-DD

### ✅ Bug 修复 / 🎨 功能优化 / 🎉 功能实现 / 🏗️ 架构调整 / 📚 文档更新

#### 变更标题
- **问题描述**：（如果是 Bug 修复）
- **功能描述**：（如果是新功能）
- **根本原因**：（如果是 Bug 修复）
- **解决方案**：
  - 要点 1
  - 要点 2
- **修改文件**：
  - `path/to/file1.cs`
  - `path/to/file2.cs`
- **详细文档**：（如果有独立文档）
- **影响范围**：
- **需要重新打包**：✅ 手机端 / ✅ Quest 端 / ❌ 无需打包
```

---

## 📊 统计信息

### 总体进度
- ✅ 核心功能：已完成
- 🔄 稳定性优化：进行中
- ⏳ 产品化：待开始

### Bug 修复统计
- 已修复：5 个
- 待修复：1 个（Quest 抓握模型缩小）

### 功能完成度
- 模型导入：✅ 100%
- 任务创建：✅ 100%
- Quest 录制：✅ 100%
- 手机回放：✅ 100%
- 录制列表：✅ 100%
- 用户系统：❌ 0%（不在范围内）

---

## 🔗 相关文档

- [PROJECT_README.md](./PROJECT_README.md) - 项目核心文档
- [BUGFIX_SUMMARY.md](./BUGFIX_SUMMARY.md) - Bug 修复详细报告
- [.cursorrules](./.cursorrules) - Cursor AI 工作规则

---

**最后更新**：2026-03-04  
**维护者**：AI Agent + 项目负责人
