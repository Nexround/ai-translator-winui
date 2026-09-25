# AI Translator for Windows

使用 C#、WinUI 3 和 Windows App SDK 重建的 Windows 原生翻译工具。功能与原始 Rust/egui 版本保持一致，并继续使用原配置文件。

## 功能

- OpenAI Chat Completions 兼容 API
- 流式显示翻译结果
- 中文/非中文自动选择默认目标语言
- 13 种目标语言，也可手动选择
- 粘贴后自动翻译，支持 `Ctrl+Enter`
- 取消请求、清空原文和复制译文
- API 连接测试与自定义系统提示词
- Mica 背景、原生 WinUI 3 控件和深浅色主题
- 无控制台窗口的 Windows GUI 可执行程序

## 配置

首次启动后进入“设置”，填写：

- API Key
- Base URL，例如 `https://api.openai.com/v1`
- 模型，例如 `gpt-4o-mini`

配置保存在 `%USERPROFILE%\.translator\config.json`。该格式与原始 `ai-translator-rs` 项目兼容。

## 开发

要求：

- Windows 10/11
- .NET 10 SDK

```powershell
dotnet restore
dotnet build -c Debug -p:Platform=x64
dotnet run -c Debug -p:Platform=x64
```

项目采用 unpackaged + Windows App SDK self-contained 配置，因此普通 `dotnet build` 不需要安装或签名 MSIX 包。

## 发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/win-x64
```

发布目录必须整体分发，不能只复制 `AiTranslator.exe`。推送 `v*` 标签时，GitHub Actions 会自动生成 x64 ZIP 并附加到 Release。

## 项目结构

- `MainPage.xaml`：主界面和设置界面
- `MainPage.xaml.cs`：UI 状态、快捷键、剪贴板与任务取消
- `Services/TranslationService.cs`：HTTP 请求和 SSE 流式响应解析
- `Services/SettingsStore.cs`：兼容原项目的 JSON 配置读写
- `Models/AppSettings.cs`：设置模型、默认提示词和语言列表
