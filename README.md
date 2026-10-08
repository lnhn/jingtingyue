# 静听阅

一款安静、漂亮、适合长期使用的中文 EPUB 阅读器，听书靠本机神经语音，不依赖云端。

## 截图

![书架](docs/shelf.png)
![阅读](docs/reader.png)

## 特性

- **书架**：导入 EPUB、提取封面、分类管理、阅读进度与「继续阅读」。
- **阅读**：单页/双页自适应分页、键盘/触控板翻页、目录跳转、夜间/羊皮纸/纸白三主题。
- **字体**：内置开源的思源宋体与霞鹜文楷，也支持楷体、宋体等系统字体；每本书独立记忆字体、字号、主题、音色、语速与阅读位置。
- **听书**：本地 Kokoro 神经 TTS，5 个中文音色，逐句高亮、自动翻页；章节结束时询问是否继续，并跳过网址、邮箱、数字引用和参考文献列表。定时停止控件尚未接通。
- **笔记**：选中文字划线备注，悬停查看、回看原文，可导出竖版分享卡片。
- **关于**：在书架左下角查看版本和本地数据说明。
- **隐私**：书、笔记和进度保存在本机。AI 解读尚未实现。

## 2026-10-08 更新

- 阅读界面、主题和书籍封面进一步调整；EPUB 封面与正文图片现在可显示。
- 朗读面板改为手动开始，音色单选；单句出现小故障时跳过继续，参考文献不进入朗读队列。
- 笔记可在原文悬停查看；竖版分享卡片包含摘录、想法、完整书籍信息与应用标识。点击面板外可以关闭小窗口。
- 新增“关于”页面，随应用附带思源宋体与霞鹜文楷。

关键功能提交：[`bc006b0`](https://github.com/lnhn/jingtingyue/commit/bc006b033c34752d0100fef8005eb2d7bfe8cd6e)（`feat: refine reader UI, notes, TTS and sharing`）；对应发布版：[v1.1.0](https://github.com/lnhn/jingtingyue/releases/tag/v1.1.0)。完整开发提示词与提交详情见 [SPEC.md](SPEC.md)。

## 技术栈

- C# / .NET 8 / WinUI 3，自包含免安装（不依赖目标机的 .NET）。
- 阅读引擎：内嵌 WebView2，CSS 多栏分页。
- 本地 TTS：Kokoro-ONNX + onnxruntime（纯 CPU），FastAPI 微服务随 App 拉起。

## 开发

```powershell
# 编译
dotnet build src\JingTingYue\JingTingYue.csproj -c Debug

# 运行（开发期）
.\src\JingTingYue\bin\Debug\net8.0-windows10.0.19041.0\win-x64\JingTingYue.exe
```

## 打包

```powershell
dotnet publish src\JingTingYue\JingTingYue.csproj -c Release -r win-x64 --self-contained true -o publish
```

把 `tts-server/`（含 `python/` 运行时、`model_uint8.onnx`、`voices.npz`）放到 `publish\tts-server\`，即得到绿色免安装目录。

## 数据位置

`%LOCALAPPDATA%\JingTingYue\`：books.json、notes.json、epubs\。

## 已知边界

- 仅支持 EPUB 与纯文本，不支持 PDF。
- 听书为 CPU 推理，长句有秒级合成延迟。
- AI 解读需自备 DeepSeek API Key，联网调用，可能产生费用。
