#  CallREC-Scribe 🎙️

**一个跨平台的通话录音转录工具，旨在简化整理和查看通话录音内容的过程。**

[![.NET MAUI](https://img.shields.io/badge/.NET-MAUI-blueviolet.svg)](https://dotnet.microsoft.com/apps/maui)
[![Platform](https://img.shields.io/badge/platform-Android%20%7C%20Windows-brightgreen.svg)](https://dotnet.microsoft.com/apps/maui)
[![License](https://img.shields.io/badge/license-BSD--3--Clause-blue.svg)](LICENSE.txt)

*CallREC-Scribe* 能够帮助您快速将大量的通话录音文件（例如由第三方应用录制的音频）通过腾讯云语音识别服务（ASR）转换成文字，并提供统一的界面进行管理、导出和删除。

---

## ✨ 主要功能

*   **📁 文件夹浏览与持久化访问**: 在 Android 和 Windows 上方便地选择并授权录音文件所在的文件夹。
*   **📝 智能文件名解析**:
    *   内置标准解析器，可从 `联系人@手机号_录制时间.mp3` 格式的文件名中提取信息。
    *   支持**自定义正则表达式**，以适应各种不同的文件名格式。
*   **☁️ 腾讯云语音识别 (ASR)**:
    *   集成腾讯云 ASR API，支持长短音频的自动识别。
    *   可配置识别引擎（如 `8k_zh` 通话模型或 `16k_zh` 普通话模型）。
    *   安全地使用 `TC3-HMAC-SHA256` 签名算法进行 API 请求。
*   **🔄 批量处理与并行任务**:
    *   支持多选文件进行批量转录、导出或删除。
    *   采用并行任务处理，即使个别文件处理时间很长，也不会阻塞整个应用。
*   **💾 本地数据库缓存**:
    *   使用 SQLite 数据库在本地存储文件信息和转录结果，加快二次加载速度。
    *   即使原始音频文件被删除，依然可以保留其关联的记录和转录文本。
*   **📄 灵活的导出选项**:
    *   导出单个文件时，生成格式精美的 Word (`.docx`) 文档。
    *   导出多个文件时，生成结构清晰的 Excel (`.xlsx`) 表格。
*   **⚙️ 跨平台音频处理**:
    *   内置音频转换服务，可在上传前自动将 `m4a`, `wav`, `ogg` 等格式统一转换为 ASR 服务支持的采样率和格式。
*   **📱 跨平台 UI**:
    *   基于 .NET MAUI 构建，确保在 Windows 和 Android 平台拥有一致的用户体验。

## 📦 下载与安装

您可以直接从 [GitHub Releases](https://github.com/Tokisaki-Galaxy/CallREC-Scribe/releases) 页面下载最新的预编译版本。

## 🚀 快速开始开发

### 环境要求

1.  **.NET 8 SDK**: [下载并安装](https://dotnet.microsoft.com/download/dotnet/8.0)
2.  **腾讯云账户**:
    *   开通 [语音识别 ASR 服务](https://cloud.tencent.com/product/asr)。
    *   在 [API 密钥管理](https://console.cloud.tencent.com/cam/capi) 页面获取 `SecretId` 和 `SecretKey`。

### 运行项目

1.  克隆本仓库。
2.  使用 Visual Studio 2022 打开 `CallREC-Scribe.sln` 解决方案。
3.  选择目标平台（例如 `net8.0-android` 或 `net8.0-windows...`）。
4.  点击运行按钮进行部署和调试。

## ⚙️ 应用配置

在开始使用前，您需要进行简单的配置：

1.  **API 配置**:
    *   点击主界面的 **"语音识别配置"** 按钮。
    *   在弹窗中输入您的腾讯云 `SecretId` 和 `SecretKey`。
    *   根据您的录音文件类型选择合适的**引擎类型**（通话录音通常选择 `8k_zh`）。
    *   点击 **"保存"**。

2.  **文件名解析配置**:
    *   点击主界面的 **"文件名解析配置"** 按钮。
    *   **Standard 模式**: 适用于 `联系人@13800138000_20230828123000.mp3` 格式的文件名。
    *   **CustomRegex 模式**: 如果您的文件名格式不同，请选择此模式并提供一个包含 `(?<phone>...)` 和 `(?<date>...)` 命名捕获组的正则表达式。

## 📖 使用指南

1.  **选择文件夹**: 点击 **"浏览..."** 按钮，选择包含您通话录音的文件夹。
2.  **加载文件**: 程序会自动加载文件夹中的音频文件，并根据解析规则显示日期和号码。
3.  **选择文件**:
    *   点击列表中的任意一项来切换其选中状态。
    *   使用 **"全选"** 或 **"反选"** 按钮进行批量选择。
4.  **开始转录**: 选中一个或多个文件后，点击 **"转译选中"**。程序将显示进度，并依次处理所有选中的文件。
5.  **查看结果**: 转录完成后，结果会直接显示在 "转录预览" 列。
6.  **导出内容**: 选中您想要导出的条目，点击 **"导出"**。应用将调用系统的分享功能，让您选择保存为 Word/Excel 文件或发送到其他应用。
7.  **删除记录**: 选中条目后点击 **"删除"**。您可以选择：
    *   **仅删除此记录**: 从应用列表中移除，但保留原始音频文件。
    *   **删除记录和文件**: 彻底删除记录和对应的音频文件。

## 🛠️ 技术栈

*   **框架**: .NET MAUI
*   **设计模式**: MVVM (使用 [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet))
*   **UI 工具**: [CommunityToolkit.Maui](https://github.com/CommunityToolkit/Maui) (用于 Popups 等)
*   **数据库**: [SQLite-net-pcl](https://github.com/praeclarum/sqlite-net)
*   **音频处理**: [NAudio](https://github.com/naudio/NAudio) (用于在 C# 中进行重采样和格式转换)
*   **文档导出**:
    *   [ClosedXML](https://github.com/ClosedXML/ClosedXML) (Excel .xlsx)
    *   [DocX](https://github.com/xceedsoftware/DocX) (Word .docx)
*   **API 签名**: 自定义实现的 `TC3-HMAC-SHA256` 签名逻辑。

## 🐞 调试技巧

在 Android 平台进行调试时，附加的调试器可能会干扰某些原生回调的执行。如果遇到程序卡住的情况，建议断开调试器，通过 `logcat` 查看实时日志：

```sh
adb logcat -s "com.tokisaki.CallREC_Scribe"
```

这会只显示来自本应用的日志输出，方便定位问题。
