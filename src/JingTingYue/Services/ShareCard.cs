using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace JingTingYue.Services;

/// <summary>
/// 把一条笔记渲染成竖版分享卡片 PNG，长内容自动增高。
/// 用隐藏 WebView2 渲染 HTML，再 CapturePreview 截图。
/// </summary>
public static class ShareCard
{
    public const int PreviewWidth = 320;
    public const int PreviewHeight = 426;
    private const int ExportScale = 3;
    /// <summary>把渲染好的 WebView2 内容捕获为 PNG，返回保存路径。</summary>
    public static async Task<string> CaptureToPngAsync(WebView2 web)
    {
        var outputDir = DataPaths.IsPortable
            ? Path.Combine(DataPaths.Root, "分享卡片")
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        Directory.CreateDirectory(outputDir);
        var outPath = Path.Combine(
            outputDir,
            $"静听阅_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        // 3:4 竖版预览；导出时按 3 倍尺寸重新渲染同一张卡片。
        var previewHeight = web.Height;
        web.Width = PreviewWidth * ExportScale;
        web.Height = previewHeight * ExportScale;
        try
        {
            await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.zoom = '3'");
            await Task.Delay(250);
            using var stream = new InMemoryRandomAccessStream();
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            stream.Seek(0);
            using var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            await stream.AsStreamForRead().CopyToAsync(outStream);
        }
        finally
        {
            web.Width = PreviewWidth;
            web.Height = previewHeight;
            await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.zoom = '1'");
        }
        return outPath;
    }

    public static async Task LoadCardAsync(WebView2 web, string quote, string thought, string bookTitle, string author)
    {
        await web.EnsureCoreWebView2Async();
        web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "jingtingyue.local", Path.Combine(AppContext.BaseDirectory, "Assets"),
            CoreWebView2HostResourceAccessKind.Allow);
        var loaded = new TaskCompletionSource<bool>();
        void OnCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args) => loaded.TrySetResult(args.IsSuccess);
        web.CoreWebView2.NavigationCompleted += OnCompleted;
        try
        {
            web.NavigateToString(BuildHtml(quote, thought, bookTitle, author));
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("分享卡片加载失败");
            var height = await web.CoreWebView2.ExecuteScriptAsync("document.documentElement.scrollHeight");
            if (double.TryParse(height?.Trim('"'), out var measured) && measured > PreviewHeight)
                web.Height = Math.Ceiling(measured);
        }
        finally { web.CoreWebView2.NavigationCompleted -= OnCompleted; }
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&#39;").Replace("\r\n", "\n").Replace("\n", "<br/>");

    private static string BuildHtml(string quote, string thought, string book, string author)
    {
        var excerpt = Esc((quote ?? "").Trim());
        var note = Esc((thought ?? "").Trim());
        var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "share-logo.png");
        var logo = File.Exists(logoPath) ? $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(logoPath))}" : "";
        return $@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
  @font-face {{ font-family:'Noto Serif SC Local'; src:url('https://jingtingyue.local/Fonts/NotoSerifSC/NotoSerifSC-VF.ttf') format('truetype'); font-weight:200 900; }}
  @font-face {{ font-family:'LXGW WenKai Local'; src:url('https://jingtingyue.local/Fonts/LXGWWenKai/LXGWWenKai-Regular.ttf') format('truetype'); font-weight:400; }}
  * {{ margin:0; box-sizing:border-box; }}
  html, body {{ width:100%; min-height:426px; }}
  body {{ background:#E8E4D9; padding:10px; color:#292B27; font-family:'Microsoft YaHei UI',sans-serif; }}
  .card {{ min-height:406px; padding:16px 19px 15px; border:1px solid #DED9CA;
    background:radial-gradient(circle at 100% 0%,#FBFAF4 0%,#F7F5EC 55%,#F3F0E6 100%);
    display:flex; flex-direction:column; overflow-wrap:anywhere; }}
  .mast {{ display:flex; align-items:center; gap:10px; }}
  .logo {{ width:34px; height:34px; border-radius:10px; flex:none; }}
  .brand {{ font-size:16px; font-weight:600; letter-spacing:2px; color:#2D594B; line-height:1.1; }}
  .tagline {{ margin-top:3px; color:#8C877D; font-size:9px; letter-spacing:1px; }}
  .issue {{ margin-left:auto; color:#B7A998; font:italic 12px Georgia,serif; }}
  .rule {{ height:1px; background:linear-gradient(90deg,#B8AB96,#E4DFD2); margin:11px 0 12px; flex:none; }}
  .label {{ color:#A8664F; font-size:10px; letter-spacing:2px; font-weight:600; }}
  .quote {{ margin-top:8px; color:#282B27; font:17px/1.48 'Noto Serif SC Local','KaiTi',serif;
    display:-webkit-box; -webkit-box-orient:vertical; -webkit-line-clamp:5; overflow:hidden; }}
  .quote:before {{ content:'“'; font:32px Georgia,serif; color:#B36D51; vertical-align:-6px; margin-right:4px; }}
  .note {{ margin-top:12px; padding:11px 13px; background:rgba(45,89,75,.07);
           border-left:3px solid #456E5F; min-height:80px; }}
  .thought {{ margin-top:7px; color:#405048; font:14px/1.48 'LXGW WenKai Local','Microsoft YaHei UI',sans-serif;
    display:-webkit-box; -webkit-box-orient:vertical; -webkit-line-clamp:3; overflow:hidden; }}
  .empty {{ color:#9B9A8F; }}
  .foot {{ margin-top:auto; padding-top:13px; border-top:1px solid #DDD8C9; }}
  .book {{ font-size:12px; line-height:1.5; color:#323A33; font-weight:600; overflow-wrap:anywhere; }}
  .author {{ font-size:11px; line-height:1.5; color:#8B877E; margin-top:4px; overflow-wrap:anywhere; }}
</style></head><body><div class='card'>
  <div class='mast'><img class='logo' src='{logo}'/><div><div class='brand'>静听阅</div><div class='tagline'>让阅读，慢慢发生</div></div><div class='issue'>READING NOTE</div></div>
  <div class='rule'></div>
  <div class='label'>原文摘录</div>
  <div class='quote'>{excerpt}</div>
  <div class='note'><div class='label'>我的笔记</div><div class='thought{(string.IsNullOrWhiteSpace(note) ? " empty" : "")}'>{(string.IsNullOrWhiteSpace(note) ? "这段摘录尚未添加笔记" : note)}</div></div>
  <div class='foot'><div class='book'>《{Esc(book)}》</div><div class='author'>{Esc(author)}</div></div>
</div></body></html>";
    }
}
