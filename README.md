# AI Translator for Windows

使用 C#、WinUI 3 和 Windows App SDK 构建的 Windows 原生翻译工具。支持 AI 文本翻译与 UAPI 英汉查词，并继续使用原始 Rust/egui 项目的配置文件。

## 功能

- OpenAI Chat Completions 兼容 API
- 流式显示翻译结果
- 单个英文单词自动使用 UAPI 英汉词典，无需配置 AI API Key
- 词典显示中文释义、英美音标、英美发音、词形、常用搭配和双语例句
- 中文/非中文自动选择默认目标语言
- 13 种目标语言，也可手动选择
- 粘贴后自动查词或翻译，支持 `Ctrl+Enter`（不会插入换行）
- 取消请求、清空原文和复制译文
- API 连接测试与自定义系统提示词
- Mica 背景、原生 WinUI 3 控件和深浅色主题
- 无控制台窗口的 Windows GUI 可执行程序

## 查词与翻译

输入或粘贴单个英文单词后，点击“查词”或按 `Ctrl+Enter`，程序会查询 UAPI 并在原文下方显示词典卡片。发音按钮会播放英式或美式音频；“复制释义”会复制单词和中文释义。再次查询相同单词时，程序会在当前运行期间复用结果。

输入句子、中文或其他语言时，程序使用设置中的 OpenAI Chat Completions 兼容 API 进行翻译。若手动选择了非中文目标语言，即使输入单个英文单词，也会使用 AI 翻译。词典未收录或暂不可用时，可以重试或点击“改用 AI 翻译”。

UAPI 访客可使用有限免费额度；单词查询消耗 2 积分，播放一次发音消耗 1 积分。词典目前只支持英文单词查询，最长 64 个字符。查询会将该单词发送给 UAPI。接口说明见 [UAPI Word Lookup](https://uapis.cn/en/docs/api-reference/get-dictionary-lookup) 和 [Word Pronunciation](https://uapis.cn/en/docs/api-reference/get-dictionary-audio)。

## 配置

使用 AI 翻译前，进入“设置”填写：

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

## WinUI 3 架构

- `App.xaml.cs` 使用 `Microsoft.Extensions.DependencyInjection` 注册页面、配置存储、翻译和词典模块。
- `ViewModels/MainPageViewModel.cs` 使用 `CommunityToolkit.Mvvm` 提供可观察的页面状态；页面代码只保留窗口控件、焦点、剪贴板和媒体播放等 UI 专属工作。
- XAML 使用 `x:Bind` 绑定输入、输出、状态和操作标签，避免手动同步这些基础状态。
- `Services` 模块负责网络请求、配置持久化和 UAPI 缓存；流式响应会先消费同一数据包中的 `content`，再处理 `finish_reason`。

默认构建仍保持 unpackaged，便于本地调试和自包含发布。项目同时保留 single-project MSIX 清单和 `win10-x64` 发布配置；在安装了 Visual Studio 的 Windows App SDK/MSIX 工具链的机器上，可显式启用 `EnableMsixTooling=true` 生成 MSIX。当前工作区的轻量 .NET SDK 环境没有完整的 x64 MSIX 原生工具，因此不会把 MSIX 构建误报为成功。

## 发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/win-x64
```

发布目录必须整体分发，不能只复制 `AiTranslator.exe`。推送 `v*` 标签时，GitHub Actions 会生成 x64 ZIP 和免管理员权限的 `AiTranslator-Setup.exe` 安装包并附加到 Release。安装器使用 Windows IExpress 生成，适合当前 unpackaged 自包含版本；正式面向商店或企业分发时应使用签名的 MSIX。

本地生成安装包：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts/installer-payload
.\installer\build-installer.ps1 `
  -PayloadDirectory (Resolve-Path artifacts/installer-payload) `
  -OutputPath (Join-Path (Resolve-Path artifacts) 'AiTranslator-Setup.exe')
```

## 项目结构

- `MainPage.xaml`：主界面和设置界面
- `MainPage.xaml.cs`：UI 状态、快捷键、剪贴板与任务取消
- `Services/TranslationService.cs`：HTTP 请求和 SSE 流式响应解析
- `Services/DictionaryService.cs`：UAPI 查询、结果缓存和发音地址
- `Models/DictionaryEntry.cs`：词典响应模型
- `Services/SettingsStore.cs`：兼容原项目的 JSON 配置读写
- `Models/AppSettings.cs`：设置模型、默认提示词和语言列表
