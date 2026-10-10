# 静听阅

一款面向 Windows 的中文 EPUB 阅读器，提供书库、沉浸式阅读、笔记、分享卡片和本地语音朗读。

## 下载与使用

1. 从 [最新发布页](https://github.com/lnhn/jingtingyue/releases/latest) 下载 **`JingTingYue-v1.3.0-win-x64.zip`**。
2. 完整解压到一个**可写入的文件夹**中，双击其中的 **`JingTingYue.exe`**。无需安装 .NET、Python 或 WebView2，也无需管理员权限。
3. 首次朗读会加载本地语音模型并提前缓冲。请保留整个解压目录中的组件，不要在压缩包内直接运行。

应用支持 Windows 10 1809 及更新版本、Windows 11 的 x64 系统。离线语音合成使用本机 CPU；首次加载和合成长句可能稍慢。

### 数据放在哪里？

书籍、笔记、阅读进度、朗读片段、主题设置、浏览器数据、朗读日志和导出的分享卡片都保存在 **解压目录**，分别位于 `books.json`、`notes.json`、`snippets.json`、`theme.txt`、`epubs/`、`WebView2/`、`tts.log` 和 `分享卡片/`。移动到另一台电脑时，先关闭应用，再复制整个目录。请保留 `portable.flag`，它让应用将数据保存在 EXE 旁边；目录需可写入。

ZIP 内已包含 .NET、Windows App SDK、WebView2、嵌入式 Python、语音模型和音色，无需首次启动时另外释放运行组件。

升级时，先关闭旧版，将旧目录中的 `books.json`、`notes.json`、`snippets.json`、`theme.txt` 和 `epubs/` **复制**到新版解压目录。从 v1.1.1 升级时，这些数据位于 `%LOCALAPPDATA%\JingTingYue\`；v1.2.0 单 EXE 版的数据位于旧 EXE 所在目录。旧数据不会被自动删除。

## 界面预览

### 书库

![书库界面](docs/shelf.png)

### 阅读

![阅读界面](docs/reader.png)

## 功能

- EPUB 书库：提取封面、分类、保存阅读位置，按书籍记忆阅读设置。
- 阅读排版：根据窗口宽度切换单页与双页，提供目录、翻页、字体与三种主题。
- 本地朗读：五种中文音色、语速选择、逐句高亮和自动翻页；跳过正文中的引用数字和参考文献列表。
- 提前缓冲并持续生成后续语音；支持中文中的英文发音，以及逗号、分号和句末停顿。
- 朗读片段：粘贴文字独立保存、编辑和朗读。
- 笔记与分享：选中原文写笔记，悬停查看笔记，生成包含原文、笔记及书籍信息的竖版 PNG 卡片。
- 随包提供思源宋体和霞鹜文楷；书籍与笔记始终在本机。

## 从源码构建绿色 ZIP

构建者需要 .NET 8 SDK、Python 3，以及嵌入式 TTS Python、语音模型和 [微软 WebView2 Fixed Version x64](https://developer.microsoft.com/en-us/microsoft-edge/webview2)。打包脚本会从 `tts-server/`、`webview2-fixed/` 或已有的 `publish/` 目录读取这些大型运行文件。

```powershell
python scripts\build_green.py
```

产物是 `dist/JingTingYue-v1.3.0-win-x64.zip` 和 SHA-256 校验文件。脚本发布自包含 WinUI 应用，并打包完整的离线运行组件。WebView2 位于其他目录时可传入 `--webview2 "完整目录路径"`。开发提示见 [SPEC.md](SPEC.md)。

单 EXE 打包脚本为 `python scripts\build_portable.py`。

## 已知限制

- 目前只支持 EPUB，不支持 PDF。
- 本地语音合成使用 CPU，速度取决于设备性能。
- 定时停止朗读控件尚未接通。
