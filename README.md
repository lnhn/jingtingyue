# 静听阅

一款面向 Windows 的中文 EPUB 阅读器。提供书库、沉浸式阅读、笔记、分享卡片和本地语音朗读。

## 下载与使用

1. 从 [Releases](https://github.com/lnhn/jingtingyue/releases) 下载最新的 **`JingTingYue-*-win-x64.zip`**。
2. 将 ZIP **完整解压**到一个文件夹，双击其中的 `JingTingYue.exe`。
3. 在书库中导入 EPUB，开始阅读。

发布 ZIP 内包含 .NET、Windows App SDK、WebView2 Fixed Version、Python 和朗读模型。**无需另行下载或安装运行环境，也无需管理员权限。**请保留解压后的目录结构；单独复制 EXE 或直接在压缩包内运行会缺少依赖文件。

支持 Windows 10 1809 及更新版本、Windows 11 的 x64 系统。语音合成在本机 CPU 上运行，首次启动朗读服务和合成长句可能需要等待。应用不会上传书籍或笔记。

## 界面预览

### 书库

![书库界面：分类、继续阅读和书籍封面](docs/shelf.png)

### 阅读

![阅读界面：自适应分页和中文排版](docs/reader.png)

## 功能

- EPUB 书库：提取封面、分类、保存阅读位置，按书籍记忆阅读设置。
- 阅读排版：根据窗口宽度切换单页与双页，提供目录、翻页、字体与三种主题。
- 本地朗读：五种中文音色、语速选择、逐句高亮和自动翻页；跳过正文中的引用数字和参考文献列表。
- 笔记与分享：选中原文写笔记，悬停查看笔记，生成包含原文、笔记及书籍信息的竖版 PNG 卡片。
- 随包提供思源宋体、霞鹜文楷；书籍和笔记保存在 `%LOCALAPPDATA%\JingTingYue\`。

## 从源码构建

需要 .NET 8 SDK 和 Windows 开发环境。最终用户只需要上面的发布 ZIP。

```powershell
dotnet publish src\JingTingYue\JingTingYue.csproj -c Release -r win-x64 --self-contained true -o publish
```

发布前还需将 `tts-server/server.py`、嵌入式 Python、`model_uint8.onnx`、`voices.npz` 放进 `publish/tts-server/`，并将 [微软 WebView2 Fixed Version x64](https://developer.microsoft.com/en-us/microsoft-edge/webview2) 解压至 `publish/WebView2Fixed/`，使 `msedgewebview2.exe` 位于该目录根部。完整打包时不得包含 `JingTingYue.exe.WebView2` 用户缓存目录。

开发提示见 [SPEC.md](SPEC.md)。

## 已知限制

- 目前只支持 EPUB，不支持 PDF。
- 本地语音合成使用 CPU，速度取决于设备性能。
- 定时停止朗读控件尚未接通。
