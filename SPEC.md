# 静听阅 · 完整开发提示词

本文档是给后续开发者或编码助手使用的项目提示词。以仓库源码为准；下文明确区分**已实现行为**与**后续规划**，不要把规划功能写成已完成。

当前基线：2026-10-08 的提交 `bc006b0`，标签 `v1.1.0`。继续开发时先检查 `git status`、现有代码和 Release，再改动、编译并实际运行。

---

## 一、产品定位

做一个**安静、漂亮、适合长期使用的中文 EPUB 桌面阅读器**，体验对标 Apple Books。

- 中文阅读为主。
- 书籍、笔记、阅读记录全部保存在本机，不依赖云端。
- 普通阅读和听书**不联网**。
- AI 解读属于后续规划，若实现只能使用用户自行提供的 API Key 并明确告知联网。
- 当前导入来源为 **EPUB**；粘贴文字导入属于后续规划。不做 PDF、账号登录和云同步。

## 二、技术栈（已验证可行）

- **语言/框架**：C# / .NET 8 / **WinUI 3**（WindowsAppSDK 1.6）。
- **打包方式**：`WindowsPackageType=None`，自包含（`--self-contained`），绿色免安装，解压即用，目标机不需要装 .NET。
- **阅读引擎**：WinUI 里 new 一个 **WebView2**，用 CSS 多栏（`column-width`）做分页。
- **本地 TTS**：**Kokoro-82M** 神经语音，ONNX 格式，通过 onnxruntime CPU 推理；Python 3.13 跑一个 FastAPI 微服务（端口 127.0.0.1:8123），C# 侧用 HttpClient 调用。
- **UI 风格**：纸白、暖棕、夜间三主题；阅读界面使用暖白纸面与陶土色强调，封面及分享卡片使用克制的深绿。默认正文为随包提供的思源宋体，也可选择霞鹜文楷和系统字体。

> 不要用 WPF、不要用 MAUI、不要用 Electron。WinUI 3 自包含是这条路上体验最接近原生的方案。

## 三、目录结构

```
JingTingYue/
├── JingTingYue.sln
├── src/JingTingYue/
│   ├── JingTingYue.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── Models/StoredBook.cs, BookNote.cs
│   ├── Services/BookStore.cs, NoteStore.cs, EpubParser.cs,
│   │   TtsServer.cs, TtsService.cs, ShareCard.cs, AppTheme.cs
│   ├── Views/BookshelfPage.xaml(.cs), ReaderPage.xaml(.cs), AboutPage.xaml(.cs)
│   └── Assets/reader/reader.html, Assets/Fonts/
├── tts-server/
│   ├── server.py
│   ├── python/            # 裁剪过的内嵌 Python 运行时（打包时拷进来）
│   ├── model_uint8.onnx   # 169MB 量化模型
│   └── voices.npz
└── publish/               # dotnet publish 输出，含 tts-server/
```

## 四、当前已实现的核心行为

### 1. 书架
- 导入 EPUB，复制到 `%LOCALAPPDATA%\JingTingYue\epubs\`，元信息存 `books.json`。
- 分类（新建/移动/删除），左侧栏显示「全部内容 / 未分类 / 各分类」及数量。
- 尝试提取 EPUB 内的封面；没有封面时以与主题协调的分类配色显示书籍。
- 顶部大卡片「继续阅读」，显示上次读到哪本、进度百分比，点击直接回到上次章节+页位置。
- 长按/右键卡片弹出删除、移动到分类。

### 2. 阅读
- 按 EPUB 的 spine 顺序解析章节；提取封面并把正文图片嵌入阅读 HTML，去掉会破坏统一排版的内联样式。
- WebView2 用 CSS 多栏分页；宽度不足时单页，足够宽时双页。窗口大小和字号变化需要重新分页并尽可能保留阅读位置，不能裁断正文。
- 键盘 ←/→ 翻页，触控板横向手势翻页。
- 目录侧栏跳转；底部有上一章、下一章和翻页按钮。
- 每本书**独立记忆**：字体、字号、主题（纸白/暖棕/夜间）、音色、语速、章节、页位置。
- 用 CSS 覆盖 EPUB 自带字体、颜色和边距。内置字体文件随 Release 放在 `Assets/Fonts/`，许可证保留在字体目录。

### 3. 本地听书（Kokoro）
- 从当前页正文开始朗读。
- 播放控制：开始 / 暂停 / 继续 / 停止；暂停后从原句继续。
- 音色为单选；提供 `zf_xiaoxiao`、`zf_xiaobei`、`zm_yunjian`、`zm_yunxi`、`zm_yunyang`。朗读面板打开后只显示设置，用户按「开始朗读」才发声。面板外点击可关闭面板，但关闭书籍必须停止朗读。
- 语速界面提供 0.75、1、1.25、1.5、2 倍选项；实际服务端语速仍需结合 `TtsService.SetRate` 的范围检查。
- 逐句高亮当前句子；朗读到下一页边界时自动翻页。
- 手动翻页不应无故中断当前句；朗读句子进入下一页时自动跟随。
- 一章结束后目前会询问是否继续下一章。界面已有定时停止选项，但计时行为尚未接通，后续实现时必须验证。
- 朗读时**跳过**：URL、邮箱、`[1]`、`[1, 2]`、`[3–5]` 这类数字引用；遇到独立标题「参考文献 / References / Bibliography」后整段跳过。
- 过滤只影响朗读，不改阅读正文。
- 单句合成或播放失败时跳过并继续下一句；请求和播放都有限时处理，停止后旧的预取结果不能混入新一轮朗读。

### 4. 笔记
- 选中文字 → 右键/弹按钮「添加笔记」→ 写备注。
- 原文加下划线/色块；鼠标悬停显示备注预览，点击进详情。
- 笔记列表可搜索、删除、跳回原文；写笔记使用主题适配的应用内面板。
- 分享卡片：3:4 竖版，预览宽 320 像素，导出按 3 倍渲染；原文最多 5 行、笔记最多 3 行并显示省略号，书名和作者完整换行。长书籍信息使卡片增高，预览可滚动；卡片包含应用 Logo 与署名。
- 朗读、手记、阅读设置、写笔记和分享面板都支持点击面板外关闭；不能因为收起朗读面板而停播。

## 五、后续规划（尚未实现）

### 1. AI 解读（DeepSeek）
- 独立设置窗口填 API Key，用 **Windows 凭据管理器**存，不写明文。
- 选中文字点「帮我读懂」，把选中段落发给 DeepSeek，返回解释，可继续追问。
- 只在用户点发送时提交；**不自动上传整本书**。
- 明确提示需要联网、可能产生费用。
- 回答支持 Markdown（粗体、标题、列表、引用、代码块）。
- 按书保存对话历史，重开可继续。
- 接 DeepSeek API 前**查官方文档**确认模型名和参数；短篇解释用非思考模式（`deepseek-chat`），不要让思考 token 吃掉额度。
- 正确处理：无效 Key、余额不足、超时、取消、空回答、截断；不要把思考内容当答案。

### 2. 数据保护
- 导出/恢复书架、分类、笔记、进度；恢复是合并，恢复前自动备份。
- 删除可恢复；数据损坏要提示，不静默覆盖。
- 备份里**不含 API Key**。

## 六、视觉规范

- 实际主题资源以 `App.xaml` 和 `AppTheme.cs` 为准：纸白背景 `#F8F7F4`、正文墨色 `#252320`、强调色 `#D85F45`；夜间与暖棕色值由主题服务切换。
- 保持留白、圆角、主题适配的悬停和按下状态；系统标题栏按钮也要与主题适配。
- 书架左栏约 244px；阅读区域随窗口宽度切换单页或双页。
- 字体下拉优先显示思源宋体、霞鹜文楷，再列出楷体、宋体、仿宋、微软雅黑、黑体、等线、魏碑、隶书。
- 自己做一个 app 图标（深绿圆角方块 + 「静」字），不要用默认 exe 图标。
- 检查浅色和夜间主题的文字对比度、按钮悬停状态与阅读卡片的截断行为。

## 七、实现与发布注意事项

1. **PowerShell 改中文 XAML/cs 文件会乱码**——一律用 Edit/Write 工具改，不要用 `Set-Content -replace`。
2. **WinUI 3 XamlCompiler.exe 偶发无输出崩溃**：通常是同文件里 C# 有隐藏错误（比如 `CornerRadius = 16` 没写 `new CornerRadius(16)`）。先看 C# 错误，再清 `obj/Debug` 重编。
3. 改 `reader.html` 后确认构建输出的 `Assets/reader/reader.html` 已更新；项目文件已设置 `Assets/**` 自动复制到输出目录。必要时调整 WebView2 URL 的版本查询参数以排除缓存。
4. **TTS 冷启动 15–20 秒**：阅读页一打开就拉音色列表会拿到空列表。`FillVoices()` 要重试 15 次、每次间隔 2 秒。
5. **打包后 exe 同级要有 `tts-server/`**：TtsServer.cs 先找 exe 同级 `tts-server\python\python.exe`，找不到再回退开发期相对路径。
6. **Git 代理**：Windows Credential Manager 不支持 socks5，git 代理要配 `http://127.0.0.1:7890` 而不是 socks5。
7. GitHub Release API 发送中文内容时要明确用 UTF-8 字节提交 JSON，避免编码问题。
8. Release ZIP 必须包含 `tts-server/python/python.exe`、`server.py`、`model_uint8.onnx`、`voices.npz` 及两款字体；压缩前结束仍占用 `voices.npz` 的遗留朗读服务进程。

## 八、编译/打包命令

```powershell
# 开发
dotnet build src\JingTingYue\JingTingYue.csproj -c Debug
.\src\JingTingYue\bin\Debug\net8.0-windows10.0.19041.0\win-x64\JingTingYue.exe

# 打包绿色版
dotnet publish src\JingTingYue\JingTingYue.csproj -c Release -r win-x64 --self-contained true -o publish
# 把 tts-server/（含 python/、model_uint8.onnx、voices.npz）放入 publish\tts-server\
Compress-Archive -Path publish\* -DestinationPath JingTingYue-win-x64.zip -CompressionLevel Optimal
# 发布前验证 ZIP 完整性与 SHA-256
python -m zipfile -t JingTingYue-win-x64.zip
Get-FileHash JingTingYue-win-x64.zip -Algorithm SHA256
```

`v1.1.0` 的发布目录约 667 MB，ZIP 为 349,317,813 字节；两款完整中文字体约增加 48 MB。

## 九、数据位置

`%LOCALAPPDATA%\JingTingYue\`：
- `books.json`：书元信息（含每本书的字体/字号/主题/音色/语速/位置）
- `notes.json`：笔记
- `epubs/`：原始 EPUB

## 十、2026-10-08 更新与提交记录

今天的源码提交：[`bc006b0`](https://github.com/lnhn/jingtingyue/commit/bc006b033c34752d0100fef8005eb2d7bfe8cd6e)，2026-10-08 17:14:26（北京时间），`feat: refine reader UI, notes, TTS and sharing`。该提交修改 19 个文件，新增 1,146 行、删除 221 行。

- 阅读与书架：改进主题配色、标题栏状态、封面分类色；解析 EPUB 封面和正文图片；优化分页与章节控制。
- 听书：音色单选、手动开始、自动跟页；单句合成或播放出错时尽量跳过继续，过滤正文引用序号和章节末参考文献。
- 笔记与分享：应用内写笔记面板、划线悬停预览、3:4 竖版品牌卡片；摘录与笔记按行数省略，书籍信息完整换行；面板外点击可关闭。
- 其他：独立“关于”页，内置思源宋体和霞鹜文楷及各自的 OFL 许可文件。

该提交已进入 `main`，打了 `v1.1.0` 标签并发布 [GitHub Release](https://github.com/lnhn/jingtingyue/releases/tag/v1.1.0)。Windows 免安装包：`JingTingYue-v1.1.0-win-x64.zip`，SHA-256 为 `90460AD57F1C08B22EA6625A9CCC5ABF6D892E4E95E50CB7F0BBD7EF1F297193`。发布前已运行 Release 构建、检查必需文件并通过 ZIP 完整性检查。

## 十一、开发节奏要求

按阶段交付，每阶段可运行可验证：
1. 漂亮的阅读页原型 + 示例章节
2. EPUB 导入 + 书架 + 阅读进度持久化
3. 本地 Kokoro TTS 听书 + 播放控制
4. 选字笔记 + 原文划线 + 分享卡片
5. 完成已显示但未接通的控制（例如定时停止），并用实际书籍验证朗读和分享卡片
6. 如需扩展，再做粘贴文字导入、DeepSeek AI 解读、数据备份恢复等规划功能

一次只做一个小而完整的变更；编译通过 ≠ 正常，要真打开看界面。
