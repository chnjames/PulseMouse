# 稀土掘金作品广场投稿材料

以下内容依据当前 `v0.9.0` 预发布代码、已通过的 Core 冒烟测试及公开 README 编写。不要把尚未完成的真机验收、安装器、代码签名或自动更新写成已发布能力。

## 作品名称

PulseMouse：鼠标上的节奏快捷键

## 一句话简介

按住右键、左键敲节奏，松开即可在不移动光标的情况下执行高频操作；同一节奏还能随光标所在程序切换含义。

## 详细介绍

PulseMouse 是一个面向 Windows 的鼠标优先效率工具。它不试图替代启动器、命令面板或宏平台，而是为已经熟记的 3–8 个高频动作提供一层“盲操作”触发方式：按住右键，用左键输入短按 `·` 与长按 `—` 的节奏，松开右键立即执行。鼠标不必移动，视线也不必离开正在处理的内容。

v0.9 的核心是 **Context Pulse**。PulseMouse 会在开始输入时记录光标下的窗口，让相同节奏在 Chrome、资源管理器、Excel 等程序中拥有不同含义；固定的全局动作则始终保持一致。快捷键还能先激活目标窗口再发送，避免把操作误发到错误的前台程序。

可绑定的动作包括：打开程序、文件夹与网址，发送快捷键，执行需确认的工作流，重复上一次成功动作，以及中键四方向手势。为了尽量不破坏原有操作，普通右键、右键拖拽、排除程序、全屏程序和不适合注入的高权限目标都会回退到原生行为。设置与统计仅保存在本机，不需要账户，也不上传遥测数据。

当前版本为 `v0.9.0` 预发布版：项目可成功构建，Core 冒烟测试已通过；全局鼠标钩子、模拟输入、窗口激活和多显示器缩放仍在按真机验收清单验证。项目采用 MIT License 开源。

## 产品 Logo（1:1）

- 上传文件：[`docs/assets/pulsemouse-logo-1024.png`](assets/pulsemouse-logo-1024.png)
- 尺寸：1024 × 1024 PNG
- 使用说明：这是应用内图标的官方高分辨率导出，不要替换为生成图。

## 介绍图片（最多 5 张）

按以下顺序上传；这 5 张都是正在运行的 `v0.9.0` 应用实机截图，而不是设计稿或生成的 UI：

1. [`docs/assets/screenshots/01-dashboard.jpg`](assets/screenshots/01-dashboard.jpg) — **仪表盘**：一眼看出“按住右键、左键输入点划节奏、松开执行”的实时输入区，以及已绑定动作和输入方式。
2. [`docs/assets/screenshots/02-commands.jpg`](assets/screenshots/02-commands.jpg) — **指令库**：展示节奏地图、`·` / `—` 输入、动作类型和 `Ctrl+C` 等实际绑定。
3. [`docs/assets/screenshots/03-gestures.jpg`](assets/screenshots/03-gestures.jpg) — **方向手势**：展示中键上下左右手势、动作绑定和阈值设置。
4. [`docs/assets/screenshots/04-workflows.jpg`](assets/screenshots/04-workflows.jpg) — **工作流**：展示最多 12 步的本地自动化编辑器与执行前确认机制。
5. [`docs/assets/screenshots/05-settings.jpg`](assets/screenshots/05-settings.jpg) — **设置**：展示按程序切换含义、屏幕状态胶囊、隐私本地化与备份导入导出。

不要把 [`docs/assets/pulsemouse-cover.png`](assets/pulsemouse-cover.png) 当作介绍图上传；它只是品牌概念封面，不能充分说明产品。

## 体验链接

体验链接：<https://github.com/chnjames/PulseMouse/releases/tag/v0.9.0>

这是源码预发布链接。用户可下载 GitHub 自动提供的 Source code，按照 README 使用 .NET 10 SDK 构建。便携版 Windows 附件会在网络恢复后补传，不要将其标注为已有下载包。

## 代码仓库

代码仓库：<https://github.com/chnjames/PulseMouse>

仓库建议为公开仓库，描述为：

> Mouse-first shortcuts for Windows — tap rhythms to run commands without moving the pointer.

建议标签：`windows`、`wpf`、`dotnet`、`csharp`、`mouse-shortcuts`、`mouse-gestures`、`productivity`、`automation`、`global-hotkeys`、`desktop-application`。
