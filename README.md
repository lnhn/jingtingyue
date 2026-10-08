# 静听阅

一款面向 Windows 的中文 EPUB 阅读器，提供书库、沉浸式阅读、笔记、分享卡片和本地语音朗读。

## 下载与使用

1. 从 [最新发布页](https://github.com/lnhn/jingtingyue/releases/latest) 下载 **`JingTingYue-v1.2.0-win-x64.exe`**。
2. 把 EXE 放在一个**可写入的文件夹**中，双击打开。无需解压、安装 .NET、Python 或 WebView2，也无需管理员权限。
3. 首次打开会显示“正在准备阅读与朗读组件”，请等候完成。之后打开会直接进入书库。

应用支持 Windows 10 1809 及更新版本、Windows 11 的 x64 系统。离线语音合成使用本机 CPU；首次加载和合成长句可能稍慢。

### 数据放在哪里？

书籍、笔记、阅读进度、主题设置、浏览器数据、朗读日志和导出的分享卡片都保存在 **EXE 所在文件夹**，分别位于 `books.json`、`notes.json`、`theme.txt`、`epubs/`、`WebView2/`、`tts.log` 和 `分享卡片/`。移动到另一台电脑时，把 EXE 和这些数据文件一起复制过去即可。建议先将 EXE 放进自己创建的文件夹，再打开；不要放在需要管理员权限才能写入的位置。

首次启动自动释放的应用组件缓存在 `%LOCALAPPDATA%\JingTingYue\Runtime\`。这里没有书籍或笔记，可以在关闭应用后清理；下次打开会重新准备组件。单 EXE 只是**下载和启动入口**，运行时仍需自动释放依赖文件。

从 v1.1.1 ZIP 版升级时，先关闭旧版，再将 `%LOCALAPPDATA%\JingTingYue\` 下的 `books.json`、`notes.json`、`theme.txt` 和 `epubs/` **复制**到新 EXE 所在文件夹。旧数据不会被自动删除。

## 界面预览

### 书库

![书库界面](docs/shelf.png)

### 阅读

![阅读界面](docs/reader.png)

## 功能

- EPUB 书库：提取封面、分类、保存阅读位置，按书籍记忆阅读设置。
- 阅读排版：根据窗口宽度切换单页与双页，提供目录、翻页、字体与三种主题。
- 本地朗读：五种中文音色、语速选择、逐句高亮和自动翻页；跳过正文中的引用数字和参考文献列表。
- 笔记与分享：选中原文写笔记，悬停查看笔记，生成包含原文、笔记及书籍信息的竖版 PNG 卡片。
- 随包提供思源宋体和霞鹜文楷；书籍与笔记始终在本机。

## 从源码构建单 EXE

构建者需要 .NET 8 SDK、Python 3，以及嵌入式 TTS Python、语音模型和 [微软 WebView2 Fixed Version x64](https://developer.microsoft.com/en-us/microsoft-edge/webview2)。打包脚本会从 `tts-server/`、`webview2-fixed/` 或已有的 `publish/` 目录读取这些大型运行文件。

```powershell
python scripts\build_portable.py
```

产物是 `dist/JingTingYue-v1.2.0-win-x64.exe`。脚本先发布 WinUI 应用，打包其运行组件，再将它们嵌入单文件启动器。开发提示见 [SPEC.md](SPEC.md)。

## 已知限制

- 目前只支持 EPUB，不支持 PDF。
- 本地语音合成使用 CPU，速度取决于设备性能。
- 定时停止朗读控件尚未接通。
