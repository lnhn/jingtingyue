# 静听阅 · 中文 EPUB 阅读器

安静、漂亮、适合长期使用的 Windows 原生阅读器。参考 Apple Books 的体验，以中文阅读为主，书籍、笔记和阅读记录保存在本机；听书走 Windows 本地语音，AI 解读用你自己的 DeepSeek API Key。

## 技术栈（已核实可用）

| 项 | 选择 | 说明 |
|---|---|---|
| 语言/运行时 | C# / .NET 8 (LTS, SDK 8.0.425) | 本机原无 SDK，已安装 |
| UI | WinUI 3 (Windows App SDK 1.6) | 免安装（自包含运行时），Fluent 外观 |
| EPUB 渲染 | WebView2 | 本机已装运行时 154.0.4258.53，HTML/CSS 还原 EPUB |
| 本地听书 | Windows 语音（System.Speech / OneCore） | 已确认本机 3 个中文音色：Microsoft Huihui、Kangkang、Yaoyao |
| AI | DeepSeek API（用户自填 Key） | Key 存 Windows 凭据管理器，不入备份 |
| 环境 | Windows 11 Pro (build 26300)，Core Ultra 9 275HX，64GB，RTX 5080 | |

## 目录结构

```
JingTingYue/
  JingTingYue.sln
  src/JingTingYue/
    JingTingYue.csproj
    App.xaml / App.xaml.cs          # 应用入口与全局配色
    MainWindow.xaml(.cs)            # 主窗口：顶栏 + 阅读区 + 翻页
    Assets/
      reader/reader.html            # WebView2 阅读引擎（分页/翻页/定位/高亮）
      samples/chapter1.html         # 示例章节（公版《菜根谭》选段）
```

## 构建与运行

```powershell
# 构建
dotnet build src\JingTingYue\JingTingYue.csproj -c Debug

# 运行（免安装，直接运行 exe）
& "src\JingTingYue\bin\Debug\net8.0-windows10.0.19041.0\win-x64\JingTingYue.exe"
```

## 开发阶段（按序推进，每阶段可运行可验证）

1. **阅读页原型**（本阶段，已完成）
   - WebView2 渲染示例章节，CSS 多栏分页引擎
   - 翻页（左右按钮 / ← → 键 / 触控板横向手势）、页码指示
   - 字号调节（改字号后按文字位置重排）、夜间模式
   - 配色：暖白书页、深森林绿主色、陶土点缀、楷体正文
   - 已实测：启动渲染、翻页、字号+重排、夜间切换

2. EPUB 导入、书架与阅读进度（规划）
3. 本地听书与播放控制（规划）
4. 笔记、原文标记与分享卡片（规划）
5. DeepSeek 解读与历史（规划）
6. 备份恢复、打包与整体体验检查（规划）

## 分层设计（面向后续阶段）

- 界面层：XAML + MainWindow / 后续 Views
- 阅读数据层：书籍/章节/进度存储（本地），EPUB 解析
- 语音层：Windows TTS，与界面解耦
- AI 层：DeepSeek 调用 + 凭据管理（独立，不入备份）
