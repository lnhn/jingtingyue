# 静听阅 · 完整开发提示词

把本文档整体喂给任何一个有软件工程能力的 AI（Claude / GPT / 豆包 / Cursor 等），它就能从零复现这个 Windows 中文 EPUB 阅读器。

---

## 一、产品定位

做一个**安静、漂亮、适合长期使用的中文 EPUB 桌面阅读器**，体验对标 Apple Books。

- 中文阅读为主。
- 书籍、笔记、阅读记录全部保存在本机，不依赖云端。
- 普通阅读和听书**不联网**。
- AI 解读功能通过用户自己的 DeepSeek API Key 调用（用户在设置里填）。
- 只支持 **EPUB** 和**粘贴文字**两种来源；不做 PDF、不做账号登录、不做云同步。

## 二、技术栈（已验证可行）

- **语言/框架**：C# / .NET 8 / **WinUI 3**（WindowsAppSDK 1.6）。
- **打包方式**：`WindowsPackageType=None`，自包含（`--self-contained`），绿色免安装，解压即用，目标机不需要装 .NET。
- **阅读引擎**：WinUI 里 new 一个 **WebView2**，用 CSS 多栏（`column-width`）做分页。
- **本地 TTS**：**Kokoro-82M** 神经语音，ONNX 格式，通过 onnxruntime CPU 推理；Python 3.13 跑一个 FastAPI 微服务（端口 127.0.0.1:8123），C# 侧用 HttpClient 调用。
- **UI 风格**：暖白书页 `#FAF7F2`、深森林绿主色 `#2F5D4B`、陶土点缀 `#B85C38`；中文字体优先楷体/宋体/仿宋。

> 不要用 WPF、不要用 MAUI、不要用 Electron。WinUI 3 自包含是这条路上体验最接近原生的方案。

## 三、目录结构

```
JingTingYue/
├── JingTingYue.sln
├── src/JingTingYue/
│   ├── JingTingYue.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── Models/StoredBook.cs, StoredNote.cs
│   ├── Services/BookStore.cs, TtsServer.cs, EpubService.cs
│   ├── Views/BookshelfPage.xaml(.cs), ReaderPage.xaml(.cs)
│   └── Assets/reader/reader.html
├── tts-server/
│   ├── server.py
│   ├── python/            # 裁剪过的内嵌 Python 运行时（打包时拷进来）
│   ├── model_uint8.onnx   # 169MB 量化模型
│   └── voices.npz
└── publish/               # dotnet publish 输出，含 tts-server/
```

## 四、核心功能规格

### 1. 书架
- 导入 EPUB，复制到 `%LOCALAPPDATA%\JingTingYue\epubs\`，元信息存 `books.json`。
- 粘贴大段文字，自定义标题，存成可阅读可朗读的条目。
- 分类（新建/移动/删除），左侧栏显示「全部内容 / 未分类 / 各分类」及数量。
- 卡片显示封面（无封面按书名首字+主题色生成）、书名、作者、进度条。
- 顶部大卡片「继续阅读」，显示上次读到哪本、进度百分比，点击直接回到上次章节+页位置。
- 长按/右键卡片弹出删除、移动到分类。

### 2. 阅读
- 正确解析 EPUB 章节顺序与 nav/toc，正文提取（去脚本/样式，保留段落结构）。
- WebView2 里用 CSS `columns` 分页；窗口大小或字号变化后按文字锚点重新分页，**不要跳回章节开头**。
- 键盘 ←/→ 翻页，触控板横向手势翻页。
- 目录抽屉跳转；全书搜索，点结果跳转到原文并高亮。
- 每本书**独立记忆**：字体、字号、主题（纸白/暖棕/夜间）、音色、语速、章节、页位置。
- 用 `!important` CSS 强制覆盖 EPUB 自带字体/颜色/边距，统一成我们的排版。

### 3. 本地听书（Kokoro）
- 从当前页正文开始朗读。
- 播放控制：开始 / 暂停 / 继续 / 停止；暂停后从原句继续。
- 语速调节（0.6–1.6x）；音色下拉只列出**真实能出声**的 5 个：`zf_xiaoxiao`（女 温柔）、`zf_xiaobei`（女 活泼）、`zm_yunjian`（男 沉稳）、`zm_yunxi`（男 青年）、`zm_yunyang`（男 低沉）。
- 逐句高亮当前句子；朗读到下一页边界时自动翻页。
- **手动翻页不打断朗读**，也不跳回朗读位置；只在朗读自然翻到下一页时才跟随。
- 提供「回到朗读位置」按钮。
- 跨章节连续朗读（默认关）；定时停止（15/30/60 分钟），跨章节不重置计时。
- 朗读时**跳过**：URL、邮箱、`[1]`、`[1, 2]`、`[3–5]` 这类数字引用；遇到独立标题「参考文献 / References / Bibliography」后整段跳过。
- 过滤只影响朗读，不改阅读正文。
- 后台用队列+锁，避免任务互相抢占、音频队列无限增长。

### 4. 笔记
- 选中文字 → 右键/弹按钮「添加笔记」→ 写备注。
- 原文加下划线/色块；鼠标悬停显示备注预览，点击进详情。
- 笔记列表可搜索、编辑、删除、跳回原文。
- 分享卡片：竖版 PNG，至少 1920×2560，长内容自动增高，含原文、个人想法、书名、作者、「静听阅」署名。

### 5. AI 解读（DeepSeek）
- 独立设置窗口填 API Key，用 **Windows 凭据管理器**存，不写明文。
- 选中文字点「帮我读懂」，把选中段落发给 DeepSeek，返回解释，可继续追问。
- 只在用户点发送时提交；**不自动上传整本书**。
- 明确提示需要联网、可能产生费用。
- 回答支持 Markdown（粗体、标题、列表、引用、代码块）。
- 按书保存对话历史，重开可继续。
- 接 DeepSeek API 前**查官方文档**确认模型名和参数；短篇解释用非思考模式（`deepseek-chat`），不要让思考 token 吃掉额度。
- 正确处理：无效 Key、余额不足、超时、取消、空回答、截断；不要把思考内容当答案。

### 6. 数据保护
- 导出/恢复书架、分类、笔记、进度；恢复是合并，恢复前自动备份。
- 删除可恢复；数据损坏要提示，不静默覆盖。
- 备份里**不含 API Key**。

## 五、视觉规范

- 主色：深森林绿 `#2F5D4B`；点缀陶土 `#B85C38`；书页 `#FAF7F2`；墨色 `#2A2A28`；淡墨 `#8A8A85`。
- 大量留白；卡片圆角 12–16；按钮圆角胶囊 999；悬停有反馈。
- 书架左栏窄（~200px），右侧大留白。
- 阅读页正文居中，最大宽度 ~720px，行高 1.9。
- 字体下拉：楷体、宋体、仿宋、微软雅黑、黑体、等线、魏碑、隶书（`LiSu,STLiti` 回退）。
- 自己做一个 app 图标（深绿圆角方块 + 「静」字），不要用默认 exe 图标。
- **不要用 Segoe MDL2 Assets 图标字体**（在本项目里全是豆腐块）；用 Unicode 字符或 Path 矢量。

## 六、已踩过的坑（必须知道）

1. **PowerShell 改中文 XAML/cs 文件会乱码**——一律用 Edit/Write 工具改，不要用 `Set-Content -replace`。
2. **WinUI 3 XamlCompiler.exe 偶发无输出崩溃**：通常是同文件里 C# 有隐藏错误（比如 `CornerRadius = 16` 没写 `new CornerRadius(16)`）。先看 C# 错误，再清 `obj/Debug` 重编。
3. **在 BookshelfPage.xaml 里直接新增一个 Button 偶发导致 XamlCompiler 崩溃**——改用 TextBlock + 代码里 `PointerPressed` 挂事件，或塞进已有 StackPanel。
4. **reader.html 改完必须手动 Copy 到输出 `Assets/reader/`**，URL 加 `?v=N` 破 WebView2 缓存。
5. **TTS 冷启动 15–20 秒**：阅读页一打开就拉音色列表会拿到空列表。`FillVoices()` 要重试 15 次、每次间隔 2 秒。
6. **打包后 exe 同级要有 `tts-server/`**：TtsServer.cs 先找 exe 同级 `tts-server\python\python.exe`，找不到再回退开发期相对路径。
7. **Git 代理**：Windows Credential Manager 不支持 socks5，git 代理要配 `http://127.0.0.1:7890` 而不是 socks5。
8. **GitHub Release 中文乱码**：PowerShell `ConvertTo-Json` + here-string 发中文会编码错；把 JSON 写成 UTF-8 文件再 `ReadAllBytes` 发出去。

## 七、编译/打包命令

```powershell
# 开发
dotnet build src\JingTingYue\JingTingYue.csproj -c Debug
.\src\JingTingYue\bin\Debug\net8.0-windows10.0.19041.0\win-x64\JingTingYue.exe

# 打包绿色版
dotnet publish src\JingTingYue\JingTingYue.csproj -c Release -r win-x64 --self-contained true -o publish
# 把 tts-server/（含 python/、model_uint8.onnx、voices.npz）拷进 publish\tts-server\
Compress-Archive publish\* 静听阅-portable.zip
```

发布产物约 550MB，zip 后 ~295MB（模型 uint8 量化后 169MB）。

## 八、数据位置

`%LOCALAPPDATA%\JingTingYue\`：
- `books.json`：书元信息（含每本书的字体/字号/主题/音色/语速/位置）
- `notes.json`：笔记
- `epubs/`：原始 EPUB

## 九、开发节奏要求

按阶段交付，每阶段可运行可验证：
1. 漂亮的阅读页原型 + 示例章节
2. EPUB 导入 + 书架 + 阅读进度持久化
3. 本地 Kokoro TTS 听书 + 播放控制
4. 选字笔记 + 原文划线 + 分享卡片
5. DeepSeek AI 解读 + 历史
6. 备份恢复 + 打包 + 整体打磨

一次只做一个小而完整的变更；编译通过 ≠ 正常，要真打开看界面。
